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
    private static long replayFinalizationWaitStarted;
    private static long lastFinalizedSegmentRead;
    private static List<AkronReplaySegment> finalizedSegmentCache = new();

    private sealed record ReplayExportRequest(PendingClipSave Window, string Path, string Chapter, string Room);
    private static readonly Dictionary<string, AkronReplaySegment> KnownReplaySegments = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PinnedReplaySegments = new(StringComparer.OrdinalIgnoreCase);
    private sealed record ReplayExportSnapshot(RecorderSpec Spec, AkronReplayAudioLease Audio,
        string Directory, string Ffmpeg, DateTime OriginUtc, long Frames,
        IReadOnlyList<AkronReplaySegment> Segments, AkronModuleSettings Settings, int Generation);

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

    internal static bool IsReplayWindowFinalized(DateTime requestedEnd, DateTime finalizedEnd) => requestedEnd <= finalizedEnd;

    private static List<AkronReplaySegment> ReadKnownReplaySegments()
    {
        long now = Stopwatch.GetTimestamp();
        if (lastFinalizedSegmentRead != 0 && (now - lastFinalizedSegmentRead) / (double)Stopwatch.Frequency < 0.25)
            return finalizedSegmentCache;
        lastFinalizedSegmentRead = now;
        foreach (var segment in AkronReplayWindows.ReadFinalizedSegments(replayDirectory))
            KnownReplaySegments[segment.Path] = segment;
        foreach (var path in KnownReplaySegments.Keys.Where(p => !File.Exists(p)).ToArray())
            KnownReplaySegments.Remove(path);
        finalizedSegmentCache = KnownReplaySegments.Values.OrderBy(s => s.Start).ToList();
        return finalizedSegmentCache;
    }

    internal static bool ReplayFinalizationExpired(long started, long now, long frequency) =>
        started > 0 && frequency > 0 && (now - started) / (double)frequency >= 60;

    private static IReadOnlyList<PendingClipSave> DeferReplayFinalization(Scene scene)
    {
        long now = Stopwatch.GetTimestamp();
        if (replayFinalizationWaitStarted == 0) replayFinalizationWaitStarted = now;
        if (ReplayFinalizationExpired(replayFinalizationWaitStarted, now, Stopwatch.Frequency))
            throw new IOException("Replay segments did not finalize within 60 seconds after a requested window became due.");
        return Array.Empty<PendingClipSave>();
    }

    private static IReadOnlyList<PendingClipSave> SaveReplayWindows(Scene scene, IReadOnlyList<PendingClipSave> requests)
    {
        if (!AkronPolicy.CanUse(AkronFeatureKind.InternalRecorder).Allowed || requests.Count == 0)
            return Array.Empty<PendingClipSave>();
        if (Interlocked.CompareExchange(ref replayExportBusy, 1, 0) != 0) return Array.Empty<PendingClipSave>();
        ReplayExportSnapshot snapshot = null;
        bool dispatched = false;
        List<PendingClipSave> ready = new();
        try
        {
            var settings = SnapshotReplayExportSettings(AkronModule.Settings);
            lock (Sync)
            {
                if (replayProcess == null || replayInput == null || replayTimeline?.Started != true)
                    return Array.Empty<PendingClipSave>();
                if (replayProcess.HasExited) throw new IOException("Replay encoder exited before pending windows finalized.");
                List<AkronReplaySegment> segments;
                try { segments = ReadKnownReplaySegments(); }
                catch (IOException) { return DeferReplayFinalization(scene); }
                catch (InvalidDataException) { return DeferReplayFinalization(scene); }
                if (segments.Count == 0) return DeferReplayFinalization(scene);
                DateTime AlignedEnd(PendingClipSave r) => AkronReplayWindows.RequestedEndOnFrameGrid(r.EndUtc, replayTimeline.OriginUtc, replaySpec.Framerate);
                DateTime finalizedEnd = replayTimeline.OriginUtc.AddSeconds(
                    AkronReplayWindows.SnapMediaFrame(segments[^1].End, replaySpec.Framerate) / (double)replaySpec.Framerate);
                ready = requests.Where(r => IsReplayWindowFinalized(AlignedEnd(r), finalizedEnd)).Take(8).ToList();
                if (ready.Count == 0) return DeferReplayFinalization(scene);
                DateTime first = ready.Min(r => r.StartUtc);
                DateTime last = ready.Max(AlignedEnd);
                var selected = segments.Where(s => replayTimeline.OriginUtc.AddSeconds(s.End) > first &&
                    replayTimeline.OriginUtc.AddSeconds(s.Start) < last).ToList();
                string ffmpeg = replayProcess.StartInfo.FileName;
                var audio = AkronInternalAudioRecorder.LeaseReplayBuffer(first, last);
                ready = ready.Where(r => audio.HasCapturedThrough(AlignedEnd(r))).ToList();
                if (ready.Count == 0) {
                    audio.Dispose();
                    return DeferReplayFinalization(scene);
                }
                replayFinalizationWaitStarted = 0;
                snapshot = new ReplayExportSnapshot(replaySpec, audio,
                    replayDirectory, ffmpeg, replayTimeline.OriginUtc,
                    replayTimeline.FramesWritten, selected, settings, Volatile.Read(ref replayExportGeneration));
                foreach (var segment in selected) PinnedReplaySegments.Add(segment.Path);
                replayExportDirectory = replayDirectory;
            }
            var outputRequests = ready.Select(request => new ReplayExportRequest(request,
                BuildOutputPath(request.Identity, settings.RecordingContainerFormat, request.Kind),
                SanitizeToken(request.Identity.Chapter), SanitizeToken(request.Identity.Room))).ToList();
            ReplayExportSnapshot owned = snapshot;
            _ = Task.Run(() => ExportReplayBatch(owned, outputRequests));
            dispatched = true;
            scene?.Add(new AkronToast("Saving replay clip..."));
            return ready;
        }
        catch (Exception ex)
        {
            string error = ex.Message;
            if (ready.Count == 0) {
                // This capture generation cannot fulfill accepted windows. Stop and
                // explicitly disarm it rather than pin history and retry forever.
                try { StopReplayBuffer(); } catch (Exception cleanup) { error += " | " + cleanup.Message; }
            }
            lastError = error;
            scene?.Add(new AkronToast(AkronEncoderCompletion.FailureToast(error)));
            return ready;
        }
        finally
        {
            if (!dispatched)
            {
                snapshot?.Audio.Dispose();
                lock (Sync)
                {
                    if (snapshot != null) foreach (var segment in snapshot.Segments) PinnedReplaySegments.Remove(segment.Path);
                    replayExportDirectory = null;
                }
                Volatile.Write(ref replayExportBusy, 0);
            }
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
        try
        {
            foreach (ReplayExportRequest request in requests)
            {
                CheckReplayExportGeneration(snapshot);
                try { ExportReplayClip(snapshot, request, snapshot.Segments); }
                catch (Exception ex) { PublishReplayExport(snapshot, null, ex.Message); }
            }
        }
        catch (Exception ex) { PublishReplayExport(snapshot, null, ex.Message); }
        finally
        {
            snapshot.Audio.Dispose();
            lock (Sync)
            {
                foreach (var segment in snapshot.Segments) PinnedReplaySegments.Remove(segment.Path);
                replayExportDirectory = null;
                replayExportProcess = null;
            }
            Volatile.Write(ref replayExportBusy, 0);
        }
    }

    private static void ExportReplayClip(ReplayExportSnapshot snapshot, ReplayExportRequest request, IReadOnlyList<AkronReplaySegment> segments)
    {
        AkronReplayWindow window = AkronReplayWindows.Plan(segments, snapshot.OriginUtc,
            snapshot.Spec.Framerate, snapshot.Frames, request.Window.StartUtc, request.Window.EndUtc);
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
            AddRateControlArguments(args, snapshot.Settings, snapshot.Spec);
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
            sliced = snapshot.Audio.CopyWindow(window.StartUtc, window.EndUtc);
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
            PublishReplayExport(snapshot, publishedPath, null, window.IsPartial);
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
