using System;
using System.Collections.Generic;
using System.Reflection;
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
    [InlineData(AkronFeatureKind.CustomHudLabels, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.InternalRecorder, AkronStatus.GoldberryHardlistClean)]
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
    [InlineData(AkronFeatureKind.SpeedWidget, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.DashWidget, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.ReducedVisualNoise, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.GrabModeHotkey, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.ScreenshotTool, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.RetryHotkey, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.RoomReload, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.ChapterReload, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.DebugMapLauncher, AkronStatus.Cheat)]
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
    [InlineData(AkronFeatureKind.CursorTools, AkronStatus.Cheat)]
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
    [InlineData(AkronFeatureKind.ExtendedVariantMode, AkronStatus.GoldberryHardlistClean)]
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
    [InlineData(AkronFeatureKind.Logging, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.JournalSnapshotCompare, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.Backups, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.DisablePlayback, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.EntitySpawn, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.PauseBuffering, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.CutsceneSkip, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.MadelineColors, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.AttemptsLabel, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.StatusLabels, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.PracticeCounters, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.RoomStatTracker, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.CaptureCheatOptions, AkronStatus.Cheat)]
    [InlineData(AkronFeatureKind.Autosave, AkronStatus.GoldberryHardlistClean)]
    [InlineData(AkronFeatureKind.DeathHitboxes, AkronStatus.RegularClean)]
    [InlineData(AkronFeatureKind.SoundVolumeOverride, AkronStatus.RegularClean)]
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
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void LabelFeaturesAreActiveOnlyWhenTheirLabelAndMasterSwitchAreEnabled(
        bool labelsVisible,
        bool labelEnabled,
        bool expectedActive)
    {
        Assert.Equal(expectedActive, AkronPolicy.IsLabelFeatureActive(labelsVisible, labelEnabled));
    }

    [Fact]
    public void ExtendedVariantPolicyFlagsOnlyUserControlledVariantOptions()
    {
        Assert.False(AkronPolicy.ShouldFlagExtendedVariantOption(new AkronExtendedVariantOption
        {
            Label = "Map-controlled variant",
            IsDefault = false,
            IsMapDefined = true
        }));
        Assert.False(AkronPolicy.ShouldFlagExtendedVariantOption(new AkronExtendedVariantOption
        {
            Label = "Default variant",
            IsDefault = true,
            IsMapDefined = false
        }));
        Assert.True(AkronPolicy.ShouldFlagExtendedVariantOption(new AkronExtendedVariantOption
        {
            Label = "User override",
            IsDefault = false,
            IsMapDefined = false
        }));
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
            TransitionSpeedEnabled = true,
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

    // A row's badge is its feature kind's class, and the kind is what the feature records
    // through TryUse. There is no second table a row could take a class from, so a row with
    // no kind shows no class. Every row listed here is either a recording feature (kind) or
    // a control with no attempt impact (null); a new row that shows a class must appear here
    // with the kind that records it.
    [Theory]
    [InlineData("Global", "Timescale", AkronFeatureKind.Timescale)]
    [InlineData("Global", "Transition Speed", AkronFeatureKind.TransitionSpeed)]
    [InlineData("Global", "Frame Stepper", AkronFeatureKind.FrameAdvance)]
    [InlineData("Global", "Safe Mode", null)]
    [InlineData("Global", "Freeze Attempts", AkronFeatureKind.SafeModeStats)]
    [InlineData("Global", "Submission Mode", AkronFeatureKind.SubmissionMode)]
    [InlineData("Global", "Pause Buffering", AkronFeatureKind.PauseBuffering)]
    [InlineData("Global", "Autosave", AkronFeatureKind.Autosave)]
    [InlineData("Global", "Defer Engine GC", null)]
    [InlineData("Level", "Core Mode", AkronFeatureKind.MovementStatMutation)]
    [InlineData("Level", "Freeze Gameplay", AkronFeatureKind.Freeze)]
    [InlineData("Level", "Confirm Actions", null)]
    [InlineData("Level", "Auto Kill", AkronFeatureKind.AutoKill)]
    [InlineData("Level", "Auto Deafen", AkronFeatureKind.AutoDeafen)]
    [InlineData("Level", "Deload Spinners", AkronFeatureKind.DeloadSimulation)]
    [InlineData("Level", "Show Hitboxes", AkronFeatureKind.HitboxViewer)]
    [InlineData("Level", "Fix Hitbox Pixels", null)]
    [InlineData("Level", "Show Hitboxes On Death", AkronFeatureKind.DeathHitboxes)]
    [InlineData("Level", "Reduced Visual Noise", AkronFeatureKind.ReducedVisualNoise)]
    [InlineData("Level", "Hide Vanilla HUD", AkronFeatureKind.HudVisibility)]
    [InlineData("Level", "Hide Akron HUD", AkronFeatureKind.HudVisibility)]
    [InlineData("Level", "Hide Snow", AkronFeatureKind.ReducedVisualNoise)]
    [InlineData("Level", "Hide Wind Snow", AkronFeatureKind.ReducedVisualNoise)]
    [InlineData("Level", "Hide Waterfalls", AkronFeatureKind.ReducedVisualNoise)]
    [InlineData("Level", "Hide Tentacles", AkronFeatureKind.ReducedVisualNoise)]
    [InlineData("Level", "Hide Heat Distortion", AkronFeatureKind.ReducedVisualNoise)]
    [InlineData("Level", "Disable Playback", AkronFeatureKind.DisablePlayback)]
    [InlineData("StartPos", "Smart StartPos", AkronFeatureKind.StartPosTools)]
    [InlineData("StartPos", "StartPos Slot", AkronFeatureKind.StartPosTools)]
    [InlineData("StartPos", "Respawn at StartPos", AkronFeatureKind.StartPosTools)]
    [InlineData("Backups", "Enabled", AkronFeatureKind.Backups)]
    [InlineData("Backups", "Restore", AkronFeatureKind.Backups)]
    [InlineData("Bypass", "Instant Complete", AkronFeatureKind.InstantComplete)]
    [InlineData("Bypass", "Uncomplete Level", AkronFeatureKind.UnlockSystem)]
    [InlineData("Bypass", "Unlock Golden Berries", AkronFeatureKind.UnlockSystem)]
    [InlineData("Bypass", "Obtain Room Berries", AkronFeatureKind.UnlockSystem)]
    [InlineData("Bypass", "Berry Obtain Options", null)]
    [InlineData("Player", "Set Inventory", AkronFeatureKind.MovementStatMutation)]
    [InlineData("Player", "Dream State", AkronFeatureKind.MovementStatMutation)]
    [InlineData("Player", "Golden Start", AkronFeatureKind.GoldenStartHelper)]
    [InlineData("Player", "Dash Bar", AkronFeatureKind.ResourceBars)]
    [InlineData("Player", "Dash Number", AkronFeatureKind.ResourceBars)]
    [InlineData("Player", "Stamina Bar", AkronFeatureKind.ResourceBars)]
    [InlineData("Player", "Madeline Colors", AkronFeatureKind.MadelineColors)]
    [InlineData("Player", "Trail Visibility", AkronFeatureKind.CustomTrail)]
    [InlineData("Player", "No Trails", AkronFeatureKind.ReducedVisualNoise)]
    [InlineData("Player", "No Stamina Flash", AkronFeatureKind.ReducedVisualNoise)]
    [InlineData("Sound", "Audio Splitter", null)]
    [InlineData("Sound", "Allow Low Volume", AkronFeatureKind.LowVolumeBypass)]
    [InlineData("Sound", "Player", null)]
    [InlineData("Creator", "Entity Inspector", AkronFeatureKind.EntityInspector)]
    [InlineData("Creator", "Warp Selected Room", AkronFeatureKind.RoomWarp)]
    [InlineData("Creator", "Previous Room", null)]
    [InlineData("Creator", "Next Room", null)]
    [InlineData("Creator", "Previous Room In Order", AkronFeatureKind.RoomWarp)]
    [InlineData("Creator", "Next Checkpoint", AkronFeatureKind.RoomWarp)]
    [InlineData("Creator", "Previous Map", AkronFeatureKind.RoomWarp)]
    [InlineData("Creator", "Open Debug Map", AkronFeatureKind.DebugMapLauncher)]
    [InlineData("Creator", "Room Capture", AkronFeatureKind.ScreenshotTool)]
    [InlineData("Creator", "Map Capture", AkronFeatureKind.ScreenshotTool)]
    [InlineData("Creator", "Export Room Stats", AkronFeatureKind.SplitHelper)]
    [InlineData("Creator", "Export Room Times", AkronFeatureKind.SplitHelper)]
    [InlineData("Labels", "Visible", null)]
    [InlineData("Labels", "Player Overlap", null)]
    [InlineData("Labels", "Death Stats", AkronFeatureKind.DeathStats)]
    [InlineData("Labels", "Stamina Widget", AkronFeatureKind.StaminaWidget)]
    [InlineData("Labels", "Speed Widget", AkronFeatureKind.SpeedWidget)]
    [InlineData("Labels", "Dash Widget", AkronFeatureKind.DashWidget)]
    [InlineData("Labels", "Room", AkronFeatureKind.RoomLabelOverlay)]
    [InlineData("Labels", "Status", AkronFeatureKind.StatusLabels)]
    [InlineData("Labels", "Toasts", null)]
    [InlineData("Labels", "Cheat Indicator", null)]
    [InlineData("Labels", "Input History", AkronFeatureKind.InputHistory)]
    [InlineData("Labels", "Inputs per second", AkronFeatureKind.InputsPerSecondCounter)]
    [InlineData("Labels", "Dash Stats", AkronFeatureKind.PracticeCounters)]
    [InlineData("Labels", "Jump Stats", AkronFeatureKind.PracticeCounters)]
    [InlineData("Labels", "StartPos HUD", AkronFeatureKind.StartPosTools)]
    [InlineData("Labels", "Room Timer", AkronFeatureKind.RoomTimer)]
    [InlineData("Labels", "Room Stat Tracker", AkronFeatureKind.RoomStatTracker)]
    [InlineData("Labels", "Attempts", AkronFeatureKind.AttemptsLabel)]
    [InlineData("Labels", "No Short Numbers", null)]
    [InlineData("Labels", "+ Custom", AkronFeatureKind.CustomHudLabels)]
    [InlineData("Shortcuts", "Open Options", null)]
    [InlineData("Shortcuts", "Retry", AkronFeatureKind.RetryHotkey)]
    [InlineData("Shortcuts", "Reload Room", AkronFeatureKind.RoomReload)]
    [InlineData("Shortcuts", "Reload Chapter", AkronFeatureKind.ChapterReload)]
    [InlineData("Shortcuts", "Spawn Jelly", AkronFeatureKind.EntitySpawn)]
    [InlineData("Shortcuts", "Spawn Theo", AkronFeatureKind.EntitySpawn)]
    [InlineData("Shortcuts", "Neutral Drop", AkronFeatureKind.InputAssistShortcut)]
    [InlineData("Shortcuts", "Backboost", AkronFeatureKind.InputAssistShortcut)]
    [InlineData("Shortcuts", "Skip Cutscene", AkronFeatureKind.CutsceneSkip)]
    [InlineData("Interface", "Theme", null)]
    [InlineData("Interface", "Export Setup", null)]
    [InlineData("Interface", "Import Setup", null)]
    [InlineData("Interface", "Community Packs", null)]
    [InlineData("Interface", "Upload Pack", null)]
    [InlineData("Interface", "Streamer Mode", null)]
    [InlineData("Interface", "Block Gameplay Input", null)]
    [InlineData("Interface", "Logging", AkronFeatureKind.Logging)]
    [InlineData("Internal Recorder", "Start Recording", AkronFeatureKind.InternalRecorder)]
    [InlineData("Internal Recorder", "Build Clear Video", AkronFeatureKind.InternalRecorder)]
    [InlineData("Internal Recorder", "Journal Snapshot / Compare", AkronFeatureKind.JournalSnapshotCompare)]
    [InlineData("Internal Recorder", "Framerate", AkronFeatureKind.InternalRecorder)]
    [InlineData("Internal Recorder", "Presets", AkronFeatureKind.InternalRecorder)]
    [InlineData("Speedrun Tool", "SRT Status", null)]
    [InlineData("Speedrun Tool", "SRT Capture State", AkronFeatureKind.BrokeredSavestates)]
    [InlineData("CelesteTAS", "Play Configured TAS", AkronFeatureKind.TasHandoff)]
    [InlineData("Extended Camera Dynamics", "ECD Zoom Out", AkronFeatureKind.CursorZoom)]
    [InlineData("Extended Camera Dynamics", "ECD Restore Zooming", null)]
    [InlineData("Extended Variant Mode", "Extended Variants Master", AkronFeatureKind.ExtendedVariantMode)]
    [InlineData("Extended Variant Mode", "Reset Extended", null)]
    public void RowsClassifyThroughTheFeatureKindThatRecordsThem(string tab, string label, AkronFeatureKind? expectedKind)
    {
        Dictionary<string, AkronFeatureKind?> kinds = BuildOverlayEntryFeatureKinds(tab);

        Assert.True(kinds.ContainsKey(label), $"{tab} has no row named {label}.");
        Assert.Equal(expectedKind, kinds[label]);
    }

    [Fact]
    public void TheLabelClassificationTableIsGone()
    {
        Assert.Null(typeof(AkronFeatureRegistry).GetMethod("TryClassifyUiLabel"));
    }

    private static Dictionary<string, AkronFeatureKind?> BuildOverlayEntryFeatureKinds(string tab)
    {
        MethodInfo method = typeof(AkronOverlay).GetMethod("BuildDisplayEntriesForTab", BindingFlags.NonPublic | BindingFlags.Static)!;
        object entries = method.Invoke(null, new object?[] { tab, null })!;
        Type entryType = entries.GetType().GetGenericArguments()[0];
        PropertyInfo labelProperty = entryType.GetProperty("Label", BindingFlags.Public | BindingFlags.Instance)!;
        PropertyInfo featureKindProperty = entryType.GetProperty("FeatureKind", BindingFlags.Public | BindingFlags.Instance)!;
        Dictionary<string, AkronFeatureKind?> kinds = new Dictionary<string, AkronFeatureKind?>(StringComparer.OrdinalIgnoreCase);
        foreach (object entry in (System.Collections.IEnumerable)entries)
        {
            // Built-in rows come first; a shipped custom label named like one must not shadow it.
            kinds.TryAdd((string)labelProperty.GetValue(entry)!, (AkronFeatureKind?)featureKindProperty.GetValue(entry));
        }

        return kinds;
    }

    [Theory]
    [InlineData("Safe Mode", "Freeze deaths", AkronStatus.Cheat)]
    [InlineData("Death Stats", "PB loss prompt", AkronStatus.RegularClean)]
    [InlineData("Input History", "Input history", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Input History", "Rows", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Input History", "Pin on death", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Input History", "Show on death", AkronStatus.GoldberryHardlistClean)]
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
    [InlineData("Autosave", "Interval", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Autosave", "Avoid gameplay", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Autosave", "Save now", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Triggers", "On launch", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Triggers", "Timed", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Retention", "Max count", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Retention", "Keep at least", AkronStatus.GoldberryHardlistClean)]
    [InlineData("Timescale", "Enabled", AkronStatus.Cheat)]
    [InlineData("Timescale", "Reset", AkronStatus.Cheat)]
    [InlineData("Transition Speed", "Multiplier", AkronStatus.Cheat)]
    [InlineData("Transition Speed", "Speed", AkronStatus.Cheat)]
    [InlineData("Room Capture", "Freeze time", AkronStatus.Cheat)]
    [InlineData("Room Capture", "Noclip + hide Madeline", AkronStatus.Cheat)]
    [InlineData("Map Capture", "Noclip + hide Madeline", AkronStatus.Cheat)]
    public void UiSuboptionsCanOverrideParentClassification(string parentLabel, string suboptionLabel, AkronStatus expectedStatus)
    {
        Assert.True(AkronFeatureRegistry.TryClassifyUiSuboption(parentLabel, suboptionLabel, out AkronStatus status));
        Assert.Equal(expectedStatus, status);
    }
}
