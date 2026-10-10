using System.Runtime.CompilerServices;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public readonly struct AkronPolicyDecision
{
    public AkronPolicyDecision(bool allowed, string message)
    {
        Allowed = allowed;
        Message = message;
    }

    public bool Allowed { get; }
    public string Message { get; }
}

public sealed class AkronActiveCheatContributor
{
    public AkronActiveCheatContributor(string label, string disableCommand, AkronFeatureKind feature)
    {
        Label = label;
        DisableCommand = disableCommand;
        Feature = feature;
    }

    public string Label { get; }
    public string DisableCommand { get; }
    public AkronFeatureKind Feature { get; }
}

public static class AkronPolicy
{
    public const int GoldberryHardlistCleanColorRgb = 0x248BFF;
    public const int RegularCleanColorRgb = 0x00FF00;
    public const int CheatColorRgb = 0xFF0000;
    public const int UnclassifiedColorRgb = 0x909090;
    public const int SafeModeRedactedCleanColorRgb = 0x6495ED;

    public const int MapRestrictionVersion = 1;
    private const string MapRestrictionMessage = "Suppressed by this map. Your saved preference is unchanged.";
    private static ConditionalWeakTable<MapData, AkronMapFeatureRestrictions> mapRestrictions = new();
    private static volatile AkronMapFeatureRestrictions activeMapRestrictions = AkronMapFeatureRestrictions.Empty;
    [System.ThreadStatic]
    private static AkronMapFeatureRestrictions loadingMapRestrictions;
    private static AkronMapFeatureRestrictions CurrentMapRestrictions => loadingMapRestrictions ?? activeMapRestrictions;

    public static bool HasMapRestrictions => CurrentMapRestrictions.Features.Count != 0;
    public static IReadOnlyList<AkronFeatureKind> RestrictedFeatures => CurrentMapRestrictions.Features;
    public static bool IsMapRestricted(AkronFeatureKind feature) => CurrentMapRestrictions.Contains(feature);

    internal static bool EnterMap(MapData map)
    {
        return SetMapRestrictions(GetMapRestrictions(map));
    }

    private static AkronMapFeatureRestrictions GetMapRestrictions(MapData map) => map == null
        ? AkronMapFeatureRestrictions.Empty
        : mapRestrictions.GetValue(map, CompileMapRestrictions);

    internal static AkronMapFeatureRestrictions EnterMapLoadScope(MapData map)
    {
        // Everest can construct the incoming room while the outgoing level still updates.
        // Loader effects use the target declaration without replacing the live scene's policy.
        AkronMapFeatureRestrictions previous = loadingMapRestrictions;
        loadingMapRestrictions = GetMapRestrictions(map);
        return previous;
    }

    internal static void ExitMapLoadScope(AkronMapFeatureRestrictions previous)
    {
        loadingMapRestrictions = previous;
    }

    private static AkronMapFeatureRestrictions CompileMapRestrictions(MapData map)
    {
        AkronMapFeatureRestrictions restrictions = new(map.Levels);
        foreach (string identity in restrictions.UnknownIdentities)
        {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Unknown Akron/featureRestrictions identity '" + identity + "'. Use an exact AkronFeatureKind name; this entry was ignored.");
        }
        return restrictions;
    }

    private static bool SetMapRestrictions(AkronMapFeatureRestrictions restrictions)
    {
        if (ReferenceEquals(activeMapRestrictions, restrictions))
        {
            return false;
        }
        activeMapRestrictions = restrictions;
        return true;
    }

    internal static bool LeaveMap() => SetMapRestrictions(AkronMapFeatureRestrictions.Empty);

    internal static void UnloadMapRestrictions()
    {
        LeaveMap();
        mapRestrictions = new();
    }

    public static AkronPolicyDecision CanUse(AkronFeatureKind feature)
    {
        return IsMapRestricted(feature)
            ? new AkronPolicyDecision(false, MapRestrictionMessage)
            : new AkronPolicyDecision(true, AkronFeatureRegistry.Get(feature).Reason);
    }

