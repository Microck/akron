using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronInternalRecorder {
    // Both of these live under Saves and a backup has to know about them. The ffmpeg child process holds its
    // output file and the replay buffer's segment files open for as long as it records, and a player-supplied
    // ffmpeg binary is a loaded executable image while it runs. On Windows neither can be deleted or renamed
    // by the game, so a restore leaves both folders alone (Source/Actions/akron-backup-actions.cs).
    internal const string DefaultOutputFolderName = "AkronRecordings";
    internal const string ToolsFolderName = "AkronTools";
    private static readonly object Sync = new object();

    private static Process ffmpegProcess;
    private static Stream ffmpegInput;
    private static AkronEncoderDiagnostics ffmpegDiagnostics;
    private static Stopwatch captureClock;
    private static AkronCaptureCadence manualCadence;
    private static string activePath = string.Empty;
    private static RecordingIdentity activeIdentity;
    private static RecorderSpec manualSpec;
    private static int sourceWidth;
    private static int sourceHeight;
    private static Color[] captureSourceBuffer = Array.Empty<Color>();
    private static byte[] captureFrameBuffer = Array.Empty<byte>();
    private static long capturedFrames;
    private static long droppedFrames;
    private static string lastError = string.Empty;
    private static string lastClipPath = string.Empty;
    private static bool warningShown;
    private static bool lastCaptureWasReplay;
    private static DateTime recordingStartUtc = DateTime.MinValue;
    private static double autoStopAtSeconds = -1d;

    public static bool IsRecording {
        get {
            lock (Sync) {
                return ffmpegProcess != null;
            }
        }
    }

    public static string LastError => lastError;
    public static string LastClipPath => lastClipPath;
    public static long CapturedFrames {
        get {
            lock (Sync) return ShouldUseReplayDiagnostics(ffmpegProcess != null, replayProcess != null, lastCaptureWasReplay)
                ? replayCapturedFrames : capturedFrames;
        }
    }
    public static long DroppedFrames {
        get {
            lock (Sync) return ShouldUseReplayDiagnostics(ffmpegProcess != null, replayProcess != null, lastCaptureWasReplay)
                ? replayDroppedFrames : droppedFrames;
        }
    }
    internal static bool ShouldUseReplayDiagnostics(bool manualActive, bool replayActive, bool lastWasReplay) =>
        !manualActive && (replayActive || lastWasReplay);
    public static string DescribeStatus() {
        if (IsRecording) {
            return "Recording " + capturedFrames.ToString(CultureInfo.InvariantCulture) + "f";
        }

        return string.IsNullOrWhiteSpace(lastClipPath)
            ? "Idle"
            : Path.GetFileName(lastClipPath);
    }

    public static string DescribeWarnings() {
        if (!string.IsNullOrWhiteSpace(lastError)) {
            return "Error";
        }

        long visibleDroppedFrames = DroppedFrames;
        return visibleDroppedFrames > 0
            ? visibleDroppedFrames.ToString(CultureInfo.InvariantCulture) + " dropped"
            : "OK";
    }

    public static void Start(Level level) {
        if (level == null) {
            Engine.Scene?.Add(new AkronToast("Open a level before recording."));
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.InternalRecorder)) {
            return;
        }

        lock (Sync) {
            if (ffmpegProcess != null) {
                Engine.Scene?.Add(new AkronToast("Internal recorder is already running."));
                return;
            }
        }

        try {
            AkronModuleSettings settings = AkronModule.Settings;
            RecorderSpec spec = RecorderSpec.FromSettings(settings);
            RecordingIdentity identity = CaptureRecordingIdentity(level);
            string outputPath = BuildOutputPath(identity, settings.RecordingContainerFormat, "recording");
            string outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory)) {
                Directory.CreateDirectory(outputDirectory);
            }

            outputPath = AkronEncoderCompletion.ReserveOutputPath(outputPath);

            ProcessStartInfo startInfo = new ProcessStartInfo {
                FileName = ResolveFfmpegPath(),
                Arguments = BuildFfmpegArguments(settings, spec, outputPath),
                WorkingDirectory = ResolveFfmpegWorkingDirectory(outputDirectory),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            ConfigureFfmpegStartInfo(startInfo);

            Process process = Process.Start(startInfo);
            if (process == null) {
                lastError = "FFmpeg did not start.";
                Engine.Scene?.Add(new AkronToast(lastError));
                return;
            }

            AkronEncoderDiagnostics diagnostics = new AkronEncoderDiagnostics();
            process.ErrorDataReceived += (_, e) => diagnostics.Receive(e.Data);
            process.BeginErrorReadLine();

            lock (Sync) {
                ffmpegProcess = process;
                ffmpegInput = process.StandardInput.BaseStream;
                ffmpegDiagnostics = diagnostics;
                EnsureCaptureClock();
                activePath = outputPath;
                manualSpec = spec;
                lastCaptureWasReplay = false;
                manualCadence = new AkronCaptureCadence(spec.Framerate, captureClock.Elapsed.TotalSeconds);
                sourceWidth = 0;
                sourceHeight = 0;
                capturedFrames = 0;
                droppedFrames = 0;
                lastError = string.Empty;
                warningShown = false;
                recordingStartUtc = DateTime.UtcNow;
                activeIdentity = identity;
                autoStopAtSeconds = -1d;
            }

            Engine.Scene?.Add(new AkronToast("Recording: " + Path.GetFileName(outputPath)));
            bool audioStarted = AkronInternalAudioRecorder.TryStart(settings, out string audioWarning);
            if (!string.IsNullOrWhiteSpace(audioWarning) &&
                (audioStarted || settings.RecordingAudioFullMixTrack || settings.RecordingAudioMusicTrack || settings.RecordingAudioSfxTrack || settings.RecordingAudioAmbienceTrack)) {
                Engine.Scene?.Add(new AkronToast("Audio capture unavailable: " + audioWarning));
            }
            WarnAboutUnsupportedCaptureSettings(settings);
        } catch (Exception ex) {
            lastError = ex.Message;
            Engine.Scene?.Add(new AkronToast(AkronEncoderCompletion.FailureToast(lastError)));
        }
    }

    // Frames are fed only while the scene is a Level, so a recording that outlives the level
    // is FFmpeg waiting on a pipe and a video that splices across the gap. Restarts get a new
    // level within a wipe and keep recording. A completion keeps recording only while the
    // endscreen auto-stop is armed, which NotifyAreaComplete does whenever a recording is live.
    public static void NotifyLevelExit(LevelExit.Mode mode) {
        if (mode is LevelExit.Mode.Restart or LevelExit.Mode.GoldenBerryRestart) {
            return;
        }

        lock (Sync) {
            if (ffmpegProcess == null || autoStopAtSeconds >= 0d) {
                return;
            }
        }

        Stop();
    }

    public static void Stop() => Stop(null);

    private static void Stop(string captureError) {
        Process process;
        AkronEncoderDiagnostics diagnostics;
        Stream input;
        string completedPath;
        RecordingIdentity completedIdentity;
        DateTime startedUtc;
        DateTime endedUtc = DateTime.UtcNow;
        lock (Sync) {
            process = ffmpegProcess;
            if (process != null) lastCaptureWasReplay = false;
            input = ffmpegInput;
            diagnostics = ffmpegDiagnostics;
            ffmpegDiagnostics = null;
            completedPath = activePath;
            completedIdentity = activeIdentity;
            activeIdentity = null;
            startedUtc = recordingStartUtc == DateTime.MinValue ? endedUtc : recordingStartUtc;
            ffmpegProcess = null;
            ffmpegInput = null;
            manualCadence = null;
            activePath = string.Empty;
            recordingStartUtc = DateTime.MinValue;
            autoStopAtSeconds = -1d;
            ResetCaptureClockIfIdle();
        }

        if (process == null) {
            Engine.Scene?.Add(new AkronToast("Internal recorder is not running."));
            return;
        }

        string completionError = AkronEncoderCompletion.Finish(input, process.WaitForExit,
            () => process.ExitCode, process.Kill, process.Dispose,
            () => diagnostics?.ReadAfterExit() ?? string.Empty,
            () => AkronEncoderCompletion.HasNonemptyOutput(completedPath), captureError);
        AkronRecordedAudio recordedAudio;
        try {
            recordedAudio = AkronInternalAudioRecorder.Stop();
        } catch (Exception ex) {
            lastError = string.IsNullOrWhiteSpace(completionError) ? ex.Message : completionError + " | " + ex.Message;
            Engine.Scene?.Add(new AkronToast(AkronEncoderCompletion.FailureToast(lastError)));
            return;
        }

        try {
            if (!string.IsNullOrWhiteSpace(completionError)) {
                lastError = completionError;
                Engine.Scene?.Add(new AkronToast(AkronEncoderCompletion.FailureToast(completionError)));
                return;
            }
            lastError = string.Empty;
            string remuxError = string.Empty;
            if (AkronModule.Settings.RecordingAutoRemux &&
                string.Equals(Path.GetExtension(completedPath), ".mkv", StringComparison.OrdinalIgnoreCase) &&
                TryRemuxToMp4(completedPath, out string remuxedPath, out remuxError)) {
                completedPath = remuxedPath;
            } else if (!string.IsNullOrWhiteSpace(remuxError)) {
                lastError = remuxError;
                Engine.Scene?.Add(new AkronToast("Auto-remux failed: " + remuxError));
            }

            string audioMuxError = string.Empty;
            if (recordedAudio.HasSamples &&
                TryMuxAudioIntoVideo(completedPath, recordedAudio, out string audioMuxedPath, out audioMuxError)) {
                completedPath = audioMuxedPath;
            } else if (!string.IsNullOrWhiteSpace(audioMuxError)) {
                lastError = audioMuxError;
                Engine.Scene?.Add(new AkronToast("Audio mux failed: " + audioMuxError));
            }

            WriteClipSidecar(completedPath, "manual-recording", AkronModule.Settings.RecordingContainerFormat, startedUtc, endedUtc, completedIdentity);
            lastClipPath = completedPath;
            Engine.Scene?.Add(new AkronToast("Recording saved: " + Path.GetFileName(completedPath)));
        } finally {
            DeleteRecordedAudioFiles(recordedAudio);
        }
    }

    public static void BuildCompletionVideo(Scene scene) {
        if (scene == null) {
            Engine.Scene?.Add(new AkronToast("Open Celeste before building a clear video."));
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.InternalRecorder)) {
            return;
        }

        AkronModuleSettings settings = AkronModule.Settings;
        List<AkronRecordingClipInfo> clips = SelectCompletionClips(settings, scene).ToList();
        if (clips.Count == 0) {
            Engine.Scene?.Add(new AkronToast("No clear or completion-flag clips found for this map."));
            return;
        }

        string outputPath = BuildOutputPath(scene, settings.RecordingContainerFormat, "clear-video");
        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory)) {
            Directory.CreateDirectory(outputDirectory);
        }

        string concatList = Path.Combine(outputDirectory ?? ResolveOutputFolder(settings), "completion-concat-" + Guid.NewGuid().ToString("N") + ".txt");
        try {
            File.WriteAllLines(concatList, clips.Select(clip => "file '" + clip.Path.Replace("'", "'\\''") + "'"));
            ProcessStartInfo startInfo = new ProcessStartInfo {
                FileName = ResolveFfmpegPath(),
                Arguments = BuildCompletionConcatArguments(concatList, outputPath),
                WorkingDirectory = ResolveFfmpegWorkingDirectory(outputDirectory),
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            ConfigureFfmpegStartInfo(startInfo);

            using Process process = Process.Start(startInfo);
            if (process == null) {
                lastError = "FFmpeg did not start.";
                Engine.Scene?.Add(new AkronToast(lastError));
                return;
            }

            string stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30000) || process.ExitCode != 0) {
                lastError = string.IsNullOrWhiteSpace(stderr) ? "FFmpeg clear-video build failed." : stderr.Trim();
                Engine.Scene?.Add(new AkronToast("Clear video failed: " + lastError));
                return;
            }

            WriteClipSidecar(outputPath, "clear-video", settings.RecordingContainerFormat, clips[0].StartUtc, clips[clips.Count - 1].EndUtc, scene);
            lastClipPath = outputPath;
            Engine.Scene?.Add(new AkronToast("Clear video saved: " + Path.GetFileName(outputPath)));
        } catch (Exception ex) {
            lastError = ex.Message;
            Engine.Scene?.Add(new AkronToast("Clear video failed: " + ex.Message));
        } finally {
            TryDeleteFile(concatList);
        }
    }

    public static void ApplyMapRestrictions() {
        if (AkronPolicy.CanUse(AkronFeatureKind.InternalRecorder).Allowed) {
            return;
        }

        lock (Sync) {
            PendingClipSaves.Clear();
            manualReplayBufferEnabled = false;
        }
        if (IsRecording) {
            Stop();
        }
        if (IsReplayBuffering) {
            StopReplayBuffer(false);
        }
    }

    public static void Update(Scene scene) {
        if (!AkronPolicy.CanUse(AkronFeatureKind.InternalRecorder).Allowed) {
            ApplyMapRestrictions();
            return;
        }

        if (scene == null) {
            return;
        }

        Process processToStop = null;
        lock (Sync) {
            if (ffmpegProcess != null &&
                captureClock != null &&
                autoStopAtSeconds >= 0d &&
                captureClock.Elapsed.TotalSeconds >= autoStopAtSeconds) {
                processToStop = ffmpegProcess;
            }
        }

        if (processToStop != null) {
            Stop();
        }

        if (System.Threading.Volatile.Read(ref replayExportBusy) != 0) return;
        DateTime now = ReplayNowUtc();
        List<PendingClipSave> due;
        lock (Sync) {
            due = PendingClipSaves.Where(save => save.SaveAtUtc <= now).ToList();
            PendingClipSaves.RemoveAll(save => save.SaveAtUtc <= now);
        }

        if (due.Count > 0) SaveReplayWindows(scene, due);
    }

    public static void CaptureFrame(Level level) {
        CaptureFrame(level, true);
    }

    public static void CaptureFrame(Scene scene) {
        CaptureFrame(scene, false);
    }

    private static void CaptureFrame(Scene scene, bool captureManualRecording) {
        if (!AkronPolicy.CanUse(AkronFeatureKind.InternalRecorder).Allowed) {
            ApplyMapRestrictions();
            return;
        }

        if (scene == null) {
            return;
        }

        AkronModuleSettings settings = AkronModule.Settings;
        bool wantsReplayBuffer = ShouldMaintainReplayBuffer(settings, scene);
        RecorderSpec spec = RecorderSpec.FromSettings(settings);
        if (wantsReplayBuffer) {
            EnsureReplayBuffer(scene, settings, spec);
        } else if (IsReplayBuffering) {
            StopReplayBuffer(false);
        }

        Process recordingProcess;
        Stream recordingInput;
        Process activeReplayProcess;
        Stream activeReplayInput;
        RecorderSpec recordingSpec;
        RecorderSpec replayFrameSpec;
        AkronReplayTimeline activeReplayTimeline;
        bool manualDue;
        bool replayDue;
        long missedManualFrames = 0;
        long missedReplayFrames = 0;
        lock (Sync) {
            recordingProcess = captureManualRecording ? ffmpegProcess : null;
            recordingInput = captureManualRecording ? ffmpegInput : null;
            recordingSpec = manualSpec;
            activeReplayProcess = replayProcess;
            activeReplayInput = replayInput;
            replayFrameSpec = replaySpec;
            activeReplayTimeline = replayTimeline;
            if (captureClock == null) return;
            double now = captureClock.Elapsed.TotalSeconds;
            manualDue = recordingProcess != null && recordingInput != null && manualCadence != null &&
                manualCadence.TrySchedule(now, capturedFrames > 0, out missedManualFrames);
            replayDue = activeReplayProcess != null && activeReplayInput != null && replayCadence != null &&
                replayCadence.TrySchedule(now, replayCapturedFrames > 0, out missedReplayFrames);
        }

        if (!manualDue && !replayDue) return;
        if (missedManualFrames > 0) {
            droppedFrames += missedManualFrames;
            if (settings.RecordingDroppedFrameWarning && !warningShown) {
                warningShown = true;
                Engine.Scene?.Add(new AkronToast("Recorder is dropping frames."));
            }
        }

        if (missedReplayFrames > 0) {
            replayDroppedFrames += missedReplayFrames;
            if (settings.RecordingDroppedFrameWarning && !replayWarningShown) {
                replayWarningShown = true;
                Engine.Scene?.Add(new AkronToast("Replay buffer is dropping frames."));
            }
        }

        try {
            GraphicsDevice graphicsDevice = Engine.Instance.GraphicsDevice;
            Viewport viewport = Engine.Viewport;
            if (sourceWidth != viewport.Width || sourceHeight != viewport.Height) {
                sourceWidth = viewport.Width;
                sourceHeight = viewport.Height;
            }

            Color[] source = EnsureCaptureSourceBuffer(sourceWidth * sourceHeight);
            graphicsDevice.GetBackBufferData(viewport.Bounds, source, 0, source.Length);
            long replaySampleTick = Stopwatch.GetTimestamp();
            DateTime replaySampleUtc = DateTime.UtcNow;
            // Each encoder keeps the byte geometry and cadence used in its launch arguments.
            // BuildFrameBytes reuses a buffer: finish each write before rebuilding for another sink.
            if (manualDue) {
                byte[] frame = BuildFrameBytes(source, sourceWidth, sourceHeight, recordingSpec.Width, recordingSpec.Height);
                recordingInput.Write(frame, 0, frame.Length);
            }

            if (replayDue) {
                try {
                    byte[] replayFrame = BuildFrameBytes(source, sourceWidth, sourceHeight, replayFrameSpec.Width, replayFrameSpec.Height);
                    replayCapturedFrames += activeReplayTimeline.WriteSample(activeReplayInput, replayFrame, replaySampleTick, replaySampleUtc);
                    PruneReplaySegments(settings);
                } catch (Exception replayError) {
                    // A replay-only failure must not reject or stop a healthy manual encode.
                    // The public stop also disarms auto-start, preventing a restart loop.
                    string replayMessage = replayError.Message;
                    try {
                        StopReplayBuffer();
                    } catch (Exception cleanupError) {
                        replayMessage += " | " + cleanupError.Message;
                    }
                    lastError = replayMessage;
                }
            }

            if (manualDue) capturedFrames++;
        } catch (Exception ex) {
            string captureError = ex.Message;
            StopReplayBuffer();
            if (recordingProcess != null) {
                Stop(captureError);
            } else {
                lastError = captureError;
            }
        }
    }

    public static void ApplyPreset(AkronRecordingPreset preset) {
        ApplyPresetToSettings(AkronModule.Settings, preset);
    }

    public static void ApplyPresetForTesting(AkronModuleSettings settings, AkronRecordingPreset preset) {
        ApplyPresetToSettings(settings, preset);
    }

    private static void ApplyPresetToSettings(AkronModuleSettings settings, AkronRecordingPreset preset) {
        settings.RecordingPreset = preset;
        switch (preset) {
            case AkronRecordingPreset.Nvidia:
                settings.RecordingCodec = AkronRecordingCodec.H264Nvenc;
                settings.RecordingQualityPreset = AkronRecordingQualityPreset.HighQuality;
                break;
            case AkronRecordingPreset.Amd:
                settings.RecordingCodec = AkronRecordingCodec.H264Amf;
                settings.RecordingQualityPreset = AkronRecordingQualityPreset.HighQuality;
                break;
            default:
                settings.RecordingCodec = AkronRecordingCodec.Libx264;
                settings.RecordingQualityPreset = AkronRecordingQualityPreset.Balanced;
                break;
        }
    }

    private static void CaptureFfmpegStderr(Process process) {
        process.ErrorDataReceived += (_, e) => {
            if (!string.IsNullOrWhiteSpace(e.Data)) {
                lastError = e.Data;
            }
        };
        process.BeginErrorReadLine();
    }

    private static void EnsureCaptureClock() {
        captureClock ??= Stopwatch.StartNew();
    }

    private static void ResetCaptureClockIfIdle() {
        if (ffmpegProcess == null && replayProcess == null) {
            captureClock = null;
            manualCadence = null;
            replayCadence = null;
            sourceWidth = 0;
            sourceHeight = 0;
            captureSourceBuffer = Array.Empty<Color>();
            captureFrameBuffer = Array.Empty<byte>();
        }
    }

    private static string ResolveFfmpegPath() {
        string configured = Environment.GetEnvironmentVariable("AKRON_FFMPEG");
        if (!string.IsNullOrWhiteSpace(configured)) {
            return configured;
        }

        string localTool = Path.Combine(Everest.PathGame, "Saves", ToolsFolderName, "ffmpeg");
        if (File.Exists(localTool)) {
            return localTool;
        }

        if (Path.DirectorySeparatorChar == '/') {
            string steamRuntimeHostTool = "/run/host/usr/bin/ffmpeg";
            if (File.Exists(steamRuntimeHostTool)) {
                return steamRuntimeHostTool;
            }

            return "/usr/bin/ffmpeg";
        }

        return "ffmpeg";
    }

    private static void ConfigureFfmpegStartInfo(ProcessStartInfo startInfo) {
        if (!IsSteamRuntimeHostTool(startInfo.FileName)) {
            return;
        }

        // Steam Linux Runtime exposes the host binary under /run/host, but the
        // process still inherits the container library path. Prepend the host
        // library directories so host FFmpeg can resolve its own dependencies.
        string[] hostLibraryDirectories = {
            "/run/host/usr/lib/x86_64-linux-gnu",
            "/run/host/usr/lib/x86_64-linux-gnu/pulseaudio",
            "/run/host/lib/x86_64-linux-gnu",
            "/run/host/usr/lib",
            "/run/host/lib"
        };

        string hostPath = string.Join(
            Path.PathSeparator.ToString(),
            hostLibraryDirectories.Where(Directory.Exists));
        if (string.IsNullOrWhiteSpace(hostPath)) {
            return;
        }

        string inherited = startInfo.EnvironmentVariables["LD_LIBRARY_PATH"];
        startInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = string.IsNullOrWhiteSpace(inherited)
            ? hostPath
            : hostPath + Path.PathSeparator + inherited;
    }

    private static bool IsSteamRuntimeHostTool(string path) {
        return !string.IsNullOrWhiteSpace(path) &&
            path.StartsWith("/run/host/", StringComparison.Ordinal);
    }

    private static string ResolveFfmpegWorkingDirectory(string preferredDirectory = null) {
        if (!string.IsNullOrWhiteSpace(preferredDirectory) && Directory.Exists(preferredDirectory)) {
            return preferredDirectory;
        }

        string temp = Path.GetTempPath();
        if (!string.IsNullOrWhiteSpace(temp) && Directory.Exists(temp)) {
            return temp;
        }

        return Environment.CurrentDirectory;
    }

    private static string BuildOutputPath(Scene scene, AkronRecordingContainerFormat format, string kind) =>
        BuildOutputPath(CaptureRecordingIdentity(scene), format, kind);

    private static string BuildOutputPath(RecordingIdentity identity, AkronRecordingContainerFormat format, string kind) {
        string folder = ResolveOutputFolder(AkronModule.Settings);
        string filename = ExpandFilenameTemplate(identity);
        if (!string.IsNullOrWhiteSpace(kind)) {
            filename += "-" + SanitizeToken(kind);
        }

        return Path.Combine(folder, filename + AkronModuleSettings.GetRecordingContainerExtension(format));
    }

    private static string ResolveOutputFolder(AkronModuleSettings settings) {
        // Custom folders do not depend on Everest's game-path accessor (also absent in stripped test refs).
        string gameFolder = string.IsNullOrWhiteSpace(settings.RecordingOutputFolder) ? Everest.PathGame : null;
        return ResolveAbsoluteOutputFolder(settings.RecordingOutputFolder, gameFolder, Environment.CurrentDirectory);
    }

    internal static string ResolveAbsoluteOutputFolder(string configuredFolder, string gameFolder, string currentDirectory) {
        string folder = string.IsNullOrWhiteSpace(configuredFolder)
            ? Path.Combine(gameFolder, "Saves", DefaultOutputFolderName)
            : configuredFolder.Trim();
        // FFmpeg runs with the output folder as its working directory. Resolve once in
        // the game process so its output argument cannot repeat a relative folder prefix.
        return Path.GetFullPath(folder, currentDirectory);
    }

    // Copy scalar identity on the main thread before a delayed export or scene transition.
    // Never retain a mutable Session/Level as the source of publication metadata.
    internal sealed record RecordingIdentity(string Chapter, string Room, int Deaths, int Attempts);

    private static RecordingIdentity CaptureRecordingIdentity(Scene scene) {
        Session session = (scene as Level)?.Session;
        string fallback = scene?.GetType().Name ?? "scene";
        return new RecordingIdentity(session?.Area.GetSID() ?? fallback, session?.Level ?? fallback,
            AkronModule.Session?.DeathsSinceLevelLoad ?? 0, SaveData.Instance?.TotalDeaths ?? 0);
    }

    private static string ExpandFilenameTemplate(RecordingIdentity identity) =>
        ExpandFilenameIdentity(AkronModule.Settings.RecordingFilenameTemplate, identity, DateTime.UtcNow);

    internal static string ExpandFilenameIdentity(string template, RecordingIdentity identity, DateTime timestampUtc) {
        identity ??= new RecordingIdentity("recording", "recording", 0, 0);
        return SanitizeToken(AkronModuleSettings.NormalizeRecordingFilenameTemplate(template)
            .Replace("{chapter}", SanitizeToken(identity.Chapter))
            .Replace("{room}", SanitizeToken(identity.Room))
            .Replace("{timestamp}", timestampUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture))
            .Replace("{death}", identity.Deaths.ToString(CultureInfo.InvariantCulture))
            .Replace("{attempt}", identity.Attempts.ToString(CultureInfo.InvariantCulture)));
    }

    private static string SanitizeToken(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "recording";
        }

        foreach (char invalid in Path.GetInvalidFileNameChars()) {
            value = value.Replace(invalid, '-');
        }

        return value.Replace('/', '-').Replace('\\', '-').Trim();
    }

    private static byte[] BuildFrameBytes(Color[] source, int width, int height, int targetWidth, int targetHeight) {
        byte[] bytes = EnsureCaptureFrameBuffer(targetWidth * targetHeight * 4);
        if (width == targetWidth && height == targetHeight) {
            int offset = 0;
            int pixels = width * height;
            for (int i = 0; i < pixels; i++) {
                Color color = source[i];
                bytes[offset++] = color.R;
                bytes[offset++] = color.G;
                bytes[offset++] = color.B;
                bytes[offset++] = color.A;
            }

            return bytes;
        }

        for (int y = 0; y < targetHeight; y++) {
            int sourceY = y * height / targetHeight;
            for (int x = 0; x < targetWidth; x++) {
                int sourceX = x * width / targetWidth;
                Color color = source[sourceY * width + sourceX];
                int offset = (y * targetWidth + x) * 4;
                bytes[offset] = color.R;
                bytes[offset + 1] = color.G;
                bytes[offset + 2] = color.B;
                bytes[offset + 3] = color.A;
            }
        }

        return bytes;
    }

    private static Color[] EnsureCaptureSourceBuffer(int length) {
        if (captureSourceBuffer.Length != length) {
            captureSourceBuffer = new Color[length];
        }

        return captureSourceBuffer;
    }

    private static byte[] EnsureCaptureFrameBuffer(int length) {
        if (captureFrameBuffer.Length != length) {
            captureFrameBuffer = new byte[length];
        }

        return captureFrameBuffer;
    }

    private static void TryDeleteFile(string path) {
        try {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) {
                File.Delete(path);
            }
        } catch {
            // Best-effort cleanup: recording must not fail because a temp segment is locked.
        }
    }

    private static string JoinArguments(IEnumerable<string> args) {
        List<string> escaped = new List<string>();
        foreach (string arg in args) {
            escaped.Add("\"" + arg.Replace("\"", "\\\"") + "\"");
        }

        return string.Join(" ", escaped);
    }

    private readonly struct RecorderSpec : IEquatable<RecorderSpec> {
        public RecorderSpec(int width, int height, int framerate, AkronRecordingCodec codec) {
            Width = AkronModuleSettings.ClampRecordingResolutionX(width);
            Height = AkronModuleSettings.ClampRecordingResolutionY(height);
            Framerate = AkronModuleSettings.ClampRecordingFramerate(framerate);
            Codec = codec;
        }

        public int Width { get; }
        public int Height { get; }
        public int Framerate { get; }
        public AkronRecordingCodec Codec { get; }

        public static RecorderSpec FromSettings(AkronModuleSettings settings) {
            return new RecorderSpec(settings.RecordingResolutionX, settings.RecordingResolutionY, settings.RecordingFramerate, settings.RecordingCodec);
        }

        public bool Equals(RecorderSpec other) {
            return Width == other.Width &&
                   Height == other.Height &&
                   Framerate == other.Framerate &&
                   Codec == other.Codec;
        }

        public override bool Equals(object obj) {
            return obj is RecorderSpec other && Equals(other);
        }

        public override int GetHashCode() {
            unchecked {
                int hash = Width;
                hash = hash * 397 ^ Height;
                hash = hash * 397 ^ Framerate;
                hash = hash * 397 ^ (int) Codec;
                return hash;
            }
        }
    }

}

public readonly struct AkronRecordingClipInfo {
    public AkronRecordingClipInfo(string path, string displayName, string chapter, string room, string kind, DateTime createdUtc, DateTime startUtc, DateTime endUtc, bool favorite) {
        Path = path;
        DisplayName = displayName;
        Chapter = chapter;
        Room = room;
        Kind = kind;
        CreatedUtc = createdUtc;
        StartUtc = startUtc;
        EndUtc = endUtc;
        Favorite = favorite;
    }

    public string Path { get; }
    public string DisplayName { get; }
    public string Chapter { get; }
    public string Room { get; }
    public string Kind { get; }
    public DateTime CreatedUtc { get; }
    public DateTime StartUtc { get; }
    public DateTime EndUtc { get; }
    public bool Favorite { get; }
}
