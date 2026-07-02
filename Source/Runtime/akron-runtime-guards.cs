using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    private static bool proofRecorderGuardWarningShown;
    private static float pauseCountdownTimer;
    private static double pauseCountdownEndTimestamp;
    private static long lastLagPauserUpdateTimestamp;

    private static void LevelOnPause(Level level, int startIndex, bool minimal, bool quickReset) {
        if (level == null || !Settings.PauseTracker || !TryUse(AkronFeatureKind.PauseTracker)) {
            return;
        }

        float now = level.RawTimeActive;
        if (Session.PauseTrackerLastPauseAt >= 0f && now - Session.PauseTrackerLastPauseAt <= 1f) {
            Session.PauseTrackerRapidPauseCount++;
        }

        Session.PauseTrackerPauseCount++;
        Session.PauseTrackerLastPauseAt = now;
        Session.PauseTrackerCurrentPauseStartedAt = now;
    }

    private static void LevelOnUnpause(Level level) {
        AkronAutosave.NotifyPause();
        if (level != null && Settings.PauseTracker && Session.PauseTrackerCurrentPauseStartedAt >= 0f) {
            Session.PauseTrackerPausedSeconds += Math.Max(0f, level.RawTimeActive - Session.PauseTrackerCurrentPauseStartedAt);
            Session.PauseTrackerCurrentPauseStartedAt = -1f;
        }

        if (level == null ||
            !Settings.PauseCountdown ||
            !TryUse(AkronFeatureKind.PauseCountdown)) {
            ClearPauseCountdown();
            return;
        }

        pauseCountdownTimer = AkronModuleSettings.ClampPauseCountdownSeconds(Settings.PauseCountdownSeconds);
        pauseCountdownEndTimestamp = Stopwatch.GetTimestamp() + pauseCountdownTimer * Stopwatch.Frequency;
    }

    internal static void NotifyPauseForQa(Level level) {
        LevelOnPause(level, 0, minimal: false, quickReset: false);
    }

    internal static void NotifyUnpauseForQa(Level level) {
        LevelOnUnpause(level);
    }

    private static bool UpdatePauseCountdown(Level level) {
        float remaining = GetPauseCountdownRemaining();
        if (remaining <= 0f) {
            ClearPauseCountdown();
            return false;
        }

        pauseCountdownTimer = remaining;
        AkronRuntimeOptions.HoldSceneClockForSkippedLevelUpdate(level);
        return true;
    }

    private static float GetPauseCountdownRemaining() {
        if (pauseCountdownTimer <= 0f) {
            return 0f;
        }

        if (pauseCountdownEndTimestamp <= 0d) {
            return Math.Max(0f, pauseCountdownTimer);
        }

        double remaining = (pauseCountdownEndTimestamp - Stopwatch.GetTimestamp()) / Stopwatch.Frequency;
        return (float) Math.Max(0d, remaining);
    }

    private static void ClearPauseCountdown() {
        pauseCountdownTimer = 0f;
        pauseCountdownEndTimestamp = 0d;
    }

    private static void ResetProofRuntimeTelemetry() {
        Session.PauseTrackerPauseCount = 0;
        Session.PauseTrackerRapidPauseCount = 0;
        Session.PauseTrackerPausedSeconds = 0f;
        Session.PauseTrackerLastPauseAt = -1f;
        Session.PauseTrackerCurrentPauseStartedAt = -1f;
        Session.LagPauserTriggerCount = 0;
        Session.LagPauserLastSpikeMs = 0f;
        lastLagPauserUpdateTimestamp = 0L;
        Session.UsedGoldenStartHelper = false;
        Session.UsedJournalSnapshotCompare = false;
        Session.LastJournalSnapshotPath = string.Empty;
        Session.LastJournalCompareSummary = string.Empty;
    }

    private static void UpdateLagPauser(Level level) {
        long now = Stopwatch.GetTimestamp();
        float spikeMs = lastLagPauserUpdateTimestamp > 0L
            ? (float) ((now - lastLagPauserUpdateTimestamp) * 1000d / Stopwatch.Frequency)
            : 0f;
        lastLagPauserUpdateTimestamp = now;

        if (level == null ||
            level.Paused ||
            !Settings.LagPauser ||
            !AkronPolicy.CanUse(AkronFeatureKind.LagPauser).Allowed) {
            return;
        }

        if (spikeMs < AkronModuleSettings.ClampLagPauserThresholdMs(Settings.LagPauserThresholdMs)) {
            return;
        }

        Session.LagPauserTriggerCount++;
        Session.LagPauserLastSpikeMs = spikeMs;
        level.Pause();
        Engine.Scene?.Add(new AkronToast("Lag pause: " + Math.Round(spikeMs).ToString(CultureInfo.InvariantCulture) + " ms"));
    }

    private static void UpdateProofRecorderGuard(Level level) {
        if (level == null ||
            proofRecorderGuardWarningShown ||
            !Settings.ProofRecorderGuard ||
            !Settings.SubmissionMode ||
            AkronInternalRecorder.IsRecording ||
            AkronInternalRecorder.IsReplayBuffering ||
            !AkronPolicy.CanUse(AkronFeatureKind.ProofRecorderGuard).Allowed) {
            return;
        }

        proofRecorderGuardWarningShown = true;
        Engine.Scene?.Add(new AkronToast("Submission mode: proof recorder is not armed.", forceVisible: true));
    }

    private static void UpdateGoldenTransparency(Level level) {
        if (level == null) {
            return;
        }

        byte alpha = (byte) (Settings.GoldenTransparency && AkronPolicy.CanUse(AkronFeatureKind.GoldenTransparency).Allowed
            ? Calc.Clamp(AkronModuleSettings.ClampGoldenTransparencyOpacity(Settings.GoldenTransparencyOpacity), 0, 100) * 255 / 100
            : 255);

        foreach (Strawberry strawberry in level.Entities.OfType<Strawberry>()) {
            if (!strawberry.Golden) {
                continue;
            }

            foreach (Sprite sprite in strawberry.Components.OfType<Sprite>()) {
                Color color = sprite.Color;
                sprite.Color = new Color(color.R, color.G, color.B, alpha);
            }
        }
    }

    private static void UpdateDeathStatsTimer() {
        if (Session.DeathStatsAfterDeathTimer > 0f) {
            Session.DeathStatsAfterDeathTimer = Math.Max(0f, Session.DeathStatsAfterDeathTimer - Math.Max(0f, Engine.RawDeltaTime));
        }
    }

    private static void MaybeShowDeathPbLossPrompt(Level level) {
        if (!Settings.DeathPbLossPrompt ||
            Session.DeathPbLossPromptShown ||
            level?.Session == null ||
            !TryUse(AkronFeatureKind.DeathPbLossRestart)) {
            return;
        }

        int mode = (int) level.Session.Area.Mode;
        AreaModeStats modeStats = level.Session.OldStats?.Modes != null && mode >= 0 && mode < level.Session.OldStats.Modes.Length
            ? level.Session.OldStats.Modes[mode]
            : null;
        if (modeStats == null || !modeStats.SingleRunCompleted) {
            return;
        }

        int currentDeaths = AkronHudRenderer.GetCurrentMapDeathTotal(level);
        if (currentDeaths <= modeStats.BestDeaths) {
            return;
        }

        Session.DeathPbLossPromptShown = true;
        AkronPromptMenu.Show(
            level,
            "PB LOSS",
            "Current deaths are " + currentDeaths.ToString(CultureInfo.InvariantCulture) +
            ", above PB " + modeStats.BestDeaths.ToString(CultureInfo.InvariantCulture) + ".",
            new AkronPromptOption("Restart Chapter", () => ReloadChapter(level, confirmed: true)),
            new AkronPromptOption("Continue", () => { })
        );
    }

    public static float PauseCountdownRemaining => GetPauseCountdownRemaining();

    public static bool IsPauseCountdownActive => GetPauseCountdownRemaining() > 0f;
}
