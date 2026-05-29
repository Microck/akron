using System.Collections.Generic;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed class AkronPolicyDecision {
    public AkronPolicyDecision(bool allowed, string message) {
        Allowed = allowed;
        Message = message;
    }

    public bool Allowed { get; }
    public string Message { get; }
}

public sealed class AkronActiveCheatContributor {
    public AkronActiveCheatContributor(string label, string disableCommand, AkronFeatureKind feature) {
        Label = label;
        DisableCommand = disableCommand;
        Feature = feature;
    }

    public string Label { get; }
    public string DisableCommand { get; }
    public AkronFeatureKind Feature { get; }
}

public static class AkronPolicy {
    public const int GoldberryHardlistCleanColorRgb = 0x248BFF;
    public const int RegularCleanColorRgb = 0x00FF00;
    public const int CheatColorRgb = 0xFF0000;
    public const int SafeModeRedactedCleanColorRgb = 0x6495ED;

    private static readonly HashSet<AkronFeatureKind> TrainingBlockedInLeaderboardClean = new HashSet<AkronFeatureKind> {
        AkronFeatureKind.RetryHotkey,
        AkronFeatureKind.RoomReload,
        AkronFeatureKind.ChapterReload,
        AkronFeatureKind.DebugMapLauncher,
        AkronFeatureKind.MountainViewer,
        AkronFeatureKind.Savestates,
        AkronFeatureKind.BrokeredSavestates,
        AkronFeatureKind.TasHandoff,
        AkronFeatureKind.SplitHelper,
        AkronFeatureKind.RoomWarp,
        AkronFeatureKind.HitboxViewer,
        AkronFeatureKind.EntityInspector,
        AkronFeatureKind.FlagInspector,
        AkronFeatureKind.FrameAdvance,
        AkronFeatureKind.Freeze,
        AkronFeatureKind.Timescale,
        AkronFeatureKind.FreeCamera
    };

    public static AkronPolicyDecision CanUse(AkronFeatureKind feature) {
        AkronModuleSettings settings = AkronModule.Settings;
        AkronStatus classification = AkronFeatureRegistry.Classify(feature);

        if (settings.PrimaryRuleset == PrimaryRuleset.Sandbox) {
            return new AkronPolicyDecision(true, "Sandbox allows all Akron feature classes.");
        }

        if (settings.PrimaryRuleset == PrimaryRuleset.LeaderboardClean) {
            if (classification == AkronStatus.Cheat || TrainingBlockedInLeaderboardClean.Contains(feature)) {
                return new AkronPolicyDecision(false, "Current ruleset blocks state-changing features. Switch rulesets or keep current guardrails.");
            }
        }

        // Safe mode only redacts clean-legitimacy surfaces. It should not turn into a
        // global gameplay lock that blocks local state-changing features by itself.
        if (feature == AkronFeatureKind.UnsafeNativeSavestateOverride &&
            !settings.UnsafeSavestateOverride &&
            !AkronMapOverrides.ShouldAllowUnsafeSavestates(Engine.Scene as Level)) {
            return new AkronPolicyDecision(false, "Unsafe StartPos restore override is disabled in settings.");
        }

        return new AkronPolicyDecision(true, AkronFeatureRegistry.Get(feature).Reason);
    }

    public static bool ShouldOfferRulesetEscape(AkronFeatureKind feature) {
        if (AkronModule.Settings.PrimaryRuleset != PrimaryRuleset.LeaderboardClean) {
            return false;
        }

        AkronStatus classification = AkronFeatureRegistry.Classify(feature);
        return classification == AkronStatus.Cheat || TrainingBlockedInLeaderboardClean.Contains(feature);
    }

    public static PrimaryRuleset GetSuggestedRuleset(AkronFeatureKind feature) {
        return AkronFeatureRegistry.Classify(feature) == AkronStatus.Cheat
            ? PrimaryRuleset.Sandbox
            : PrimaryRuleset.Practice;
    }

