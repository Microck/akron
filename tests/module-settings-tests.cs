using Celeste;
using Celeste.Mod.Akron;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using XnaButtons = Microsoft.Xna.Framework.Input.Buttons;

namespace Celeste.Mod.Akron.Tests;

public sealed class ModuleSettingsTests
{
    [Theory]
    [InlineData(AkronStatus.Unclassified, "Unclassified")]
    [InlineData(AkronStatus.GoldberryHardlistClean, "Goldberry/Hardlist clear")]
    [InlineData(AkronStatus.RegularClean, "Normal clear")]
    [InlineData(AkronStatus.Cheat, "Cheat")]
    public void StatusLabelsMatchPublicUiContract(AkronStatus status, string expected)
    {
        Assert.Equal(expected, AkronModuleSettings.FormatStatus(status));
    }

    [Theory]
    [InlineData(-20, 240)]
    [InlineData(0, 240)]
    [InlineData(1, 20)]
    [InlineData(250, 250)]
    [InlineData(1200, 900)]
    public void NoclipSpeedClampUsesDefaultAndBounds(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampNoclipSpeed(input));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(12, 12)]
    [InlineData(1200, 999)]
    public void NoclipAccuracyInvalidLimitClampUsesZeroAsDisabled(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampNoclipAccuracyInvalidLimit(input));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(180, 180)]
    [InlineData(250, 250)]
    [InlineData(6000, 5000)]
    public void NoclipAccuracyTintDurationClampUsesDefaultAndBounds(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampNoclipAccuracyTintDurationMs(input));
    }

    [Fact]
    public void HazardAccuracyTintDefaultsUsePlayableFeedbackByDefault()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.True(settings.NoclipAccuracyTint);
        Assert.Equal(AkronNoclipAccuracyTintMode.WhileTouching, settings.NoclipAccuracyTintMode);
        Assert.Equal(AkronModuleSettings.DefaultHazardAccuracyTintColor, settings.NoclipAccuracyTintColor);
        Assert.Equal(AkronModuleSettings.DefaultHazardAccuracyTintOpacity, settings.NoclipAccuracyTintOpacity);
        Assert.Equal(30, settings.NoclipAccuracyTintOpacity);
        Assert.Equal(AkronModuleSettings.DefaultHazardAccuracyTintDurationMs, settings.NoclipAccuracyTintDurationMs);
        Assert.Equal(250, settings.NoclipAccuracyTintDurationMs);
    }

    [Fact]
    public void InvincibilityDefaultsUseAkronModeWithNativeLikeEffectsEnabled()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.False(settings.Invincibility);
        Assert.Equal(AkronInvincibilityMode.Akron, settings.InvincibilityMode);
        Assert.True(settings.InvincibilityBottomlessFallRescue);
        Assert.True(settings.InvincibilityCrushCollisionChanges);
        Assert.True(settings.InvincibilityLavaIcePushback);
        Assert.True(settings.InvincibilitySpikeGroundRefills);
    }

    [Theory]
    [InlineData(AkronInvincibilityMode.Akron, AkronInvincibilityMode.Akron)]
    [InlineData(AkronInvincibilityMode.Native, AkronInvincibilityMode.Native)]
    [InlineData((AkronInvincibilityMode) 99, AkronInvincibilityMode.Akron)]
    public void InvincibilityModeNormalizationFallsBackToAkron(AkronInvincibilityMode input, AkronInvincibilityMode expected)
    {
        Assert.Equal(expected, AkronModuleSettings.NormalizeInvincibilityMode(input));
    }

    [Fact]
    public void InputsPerSecondDefaultsAreAVisualCounterOnly()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.False(settings.InputsPerSecondCounter);
        Assert.Equal(AkronHudPlacement.Left, settings.InputsPerSecondPlacement);
        Assert.Equal(100, settings.InputsPerSecondScale);
        Assert.Equal(90, settings.InputsPerSecondOpacity);
        Assert.Equal(0xFFFFFF, settings.InputsPerSecondTextColor);
        Assert.True(settings.InputsPerSecondShowTotal);
        Assert.True(settings.InputsPerSecondShowMax);
        Assert.True(settings.InputsPerSecondCountMovement);
        Assert.True(settings.InputsPerSecondCountActions);
        Assert.False(settings.InputsPerSecondCountMenu);
    }

    [Fact]
    public void DeathStatsDefaultsUseBoundedFormatAndDeathMenuVisibility()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.Equal(AkronModuleSettings.DefaultDeathStatsFormat, settings.DeathStatsFormat);
        Assert.Equal(AkronDeathStatsVisibility.AfterDeathAndInMenu, settings.DeathStatsVisibility);
        Assert.False(settings.DeathPbLossPrompt);
    }

    [Theory]
    [InlineData("", "$C ($B)")]
    [InlineData("{0}/{1}", "$C/$B")]
    [InlineData("deaths $C best $B", "deaths $C best $B")]
    public void DeathStatsFormatNormalizationKeepsPublicTokens(string input, string expected)
    {
        Assert.Equal(expected, AkronModuleSettings.NormalizeDeathStatsFormat(input));
    }

    [Fact]
    public void DeathStatsFormatterReplacesAkronOwnedTokens()
    {
        string text = AkronHudRenderer.FormatDeathStatsText("$C/$B/$A/$T/$L/$S", 2, "5", 10, 99, 3, 1);

        Assert.Equal("2/5/10/99/3/1", text);
    }

    [Theory]
    [InlineData(4, 1, false, 5)]
    [InlineData(5, 1, true, 5)]
    [InlineData(0, 1, true, 1)]
    public void DeathStatsMapTotalAvoidsTransitionDoubleCount(int sessionDeaths, int roomDeaths, bool deathTransitionActive, int expected)
    {
        Assert.Equal(expected, AkronHudRenderer.GetSessionMapDeathTotal(sessionDeaths, roomDeaths, deathTransitionActive));
    }

    [Theory]
    [InlineData(100, 200, 200, 160, 308, 200)]
    [InlineData(1720, 200, 200, 160, 1512, 200)]
    [InlineData(100, 990, 200, 160, 308, 904)]
    public void AnchoredPopupPlacementStaysBesideRowWithMinimalVerticalShift(int anchorX, int anchorY, int popupWidth, int popupHeight, float expectedX, float expectedY)
    {
        (float x, float y) = AkronOverlay.CalculateAnchoredPopupPosition(
            anchorX,
            anchorY,
            200,
            popupWidth,
            popupHeight,
            1920,
            1080);

        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    [Theory]
    [InlineData(100, 100, 500, 160, 360, 240, 16, 64)]
    [InlineData(1720, 1060, 220, 160, 1920, 1080, 1492, 904)]
    public void AnchoredPopupPlacementKeepsViewportMargin(int anchorX, int anchorY, int popupWidth, int popupHeight, int displayWidth, int displayHeight, float expectedX, float expectedY)
    {
        (float x, float y) = AkronOverlay.CalculateAnchoredPopupPosition(
            anchorX,
            anchorY,
            200,
            popupWidth,
            popupHeight,
            displayWidth,
            displayHeight);

        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
        Assert.True(x >= 16f);
        Assert.True(y >= 16f);
        Assert.True(x + popupWidth <= displayWidth - 16f || popupWidth > displayWidth - 32f);
        Assert.True(y + popupHeight <= displayHeight - 16f || popupHeight > displayHeight - 32f);
    }

    [Fact]
    public void FreshInstallDefaultsDoNotEnableStatusOrAttemptClassification()
    {
        AkronModuleSettings settings = new AkronModuleSettings();
        AkronModuleSession session = new AkronModuleSession();

        Assert.False(settings.SafeMode);
        Assert.False(settings.ProofModeOverlay);
        Assert.False(settings.SubmissionMode);
        Assert.False(settings.LabelSystemVisible);
        Assert.False(settings.StatusLabelsWidget);
        Assert.False(settings.HudCheatIndicator);
        Assert.Equal(AkronStatus.Unclassified, session.AttemptStatus);
    }

    [Fact]
    public void LagPauserIgnoresNativeFreezeSample()
    {
        Assert.False(AkronModule.ShouldTriggerLagPauser(150f, 50, ignoreNativeFreezeSpike: true, ignoreIntentionalLoadSpike: false, skippedEngineFrames: 1UL));
        Assert.False(AkronModule.ShouldTriggerLagPauser(150f, 50, ignoreNativeFreezeSpike: false, ignoreIntentionalLoadSpike: false, skippedEngineFrames: 9UL));
    }

    [Fact]
    public void LagPauserIgnoresSpeedrunToolLoadStateSample()
    {
        Assert.False(AkronModule.ShouldTriggerLagPauser(150f, 50, ignoreNativeFreezeSpike: false, ignoreIntentionalLoadSpike: true, skippedEngineFrames: 1UL));
    }

    [Fact]
    public void NativeStartPosRestoreStartsLagPauserGraceWindow()
    {
        long timestamp = 1_000_000L;
        long graceTicks = (long) (1.5d * System.Diagnostics.Stopwatch.Frequency);

        AkronModule.SuppressLagPauserForNativeStartPosRestore(timestamp);

        Assert.True(AkronModule.IsLagPauserStartPosIgnoreActive(timestamp));
        Assert.True(AkronModule.IsLagPauserStartPosIgnoreActive(timestamp + graceTicks - 1L));
        Assert.False(AkronModule.IsLagPauserStartPosIgnoreActive(timestamp + graceTicks));
    }

    [Fact]
    public void LagPauserThresholdStillRequiresRealSpikeWithoutIgnoreFlags()
    {
        Assert.False(AkronModule.ShouldTriggerLagPauser(249f, 250, ignoreNativeFreezeSpike: false, ignoreIntentionalLoadSpike: false, skippedEngineFrames: 1UL));
        Assert.True(AkronModule.ShouldTriggerLagPauser(250f, 250, ignoreNativeFreezeSpike: false, ignoreIntentionalLoadSpike: false, skippedEngineFrames: 1UL));
    }

    [Fact]
    public void LagPauserStillTriggersLowThresholdNonFreezeSamples()
    {
        Assert.True(AkronModule.ShouldTriggerLagPauser(50f, 50, ignoreNativeFreezeSpike: false, ignoreIntentionalLoadSpike: false, skippedEngineFrames: 1UL));
        Assert.True(AkronModule.ShouldTriggerLagPauser(149f, 50, ignoreNativeFreezeSpike: false, ignoreIntentionalLoadSpike: false, skippedEngineFrames: 1UL));
        Assert.False(AkronModule.ShouldTriggerLagPauser(49f, 50, ignoreNativeFreezeSpike: false, ignoreIntentionalLoadSpike: false, skippedEngineFrames: 1UL));
    }

    [Fact]
    public void NewlyAddedFeatureDefaultsStartOptIn()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.False(settings.SafeMode);
        Assert.False(settings.RoomLabels);
        Assert.False(settings.LabelSystemVisible);
        Assert.False(settings.ToastLabels);
        Assert.False(settings.HudCheatIndicator);
        Assert.False(settings.RespawnAtStartPos);
        Assert.False(settings.StartPosShowLabel);
        Assert.False(settings.ScreenshotStatus);
        Assert.False(settings.RoomStatTracker);
        Assert.False(settings.LagPauserIgnoreSpeedrunToolLoadStates);
        Assert.Equal(AkronRoomStatTimerFreezeMode.PausedOrInactive, settings.RoomStatTimerFreezeMode);
        Assert.False(settings.FreezeTimerWhilePaused);
        Assert.False(settings.DashRedirectEnabled);
        Assert.Equal(AkronDashRedirectDirection.Down, settings.DashRedirectDirections);
        AkronAutoKillAreaData autoKillArea = new AkronAutoKillAreaData();
        Assert.False(autoKillArea.SpeedCondition);
        Assert.Equal(0, autoKillArea.MinSpeed);
        Assert.Equal(1000, autoKillArea.MaxSpeed);
        Assert.False(autoKillArea.DashCountCondition);
        Assert.Equal(0, autoKillArea.DashCount);
        Assert.Equal(AkronAutoKillAxisCondition.Any, autoKillArea.HorizontalDirection);
        Assert.Equal(AkronAutoKillAxisCondition.Any, autoKillArea.VerticalDirection);
        Assert.False(settings.GrabModeOverrideEnabled);
        Assert.Equal(GrabModes.Toggle, settings.GrabModeOverrideMode);
        Assert.False(settings.MadelineHairLength);
        Assert.Equal(4, settings.MadelineNoDashHairLength);
        Assert.Equal(4, settings.MadelineOneDashHairLength);
        Assert.Equal(5, settings.MadelineTwoDashHairLength);
        Assert.Equal(5, settings.MadelineFiveDashHairLength);
        Assert.False(settings.MadelineEffectSync);
        Assert.Equal(AkronMadelineEffectSyncMode.MatchHair, settings.MadelineDashParticleSync);
        Assert.Equal(AkronMadelineEffectSyncMode.MatchHair, settings.MadelineDashTrailSync);
        Assert.Equal(AkronMadelineEffectSyncMode.MatchHair, settings.MadelineDeathEffectSync);
        Assert.Equal(AkronMadelineEffectSyncMode.MatchHair, settings.MadelineFeatherColorSync);
        Assert.Equal(AkronMadelineEffectSyncMode.MatchHair, settings.MadelineCrownColorSync);
        Assert.False(settings.CustomDeathParticles);
        Assert.Equal(AkronDeathParticleColorMode.Hair, settings.DeathParticleColorMode);
        Assert.Equal(AkronDeathParticleShape.Vanilla, settings.DeathParticleShape);
        Assert.Equal(0.834f, settings.DeathParticleDurationSeconds);
        Assert.Equal(AkronModuleSettings.DefaultDeathParticleCustomShape, settings.DeathParticleCustomShape);
    }

    [Theory]
    [InlineData(-1f, 0.834f)]
    [InlineData(0f, 0.834f)]
    [InlineData(0.05f, 0.1f)]
    [InlineData(1.25f, 1.25f)]
    [InlineData(5f, 3f)]
    public void DeathParticleDurationClampUsesVanillaDefaultAndBounds(float input, float expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampDeathParticleDurationSeconds(input));
    }

    [Theory]
    [InlineData(0.5f, 0.834f, 0.5f)]
    [InlineData(0.5f, 0.417f, 1f)]
    [InlineData(0.5f, 1.668f, 0.25f)]
    [InlineData(0.5f, 3f, 0.139f)]
    public void DeathParticleDurationRemapsVisualEaseWithoutChangingEngineDuration(float vanillaEase, float durationSeconds, float expected)
    {
        Assert.Equal(expected, AkronModule.ResolveDeathParticleVisualEase(vanillaEase, durationSeconds), precision: 3);
    }

    [Fact]
    public void CustomDeathParticlesDoNotHookEngineDuration()
    {
        string moduleSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/AkronModule.cs"));
        string runtimeSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Runtime/akron-death-particles.cs"));

        Assert.DoesNotContain("DeathEffect.ctor", moduleSource);
        Assert.DoesNotContain(".Duration =", runtimeSource);
        Assert.Contains("DeathEffect.Render += DeathEffectOnRender", moduleSource);
        Assert.Contains("DeathEffect.Draw += DeathEffectOnDraw", moduleSource);
    }

    [Fact]
    public void CustomDeathParticleMasksUseScaledPixelTexture()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Runtime/akron-death-particles.cs"));
        int start = source.IndexOf("private static void DrawMaskedDeathParticleLayer", StringComparison.Ordinal);
        int end = source.IndexOf("internal static string ResolveDeathParticleMask", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        string method = source[start..end];

        Assert.Contains("Draw.Pixel.Draw(", method);
        Assert.Contains("new Vector2(pixelSize, pixelSize)", method);
        Assert.DoesNotContain("Draw.Rect(", method);
    }

    [Fact]
    public void DeathParticleCustomShapeNormalizesToEightByEightMask()
    {
        Assert.Equal(AkronModuleSettings.DefaultDeathParticleCustomShape, AkronModuleSettings.NormalizeDeathParticleCustomShape(null));
        Assert.Equal(AkronModuleSettings.DefaultDeathParticleCustomShape, AkronModuleSettings.NormalizeDeathParticleCustomShape("0000"));

        string normalized = AkronModuleSettings.NormalizeDeathParticleCustomShape("1x0 1");

        Assert.Equal(64, normalized.Length);
        Assert.StartsWith("101", normalized);
        Assert.All(normalized, character => Assert.Contains(character, new[] { '0', '1' }));
    }

    [Fact]
    public void LoggingDefaultsToPlaytesterSafeDiagnosticsWithBoundedRetention()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.True(settings.Logging);
        Assert.Equal(AkronLoggingLevel.Diagnostic, settings.LoggingLevel);
        Assert.True(settings.LoggingMirrorWarningsToEverest);
        Assert.Equal(5, settings.LoggingMaxFileSizeMb);
        Assert.Equal(5, settings.LoggingRetainedFiles);
    }

    [Theory]
    [InlineData(AkronLoggingLevel.Normal, "Normal")]
    [InlineData(AkronLoggingLevel.Verbose, "Verbose")]
    [InlineData(AkronLoggingLevel.Diagnostic, "Diagnostic")]
    [InlineData(AkronLoggingLevel.Trace, "Trace")]
    [InlineData((AkronLoggingLevel) 99, "Diagnostic")]
    public void LoggingLevelLabelsMatchPublicUiContract(AkronLoggingLevel level, string expected)
    {
        Assert.Equal(expected, AkronLog.FormatLevel(level));
    }

    [Fact]
    public void DiagnosticPolicySummaryFormatsCountsInStableFeatureOrder()
    {
        Dictionary<AkronFeatureKind, long> counts = new Dictionary<AkronFeatureKind, long>
        {
            { AkronFeatureKind.Noclip, 9 },
            { AkronFeatureKind.RoomLabelOverlay, 2 }
        };

        string summary = AkronLog.FormatDiagnosticPolicySummary(counts, TimeSpan.FromSeconds(60));

        Assert.Equal("policy checks allowed: RoomLabelOverlay=2, Noclip=9; window-seconds=60", summary);
    }

    [Fact]
    public void DiagnosticFeatureUseSummaryFormatsCountsInStableFeatureOrder()
    {
        Dictionary<AkronFeatureKind, long> counts = new Dictionary<AkronFeatureKind, long>
        {
            { AkronFeatureKind.SpeedNumber, 12 },
            { AkronFeatureKind.InputViewer, 5 }
        };

        string summary = AkronLog.FormatDiagnosticFeatureUseSummary(counts, TimeSpan.FromSeconds(60));

        Assert.Equal("feature uses recorded: InputViewer=5, SpeedNumber=12; window-seconds=60", summary);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(500, 100)]
    public void LoggingFileSizeClampKeepsFilesBounded(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampLoggingMaxFileSizeMb(input));
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    [InlineData(500, 20)]
    public void LoggingRetentionClampKeepsRotationBounded(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampLoggingRetainedFiles(input));
    }

    [Fact]
    public void DefaultButtonBindingsStartEmptyExceptMenuAndLeftAltHolds()
    {
        IReadOnlyDictionary<string, (XnaButtons Buttons, Keys Key)> allowedDefaults = new Dictionary<string, (XnaButtons, Keys)>
        {
            ["ToggleOverlay"] = ((XnaButtons)0, Keys.Tab),
            ["EntityInspectorCursorHold"] = ((XnaButtons)0, Keys.LeftAlt),
            ["ClickTeleportCursor"] = ((XnaButtons)0, Keys.LeftAlt),
            ["CursorZoomHold"] = ((XnaButtons)0, Keys.LeftAlt),
            ["CursorToolsHold"] = ((XnaButtons)0, Keys.LeftAlt)
        };

        foreach (PropertyInfo property in typeof(AkronModuleSettings).GetProperties())
        {
            CustomAttributeData? attribute = property.CustomAttributes
                .SingleOrDefault(data => data.AttributeType.Name == "DefaultButtonBindingAttribute");
            if (attribute == null)
            {
                continue;
            }

            Assert.Equal(2, attribute.ConstructorArguments.Count);
            XnaButtons buttons = (XnaButtons)Convert.ToInt32(attribute.ConstructorArguments[0].Value, CultureInfo.InvariantCulture);
            Keys key = (Keys)Convert.ToInt32(attribute.ConstructorArguments[1].Value, CultureInfo.InvariantCulture);

            if (allowedDefaults.TryGetValue(property.Name, out (XnaButtons Buttons, Keys Key) expected))
            {
                Assert.Equal(expected.Buttons, buttons);
                Assert.Equal(expected.Key, key);
                continue;
            }

            Assert.Equal((XnaButtons)0, buttons);
            Assert.Equal(Keys.None, key);
        }
    }

    [Fact]
    public void CurrentKeybindDefaultsClearLegacyButtonBindingsExceptMenuAndLeftAltHolds()
    {
        ButtonBinding retry = BuildInitializedButtonBinding(Keys.R);
        ButtonBinding reloadRoom = BuildInitializedButtonBinding(Keys.F6);
        ButtonBinding reloadChapter = BuildInitializedButtonBinding(Keys.F7);
        ButtonBinding saveState = BuildInitializedButtonBinding(Keys.F5);
        ButtonBinding loadState = BuildInitializedButtonBinding(Keys.F9);
        ButtonBinding toggleHitboxes = BuildInitializedButtonBinding(Keys.H);
        ButtonBinding freezeGameplay = BuildInitializedButtonBinding(Keys.F);
        ButtonBinding entityInspectorCursorHold = BuildInitializedButtonBinding(Keys.I);
        ButtonBinding cursorZoomHold = BuildInitializedButtonBinding(Keys.Z);
        ButtonBinding cursorToolsHold = BuildInitializedButtonBinding(Keys.C);
        ButtonBinding previousStartPos = BuildInitializedButtonBinding(Keys.OemMinus);
        ButtonBinding nextStartPos = BuildInitializedButtonBinding(Keys.OemPlus);
        AkronModuleSettings settings = new AkronModuleSettings
        {
            Retry = retry,
            ReloadRoom = reloadRoom,
            ReloadChapter = reloadChapter,
            SaveState = saveState,
            LoadState = loadState,
            ToggleHitboxes = toggleHitboxes,
            FreezeGameplay = freezeGameplay,
            EntityInspectorCursorHold = entityInspectorCursorHold,
            CursorZoomHold = cursorZoomHold,
            CursorToolsHold = cursorToolsHold,
            PreviousStartPos = previousStartPos,
            NextStartPos = nextStartPos
        };

        AkronModuleSettings.EnsureCurrentKeybindDefaults(settings);

        Assert.NotSame(retry, settings.Retry);
        Assert.NotSame(reloadRoom, settings.ReloadRoom);
        Assert.NotSame(reloadChapter, settings.ReloadChapter);
        Assert.NotSame(saveState, settings.SaveState);
        Assert.NotSame(loadState, settings.LoadState);
        Assert.NotSame(toggleHitboxes, settings.ToggleHitboxes);
        Assert.NotSame(freezeGameplay, settings.FreezeGameplay);
        Assert.NotSame(entityInspectorCursorHold, settings.EntityInspectorCursorHold);
        Assert.NotSame(cursorZoomHold, settings.CursorZoomHold);
        Assert.NotSame(cursorToolsHold, settings.CursorToolsHold);
        Assert.NotSame(previousStartPos, settings.PreviousStartPos);
        Assert.NotSame(nextStartPos, settings.NextStartPos);

        Assert.NotNull(settings.Retry);
        Assert.NotNull(settings.ReloadRoom);
        Assert.NotNull(settings.ReloadChapter);
        Assert.NotNull(settings.SaveState);
        Assert.NotNull(settings.LoadState);
        Assert.NotNull(settings.ToggleHitboxes);
        Assert.NotNull(settings.FreezeGameplay);
        Assert.NotNull(settings.EntityInspectorCursorHold);
        Assert.NotNull(settings.CursorZoomHold);
        Assert.NotNull(settings.CursorToolsHold);
        Assert.NotNull(settings.PreviousStartPos);
        Assert.NotNull(settings.NextStartPos);
        Assert.NotNull(settings.ToggleOverlay);
        Assert.NotNull(settings.ClickTeleportCursor);
    }

    private static ButtonBinding BuildInitializedButtonBinding(Keys key)
    {
        return new ButtonBinding(0, key)
        {
            Keys = key == Keys.None ? new List<Keys>() : new List<Keys> { key },
            Buttons = new List<XnaButtons>(),
            MouseButtons = new List<Monocle.MInput.MouseData.MouseButtons>()
        };
    }

    [Fact]
    public void OverlayToggleDefaultRestoresOnlyMissingBindings()
    {
        Assert.False(AkronModuleSettings.HasUsableOverlayToggleBinding(new List<Keys>(), new List<XnaButtons>()));
        Assert.False(AkronModuleSettings.HasUsableOverlayToggleBinding(new List<Keys> { Keys.None }, new List<XnaButtons>()));
        Assert.True(AkronModuleSettings.HasUsableOverlayToggleBinding(new List<Keys> { Keys.RightShift }, new List<XnaButtons>()));
        Assert.True(AkronModuleSettings.HasUsableOverlayToggleBinding(new List<Keys> { Keys.LeftAlt }, new List<XnaButtons>()));
        Assert.True(AkronModuleSettings.HasUsableOverlayToggleBinding(new List<Keys> { Keys.F1 }, new List<XnaButtons>()));
        Assert.True(AkronModuleSettings.HasUsableOverlayToggleBinding(new List<Keys> { Keys.LeftShift, Keys.F1 }, new List<XnaButtons>()));
        Assert.True(AkronModuleSettings.HasUsableOverlayToggleBinding(new List<Keys>(), new List<XnaButtons> { XnaButtons.LeftShoulder }));
    }

    [Fact]
    public void OverlayToggleDefaultIsPlainTabOnly()
    {
        Assert.True(AkronModuleSettings.ShouldUseDefaultOverlayToggleBinding(
            new List<Keys> { Keys.Tab, Keys.LeftShift },
            new List<XnaButtons>()));
        Assert.True(AkronModuleSettings.ShouldUseDefaultOverlayToggleBinding(
            new List<Keys> { Keys.LeftControl, Keys.Tab, Keys.None },
            null));

        Assert.False(AkronModuleSettings.ShouldUseDefaultOverlayToggleBinding(
            new List<Keys> { Keys.Tab },
            new List<XnaButtons>()));
        Assert.False(AkronModuleSettings.ShouldUseDefaultOverlayToggleBinding(
            new List<Keys> { Keys.F1 },
            new List<XnaButtons>()));
        Assert.False(AkronModuleSettings.ShouldUseDefaultOverlayToggleBinding(
            new List<Keys> { Keys.LeftShift, Keys.F1 },
            new List<XnaButtons>()));
        Assert.False(AkronModuleSettings.ShouldUseDefaultOverlayToggleBinding(
            new List<Keys> { Keys.Tab, Keys.LeftShift },
            new List<XnaButtons> { XnaButtons.LeftShoulder }));
    }

    [Fact]
    public void CurrentKeybindDefaultsDropPersistedActionBindings()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            MenuActionBindings = new Dictionary<string, string>
            {
                ["Shortcuts/Retry"] = "R",
                ["Level/Freeze Gameplay"] = "RightShift"
            }
        };

        AkronModuleSettings.EnsureCurrentKeybindDefaults(settings);

        Assert.Empty(settings.MenuActionBindings);
    }

    [Fact]
    public void EntityInspectorCursorHoldDefaultsOnlyWhenMissing()
    {
        AkronModuleSettings missingSettings = new AkronModuleSettings
        {
            EntityInspectorCursorHold = null
        };
        ButtonBinding missingDefault = AkronModuleSettings.ResolveEntityInspectorCursorHoldBinding(missingSettings);

        Assert.NotNull(missingDefault);
        Assert.Null(missingSettings.EntityInspectorCursorHold);
    }

    [Fact]
    public void EntityInspectorCursorHoldUsesDefaultForMalformedPersistedBinding()
    {
        ButtonBinding malformedBinding = new ButtonBinding(0, Keys.LeftAlt)
        {
            Keys = null,
            Buttons = null,
            MouseButtons = new List<Monocle.MInput.MouseData.MouseButtons>()
        };
        AkronModuleSettings malformedSettings = new AkronModuleSettings
        {
            EntityInspectorCursorHold = malformedBinding
        };

        ButtonBinding resolved = AkronModuleSettings.ResolveEntityInspectorCursorHoldBinding(malformedSettings);

        Assert.NotNull(resolved);
        Assert.NotSame(malformedBinding, resolved);
    }

    [Theory]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, false, false, true, true)]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, false, false, true)]
    public void PauseTimerFreezeOnlyReleasesStopsOwnedByAkron(bool stoppedByAkron, bool freezeTimerEnabled, bool canFreezeTimer, bool freezeTimerDuringPause, bool expected)
    {
        Assert.Equal(expected, AkronModule.ShouldReleasePauseTimerFreezeStop(stoppedByAkron, freezeTimerEnabled, canFreezeTimer, freezeTimerDuringPause));
    }

    [Fact]
    public void ModMenuDescriptionsWrapBeforeTheyCanWidenTheMenu()
    {
        IReadOnlyList<string> lines = AkronModule.WrapModMenuLine(
            "Akron menu rows are bindable in the overlay by right-clicking or Shift-clicking them. These native config UIs remain for built-in keyboard/controller bindings.",
            maxCharacters: 48);

        Assert.All(lines, line => Assert.InRange(line.Length, 1, 48));
        Assert.Equal(new[] {
            "Akron menu rows are bindable in the overlay by",
            "right-clicking or Shift-clicking them. These",
            "native config UIs remain for built-in",
            "keyboard/controller bindings."
        }, lines);
    }

    [Theory]
    [InlineData(960, 2200, 1920, 0.5f, 1196)]
    [InlineData(960, 800, 1920, 0.5f, 960)]
    [InlineData(1700, 600, 1920, 0.5f, 1524)]
    [InlineData(50, 400, 1920, 0f, 96)]
    public void NativeTextMenuClampKeepsReadableEdgeInsideViewport(
        float currentX,
        float menuWidth,
        float displayWidth,
        float justifyX,
        float expectedX)
    {
        Assert.Equal(
            expectedX,
            AkronModule.CalculateSafeTextMenuX(currentX, menuWidth, displayWidth, justifyX));
    }

    [Theory]
    [InlineData(-5, 4)]
    [InlineData(0, 4)]
    [InlineData(1, 1)]
    [InlineData(12, 12)]
    [InlineData(120, 100)]
    public void MadelineHairLengthClampUsesDefaultAndBounds(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampMadelineHairLength(input));
    }

    [Theory]
    [InlineData(AkronMadelineEffectSyncMode.Off, AkronMadelineEffectSyncMode.Off)]
    [InlineData(AkronMadelineEffectSyncMode.MatchHair, AkronMadelineEffectSyncMode.MatchHair)]
    [InlineData((AkronMadelineEffectSyncMode)99, AkronMadelineEffectSyncMode.Off)]
    public void MadelineEffectSyncModeNormalizesToSupportedModes(AkronMadelineEffectSyncMode input, AkronMadelineEffectSyncMode expected)
    {
        Assert.Equal(expected, AkronModuleSettings.NormalizeMadelineEffectSyncMode(input));
    }

    [Fact]
    public void RoomStatTrackerDefaultsExposeUsefulLabelLines()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.Equal(0xFFFFFF, settings.RoomStatTrackerColor);
        Assert.True(settings.RoomStatShowRoomName);
        Assert.True(settings.RoomStatShowDeaths);
        Assert.True(settings.RoomStatShowInGameTime);
        Assert.True(settings.RoomStatShowStrawberries);
        Assert.False(settings.RoomStatShowAliveTime);
        Assert.False(settings.RoomStatHideIfGolden);
    }

    [Theory]
    [InlineData(AkronRoomStatTimerFreezeMode.Never, false, false, false)]
    [InlineData(AkronRoomStatTimerFreezeMode.Never, true, true, false)]
    [InlineData(AkronRoomStatTimerFreezeMode.Paused, false, false, false)]
    [InlineData(AkronRoomStatTimerFreezeMode.Paused, true, false, true)]
    [InlineData(AkronRoomStatTimerFreezeMode.Paused, false, true, false)]
    [InlineData(AkronRoomStatTimerFreezeMode.Inactive, false, true, true)]
    [InlineData(AkronRoomStatTimerFreezeMode.Inactive, true, false, false)]
    [InlineData(AkronRoomStatTimerFreezeMode.Cutscene, false, false, false)]
    [InlineData(AkronRoomStatTimerFreezeMode.PausedOrInactive, false, false, false)]
    [InlineData(AkronRoomStatTimerFreezeMode.PausedOrInactive, true, false, true)]
    [InlineData(AkronRoomStatTimerFreezeMode.PausedOrInactive, false, true, true)]
    [InlineData(AkronRoomStatTimerFreezeMode.PausedInactiveOrCutscene, false, false, false)]
    [InlineData(AkronRoomStatTimerFreezeMode.PausedInactiveOrCutscene, true, false, true)]
    [InlineData(AkronRoomStatTimerFreezeMode.PausedInactiveOrCutscene, false, true, true)]
    public void RoomStatFreezeModeMatchesPublicOptions(AkronRoomStatTimerFreezeMode mode, bool paused, bool inactive, bool expected)
    {
        Assert.Equal(expected, AkronPracticeStats.ShouldFreezeRoomStatTimers(mode, paused, inactive));
    }

    [Theory]
    [InlineData(AkronRoomStatTimerFreezeMode.Cutscene, false, false, true, true)]
    [InlineData(AkronRoomStatTimerFreezeMode.PausedInactiveOrCutscene, false, false, true, true)]
    [InlineData(AkronRoomStatTimerFreezeMode.PausedOrInactive, false, false, true, false)]
    public void RoomStatFreezeModeCanIncludeCutscenes(AkronRoomStatTimerFreezeMode mode, bool paused, bool inactive, bool cutscene, bool expected)
    {
        Assert.Equal(expected, AkronPracticeStats.ShouldFreezeRoomStatTimers(mode, paused, inactive, cutscene));
    }

    [Fact]
    public void RefillClarityDefaultsAreVisibleStateOnly()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.False(settings.RefillClarity);
        Assert.Equal(AkronModuleSettings.DefaultRefillClarityColor, settings.RefillClarityColor);
        Assert.Equal(100, settings.RefillClarityOpacity);
    }

    [Fact]
    public void RefillClarityRecognizesCustomOneUseRefillEntities()
    {
        Assert.True(AkronHudRenderer.ShouldRenderRefillClarityOutline(MakeLive(new CustomOneUseRefillProbe())));
        Assert.True(AkronHudRenderer.ShouldRenderRefillClarityOutline(MakeLive(new CustomOnlyOnceRefillProbe())));
        Assert.False(AkronHudRenderer.ShouldRenderRefillClarityOutline(MakeLive(new CustomReusableRefillProbe())));
        Assert.False(AkronHudRenderer.ShouldRenderRefillClarityOutline(MakeLive(new CustomOneUseGemProbe())));
        Assert.False(AkronHudRenderer.ShouldRenderRefillClarityOutline(MakeLive(new CustomOneUseRefillProbe(), visible: false)));
    }

    [Fact]
    public void EntityInspectorCyclingUsesStableStackIdentityInsteadOfClickPosition()
    {
        Assert.True(AkronEntityInspector.IsSameInspectorStackForCycling("7|Both|10,11", "7|Both|10,11", 2, 2));
        Assert.False(AkronEntityInspector.IsSameInspectorStackForCycling("7|Both|10,12", "7|Both|10,11", 2, 2));
        Assert.False(AkronEntityInspector.IsSameInspectorStackForCycling("7|Both|10,11", "7|Both|10,11", 1, 2));
        Assert.False(AkronEntityInspector.IsSameInspectorStackForCycling("7|Both|10,11", "7|Both|10,11", 2, 0));
    }

    [Fact]
    public void AutoKillAreaConditionsMatchUsesAreaSpecificConditions()
    {
        AkronAutoKillAreaData fastArea = new AkronAutoKillAreaData
        {
            SpeedCondition = true,
            MinSpeed = 200,
            MaxSpeed = 300
        };
        AkronAutoKillAreaData slowArea = new AkronAutoKillAreaData
        {
            SpeedCondition = true,
            MinSpeed = 0,
            MaxSpeed = 100
        };

        Assert.True(AkronModule.AutoKillAreaConditionsMatch(fastArea, totalSpeed: 240f, horizontalSpeed: 240f, verticalSpeed: 0f, dashes: 1, onGround: true, playerState: 0));
        Assert.False(AkronModule.AutoKillAreaConditionsMatch(slowArea, totalSpeed: 240f, horizontalSpeed: 240f, verticalSpeed: 0f, dashes: 1, onGround: true, playerState: 0));
    }

    [Fact]
    public void AutoKillAreaConditionsInvertPerArea()
    {
        AkronAutoKillAreaData area = new AkronAutoKillAreaData
        {
            DashCountCondition = true,
            DashCount = 1,
            InvertConditions = true
        };

        Assert.False(AkronModule.AutoKillAreaConditionsMatch(area, totalSpeed: 0f, horizontalSpeed: 0f, verticalSpeed: 0f, dashes: 1, onGround: true, playerState: 0));
        Assert.True(AkronModule.AutoKillAreaConditionsMatch(area, totalSpeed: 0f, horizontalSpeed: 0f, verticalSpeed: 0f, dashes: 0, onGround: true, playerState: 0));
    }

    [Fact]
    public void AutoKillAreaCopiesDefaultConditionsWhenPlaced()
    {
        AkronAutoKillAreaData defaults = new AkronAutoKillAreaData
        {
            SpeedCondition = true,
            MinSpeed = 200,
            MaxSpeed = 300,
            DashCountCondition = true,
            DashCount = 1
        };

        Rectangle rectangle = default;
        rectangle.X = 10;
        rectangle.Y = 20;
        rectangle.Width = 30;
        rectangle.Height = 40;
        AkronAutoKillAreaData placedArea = defaults.CopyWithRectangle(rectangle);
        defaults.MinSpeed = 500;
        defaults.DashCount = 2;

        Assert.Equal(10, placedArea.X);
        Assert.Equal(20, placedArea.Y);
        Assert.Equal(30, placedArea.Width);
        Assert.Equal(40, placedArea.Height);
        Assert.True(placedArea.SpeedCondition);
        Assert.Equal(200, placedArea.MinSpeed);
        Assert.Equal(300, placedArea.MaxSpeed);
        Assert.True(placedArea.DashCountCondition);
        Assert.Equal(1, placedArea.DashCount);
    }

    [Fact]
    public void ScreenshotDefaultsKeepGameFrameCaptureSimple()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.Equal(new[] { "Speedrun Tool", "CelesteTAS", "Extended Variant Mode", "Extended Camera Dynamics" }, settings.CollapsedOverlaySections);
        Assert.Equal(1, settings.ScreenshotScale);
        Assert.False(settings.ScreenshotStatus);
        Assert.Equal(AkronScreenshotImageFormat.Png, settings.ScreenshotScannerImageFormat);
        Assert.False(settings.ScreenshotScannerExportMarkers);
        Assert.True(settings.ScreenshotScannerExportStartPositions);
        Assert.True(settings.ScreenshotScannerExportAutoKillAreas);
        Assert.True(settings.ScreenshotScannerExportAutoDeafenAreas);
        Assert.True(settings.ScreenshotScannerFreezeTime);
        Assert.True(settings.ScreenshotScannerNoclipHideMadeline);
        Assert.Equal(600, settings.AutosaveIntervalSeconds);
        Assert.False(settings.AutosaveHideSavingIcon);
        Assert.Equal(100, settings.SoundVolumes["bird-squawk"]);
        Assert.False(settings.FastLookout);
        Assert.Equal(3, settings.FastLookoutMultiplier);
        Assert.False(settings.SkipPostcards);
        Assert.False(settings.SkipIntro);
        Assert.False(settings.CameraOffset);
        Assert.Equal(0, settings.CameraOffsetX);
        Assert.Equal(0, settings.CameraOffsetY);
        Assert.False(settings.CursorZoom);
        Assert.Equal(100, settings.CursorZoomPercent);
        Assert.Equal(10, settings.CursorZoomStepPercent);
        Assert.False(settings.CursorZoomAllowZoomOut);
        Assert.False(settings.CursorZoomResetOnDeactivate);
        Assert.Equal(AkronCursorZoomActivationMode.Hold, settings.CursorZoomActivationMode);
        Assert.False(settings.CursorTools);
        Assert.Equal(AkronCursorToolsClickAction.ClickTeleport, settings.CursorToolsClickAction);
        Assert.True(settings.CursorToolsCursorZoom);
        Assert.True(settings.CursorToolsFreeCamera);
        Assert.False(settings.CursorToolsFreezeGameplay);
        Assert.False(settings.FrameStepper);
        Assert.False(settings.SubmissionMode);
        Assert.False(settings.ProofRecorderGuard);
        Assert.False(settings.EndScreenHelper);
        Assert.False(settings.PauseTracker);
        Assert.False(settings.MapVersionStamp);
        Assert.False(settings.GoldenTransparency);
        Assert.Equal(55, settings.GoldenTransparencyOpacity);
        Assert.False(settings.LagPauser);
        Assert.Equal(250, settings.LagPauserThresholdMs);
        Assert.False(settings.FreeCameraMouseControl);
    }

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public void CursorToolsHoldRequiresSettingBindingAndHiddenOverlay(bool enabled, bool bindingHeld, bool overlayVisible, bool expected)
    {
        Assert.Equal(expected, AkronModule.ShouldUseCursorToolsHold(enabled, bindingHeld, overlayVisible));
    }

    [Theory]
    [InlineData(false, true, false, true, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, true, false, true, true)]
    public void EntityInspectorCursorRequiresInspectorHoldHiddenOverlayAndPolicy(bool entityInspector, bool bindingHeld, bool overlayVisible, bool policyAllowed, bool expected)
    {
        Assert.Equal(expected, AkronModule.ShouldShowEntityInspectorCursor(entityInspector, bindingHeld, overlayVisible, policyAllowed));
    }

    [Theory]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, true)]
    public void CursorToolsEffectiveToolRequiresHoldAndChildOption(bool savedToolEnabled, bool cursorToolsHeld, bool childOptionEnabled, bool expected)
    {
        Assert.Equal(expected, AkronModule.IsCursorToolEffectiveEnabled(savedToolEnabled, cursorToolsHeld, childOptionEnabled));
    }

    [Theory]
    [InlineData(false, false, true, true, true)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, false, false, true)]
    public void ClickTeleportCursorUsesNormalBindOrCursorTools(bool clickTeleportEnabled, bool clickTeleportHoldActive, bool cursorToolsHeld, bool cursorToolsClickTeleport, bool expected)
    {
        Assert.Equal(expected, AkronModule.IsClickTeleportCursorActive(clickTeleportEnabled, clickTeleportHoldActive, cursorToolsHeld, cursorToolsClickTeleport));
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, true)]
    public void CursorToolsFreeCameraUsesMouseControlByDefault(bool freeCameraMouseControl, bool cursorToolsHeld, bool cursorToolsFreeCamera, bool expected)
    {
        Assert.Equal(expected, AkronModule.IsFreeCameraMouseControlEffectiveEnabled(freeCameraMouseControl, cursorToolsHeld, cursorToolsFreeCamera));
    }

    [Theory]
    [InlineData(AkronCursorToolsClickAction.ClickTeleport, AkronCursorToolsClickAction.ClickTeleport)]
    [InlineData(AkronCursorToolsClickAction.InspectorPin, AkronCursorToolsClickAction.InspectorPin)]
    [InlineData((AkronCursorToolsClickAction)99, AkronCursorToolsClickAction.ClickTeleport)]
    public void CursorToolsClickActionNormalizesToSupportedModes(AkronCursorToolsClickAction action, AkronCursorToolsClickAction expected)
    {
        Assert.Equal(expected, AkronModuleSettings.NormalizeCursorToolsClickAction(action));
    }

    [Theory]
    [InlineData(160f, 90f, 0f, 0f)]
    [InlineData(166f, 90f, 0f, 0f)]
    [InlineData(320f, 90f, 1f, 0f)]
    [InlineData(0f, 90f, -1f, 0f)]
    [InlineData(160f, 180f, 0f, 1f)]
    public void FreeCameraMouseAimUsesScreenCenterDeadzoneAndEdges(float mouseX, float mouseY, float expectedX, float expectedY)
    {
        Vector2 aim = AkronRuntimeOptions.CalculateFreeCameraMouseAim(TestVector2(mouseX, mouseY));

        Assert.Equal(expectedX, aim.X, precision: 3);
        Assert.Equal(expectedY, aim.Y, precision: 3);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void ClickTeleportOnlyRecentersCameraOutsideFreeCameraAndZoom(bool freeCameraActive, bool levelZoomActive, bool expected)
    {
        Assert.Equal(expected, AkronModule.ShouldClickTeleportMoveCamera(freeCameraActive, levelZoomActive));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void HideSavingIconSuppressesAutosaveNotice(bool isCapturingGameFrame, bool hideSavingIcon, bool expected)
    {
        Assert.Equal(expected, AkronModule.ShouldSuppressSavingNotice(isCapturingGameFrame, hideSavingIcon));
        Assert.Equal(expected, AkronModule.ShouldSuppressSaveLoadIcon(isCapturingGameFrame, hideSavingIcon));
    }

    [Theory]
    [InlineData(AkronScreenshotImageFormat.Png, AkronScreenshotImageFormat.Png)]
    [InlineData(AkronScreenshotImageFormat.Jpeg, AkronScreenshotImageFormat.Jpeg)]
    [InlineData((AkronScreenshotImageFormat)99, AkronScreenshotImageFormat.Png)]
    public void ScreenshotScannerImageFormatNormalizesToSupportedFormats(AkronScreenshotImageFormat input, AkronScreenshotImageFormat expected)
    {
        Assert.Equal(expected, AkronModuleSettings.NormalizeScreenshotScannerImageFormat(input));
    }

    [Fact]
    public void InternalRecorderDefaultsMatchEclipseStyleRecordingSurface()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.Equal(AkronModuleSettings.DefaultRecordingFilenameTemplate, settings.RecordingFilenameTemplate);
        Assert.Equal(AkronRecordingContainerFormat.Mkv, settings.RecordingContainerFormat);
        Assert.Equal(60, settings.RecordingFramerate);
        Assert.Equal(3.4f, settings.RecordingEndscreenDurationSeconds);
        Assert.Equal(30, settings.RecordingBitrateMbps);
        Assert.Equal(1920, settings.RecordingResolutionX);
        Assert.Equal(1080, settings.RecordingResolutionY);
        Assert.Equal(AkronRecordingCodec.Libx264, settings.RecordingCodec);
        Assert.Equal(AkronRecordingPreset.Cpu, settings.RecordingPreset);
        Assert.Equal(AkronRecordingQualityPreset.Balanced, settings.RecordingQualityPreset);
        Assert.Equal(AkronRecordingRateControl.Crf, settings.RecordingRateControl);
        Assert.Equal(0, settings.RecordingReplayBufferSeconds);
        Assert.Equal(AkronRecordingReplayAutoStart.Off, settings.RecordingReplayAutoStart);
        Assert.True(settings.RecordingAudioFullMixTrack);
        Assert.True(settings.RecordingTriggerLastDeath);
        Assert.True(settings.RecordingTriggerBerryCollect);
        Assert.True(settings.RecordingTriggerGoldenDeath);
    }

    [Theory]
    [InlineData("", "{chapter}-{room}-{timestamp}-d{death}-a{attempt}")]
    [InlineData("  {room}-{timestamp}  ", "{room}-{timestamp}")]
    public void RecordingFilenameTemplateNormalizationUsesPublicTokens(string input, string expected)
    {
        Assert.Equal(expected, AkronModuleSettings.NormalizeRecordingFilenameTemplate(input));
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(1, 1)]
    [InlineData(240, 240)]
    [InlineData(500, 360)]
    public void RecordingFramerateClampMatchesRecorderRange(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampRecordingFramerate(input));
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 1)]
    [InlineData(250, 250)]
    [InlineData(2000, 1000)]
    public void RecordingBitrateClampMatchesMegabitRange(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampRecordingBitrateMbps(input));
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(3.4f, 3.4f)]
    [InlineData(45f, 30f)]
    public void RecordingEndscreenDurationClampMatchesClearDelayRange(float input, float expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampRecordingEndscreenDurationSeconds(input), precision: 3);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 5)]
    [InlineData(30, 30)]
    [InlineData(900, 600)]
    public void RecordingReplayBufferClampUsesDefaultAndBounds(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampRecordingReplayBufferSeconds(input));
    }

    [Theory]
    [InlineData(AkronRecordingReplayAutoStart.Off, "Off")]
    [InlineData(AkronRecordingReplayAutoStart.InLevels, "In Levels")]
    [InlineData(AkronRecordingReplayAutoStart.Always, "Always")]
    public void RecordingReplayAutoStartFormattingMatchesUiLabels(AkronRecordingReplayAutoStart mode, string expected)
    {
        Assert.Equal(expected, AkronModuleSettings.FormatRecordingReplayAutoStart(mode));
    }

    [Theory]
    [InlineData(0, AkronRecordingReplayAutoStart.Always, false, false, false)]
    [InlineData(30, AkronRecordingReplayAutoStart.Off, false, false, false)]
    [InlineData(30, AkronRecordingReplayAutoStart.Off, true, false, true)]
    [InlineData(30, AkronRecordingReplayAutoStart.InLevels, false, true, true)]
    [InlineData(30, AkronRecordingReplayAutoStart.InLevels, false, false, false)]
    [InlineData(30, AkronRecordingReplayAutoStart.Always, false, true, true)]
    [InlineData(30, AkronRecordingReplayAutoStart.Always, false, false, true)]
    public void RecordingReplayAutoStartScopeMatchesCaptureScene(
        int bufferSeconds,
        AkronRecordingReplayAutoStart autoStart,
        bool manualReplayBuffer,
        bool sceneIsLevel,
        bool expected)
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            RecordingReplayBufferSeconds = bufferSeconds,
            RecordingReplayAutoStart = autoStart
        };

        Assert.Equal(expected, AkronInternalRecorder.ShouldMaintainReplayBufferForTesting(settings, sceneIsLevel, manualReplayBuffer));
    }

    [Fact]
    public void DisarmingReplayBufferAutoStartClearsCompletionCaptureTriggers()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            RecordingReplayBufferSeconds = 300,
            RecordingReplayAutoStart = AkronRecordingReplayAutoStart.InLevels,
            RecordingTriggerRoomEntryToClear = true,
            RecordingTriggerCheckpointClear = true
        };

        AkronInternalRecorder.DisarmReplayBufferAutoStartForTesting(settings);

        Assert.Equal(300, settings.RecordingReplayBufferSeconds);
        Assert.Equal(AkronRecordingReplayAutoStart.Off, settings.RecordingReplayAutoStart);
        Assert.False(settings.RecordingTriggerRoomEntryToClear);
        Assert.False(settings.RecordingTriggerCheckpointClear);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(45, 45)]
    [InlineData(240, 120)]
    public void RecordingClipSecondsClampAllowsDisabledAndBounds(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampRecordingClipSeconds(input));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(4, 4)]
    [InlineData(40, 20)]
    public void RecordingKeyframeClampAllowsEncoderDefaultAndBounds(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampRecordingKeyframeIntervalSeconds(input));
    }

    [Theory]
    [InlineData(0, 1920, 0, 1080)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(3840, 3840, 2160, 2160)]
    [InlineData(20000, 15360, 12000, 8640)]
    public void RecordingResolutionClampMatchesVisibleAxisRanges(int inputX, int expectedX, int inputY, int expectedY)
    {
        Assert.Equal(expectedX, AkronModuleSettings.ClampRecordingResolutionX(inputX));
        Assert.Equal(expectedY, AkronModuleSettings.ClampRecordingResolutionY(inputY));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(250, 200)]
    public void RecordingAudioLevelClampMatchesGameTrackMixerRange(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampRecordingAudioLevel(input));
    }

    [Theory]
    [InlineData(AkronRecordingPreset.Cpu, AkronRecordingCodec.Libx264, AkronRecordingQualityPreset.Balanced)]
    [InlineData(AkronRecordingPreset.Nvidia, AkronRecordingCodec.H264Nvenc, AkronRecordingQualityPreset.HighQuality)]
    [InlineData(AkronRecordingPreset.Amd, AkronRecordingCodec.H264Amf, AkronRecordingQualityPreset.HighQuality)]
    public void RecordingPresetAppliesExpectedGameRecorderEncoder(AkronRecordingPreset preset, AkronRecordingCodec codec, AkronRecordingQualityPreset quality)
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            RecordingCodec = AkronRecordingCodec.LibVpxVp9,
            RecordingQualityPreset = AkronRecordingQualityPreset.Lossless
        };

        AkronInternalRecorder.ApplyPresetForTesting(settings, preset);

        Assert.Equal(preset, settings.RecordingPreset);
        Assert.Equal(codec, settings.RecordingCodec);
        Assert.Equal(quality, settings.RecordingQualityPreset);
    }

    [Fact]
    public void RecordingFfmpegArgumentsUseRawVideoInputAndConfiguredQualityControls()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            RecordingRateControl = AkronRecordingRateControl.Cbr,
            RecordingBitrateMbps = 45,
            RecordingKeyframeIntervalSeconds = 3,
            RecordingColorspaceArgs = "format=yuv420p"
        };

        string args = AkronInternalRecorder.BuildFfmpegArgumentsForTesting(settings, 1280, 720, 120, "/tmp/akron-test.mkv");

        Assert.Contains("\"-f\" \"rawvideo\"", args);
        Assert.Contains("\"-pix_fmt\" \"rgba\"", args);
        Assert.Contains("\"-s\" \"1280x720\"", args);
        Assert.Contains("\"-r\" \"120\"", args);
        Assert.Contains("\"-preset\" \"veryfast\"", args);
        Assert.Contains("\"-tune\" \"zerolatency\"", args);
        Assert.Contains("\"-b:v\" \"45M\"", args);
        Assert.Contains("\"-maxrate\" \"45M\"", args);
        Assert.Contains("\"-g\" \"360\"", args);
        Assert.Contains("\"-vf\" \"format=yuv420p\"", args);
    }

    [Fact]
    public void RecordingFfmpegArgumentsKeepHardwarePresetFlagsEncoderSpecific()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            RecordingCodec = AkronRecordingCodec.H264Nvenc,
            RecordingQualityPreset = AkronRecordingQualityPreset.LowImpact
        };

        string args = AkronInternalRecorder.BuildFfmpegArgumentsForTesting(settings, 1280, 720, 60, "/tmp/akron-test.mkv");

        Assert.DoesNotContain("\"-preset\"", args);
        Assert.DoesNotContain("\"-tune\" \"zerolatency\"", args);
    }

    [Fact]
    public void ReplaySegmentSelectionKeepsLateFinalizedNonEmptySegment()
    {
        string folder = Path.Combine(Path.GetTempPath(), "akron-replay-segment-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            DateTime startUtc = new DateTime(2026, 5, 5, 15, 23, 22, DateTimeKind.Utc);
            DateTime endUtc = startUtc.AddSeconds(30);
            string oldSegment = WriteReplaySegment(folder, "segment-000000.mkv", startUtc.AddSeconds(-10), 128);
            string selectedSegment = WriteReplaySegment(folder, "segment-000001.mkv", endUtc.AddSeconds(2), 128);
            string emptySegment = WriteReplaySegment(folder, "segment-000002.mkv", endUtc.AddSeconds(2), 0);
            string futureSegment = WriteReplaySegment(folder, "segment-000003.mkv", endUtc.AddSeconds(10), 128);

            List<string> selected = AkronInternalRecorder.SelectReplaySegmentsForTesting(folder, startUtc, endUtc).ToList();

            string segment = Assert.Single(selected);
            Assert.Equal(selectedSegment, segment);
            Assert.DoesNotContain(oldSegment, selected);
            Assert.DoesNotContain(emptySegment, selected);
            Assert.DoesNotContain(futureSegment, selected);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void RecordingClipBrowserSortsByConfiguredModeAndSidecarMetadata()
    {
        string folder = Path.Combine(Path.GetTempPath(), "akron-recorder-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string deathClip = Path.Combine(folder, "chapter-a-room-a-last-death.mp4");
            string clearClip = Path.Combine(folder, "chapter-b-room-b-area-clear.mp4");
            File.WriteAllText(deathClip, string.Empty);
            File.WriteAllText(clearClip, string.Empty);
            File.WriteAllLines(deathClip + ".akrclip", new[] { "kind=last-death", "favorite=true" });
            File.WriteAllLines(clearClip + ".akrclip", new[] { "kind=area-clear" });
            File.SetCreationTimeUtc(deathClip, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetCreationTimeUtc(clearClip, new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc));

            AkronModuleSettings settings = new AkronModuleSettings
            {
                RecordingOutputFolder = folder,
                RecordingClipBrowserSort = AkronRecordingClipSort.Favorite
            };

            List<AkronRecordingClipInfo> byFavorite = AkronInternalRecorder.ListClips(settings).ToList();
            Assert.Equal("last-death", byFavorite[0].Kind);

            settings.RecordingClipBrowserSort = AkronRecordingClipSort.Clear;
            List<AkronRecordingClipInfo> byClear = AkronInternalRecorder.ListClips(settings).ToList();
            Assert.Equal("area-clear", byClear[0].Kind);

            settings.RecordingClipBrowserFilter = AkronRecordingClipFilter.Death;
            List<AkronRecordingClipInfo> deathOnly = AkronInternalRecorder.ListClips(settings).ToList();
            AkronRecordingClipInfo death = Assert.Single(deathOnly);
            Assert.Equal("last-death", death.Kind);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void CompletionClipSelectionUsesClearAndFlagClipsForCurrentChapterInTimelineOrder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "akron-completion-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            DateTime baseUtc = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc);
            string laterFlag = WriteClipWithSidecar(folder, "chapter-a-room-b-flag.mkv", "completion-flag", baseUtc.AddSeconds(20), baseUtc.AddSeconds(25));
            string earlierClear = WriteClipWithSidecar(folder, "chapter-a-room-a-clear.mkv", "room-clear", baseUtc, baseUtc.AddSeconds(10));
            WriteClipWithSidecar(folder, "chapter-a-room-c-death.mkv", "last-death", baseUtc.AddSeconds(30), baseUtc.AddSeconds(35));
            WriteClipWithSidecar(folder, "chapter-b-room-a-clear.mkv", "room-clear", baseUtc.AddSeconds(40), baseUtc.AddSeconds(45));

            AkronModuleSettings settings = new AkronModuleSettings
            {
                RecordingOutputFolder = folder
            };

            List<AkronRecordingClipInfo> selected = AkronInternalRecorder.SelectCompletionClipsForTesting(settings, "chapter-a").ToList();

            Assert.Equal(new[] { earlierClear, laterFlag }, selected.Select(clip => clip.Path));
            Assert.Equal(new[] { "room-clear", "completion-flag" }, selected.Select(clip => clip.Kind));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void CompletionConcatArgumentsUseFfmpegConcatCopy()
    {
        string args = AkronInternalRecorder.BuildCompletionConcatArgumentsForTesting("/tmp/clear-list.txt", "/tmp/clear-video.mkv");

        Assert.Contains("\"-f\" \"concat\"", args);
        Assert.Contains("\"-safe\" \"0\"", args);
        Assert.Contains("\"-i\" \"/tmp/clear-list.txt\"", args);
        Assert.Contains("\"-c\" \"copy\"", args);
        Assert.Contains("\"/tmp/clear-video.mkv\"", args);
    }

    [Fact]
    public void RecordingAudioSupportPlansOnlyFmodGameTracks()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.True(AkronInternalAudioRecorder.ShouldCaptureFullMix(settings));
        Assert.False(AkronInternalAudioRecorder.HasUnsupportedTrackRequest(settings));
        Assert.Equal(new[] { "Full Mix" }, AkronInternalAudioRecorder.GetPlannedTrackNamesForTesting(settings));

        settings.RecordingAudioMusicTrack = true;
        settings.RecordingRecordMutedAudio = true;
        settings.RecordingAudioSfxTrack = true;
        settings.RecordingAudioAmbienceTrack = true;

        Assert.False(AkronInternalAudioRecorder.HasUnsupportedTrackRequest(settings));
        Assert.Equal(new[] { "Full Mix", "Music", "SFX", "Ambience" }, AkronInternalAudioRecorder.GetPlannedTrackNamesForTesting(settings));
    }

    [Fact]
    public void RecordingMutedAudioRequiresAnIsolatedGameBusTrack()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            RecordingRecordMutedAudio = true
        };

        Assert.True(AkronInternalAudioRecorder.HasUnsupportedTrackRequest(settings));
        Assert.Contains("music, SFX, or ambience bus track", AkronInternalAudioRecorder.DescribeUnsupportedTrackRequest(settings));

        settings.RecordingAudioSfxTrack = true;

        Assert.False(AkronInternalAudioRecorder.HasUnsupportedTrackRequest(settings));
    }

    [Fact]
    public void RecordingAudioSupportCanDisableAllAudioTracks()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            RecordingAudioFullMixTrack = false
        };

        Assert.Empty(AkronInternalAudioRecorder.GetPlannedTrackNamesForTesting(settings));
    }

    [Fact]
    public void RecordingAudioMuxArgumentsMapMultipleNamedTracks()
    {
        AkronRecordedAudio audio = new AkronRecordedAudio(new[] {
            new AkronRecordedAudioTrack("/tmp/full.s16le", "Full Mix", 48000, 2, 128),
            new AkronRecordedAudioTrack("/tmp/music.s16le", "Music", 48000, 2, 128)
        });

        string args = AkronInternalRecorder.BuildAudioMuxArgumentsForTesting("/tmp/video.mkv", audio, "/tmp/video-audio.mp4");

        Assert.Contains("\"-map\" \"0:v:0\"", args);
        Assert.Contains("\"-map\" \"1:a:0\"", args);
        Assert.Contains("\"-map\" \"2:a:0\"", args);
        Assert.Contains("\"-metadata:s:a:0\" \"title=Full Mix\"", args);
        Assert.Contains("\"-metadata:s:a:1\" \"title=Music\"", args);
        Assert.Contains("\"-c:a\" \"aac\"", args);
    }

    [Fact]
    public void RecordingReplayAudioSliceUsesRequestedTimeWindow()
    {
        string path = Path.Combine(Path.GetTempPath(), "akron-audio-source-" + Guid.NewGuid().ToString("N") + ".s16le");
        try
        {
            File.WriteAllBytes(path, Enumerable.Range(0, 100).Select(value => (byte)value).ToArray());
            DateTime started = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            AkronRecordedAudio audio = new AkronRecordedAudio(new[] {
                new AkronRecordedAudioTrack(path, "Full Mix", 10, 1, 100, started, started.AddSeconds(5))
            });

            AkronRecordedAudio sliced = AkronInternalRecorder.SliceRecordedAudioForTesting(audio, started.AddSeconds(1), started.AddSeconds(3));
            AkronRecordedAudioTrack track = Assert.Single(sliced.Tracks);

            Assert.Equal(40, track.Bytes);
            Assert.Equal(Enumerable.Range(20, 40).Select(value => (byte)value).ToArray(), File.ReadAllBytes(track.Path));
            File.Delete(track.Path);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    [InlineData(30, 16)]
    public void ScreenshotScaleClampMatchesPixelScaleRange(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampScreenshotScale(input));
    }

    [Theory]
    [InlineData(-5, 3)]
    [InlineData(0, 3)]
    [InlineData(1, 1)]
    [InlineData(6, 6)]
    [InlineData(20, 10)]
    public void FastLookoutMultiplierIsClamped(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampFastLookoutMultiplier(input));
    }

    [Theory]
    [InlineData(-2, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(250, 99)]
    public void SetInventoryJumpsClampAllowsNoExtraJumps(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampSetInventoryJumps(input));
    }

    [Theory]
    [InlineData(0f, false)]
    [InlineData(0.001f, true)]
    [InlineData(0.1f, true)]
    public void AirJumpsPreserveVanillaCoyoteJump(float jumpGraceTimer, bool expected)
    {
        Assert.Equal(expected, AkronModule.ShouldPreserveVanillaJumpForAirJump(jumpGraceTimer));
    }

    [Theory]
    [InlineData(1f, 0f, false, false)]
    [InlineData(1f, 1f, false, false)]
    [InlineData(1f, -1f, false, true)]
    [InlineData(0f, -1f, false, true)]
    [InlineData(1f, -1f, true, false)]
    public void AirJumpDashDirectionPolicyKeepsVerticalDashJumpsOptional(float x, float y, bool allowVerticalDashJumps, bool expectedSkip)
    {
        Assert.Equal(
            expectedSkip,
            AkronModule.ShouldSkipAirJumpForDashDirection(AkronModule.PlayerDashState, TestVector2(x, y), allowVerticalDashJumps));
    }

    [Theory]
    [InlineData(1f, 0f, true)]
    [InlineData(1f, 1f, true)]
    [InlineData(1f, -1f, false)]
    [InlineData(0f, 1f, false)]
    public void AirJumpUsesSuperJumpOnlyForVanillaDashJumpDirections(float x, float y, bool expected)
    {
        Assert.Equal(expected, AkronModule.ShouldUseSuperJumpForAirJump(AkronModule.PlayerDashState, TestVector2(x, y)));
    }

    [Theory]
    [InlineData(-50, -20)]
    [InlineData(-2, -2)]
    [InlineData(0, 0)]
    [InlineData(12, 12)]
    [InlineData(50, 20)]
    public void CameraOffsetIsClampedToDebugRange(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampCameraOffset(input));
    }

    [Theory]
    [InlineData(-50, 100)]
    [InlineData(0, 100)]
    [InlineData(25, 100)]
    [InlineData(99, 100)]
    [InlineData(100, 100)]
    [InlineData(150, 150)]
    [InlineData(700, 700)]
    [InlineData(40000, 32000)]
    public void CursorZoomPercentIsClampedToInspectionRange(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampCursorZoomPercent(input));
    }

    [Theory]
    [InlineData(25, 25)]
    [InlineData(50, 50)]
    [InlineData(40000, 32000)]
    public void CursorZoomPercentAllowsZoomOutWhenEnabled(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampCursorZoomPercent(input, allowZoomOut: true));
    }

    [Theory]
    [InlineData(AkronCursorZoomActivationMode.Hold, AkronCursorZoomActivationMode.Hold)]
    [InlineData(AkronCursorZoomActivationMode.Toggle, AkronCursorZoomActivationMode.Toggle)]
    [InlineData((AkronCursorZoomActivationMode)99, AkronCursorZoomActivationMode.Hold)]
    public void CursorZoomActivationModeNormalizesToSupportedModes(AkronCursorZoomActivationMode input, AkronCursorZoomActivationMode expected)
    {
        Assert.Equal(expected, AkronModuleSettings.NormalizeCursorZoomActivationMode(input));
    }

    [Theory]
    [InlineData(-5, 10)]
    [InlineData(0, 10)]
    [InlineData(1, 1)]
    [InlineData(25, 25)]
    [InlineData(250, 100)]
    public void CursorZoomStepPercentIsClampedToWheelRange(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampCursorZoomStepPercent(input));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void CursorZoomLevelResetIsSkippedWhileExtendedCameraOwnsZoom(bool zoomApplied, bool extendedCameraOwnsZoom, bool expected)
    {
        Assert.Equal(expected, AkronModule.ShouldResetCursorZoomLevelState(zoomApplied, extendedCameraOwnsZoom));
    }

    [Theory]
    [InlineData(0.5f, true, true, true)]
    [InlineData(1f, true, true, false)]
    [InlineData(2f, true, true, false)]
    [InlineData(0.5f, false, true, false)]
    [InlineData(0.5f, true, false, false)]
    public void ExtendedCameraCursorZoomOnlyHandlesAvailableZoomOut(float zoom, bool allowZoomOut, bool extendedCameraAvailable, bool expected)
    {
        Assert.Equal(expected, AkronModule.ShouldUseExtendedCameraCursorZoom(zoom, allowZoomOut, extendedCameraAvailable));
    }

    [Theory]
    [InlineData(0.25f, 1f)]
    [InlineData(0.5f, 1f)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 2f)]
    public void NativeCursorZoomNeverZoomsOut(float zoom, float expected)
    {
        Assert.Equal(expected, AkronModule.ClampNativeCursorZoom(zoom));
    }

    [Fact]
    public void ExtendedCameraCursorZoomOutCentersOnPlayer()
    {
        Vector2 center = AkronModule.CalculateExtendedCameraCursorZoomOutCenter(
            TestVector2(5000f, -7000f),
            0.2f,
            TestVector2(8960f, -7408f));

        Assert.Equal(8960f, center.X);
        Assert.Equal(-7408f, center.Y);
    }

    [Fact]
    public void ExtendedCameraCursorZoomOutFallsBackToCurrentCameraCenterWithoutPlayer()
    {
        Vector2 center = AkronModule.CalculateExtendedCameraCursorZoomOutCenter(
            TestVector2(5000f, -7000f),
            0.2f,
            null);

        Assert.Equal(5800f, center.X);
        Assert.Equal(-6550f, center.Y);
    }

    [Fact]
    public void CustomHudLabelDefaultsUseAkronStyleContainers()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.False(settings.LabelSystemVisible);
        Assert.Equal(0xFFFFFF, settings.RoomLabelColor);
        Assert.Equal(0xFFFFFF, settings.InputHistoryTextColor);
        Assert.Equal(0xFFFFFF, settings.InputHistoryEventColor);
        Assert.Equal(0xFFFFFF, settings.RoomTimerColor);
        Assert.Equal(0xFFFFFF, settings.DeathStatsColor);
        Assert.Equal(0xFFFFFF, settings.TotalAttemptsColor);
        Assert.Equal(0xFFFFFF, settings.StatusLabelsColor);
        Assert.Equal(0xFFFFFF, settings.StartPosLabelColor);
        Assert.Equal(0xFFFFFF, settings.SpeedNumberColor);
        Assert.False(settings.ToastLabels);
        Assert.Equal(0xFFFFFF, settings.ToastLabelColor);
        Assert.Equal(AkronHudAnchor.BottomLeft, settings.ToastLabelAnchor);
        Assert.True(settings.ToastLabelStyle.Shadow);
        Assert.Equal(0x000000, settings.ToastLabelStyle.ShadowColor);
        Assert.InRange(settings.ToastLabelStyle.ShadowOpacity, 80, 100);
        Assert.Equal(5, settings.CustomHudLabelPadding);
        Assert.Equal(8, settings.CustomHudLabelGap);
        Assert.Equal(100, settings.LabelBulkStyle.Scale);
        Assert.Equal(100, settings.RoomLabelStyle.Opacity);
        Assert.True(settings.StartPosLabelStyle.Shadow);
        Assert.True(settings.InputsPerSecondLabelStyle.Shadow);
        Assert.True(settings.CustomHudLabelDefinitions.Count >= 3);
        foreach (AkronCustomHudLabel label in settings.CustomHudLabelDefinitions)
        {
            Assert.True(label.Visible);
            Assert.Equal(0xFFFFFF, label.Color);
            Assert.Equal(100, label.LineSpacing);
            Assert.True(label.Shadow);
            Assert.InRange(label.ShadowOpacity, 80, 100);
            Assert.InRange(label.Scale, 0.2f, 1.5f);
        }
        Assert.False(settings.CustomHudLabelObstructionEnabled);
        Assert.Equal(AkronLabelObstructionMode.Fade, settings.CustomHudLabelObstructionMode);
        Assert.Equal(35, settings.CustomHudLabelObstructedOpacity);
        Assert.Equal(100, settings.CustomHudLabelObstructionPaddingPixels);
        Assert.False(settings.CustomHudLabelObstructionOnlyOverlappedLabel);
        Assert.Equal(AkronHudAnchor.BottomRight, settings.CustomHudLabelObstructedAnchor);
        Assert.Contains(settings.CustomHudLabelDefinitions, label => label.TextAlignment == AkronLabelTextAlignment.Right);
    }

    [Fact]
    public void CustomHudLabelDefaultsHideGameplayLabelsOutsideLevels()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        AkronCustomHudLabel roomTimer = Assert.Single(settings.CustomHudLabelDefinitions, label => label.Name == "Room Timer");
        AkronCustomHudLabel player = Assert.Single(settings.CustomHudLabelDefinitions, label => label.Name == "Player");
        AkronCustomHudLabel runState = Assert.Single(settings.CustomHudLabelDefinitions, label => label.Name == "Run State");

        Assert.False(AkronCustomHudLabels.CanRenderInScene(roomTimer, hasLevel: false));
        Assert.False(AkronCustomHudLabels.CanRenderInScene(player, hasLevel: false));
        Assert.True(AkronCustomHudLabels.CanRenderInScene(runState, hasLevel: false));
        Assert.True(AkronCustomHudLabels.CanRenderInScene(roomTimer, hasLevel: true));
        Assert.True(AkronCustomHudLabels.CanRenderInScene(player, hasLevel: true));
    }

    [Fact]
    public void LabelRowOrderNormalizesBuiltInsAndCustomRows()
    {
        List<AkronCustomHudLabel> labels = new List<AkronCustomHudLabel> {
            new AkronCustomHudLabel { Id = "alpha", Name = "Custom 1" },
            new AkronCustomHudLabel { Id = "beta", Name = "Custom 2" }
        };

        List<string> order = AkronModuleSettings.NormalizeLabelRowOrder(
            new[] {
                AkronModuleSettings.BuildCustomLabelRowKey("beta"),
                "Room Timer",
                "missing",
                "Room Timer"
            },
            labels);

        Assert.Equal(AkronModuleSettings.BuildCustomLabelRowKey("beta"), order[0]);
        Assert.Equal("Room Timer", order[1]);
        Assert.Contains("StartPos HUD", order);
        Assert.Contains(AkronModuleSettings.BuildCustomLabelRowKey("alpha"), order);
        Assert.DoesNotContain("missing", order);
        Assert.Equal(order.Count, order.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ToastsLiveInLabelsWithConfigurableStylePopup()
    {
        List<string> defaultLabelOrder = AkronModuleSettings.BuildDefaultLabelRowOrder();

        Assert.Contains("Toasts", defaultLabelOrder);
        Assert.True(HasOverlayOptionsPopup("Toasts"));
    }

    [Fact]
    public void LabelPlayerOverlapIsATopLevelLabelsControl()
    {
        List<string> labelRows = BuildOverlayEntryLabels("Labels");
        Dictionary<string, string> controls = BuildOverlayEntryControls("Labels");

        Assert.Equal("Visible", labelRows[0]);
        Assert.Equal("Player Overlap", labelRows[1]);
        Assert.Equal("Toggle", controls["Player Overlap"]);
        Assert.Equal("Off", BuildOverlayEntryValue("Labels", "Player Overlap"));
        Assert.True(HasOverlayOptionsPopup("Player Overlap"));
    }

    [Fact]
    public void LabelPlayerOverlapTopLevelActionTogglesEnabledWithoutChangingMode()
    {
        (Func<string> value, Action execute) = BuildOverlayEntryValueAndExecute("Labels", "Player Overlap");

        Assert.Equal("Off", value());

        execute();

        Assert.Equal("On", value());

        execute();

        Assert.Equal("Off", value());
    }

    [Fact]
    public void SaveReplayBufferIsThirdInternalRecorderOption()
    {
        List<string> recorderRows = BuildOverlayEntryLabels("Internal Recorder");

        Assert.Equal("Start Recording", recorderRows[0]);
        Assert.Equal("Stop Recording", recorderRows[1]);
        Assert.Equal("Save Replay Buffer", recorderRows[2]);
    }

    [Fact]
    public void StartPosLabelAndSwitcherLiveInTheirOwnColumns()
    {
        List<string> startPosLabels = BuildOverlayEntryLabels("StartPos");
        List<string> labelLabels = AkronModuleSettings.BuildDefaultLabelRowOrder();

        Assert.Contains("StartPos Switcher", startPosLabels);
        Assert.DoesNotContain("StartPos HUD", startPosLabels);
        Assert.Contains("StartPos HUD", labelLabels);
        Assert.False(HasOverlayOptionsPopup("Place StartPos"));
        Assert.True(HasOverlayOptionsPopup("StartPos HUD"));
    }

    [Fact]
    public void SoundColumnReplacesBotWithoutRawStartPosSnapshotRows()
    {
        List<string> soundLabels = BuildOverlayEntryLabels("Sound");

        Assert.Equal(
            new[] {
                "Audio Splitter",
                "Allow Low Volume",
                "Audio Speed",
                "Pitch Shift",
                "Player",
                "Objects",
                "Entities",
                "Ambience",
                "UI"
            },
            soundLabels);
        Assert.DoesNotContain("Bird Squawk", soundLabels);
        Assert.DoesNotContain("Zip Mover", soundLabels);
        Assert.DoesNotContain("Ear Aid", soundLabels);
        Assert.DoesNotContain("StartPos Snapshot Slot", soundLabels);
        Assert.DoesNotContain("Capture StartPos State", soundLabels);
        Assert.DoesNotContain("Restore StartPos State", soundLabels);
    }

    [Fact]
    public void SoundGroupsExposeChildrenOnlyWhenExpanded()
    {
        AkronOverlay overlay = CreateSoundOverlayForListTest();

        List<string> collapsedLabels = BuildRuntimeOverlayEntryLabels(overlay, "Sound");
        Assert.Contains("Player", collapsedLabels);
        Assert.DoesNotContain("Death", collapsedLabels);

        AddExpandedSoundGroup(overlay, "Player");
        List<string> expandedLabels = BuildRuntimeOverlayEntryLabels(overlay, "Sound");

        Assert.Contains("Death", expandedLabels);
        Assert.Contains("Respawn", expandedLabels);
        Assert.Contains("Golden Death", expandedLabels);
        Assert.DoesNotContain("Zip Mover", expandedLabels);
    }

    [Theory]
    [InlineData("Zip Mover", "Objects", "Zip Mover", "Broken Window")]
    [InlineData("Bird Squawk", "Ambience", "Bird Squawk", "Lightning Ambience")]
    [InlineData("Dialogue", "UI", "Dialogue", "Heart Collect")]
    public void SoundSearchShowsHeaderAndMatchingCollapsedChild(string query, string expectedGroup, string expectedChild, string unrelatedSibling)
    {
        AkronOverlay overlay = CreateSoundOverlayForListTest();

        List<string> labels = BuildFilteredOverlayEntryLabels(overlay, "Sound", query);

        Assert.Contains(expectedGroup, labels);
        Assert.Contains(expectedChild, labels);
        Assert.DoesNotContain(unrelatedSibling, labels);
        Assert.True(labels.IndexOf(expectedGroup) < labels.IndexOf(expectedChild));
    }

    [Fact]
    public void SoundSearchByGroupNameShowsAllChildren()
    {
        AkronOverlay overlay = CreateSoundOverlayForListTest();

        List<string> labels = BuildFilteredOverlayEntryLabels(overlay, "Sound", "Objects");

        Assert.Contains("Objects", labels);
        Assert.Contains("Broken Window", labels);
        Assert.Contains("Zip Mover", labels);
    }

    [Fact]
    public void SearchMatchingIgnoresDynamicEntryValues()
    {
        AkronOverlay overlay = CreateOverlayForListTest();

        List<string> labels = BuildFilteredOverlayEntryLabels(overlay, "Level", "No level");

        Assert.Empty(labels);
    }

    [Fact]
    public void SoundGroupHeadersAreNotBindableActions()
    {
        Dictionary<string, bool> bindableRows = BuildOverlayEntryBindableExposure("Sound");

        Assert.False(bindableRows["Player"]);
        Assert.False(bindableRows["Objects"]);
        Assert.False(bindableRows["Entities"]);
        Assert.False(bindableRows["Ambience"]);
        Assert.False(bindableRows["UI"]);
    }

    [Fact]
    public void RemovedRowsAreNotShownInOverlay()
    {
        Assert.DoesNotContain("Hide HUD", BuildOverlayEntryLabels("Level"));
        Assert.DoesNotContain("Disable Quick Restart Keybind", BuildOverlayEntryLabels("Interface"));
        Assert.DoesNotContain("Restart Level", BuildOverlayEntryLabels("Shortcuts"));
    }

    [Fact]
    public void CommunityPacksOpensStandaloneBrowserInsteadOfTriangleOptions()
    {
        Dictionary<string, string> interfaceControls = BuildOverlayEntryControls("Interface");

        Assert.Equal("Action", interfaceControls["Community Packs"]);
        Assert.False(HasOverlayOptionsPopup("Community Packs"));
    }

    [Fact]
    public void LoggingLivesInInterfaceWithOptionsPopup()
    {
        Dictionary<string, string> interfaceControls = BuildOverlayEntryControls("Interface");

        Assert.Equal("Toggle", interfaceControls["Logging"]);
        Assert.True(HasOverlayOptionsPopup("Logging"));
        Assert.Contains("Logging", BuildOverlayEntryLabels("Interface"));
        Assert.DoesNotContain("Logging", BuildOverlayEntryLabels("Global"));
    }

    [Fact]
    public void TimescaleUsesDirectNumericInputInsteadOfTriangleOptions()
    {
        Dictionary<string, string> globalControls = BuildOverlayEntryControls("Global");

        Assert.Equal("NumericInput", globalControls["Timescale"]);
        Assert.False(HasOverlayOptionsPopup("Timescale"));
    }

    [Fact]
    public void GameplayPresentationRowsLiveInTheirChosenCategories()
    {
        List<string> levelLabels = BuildOverlayEntryLabels("Level");
        List<string> playerLabels = BuildOverlayEntryLabels("Player");
        List<string> shortcutsLabels = BuildOverlayEntryLabels("Shortcuts");

        Assert.Contains("Skip Intro", levelLabels);
        Assert.Contains("Skip Postcards", levelLabels);
        Assert.DoesNotContain("Skip Intro", playerLabels);
        Assert.DoesNotContain("Skip Postcards", playerLabels);
        Assert.Contains("Frame Stepper", playerLabels);
        Assert.Contains("No Stamina Flash", playerLabels);
        Assert.Contains("No Trails", playerLabels);
        Assert.DoesNotContain("No Stamina Flash", levelLabels);
        Assert.DoesNotContain("No Trails", levelLabels);
        Assert.DoesNotContain("Uncomplete Level", shortcutsLabels);
    }

    [Fact]
    public void ReworkedRowsUseExpectedControlsWithPopupConfiguration()
    {
        Dictionary<string, string> levelControls = BuildOverlayEntryControls("Level");
        Dictionary<string, string> playerControls = BuildOverlayEntryControls("Player");
        Dictionary<string, string> soundControls = BuildOverlayEntryControls("Sound");

        Assert.Equal("Action", levelControls["Deload Spinners"]);
        Assert.Equal("Toggle", levelControls["Light Level"]);
        Assert.Equal("Toggle", levelControls["Bloom Level"]);
        Dictionary<string, string> creatorControls = BuildOverlayEntryControls("Creator");
        Assert.Equal("Toggle", creatorControls["Cursor Zoom"]);
        Assert.Equal("Toggle", creatorControls["Cursor Tools"]);
        Assert.Equal("Toggle", creatorControls["Camera Offset"]);
        Assert.Equal("Toggle", playerControls["Dash Redirect"]);
        Assert.Equal("Toggle", playerControls["Grab Mode"]);
        Assert.Equal("GroupHeader", soundControls["Ambience"]);
        Assert.True(HasOverlayOptionsPopup("Deload Spinners"));
        Assert.True(HasOverlayOptionsPopup("Light Level"));
        Assert.True(HasOverlayOptionsPopup("Bloom Level"));
        Assert.True(HasOverlayOptionsPopup("Dash Redirect"));
        Assert.True(HasOverlayOptionsPopup("Grab Mode"));
        Assert.True(HasOverlayOptionsPopup("Bird Squawk"));
        Assert.True(HasOverlayOptionsPopup("Cursor Zoom"));
        Assert.True(HasOverlayOptionsPopup("Cursor Tools"));
        Assert.True(HasOverlayOptionsPopup("Camera Offset"));
    }

    [Fact]
    public void CreatorPanelDropsRedundantAndUnsafeRows()
    {
        List<string> creatorLabels = BuildOverlayEntryLabels("Creator");

        Assert.DoesNotContain("Play Configured TAS", creatorLabels);
        Assert.DoesNotContain("Toggle Force Broker Override", creatorLabels);
        Assert.DoesNotContain("Toggle Unsafe Native Override", creatorLabels);
        Assert.DoesNotContain("Toggle Everest-safe Override", creatorLabels);
        Assert.DoesNotContain("Camera Offset X", creatorLabels);
        Assert.DoesNotContain("Camera Offset Y", creatorLabels);
        Assert.DoesNotContain("Reset Cursor Zoom", creatorLabels);
        Assert.DoesNotContain("Open Mountain Viewer", creatorLabels);
        Assert.DoesNotContain("Toggle Editable Flag", creatorLabels);
        Assert.DoesNotContain("Toggle Selected Visible Flag", creatorLabels);
        Assert.DoesNotContain("Previous Visible Flag", creatorLabels);
        Assert.DoesNotContain("Next Visible Flag", creatorLabels);
    }

    [Fact]
    public void CreatorPanelGroupsNavigationActionsByScope()
    {
        List<string> creatorLabels = BuildOverlayEntryLabels("Creator");

        Assert.Equal(
            new[] {
                "Camera Offset",
                "Cursor Tools",
                "Cursor Zoom",
                "Entity Inspector",
                "Free Camera",
                "Map Capture",
                "Room Capture",
                "Warp Selected Room",
                "Previous Room",
                "Next Room",
                "Previous Room In Order",
                "Next Room In Order",
                "Previous Checkpoint",
                "Next Checkpoint",
                "Previous Map",
                "Next Map",
                "Open Debug Map",
                "Export Room Stats",
                "Export Room Times"
            },
            creatorLabels);
    }

    [Fact]
    public void CheckpointNavigationIncludesImplicitStartCheckpoint()
    {
        List<CheckpointData> checkpoints = AkronActions.BuildCheckpointOrder(
            new[] {
                CreateCheckpointData("b-00", "Crossing"),
                CreateCheckpointData("c-00", "Collapse")
            },
            "a-00");

        Assert.Equal(new[] { "a-00", "b-00", "c-00" }, checkpoints.Select(checkpoint => checkpoint.Level).ToArray());
        Assert.Equal("Start", checkpoints[0].Name);
        Assert.Null(AkronActions.ResolveCheckpointLevelForLoad(checkpoints[0], "a-00", implicitStartCheckpoint: true));
    }

    [Fact]
    public void CheckpointNavigationDoesNotDuplicateExplicitStartCheckpoint()
    {
        List<CheckpointData> checkpoints = AkronActions.BuildCheckpointOrder(
            new[] {
                CreateCheckpointData("a-00", "Start"),
                CreateCheckpointData("b-00", "Crossing")
            },
            "a-00");

        Assert.Equal(new[] { "a-00", "b-00" }, checkpoints.Select(checkpoint => checkpoint.Level).ToArray());
        Assert.Equal("a-00", AkronActions.ResolveCheckpointLevelForLoad(checkpoints[0], "a-00", implicitStartCheckpoint: false));
    }

    [Fact]
    public void NumericTogglesUseTriangleOptionsInsteadOfInlineInputs()
    {
        Dictionary<string, string> levelControls = BuildOverlayEntryControls("Level");
        Dictionary<string, string> playerControls = BuildOverlayEntryControls("Player");
        Dictionary<string, string> soundControls = BuildOverlayEntryControls("Sound");

        Assert.Equal("Toggle", levelControls["Respawn Time"]);
        Assert.Equal("Toggle", levelControls["Pause Timer"]);
        Assert.Equal("Toggle", playerControls["Fast Lookout"]);
        Assert.Equal("Toggle", soundControls["Audio Speed"]);
        Assert.Equal("Toggle", soundControls["Pitch Shift"]);
        Assert.True(HasOverlayOptionsPopup("Respawn Time"));
        Assert.True(HasOverlayOptionsPopup("Pause Timer"));
        Assert.True(HasOverlayOptionsPopup("Fast Lookout"));
        Assert.True(HasOverlayOptionsPopup("Audio Speed"));
        Assert.True(HasOverlayOptionsPopup("Pitch Shift"));
    }

    [Fact]
    public void SpawnEntityShortcutsUseButtonRowsInsteadOfTriangleOptions()
    {
        Dictionary<string, string> shortcutControls = BuildOverlayEntryControls("Shortcuts");

        Assert.Equal("Action", shortcutControls["Spawn Jelly"]);
        Assert.Equal("Action", shortcutControls["Spawn Theo"]);
        Assert.False(HasOverlayOptionsPopup("Spawn Jelly"));
        Assert.False(HasOverlayOptionsPopup("Spawn Theo"));
    }

    [Fact]
    public void SoundVolumeOverridesDefaultOff()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.All(AkronEarAid.Sounds, sound => Assert.False(settings.SoundVolumeOverrides[sound.Key]));
        Assert.All(AkronEarAid.Sounds, sound => Assert.Equal(100, settings.SoundVolumes[sound.Key]));
    }

    [Fact]
    public void KeybindsStackDirectlyUnderBypass()
    {
        string[] tabs = GetOverlayTabs();
        int bypassIndex = Array.IndexOf(tabs, "Bypass");
        int keybindsIndex = Array.IndexOf(tabs, "Keybinds");

        Assert.True(bypassIndex >= 0);
        Assert.Equal(bypassIndex + 1, keybindsIndex);
    }

    [Fact]
    public void KeybindOverviewRowsRemainEditable()
    {
        Dictionary<string, string> controls = BuildOverlayEntryControls("Keybinds");

        Assert.Equal("Keybind", controls["Open Menu"]);
        Assert.Equal("Keybind", controls["Retry"]);
        Assert.Equal("Keybind", controls["Capture StartPos State"]);
    }

    [Fact]
    public void ScreenshotCaptureRowsAreSeparateCreatorActions()
    {
        List<string> interfaceLabels = BuildOverlayEntryLabels("Interface");
        List<string> creatorLabels = BuildOverlayEntryLabels("Creator");

        Assert.Contains("Room Capture", creatorLabels);
        Assert.Contains("Map Capture", creatorLabels);
        Assert.DoesNotContain("Room Capture", interfaceLabels);
        Assert.DoesNotContain("Map Capture", interfaceLabels);
        Assert.DoesNotContain("Screenshot Scanner", interfaceLabels);
    }

    [Fact]
    public void LabelStyleCloneClampsCurrentStyleSurface()
    {
        AkronHudLabelStyleSettings style = AkronModuleSettings.CloneLabelStyle(new AkronHudLabelStyleSettings
        {
            OffsetX = -4000,
            OffsetY = 4000,
            Scale = 999,
            Opacity = -1,
            LineSpacing = 999,
            ShadowColor = -1,
            ShadowOpacity = 999,
            ShadowOffsetX = 99,
            ShadowOffsetY = -99
        });

        Assert.Equal(-1920, style.OffsetX);
        Assert.Equal(1080, style.OffsetY);
        Assert.Equal(250, style.Scale);
        Assert.Equal(0, style.Opacity);
        Assert.Equal(250, style.LineSpacing);
        Assert.Equal(0, style.ShadowColor);
        Assert.Equal(100, style.ShadowOpacity);
        Assert.Equal(24, style.ShadowOffsetX);
        Assert.Equal(-24, style.ShadowOffsetY);
    }

    [Fact]
    public void HudCheatIndicatorDefaultsMatchAkronDot()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.False(settings.HudCheatIndicator);
        Assert.Equal(AkronHudCheatIndicatorStyle.Dot, settings.HudCheatIndicatorStyle);
        Assert.Equal(AkronHudAnchor.TopLeft, settings.HudCheatIndicatorAnchor);
        Assert.False(settings.HudCheatIndicatorOnlyFlagged);
        Assert.Equal(100, settings.HudCheatIndicatorScale);
        Assert.Equal(100, settings.HudCheatIndicatorOpacity);
    }

    [Fact]
    public void HudCheatIndicatorOpacityIncludesBulkLabelOpacity()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            HudCheatIndicatorOpacity = 50,
            LabelBulkStyle = new AkronHudLabelStyleSettings
            {
                Opacity = 40
            }
        };

        Assert.Equal(0.2f, AkronHudRenderer.CalculateHudCheatIndicatorOpacity(settings), precision: 3);
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(0, 0)]
    [InlineData(36, 36)]
    [InlineData(400, 240)]
    public void CustomHudLabelPaddingClampUsesDefaultAndBounds(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampCustomLabelPadding(input));
    }

    [Theory]
    [InlineData(-1, 8)]
    [InlineData(0, 0)]
    [InlineData(12, 12)]
    [InlineData(400, 120)]
    public void CustomHudLabelGapClampUsesDefaultAndBounds(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampCustomLabelGap(input));
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(0, 100)]
    [InlineData(50, 50)]
    [InlineData(125, 125)]
    [InlineData(400, 250)]
    public void CustomHudLabelLineSpacingClampUsesDefaultAndBounds(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampCustomLabelLineSpacing(input));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    [InlineData(150, 150)]
    [InlineData(450, 400)]
    public void CustomHudLabelObstructionPaddingClampUsesBounds(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampCustomLabelObstructionPaddingPixels(input));
    }

    [Fact]
    public void CustomHudLabelPlayerOverlapUsesConfigurablePadding()
    {
        Assert.False(AkronCustomHudLabels.PlayerIntersectsLabelResponseArea(
            100f, 100f, 200f, 40f,
            305f, 120f, 8f, 11f,
            0));
        Assert.True(AkronCustomHudLabels.PlayerIntersectsLabelResponseArea(
            100f, 100f, 200f, 40f,
            305f, 120f, 8f, 11f,
            10));
    }

    [Fact]
    public void CustomHudLabelPlayerOverlapPaddingUsesAbsoluteHudPixels()
    {
        Assert.True(AkronCustomHudLabels.PlayerIntersectsLabelResponseArea(
            100f, 100f, 220f, 32f,
            369f, 112f, 8f, 11f,
            50));
        Assert.True(AkronCustomHudLabels.PlayerIntersectsLabelResponseArea(
            100f, 100f, 220f, 32f,
            180f, 181f, 8f, 11f,
            50));
        Assert.False(AkronCustomHudLabels.PlayerIntersectsLabelResponseArea(
            100f, 100f, 220f, 32f,
            370f, 112f, 8f, 11f,
            50));
        Assert.False(AkronCustomHudLabels.PlayerIntersectsLabelResponseArea(
            100f, 100f, 220f, 32f,
            180f, 182f, 8f, 11f,
            50));
    }

    [Fact]
    public void AutoDeafenDefaultsAreAreaBasedAndInactive()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.False(settings.AutoDeafen);
        Assert.Empty(AkronModuleSettings.DefaultAutoDeafenHotkey);
        Assert.Empty(settings.AutoDeafenHotkey);
        Assert.False(settings.AutoDeafenArea);
        Assert.True(settings.AutoDeafenShowArea);
        Assert.Empty(settings.AutoDeafenAreas);
        Assert.Equal(0, settings.AutoDeafenAreaWidth);
        Assert.Equal(0, settings.AutoDeafenAreaHeight);
    }

    [Fact]
    public void HazardAccuracyDefaultsResetRuntimeCustomizations()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            NoclipAccuracyInvalidLimit = 7,
            NoclipAccuracyTint = true,
            NoclipAccuracyTintMode = AkronNoclipAccuracyTintMode.WhileTouching,
            NoclipAccuracyTintColor = 0x00FF00,
            NoclipAccuracyTintOpacity = 100,
            NoclipAccuracyTintDurationMs = 2000
        };

        settings.ResetHazardAccuracyDefaults();

        Assert.Equal(AkronModuleSettings.DefaultHazardAccuracyInvalidLimit, settings.NoclipAccuracyInvalidLimit);
        Assert.Equal(AkronModuleSettings.DefaultHazardAccuracyTint, settings.NoclipAccuracyTint);
        Assert.Equal(AkronModuleSettings.DefaultHazardAccuracyTintMode, settings.NoclipAccuracyTintMode);
        Assert.Equal(AkronModuleSettings.DefaultHazardAccuracyTintColor, settings.NoclipAccuracyTintColor);
        Assert.Equal(AkronModuleSettings.DefaultHazardAccuracyTintOpacity, settings.NoclipAccuracyTintOpacity);
        Assert.Equal(AkronModuleSettings.DefaultHazardAccuracyTintDurationMs, settings.NoclipAccuracyTintDurationMs);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void HazardAccuracySamplesOnlyMovementAndInvalidContact(bool invalid, bool moved, bool expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ShouldCountHazardAccuracySample(invalid, moved));
    }

    [Theory]
    [InlineData(180f, 180, false)]
    [InlineData(180.01f, 180, true)]
    public void HazardAccuracyTreatsBottomKillboxAsInvalidContact(float playerTop, int levelBottom, bool expected)
    {
        Assert.Equal(expected, AkronModule.IsPlayerTouchingBottomKillbox(playerTop, levelBottom));
    }

    [Theory]
    [InlineData(244f, 180, false)]
    [InlineData(244.01f, 180, true)]
    public void InvincibilityRescueKeepsBottomlessFallRecoveryBuffer(float playerTop, int levelBottom, bool expected)
    {
        Assert.Equal(expected, AkronModule.IsPlayerPastBottomKillboxRescueBoundary(playerTop, levelBottom));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void HazardAccuracyRecordsBottomKillboxBeforeInvincibilityRescue(bool hazardAccuracyAllowed, bool touchingBottomKillbox, bool expected)
    {
        Assert.Equal(expected, AkronModule.ShouldRecordBottomKillboxHazardAccuracyBeforeRescue(hazardAccuracyAllowed, touchingBottomKillbox));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, true, true, false)]
    public void AkronInvincibilitySuppressesOnlyNormalDeaths(bool evenIfInvincible, bool hazardAccuracyAllowed, bool akronInvincibilityAllowed, bool expected)
    {
        Assert.Equal(expected, AkronModule.ShouldSuppressNormalDeathForAkronInvincibility(evenIfInvincible, hazardAccuracyAllowed, akronInvincibilityAllowed));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    public void AkronInvincibilityCanSkipVanillaSquishBeforeCollisionMutates(bool akronInvincibilityAllowed, bool allowCollisionChanges, bool forcedSquish, bool expected)
    {
        Assert.Equal(expected, AkronModule.ShouldSkipVanillaSquishForAkronInvincibility(akronInvincibilityAllowed, allowCollisionChanges, forcedSquish));
    }

    [Theory]
    [InlineData(-1f, 3f)]
    [InlineData(0f, 3f)]
    [InlineData(0.05f, 0.1f)]
    [InlineData(2.5f, 2.5f)]
    [InlineData(30f, 15f)]
    public void PauseCountdownClampUsesDefaultAndBounds(float input, float expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampPauseCountdownSeconds(input), precision: 3);
    }

    [Theory]
    [InlineData(-1, 300)]
    [InlineData(0, 300)]
    [InlineData(1, 1)]
    [InlineData(120, 120)]
    [InlineData(1200, 1000)]
    public void ShowTrajectoryFramesClampUsesDefaultAndBounds(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampShowTrajectoryFrames(input));
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(6, 6)]
    [InlineData(20, 12)]
    public void ShowTrajectoryLineThicknessClampUsesDefaultAndBounds(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampShowTrajectoryLineThickness(input));
    }

    [Theory]
    [InlineData(-1, 6)]
    [InlineData(0, 6)]
    [InlineData(1, 1)]
    [InlineData(12, 12)]
    [InlineData(120, 60)]
    public void ShowTrajectoryFrameHitboxIntervalClampUsesDefaultAndBounds(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampShowTrajectoryFrameHitboxInterval(input));
    }

    [Theory]
    [InlineData(-1, 30)]
    [InlineData(0, 30)]
    [InlineData(1, 1)]
    [InlineData(60, 60)]
    [InlineData(400, 240)]
    public void HitboxTrailLengthClampUsesDefaultAndBounds(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampHitboxTrailLength(input));
    }

    [Theory]
    [InlineData(-1f, 1f)]
    [InlineData(0f, 1f)]
    [InlineData(0.5f, 1f)]
    [InlineData(1f, 1f)]
    [InlineData(3f, 3f)]
    [InlineData(5.5f, 5.5f)]
    [InlineData(12f, 8f)]
    public void HitboxLineThicknessClampAllowsThinLines(float input, float expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampHitboxLineThickness(input));
    }

    [Fact]
    public void HitboxStyleResetRestoresTrailDefaults()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            ShowHitboxTrail = true,
            HitboxTrailLength = 120,
            HitboxTrailOpacity = 20,
            HitboxLineThickness = 4f,
            HitboxFillOpacity = 50,
            HitboxBlackOutline = true
        };

        settings.ResetHitboxStyle();

        Assert.False(settings.ShowHitboxTrail);
        Assert.Equal(30, settings.HitboxTrailLength);
        Assert.Equal(55, settings.HitboxTrailOpacity);
        Assert.Equal(5f, settings.HitboxLineThickness);
        Assert.Equal(0, settings.HitboxFillOpacity);
        Assert.False(settings.HitboxBlackOutline);
        Assert.Equal(AkronModuleSettings.DefaultHitboxPlayerColor, settings.HitboxPlayerColor);
        Assert.Equal(AkronModuleSettings.DefaultHitboxPlayerHurtboxColor, settings.HitboxPlayerHurtboxColor);
        Assert.Equal(AkronModuleSettings.DefaultHitboxSolidColor, settings.HitboxSolidColor);
        Assert.Equal(AkronModuleSettings.DefaultHitboxHazardColor, settings.HitboxHazardColor);
        Assert.Equal(AkronModuleSettings.DefaultHitboxTriggerColor, settings.HitboxTriggerColor);
        Assert.Equal(AkronModuleSettings.DefaultHitboxOtherColor, settings.HitboxOtherColor);
        Assert.Equal(AkronModuleSettings.DefaultHitboxDeathColor, settings.HitboxDeathColor);
        Assert.Equal(AkronModuleSettings.DefaultHitboxDeathPlayerColor, settings.HitboxDeathPlayerColor);
    }

    [Fact]
    public void HitboxLineThicknessDefaultsToOneNativePixel()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.Equal(5f, settings.HitboxLineThickness);
    }

    [Fact]
    public void HitboxColorDefaultsMatchCelesteTasConvention()
    {
        AkronModuleSettings settings = new AkronModuleSettings();
        AkronSetupState setup = new AkronSetupState();

        Assert.Equal(0xFF0000, settings.HitboxPlayerColor);
        Assert.Equal(0x9ACD32, settings.HitboxPlayerHurtboxColor);
        Assert.Equal(0xFF7F50, settings.HitboxSolidColor);
        Assert.Equal(0xFF0000, settings.HitboxHazardColor);
        Assert.Equal(0x9370DB, settings.HitboxTriggerColor);
        Assert.Equal(0xFF0000, settings.HitboxOtherColor);
        Assert.Equal(0x8B0000, settings.HitboxDeathColor);
        Assert.Equal(0xF5F5F5, settings.HitboxDeathPlayerColor);
        Assert.Equal(settings.HitboxPlayerColor, setup.HitboxPlayerColor);
        Assert.Equal(settings.HitboxPlayerHurtboxColor, setup.HitboxPlayerHurtboxColor);
        Assert.Equal(settings.HitboxSolidColor, setup.HitboxSolidColor);
        Assert.Equal(settings.HitboxHazardColor, setup.HitboxHazardColor);
        Assert.Equal(settings.HitboxTriggerColor, setup.HitboxTriggerColor);
        Assert.Equal(settings.HitboxOtherColor, setup.HitboxOtherColor);
        Assert.Equal(settings.HitboxDeathColor, setup.HitboxDeathColor);
        Assert.Equal(settings.HitboxDeathPlayerColor, setup.HitboxDeathPlayerColor);
    }

    [Fact]
    public void InspectorPinFilterDefaultsToBoth()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.Equal(AkronInspectorPinFilter.Both, settings.InspectorPinFilter);
    }

    [Theory]
    [InlineData(AkronInspectorPinFilter.Entities, AkronInspectorPinFilter.Entities)]
    [InlineData(AkronInspectorPinFilter.Triggers, AkronInspectorPinFilter.Triggers)]
    [InlineData(AkronInspectorPinFilter.Both, AkronInspectorPinFilter.Both)]
    [InlineData((AkronInspectorPinFilter) 99, AkronInspectorPinFilter.Both)]
    public void InspectorPinFilterNormalizationFallsBackToBoth(AkronInspectorPinFilter input, AkronInspectorPinFilter expected)
    {
        Assert.Equal(expected, AkronEntityInspector.NormalizeInspectorPinFilter(input));
    }

    [Fact]
    public void InspectorPinValueFormatterUsesInvariantJsonLikeShapes()
    {
        Dictionary<object, object> dictionary = new Dictionary<object, object>
        {
            { "z", 2 },
            { 1, "number-key" },
            { "a", new Vector2 { X = 1.5f, Y = float.PositiveInfinity } }
        };

        Assert.Equal("\"quote\\\"line\\n\"", AkronEntityInspector.FormatInspectorValue("quote\"line\n"));
        Assert.Equal("true", AkronEntityInspector.FormatInspectorValue(true));
        Assert.Equal("\"NaN\"", AkronEntityInspector.FormatInspectorValue(float.NaN));
        Assert.Equal("{ \"x\": 1.5, \"y\": \"Infinity\" }", AkronEntityInspector.FormatInspectorValue(new Vector2 { X = 1.5f, Y = float.PositiveInfinity }));
        Assert.Equal("[1, \"x\"]", AkronEntityInspector.FormatInspectorValue(new object[] { 1, "x" }));
        Assert.Equal("{ \"1\": \"number-key\", \"a\": { \"x\": 1.5, \"y\": \"Infinity\" }, \"z\": 2 }", AkronEntityInspector.FormatInspectorValue(dictionary));
        Assert.Equal("{ \"unsupported\": \"Celeste.Mod.Akron.Tests.ModuleSettingsTests\" }", AkronEntityInspector.FormatInspectorValue(this));
    }

    [Fact]
    public void InspectorPinCopyReportUsesDisplayedRows()
    {
        AkronInspectorReportData data = new AkronInspectorReportData
        {
            Filter = AkronInspectorPinFilter.Both,
            Room = "room-01",
            MapSid = "Celeste/1-ForsakenCity",
            RoomSessionId = 7,
            StackIndex = 1,
            StackCount = 3,
            StackSignature = "7|Both|10,11,12",
            Category = "Trigger",
            DisplayName = "CameraTargetTrigger",
            FullTypeName = "Celeste.CameraTargetTrigger",
            InspectorId = 11,
            SourceId = "room-01:42",
            Position = new Vector2 { X = 12f, Y = 34f },
            Center = new Vector2 { X = 20f, Y = 40f },
            ColliderKind = "Hitbox",
            ColliderBounds = new Rectangle(12, 34, 16, 12),
            ColliderArea = 192f,
            Depth = -1000000,
            Active = true,
            Visible = false,
            Collidable = false,
            MapPlacementLabel = "Everest source-bound",
            SourceObjectCount = 2,
            SourceOrdinal = 1
        };
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("visible", "false"));
        data.PlacementRows.Add(new AkronInspectorPropertyRow("mapName", "cameraTargetTrigger"));
        data.AuthoredRows.Add(new AkronInspectorPropertyRow("values.width", "16"));
        data.StackEntries.Add("10:Celeste.Spring");
        data.StackEntries.Add("11:Celeste.CameraTargetTrigger");

        string report = AkronEntityInspector.BuildCopyReport(data);

        Assert.Contains("filter: Both", report);
        Assert.Contains("cycle: 2/3", report);
        Assert.Contains("- 11:Celeste.CameraTargetTrigger", report);
        Assert.Contains("mapPlacement: Everest source-bound", report);
        Assert.Contains("runtime:\n  visible: false", report);
        Assert.Contains("placement:\n  mapName: cameraTargetTrigger", report);
        Assert.Contains("authoredProperties:\n  values.width: 16", report);
    }

    [Fact]
    public void PlayerHazardHitboxUsesCelesteTasNormalHurtboxShape()
    {
        (int left, int top, int width, int height) = AkronEntityInspector.PixelExactBoundsParts(19f - 4f, 144f - 11f, 8f, 9f);

        AssertHitboxBounds(15, 133, 8, 9, left, top, width, height);
    }

    [Fact]
    public void PlayerHazardHitboxFallbackUsesCelesteNormalHurtboxShape()
    {
        (int left, int top, int width, int height) = AkronEntityInspector.PixelExactBoundsParts(19f - 4f, 144f - 11f, 8f, 9f);

        AssertHitboxBounds(15, 133, 8, 9, left, top, width, height);
    }

    [Fact]
    public void PlayerHazardHitboxUsesCelesteTasPixelRounding()
    {
        (int left, int top, int width, int height) = AkronEntityInspector.PixelExactBoundsParts(19.25f - 4f, 144.75f - 11f, 8f, 9f);

        AssertHitboxBounds(15, 133, 9, 10, left, top, width, height);
    }

    [Theory]
    [InlineData(typeof(CrystalStaticSpinnerProbe))]
    [InlineData(typeof(DustStaticSpinnerProbe))]
    [InlineData(typeof(DustTrackSpinnerProbe))]
    [InlineData(typeof(DustRotateSpinnerProbe))]
    [InlineData(typeof(TriggerSpikesProbe))]
    public void HitboxHazardClassifierCoversCsideHazardTypes(Type entityType)
    {
        Entity entity = Assert.IsAssignableFrom<Entity>(Activator.CreateInstance(entityType));

        Assert.True(AkronEntityInspector.IsHazard(entity));
    }

    [Fact]
    public void HitboxHazardClassifierDoesNotTreatRefillsAsHazards()
    {
        Assert.False(AkronEntityInspector.IsHazard(new RefillProbe()));
    }

    [Fact]
    public void HitboxRendererUsesLiveColliderTraversalInsteadOfArtificialSpinnerGeometry()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-entity-inspector.cs"));

        Assert.Contains("case ColliderList colliderList:", source);
        Assert.Contains("foreach (Collider child in colliderList.colliders)", source);
        Assert.Contains("DrawExactColliderPixels(level, child, color, cameraBounds);", source);
        Assert.DoesNotContain("ShouldDrawLineAndCircleSpinnerHitbox", source);
        Assert.DoesNotContain("SpinnerColliderPart", source);
        Assert.DoesNotContain("SpinnerCompositeCenter", source);
        Assert.DoesNotContain("SpinnerLineBounds", source);
    }

    private static void AssertHitboxBounds(int expectedLeft, int expectedTop, int expectedWidth, int expectedHeight, int left, int top, int width, int height)
    {
        Assert.Equal(expectedLeft, left);
        Assert.Equal(expectedTop, top);
        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    private sealed class CrystalStaticSpinnerProbe : Entity
    {
    }

    private sealed class DustStaticSpinnerProbe : Entity
    {
    }

    private sealed class DustTrackSpinnerProbe : Entity
    {
    }

    private sealed class DustRotateSpinnerProbe : Entity
    {
    }

    private sealed class TriggerSpikesProbe : Entity
    {
    }

    private sealed class RefillProbe : Entity
    {
    }

    private sealed class CustomOneUseRefillProbe : Entity
    {
        public bool oneUse = true;
    }

    private sealed class CustomReusableRefillProbe : Entity
    {
        public bool oneUse = false;
    }

    private sealed class CustomOnlyOnceRefillProbe : Entity
    {
        public bool onlyOnce { get; } = true;
    }

    private sealed class CustomOneUseGemProbe : Entity
    {
        public bool oneUse = true;
    }

    private static T MakeLive<T>(T entity, bool visible = true, bool collidable = true) where T : Entity
    {
        entity.Visible = visible;
        entity.Collidable = collidable;
        return entity;
    }

    [Fact]
    public void ExactColliderPixelRunsUseColliderCollisionInsteadOfBoundingCircleApproximation()
    {
        const float radius = 4f;
        bool CircleIntersectsPixel(int x, int y)
        {
            float nearestX = Math.Clamp(0f, x, x + 1f);
            float nearestY = Math.Clamp(0f, y, y + 1f);
            return nearestX * nearestX + nearestY * nearestY <= radius * radius;
        }

        List<(int X, int Y, int Width)> outlineRuns = AkronEntityInspector.ExactPixelRunSegments(-5, -5, 10, 10, CircleIntersectsPixel, includeFill: false);
        List<(int X, int Y, int Width)> fillRuns = AkronEntityInspector.ExactPixelRunSegments(-5, -5, 10, 10, CircleIntersectsPixel, includeFill: true);

        int outlinePixelCount = outlineRuns.Sum(run => run.Width);
        int fillPixelCount = fillRuns.Sum(run => run.Width);

        Assert.NotEmpty(outlineRuns);
        Assert.True(fillPixelCount >= outlinePixelCount);
        Assert.All(outlineRuns.SelectMany(ExpandRun), pixel =>
        {
            Assert.True(CircleIntersectsPixel(pixel.X, pixel.Y));
            Assert.True(
                !CircleIntersectsPixel(pixel.X - 1, pixel.Y) ||
                !CircleIntersectsPixel(pixel.X + 1, pixel.Y) ||
                !CircleIntersectsPixel(pixel.X, pixel.Y - 1) ||
                !CircleIntersectsPixel(pixel.X, pixel.Y + 1));
        });

        static IEnumerable<(int X, int Y)> ExpandRun((int X, int Y, int Width) run)
        {
            for (int x = run.X; x < run.X + run.Width; x++)
            {
                yield return (x, run.Y);
            }
        }
    }

    [Fact]
    public void ExactPixelRunsIncludeEveryRectangleEdge()
    {
        static bool RectangleIntersectsPixel(int x, int y)
        {
            return x >= 10 && x < 14 && y >= 20 && y < 24;
        }

        HashSet<(int X, int Y)> outlinePixels = AkronEntityInspector
            .ExactPixelRunSegments(9, 19, 6, 6, RectangleIntersectsPixel, includeFill: false)
            .SelectMany(ExpandRun)
            .ToHashSet();

        Assert.Contains((10, 20), outlinePixels);
        Assert.Contains((13, 20), outlinePixels);
        Assert.Contains((10, 23), outlinePixels);
        Assert.Contains((13, 23), outlinePixels);
        Assert.DoesNotContain((11, 21), outlinePixels);
        Assert.DoesNotContain((12, 22), outlinePixels);

        static IEnumerable<(int X, int Y)> ExpandRun((int X, int Y, int Width) run)
        {
            for (int x = run.X; x < run.X + run.Width; x++)
            {
                yield return (x, run.Y);
            }
        }
    }

    [Fact]
    public void ExactPixelRunsPreserveRunsStartingAtNegativeWorldX()
    {
        static bool NegativeRectangleIntersectsPixel(int x, int y)
        {
            return x >= -4 && x < -1 && y >= 2 && y < 4;
        }

        List<(int X, int Y, int Width)> fillRuns =
            AkronEntityInspector.ExactPixelRunSegments(-6, 0, 8, 6, NegativeRectangleIntersectsPixel, includeFill: true);

        Assert.Contains((-4, 2, 3), fillRuns);
        Assert.Contains((-4, 3, 3), fillRuns);
    }

    [Fact]
    public void RegularCircleHitboxesUsePixelRunsInsteadOfSmoothCircleSegments()
    {
        List<(int X, int Y, int Width)> outlineRuns = AkronEntityInspector.PixelCircleRunSegments(0f, 0f, 4f, includeFill: false);
        HashSet<(int X, int Y)> outlinePixels = outlineRuns.SelectMany(ExpandRun).ToHashSet();

        Assert.NotEmpty(outlineRuns);
        Assert.Contains((0, -4), outlinePixels);
        Assert.Contains((1, -4), outlinePixels);
        Assert.Contains((3, -1), outlinePixels);
        Assert.Contains((3, 0), outlinePixels);
        Assert.DoesNotContain((0, 0), outlinePixels);

        static IEnumerable<(int X, int Y)> ExpandRun((int X, int Y, int Width) run)
        {
            for (int x = run.X; x < run.X + run.Width; x++)
            {
                yield return (x, run.Y);
            }
        }
    }

    [Fact]
    public void RegularCircleHitboxesPreserveNegativeWorldCoordinates()
    {
        List<(int X, int Y, int Width)> outlineRuns = AkronEntityInspector.PixelCircleRunSegments(-144f, 24f, 6f, includeFill: false);
        HashSet<(int X, int Y)> outlinePixels = outlineRuns.SelectMany(ExpandRun).ToHashSet();

        Assert.NotEmpty(outlineRuns);
        Assert.Contains((-144, 18), outlinePixels);
        Assert.Contains((-150, 24), outlinePixels);
        Assert.Contains((-139, 24), outlinePixels);
        Assert.All(outlinePixels, pixel => Assert.True(pixel.X < 0));

        static IEnumerable<(int X, int Y)> ExpandRun((int X, int Y, int Width) run)
        {
            for (int x = run.X; x < run.X + run.Width; x++)
            {
                yield return (x, run.Y);
            }
        }
    }

    [Fact]
    public void PixelOutlineSamplesIncludeStackedEdgePixelsForVisualConnectors()
    {
        static bool Shape(int x, int y)
        {
            return (x == 0 && y >= 0 && y <= 2) ||
                   (x == 1 && y == 1);
        }

        HashSet<(int X, int Y)> samples =
            AkronEntityInspector.ExactPixelOutlineSamples(-1, -1, 4, 5, Shape);

        Assert.Contains((0, 0), samples);
        Assert.Contains((0, 1), samples);
        Assert.Contains((0, 2), samples);
        Assert.Contains((1, 1), samples);
        Assert.True(samples.Contains((0, 0)) && samples.Contains((0, 1)));
        Assert.True(samples.Contains((0, 1)) && samples.Contains((0, 2)));
    }

    [Fact]
    public void GridOutlineEdgesSkipSharedBordersBetweenStackedCells()
    {
        static bool CellFilled(int x, int y)
        {
            return x == 0 && (y == 0 || y == 1);
        }

        List<(int CellX, int CellY, GridEdge Edge)> edges =
            AkronEntityInspector.GridOutlineEdges(0, 0, 1, 2, CellFilled);

        Assert.DoesNotContain((0, 0, GridEdge.Bottom), edges);
        Assert.DoesNotContain((0, 1, GridEdge.Top), edges);
        Assert.Contains((0, 0, GridEdge.Top), edges);
        Assert.Contains((0, 0, GridEdge.Left), edges);
        Assert.Contains((0, 0, GridEdge.Right), edges);
        Assert.Contains((0, 1, GridEdge.Bottom), edges);
        Assert.Contains((0, 1, GridEdge.Left), edges);
        Assert.Contains((0, 1, GridEdge.Right), edges);
    }

    [Fact]
    public void ShowTrajectoryMapAwareDefaultsAreConservativeAndOptional()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.False(settings.ShowTrajectoryMapAware);
        Assert.True(settings.ShowTrajectoryStopOnSolids);
        Assert.True(settings.ShowTrajectoryStopOnHazards);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(0, 100)]
    [InlineData(1, 50)]
    [InlineData(125, 125)]
    [InlineData(400, 300)]
    public void ResourcePlayerScaleClampUsesDefaultAndBounds(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampResourcePlayerScale(input));
    }

    [Theory]
    [InlineData(-2f, 1f)]
    [InlineData(0f, 1f)]
    [InlineData(0.05f, 0.1f)]
    [InlineData(1.5f, 1.5f)]
    [InlineData(10f, 4f)]
    public void AudioMultiplierClampUsesDefaultAndBounds(float input, float expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampAudioMultiplier(input), precision: 3);
    }

    [Theory]
    [InlineData(-1, 120)]
    [InlineData(0, 120)]
    [InlineData(59, 60)]
    [InlineData(144, 144)]
    [InlineData(999, 480)]
    public void FpsTargetClampMatchesMotionSmoothingRange(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampFpsTarget(input));
    }

    [Theory]
    [InlineData(-1, 60)]
    [InlineData(0, 60)]
    [InlineData(29, 30)]
    [InlineData(120, 120)]
    [InlineData(999, 480)]
    public void TpsTargetClampKeepsSimulationRateBounded(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampTpsTarget(input));
    }

    [Fact]
    public void FrameBypassDefaultsMatchMotionSmoothingReferenceOptions()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.Equal(AkronFrameIncreaseMethod.Interval, settings.FrameBypassMethod);
        Assert.Equal(AkronCameraSmoothingMode.Fancy, settings.FrameBypassCameraSmoothing);
        Assert.Equal(AkronObjectSmoothingMode.Extrapolate, settings.FrameBypassObjectSmoothing);
        Assert.False(settings.FrameBypassSubpixelMadeline);
        Assert.False(settings.FrameBypassSmoothBackground);
        Assert.False(settings.FrameBypassSmoothForeground);
        Assert.False(settings.FrameBypassHideStretchedEdges);
        Assert.False(settings.FrameBypassTasMode);
        Assert.False(settings.FrameBypassSillyMode);
    }

    [Fact]
    public void FrameBypassIntervalRoundsDrawRateToUpdateMultiple()
    {
        AkronFrameBypassRates rates = AkronModuleSettings.ResolveFrameBypassRates(
            fpsBypass: true,
            fpsTarget: 144,
            tpsBypass: false,
            tpsTarget: 60,
            method: AkronFrameIncreaseMethod.Interval);

        Assert.True(rates.Active);
        Assert.Equal(60, rates.UpdateRate);
        Assert.Equal(180, rates.DrawRate);
        Assert.Equal(144, rates.RequestedDrawRate);
    }

    [Fact]
    public void FrameBypassDynamicKeepsArbitraryDrawRate()
    {
        AkronFrameBypassRates rates = AkronModuleSettings.ResolveFrameBypassRates(
            fpsBypass: true,
            fpsTarget: 144,
            tpsBypass: true,
            tpsTarget: 60,
            method: AkronFrameIncreaseMethod.Dynamic);

        Assert.True(rates.Active);
        Assert.Equal(60, rates.UpdateRate);
        Assert.Equal(144, rates.DrawRate);
    }

    [Theory]
    [InlineData(-1f, 0.1f)]
    [InlineData(0f, 0.1f)]
    [InlineData(0.05f, 0.1f)]
    [InlineData(1.25f, 1.25f)]
    [InlineData(12f, 10f)]
    public void RespawnTimeClampKeepsAUsableRange(float input, float expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampRespawnTimeSeconds(input), precision: 3);
    }

    [Theory]
    [InlineData(-9, -1)]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(9, 5)]
    public void StartPosDashClampAllowsNativeOrExplicitDashCounts(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampStartPosDashes(input));
    }

    [Theory]
    [InlineData(-20, -1)]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(250, 100)]
    public void StartPosStaminaClampAllowsNativeOrExplicitPercent(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampStartPosStaminaPercent(input));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(9, 9)]
    [InlineData(24, 24)]
    [InlineData(120, 99)]
    public void StartPosSlotCountClampAllowsMoreThanNineSlots(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampStartPosSlotCount(input));
    }

    [Fact]
    public void StartPosCoreActionsHaveIndependentBindings()
    {
        AkronModuleSettings settings = new AkronModuleSettings();
        ButtonBinding set = new ButtonBinding(0, Keys.D1);
        ButtonBinding load = new ButtonBinding(0, Keys.D2);
        ButtonBinding clear = new ButtonBinding(0, Keys.D3);

        settings.SetStartPos = set;
        settings.LoadStartPos = load;
        settings.ClearStartPos = clear;

        Assert.Same(set, settings.SetStartPos);
        Assert.Same(load, settings.LoadStartPos);
        Assert.Same(clear, settings.ClearStartPos);
    }

    [Fact]
    public void StartPosSlotUsesIndependentSetting()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            ActiveStartPosSlot = 7
        };

        Assert.Equal(7, settings.ActiveStartPosSlot);
    }

    [Fact]
    public void StartPosRespawnIsDisabledByDefault()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.False(settings.RespawnAtStartPos);
    }

    [Fact]
    public void StartPosDefaultsDoNotApplySpawnConfig()
    {
        AkronStartPos startPos = new AkronStartPos();

        Assert.False(startPos.UsesSpawnConfig);
        Assert.Equal(-1, startPos.Dashes);
        Assert.Equal(AkronStartPosFacing.Current, startPos.Facing);
        Assert.False(startPos.Idle);
        Assert.False(startPos.Grab);
    }

    [Fact]
    public void ImportedStartPosEntriesDoNotRequireRuntimeSnapshots()
    {
        AkronStartPos imported = new AkronStartPos
        {
            AreaSid = "Example/Map",
            StateSlotName = string.Empty
        };
        AkronStartPos capturedWithoutState = new AkronStartPos
        {
            AreaSid = "Example/Map",
            StateSlotName = "Akron StartPos missing test slot"
        };

        Assert.True(InvokeStartPosInArea(imported, "Example/Map"));
        Assert.False(InvokeStartPosInArea(imported, "Other/Map"));
        Assert.False(InvokeStartPosInArea(capturedWithoutState, "Example/Map"));
    }

    [Fact]
    public void NativeStartPosRuntimeSlotsDoNotUseSpeedrunToolBrokerPath()
    {
        MethodInfo? method = typeof(AkronSaveLoadService).GetMethod("ShouldBrokerRuntimeState", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.False(Assert.IsType<bool>(method!.Invoke(null, new object[] { "Akron StartPos 1" })));
    }

    [Fact]
    public void NativeStartPosRuntimeRestoreSuppressesLagPauserSpike()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/SaveLoad/AkronSaveLoad.cs"));

        Assert.Contains("saveSlot.SlotName.StartsWith(\"Akron StartPos \", StringComparison.Ordinal)", source);
        Assert.Contains("AkronModule.SuppressLagPauserForNativeStartPosRestore();", source);
    }

    [Theory]
    [InlineData("", "snapshot")]
    [InlineData("good-before-load", "good-before-load")]
    [InlineData("bad after load", "bad-after-load")]
    [InlineData("../camera:broken?", "camera-broken")]
    public void DebugSnapshotTagsAreSafeForArtifactNames(string input, string expected)
    {
        Assert.Equal(expected, AkronDebugSnapshot.SanitizeTag(input));
    }

    [Theory]
    [InlineData(-999, -128)]
    [InlineData(-34, -34)]
    [InlineData(999, 128)]
    public void SpeedNumberOffsetClampKeepsHudTextNearPlayer(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampSpeedNumberOffsetY(input));
    }

    [Fact]
    public void PlayerNumbersRenderEvenWhenHudLabelsAreHidden()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            LabelSystemVisible = false,
            DashNumber = true,
            SpeedNumber = true
        };

        Assert.True(InvokePlayerNumberGate("ShouldRenderDashNumber", settings, true));
        Assert.True(InvokePlayerNumberGate("ShouldRenderSpeedNumber", settings, true));
    }

    [Fact]
    public void PlayerNumbersStillRespectTheirOwnToggleAndPolicyGate()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            LabelSystemVisible = true,
            DashNumber = false,
            SpeedNumber = false
        };

        Assert.False(InvokePlayerNumberGate("ShouldRenderDashNumber", settings, true));
        Assert.False(InvokePlayerNumberGate("ShouldRenderSpeedNumber", settings, true));

        settings.DashNumber = true;
        settings.SpeedNumber = true;

        Assert.False(InvokePlayerNumberGate("ShouldRenderDashNumber", settings, false));
        Assert.False(InvokePlayerNumberGate("ShouldRenderSpeedNumber", settings, false));
    }

    [Fact]
    public void AkronHudUsesLinearFilteringForScaledText()
    {
        Assert.Same(SamplerState.LinearClamp, AkronModule.HudSamplerState());
    }

    [Fact]
    public void AreaSelectionPreviewShowsSinglePixelMarkerAtCursorBeforeFirstCorner()
    {
        AkronOverlay.PracticeAreaSelectionPreviewBounds preview = AkronOverlay.PracticeAreaSelectionPreviewBoundsFor(12.8f, 20.2f, false, 0f, 0f);

        AssertPreviewBounds(preview, 12, 20, 1, 1);
    }

    [Fact]
    public void AreaSelectionPreviewShowsDragRectangleAfterFirstCorner()
    {
        AkronOverlay.PracticeAreaSelectionPreviewBounds preview = AkronOverlay.PracticeAreaSelectionPreviewBoundsFor(20.2f, 8.8f, true, 4.4f, 18.2f);

        AssertPreviewBounds(preview, 4, 8, 17, 11);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(65, 65)]
    [InlineData(140, 100)]
    public void LightLevelClampKeepsReadablePercentRange(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampLightLevelPercent(input));
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(125, 125)]
    [InlineData(450, 300)]
    public void BloomLevelClampKeepsVisualOverrideRange(int input, int expected)
    {
        Assert.Equal(expected, AkronModuleSettings.ClampBloomLevelPercent(input));
    }

    [Fact]
    public void LowDistractionOverlayIsDerivedFromAllNoiseChannels()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        settings.SetLowDistractionChannels(true);

        Assert.True(settings.IsLowDistractionActive());
        Assert.Equal("Low-distraction", settings.DescribePresentationOverlays());

        settings.SetNoTrails(false);

        Assert.False(settings.IsLowDistractionActive());
        Assert.Equal("None", settings.DescribePresentationOverlays());
    }

    [Fact]
    public void StreamerModeOnlyRedactsPathDisplay()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            StreamerMode = true
        };

        Assert.Equal("Streamer Mode", settings.DescribePresentationOverlays());
        Assert.Contains("filesystem paths", settings.DescribeOverlayBehavior(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("clear.json", settings.FormatPathForDisplay("/tmp/akron/proof/clear.json"));
        Assert.Equal("AkronRecordings", settings.FormatPathForDisplay("/tmp/akron/AkronRecordings/"));
    }

    [Fact]
    public void BackupStatusDisplayRedactsBackupFolderInStreamerMode()
    {
        string backupFolder = "/tmp/akron/Saves/AkronBackups";
        string status = "Open folder failed: access denied to " + backupFolder;

        string redacted = AkronBackupActions.FormatBackupTextForDisplay(status, backupFolder, streamerMode: true);

        Assert.Equal("Open folder failed: access denied to AkronBackups", redacted);
        Assert.DoesNotContain("/tmp/akron", redacted);
    }

    [Fact]
    public void PathDisplayKeepsFullPathOutsideStreamerMode()
    {
        AkronModuleSettings settings = new AkronModuleSettings();

        Assert.Equal("/tmp/akron/proof/clear.json", settings.FormatPathForDisplay("/tmp/akron/proof/clear.json"));
    }

    [Fact]
    public void OneShotRuntimeActionsAreClearedOnSettingsLoad()
    {
        AkronModuleSettings settings = new AkronModuleSettings
        {
            DeloadSpinners = true,
            DeloadSpinnerDelaySeconds = 3f
        };

        AkronModuleSettings.ClearOneShotRuntimeActions(settings);

        Assert.False(settings.DeloadSpinners);
        Assert.Equal(3f, settings.DeloadSpinnerDelaySeconds);
    }

    [Fact]
    public void InputBoardNormalizationClampsInvalidElementsWithoutCompatibilityFallback()
    {
        List<AkronInputBoardElement> elements = AkronInputBoard.NormalizeElements(new[] {
            new AkronInputBoardElement {
                Id = "  key  ",
                Label = "  K  ",
                X = -5000,
                Y = 5000,
                Width = 1,
                Height = 999,
                FillColor = -1,
                PressedFillColor = 0xABCDEF,
                StrokeColor = 0x123456,
                TextColor = 0x654321,
                OutlineWidth = 99,
                TextScale = 999,
                Bindings = new List<AkronInputBoardBinding> { AkronInputBoardBinding.Jump, AkronInputBoardBinding.Jump },
                KeyBindings = new List<Keys> { Keys.Space, Keys.Space, Keys.None }
            }
        });

        AkronInputBoardElement element = Assert.Single(elements);
        Assert.Equal("key", element.Id);
        Assert.Equal("K", element.Label);
        Assert.Equal(AkronInputBoard.MinimumPosition, element.X);
        Assert.Equal(AkronInputBoard.MaximumPosition, element.Y);
        Assert.Equal(AkronInputBoard.MinimumElementSize, element.Width);
        Assert.Equal(AkronInputBoard.MaximumElementSize, element.Height);
        Assert.Equal(0, element.FillColor);
        Assert.Equal(8, element.OutlineWidth);
        Assert.Equal(AkronInputBoard.MaximumTextScale, element.TextScale);
        Assert.Equal(new[] { AkronInputBoardBinding.Jump }, element.Bindings);
        Assert.Equal(new[] { Keys.Space }, element.KeyBindings);
    }

    [Fact]
    public void InputBoardDefaultIsCompactKeyboardWithKeyboardLabels()
    {
        AkronModuleSettings settings = new AkronModuleSettings();
        List<AkronInputBoardElement> defaults = AkronInputBoard.BuildDefaultElements();

        Assert.Equal(AkronInputBoardLabelPreset.Keyboard, settings.InputBoardLabelPreset);
        Assert.Equal(AkronInputBoard.Describe(AkronInputBoard.BuildCompactElements()), AkronInputBoard.Describe(defaults));
        Assert.Equal(
            AkronInputBoard.BuildCompactElements().Max(element => element.X + element.Width),
            defaults.Max(element => element.X + element.Width));
        Assert.Contains(defaults, element => element.Id == "up" && element.Label == "W");
    }

    [Fact]
    public void InputBoardPresetsAreVisiblyDistinctAndUseBoringReadableColors()
    {
        List<AkronInputBoardElement> split = AkronInputBoard.BuildSplitElements();
        List<AkronInputBoardElement> compact = AkronInputBoard.BuildCompactElements();
        List<AkronInputBoardElement> keyboard = AkronInputBoard.BuildKeyboardElements();
        List<AkronInputBoardElement> bar = AkronInputBoard.BuildBarElements();

        Assert.NotEqual(AkronInputBoard.Describe(split), AkronInputBoard.Describe(bar));
        Assert.NotEqual(split.Max(element => element.X + element.Width), compact.Max(element => element.X + element.Width));
        Assert.Contains(keyboard, element => element.Label == "Jump 2");
        Assert.True(bar.All(element => element.Y == 0));
        Assert.Contains(split, element => element.Id == "menu" && element.X == 12 && element.Y == 0 && element.Width == 54);
        Assert.Contains(split, element => element.Id == "jump-2" && element.X == 24 && element.Y == 96 && element.Width == 90 && element.Label == "Jump");
        Assert.Contains(split, element => element.Id == "up" && element.X == 240 && element.Y == 30 && element.Label == "W");
        Assert.Contains(split, element => element.Id == "right" && element.X == 286 && element.Y == 76 && element.Label == "D");
        Assert.All(split, element =>
        {
            Assert.Equal(AkronInputBoard.DefaultFillColor, element.FillColor);
            Assert.Equal(AkronInputBoard.DefaultStrokeColor, element.StrokeColor);
            Assert.Equal(AkronInputBoard.DefaultTextColor, element.TextColor);
        });
    }

    [Fact]
    public void InputBoardParsesCustomKeyboardBindings()
    {
        Assert.True(AkronInputBoard.TryParseKeyBindings("Space, C, LeftShift", out List<Keys> keys));
        Assert.Equal(new[] { Keys.Space, Keys.C, Keys.LeftShift }, keys);
        Assert.False(AkronInputBoard.TryParseKeyBindings("not-a-key", out _));
    }

    [Fact]
    public void ControlDisplayFullPresetArchiveRoundTripsCurrentBoard()
    {
        string folder = Path.Combine(Path.GetTempPath(), "akron-control-display-preset-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            AkronModuleSettings source = new AkronModuleSettings
            {
                ShowTaps = true,
                TapDisplayCorner = IndicatorCorner.TopLeft,
                TapDisplayScale = 145,
                TapDisplayOpacity = 67,
                InputBoardSource = AkronInputBoardSource.KeyboardKeys,
                InputBoardLabelPreset = AkronInputBoardLabelPreset.Keyboard,
                InputBoardElements = new List<AkronInputBoardElement> {
                    new AkronInputBoardElement {
                        Id = "custom-jump",
                        Label = "J",
                        X = 7,
                        Y = 9,
                        Width = 41,
                        Height = 37,
                        Bindings = new List<AkronInputBoardBinding> { AkronInputBoardBinding.Jump },
                        KeyBindings = new List<Keys> { Keys.C },
                        FillColor = 0x101010,
                        PressedFillColor = 0x202020,
                        StrokeColor = 0x303030,
                        TextColor = 0xE0E0E0,
                        OutlineWidth = 2,
                        TextScale = 155
                    }
                }
            };
            string path = Path.Combine(folder, "my-board.akr");

            AkronControlDisplayPresets.Write(source, path, "My Board");
            AkronControlDisplayPreset preset = AkronControlDisplayPresets.Read(path);
            AkronModuleSettings imported = new AkronModuleSettings();
            AkronControlDisplayPresets.Apply(imported, preset);

            Assert.True(imported.ShowTaps);
            Assert.Equal(IndicatorCorner.TopLeft, imported.TapDisplayCorner);
            Assert.Equal(145, imported.TapDisplayScale);
            Assert.Equal(67, imported.TapDisplayOpacity);
            Assert.Equal(AkronInputBoardSource.KeyboardKeys, imported.InputBoardSource);
            Assert.Equal(AkronInputBoardLabelPreset.Keyboard, imported.InputBoardLabelPreset);
            AkronInputBoardElement element = Assert.Single(imported.InputBoardElements);
            Assert.Equal("custom-jump", element.Id);
            Assert.Equal("J", element.Label);
            Assert.Equal(new[] { Keys.C }, element.KeyBindings);
            Assert.Equal(0xE0E0E0, element.TextColor);
            Assert.Equal(155, element.TextScale);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void CustomHudLabelPackArchiveRoundTripsLabels()
    {
        string folder = Path.Combine(Path.GetTempPath(), "akron-hud-label-pack-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "labels.akr");
            List<AkronCustomHudLabel> labels = new List<AkronCustomHudLabel> {
                new AkronCustomHudLabel {
                    Id = "room-proof",
                    Name = "Room Proof",
                    Text = "{room} / {status}",
                    Anchor = AkronHudAnchor.BottomRight,
                    TextAlignment = AkronLabelTextAlignment.Right,
                    Scale = 0.75f,
                    Color = 0xC0FFEE,
                    Shadow = true,
                    ShadowOpacity = 92
                }
            };

            AkronCustomHudLabels.Write(labels, path);
            AkronHudLabelPack pack = AkronCustomHudLabels.Read(path);

            AkronCustomHudLabel label = Assert.Single(pack.Labels);
            Assert.Equal("akron-hud-labels-v1", pack.Format);
            Assert.Equal("room-proof", label.Id);
            Assert.Equal("Room Proof", label.Name);
            Assert.Equal("{room} / {status}", label.Text);
            Assert.Equal(AkronHudAnchor.BottomRight, label.Anchor);
            Assert.Equal(AkronLabelTextAlignment.Right, label.TextAlignment);
            Assert.Equal(0.75f, label.Scale);
            Assert.Equal(0xC0FFEE, label.Color);
            Assert.True(label.Shadow);
            Assert.Equal(92, label.ShadowOpacity);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void SetupPackArchiveRoundTripsListedSetupSystems()
    {
        string folder = Path.Combine(Path.GetTempPath(), "akron-setup-pack-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            AkronModuleSettings source = new AkronModuleSettings
            {
                SmartStartPos = true,
                RespawnAtStartPos = true,
                StartPosConfiguredDashes = 2,
                StartPosConfiguredStaminaPercent = 55,
                StartPosConfiguredFacing = AkronStartPosFacing.Left,
                AutoKill = true,
                AutoKillArea = true,
                AutoKillDefaultAreaConditions = new AkronAutoKillAreaData {
                    HorizontalSpeedCondition = true,
                    MinHorizontalSpeed = 80,
                    MaxHorizontalSpeed = 180
                },
                AutoKillAreas = new List<AkronAutoKillAreaData> {
                    new AkronAutoKillAreaData {
                        X = 1,
                        Y = 2,
                        Width = 30,
                        Height = 40,
                        SpeedCondition = true,
                        MinSpeed = 120,
                        MaxSpeed = 240,
                        DashCountCondition = true,
                        DashCount = 1,
                        HorizontalDirection = AkronAutoKillAxisCondition.Negative,
                        VerticalDirection = AkronAutoKillAxisCondition.Positive
                    }
                },
                AutoDeafen = true,
                AutoDeafenHotkey = "Ctrl+Shift+D",
                AudioSpeed = true,
                AudioSpeedMultiplier = 1.25f,
                PitchShift = true,
                PitchShiftMultiplier = 0.75f,
                AudioSplitter = true,
                RecordingFramerate = 120,
                RecordingBitrateMbps = 80,
                RecordingCodec = AkronRecordingCodec.H264Nvenc,
                RecordingAudioMusicTrack = true,
                MenuActionBindings = new Dictionary<string, string>
                {
                    ["Shortcuts/Retry"] = "Ctrl+R"
                }
            };
            source.SoundVolumes["bird-squawk"] = 42;
            source.SoundVolumeOverrides["bird-squawk"] = true;

            AkronModuleSession sourceSession = new AkronModuleSession
            {
                StartPositions = new Dictionary<int, AkronStartPos>
                {
                    [3] = new AkronStartPos
                    {
                        Position = new Vector2(12, 34),
                        Room = "a-00",
                        AreaSid = "Celeste/1-ForsakenCity",
                        UsesSpawnConfig = true,
                        Dashes = 2,
                        StaminaPercent = 55,
                        Facing = AkronStartPosFacing.Left,
                        Idle = true,
                        Grab = true
                    }
                }
            };
            string path = Path.Combine(folder, "practice.akr");

            AkronSetupPacks.Write(source, sourceSession, path, "Practice Setup");
            AkronSetupPack pack = AkronSetupPacks.Read(path);
            AkronModuleSettings imported = new AkronModuleSettings();
            AkronModuleSession importedSession = new AkronModuleSession();
            AkronSetupPacks.Apply(imported, importedSession, pack);

            Assert.Equal(AkronSetupSection.Whole, pack.Section);
            Assert.True(imported.SmartStartPos);
            Assert.Equal(2, imported.StartPosConfiguredDashes);
            Assert.True(imported.AutoKill);
            Assert.True(imported.AutoKillArea);
            Assert.True(imported.AutoKillDefaultAreaConditions.HorizontalSpeedCondition);
            Assert.Equal(80, imported.AutoKillDefaultAreaConditions.MinHorizontalSpeed);
            Assert.Equal(180, imported.AutoKillDefaultAreaConditions.MaxHorizontalSpeed);
            AkronAutoKillAreaData importedAutoKillArea = Assert.Single(imported.AutoKillAreas);
            Assert.True(importedAutoKillArea.SpeedCondition);
            Assert.Equal(120, importedAutoKillArea.MinSpeed);
            Assert.Equal(240, importedAutoKillArea.MaxSpeed);
            Assert.True(importedAutoKillArea.DashCountCondition);
            Assert.Equal(1, importedAutoKillArea.DashCount);
            Assert.Equal(AkronAutoKillAxisCondition.Negative, importedAutoKillArea.HorizontalDirection);
            Assert.Equal(AkronAutoKillAxisCondition.Positive, importedAutoKillArea.VerticalDirection);
            Assert.True(imported.AutoDeafen);
            Assert.Equal("Ctrl+Shift+D", imported.AutoDeafenHotkey);
            Assert.True(imported.AudioSpeed);
            Assert.Equal(1.25f, imported.AudioSpeedMultiplier);
            Assert.True(imported.PitchShift);
            Assert.Equal(0.75f, imported.PitchShiftMultiplier);
            Assert.True(imported.AudioSplitter);
            Assert.Equal(42, imported.SoundVolumes["bird-squawk"]);
            Assert.True(imported.SoundVolumeOverrides["bird-squawk"]);
            Assert.Equal(120, imported.RecordingFramerate);
            Assert.Equal(80, imported.RecordingBitrateMbps);
            Assert.Equal(AkronRecordingCodec.H264Nvenc, imported.RecordingCodec);
            Assert.True(imported.RecordingAudioMusicTrack);
            Assert.Equal("Ctrl+R", imported.MenuActionBindings["Shortcuts/Retry"]);
            Assert.True(pack.ButtonBindings.ContainsKey(nameof(AkronModuleSettings.Retry)));
            Assert.True(pack.ButtonBindings.ContainsKey(nameof(AkronModuleSettings.SetStartPos)));

            AkronStartPos startPos = Assert.Single(importedSession.StartPositions).Value;
            Assert.Equal("a-00", startPos.Room);
            Assert.Equal("Celeste/1-ForsakenCity", startPos.AreaSid);
            Assert.True(startPos.UsesSpawnConfig);
            Assert.Equal(2, startPos.Dashes);
            Assert.Equal(55, startPos.StaminaPercent);
            Assert.Equal(AkronStartPosFacing.Left, startPos.Facing);
            Assert.True(startPos.Grab);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void SetupPacksDoNotReplaceMachineLocalLoggingSettings()
    {
        AkronModuleSettings source = new AkronModuleSettings
        {
            StreamerMode = true
        };
        AkronSetupPack pack = AkronSetupPacks.Capture(source, session: null, "Practice Setup");
        AkronModuleSettings target = new AkronModuleSettings
        {
            Logging = false,
            LoggingLevel = AkronLoggingLevel.Normal,
            LoggingMirrorWarningsToEverest = false,
            LoggingMaxFileSizeMb = 42,
            LoggingRetainedFiles = 2
        };

        AkronSetupPacks.Apply(target, session: null, pack);

        Assert.False(target.Logging);
        Assert.Equal(AkronLoggingLevel.Normal, target.LoggingLevel);
        Assert.False(target.LoggingMirrorWarningsToEverest);
        Assert.Equal(42, target.LoggingMaxFileSizeMb);
        Assert.Equal(2, target.LoggingRetainedFiles);
    }

    [Fact]
    public void SetupPackScopedImportsOnlyReplaceRequestedSystems()
    {
        AkronModuleSettings source = new AkronModuleSettings
        {
            SmartStartPos = true,
            StartPosConfiguredDashes = 2,
            StartPosConfiguredStaminaPercent = 55,
            AutoKill = true,
            AutoKillTimer = true,
            AutoKillSeconds = 12,
            AutoKillArea = true,
            AutoKillDefaultAreaConditions = new AkronAutoKillAreaData {
                HorizontalSpeedCondition = true,
                MinHorizontalSpeed = 80,
                MaxHorizontalSpeed = 180
            },
            AutoKillAreas = new List<AkronAutoKillAreaData> {
                new AkronAutoKillAreaData {
                    X = 1,
                    Y = 2,
                    Width = 30,
                    Height = 40,
                    SpeedCondition = true,
                    MinSpeed = 120,
                    MaxSpeed = 240,
                    DashCountCondition = true,
                    DashCount = 1,
                    HorizontalDirection = AkronAutoKillAxisCondition.Negative,
                    VerticalDirection = AkronAutoKillAxisCondition.Positive
                }
            },
            AutoDeafen = true,
            AutoDeafenHotkey = "Ctrl+Shift+D",
            RecordingFramerate = 120,
            RecordingBitrateMbps = 80,
            RecordingCodec = AkronRecordingCodec.H264Nvenc,
            RecordingAudioMusicTrack = true,
            AudioSpeed = true,
            AudioSpeedMultiplier = 1.25f,
            PitchShift = true,
            PitchShiftMultiplier = 0.75f,
            AudioSplitter = true,
            LabelSystemVisible = false,
            RoomLabels = false,
            RoomLabelColor = 0x123456,
            CustomHudLabels = true,
            CustomHudLabelPadding = 17,
            CustomHudLabelGap = 23,
            CustomHudLabelObstructionEnabled = true,
            CustomHudLabelObstructionMode = AkronLabelObstructionMode.Move,
            CustomHudLabelObstructionOnlyOverlappedLabel = true,
            CustomHudLabelDefinitions = new List<AkronCustomHudLabel> {
                new AkronCustomHudLabel {
                    Id = "hud-proof",
                    Name = "HUD Proof",
                    Text = "{status} / {room}",
                    Anchor = AkronHudAnchor.TopRight,
                    Color = 0xC0FFEE
                }
            },
            LabelRowOrder = new List<string> {
                AkronModuleSettings.BuildCustomLabelRowKey("hud-proof")
            },
            HudCheatIndicator = false,
            HudCheatIndicatorAnchor = AkronHudAnchor.BottomRight,
            StaminaBar = true,
            StaminaBarHud = true,
            StaminaBarHudPosition = AkronStaminaHudPosition.BottomCenter,
            DashBar = true,
            DashBarHudOffsetY = 96,
            SetInventoryDashes = 4,
            SetInventoryJumps = 3,
            SetInventoryRestoreOnDeath = true,
            JumpHackAllowVerticalDashJumps = true,
            MenuActionBindings = new Dictionary<string, string>
            {
                ["Shortcuts/Retry"] = "Ctrl+R"
            }
        };
        source.SoundVolumes["bird-squawk"] = 42;
        source.SoundVolumeOverrides["bird-squawk"] = true;

        AkronModuleSession sourceSession = new AkronModuleSession
        {
            StartPositions = new Dictionary<int, AkronStartPos>
            {
                [3] = new AkronStartPos
                {
                    Position = new Vector2(12, 34),
                    Room = "a-00",
                    AreaSid = "Celeste/1-ForsakenCity",
                    UsesSpawnConfig = true,
                    Dashes = 2,
                    StaminaPercent = 55
                }
            }
        };
        AkronSetupPack pack = AkronSetupPacks.Capture(source, sourceSession, "Practice Setup");
        Assert.Equal(4, pack.State.SetInventoryDashes);
        Assert.Equal(3, pack.State.SetInventoryJumps);
        Assert.True(pack.State.SetInventoryRestoreOnDeath);
        Assert.True(pack.State.JumpHackAllowVerticalDashJumps);

        AkronModuleSettings startPosOnly = new AkronModuleSettings { AutoKill = false };
        AkronModuleSession startPosSession = new AkronModuleSession();
        AkronSetupPacks.Apply(startPosOnly, startPosSession, pack, AkronSetupSection.StartPos);
        Assert.True(startPosOnly.SmartStartPos);
        Assert.Equal(2, startPosOnly.StartPosConfiguredDashes);
        Assert.False(startPosOnly.AutoKill);
        Assert.Single(startPosSession.StartPositions);

        AkronModuleSettings keybindsOnly = new AkronModuleSettings { SmartStartPos = false };
        AkronSetupPacks.Apply(keybindsOnly, new AkronModuleSession(), pack, AkronSetupSection.Keybinds);
        Assert.Equal("Ctrl+R", keybindsOnly.MenuActionBindings["Shortcuts/Retry"]);
        Assert.False(keybindsOnly.SmartStartPos);

        AkronModuleSettings autoKillOnly = new AkronModuleSettings { AutoDeafen = false };
        AkronSetupPacks.Apply(autoKillOnly, new AkronModuleSession(), pack, AkronSetupSection.AutoKill);
        Assert.True(autoKillOnly.AutoKill);
        Assert.True(autoKillOnly.AutoKillTimer);
        Assert.Equal(12, autoKillOnly.AutoKillSeconds);
        Assert.True(autoKillOnly.AutoKillDefaultAreaConditions.HorizontalSpeedCondition);
        Assert.Equal(80, autoKillOnly.AutoKillDefaultAreaConditions.MinHorizontalSpeed);
        Assert.Equal(180, autoKillOnly.AutoKillDefaultAreaConditions.MaxHorizontalSpeed);
        AkronAutoKillAreaData scopedAutoKillArea = Assert.Single(autoKillOnly.AutoKillAreas);
        Assert.True(scopedAutoKillArea.SpeedCondition);
        Assert.Equal(120, scopedAutoKillArea.MinSpeed);
        Assert.Equal(240, scopedAutoKillArea.MaxSpeed);
        Assert.True(scopedAutoKillArea.DashCountCondition);
        Assert.Equal(1, scopedAutoKillArea.DashCount);
        Assert.Equal(AkronAutoKillAxisCondition.Negative, scopedAutoKillArea.HorizontalDirection);
        Assert.Equal(AkronAutoKillAxisCondition.Positive, scopedAutoKillArea.VerticalDirection);
        Assert.False(autoKillOnly.AutoDeafen);

        AkronModuleSettings autoDeafenOnly = new AkronModuleSettings { AutoKill = false };
        AkronSetupPacks.Apply(autoDeafenOnly, new AkronModuleSession(), pack, AkronSetupSection.AutoDeafen);
        Assert.True(autoDeafenOnly.AutoDeafen);
        Assert.Equal("Ctrl+Shift+D", autoDeafenOnly.AutoDeafenHotkey);
        Assert.False(autoDeafenOnly.AutoKill);

        AkronModuleSettings recorderOnly = new AkronModuleSettings { AudioSplitter = false };
        AkronSetupPacks.Apply(recorderOnly, new AkronModuleSession(), pack, AkronSetupSection.Recorder);
        Assert.Equal(120, recorderOnly.RecordingFramerate);
        Assert.Equal(80, recorderOnly.RecordingBitrateMbps);
        Assert.Equal(AkronRecordingCodec.H264Nvenc, recorderOnly.RecordingCodec);
        Assert.True(recorderOnly.RecordingAudioMusicTrack);
        Assert.False(recorderOnly.AudioSplitter);

        AkronModuleSettings audioOnly = new AkronModuleSettings { RecordingFramerate = 60 };
        AkronSetupPacks.Apply(audioOnly, new AkronModuleSession(), pack, AkronSetupSection.Audio);
        Assert.True(audioOnly.AudioSpeed);
        Assert.Equal(1.25f, audioOnly.AudioSpeedMultiplier);
        Assert.True(audioOnly.PitchShift);
        Assert.Equal(0.75f, audioOnly.PitchShiftMultiplier);
        Assert.True(audioOnly.AudioSplitter);
        Assert.Equal(42, audioOnly.SoundVolumes["bird-squawk"]);
        Assert.True(audioOnly.SoundVolumeOverrides["bird-squawk"]);
        Assert.Equal(60, audioOnly.RecordingFramerate);

        AkronModuleSettings hudOnly = new AkronModuleSettings
        {
            AutoKill = false,
            AudioSpeed = false
        };
        AkronSetupPacks.Apply(hudOnly, new AkronModuleSession(), pack, AkronSetupSection.Hud);
        Assert.False(hudOnly.LabelSystemVisible);
        Assert.False(hudOnly.RoomLabels);
        Assert.Equal(0x123456, hudOnly.RoomLabelColor);
        Assert.True(hudOnly.CustomHudLabels);
        Assert.Equal(17, hudOnly.CustomHudLabelPadding);
        Assert.Equal(23, hudOnly.CustomHudLabelGap);
        Assert.True(hudOnly.CustomHudLabelObstructionEnabled);
        Assert.Equal(AkronLabelObstructionMode.Move, hudOnly.CustomHudLabelObstructionMode);
        Assert.True(hudOnly.CustomHudLabelObstructionOnlyOverlappedLabel);
        AkronCustomHudLabel importedLabel = Assert.Single(hudOnly.CustomHudLabelDefinitions);
        Assert.Equal("hud-proof", importedLabel.Id);
        Assert.Equal("{status} / {room}", importedLabel.Text);
        Assert.Equal(AkronHudAnchor.BottomRight, hudOnly.HudCheatIndicatorAnchor);
        Assert.False(hudOnly.HudCheatIndicator);
        Assert.True(hudOnly.StaminaBar);
        Assert.Equal(AkronStaminaHudPosition.BottomCenter, hudOnly.StaminaBarHudPosition);
        Assert.True(hudOnly.DashBar);
        Assert.Equal(96, hudOnly.DashBarHudOffsetY);
        Assert.False(hudOnly.AutoKill);
        Assert.False(hudOnly.AudioSpeed);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, true)]
    [InlineData(0, 2, true)]
    [InlineData(1, 1, false)]
    public void MadelineNoDashColorStateMatchesVanillaHairInventory(int dashes, int maxDashes, bool expected)
    {
        Assert.Equal(expected, AkronModule.IsMadelineNoDashColorState(dashes, maxDashes));
    }

    [Theory]
    [InlineData(0.01f, true)]
    [InlineData(0.05f, true)]
    [InlineData(0.1f, true)]
    [InlineData(0.15f, false)]
    [InlineData(0.2f, false)]
    public void FreezeFrameSuppressionKeepsScriptedLongFreezes(float seconds, bool expected)
    {
        Assert.Equal(expected, AkronModule.ShouldSuppressFreezeFrames(seconds));
    }

    [Theory]
    [InlineData("Ctrl+Shift+D", "LeftControl+LeftShift+D")]
    [InlineData("RAlt", "RightAlt")]
    [InlineData("RShift+F1", "RightShift+F1")]
    [InlineData("Button:LeftShoulder", "Button:LeftShoulder")]
    [InlineData("Controller:RightStick", "Button:RightStick")]
    public void MenuBindingsKeepKeyboardBindingsAndAcceptControllerButtons(string stored, string expectedNormalized)
    {
        object binding = ParseMenuBinding(stored);

        Assert.Equal(expectedNormalized, InvokeMenuBindingString(binding, "ToStorageString"));
    }

    [Theory]
    [InlineData("RAlt", "RightAlt")]
    [InlineData("RShift+F1", "RightShift+F1")]
    public void MenuBindingDisplayPreservesRightSideModifiers(string stored, string expectedDisplay)
    {
        object binding = ParseMenuBinding(stored);

        Assert.Equal(expectedDisplay, InvokeMenuBindingString(binding, "ToDisplayString"));
    }

    [Fact]
    public void MenuBindingDisplayUsesControllerButtonNames()
    {
        object binding = ParseMenuBinding("Button:LeftShoulder");

        Assert.Equal("LB", InvokeMenuBindingString(binding, "ToDisplayString"));
    }

    private static List<string> BuildOverlayEntryLabels(string tab)
    {
        MethodInfo? method = typeof(AkronOverlay).GetMethod("BuildDisplayEntriesForTab", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        object? entries = method.Invoke(null, new object?[] { tab, null });
        Assert.NotNull(entries);

        PropertyInfo? labelProperty = entries
            .GetType()
            .GetGenericArguments()[0]
            .GetProperty("Label", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(labelProperty);

        return ((System.Collections.IEnumerable)entries)
            .Cast<object>()
            .Select(entry => (string)labelProperty.GetValue(entry)!)
            .ToList();
    }

    private static List<string> BuildRuntimeOverlayEntryLabels(AkronOverlay overlay, string tab)
    {
        MethodInfo? method = typeof(AkronOverlay).GetMethod("GetDisplayActionEntries", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        object? entries = method.Invoke(overlay, new object?[] { tab, null });
        Assert.NotNull(entries);

        return ExtractPrivateEntryLabels(entries);
    }

    private static AkronOverlay CreateSoundOverlayForListTest()
    {
        return CreateOverlayForListTest();
    }

    private static AkronOverlay CreateOverlayForListTest()
    {
        AkronOverlay overlay = (AkronOverlay)RuntimeHelpers.GetUninitializedObject(typeof(AkronOverlay));
        SetPrivateFieldToNewStringComparerCollection(overlay, "displayActionEntryCache");
        SetPrivateField(overlay, "expandedSoundGroups", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return overlay;
    }

    private static List<string> BuildFilteredOverlayEntryLabels(AkronOverlay overlay, string tab, string query)
    {
        overlay.SetSearchQuery(query);
        MethodInfo? method = typeof(AkronOverlay).GetMethod("GetFilteredDisplayActionEntries", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        object? entries = method.Invoke(overlay, new object?[] { tab, null });
        Assert.NotNull(entries);

        return ExtractPrivateEntryLabels(entries);
    }

    private static void AddExpandedSoundGroup(AkronOverlay overlay, string groupLabel)
    {
        FieldInfo? field = typeof(AkronOverlay).GetField("expandedSoundGroups", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        HashSet<string> groups = (HashSet<string>)field.GetValue(overlay)!;
        groups.Add(groupLabel);

        MethodInfo? invalidate = typeof(AkronOverlay).GetMethod("InvalidateDisplayActionEntryCache", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(invalidate);
        invalidate.Invoke(overlay, Array.Empty<object>());
    }

    private static CheckpointData CreateCheckpointData(string level, string name)
    {
        return new CheckpointData(level, name)
        {
            Level = level,
            Name = name
        };
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo? field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }

    private static void SetPrivateFieldToNewStringComparerCollection(object target, string fieldName)
    {
        FieldInfo? field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        object? value = Activator.CreateInstance(field.FieldType, new object[] { StringComparer.OrdinalIgnoreCase });
        Assert.NotNull(value);
        field.SetValue(target, value);
    }

    private static List<string> ExtractPrivateEntryLabels(object entries)
    {
        PropertyInfo? labelProperty = entries
            .GetType()
            .GetGenericArguments()[0]
            .GetProperty("Label", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(labelProperty);

        return ((System.Collections.IEnumerable)entries)
            .Cast<object>()
            .Select(entry => (string)labelProperty.GetValue(entry)!)
            .ToList();
    }

    private static Dictionary<string, string> BuildOverlayEntryControls(string tab)
    {
        MethodInfo? method = typeof(AkronOverlay).GetMethod("BuildDisplayEntriesForTab", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        object? entries = method.Invoke(null, new object?[] { tab, null });
        Assert.NotNull(entries);

        Type entryType = entries.GetType().GetGenericArguments()[0];
        PropertyInfo? labelProperty = entryType.GetProperty("Label", BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo? controlProperty = entryType.GetProperty("Control", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(labelProperty);
        Assert.NotNull(controlProperty);

        return ((System.Collections.IEnumerable)entries)
            .Cast<object>()
            .GroupBy(entry => (string)labelProperty.GetValue(entry)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => controlProperty.GetValue(group.First())!.ToString()!,
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, bool> BuildOverlayEntryBindableExposure(string tab)
    {
        MethodInfo? buildEntries = typeof(AkronOverlay).GetMethod("BuildDisplayEntriesForTab", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(buildEntries);

        object? entries = buildEntries.Invoke(null, new object?[] { tab, null });
        Assert.NotNull(entries);

        Type entryType = entries.GetType().GetGenericArguments()[0];
        PropertyInfo? labelProperty = entryType.GetProperty("Label", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo? isBindable = typeof(AkronOverlay).GetMethod("IsBindableOverlayEntry", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(labelProperty);
        Assert.NotNull(isBindable);

        return ((System.Collections.IEnumerable)entries)
            .Cast<object>()
            .GroupBy(entry => (string)labelProperty.GetValue(entry)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (bool)isBindable.Invoke(null, new[] { group.First() })!,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildOverlayEntryValue(string tab, string label)
    {
        MethodInfo? method = typeof(AkronOverlay).GetMethod("BuildDisplayEntriesForTab", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        object? entries = method.Invoke(null, new object?[] { tab, null });
        Assert.NotNull(entries);

        Type entryType = entries.GetType().GetGenericArguments()[0];
        PropertyInfo? labelProperty = entryType.GetProperty("Label", BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo? valueProperty = entryType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(labelProperty);
        Assert.NotNull(valueProperty);

        object entry = ((System.Collections.IEnumerable)entries)
            .Cast<object>()
            .First(entry => string.Equals((string)labelProperty.GetValue(entry)!, label, StringComparison.OrdinalIgnoreCase));

        return ((Func<string>)valueProperty.GetValue(entry)!)();
    }

    private static (Func<string> Value, Action Execute) BuildOverlayEntryValueAndExecute(string tab, string label)
    {
        MethodInfo? method = typeof(AkronOverlay).GetMethod("BuildDisplayEntriesForTab", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        object? entries = method.Invoke(null, new object?[] { tab, null });
        Assert.NotNull(entries);

        Type entryType = entries.GetType().GetGenericArguments()[0];
        PropertyInfo? labelProperty = entryType.GetProperty("Label", BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo? valueProperty = entryType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo? executeProperty = entryType.GetProperty("Execute", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(labelProperty);
        Assert.NotNull(valueProperty);
        Assert.NotNull(executeProperty);

        object entry = ((System.Collections.IEnumerable)entries)
            .Cast<object>()
            .First(entry => string.Equals((string)labelProperty.GetValue(entry)!, label, StringComparison.OrdinalIgnoreCase));

        return ((Func<string>)valueProperty.GetValue(entry)!, (Action)executeProperty.GetValue(entry)!);
    }

    private static bool HasOverlayOptionsPopup(string label)
    {
        MethodInfo? method = typeof(AkronOverlay).GetMethod("HasOptionsPopup", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (bool)method.Invoke(null, new object[] { label })!;
    }

    private static string[] GetOverlayTabs()
    {
        FieldInfo? field = typeof(AkronOverlay).GetField("BaseTabs", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return (string[])field.GetValue(null)!;
    }

    private static object ParseMenuBinding(string value)
    {
        Type? menuBindingType = typeof(AkronOverlay).GetNestedType("MenuBinding", BindingFlags.NonPublic);
        Assert.NotNull(menuBindingType);

        MethodInfo? tryParse = menuBindingType.GetMethod("TryParse", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(tryParse);

        object?[] arguments = { value, null };
        Assert.True((bool)tryParse.Invoke(null, arguments)!);
        return arguments[1]!;
    }

    private static string InvokeMenuBindingString(object binding, string methodName)
    {
        MethodInfo? method = binding.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        return (string)method.Invoke(binding, Array.Empty<object>())!;
    }

    private static bool InvokePlayerNumberGate(string methodName, AkronModuleSettings settings, bool featureAllowed)
    {
        MethodInfo? method = typeof(AkronHudRenderer).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (bool)method.Invoke(null, new object[] { settings, featureAllowed })!;
    }

    private static bool InvokeStartPosInArea(AkronStartPos startPos, string areaSid)
    {
        MethodInfo? method = typeof(AkronActions).GetMethod("IsStartPosInArea", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (bool)method.Invoke(null, new object[] { startPos, areaSid })!;
    }

    private static Vector2 TestVector2(float x, float y)
    {
        return new Vector2
        {
            X = x,
            Y = y
        };
    }

    private static void AssertPreviewBounds(AkronOverlay.PracticeAreaSelectionPreviewBounds actual, int x, int y, int width, int height)
    {
        Assert.Equal(x, actual.X);
        Assert.Equal(y, actual.Y);
        Assert.Equal(width, actual.Width);
        Assert.Equal(height, actual.Height);
    }

    private static string WriteReplaySegment(string folder, string filename, DateTime writeUtc, int bytes)
    {
        string path = Path.Combine(folder, filename);
        File.WriteAllBytes(path, Enumerable.Repeat((byte)1, bytes).ToArray());
        File.SetLastWriteTimeUtc(path, writeUtc);
        File.SetCreationTimeUtc(path, writeUtc);
        return path;
    }

    private static string WriteClipWithSidecar(string folder, string filename, string kind, DateTime startUtc, DateTime endUtc)
    {
        string path = Path.Combine(folder, filename);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        File.WriteAllLines(path + ".akrclip", new[] {
            "kind=" + kind,
            "format=MKV",
            "chapter=" + filename.Split('-')[0] + "-" + filename.Split('-')[1],
            "room=" + filename.Split('-')[2] + "-" + filename.Split('-')[3],
            "createdUtc=" + startUtc.AddSeconds(1).ToString("O", CultureInfo.InvariantCulture),
            "startUtc=" + startUtc.ToString("O", CultureInfo.InvariantCulture),
            "endUtc=" + endUtc.ToString("O", CultureInfo.InvariantCulture)
        });
        return path;
    }
}
