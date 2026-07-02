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
        manualReplayBufferEnabled = false;
        DisarmReplayBufferAutoStart(AkronModule.Settings);
        bool wasBuffering = IsReplayBuffering;
        StopReplayBuffer(false);
        Engine.Scene?.Add(new AkronToast(wasBuffering ? "Replay buffer stopped." : "Replay buffer is not running."));
    }

    public static void DisarmReplayBufferAutoStart() {
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

        SaveReplayWindow(scene, DateTime.UtcNow.AddSeconds(-seconds), DateTime.UtcNow, "replay-buffer");
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

        SaveReplayWindow(scene, DateTime.UtcNow.AddSeconds(-seconds), DateTime.UtcNow, "completion-flag");
    }

    public static void NotifyLevelBegin(Level level) {
        DateTime now = DateTime.UtcNow;
        lock (Sync) {
            roomEntryUtc = now;
            respawnUtc = now;
            roomEntryName = level?.Session?.Level ?? string.Empty;
            PendingClipSaves.Clear();
        }
    }

    public static void NotifyPlayerRespawn(Level level) {
        DateTime now = DateTime.UtcNow;
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
            QueueClipSave("room-clear", start, DateTime.UtcNow, AkronModule.Settings.RecordingPostRollSeconds);
        }
    }

    public static void NotifyRoomEntered(Level level) {
        DateTime now = DateTime.UtcNow;
        lock (Sync) {
            roomEntryUtc = now;
            roomEntryName = level?.Session?.Level ?? string.Empty;
        }
    }

    public static void NotifyAreaComplete(Level level) {
        if (level == null) {
            return;
        }

        if (AkronModule.Settings.RecordingTriggerCheckpointClear) {
            QueueClipSave("checkpoint-clear", DateTime.UtcNow.AddSeconds(-AkronModule.Settings.RecordingPreRollSeconds), DateTime.UtcNow, AkronModule.Settings.RecordingPostRollSeconds);
        }

        if (AkronModule.Settings.RecordingTriggerRoomEntryToClear) {
            DateTime start;
            lock (Sync) {
                start = roomEntryUtc;
            }

            if (start != DateTime.MinValue) {
                QueueClipSave("area-clear", start, DateTime.UtcNow, AkronModule.Settings.RecordingPostRollSeconds);
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

        DateTime now = DateTime.UtcNow;
        if (AkronModule.Settings.RecordingTriggerLastDeath) {
            QueueClipSave("last-death", now.AddSeconds(-AkronModule.Settings.RecordingPreRollSeconds), now, AkronModule.Settings.RecordingPostRollSeconds);
        }

        if (AkronModule.Settings.RecordingTriggerRespawnToDeath) {
            DateTime start;
            lock (Sync) {
                start = respawnUtc;
            }

            if (start != DateTime.MinValue) {
                QueueClipSave("respawn-to-death", start, now, AkronModule.Settings.RecordingPostRollSeconds);
            }
        }

        if (goldenDeath && AkronModule.Settings.RecordingTriggerGoldenDeath) {
            QueueClipSave("golden-death", now.AddSeconds(-AkronModule.Settings.RecordingPreRollSeconds), now, AkronModule.Settings.RecordingPostRollSeconds);
        }
    }

    public static void NotifyBerryCollect(Level level, bool golden) {
        if (level == null) {
            return;
        }

        if (AkronModule.Settings.RecordingTriggerBerryCollect ||
            golden && AkronModule.Settings.RecordingTriggerGoldenDeath) {
            QueueClipSave(golden ? "golden-berry" : "berry-collect", DateTime.UtcNow.AddSeconds(-AkronModule.Settings.RecordingPreRollSeconds), DateTime.UtcNow, AkronModule.Settings.RecordingPostRollSeconds);
        }
    }

    public static IEnumerable<string> SelectReplaySegmentsForTesting(string directory, DateTime startUtc, DateTime endUtc) {
        return SelectReplaySegments(directory, startUtc, endUtc);
    }

    private static void QueueClipSave(string kind, DateTime startUtc, DateTime eventUtc, int postRollSeconds) {
        if (AkronModuleSettings.ClampRecordingReplayBufferSeconds(AkronModule.Settings.RecordingReplayBufferSeconds) <= 0) {
            return;
        }

        int postRoll = AkronModuleSettings.ClampRecordingClipSeconds(postRollSeconds);
        PendingClipSave save = new PendingClipSave(kind, startUtc, eventUtc.AddSeconds(postRoll), eventUtc.AddSeconds(postRoll));
        lock (Sync) {
            PendingClipSaves.Add(save);
        }
    }

    private static void SaveReplayWindow(Scene scene, DateTime startUtc, DateTime endUtc, string kind) {
        string directory = replayDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) {
            Engine.Scene?.Add(new AkronToast("Replay buffer has no captured segments yet."));
            return;
        }

        AkronRecordedAudio replayAudio = StopReplayBuffer(true);
        List<string> segments = SelectReplaySegments(directory, startUtc, endUtc).ToList();
        if (segments.Count == 0) {
            DeleteRecordedAudioFiles(replayAudio);
            Engine.Scene?.Add(new AkronToast("Replay buffer has no segments for " + kind + "."));
            return;
        }

        AkronModuleSettings settings = AkronModule.Settings;
        string outputPath = BuildOutputPath(scene, settings.RecordingContainerFormat, kind);
        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory)) {
            Directory.CreateDirectory(outputDirectory);
        }

        string concatList = Path.Combine(directory, "concat-" + Guid.NewGuid().ToString("N") + ".txt");
        try {
            File.WriteAllLines(concatList, segments.Select(path => "file '" + path.Replace("'", "'\\''") + "'"));
            ProcessStartInfo startInfo = new ProcessStartInfo {
                FileName = ResolveFfmpegPath(),
                Arguments = JoinArguments(new[] {
                    "-hide_banner",
                    "-loglevel", "warning",
                    "-f", "concat",
                    "-safe", "0",
                    "-i", concatList,
                    "-c", "copy",
                    "-y", outputPath
                }),
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
            if (!process.WaitForExit(10000) || process.ExitCode != 0) {
                lastError = string.IsNullOrWhiteSpace(stderr) ? "FFmpeg clip save failed." : stderr.Trim();
                Engine.Scene?.Add(new AkronToast("Clip save failed: " + lastError));
                return;
            }

            string finalPath = outputPath;
            string remuxError = string.Empty;
            if (settings.RecordingAutoRemux &&
                string.Equals(Path.GetExtension(outputPath), ".mkv", StringComparison.OrdinalIgnoreCase) &&
                TryRemuxToMp4(outputPath, out string remuxedPath, out remuxError)) {
                finalPath = remuxedPath;
            } else if (!string.IsNullOrWhiteSpace(remuxError)) {
                lastError = remuxError;
                Engine.Scene?.Add(new AkronToast("Auto-remux failed: " + remuxError));
            }

            string audioMuxError = string.Empty;
            AkronRecordedAudio clippedAudio = SliceRecordedAudio(replayAudio, startUtc, endUtc);
            if (clippedAudio.HasSamples &&
                TryMuxAudioIntoVideo(finalPath, clippedAudio, out string audioMuxedPath, out audioMuxError)) {
                finalPath = audioMuxedPath;
            } else if (!string.IsNullOrWhiteSpace(audioMuxError)) {
                lastError = audioMuxError;
                Engine.Scene?.Add(new AkronToast("Clip audio mux failed: " + audioMuxError));
            }

            WriteClipSidecar(finalPath, kind, settings.RecordingContainerFormat, startUtc, endUtc, scene);
            lastClipPath = finalPath;
            Engine.Scene?.Add(new AkronToast("Clip saved: " + Path.GetFileName(finalPath)));
        } catch (Exception ex) {
            lastError = ex.Message;
            Engine.Scene?.Add(new AkronToast("Clip save failed: " + ex.Message));
        } finally {
            DeleteRecordedAudioFiles(replayAudio);
            TryDeleteFile(concatList);
        }
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
        lock (Sync) {
            if (replayProcess != null && activeSpec.Equals(spec)) {
                return;
            }
        }

        StopReplayBuffer(false);
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

        try {
            Process process = Process.Start(startInfo);
            if (process == null) {
                lastError = "FFmpeg replay buffer did not start.";
                return;
            }

            CaptureFfmpegStderr(process);

            lock (Sync) {
                replayProcess = process;
                replayInput = process.StandardInput.BaseStream;
                replayDirectory = directory;
                activeSpec = spec;
                EnsureCaptureClock();
                if (ffmpegProcess == null) {
                    nextFrameSeconds = 0d;
                    capturedFrames = 0;
                    droppedFrames = 0;
                }
                lastError = string.Empty;
                warningShown = false;
            }

            bool audioStarted = AkronInternalAudioRecorder.TryStartReplayBuffer(settings, out string audioWarning);
            if (!string.IsNullOrWhiteSpace(audioWarning) &&
                (audioStarted || settings.RecordingAudioFullMixTrack || settings.RecordingAudioMusicTrack || settings.RecordingAudioSfxTrack || settings.RecordingAudioAmbienceTrack)) {
                scene.Add(new AkronToast("Replay audio unavailable: " + audioWarning));
            }
        } catch (Exception ex) {
            lastError = ex.Message;
            scene.Add(new AkronToast("Replay buffer failed: " + ex.Message));
        }
    }

    private static AkronRecordedAudio StopReplayBuffer(bool keepAudio = false) {
        Process process;
        Stream input;
        lock (Sync) {
            process = replayProcess;
            input = replayInput;
            replayProcess = null;
            replayInput = null;
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
            lastError = ex.Message;
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

        foreach (FileInfo file in Directory.EnumerateFiles(replayDirectory, "segment-*.mkv").Select(path => new FileInfo(path))) {
            if (file.LastWriteTimeUtc < keepAfter) {
                TryDeleteFile(file.FullName);
            }
        }
    }

    private static void PruneOldReplayDirectories(string folder) {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(folder, ".replay-*")) {
            if (string.Equals(directory, replayDirectory, StringComparison.OrdinalIgnoreCase)) {
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
        public PendingClipSave(string kind, DateTime startUtc, DateTime endUtc, DateTime saveAtUtc) {
            Kind = kind;
            StartUtc = startUtc;
            EndUtc = endUtc;
            SaveAtUtc = saveAtUtc;
        }

        public string Kind { get; }
        public DateTime StartUtc { get; }
        public DateTime EndUtc { get; }
        public DateTime SaveAtUtc { get; }
    }
}