    public static void RecordFeatureUse(AkronFeatureKind feature)
    {
        if (AkronModule.Session == null || !CanUse(feature).Allowed)
        {
            return;
        }

        FeatureDefinition definition = AkronFeatureRegistry.Get(feature);
        if (definition.Classification >= AkronModule.Session.AttemptStatus)
        {
            AkronModule.Session.AttemptReason = definition.Reason;
        }
        AkronModule.Session.AttemptStatus = Max(AkronModule.Session.AttemptStatus, definition.Classification);

        if (feature == AkronFeatureKind.BrokeredSavestates)
        {
            AkronModule.Session.UsedBrokeredSavestate = true;
        }
    }

    public static void RecordCheatUse(string reason)
    {
        if (AkronModule.Session == null)
        {
            return;
        }

        if (AkronStatus.Cheat >= AkronModule.Session.AttemptStatus)
        {
            AkronModule.Session.AttemptReason = string.IsNullOrWhiteSpace(reason)
                ? "A cheat-class Akron option was used in this attempt."
                : reason;
        }
        AkronModule.Session.AttemptStatus = AkronStatus.Cheat;
    }

    public static void ResetAttempt(string reason)
    {
        AkronModule.Session.AttemptStatus = AkronStatus.Unclassified;
        AkronModule.Session.AttemptReason = string.IsNullOrWhiteSpace(reason)
            ? "No Akron attempt classification has been selected or earned yet."
            : reason;
        AkronModule.Session.UsedBrokeredSavestate = false;
    }

    public static bool CanExposeCleanLegitimacy()
    {
        return AkronModule.Instance == null || !AkronModule.Settings.SafeMode;
    }

    public static string GetLegitimacySensitiveStatusLabel(AkronStatus status)
    {
        if (AkronModule.Settings.SafeMode && status == AkronStatus.GoldberryHardlistClean)
        {
            return "Safe mode";
        }

        return AkronModuleSettings.FormatStatus(status);
    }

    public static int GetStatusColorRgb(AkronStatus status, bool safeModeRedactsCleanStatus = false)
    {
        if (safeModeRedactsCleanStatus && status == AkronStatus.GoldberryHardlistClean)
        {
            return SafeModeRedactedCleanColorRgb;
        }

        return status switch
        {
            AkronStatus.Cheat => CheatColorRgb,
            AkronStatus.RegularClean => RegularCleanColorRgb,
            AkronStatus.GoldberryHardlistClean => GoldberryHardlistCleanColorRgb,
            AkronStatus.Unclassified => UnclassifiedColorRgb,
            _ => GoldberryHardlistCleanColorRgb
        };
    }

    public static string GetStatusColorName(AkronStatus status, bool safeModeRedactsCleanStatus = false)
    {
        if (safeModeRedactsCleanStatus && status == AkronStatus.GoldberryHardlistClean)
        {
            return "Blue";
        }

        return status switch
        {
            AkronStatus.Cheat => "Red",
            AkronStatus.RegularClean => "Green",
            AkronStatus.GoldberryHardlistClean => "Blue",
            AkronStatus.Unclassified => "Gray",
            _ => "Blue"
        };
    }

    public static string DescribeStatusColorReason(AkronStatus status, string reason, bool safeModeRedactsCleanStatus = false)
    {
        string statusLabel = safeModeRedactsCleanStatus && status == AkronStatus.GoldberryHardlistClean
            ? "Safe mode"
            : AkronModuleSettings.FormatStatus(status);
        string colorName = GetStatusColorName(status, safeModeRedactsCleanStatus);
        string trimmedReason = string.IsNullOrWhiteSpace(reason)
            ? "No Akron attempt classification has been selected or earned yet."
            : reason.Trim();

        return colorName + " because the current attempt is " + statusLabel + ". " + trimmedReason;
    }

