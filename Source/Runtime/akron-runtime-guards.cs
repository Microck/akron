using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    // Speedrun Tool load states can hitch across the callback boundary, so the
    // grace window covers the load itself and a short recovery period after it.
    private const double SpeedrunToolLagPauserIgnoreSeconds = 1.5d;
    private const double StartPosLagPauserIgnoreSeconds = 1.5d;

    private static bool proofRecorderGuardWarningShown;
    private static float pauseCountdownTimer;
    private static double pauseCountdownEndTimestamp;
    private static long lastLagPauserUpdateTimestamp;
    private static ulong lastLagPauserUpdateFrameCounter;
    private static bool ignoreNextLagPauserSpikeForNativeFreeze;
    private static long lagPauserSpeedrunToolIgnoreUntilTimestamp;
    private static long lagPauserStartPosIgnoreUntilTimestamp;
    private static long lagPauserRecoveryIgnoreUntilTimestamp;
    private static bool lagPauserRepeatCooldownPending;

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
        if (lagPauserRepeatCooldownPending) {
            // Start the repeat window when gameplay resumes so time spent reading
            // the pause menu cannot consume the protection.
            lagPauserRepeatCooldownPending = false;
            SuppressLagPauserForWindow(Settings.LagPauserRepeatCooldownMs);
        }
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
        lastLagPauserUpdateFrameCounter = 0UL;
        ignoreNextLagPauserSpikeForNativeFreeze = false;
        lagPauserSpeedrunToolIgnoreUntilTimestamp = 0L;
        lagPauserStartPosIgnoreUntilTimestamp = 0L;
        lagPauserRecoveryIgnoreUntilTimestamp = 0L;
        lagPauserRepeatCooldownPending = false;
        Session.UsedGoldenStartHelper = false;
        Session.UsedJournalSnapshotCompare = false;
        Session.LastJournalSnapshotPath = string.Empty;
        Session.LastJournalCompareSummary = string.Empty;
    }

    private static void UpdateLagPauser(Level level) {
        long now = Stopwatch.GetTimestamp();
        ulong frameCounter = Engine.FrameCounter;
        float spikeMs = lastLagPauserUpdateTimestamp > 0L
            ? (float) ((now - lastLagPauserUpdateTimestamp) * 1000d / Stopwatch.Frequency)
            : 0f;
        ulong skippedEngineFrames = lastLagPauserUpdateFrameCounter > 0UL && frameCounter >= lastLagPauserUpdateFrameCounter
            ? frameCounter - lastLagPauserUpdateFrameCounter
            : 0UL;
        lastLagPauserUpdateTimestamp = now;
        lastLagPauserUpdateFrameCounter = frameCounter;

        if (level == null ||
            level.Paused ||
            !Settings.LagPauser ||
            !AkronPolicy.CanUse(AkronFeatureKind.LagPauser).Allowed) {
            ignoreNextLagPauserSpikeForNativeFreeze = false;
            return;
        }

        bool ignoreNativeFreezeSpike = ignoreNextLagPauserSpikeForNativeFreeze || Engine.FreezeTimer > 0f;
        bool ignoreIntentionalLoadSpike = IsLagPauserSpeedrunToolIgnoreActive(now) || IsLagPauserStartPosIgnoreActive(now);
        bool ignoreRecoveryWindow = lagPauserRecoveryIgnoreUntilTimestamp > now;
        ignoreNextLagPauserSpikeForNativeFreeze = false;

        if (!ShouldTriggerLagPauser(spikeMs, Settings.LagPauserThresholdMs, ignoreNativeFreezeSpike, ignoreIntentionalLoadSpike, ignoreRecoveryWindow, skippedEngineFrames)) {
            return;
        }

        Session.LagPauserTriggerCount++;
        Session.LagPauserLastSpikeMs = spikeMs;
        lagPauserRepeatCooldownPending = true;
        level.Pause();
        Engine.Scene?.Add(new AkronToast("Lag pause: " + Math.Round(spikeMs).ToString(CultureInfo.InvariantCulture) + " ms"));
    }

    internal static bool ShouldTriggerLagPauser(
        float spikeMs,
        int thresholdMs,
        bool ignoreNativeFreezeSpike,
        bool ignoreIntentionalLoadSpike,
        bool ignoreRecoveryWindow,
        ulong skippedEngineFrames) {
        return !ignoreNativeFreezeSpike &&
               !ignoreIntentionalLoadSpike &&
               !ignoreRecoveryWindow &&
               skippedEngineFrames <= 1UL &&
               spikeMs >= AkronModuleSettings.ClampLagPauserThresholdMs(thresholdMs);
    }

    private static void RememberNativeFreezeFrameForLagPauser() {
        if (Engine.FreezeTimer > 0f) {
            ignoreNextLagPauserSpikeForNativeFreeze = true;
        }
    }

    internal static void SuppressLagPauserForSpeedrunToolLoadState() {
        if (!Settings.LagPauserIgnoreSpeedrunToolLoadStates) {
            return;
        }

        lagPauserSpeedrunToolIgnoreUntilTimestamp = Stopwatch.GetTimestamp() + (long) (SpeedrunToolLagPauserIgnoreSeconds * Stopwatch.Frequency);
    }

    internal static bool IsLagPauserSpeedrunToolIgnoreActive(long timestamp) {
        return Settings.LagPauserIgnoreSpeedrunToolLoadStates &&
               lagPauserSpeedrunToolIgnoreUntilTimestamp > timestamp;
    }

    internal static void SuppressLagPauserForNativeStartPosRestore() {
        SuppressLagPauserForNativeStartPosRestore(Stopwatch.GetTimestamp());
    }

    internal static void SuppressLagPauserForNativeStartPosRestore(long timestamp) {
        lagPauserStartPosIgnoreUntilTimestamp = timestamp + (long) (StartPosLagPauserIgnoreSeconds * Stopwatch.Frequency);
    }

    internal static bool IsLagPauserStartPosIgnoreActive(long timestamp) {
        return lagPauserStartPosIgnoreUntilTimestamp > timestamp;
    }

    internal static void SuppressLagPauserForRecovery() {
        SuppressLagPauserForWindow(Settings.LagPauserRecoveryGraceMs);
    }

    private static void SuppressLagPauserForWindow(int windowMs) {
        int clampedWindowMs = AkronModuleSettings.ClampLagPauserWindowMs(windowMs);
        long deadline = Stopwatch.GetTimestamp() + clampedWindowMs * Stopwatch.Frequency / 1000L;
        lagPauserRecoveryIgnoreUntilTimestamp = Math.Max(lagPauserRecoveryIgnoreUntilTimestamp, deadline);
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