    public static void RecordFeatureUse(AkronFeatureKind feature) {
        if (AkronModule.Session == null) {
            return;
        }

        FeatureDefinition definition = AkronFeatureRegistry.Get(feature);
        if (definition.Classification >= AkronModule.Session.AttemptStatus) {
            AkronModule.Session.AttemptReason = definition.Reason;
        }
        AkronModule.Session.AttemptStatus = Max(AkronModule.Session.AttemptStatus, definition.Classification);

        if (feature == AkronFeatureKind.BrokeredSavestates) {
            AkronModule.Session.UsedBrokeredSavestate = true;
        }

        if (feature == AkronFeatureKind.UnsafeNativeSavestateOverride) {
            AkronModule.Session.UsedUnsafeSavestateOverride = true;
        }
    }

    public static void ResetAttempt(string reason) {
        AkronModule.Session.AttemptStatus = AkronStatus.GoldberryHardlistClean;
        AkronModule.Session.AttemptReason = reason;
        AkronModule.Session.UsedBrokeredSavestate = false;
        AkronModule.Session.UsedUnsafeSavestateOverride = false;
    }

    public static bool CanExposeCleanLegitimacy() {
        return AkronModule.Instance == null || !AkronModule.Settings.SafeMode;
    }

    public static string GetLegitimacySensitiveStatusLabel(AkronStatus status) {
        if (AkronModule.Settings.SafeMode && status == AkronStatus.GoldberryHardlistClean) {
            return "Safe mode";
        }

        return AkronModuleSettings.FormatStatus(status);
    }

    public static int GetStatusColorRgb(AkronStatus status, bool safeModeRedactsCleanStatus = false) {
        if (safeModeRedactsCleanStatus && status == AkronStatus.GoldberryHardlistClean) {
            return SafeModeRedactedCleanColorRgb;
        }

        return status switch {
            AkronStatus.Cheat => CheatColorRgb,
            AkronStatus.RegularClean => RegularCleanColorRgb,
            _ => GoldberryHardlistCleanColorRgb
        };
    }

    public static string GetStatusColorName(AkronStatus status, bool safeModeRedactsCleanStatus = false) {
        if (safeModeRedactsCleanStatus && status == AkronStatus.GoldberryHardlistClean) {
            return "Blue";
        }

        return status switch {
            AkronStatus.Cheat => "Red",
            AkronStatus.RegularClean => "Green",
            _ => "Blue"
        };
    }

    public static string DescribeStatusColorReason(AkronStatus status, string reason, bool safeModeRedactsCleanStatus = false) {
        string statusLabel = safeModeRedactsCleanStatus && status == AkronStatus.GoldberryHardlistClean
            ? "Safe mode"
            : AkronModuleSettings.FormatStatus(status);
        string colorName = GetStatusColorName(status, safeModeRedactsCleanStatus);
        string trimmedReason = string.IsNullOrWhiteSpace(reason)
            ? "No modifying Akron feature has been used in this attempt."
            : reason.Trim();

        return colorName + " because the current attempt is " + statusLabel + ". " + trimmedReason;
    }

