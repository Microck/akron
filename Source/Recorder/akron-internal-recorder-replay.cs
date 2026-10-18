using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronInternalRecorder {
    private const int ReplaySegmentSeconds = 2;
    private const int ReplaySegmentTimestampSlackSeconds = ReplaySegmentSeconds + 3;
    private static readonly List<PendingClipSave> PendingClipSaves = new List<PendingClipSave>();

    private static Process replayProcess;
    private static Stream replayInput;
    private static RecorderSpec replaySpec;
    private static AkronCaptureCadence replayCadence;
    private static AkronReplayTimeline replayTimeline;
    private static long replayCapturedFrames;
    private static long replayDroppedFrames;
    private static bool replayWarningShown;
    private static string replayDirectory = string.Empty;
    private static bool manualReplayBufferEnabled;
    private static DateTime lastReplayPruneUtc = DateTime.MinValue;
    private static DateTime roomEntryUtc = DateTime.MinValue;
    private static DateTime respawnUtc = DateTime.MinValue;
    private static string roomEntryName = string.Empty;

    public static bool IsReplayBuffering {
        get {
            lock (Sync) {
                return replayProcess != null;
            }
        }
    }

    public static string DescribeReplayBufferStatus() {
        int seconds = AkronModuleSettings.ClampRecordingReplayBufferSeconds(AkronModule.Settings.RecordingReplayBufferSeconds);
        if (IsReplayBuffering) {
            return "Buffering " + seconds.ToString(CultureInfo.InvariantCulture) + "s";
        }

        if (AkronModule.Settings.RecordingReplayAutoStart != AkronRecordingReplayAutoStart.Off) {
            return "Armed " + AkronModuleSettings.FormatRecordingReplayAutoStart(AkronModule.Settings.RecordingReplayAutoStart);
        }

        return seconds <= 0 ? "Off" : seconds.ToString(CultureInfo.InvariantCulture) + "s ready";
    }

    public static void StartReplayBuffer(Scene scene) {
        if (scene == null) {
            Engine.Scene?.Add(new AkronToast("Open Celeste before starting the replay buffer."));
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.InternalRecorder)) {
            return;
        }

        AkronModuleSettings settings = AkronModule.Settings;
        EnsureReplayBufferDurationConfigured(settings);
        manualReplayBufferEnabled = true;
        EnsureReplayBuffer(scene, settings, RecorderSpec.FromSettings(settings));
        if (IsReplayBuffering) {
            scene.Add(new AkronToast("Replay buffer: " + AkronModule.Settings.RecordingReplayBufferSeconds.ToString(CultureInfo.InvariantCulture) + "s"));
        } else {
            manualReplayBufferEnabled = false;
        }
    }

    public static void StopReplayBuffer() {
        lock (Sync) { PendingClipSaves.Clear(); }
        manualReplayBufferEnabled = false;
        DisarmReplayBufferAutoStart(AkronModule.Settings);
        bool wasBuffering = IsReplayBuffering;
        StopReplayBuffer(false);
        Engine.Scene?.Add(new AkronToast(wasBuffering ? "Replay buffer stopped." : "Replay buffer is not running."));
    }

    public static void DisarmReplayBufferAutoStart() {
        lock (Sync) { PendingClipSaves.Clear(); }
        manualReplayBufferEnabled = false;
        DisarmReplayBufferAutoStart(AkronModule.Settings);
    }

    public static void DisarmReplayBufferAutoStartForTesting(AkronModuleSettings settings) {
        DisarmReplayBufferAutoStart(settings);
    }

    public static void SaveReplayBuffer(Scene scene) {
        if (scene == null) {
            Engine.Scene?.Add(new AkronToast("Open Celeste before saving the replay buffer."));
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.InternalRecorder)) {
            return;
        }

        int seconds = AkronModuleSettings.ClampRecordingReplayBufferSeconds(AkronModule.Settings.RecordingReplayBufferSeconds);
        if (seconds <= 0) {
            Engine.Scene?.Add(new AkronToast("Replay buffer is disabled."));
            return;
        }

        DateTime end = ReplayNowUtc();
        SaveReplayWindow(scene, end.AddSeconds(-seconds), end, "replay-buffer");
    }

    public static void ArmCompletionCapture(Scene scene) {
        if (!AkronModule.TryUse(AkronFeatureKind.InternalRecorder)) {
            return;
        }

        AkronModuleSettings settings = AkronModule.Settings;
        if (AkronModuleSettings.ClampRecordingReplayBufferSeconds(settings.RecordingReplayBufferSeconds) < AkronModuleSettings.CompletionRecordingReplayBufferSeconds) {
            settings.RecordingReplayBufferSeconds = AkronModuleSettings.CompletionRecordingReplayBufferSeconds;
        }

        settings.RecordingReplayAutoStart = AkronRecordingReplayAutoStart.InLevels;
        settings.RecordingTriggerRoomEntryToClear = true;
        settings.RecordingTriggerCheckpointClear = true;

        if (scene != null) {
            EnsureReplayBuffer(scene, settings, RecorderSpec.FromSettings(settings));
            if (!IsReplayBuffering) return;
        }

        Engine.Scene?.Add(new AkronToast("Completion capture armed."));
    }

    private static void DisarmReplayBufferAutoStart(AkronModuleSettings settings) {
        settings.RecordingReplayAutoStart = AkronRecordingReplayAutoStart.Off;
        settings.RecordingTriggerRoomEntryToClear = false;
        settings.RecordingTriggerCheckpointClear = false;
    }

    public static void FlagCompletion(Scene scene) {
        if (scene == null) {
            Engine.Scene?.Add(new AkronToast("Open Celeste before flagging completion."));
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.InternalRecorder)) {
            return;
        }

        int seconds = AkronModuleSettings.ClampRecordingReplayBufferSeconds(AkronModule.Settings.RecordingReplayBufferSeconds);
        if (seconds <= 0 || !IsReplayBuffering) {
            Engine.Scene?.Add(new AkronToast("Start or arm the replay buffer before flagging completion."));
            return;
        }

        DateTime end = ReplayNowUtc();
        SaveReplayWindow(scene, end.AddSeconds(-seconds), end, "completion-flag");
    }

    public static void NotifyLevelBegin(Level level) {
        DateTime now = ReplayNowUtc();
        lock (Sync) {
            roomEntryUtc = now;
            respawnUtc = now;
            roomEntryName = level?.Session?.Level ?? string.Empty;
            // A new level can begin while the prior level's post-roll is still due.
            // Keep those frozen identities only while their encoder remains alive.
            if (replayProcess == null) PendingClipSaves.Clear();
        }
    }

    public static void NotifyPlayerRespawn(Level level) {
        DateTime now = ReplayNowUtc();
        lock (Sync) {
            respawnUtc = now;
            if (level?.Session != null) {
                roomEntryUtc = now;
                roomEntryName = level.Session.Level;
            }
        }
    }

    public static void NotifyRoomLeaving(Level level) {
        if (level == null || !AkronModule.Settings.RecordingTriggerRoomEntryToClear) {
            return;
        }

        DateTime start;
        string room;
        lock (Sync) {
            start = roomEntryUtc;
            room = roomEntryName;
        }

        if (start != DateTime.MinValue && string.Equals(room, level.Session.Level, StringComparison.Ordinal)) {
            QueueClipSave(level, "room-clear", start, ReplayNowUtc(), AkronModule.Settings.RecordingPostRollSeconds);
        }
    }

    public static void NotifyRoomEntered(Level level) {
        DateTime now = ReplayNowUtc();
        lock (Sync) {
            roomEntryUtc = now;
            roomEntryName = level?.Session?.Level ?? string.Empty;
        }
    }

    public static void NotifyAreaComplete(Level level) {
        if (level == null) {
            return;
        }

        // Both completion windows own the same event boundary and must become due
        // together before their shared encoder is detached for export.
        DateTime completedUtc = ReplayNowUtc();
        if (AkronModule.Settings.RecordingTriggerCheckpointClear) {
            QueueClipSave(level, "checkpoint-clear", completedUtc.AddSeconds(-AkronModule.Settings.RecordingPreRollSeconds), completedUtc, AkronModule.Settings.RecordingPostRollSeconds);
        }

        if (AkronModule.Settings.RecordingTriggerRoomEntryToClear) {
            DateTime start;
            lock (Sync) {
                start = roomEntryUtc;
            }

            if (start != DateTime.MinValue) {
                QueueClipSave(level, "area-clear", start, completedUtc, AkronModule.Settings.RecordingPostRollSeconds);
            }
        }

        float endscreen = AkronModuleSettings.ClampRecordingEndscreenDurationSeconds(AkronModule.Settings.RecordingEndscreenDurationSeconds);
        lock (Sync) {
            if (ffmpegProcess != null && captureClock != null) {
                autoStopAtSeconds = captureClock.Elapsed.TotalSeconds + endscreen;
            }
        }
    }

    public static void NotifyDeath(Level level, bool goldenDeath) {
        if (level == null) {
            return;
        }

        DateTime now = ReplayNowUtc();
        if (AkronModule.Settings.RecordingTriggerLastDeath) {
            QueueClipSave(level, "last-death", now.AddSeconds(-AkronModule.Settings.RecordingPreRollSeconds), now, AkronModule.Settings.RecordingPostRollSeconds);
        }

        if (AkronModule.Settings.RecordingTriggerRespawnToDeath) {
            DateTime start;
            lock (Sync) {
                start = respawnUtc;
            }

            if (start != DateTime.MinValue) {
                QueueClipSave(level, "respawn-to-death", start, now, AkronModule.Settings.RecordingPostRollSeconds);
            }
        }

        if (goldenDeath && AkronModule.Settings.RecordingTriggerGoldenDeath) {
            QueueClipSave(level, "golden-death", now.AddSeconds(-AkronModule.Settings.RecordingPreRollSeconds), now, AkronModule.Settings.RecordingPostRollSeconds);
        }
    }

    public static void NotifyBerryCollect(Level level, bool golden) {
        if (level == null) {
            return;
        }

        if (AkronModule.Settings.RecordingTriggerBerryCollect ||
            golden && AkronModule.Settings.RecordingTriggerGoldenDeath) {
            QueueClipSave(level, golden ? "golden-berry" : "berry-collect", ReplayNowUtc().AddSeconds(-AkronModule.Settings.RecordingPreRollSeconds), ReplayNowUtc(), AkronModule.Settings.RecordingPostRollSeconds);
        }
    }

    public static IEnumerable<string> SelectReplaySegmentsForTesting(string directory, DateTime startUtc, DateTime endUtc) {
        return SelectReplaySegments(directory, startUtc, endUtc);
    }

    private static void QueueClipSave(Level level, string kind, DateTime startUtc, DateTime eventUtc, int postRollSeconds) {
        if (!AkronPolicy.CanUse(AkronFeatureKind.InternalRecorder).Allowed) {
            return;
        }

        if (AkronModuleSettings.ClampRecordingReplayBufferSeconds(AkronModule.Settings.RecordingReplayBufferSeconds) <= 0) {
            return;
        }

        int postRoll = AkronModuleSettings.ClampRecordingClipSeconds(postRollSeconds);
        PendingClipSave save = new PendingClipSave(kind, startUtc, eventUtc.AddSeconds(postRoll), eventUtc.AddSeconds(postRoll), CaptureRecordingIdentity(level));
        lock (Sync) {
            PendingClipSaves.Add(save);
        }
    }

    private static void SaveReplayWindow(Scene scene, DateTime startUtc, DateTime endUtc, string kind) {
        SaveReplayWindows(scene, new[] { new PendingClipSave(kind, startUtc, endUtc, endUtc, CaptureRecordingIdentity(scene)) });
    }

    private static IEnumerable<string> SelectReplaySegments(string directory, DateTime startUtc, DateTime endUtc) {
        DateTime earliestRelevantWrite = startUtc.AddSeconds(-ReplaySegmentSeconds - ReplaySegmentTimestampSlackSeconds);
        DateTime latestRelevantWrite = endUtc.AddSeconds(ReplaySegmentTimestampSlackSeconds);
        return Directory.EnumerateFiles(directory, "segment-*.mkv")
            .Select(path => new FileInfo(path))
            .Where(info => info.Exists &&
                           info.Length > 0 &&
                           info.LastWriteTimeUtc >= earliestRelevantWrite &&
                           info.LastWriteTimeUtc <= latestRelevantWrite)
            .OrderBy(info => info.LastWriteTimeUtc)
            .Select(info => info.FullName);
    }

    public static bool ShouldMaintainReplayBufferForTesting(AkronModuleSettings settings, bool sceneIsLevel, bool manualReplayBuffer) {
        return ShouldMaintainReplayBuffer(settings, sceneIsLevel, manualReplayBuffer);
    }

    private static bool ShouldMaintainReplayBuffer(AkronModuleSettings settings, Scene scene) {
        return ShouldMaintainReplayBuffer(settings, scene is Level, manualReplayBufferEnabled);
    }

    private static bool ShouldKeepPendingReplayBuffer(AkronModuleSettings settings, Scene scene) {
        lock (Sync) {
            return ShouldKeepPendingReplayBuffer(settings, scene is Level,
                replayProcess != null && replayInput != null, PendingClipSaves.Count);
        }
    }

    internal static bool ShouldKeepPendingReplayBuffer(AkronModuleSettings settings,
        bool sceneIsLevel, bool activeBuffer, int pendingCount) {
        // In Levels must not discard the event's history between CompleteArea and
        // its scheduled post-roll. Continue the existing encoder, never start one
        // outside a level just because a stale request exists. Explicit Off wins.
        return !sceneIsLevel && activeBuffer && pendingCount > 0 &&
            AkronModuleSettings.ClampRecordingReplayBufferSeconds(settings.RecordingReplayBufferSeconds) > 0 &&
            settings.RecordingReplayAutoStart == AkronRecordingReplayAutoStart.InLevels;
    }

    private static bool ShouldMaintainReplayBuffer(AkronModuleSettings settings, bool sceneIsLevel, bool manualReplayBuffer) {
        if (AkronModuleSettings.ClampRecordingReplayBufferSeconds(settings.RecordingReplayBufferSeconds) <= 0) {
            return false;
        }

        if (manualReplayBuffer) {
            return true;
        }

        return settings.RecordingReplayAutoStart switch {
            AkronRecordingReplayAutoStart.InLevels => sceneIsLevel,
            AkronRecordingReplayAutoStart.Always => true,
            _ => false
        };
    }

    private static void EnsureReplayBufferDurationConfigured(AkronModuleSettings settings) {
        if (AkronModuleSettings.ClampRecordingReplayBufferSeconds(settings.RecordingReplayBufferSeconds) <= 0) {
            settings.RecordingReplayBufferSeconds = AkronModuleSettings.DefaultRecordingReplayBufferSeconds;
        }
    }

    private static void EnsureReplayBuffer(Scene scene, AkronModuleSettings settings, RecorderSpec spec) {
        if (!AkronPolicy.CanUse(AkronFeatureKind.InternalRecorder).Allowed) {
            return;
        }

        lock (Sync) {
            if (replayProcess != null && replaySpec.Equals(spec)) {
                return;
            }
        }

        string replacementCleanupError = string.Empty;
        try {
            StopReplayBuffer(false, out replacementCleanupError);
            string folder = ResolveOutputFolder(settings);
            PruneOldReplayDirectories(folder);
            string directory = Path.Combine(folder, ".replay-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            ProcessStartInfo startInfo = new ProcessStartInfo {
                FileName = ResolveFfmpegPath(),
                Arguments = BuildReplaySegmentArguments(settings, spec, Path.Combine(directory, "segment-%06d.mkv")),
                WorkingDirectory = ResolveFfmpegWorkingDirectory(directory),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            ConfigureFfmpegStartInfo(startInfo);

            Process process = Process.Start(startInfo);
            if (process == null) {
                throw new IOException("FFmpeg replay buffer did not start.");
            }

            var diagnostics = new AkronEncoderDiagnostics();
            process.ErrorDataReceived += (_, e) => diagnostics.Receive(e.Data);
            process.BeginErrorReadLine();

            lock (Sync) {
                replayProcess = process;
                replayDiagnostics = diagnostics;
                replayInput = process.StandardInput.BaseStream;
                replayDirectory = directory;
                replaySpec = spec;
                if (ffmpegProcess == null) lastCaptureWasReplay = true;
                EnsureCaptureClock();
                replayCadence = new AkronCaptureCadence(spec.Framerate, captureClock.Elapsed.TotalSeconds);
                replayCapturedFrames = 0;
                replayTimeline = new AkronReplayTimeline(spec.Framerate, Stopwatch.Frequency);
                replayDroppedFrames = 0;
                replayWarningShown = false;
                lastError = string.Empty;
            }

            bool audioStarted = AkronInternalAudioRecorder.TryStartReplayBuffer(settings, out string audioWarning);
            if (!string.IsNullOrWhiteSpace(audioWarning) &&
                (audioStarted || settings.RecordingAudioFullMixTrack || settings.RecordingAudioMusicTrack || settings.RecordingAudioSfxTrack || settings.RecordingAudioAmbienceTrack)) {
                scene.Add(new AkronToast("Replay audio unavailable: " + audioWarning));
            }
        } catch (Exception ex) {
            // Both explicit and automatic starts must stay disarmed after a failure.
            // Otherwise CaptureFrame retries the same invalid directory every render.
            manualReplayBufferEnabled = false;
            DisarmReplayBufferAutoStart(settings);
            string cleanupFailure = string.Empty;
            string cleanupException = string.Empty;
            try {
                StopReplayBuffer(false, out cleanupFailure);
            } catch (Exception cleanupError) {
                cleanupException = cleanupError.Message;
            }
            string failure = CombineReplayStartupErrors(ex.Message, replacementCleanupError,
                cleanupFailure, cleanupException);
            lastError = failure;
            scene?.Add(new AkronToast(AkronEncoderCompletion.FailureToast("Replay buffer: " + failure)));
        }
    }

    internal static string CombineReplayStartupErrors(string startupError, params string[] cleanupErrors) {
        string result = startupError ?? string.Empty;
        foreach (string error in cleanupErrors) {
            if (!string.IsNullOrWhiteSpace(error)) result += " | Replay cleanup: " + error;
        }
        return result;
    }

    private static AkronRecordedAudio StopReplayBuffer(bool keepAudio = false) {
        return StopReplayBuffer(keepAudio, out _);
    }

    private static AkronRecordedAudio StopReplayBuffer(bool keepAudio, out string cleanupError) {
        cleanupError = string.Empty;
        Process process;
        Stream input;
        lock (Sync) {
            process = replayProcess;
            if (process != null && ffmpegProcess == null) lastCaptureWasReplay = true;
            input = replayInput;
            replayProcess = null;
            replayInput = null;
            replayCadence = null;
            ResetCaptureClockIfIdle();
        }

        if (process == null) {
            AkronRecordedAudio idleAudio = AkronInternalAudioRecorder.StopReplayBuffer();
            if (!keepAudio) {
                DeleteRecordedAudioFiles(idleAudio);
            }

            return idleAudio;
        }

        try {
            input?.Flush();
            input?.Dispose();
            if (!process.WaitForExit(3000)) {
                process.Kill();
            }
        } catch (Exception ex) {
            cleanupError = ex.Message;
            lastError = cleanupError;
        } finally {
            process.Dispose();
        }

        AkronRecordedAudio audio = AkronInternalAudioRecorder.StopReplayBuffer();
        if (!keepAudio) {
            DeleteRecordedAudioFiles(audio);
        }

        return audio;
    }

    private static void PruneReplaySegments(AkronModuleSettings settings) {
        if (string.IsNullOrWhiteSpace(replayDirectory) ||
            !Directory.Exists(replayDirectory) ||
            DateTime.UtcNow - lastReplayPruneUtc < TimeSpan.FromSeconds(1)) {
            return;
        }

        lastReplayPruneUtc = DateTime.UtcNow;
        int bufferSeconds = AkronModuleSettings.ClampRecordingReplayBufferSeconds(settings.RecordingReplayBufferSeconds);
        DateTime keepAfter = DateTime.UtcNow.AddSeconds(-bufferSeconds - ReplaySegmentSeconds * 2);
        lock (Sync) {
            foreach (PendingClipSave pending in PendingClipSaves) {
                if (pending.StartUtc < keepAfter) {
                    keepAfter = pending.StartUtc.AddSeconds(-ReplaySegmentSeconds * 2);
                }
            }
        }

        foreach (FileInfo file in SelectReplaySegmentsToPrune(
            Directory.EnumerateFiles(replayDirectory, "segment-*.mkv").Select(path => new FileInfo(path)), keepAfter)) {
            TryDeleteFile(file.FullName);
        }
    }

    internal static FileInfo[] SelectReplaySegmentsToPrune(IEnumerable<FileInfo> files, DateTime keepAfter) {
        var segments = files.Select(file => {
            string stem = Path.GetFileNameWithoutExtension(file.Name);
            long index = stem.StartsWith("segment-", StringComparison.Ordinal) &&
                long.TryParse(stem.Substring("segment-".Length), NumberStyles.None, CultureInfo.InvariantCulture, out long parsed)
                ? parsed : -1;
            return (File: file, Index: index);
        }).Where(segment => segment.Index >= 0).ToArray();
        if (segments.Length == 0) return Array.Empty<FileInfo>();

        // The segment muxer writes increasing sequence numbers. Its newest file may
        // still be open with buffered data and an old mtime: never unlink that file.
        long activeIndex = segments.Max(segment => segment.Index);
        return segments.Where(segment => segment.Index < activeIndex && segment.File.LastWriteTimeUtc < keepAfter)
            .Select(segment => segment.File).ToArray();
    }

    private static void PruneOldReplayDirectories(string folder) {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(folder, ".replay-*")) {
            if (string.Equals(directory, replayDirectory, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(directory, replayExportDirectory, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            try {
                DirectoryInfo info = new DirectoryInfo(directory);
                if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromMinutes(30)) {
                    info.Delete(true);
                }
            } catch {
                // Replay temp cleanup is opportunistic; locked files are retried later.
            }
        }
    }

    private readonly struct PendingClipSave {
        public PendingClipSave(string kind, DateTime startUtc, DateTime endUtc, DateTime saveAtUtc, RecordingIdentity identity) {
            Identity = identity;
            Kind = kind;
            StartUtc = startUtc;
            EndUtc = endUtc;
            SaveAtUtc = saveAtUtc;
        }

        public RecordingIdentity Identity { get; }
        public string Kind { get; }
        public DateTime StartUtc { get; }
        public DateTime EndUtc { get; }
        public DateTime SaveAtUtc { get; }
    }
}
