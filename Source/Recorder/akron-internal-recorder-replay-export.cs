using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronInternalRecorder
{
    private static int replayExportBusy;
    private static int replayExportGeneration;
    private static string replayExportDirectory;
    private static Process replayExportProcess;
    private static AkronEncoderDiagnostics replayDiagnostics;

    private sealed record ReplayExportRequest(PendingClipSave Window, string Path, string Chapter, string Room);
    private sealed record ReplayExportSnapshot(Process Process, Stream Input, AkronEncoderDiagnostics Diagnostics,
        AkronReplayTimeline Timeline, RecorderSpec Spec, AkronRecordedAudio Audio, string Directory,
        string Ffmpeg, long BoundaryTick, AkronModuleSettings Settings, int Generation);

    public static void CancelReplayExport()
    {
        Interlocked.Increment(ref replayExportGeneration);
        lock (Sync)
        {
            try { if (replayExportProcess != null && !replayExportProcess.HasExited) replayExportProcess.Kill(); }
            catch { /* The worker owns disposal and reports no success after cancellation. */ }
        }
    }

    private static DateTime ReplayNowUtc()
    {
        lock (Sync)
            return replayTimeline?.Started == true ? replayTimeline.ProjectUtc(Stopwatch.GetTimestamp()) : DateTime.UtcNow;
    }

    private static void SaveReplayWindows(Scene scene, IReadOnlyList<PendingClipSave> requests)
    {
        if (!AkronPolicy.CanUse(AkronFeatureKind.InternalRecorder).Allowed || requests.Count == 0) return;
        if (Interlocked.CompareExchange(ref replayExportBusy, 1, 0) != 0)
        {
            scene?.Add(new AkronToast("Replay clip export is already running."));
            return;
        }
        ReplayExportSnapshot snapshot = null;
        try
        {
            AkronModuleSettings settings = SnapshotReplayExportSettings(AkronModule.Settings);
            var outputRequests = requests.Select(request => new ReplayExportRequest(request,
                BuildOutputPath(scene, settings.RecordingContainerFormat, request.Kind),
                SanitizeToken((scene as Level)?.Session?.Area.GetSID() ?? string.Empty),
                SanitizeToken((scene as Level)?.Session?.Level ?? string.Empty))).ToList();
            long boundary = Stopwatch.GetTimestamp();
            Process process;
            Stream input;
            AkronReplayTimeline timeline;
            RecorderSpec spec;
            string directory;
            AkronEncoderDiagnostics diagnostics;
            lock (Sync)
            {
                if (replayProcess == null || replayInput == null || replayTimeline?.Started != true)
                    throw new InvalidOperationException("Replay buffer has no active timed history yet.");
                process = replayProcess;
                input = replayInput;
                timeline = replayTimeline;
                spec = replaySpec;
                directory = replayDirectory;
                diagnostics = replayDiagnostics;
                replayProcess = null;
                replayInput = null;
                replayCadence = null;
                if (ffmpegProcess == null) lastCaptureWasReplay = true;
                ResetCaptureClockIfIdle();
                replayExportDirectory = directory;
                replayExportProcess = process;
            }
            // Audio belongs to this generation and is detached before encoder drain.
            snapshot = new ReplayExportSnapshot(process, input, diagnostics, timeline, spec,
                new AkronRecordedAudio(new List<AkronRecordedAudioTrack>()), directory,
                process.StartInfo.FileName, boundary, settings, Volatile.Read(ref replayExportGeneration));
            snapshot = snapshot with { Audio = AkronInternalAudioRecorder.StopReplayBuffer() };
            ReplayExportSnapshot owned = snapshot;
            _ = Task.Run(() => ExportReplayBatch(owned, outputRequests));
            scene?.Add(new AkronToast("Saving replay clip..."));
        }
        catch (Exception ex)
        {
            if (snapshot != null) {
                try { snapshot.Input.Dispose(); } catch { }
                try { if (!snapshot.Process.HasExited) snapshot.Process.Kill(); } catch { }
                snapshot.Process.Dispose();
                DeleteRecordedAudioFiles(snapshot.Audio);
            }
            lock (Sync) { replayExportDirectory = null; replayExportProcess = null; }
            Volatile.Write(ref replayExportBusy, 0);
            lastError = ex.Message;
            scene?.Add(new AkronToast(AkronEncoderCompletion.FailureToast(ex.Message)));
        }
    }

    private static AkronModuleSettings SnapshotReplayExportSettings(AkronModuleSettings source) => new()
    {
        RecordingContainerFormat = source.RecordingContainerFormat,
        RecordingAutoRemux = source.RecordingAutoRemux,
        RecordingQualityPreset = source.RecordingQualityPreset,
        RecordingRateControl = source.RecordingRateControl,
        RecordingBitrateMbps = source.RecordingBitrateMbps
    };

    private static void ExportReplayBatch(ReplayExportSnapshot snapshot, IReadOnlyList<ReplayExportRequest> requests)
    {
        bool disposed = false;
        try
        {
            CheckReplayExportGeneration(snapshot);
            int finalizationDeadlineExpired = 0;
            using var finalizationDeadline = new Timer(_ => {
                Interlocked.Exchange(ref finalizationDeadlineExpired, 1);
                try { if (!snapshot.Process.HasExited) snapshot.Process.Kill(); } catch { }
            }, null, 10000, Timeout.Infinite);
            string fillError = null;
            try { snapshot.Timeline.CompleteThrough(snapshot.Input, snapshot.BoundaryTick); }
            catch (Exception ex) { fillError = ex.Message; }
            string finishError = AkronEncoderCompletion.Finish(snapshot.Input, snapshot.Process.WaitForExit,
                () => snapshot.Process.ExitCode, snapshot.Process.Kill, snapshot.Process.Dispose,
                () => snapshot.Diagnostics?.ReadAfterExit() ?? string.Empty,
                () => File.Exists(Path.Combine(snapshot.Directory, "segments.csv")), fillError);
            finalizationDeadline.Change(Timeout.Infinite, Timeout.Infinite);
            if (Volatile.Read(ref finalizationDeadlineExpired) != 0)
                finishError = "Replay finalization exceeded its deadline. " + finishError;
            disposed = true;
            lock (Sync) if (ReferenceEquals(replayExportProcess, snapshot.Process)) replayExportProcess = null;
            if (!string.IsNullOrWhiteSpace(finishError)) throw new IOException(finishError);
            var segments = AkronReplayWindows.ReadSegments(snapshot.Directory);
            foreach (ReplayExportRequest request in requests)
            {
                CheckReplayExportGeneration(snapshot);
                try { ExportReplayClip(snapshot, request, segments); }
                catch (Exception ex) { PublishReplayExport(snapshot, null, ex.Message); }
            }
        }
        catch (Exception ex) { PublishReplayExport(snapshot, null, ex.Message); }
        finally
        {
            if (!disposed)
            {
                try { snapshot.Input?.Dispose(); } catch { }
                try { if (!snapshot.Process.HasExited) snapshot.Process.Kill(); } catch { }
                snapshot.Process.Dispose();
            }
            DeleteRecordedAudioFiles(snapshot.Audio);
            lock (Sync) { replayExportDirectory = null; replayExportProcess = null; }
            Volatile.Write(ref replayExportBusy, 0);
        }
    }

    private static void ExportReplayClip(ReplayExportSnapshot snapshot, ReplayExportRequest request, IReadOnlyList<AkronReplaySegment> segments)
    {
        AkronReplayWindow window = AkronReplayWindows.Plan(segments, snapshot.Timeline.OriginUtc,
            snapshot.Spec.Framerate, snapshot.Timeline.FramesWritten, request.Window.StartUtc, request.Window.EndUtc);
        string folder = Path.GetDirectoryName(request.Path) ?? snapshot.Directory;
        Directory.CreateDirectory(folder);
        var ownedMedia = new List<string>();
        string output = AkronEncoderCompletion.ReserveOutputPath(Path.Combine(snapshot.Directory,
            "export-" + Guid.NewGuid().ToString("N") + Path.GetExtension(request.Path)));
        ownedMedia.Add(output);
        string publishedPath = null;
        bool publishedSidecar = false;
        bool published = false;
        string concat = Path.Combine(snapshot.Directory, "concat-" + Guid.NewGuid().ToString("N") + ".txt");
        AkronRecordedAudio sliced = new(new List<AkronRecordedAudioTrack>());
        try
        {
            var lines = new List<string>();
            foreach (var segment in window.Segments)
            {
                lines.Add("file '" + segment.Path.Replace("'", "'\\''") + "'");
                lines.Add("duration " + (segment.End - segment.Start).ToString("0.#########", CultureInfo.InvariantCulture));
            }
            File.WriteAllLines(concat, lines);
            var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-f", "concat", "-safe", "0", "-i", concat,
                "-vf", "trim=start_frame=" + window.InputFirstFrame.ToString(CultureInfo.InvariantCulture) +
                    ":end_frame=" + (window.InputFirstFrame + window.FrameCount).ToString(CultureInfo.InvariantCulture) + ",setpts=PTS-STARTPTS",
                "-an", "-c:v", AkronModuleSettings.GetRecordingCodecArgument(snapshot.Spec.Codec) };
            AddEncoderPerformanceArguments(args, snapshot.Settings, snapshot.Spec);
            AddRateControlArguments(args, snapshot.Settings);
            args.AddRange(new[] { "-r", snapshot.Spec.Framerate.ToString(CultureInfo.InvariantCulture),
                "-frames:v", window.FrameCount.ToString(CultureInfo.InvariantCulture), "-avoid_negative_ts", "disabled", "-progress", "pipe:1", "-nostats", "-y", output });
            long frames = RunReplayExportProcess(snapshot, JoinArguments(args), folder, window.Duration);
            if (frames != window.FrameCount || !AkronEncoderCompletion.HasNonemptyOutput(output))
                throw new IOException("Replay export did not produce the complete requested frame window.");
            if (snapshot.Settings.RecordingAutoRemux && string.Equals(Path.GetExtension(output), ".mkv", StringComparison.OrdinalIgnoreCase))
            {
                string remux = AkronEncoderCompletion.ReserveOutputPath(Path.ChangeExtension(output, ".mp4"));
                ownedMedia.Add(remux);
                RunReplayExportProcess(snapshot, JoinArguments(new[] { "-hide_banner", "-loglevel", "warning", "-i", output, "-c", "copy", "-avoid_negative_ts", "disabled", "-y", remux }), folder, window.Duration);
                if (!AkronEncoderCompletion.HasNonemptyOutput(remux)) throw new IOException("Replay remux produced no media.");
                output = remux;
            }
            sliced = AkronReplayAudioSlice.Slice(snapshot.Audio, window.StartUtc, window.EndUtc);
            if (sliced.HasSamples)
            {
                string mux = AkronEncoderCompletion.ReserveOutputPath(Path.Combine(snapshot.Directory,
                    Path.GetFileNameWithoutExtension(output) + "-audio" + Path.GetExtension(output)));
                ownedMedia.Add(mux);
                RunReplayExportProcess(snapshot, BuildAudioMuxArguments(output, sliced.Tracks.Where(track => track.HasSamples).ToList(), mux, window.Duration), folder, window.Duration);
                if (!AkronEncoderCompletion.HasNonemptyOutput(mux)) throw new IOException("Replay audio mux produced no media.");
                output = mux;
            }
            CheckReplayExportGeneration(snapshot);
            File.WriteAllLines(output + ClipSidecarExtension, new[] {
                "kind=" + request.Window.Kind, "format=" + Path.GetExtension(output).TrimStart('.').ToUpperInvariant(),
                "chapter=" + request.Chapter, "room=" + request.Room, "createdUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                "startUtc=" + window.StartUtc.ToString("O", CultureInfo.InvariantCulture), "endUtc=" + window.EndUtc.ToString("O", CultureInfo.InvariantCulture),
                "requestedStartUtc=" + request.Window.StartUtc.ToString("O", CultureInfo.InvariantCulture),
                "requestedEndUtc=" + request.Window.EndUtc.ToString("O", CultureInfo.InvariantCulture),
                "durationSeconds=" + window.Duration.ToString("0.#########", CultureInfo.InvariantCulture),
                "frameCount=" + window.FrameCount.ToString(CultureInfo.InvariantCulture) });
            string requestedFinalPath = Path.Combine(folder, Path.GetFileNameWithoutExtension(request.Path) +
                (sliced.HasSamples ? "-audio" : string.Empty) + Path.GetExtension(output));
            publishedPath = AkronEncoderCompletion.ReserveOutputPath(requestedFinalPath);
            File.Move(output, publishedPath, true);
            File.Move(output + ClipSidecarExtension, publishedPath + ClipSidecarExtension);
            publishedSidecar = true;
            CheckReplayExportGeneration(snapshot);
            published = true;
            bool partial = window.StartUtc > request.Window.StartUtc.AddSeconds(1d / snapshot.Spec.Framerate) ||
                window.EndUtc < request.Window.EndUtc.AddSeconds(-1d / snapshot.Spec.Framerate);
            PublishReplayExport(snapshot, publishedPath, null, partial);
        }
        finally {
            DeleteRecordedAudioFiles(sliced);
            TryDeleteFile(concat);
            foreach (string owned in ownedMedia) { TryDeleteFile(owned); TryDeleteFile(owned + ClipSidecarExtension); }
            if (!published && publishedPath != null) {
                TryDeleteFile(publishedPath);
                if (publishedSidecar) TryDeleteFile(publishedPath + ClipSidecarExtension);
            }
        }
    }

    private static long RunReplayExportProcess(ReplayExportSnapshot snapshot, string arguments, string folder, double duration)
    {
        CheckReplayExportGeneration(snapshot);
        var info = new ProcessStartInfo { FileName = snapshot.Ffmpeg, Arguments = arguments,
            WorkingDirectory = ResolveFfmpegWorkingDirectory(folder), UseShellExecute = false,
            RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        ConfigureFfmpegStartInfo(info);
        using var process = new Process { StartInfo = info };
        var diagnostics = new AkronEncoderDiagnostics();
        long frames = -1;
        var outputEnded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.ErrorDataReceived += (_, e) => diagnostics.Receive(e.Data);
        process.OutputDataReceived += (_, e) => {
            if (e.Data == null) { outputEnded.TrySetResult(true); return; }
            if (e.Data.StartsWith("frame=", StringComparison.Ordinal) && long.TryParse(e.Data.Substring(6), out long value))
                Interlocked.Exchange(ref frames, value);
        };
        if (!process.Start()) throw new IOException("FFmpeg replay export did not start.");
        lock (Sync) replayExportProcess = process;
        try
        {
            CheckReplayExportGeneration(snapshot);
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            int timeout = checked((int)Math.Clamp(30000d + duration * 2000d, 30000d, 1230000d));
            if (!process.WaitForExit(timeout))
            {
                process.Kill();
                process.WaitForExit(1000);
                throw new IOException("FFmpeg replay export timed out.");
            }
            outputEnded.Task.Wait(1000);
            string diagnostic = diagnostics.ReadAfterExit();
            CheckReplayExportGeneration(snapshot);
            if (process.ExitCode != 0) throw new IOException("FFmpeg replay export failed: " + diagnostic);
            return Interlocked.Read(ref frames);
        }
        finally {
            try { if (!process.HasExited) { process.Kill(); process.WaitForExit(1000); } } catch { }
            lock (Sync) if (ReferenceEquals(replayExportProcess, process)) replayExportProcess = null;
        }
    }

    private static void CheckReplayExportGeneration(ReplayExportSnapshot snapshot)
    {
        if (snapshot.Generation != Volatile.Read(ref replayExportGeneration)) throw new OperationCanceledException("Replay export canceled.");
    }

    private static void PublishReplayExport(ReplayExportSnapshot snapshot, string path, string error, bool partial = false)
    {
        if (snapshot.Generation != Volatile.Read(ref replayExportGeneration)) return;
        MainThreadHelper.Schedule(() => {
            if (snapshot.Generation != Volatile.Read(ref replayExportGeneration)) return;
            if (error != null) { lastError = error; Engine.Scene?.Add(new AkronToast(AkronEncoderCompletion.FailureToast(error))); }
            else { lastError = string.Empty; lastClipPath = path;
                Engine.Scene?.Add(new AkronToast((partial ? "Partial-history clip saved: " : "Clip saved: ") + Path.GetFileName(path))); }
        });
    }
}
