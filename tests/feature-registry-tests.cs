using System;
using System.Collections.Generic;
using Celeste.Mod.Akron;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class FeatureRegistryTests
{
    [Fact]
    public void EveryFeatureKindHasACompleteDefinition()
    {
        foreach (AkronFeatureKind kind in Enum.GetValues<AkronFeatureKind>())
        {
            FeatureDefinition definition = AkronFeatureRegistry.Get(kind);

            Assert.Equal(kind, definition.Kind);
            Assert.True(Enum.IsDefined(definition.Classification), $"{kind} has an invalid classification.");
            Assert.False(string.IsNullOrWhiteSpace(definition.Label), $"{kind} must have a UI label.");
            Assert.False(string.IsNullOrWhiteSpace(definition.Reason), $"{kind} must explain its policy impact.");
        }
    }

    [Fact]
    public void ClassificationOrderKeepsAttemptEscalationMonotonic()
    {
        Assert.True(AkronStatus.Unclassified < AkronStatus.GoldberryHardlistClean);
        Assert.True(AkronStatus.GoldberryHardlistClean < AkronStatus.RegularClean);
        Assert.True(AkronStatus.RegularClean < AkronStatus.Cheat);
    }

    [Theory]
    [InlineData(AkronFeatureKind.InputViewer, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.InputHistory, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.DeathStats, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.Screenshake, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.ShowTaps, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.RoomLabelOverlay, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.RoomTimer, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.RefillClarity, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.VisualTuning, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.PitchShift, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.CustomHudLabels, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.InternalRecorder, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.StaminaWidget, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.ResourceBars, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.SpeedNumber, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.Savestates, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.HitboxViewer, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.StartPosTools, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.ShowTrajectory, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.FpsBypass, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.TpsBypass, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.Noclip, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.FreezeFrames, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.GroundRefillRules, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.PauseTimerFreeze, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.InputAssistShortcut, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.CursorZoom, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.UnsafeNativeSavestateOverride, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.SpeedWidget, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.DashWidget, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.ReducedVisualNoise, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.GrabModeHotkey, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.ScreenshotTool, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.RetryHotkey, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.RoomReload, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.ChapterReload, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.DebugMapLauncher, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.MountainViewer, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.BrokeredSavestates, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.TasHandoff, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.SplitHelper, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.DeloadSimulation, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.RoomWarp, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.EntityInspector, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.FlagInspector, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.RespawnTime, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.FrameAdvance, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.Freeze, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.Timescale, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.AutoKill, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.AutoDeafen, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.TransitionSpeed, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.LowVolumeBypass, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.HudVisibility, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.PauseMenuVisibility, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.PauseCountdown, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.FreeCamera, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.AudioSpeed, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.SafeModeStats, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.TriggerViewer, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.ClickTeleport, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.CustomTrail, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.MadelineHairLength, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.MadelineEffectSync, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.HidePlayer, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.DeathVisuals, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.RespawnAnimation, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.InputsPerSecondCounter, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.InstantComplete, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.UnlockSystem, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.HazardAccuracy, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.Invincibility, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.InfiniteStamina, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.InfiniteDash, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.DashCountOverride, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.MovementStatMutation, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.ExtendedVariantMode, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.FastLookout, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.LevelEnterSkip, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.DeathPbLossRestart, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.CameraOffset, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.SubmissionMode, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.ProofRecorderGuard, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.EndScreenHelper, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.PauseTracker, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.MapVersionStamp, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.GoldenStartHelper, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.GoldenTransparency, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.LagPauser, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.Logging, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.JournalSnapshotCompare, AkronStatus.GoldberryHardlistClean)]
    public void CheatReferenceClassifiesEveryFeatureKind(AkronFeatureKind kind, AkronStatus expectedStatus)
    {
        Assert.Equal(expectedStatus, AkronFeatureRegistry.Classify(kind));
    }

    [Theory]
    [InlineData(AkronStatus.Unclassified, false)]
    [InlineData(AkronStatus.GoldberryHardlistClean, false)]
    [InlineData(AkronStatus.RegularClean, false)]
    [InlineData(AkronStatus.Cheat, true)]
    public void MegaHackStyleCheatIndicatorOnlyFlagsCheatStatus(AkronStatus status, bool expectedFlagged)
    {
        Assert.Equal(expectedFlagged, AkronPolicy.IsMegaHackStyleCheatIndicatorFlagged(status));
    }

    [Theory]
    [InlineData(AkronStatus.Unclassified, 0x909090)]
    [InlineData(AkronStatus.GoldberryHardlistClean, 0x248BFF)]
    [InlineData(AkronStatus.RegularClean, 0x00FF00)]
    [InlineData(AkronStatus.Cheat, 0xFF0000)]
    public void StatusColorsUseRulebookPalette(AkronStatus status, int expectedRgb)
    {
        Assert.Equal(expectedRgb, AkronPolicy.GetStatusColorRgb(status));
    }

    [Fact]
    public void SafeModeOnlyRedactsGoldberryHardlistCleanColor()
    {
        Assert.Equal(0x909090, AkronPolicy.GetStatusColorRgb(AkronStatus.Unclassified, safeModeRedactsCleanStatus: true));
        Assert.Equal(0x6495ED, AkronPolicy.GetStatusColorRgb(AkronStatus.GoldberryHardlistClean, safeModeRedactsCleanStatus: true));
        Assert.Equal(0x00FF00, AkronPolicy.GetStatusColorRgb(AkronStatus.RegularClean, safeModeRedactsCleanStatus: true));
        Assert.Equal(0xFF0000, AkronPolicy.GetStatusColorRgb(AkronStatus.Cheat, safeModeRedactsCleanStatus: true));
    }

    [Theory]
    [InlineData(AkronStatus.Unclassified, "No Akron attempt classification has been selected or earned yet.", "Gray because the current attempt is Unclassified.")]
    [InlineData(AkronStatus.GoldberryHardlistClean, "No modifying Akron feature has been used in this attempt.", "Blue because the current attempt is Goldberry/Hardlist clear.")]
    [InlineData(AkronStatus.RegularClean, "Displays a label without changing gameplay.", "Green because the current attempt is Normal clear.")]
    [InlineData(AkronStatus.Cheat, "Bypasses collision and intended map traversal.", "Red because the current attempt is Cheat.")]
    public void StatusColorExplanationNamesColorStatusAndReason(AkronStatus status, string reason, string expectedPrefix)
    {
        string explanation = AkronPolicy.DescribeStatusColorReason(status, reason);

        Assert.StartsWith(expectedPrefix, explanation);
        Assert.Contains(reason, explanation);
    }

    [Fact]
    public void SafeModeStatusColorExplanationRedactsGoldberryHardlistCleanLabel()
    {
        string explanation = AkronPolicy.DescribeStatusColorReason(
            AkronStatus.GoldberryHardlistClean,
            "No modifying Akron feature has been used in this attempt.",
            safeModeRedactsCleanStatus: true);

        Assert.StartsWith("Blue because the current attempt is Safe mode.", explanation);
    }

    [Fact]
    public void ActiveCheatContributorsNameEnabledCheatTogglesAndDisableCommands()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            Noclip = true,
            AutoDeafen = true,
            RoomTimerWidget = true
        };

        IReadOnlyList<AkronActiveCheatContributor> contributors = AkronPolicy.GetActiveCheatContributors(settings);

        AkronActiveCheatContributor contributor = Assert.Single(contributors);
        Assert.Equal("Noclip", contributor.Label);
        Assert.Equal("Turn off Noclip", contributor.DisableCommand);
        Assert.Equal(AkronFeatureKind.Noclip, contributor.Feature);
    }

    [Fact]
    public void DefaultSettingsHaveNoActiveCheatContributors()
    {
        IReadOnlyList<AkronActiveCheatContributor> contributors = AkronPolicy.GetActiveCheatContributors(new AkronModuleSettings());

        Assert.Empty(contributors);
    }

    [Fact]
    public void ShowHitboxesOnDeathDoesNotContributeWithoutLiveHitboxes()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            HitboxShowLastDeath = true
        };

        IReadOnlyList<AkronActiveCheatContributor> contributors = AkronPolicy.GetActiveCheatContributors(settings);

        Assert.Empty(contributors);
    }

    [Fact]
    public void HitboxRenderingStyleDoesNotContributeWithoutLiveHitboxes()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            FixHitboxPixels = true
        };

        IReadOnlyList<AkronActiveCheatContributor> contributors = AkronPolicy.GetActiveCheatContributors(settings);

        Assert.Empty(contributors);
    }

    [Fact]
    public void LiveHitboxesRemainTheSingleHitboxCheatContributor()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            HitboxViewer = true,
            FixHitboxPixels = true,
            HitboxShowLastDeath = true
        };

        IReadOnlyList<AkronActiveCheatContributor> contributors = AkronPolicy.GetActiveCheatContributors(settings);

        AkronActiveCheatContributor contributor = Assert.Single(contributors);
        Assert.Equal("Show Hitboxes", contributor.Label);
        Assert.Equal(AkronFeatureKind.HitboxViewer, contributor.Feature);
    }

    [Fact]
    public void ActiveCheatContributorsIncludeSessionOwnedCheatState()
    {
        AkronModuleSettings settings = new AkronModuleSettings();
        AkronModuleSession session = new AkronModuleSession
        {
            TimescaleEnabled = true,
            TimescaleMultiplier = 0.5f
        };

        IReadOnlyList<AkronActiveCheatContributor> contributors = AkronPolicy.GetActiveCheatContributors(settings, session);

        AkronActiveCheatContributor contributor = Assert.Single(contributors);
        Assert.Equal("Timescale", contributor.Label);
        Assert.Equal(AkronFeatureKind.Timescale, contributor.Feature);
    }

    [Theory]
    [InlineData("Dash Number")]
    [InlineData("Resource Bars")]
    [InlineData("Freeze deaths")]
    [InlineData("Freeze jumps")]
    [InlineData("Freeze best run")]
    [InlineData("Transition Speed")]
    public void ActiveCheatContributorsIncludeRedOptionsThatHaveIndependentToggles(string expectedLabel)
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            DashNumber = true,
            ResourceBars = true,
            SafeModeFreezeAttempts = true,
            SafeModeFreezeJumps = true,
            SafeModeFreezeBestRun = true,
            TransitionSpeedMultiplier = 0.5f
        };

        IReadOnlyList<AkronActiveCheatContributor> contributors = AkronPolicy.GetActiveCheatContributors(settings);

        Assert.Contains(contributors, contributor => contributor.Label == expectedLabel);
    }

    [Fact]
    public void ActiveCheatContributorsIgnoreEnabledGreenQualityOfLifeOptions()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            AudioSpeed = true,
            DeathPbLossPrompt = true,
            GrabModeOverrideEnabled = true,
            HidePauseMenu = true,
            NoDeathEffect = true,
            NoDeathWipe = true,
            NoRespawnAnimation = true,
            RespawnTimeModifier = true
        };

        IReadOnlyList<AkronActiveCheatContributor> contributors = AkronPolicy.GetActiveCheatContributors(settings);

        Assert.Empty(contributors);
    }

    [Theory]
    [InlineData("Safe Mode", AkronStatus.RegularClean)]
    [InlineData("Pause Buffering", AkronStatus.Cheat)]
    [InlineData("Death Stats", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Input History", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Stamina Bar", AkronStatus.Cheat)]
    [InlineData("Dash Number", AkronStatus.Cheat)]
    [InlineData("Dash Stats", AkronStatus.RegularClean)]
    [InlineData("Berry Obtain Options", AkronStatus.RegularClean)]
    [InlineData("Previous Room", AkronStatus.RegularClean)]
    [InlineData("Next Room", AkronStatus.RegularClean)]
    [InlineData("Reload Room", AkronStatus.Cheat)]
    [InlineData("Allow Low Volume", AkronStatus.RegularClean)]
    [InlineData("Reduced Visual Noise", AkronStatus.RegularClean)]
    [InlineData("Madeline Hair Length", AkronStatus.RegularClean)]
    [InlineData("Madeline Effect Sync", AkronStatus.RegularClean)]
    [InlineData("No Stamina Flash", AkronStatus.RegularClean)]
    [InlineData("No Particles", AkronStatus.RegularClean)]
    [InlineData("No Trails", AkronStatus.RegularClean)]
    [InlineData("No Glitch", AkronStatus.RegularClean)]
    [InlineData("No Anxiety", AkronStatus.RegularClean)]
    [InlineData("No Distortion", AkronStatus.RegularClean)]
    [InlineData("Hide Snow", AkronStatus.RegularClean)]
    [InlineData("Hide Wind Snow", AkronStatus.RegularClean)]
    [InlineData("Hide Waterfalls", AkronStatus.RegularClean)]
    [InlineData("Hide Tentacles", AkronStatus.RegularClean)]
    [InlineData("Hide Heat Distortion", AkronStatus.RegularClean)]
    [InlineData("Fix Hitbox Pixels", AkronStatus.RegularClean)]
    [InlineData("Show Hitboxes On Death", AkronStatus.RegularClean)]
    [InlineData("Room Timer", AkronStatus.RegularClean)]
    [InlineData("Extended Variants Master", AkronStatus.Cheat)]
    [InlineData("Reset Extended", AkronStatus.RegularClean)]
    [InlineData("Reset Vanilla", AkronStatus.RegularClean)]
    [InlineData("Submission Mode", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Proof Recorder Guard", AkronStatus.GoldberryHardlistClean)]
    [InlineData("End Screen Helper", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Pause Tracker", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Map Version Stamp", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Golden Start", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Golden Transparency", AkronStatus.RegularClean)]
    [InlineData("Streamer Mode", AkronStatus.RegularClean)]
    [InlineData("Lag Pauser", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Logging", AkronStatus.RegularClean)]
    [InlineData("Journal Snapshot / Compare", AkronStatus.GoldberryHardlistClean)]
    [InlineData("SRT Status", AkronStatus.RegularClean)]
    [InlineData("SRT Slot", AkronStatus.RegularClean)]
    [InlineData("SRT Capture State", AkronStatus.Cheat)]
    [InlineData("SRT Restore State", AkronStatus.Cheat)]
    [InlineData("SRT Clear State", AkronStatus.Cheat)]
    [InlineData("SRT Room Time", AkronStatus.RegularClean)]
    [InlineData("TAS Status", AkronStatus.RegularClean)]
    [InlineData("Configured TAS File", AkronStatus.RegularClean)]
    [InlineData("Play Configured TAS", AkronStatus.Cheat)]
    public void UiRowsWithoutFeatureKindsStillExposeClassification(string label, AkronStatus expectedStatus)
    {
        Assert.True(AkronFeatureRegistry.TryClassifyUiLabel(label, out AkronStatus status));
        Assert.Equal(expectedStatus, status);
    }

    [Theory]
    [InlineData("Safe Mode", "Freeze deaths", AkronStatus.Cheat)]
    [InlineData("Death Stats", "PB loss prompt", AkronStatus.RegularClean)]
    [InlineData("Input History", "Input history", AkronStatus.RegularClean)]
    [InlineData("Input History", "Rows", AkronStatus.RegularClean)]
    [InlineData("Input History", "Pin on death", AkronStatus.RegularClean)]
    [InlineData("Input History", "Show on death", AkronStatus.RegularClean)]
    [InlineData("FPS Bypass", "Target FPS", AkronStatus.RegularClean)]
    [InlineData("FPS Bypass", "Method", AkronStatus.RegularClean)]
    [InlineData("FPS Bypass", "Smooth Camera", AkronStatus.RegularClean)]
    [InlineData("FPS Bypass", "Objects", AkronStatus.RegularClean)]
    [InlineData("FPS Bypass", "Objects: Extrapolate", AkronStatus.RegularClean)]
    [InlineData("FPS Bypass", "Objects: Interpolate", AkronStatus.Cheat)]
    [InlineData("FPS Bypass", "TAS mode", AkronStatus.Cheat)]
    [InlineData("FPS Bypass", "Subpixel Madeline", AkronStatus.RegularClean)]
    [InlineData("FPS Bypass", "Smooth background", AkronStatus.RegularClean)]
    [InlineData("FPS Bypass", "Smooth foreground", AkronStatus.RegularClean)]
    [InlineData("FPS Bypass", "Hide edge gaps", AkronStatus.RegularClean)]
    [InlineData("FPS Bypass", "Nasty mode", AkronStatus.Cheat)]
    [InlineData("TPS Bypass", "Target TPS", AkronStatus.Cheat)]
    [InlineData("Room Capture", "Freeze timers", AkronStatus.Cheat)]
    public void UiSuboptionsCanOverrideParentClassification(string parentLabel, string suboptionLabel, AkronStatus expectedStatus)
    {
        Assert.True(AkronFeatureRegistry.TryClassifyUiSuboption(parentLabel, suboptionLabel, out AkronStatus status));
        Assert.Equal(expectedStatus, status);
    }
}