    public static IReadOnlyList<AkronActiveCheatContributor> GetActiveCheatContributors(AkronModuleSettings settings, AkronModuleSession session = null)
    {
        List<AkronActiveCheatContributor> contributors = new List<AkronActiveCheatContributor>();
        if (settings == null)
        {
            return contributors;
        }

        AddIfCheat(contributors, settings.AutoKill, "Auto Kill", AkronFeatureKind.AutoKill);
        AddIfCheat(contributors, settings.AllowPauseBuffering, "Pause Buffering", AkronFeatureKind.PauseBuffering);
        AddIfCheat(contributors, settings.CursorZoom, "Cursor Zoom", AkronFeatureKind.CursorZoom);
        AddIfCheat(contributors, settings.CursorTools, "Cursor Tools", AkronFeatureKind.CursorTools);
        AddIfCheat(contributors, settings.ClickTeleport, "Click Teleport", AkronFeatureKind.ClickTeleport);
        AddIfCheat(contributors, settings.Noclip, "Noclip", AkronFeatureKind.Noclip);
        AddIfCheat(contributors, settings.NoclipAccuracy, "Hazard Accuracy", AkronFeatureKind.HazardAccuracy);
        AddIfCheat(contributors, settings.FreeCamera, "Free Camera", AkronFeatureKind.FreeCamera);
        AddIfCheat(contributors, AkronMotionSmoothingInterop.Loaded && settings.FpsBypass, "FPS Bypass", AkronFeatureKind.FpsBypass);
        AddMotionSmoothingCheatContributor(contributors, AkronMotionSmoothingInterop.Loaded && settings.FrameBypassObjectSmoothing == AkronObjectSmoothingMode.Interpolate, "FPS Bypass object interpolation", "akron_frame_bypass objects extrapolate");
        AddMotionSmoothingCheatContributor(contributors, AkronMotionSmoothingInterop.Loaded && settings.FrameBypassTasMode, "FPS Bypass TAS mode", "akron_frame_bypass tas off");
        AddMotionSmoothingCheatContributor(contributors, AkronMotionSmoothingInterop.Loaded && settings.FrameBypassSillyMode, "FPS Bypass Nasty mode", "akron_frame_bypass nasty off");
        AddIfCheat(contributors, AkronMotionSmoothingInterop.Loaded && settings.TpsBypass, "TPS Bypass", AkronFeatureKind.TpsBypass);
        AddIfCheat(contributors, settings.Invincibility, "Invincibility", AkronFeatureKind.Invincibility);
        AddIfCheat(contributors, settings.JumpHack, "Air Jumps", AkronFeatureKind.MovementStatMutation);
        AddIfCheat(contributors, settings.ResourceBars, "Resource Bars", AkronFeatureKind.ResourceBars);
        AddIfCheat(contributors, settings.StaminaBar, "Stamina Bar", AkronFeatureKind.ResourceBars);
        AddIfCheat(contributors, settings.DashBar, "Dash Bar", AkronFeatureKind.ResourceBars);
        AddIfCheat(contributors, settings.DashNumber, "Dash Number", AkronFeatureKind.ResourceBars);
        AddIfCheat(contributors, settings.SpeedNumber, "Speed Number", AkronFeatureKind.SpeedNumber);
        AddIfCheat(contributors, settings.SafeModeFreezeAttempts, "Freeze deaths", AkronFeatureKind.SafeModeStats);
        AddIfCheat(contributors, settings.SafeModeFreezeJumps, "Freeze jumps", AkronFeatureKind.SafeModeStats);
        AddIfCheat(contributors, settings.SafeModeFreezeBestRun, "Freeze best run", AkronFeatureKind.SafeModeStats);
        AddIfCheat(contributors, AkronModule.IsTransitionSpeedEffective(settings), "Transition Speed", AkronFeatureKind.TransitionSpeed);
        AddIfCheat(contributors, settings.RespawnTimeModifier, "Respawn Time", AkronFeatureKind.RespawnTime);
        AddIfCheat(contributors, settings.FreezeTimerWhilePaused, "Freeze Timer While Paused", AkronFeatureKind.PauseTimerFreeze);
        AddIfCheat(contributors, settings.FastLookout, "Fast Lookout", AkronFeatureKind.FastLookout);
        AddIfCheat(contributors, settings.DeathPbLossPrompt, "Death PB Loss Prompt", AkronFeatureKind.DeathPbLossRestart);
        AddIfCheat(contributors, settings.NoDeathEffect, "No Death Effect", AkronFeatureKind.DeathVisuals);
        AddIfCheat(contributors, settings.NoDeathWipe, "No Death Wipe", AkronFeatureKind.DeathVisuals);
        AddIfCheat(contributors, settings.NoRespawnAnimation, "No Respawn Animation", AkronFeatureKind.RespawnAnimation);
        AddIfCheat(contributors, settings.NoFreezeFrames, "No Freeze Frames", AkronFeatureKind.FreezeFrames);
        // With both refills allowed the rule changes nothing, and the runtime returns before recording it.
        AddIfCheat(contributors, settings.GroundRefillRules && !(settings.GroundDashRefill && settings.GroundStaminaRefill), "Ground Refills", AkronFeatureKind.GroundRefillRules);
        AddIfCheat(contributors, settings.DashRedirectEnabled, "Dash Redirect", AkronFeatureKind.InputAssistShortcut);
        AddIfCheat(contributors, settings.InfiniteDash, "Infinite Dash", AkronFeatureKind.InfiniteDash);
        AddIfCheat(contributors, settings.InfiniteStamina, "Infinite Stamina", AkronFeatureKind.InfiniteStamina);
        AddIfCheat(contributors, settings.DashCountOverride, "Dash Count", AkronFeatureKind.DashCountOverride);
        AddIfCheat(contributors, settings.GrabModeOverrideEnabled, "Grab Mode", AkronFeatureKind.GrabModeHotkey);
        AddIfCheat(contributors, settings.HidePauseMenu, "Hide Pause Menu", AkronFeatureKind.PauseMenuVisibility);
        AddIfCheat(contributors, settings.PauseCountdown, "Pause Timer", AkronFeatureKind.PauseCountdown);
        AddIfCheat(contributors, settings.HitboxViewer, "Show Hitboxes", AkronFeatureKind.HitboxViewer);
        AddIfCheat(contributors, settings.ShowTriggers, "Show Triggers", AkronFeatureKind.TriggerViewer);
        AddIfCheat(contributors, settings.EntityInspector, "Entity Inspector", AkronFeatureKind.EntityInspector);
        AddIfCheat(contributors, settings.ShowTrajectory, "Show Trajectory", AkronFeatureKind.ShowTrajectory);
        AddIfCheat(contributors, settings.FrameStepper, "Frame Stepper", AkronFeatureKind.FrameAdvance);
        AddIfCheat(contributors, settings.AudioSpeed, "Audio Speed", AkronFeatureKind.AudioSpeed);
        AddExtendedVariantContributors(contributors);

        if (session != null)
        {
            AddIfCheat(contributors, session.FreezeGameplay, "Freeze Gameplay", AkronFeatureKind.Freeze);
            AddIfCheat(contributors, session.TimescaleEnabled && session.TimescaleMultiplier != 1f, "Timescale", AkronFeatureKind.Timescale);
        }

        return contributors;
    }

