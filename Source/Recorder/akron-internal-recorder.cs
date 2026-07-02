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
    private const string DefaultOutputFolderName = "AkronRecordings";
    private static readonly object Sync = new object();

    private static Process ffmpegProcess;
    private static Stream ffmpegInput;
    private static Stopwatch captureClock;
    private static double nextFrameSeconds;
    private static string activePath = string.Empty;
    private static RecorderSpec activeSpec;
    private static int sourceWidth;
    private static int sourceHeight;
    private static Color[] captureSourceBuffer = Array.Empty<Color>();
    private static byte[] captureFrameBuffer = Array.Empty<byte>();
    private static long capturedFrames;
    private static long droppedFrames;
    private static string lastError = string.Empty;
    private static string lastClipPath = string.Empty;
    private static bool warningShown;
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
    public static long CapturedFrames => capturedFrames;
    public static long DroppedFrames => droppedFrames;
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

        return droppedFrames > 0
            ? droppedFrames.ToString(CultureInfo.InvariantCulture) + " dropped"
            : "OK";
    }

    public static string DescribeClipBrowser() {
        List<AkronRecordingClipInfo> clips = ListClips(AkronModule.Settings).Take(3).ToList();
        if (clips.Count == 0) {
            return "No clips";
        }

        return clips[0].DisplayName;
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

        AkronModuleSettings settings = AkronModule.Settings;
        RecorderSpec spec = RecorderSpec.FromSettings(settings);
        string outputPath = BuildOutputPath(level, settings.RecordingContainerFormat, "recording");
        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory)) {
            Directory.CreateDirectory(outputDirectory);
        }

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

        try {
            Process process = Process.Start(startInfo);
            if (process == null) {
                lastError = "FFmpeg did not start.";
                Engine.Scene?.Add(new AkronToast(lastError));
                return;
            }

            CaptureFfmpegStderr(process);

            lock (Sync) {
                ffmpegProcess = process;
                ffmpegInput = process.StandardInput.BaseStream;
                EnsureCaptureClock();
                activePath = outputPath;
                activeSpec = spec;
                sourceWidth = 0;
                sourceHeight = 0;
                capturedFrames = 0;
                droppedFrames = 0;
                lastError = string.Empty;
                warningShown = false;
                recordingStartUtc = DateTime.UtcNow;
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
            Engine.Scene?.Add(new AkronToast("Recorder failed: " + ex.Message));
        }
    }

    public static void Stop() {
        Process process;
        Stream input;
        string completedPath;
        DateTime startedUtc;
        DateTime endedUtc = DateTime.UtcNow;
        lock (Sync) {
            process = ffmpegProcess;
            input = ffmpegInput;
            completedPath = activePath;
            startedUtc = recordingStartUtc == DateTime.MinValue ? endedUtc : recordingStartUtc;
            ffmpegProcess = null;
            ffmpegInput = null;
            activePath = string.Empty;
            recordingStartUtc = DateTime.MinValue;
            autoStopAtSeconds = -1d;
            ResetCaptureClockIfIdle();
        }

        if (process == null) {
            Engine.Scene?.Add(new AkronToast("Internal recorder is not running."));
            return;
        }

        try {
            input?.Flush();
            input?.Dispose();
            if (!process.WaitForExit(3000)) {
                process.Kill();
            }
        } catch (Exception ex) {
            lastError = ex.Message;
        } finally {
            process.Dispose();
        }

        AkronRecordedAudio recordedAudio = AkronInternalAudioRecorder.Stop();

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

        WriteClipSidecar(completedPath, "manual-recording", AkronModule.Settings.RecordingContainerFormat, startedUtc, endedUtc, Engine.Scene);
        lastClipPath = completedPath;
        Engine.Scene?.Add(new AkronToast("Recording saved: " + Path.GetFileName(completedPath)));
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

    public static void Update(Level level) {
        if (level == null) {
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

        DateTime now = DateTime.UtcNow;
        List<PendingClipSave> due;
        lock (Sync) {
            due = PendingClipSaves.Where(save => save.SaveAtUtc <= now).ToList();
            PendingClipSaves.RemoveAll(save => save.SaveAtUtc <= now);
        }

        foreach (PendingClipSave save in due) {
            SaveReplayWindow(level, save.StartUtc, save.EndUtc, save.Kind);
        }
    }

    public static void CaptureFrame(Level level) {
        CaptureFrame(level, true);
    }

    public static void CaptureFrame(Scene scene) {
        CaptureFrame(scene, false);
    }

    private static void CaptureFrame(Scene scene, bool captureManualRecording) {
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
        Stopwatch activeClock;
        lock (Sync) {
            recordingProcess = captureManualRecording ? ffmpegProcess : null;
            recordingInput = captureManualRecording ? ffmpegInput : null;
            activeReplayProcess = replayProcess;
            activeReplayInput = replayInput;
            activeClock = captureClock;
        }

        if (recordingProcess == null && activeReplayProcess == null ||
            activeClock == null ||
            recordingInput == null && activeReplayInput == null) {
            return;
        }

        double now = activeClock.Elapsed.TotalSeconds;
        double frameInterval = 1d / Math.Max(1, spec.Framerate);
        if (now + frameInterval * 0.25d < nextFrameSeconds) {
            return;
        }

        if (now > nextFrameSeconds + frameInterval * 1.5d && capturedFrames > 0) {
            droppedFrames += Math.Max(1, (long) Math.Floor((now - nextFrameSeconds) / frameInterval));
            if (settings.RecordingDroppedFrameWarning && !warningShown) {
                warningShown = true;
                Engine.Scene?.Add(new AkronToast("Recorder is dropping frames."));
            }
        }

        nextFrameSeconds = now + frameInterval;

        try {
            GraphicsDevice graphicsDevice = Engine.Instance.GraphicsDevice;
            Viewport viewport = Engine.Viewport;
            if (sourceWidth != viewport.Width || sourceHeight != viewport.Height) {
                sourceWidth = viewport.Width;
                sourceHeight = viewport.Height;
            }

            Color[] source = EnsureCaptureSourceBuffer(sourceWidth * sourceHeight);
            graphicsDevice.GetBackBufferData(viewport.Bounds, source, 0, source.Length);
            byte[] frame = BuildFrameBytes(source, sourceWidth, sourceHeight, spec.Width, spec.Height);

            if (recordingProcess != null && recordingInput != null) {
                recordingInput.Write(frame, 0, frame.Length);
            }

            if (activeReplayProcess != null && activeReplayInput != null) {
                activeReplayInput.Write(frame, 0, frame.Length);
                PruneReplaySegments(settings);
            }

            capturedFrames++;
        } catch (Exception ex) {
            lastError = ex.Message;
            StopReplayBuffer();
            if (recordingProcess != null) {
                Stop();
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
            nextFrameSeconds = 0d;
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

        string localTool = Path.Combine(Everest.PathGame, "Saves", "AkronTools", "ffmpeg");
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

    private static string BuildOutputPath(Scene scene, AkronRecordingContainerFormat format, string kind) {
        string folder = ResolveOutputFolder(AkronModule.Settings);
        string filename = ExpandFilenameTemplate(scene);
        if (!string.IsNullOrWhiteSpace(kind)) {
            filename += "-" + SanitizeToken(kind);
        }

        return Path.Combine(folder, filename + AkronModuleSettings.GetRecordingContainerExtension(format));
    }

    private static string ResolveOutputFolder(AkronModuleSettings settings) {
        return string.IsNullOrWhiteSpace(settings.RecordingOutputFolder)
            ? Path.Combine(Everest.PathGame, "Saves", DefaultOutputFolderName)
            : settings.RecordingOutputFolder.Trim();
    }

    private static string ExpandFilenameTemplate(Scene scene) {
        Level level = scene as Level;
        Session session = level?.Session;
        string sceneName = SanitizeToken(scene?.GetType().Name ?? "scene");
        string chapter = SanitizeToken(session?.Area.GetSID() ?? sceneName);
        string room = SanitizeToken(session?.Level ?? sceneName);
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string death = (AkronModule.Session?.DeathsSinceLevelLoad ?? 0).ToString(CultureInfo.InvariantCulture);
        string attempt = (SaveData.Instance?.TotalDeaths ?? 0).ToString(CultureInfo.InvariantCulture);
        string template = AkronModuleSettings.NormalizeRecordingFilenameTemplate(AkronModule.Settings.RecordingFilenameTemplate);

        return SanitizeToken(template
            .Replace("{chapter}", chapter)
            .Replace("{room}", room)
            .Replace("{timestamp}", timestamp)
            .Replace("{death}", death)
            .Replace("{attempt}", attempt));
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