    public static IReadOnlyList<AkronActiveCheatContributor> GetActiveCheatContributors(AkronModuleSettings settings, AkronModuleSession session = null) {
        List<AkronActiveCheatContributor> contributors = new List<AkronActiveCheatContributor>();
        if (settings == null) {
            return contributors;
        }

        AddIfCheat(contributors, settings.AutoKill, "Auto Kill", "akron_feature auto-kill off", AkronFeatureKind.AutoKill);
        AddIfCheat(contributors, settings.CursorZoom, "Cursor Zoom", "akron_feature cursor-zoom off", AkronFeatureKind.CursorZoom);
        AddIfCheat(contributors, settings.ClickTeleport, "Click Teleport", "akron_feature click-teleport off", AkronFeatureKind.ClickTeleport);
        AddIfCheat(contributors, settings.Noclip, "Noclip", "akron_feature noclip off", AkronFeatureKind.Noclip);
        AddIfCheat(contributors, settings.NoclipAccuracy, "Hazard Accuracy", "akron_feature hazard-accuracy off", AkronFeatureKind.HazardAccuracy);
        AddIfCheat(contributors, settings.FreeCamera, "Free Camera", "akron_feature free-camera off", AkronFeatureKind.FreeCamera);
        AddIfCheat(contributors, AkronMotionSmoothingInterop.Loaded && settings.FpsBypass, "FPS Bypass", "akron_feature fps-bypass off", AkronFeatureKind.FpsBypass);
        AddIfCheat(contributors, AkronMotionSmoothingInterop.Loaded && settings.TpsBypass, "TPS Bypass", "akron_feature tps-bypass off", AkronFeatureKind.TpsBypass);
        AddIfCheat(contributors, settings.Invincibility, "Invincibility", "akron_feature invincibility off", AkronFeatureKind.Invincibility);
        AddIfCheat(contributors, settings.JumpHack, "Air Jumps", "akron_feature air-jumps off", AkronFeatureKind.MovementStatMutation);
        AddIfCheat(contributors, settings.ResourceBars, "Resource Bars", "akron_feature resource-bars off", AkronFeatureKind.ResourceBars);
        AddIfCheat(contributors, settings.StaminaBar, "Stamina Bar", "akron_feature stamina-bar off", AkronFeatureKind.ResourceBars);
        AddIfCheat(contributors, settings.DashBar, "Dash Bar", "akron_feature dash-bar off", AkronFeatureKind.ResourceBars);
        AddIfCheat(contributors, settings.DashNumber, "Dash Number", "akron_dash_count number off", AkronFeatureKind.ResourceBars);
        AddIfCheat(contributors, settings.SpeedNumber, "Speed Number", "akron_feature speed-number off", AkronFeatureKind.SpeedNumber);
        AddIfCheat(contributors, settings.SafeModeFreezeAttempts, "Freeze deaths", "akron_feature freeze-deaths off", AkronFeatureKind.SafeModeStats);
        AddIfCheat(contributors, settings.SafeModeFreezeJumps, "Freeze jumps", "akron_feature freeze-jumps off", AkronFeatureKind.SafeModeStats);
        AddIfCheat(contributors, settings.SafeModeFreezeBestRun, "Freeze best run", "akron_feature freeze-best-run off", AkronFeatureKind.SafeModeStats);
        AddIfCheat(contributors, settings.TransitionSpeedMultiplier != 1f, "Transition Speed", "akron_megahack_public transition-speed 1", AkronFeatureKind.TransitionSpeed);
        AddIfCheat(contributors, settings.RespawnTimeModifier, "Respawn Time", "akron_megahack_public respawn-time off", AkronFeatureKind.RespawnTime);
        AddIfCheat(contributors, settings.FreezeTimerWhilePaused, "Freeze Timer While Paused", "akron_feature freeze-timer-paused off", AkronFeatureKind.PauseTimerFreeze);
        AddIfCheat(contributors, settings.FastLookout, "Fast Lookout", "akron_feature fast-lookout off", AkronFeatureKind.FastLookout);
        AddIfCheat(contributors, settings.DeathPbLossPrompt, "Death PB Loss Prompt", "akron_feature death-pb-loss-prompt off", AkronFeatureKind.DeathPbLossRestart);
        AddIfCheat(contributors, settings.NoDeathEffect, "No Death Effect", "akron_feature no-death-effect off", AkronFeatureKind.DeathVisuals);
        AddIfCheat(contributors, settings.NoDeathWipe, "No Death Wipe", "akron_feature no-death-wipe off", AkronFeatureKind.DeathVisuals);
        AddIfCheat(contributors, settings.NoRespawnAnimation, "No Respawn Animation", "akron_feature no-respawn-animation off", AkronFeatureKind.RespawnAnimation);
        AddIfCheat(contributors, settings.NoFreezeFrames, "No Freeze Frames", "akron_feature no-freeze-frames off", AkronFeatureKind.FreezeFrames);
        AddIfCheat(contributors, settings.GroundRefillRules, "Ground Refills", "akron_feature ground-refills off", AkronFeatureKind.GroundRefillRules);
        AddIfCheat(contributors, settings.PreventDownDashRedirectsEnabled, "Prevent Down Dash Redirects", "akron_feature prevent-down-dash-redirects off", AkronFeatureKind.InputAssistShortcut);
        AddIfCheat(contributors, settings.InfiniteDash, "Infinite Dash", "akron_feature infinite-dash off", AkronFeatureKind.InfiniteDash);
        AddIfCheat(contributors, settings.InfiniteStamina, "Infinite Stamina", "akron_feature infinite-stamina off", AkronFeatureKind.InfiniteStamina);
        AddIfCheat(contributors, settings.DashCountOverride, "Dash Count", "akron_feature dash-count off", AkronFeatureKind.DashCountOverride);
        AddIfCheat(contributors, settings.GrabModeOverrideEnabled, "Grab Mode", "akron_grab_mode off", AkronFeatureKind.GrabModeHotkey);
        AddIfCheat(contributors, settings.DeloadSpinners, "Deload Spinners", "akron_deload_spinners off", AkronFeatureKind.DeloadSimulation);
        AddIfCheat(contributors, settings.HidePauseMenu, "Hide Pause Menu", "akron_feature hide-pause-menu off", AkronFeatureKind.PauseMenuVisibility);
        AddIfCheat(contributors, settings.PauseCountdown, "Pause Timer", "akron_feature pause-countdown off", AkronFeatureKind.PauseCountdown);
        AddIfCheat(contributors, settings.HitboxViewer, "Show Hitboxes", "akron_feature show-hitboxes off", AkronFeatureKind.HitboxViewer);
        AddIfCheat(contributors, settings.ShowTriggers, "Show Triggers", "akron_feature show-triggers off", AkronFeatureKind.TriggerViewer);
        AddIfCheat(contributors, settings.EntityInspector, "Entity Inspector", "akron_feature entity-inspector off", AkronFeatureKind.EntityInspector);
        AddIfCheat(contributors, settings.ShowTrajectory, "Show Trajectory", "akron_feature show-trajectory off", AkronFeatureKind.ShowTrajectory);
        AddIfCheat(contributors, settings.AudioSpeed, "Audio Speed", "akron_feature audio-speed off", AkronFeatureKind.AudioSpeed);
        AddExtendedVariantContributors(contributors);

        if (session != null) {
            AddIfCheat(contributors, session.FreezeGameplay, "Frame Stepper / Freeze Gameplay", "akron_feature frame-stepper off", AkronFeatureKind.FrameAdvance);
            AddIfCheat(contributors, session.TimescaleEnabled && session.TimescaleMultiplier != 1f, "Timescale", "akron_timescale off", AkronFeatureKind.Timescale);
        }

        return contributors;
    }