    public static bool IsMegaHackStyleCheatIndicatorFlagged(AkronStatus status)
    {
        return status == AkronStatus.Cheat;
    }

    internal static bool IsLabelFeatureActive(bool labelsVisible, bool labelEnabled)
    {
        return labelsVisible && labelEnabled;
    }

    private static void AddIfCheat(List<AkronActiveCheatContributor> contributors, bool enabled, string label, AkronFeatureKind feature)
    {
        if (enabled && CanUse(feature).Allowed && AkronFeatureRegistry.Classify(feature) == AkronStatus.Cheat)
        {
            contributors.Add(new AkronActiveCheatContributor(label, "Turn off " + label, feature));
        }
    }

    private static void AddMotionSmoothingCheatContributor(List<AkronActiveCheatContributor> contributors, bool enabled, string label, string disableCommand)
    {
        if (enabled && CanUse(AkronFeatureKind.FpsBypass).Allowed)
        {
            contributors.Add(new AkronActiveCheatContributor(label, disableCommand, AkronFeatureKind.FpsBypass));
        }
    }

    private static void AddExtendedVariantContributors(List<AkronActiveCheatContributor> contributors)
    {
        if (!AkronExtendedVariants.Available || !CanUse(AkronFeatureKind.ExtendedVariantMode).Allowed)
        {
            return;
        }

        if (AkronExtendedVariants.RandomizerEnabled)
        {
            contributors.Add(new AkronActiveCheatContributor("Extended Variants Randomizer", "akron_evm randomizer off", AkronFeatureKind.ExtendedVariantMode));
        }

        foreach (AkronExtendedVariantOption option in AkronExtendedVariants.GetOptions())
        {
            if (ShouldFlagExtendedVariantOption(option))
            {
                contributors.Add(new AkronActiveCheatContributor(option.Label, "Turn off " + option.Label, AkronFeatureKind.ExtendedVariantMode));
            }
        }
    }

    internal static bool ShouldFlagExtendedVariantOption(AkronExtendedVariantOption option)
    {
        return option != null && !option.IsDefault && !option.IsMapDefined;
    }

    private static AkronStatus Max(AkronStatus left, AkronStatus right)
    {
        return left > right ? left : right;
    }
}