    public static bool IsMegaHackStyleCheatIndicatorFlagged(AkronStatus status) {
        return status == AkronStatus.Cheat;
    }

    private static void AddIfCheat(List<AkronActiveCheatContributor> contributors, bool enabled, string label, string disableCommand, AkronFeatureKind feature) {
        if (enabled && AkronFeatureRegistry.Classify(feature) == AkronStatus.Cheat) {
            contributors.Add(new AkronActiveCheatContributor(label, "Turn off " + label, feature));
        }
    }

    private static void AddExtendedVariantContributors(List<AkronActiveCheatContributor> contributors) {
        if (!AkronExtendedVariants.Available || AkronFeatureRegistry.Classify(AkronFeatureKind.ExtendedVariantMode) != AkronStatus.Cheat) {
            return;
        }

        AddIfCheat(contributors, AkronExtendedVariants.MasterSwitch, "Extended Variants Master", "akron_evm master off", AkronFeatureKind.ExtendedVariantMode);
        AddIfCheat(contributors, AkronExtendedVariants.RandomizerEnabled, "Extended Variants Randomizer", "akron_evm randomizer off", AkronFeatureKind.ExtendedVariantMode);

        foreach (AkronExtendedVariantOption option in AkronExtendedVariants.GetOptions()) {
            if (!option.IsDefault) {
                contributors.Add(new AkronActiveCheatContributor(option.Label, "Turn off " + option.Label, AkronFeatureKind.ExtendedVariantMode));
            }
        }
    }

    private static AkronStatus Max(AkronStatus left, AkronStatus right) {
        return left > right ? left : right;
    }
}
