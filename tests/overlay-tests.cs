using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste.Mod.Akron;
using Microsoft.Xna.Framework;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class OverlayTests {
    [Fact]
    public void ExternalToolTabsAreHiddenWhenTheirModsAreMissing() {
        string[] visibleTabs = InvokeBuildVisibleTabs(speedrunToolLoaded: false, celesteTasLoaded: false, extendedVariantModeAvailable: false, extendedCameraDynamicsLoaded: false);

        Assert.DoesNotContain("Speedrun Tool", visibleTabs);
        Assert.DoesNotContain("CelesteTAS", visibleTabs);
        Assert.DoesNotContain("Extended Variant Mode", visibleTabs);
        Assert.DoesNotContain("Extended Camera Dynamics", visibleTabs);
    }

    [Fact]
    public void ExternalToolTabsAppearOnlyForAvailableMods() {
        string[] visibleTabs = InvokeBuildVisibleTabs(speedrunToolLoaded: true, celesteTasLoaded: true, extendedVariantModeAvailable: true, extendedCameraDynamicsLoaded: true);

        Assert.Contains("Speedrun Tool", visibleTabs);
        Assert.Contains("CelesteTAS", visibleTabs);
        Assert.Contains("Extended Variant Mode", visibleTabs);
        Assert.Contains("Extended Camera Dynamics", visibleTabs);
    }

    [Fact]
    public void ExternalToolSectionsRemainToggleableForCollapseCommands() {
        string[] toggleableSections = InvokeStaticStringArray("GetToggleableSections");

        foreach (string title in new[] { "Speedrun Tool", "CelesteTAS", "Extended Variant Mode", "Extended Camera Dynamics" }) {
            Assert.Contains(title, toggleableSections);
        }
    }

    [Fact]
    public void ExternalToolColumnsDoNotChangeVanillaRows() {
        List<string> playerLabels = BuildOverlayEntryLabels("Player");
        List<string> creatorLabels = BuildOverlayEntryLabels("Creator");

        Assert.DoesNotContain("Extended Variants Master", playerLabels);
        Assert.DoesNotContain("Extended Variants Randomizer", playerLabels);
        Assert.DoesNotContain("Reset Extended", playerLabels);
        Assert.DoesNotContain("Reset Vanilla", playerLabels);
        Assert.Contains("Export Room Times", creatorLabels);
    }

    [Fact]
    public void ExternalToolTabsExposeTheirOwnRows() {
        List<string> speedrunToolLabels = BuildOverlayEntryLabels("Speedrun Tool");
        List<string> celesteTasLabels = BuildOverlayEntryLabels("CelesteTAS");
        List<string> extendedVariantModeLabels = BuildOverlayEntryLabels("Extended Variant Mode");
        List<string> extendedCameraDynamicsLabels = BuildOverlayEntryLabels("Extended Camera Dynamics");

        Assert.Contains("SRT Status", speedrunToolLabels);
        Assert.Contains("SRT Slot", speedrunToolLabels);
        Assert.Contains("TAS Status", celesteTasLabels);
        Assert.Contains("Play Configured TAS", celesteTasLabels);
        Assert.Contains("Extended Variants Master", extendedVariantModeLabels);
        Assert.Contains("Extended Variants Randomizer", extendedVariantModeLabels);
        Assert.Contains("Reset Extended", extendedVariantModeLabels);
        Assert.Contains("Reset Vanilla", extendedVariantModeLabels);
        Assert.Contains("ECD Status", extendedCameraDynamicsLabels);
        Assert.Contains("ECD Zoom Out", extendedCameraDynamicsLabels);
        Assert.Contains("ECD Restore Zooming", extendedCameraDynamicsLabels);
    }

    [Fact]
    public void FrameStepperBindableActionIsWiredToStepOnce() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-bindable-actions.cs"));
        int buildStart = source.IndexOf("private static IEnumerable<BindableAction> BuildBindableActions", StringComparison.Ordinal);
        int popupStart = source.IndexOf("private static bool IsBindableOverlayEntry", buildStart, StringComparison.Ordinal);

        Assert.True(buildStart >= 0);
        Assert.True(popupStart > buildStart);
        string buildBindableActions = source[buildStart..popupStart];

        Assert.Contains("string.Equals(entry.Label, \"Frame Stepper\"", buildBindableActions);
        Assert.Contains("? ExecuteFrameStepOnce", buildBindableActions);
        Assert.Contains(": entry.Execute", buildBindableActions);
    }

    [Fact]
    public void ExecuteFrameStepOnceOnlyRequestsStepWhileFreezeIsActive() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-bindable-actions.cs"));
        int methodStart = source.IndexOf("private static void ExecuteFrameStepOnce()", StringComparison.Ordinal);
        int nextMethod = source.IndexOf("private static void SetGrabMode", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(nextMethod > methodStart);
        string method = source[methodStart..nextMethod];

        Assert.Contains("session?.FreezeGameplay == true", method);
        Assert.Contains("session.StepFrameRequested = true;", method);
        Assert.DoesNotContain("ToggleFreeze", method);
    }

    [Fact]
    public void OverlayUpdateShortCircuitsWhenGameWindowIsInactive() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/AkronOverlay.cs"));
        int updateStart = source.IndexOf("public override void Update()", StringComparison.Ordinal);
        int placementUpdate = source.IndexOf("if (UpdateStartPosPlacement())", updateStart, StringComparison.Ordinal);
        int inactiveGuard = source.IndexOf("if (!IsGameWindowInputActive())", updateStart, StringComparison.Ordinal);

        Assert.True(updateStart >= 0);
        Assert.True(placementUpdate > updateStart);
        Assert.InRange(inactiveGuard, updateStart, placementUpdate);
    }

    [Fact]
    public void MotionSmoothingRowsStayInGlobalOnlyWhenInstalled() {
        List<string> missingLabels = BuildGlobalEntryLabels(motionSmoothingLoaded: false);
        List<string> loadedLabels = BuildGlobalEntryLabels(motionSmoothingLoaded: true);

        Assert.DoesNotContain("FPS Bypass", missingLabels);
        Assert.DoesNotContain("TPS Bypass", missingLabels);
        Assert.Equal("FPS Bypass", loadedLabels[0]);
        Assert.Equal("TPS Bypass", loadedLabels[1]);
        Assert.DoesNotContain("Motion Smoothing", InvokeBuildVisibleTabs(speedrunToolLoaded: true, celesteTasLoaded: true, extendedVariantModeAvailable: true, extendedCameraDynamicsLoaded: true));
    }

    [Fact]
    public void MotionSmoothingBypassTargetsUseInlineNumericInputs() {
        Dictionary<string, string> controls = BuildGlobalEntryControls(motionSmoothingLoaded: true);

        Assert.Equal("NumericInput", controls["FPS Bypass"]);
        Assert.Equal("NumericInput", controls["TPS Bypass"]);
        Assert.True(HasOverlayOptionsPopup("FPS Bypass"));
        Assert.False(HasOverlayOptionsPopup("TPS Bypass"));
    }

    [Fact]
    public void TransientResetKeepsCollapsedWindowState() {
        AkronOverlay overlay = CreateOverlayForStateTest();

        Assert.True(overlay.ToggleCollapsedWindow("Global"));

        overlay.ResetTransientUiState(searchAutofocus: false);

        Assert.Contains("Global", GetCollapsedWindowTitles(overlay));
    }

    [Theory]
    [InlineData(true, false, false, AkronOverlay.OverlayCancelAction.ClearSearch)]
    [InlineData(false, true, false, AkronOverlay.OverlayCancelAction.ClearSearch)]
    [InlineData(false, false, true, AkronOverlay.OverlayCancelAction.CloseCommunityPackBrowser)]
    [InlineData(false, false, false, AkronOverlay.OverlayCancelAction.KeepOverlayOpen)]
    public void CancelInputDoesNotCloseBaseOverlay(bool searchInputActive, bool hasSearchQuery, bool communityPackBrowserOpen, AkronOverlay.OverlayCancelAction expected) {
        Assert.Equal(expected, AkronOverlay.ResolveCancelAction(searchInputActive, hasSearchQuery, communityPackBrowserOpen));
    }

    [Theory]
    [InlineData(true, false, false, false, true)]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, false, false, true, false)]
    public void OverlayRenderSurfaceIgnoresDeathWipeState(bool visible, bool promptMenuOpen, bool autoKillAreaSelectionActive, bool autoDeafenAreaSelectionActive, bool expected) {
        Assert.Equal(expected, AkronOverlay.ShouldRenderOverlaySurface(visible, promptMenuOpen, autoKillAreaSelectionActive, autoDeafenAreaSelectionActive));
    }

    [Fact]
    public void PercentTooltipsUseLiteralTextRendering() {
        Assert.True(AkronOverlay.TooltipTextRequiresUnformattedRendering("0% removes bloom; values over 100% amplify glow."));
        Assert.False(AkronOverlay.TooltipTextRequiresUnformattedRendering("No percent markers here."));
    }

    [Fact]
    public void StartPosRowUsesSnapshotSlotPopupKey() {
        Dictionary<string, string> popupKeys = BuildOverlayEntryOptionsPopupKeys("StartPos");

        Assert.Equal("StartPos Snapshot Slot", popupKeys["StartPos"]);
        Assert.True(HasOverlayOptionsPopup("StartPos Snapshot Slot"));
    }

    [Fact]
    public void Issue18RowsStayInExistingGameplayTabs() {
        List<string> levelLabels = BuildOverlayEntryLabels("Level");
        List<string> playerLabels = BuildOverlayEntryLabels("Player");
        List<string> shortcutLabels = BuildOverlayEntryLabels("Shortcuts");

        Assert.Contains("Core Mode", levelLabels);
        Assert.Contains("Set Inventory", playerLabels);
        Assert.Contains("Dream State", playerLabels);
        Assert.Contains("Spawn Jelly", shortcutLabels);
        Assert.Contains("Spawn Theo", shortcutLabels);

        Assert.True(HasOverlayOptionsPopup("Core Mode"));
        Assert.True(HasOverlayOptionsPopup("Set Inventory"));
        Assert.True(HasOverlayOptionsPopup("Dream State"));
        Assert.False(HasOverlayOptionsPopup("Spawn Jelly"));
        Assert.False(HasOverlayOptionsPopup("Spawn Theo"));
    }

    [Fact]
    public void CoreModePopupUsesRadioRowsForStatusModeAndClickBehavior() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-gameplay-popups.cs"));
        int start = source.IndexOf("private void DrawCoreModePopupControls", StringComparison.Ordinal);
        int end = source.IndexOf("private void DrawSetInventoryPopupControls", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        string coreModePopup = source[start..end];

        Assert.Contains("DrawPopupRowLabel(\"Mode\"", coreModePopup);
        Assert.Contains("DrawPopupRowLabel(\"Click\"", coreModePopup);
        Assert.Contains("DrawPopupChoiceRadioButton(", coreModePopup);
        Assert.DoesNotContain("DrawPopupChoiceCheckbox(", coreModePopup);
    }

    [Fact]
    public void AutoKillPopupUsesMethodRadioRows() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-automation-popups.cs"));
        int start = source.IndexOf("private void DrawAutoKillPopupControls", StringComparison.Ordinal);
        int end = source.IndexOf("private void DrawAutoDeafenPopupControls", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        string autoKillPopup = source[start..end];

        Assert.Contains("DrawPopupRowLabel(\"Method\"", autoKillPopup);
        Assert.Contains("float choiceColumnX = ImGui.GetCursorPosX();", autoKillPopup);
        Assert.Contains("choiceColumnX,\n            true", autoKillPopup);
        Assert.Contains("DrawPopupChoiceRadioButton(", autoKillPopup);
        Assert.DoesNotContain("Timer kill", autoKillPopup);
        Assert.DoesNotContain("Area kill", autoKillPopup);
    }

    [Fact]
    public void OverlayToggleCancelsHiddenTransientMouseToolsBeforeNormalToggle() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/akron-module-overlay-input.cs"));
        int handleHotkeys = source.IndexOf("private static void HandleHotkeys", StringComparison.Ordinal);
        int handleFrameBypassBindings = source.IndexOf("private static void HandleFrameBypassBindings", handleHotkeys, StringComparison.Ordinal);
        int transientCancel = source.IndexOf("CancelTransientMouseUiForOverlayToggle", handleHotkeys, StringComparison.Ordinal);
        int visibleBranch = source.IndexOf("if (Overlay?.Visible == true)", handleHotkeys, StringComparison.Ordinal);

        Assert.True(handleHotkeys >= 0);
        Assert.True(handleFrameBypassBindings > handleHotkeys);
        Assert.InRange(transientCancel, handleHotkeys, handleFrameBypassBindings);
        Assert.InRange(visibleBranch, handleHotkeys, handleFrameBypassBindings);
        Assert.True(transientCancel < visibleBranch);
    }

    [Theory]
    [InlineData("Celeste.Mod.UI.OuiModToggler", true)]
    [InlineData("Celeste.Mod.UI.OuiModOptions", false)]
    [InlineData("Celeste.OuiMainMenu", false)]
    [InlineData(null, false)]
    public void GlobalOverlayToggleIsSuppressedOnlyInEverestModToggler(string? ouiTypeName, bool expected) {
        Assert.Equal(expected, AkronModule.ShouldSuppressGlobalOverlayToggleForOuiType(ouiTypeName));
    }

    [Fact]
    public void GlobalOverlayHotkeysSuppressModTogglerBeforeOpeningOverlay() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/akron-module-overlay-input.cs"));
        int start = source.IndexOf("private static void HandleGlobalOverlayHotkeys", StringComparison.Ordinal);
        int end = source.IndexOf("private static void UpdateStepHoldRepeat", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        string globalHotkeys = source[start..end];

        int visibleBranch = globalHotkeys.IndexOf("if (Overlay?.Visible == true)", StringComparison.Ordinal);
        int suppressBranch = globalHotkeys.IndexOf("if (ShouldSuppressGlobalOverlayToggle(scene))", StringComparison.Ordinal);
        int openBranch = globalHotkeys.IndexOf("if (IsOverlayTogglePressed())", suppressBranch, StringComparison.Ordinal);

        Assert.True(visibleBranch >= 0);
        Assert.True(suppressBranch > visibleBranch);
        Assert.True(openBranch > suppressBranch);
        Assert.Contains("RefreshOverlayToggleKeyboardState();", globalHotkeys);
    }

    [Fact]
    public void PlacementEndPathsRefreshManagedCursorState() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-placement.cs"));

        Assert.Contains("internal bool CancelTransientMouseUiForOverlayToggle()", source);
        Assert.Contains("EndStartPosPlacement(false);", source);
        Assert.Contains("EndAutoKillAreaSelection(false);", source);
        Assert.Contains("EndAutoDeafenAreaSelection(false);", source);

        foreach (string methodName in new[] { "EndStartPosPlacement", "EndAutoKillAreaSelection", "EndAutoDeafenAreaSelection" }) {
            int start = source.IndexOf("private void " + methodName, StringComparison.Ordinal);
            int nextMethod = source.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
            if (nextMethod < 0) {
                nextMethod = source.IndexOf("\n    internal ", start + 1, StringComparison.Ordinal);
            }

            Assert.True(start >= 0);
            string methodSource = source[start..nextMethod];
            Assert.Contains("AkronModule.RefreshOverlayCursorState();", methodSource);
        }
    }

    [Fact]
    public void SetInventoryPopupUsesSetButtonAndRespectsRestoreCheckbox() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-gameplay-popups.cs"));
        int start = source.IndexOf("private void DrawSetInventoryPopupControls", StringComparison.Ordinal);
        int end = source.IndexOf("private void DrawDreamStatePopupControls", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        string setInventoryPopup = source[start..end];

        Assert.Contains("ImGui.Button(\"Set##\"", setInventoryPopup);
        Assert.DoesNotContain("AkronModule.Settings.SetInventoryRestoreOnDeath = true;", setInventoryPopup);
        Assert.DoesNotContain("Apply now##", setInventoryPopup);
    }

    [Fact]
    public void AirJumpPopupExposesVerticalDashSuboption() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-gameplay-popups.cs"));
        int start = source.IndexOf("private void DrawAirJumpsPopupControls", StringComparison.Ordinal);
        int end = source.IndexOf("private void DrawCoreModePopupControls", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        string airJumpPopup = source[start..end];

        Assert.Contains("Dash verticals##", airJumpPopup);
        Assert.Contains("JumpHackAllowVerticalDashJumps", airJumpPopup);
    }

    [Fact]
    public void ActionRowsCanReadToggleStateForMainMenuColoring() {
        string renderer = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-imgui-renderer.cs"));
        Assert.Contains("entry.Control == OverlayEntryControl.Action && !entry.IsToggle", renderer);
    }

    [Fact]
    public void LoggingToggleReportsOnOffForMainMenuColoring() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-entries.cs"));
        int start = source.IndexOf("private static OverlayEntry LoggingToggle()", StringComparison.Ordinal);
        int end = source.IndexOf("private static OverlayEntry Keybind", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        string loggingToggle = source[start..end];

        Assert.Contains("AkronModule.Settings.Logging ? \"On\" : \"Off\"", loggingToggle);
        Assert.DoesNotContain("AkronLog.FormatLevel(AkronModule.Settings.LoggingLevel)", loggingToggle);
    }

    [Fact]
    public void WorldProjectionAppliesLevelZoomAroundFocus() {
        Vector2 projected = AkronScreenProjection.ApplyLevelZoom(TestVector(100f, 80f), 2f, TestVector(160f, 90f));

        Assert.Equal(40f, projected.X);
        Assert.Equal(70f, projected.Y);
    }

    [Fact]
    public void AreaPixelMarkerDoesNotUseYOffset() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Hud/akron-hud-world-overlay-renderer.cs"));

        Assert.DoesNotContain("PracticeAreaPixelMarkerYOffset", source);
        Assert.DoesNotContain("Math.Floor(rect.Y) +", source);
        Assert.DoesNotContain("Mouse.GetState()", source);
        Assert.Contains("DrawWorldPixelMarker(level, preview", source);
    }

    [Fact]
    public void InputBoardElementEditorUsesSelectionScopedInputIds() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Input/akron-input-board-overlay.cs"));
        int start = source.IndexOf("private void DrawInputBoardEditor", StringComparison.Ordinal);
        int end = source.IndexOf("private void DrawInputBoardPreview", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        string editor = source[start..end];

        Assert.Contains("string elementPopupId", editor);
        Assert.Contains("DrawPopupInputText(\"Label\", ref label, 24, elementPopupId", editor);
        Assert.Contains("DrawInputBoardIntStepperRow(\"X\",", editor);
        Assert.Contains("DrawInputBoardIntStepperRow(\"X\", () => element.X, value => element.X = Calc.Clamp(value, AkronInputBoard.MinimumPosition, AkronInputBoard.MaximumPosition), -2, 2, AkronInputBoard.MinimumPosition, AkronInputBoard.MaximumPosition, elementPopupId", editor);
        Assert.Contains("DrawInputBoardBindingEditor(element, elementPopupId)", editor);
        Assert.Contains("DrawInputBoardColorRow(\"Fill\",", editor);
        Assert.Contains("DrawInputBoardColorRow(\"Fill\", () => element.FillColor, value => element.FillColor = value, elementPopupId", editor);
    }

    [Fact]
    public void ToastStackOffsetAddsEveryNewerToastHeightPlusGap() {
        Assert.Equal(0f, AkronToast.CalculateStackOffset(Array.Empty<float>()));
        Assert.Equal(40f, AkronToast.CalculateStackOffset(new[] { 12f, 16f }));
    }

    [Fact]
    public void ImGuiItemTooltipsUseDeferredOverlayLayer() {
        Type overlayType = typeof(AkronOverlay);
        MethodInfo itemTooltip = overlayType.GetMethod("DrawImGuiItemTooltip", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo pendingTooltip = overlayType.GetMethod("DrawPendingImGuiItemTooltip", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.NotNull(itemTooltip);
        Assert.NotNull(pendingTooltip);
        Assert.False(itemTooltip.IsStatic);
        Assert.False(pendingTooltip.IsStatic);
        Assert.NotNull(overlayType.GetField("pendingImGuiTextTooltip", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.NotNull(overlayType.GetField("pendingImGuiTextTooltipAnchor", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    public void DeferredImGuiTooltipsUseTooltipLayerInsteadOfPlainWindows() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-imgui-popups.cs"));

        Assert.Contains("ImGui.BeginTooltip()", source);
        Assert.Contains("ImGui.EndTooltip()", source);
        Assert.DoesNotContain("##akron_anchored_tooltip_", source);
    }

    [Fact]
    public void ImGuiSubmenusUsePopupOutlineBorder() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-imgui-renderer.cs"));

        Assert.Contains("ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);", source);
        Assert.Contains("ImGui.PushStyleColor(ImGuiCol.Border, AkronImGuiTheme.PopupOutline);", source);
        Assert.Contains("ImGui.BeginPopup(popupId)", source);
    }

    [Fact]
    public void OverlayThemePresetsCycleThroughBuiltInKeycapPackThemes() {
        AkronOverlayThemePreset[] expected = {
            AkronOverlayThemePreset.Default,
            AkronOverlayThemePreset.Monochrome,
            AkronOverlayThemePreset.HighContrast,
            AkronOverlayThemePreset.Midnight,
            AkronOverlayThemePreset.Crimson,
            AkronOverlayThemePreset.Terminal,
            AkronOverlayThemePreset.Symbiote,
            AkronOverlayThemePreset.Carbon,
            AkronOverlayThemePreset.Retro,
            AkronOverlayThemePreset.Coniferous,
            AkronOverlayThemePreset.Wine,
            AkronOverlayThemePreset.Custom
        };

        AkronOverlayThemePreset current = expected[0];
        foreach (AkronOverlayThemePreset next in expected.Skip(1)) {
            current = AkronOverlayThemes.NextPreset(current);
            Assert.Equal(next, current);
            if (current != AkronOverlayThemePreset.Custom) {
                Assert.False(string.IsNullOrWhiteSpace(AkronOverlayThemes.DisplayName(current)));
            }
        }

        Assert.Equal(AkronOverlayThemePreset.Default, AkronOverlayThemes.NextPreset(current));
    }

    [Fact]
    public void AkronOverlayStaysOnFinalRenderPassWhileWorldDebugGeometryUsesGameplayPass() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/AkronModule.cs"));

        Assert.DoesNotContain("On.Celeste.Level.Render += LevelOnRender", source);
        Assert.DoesNotContain("private static void LevelOnRender(", source);
        Assert.Contains("On.Monocle.Engine.RenderCore += EngineOnRenderCore", source);
        Assert.Contains("RenderAkronLevelHud(postRenderLevel);", source);
        Assert.Contains("if (overlayVisible && !Overlay.RenderImGui())", source);
        Assert.Contains("On.Celeste.GameplayRenderer.Render += GameplayRendererOnRender", source);
        Assert.Contains("ShouldHideAkronRenderSurfaces()", source);
        Assert.Contains("!ShouldRenderGameplayDebugPass(level)", source);
        Assert.Contains("AkronHudRenderer.RenderAutomationAreasToGameplayBuffer(level);", source);
        Assert.Contains("AkronEntityInspector.RenderHitboxesToGameplayBuffer(level", source);
        Assert.DoesNotContain("RenderAkronHitboxHud(postRenderLevel);", source);
        Assert.DoesNotContain("RenderAutomationDeathAreasToGameplayBuffer", source);
    }

    [Fact]
    public void SpeedrunToolStateTransitionsSuppressAkronRenderSurfacesBriefly() {
        string interopSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Interop/akron-interop.cs"));
        string moduleSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/AkronModule.cs"));
        string suppressionSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Runtime/akron-render-transition-suppression.cs"));

        Assert.Contains("Celeste.Mod.SpeedrunTool.ModInterop.SaveLoadInterop+SaveLoadExports", interopSource);
        Assert.Contains("EnsureSpeedrunToolSaveLoadHooksRegistered", interopSource);
        Assert.Contains("AkronModule.SuppressAkronRenderSurfacesAfterStateTransition", interopSource);
        Assert.Contains("UnregisterSpeedrunToolSaveLoadHooks", interopSource);
        Assert.Contains("AkronInterop.EnsureSpeedrunToolSaveLoadHooksRegistered();", moduleSource);
        Assert.Contains("AkronInterop.UnregisterSpeedrunToolSaveLoadHooks();", moduleSource);
        Assert.DoesNotContain("typeof(SpeedrunToolSaveLoadShim).ModInterop()", moduleSource);
        Assert.DoesNotContain("ModExportName(\"SpeedrunTool.SaveLoad\")", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/SaveLoad/akron-save-load-exports.cs")));
        Assert.Contains("UpdateStateTransitionRenderSuppression();", moduleSource);
        Assert.Contains("private const int StateTransitionRenderSuppressionFrames = 30;", suppressionSource);
        Assert.Contains("internal static bool ShouldHideAkronRenderSurfaces()", suppressionSource);
        Assert.Contains("internal static bool ShouldHideAkronRenderSurfacesAfterStateTransition()", suppressionSource);
    }

    [Fact]
    public void NativeStartPosRestoresRebuildFrostHelperSpinnerRenderers() {
        string saveLoadSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/SaveLoad/AkronSaveLoad.cs"));

        Assert.Contains("FrostHelperSpinnerRendererTypeNames", saveLoadSource);
        Assert.Contains("\"SpinnerConnectorRenderer\"", saveLoadSource);
        Assert.Contains("\"SpinnerDecoRenderer\"", saveLoadSource);
        Assert.Contains("\"SpinnerBorderRenderer\"", saveLoadSource);
        Assert.Contains("RebuildFrostHelperSpinnerRendererRegistrations(level);", saveLoadSource);
        Assert.Contains("\"FrostHelper.CustomSpinner\"", saveLoadSource);
        Assert.Contains("\"CreateRenderersIfNeeded\"", saveLoadSource);
        Assert.Contains("\"RegisterToRenderers\"", saveLoadSource);
        Assert.Contains("AkronModule.SuppressAkronRenderSurfacesAfterStateTransition();", saveLoadSource);
    }

    [Fact]
    public void ImGuiRendererRestoresGraphicsDeviceState() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-imgui-renderer.cs"));

        Assert.Contains("private sealed class GraphicsDeviceStateScope", source);
        Assert.Contains("graphicsDevice.GetRenderTargets()", source);
        Assert.Contains("graphicsDevice.SetRenderTargets(renderTargets)", source);
        Assert.Contains("graphicsDevice.GetVertexBuffers()", source);
        Assert.Contains("graphicsDevice.SetVertexBuffers(vertexBuffers)", source);
        Assert.Contains("graphicsDevice.Textures[0]", source);
        Assert.Contains("graphicsDevice.SamplerStates[0]", source);
        Assert.Contains("using (new GraphicsDeviceStateScope(graphicsDevice))", source);
    }

    [Theory]
    [InlineData("Global", 0)]
    [InlineData("Level", 1)]
    [InlineData("Player", 3)]
    [InlineData("Shortcuts", 7)]
    public void BaseTabsKeepTheirEightColumnPositions(string tabName, int expectedColumn) {
        Assert.Equal(expectedColumn, InvokeActionColumnIndex(tabName, 8, new List<float>(), new List<int>()));
    }

    [Fact]
    public void ShortExternalToolTabsUseShortestSingleSectionColumns() {
        List<float> columnBottoms = new List<float> { 100f, 50f, 110f, 70f, 80f, 120f, 90f, 130f };
        List<int> columnSectionCounts = new List<int> { 2, 1, 2, 1, 1, 2, 1, 2 };

        int speedrunToolColumn = InvokeActionColumnIndex("Speedrun Tool", 8, columnBottoms, columnSectionCounts);
        Assert.Equal(1, speedrunToolColumn);

        columnBottoms[speedrunToolColumn] = 160f;
        columnSectionCounts[speedrunToolColumn]++;

        int celesteTasColumn = InvokeActionColumnIndex("CelesteTAS", 8, columnBottoms, columnSectionCounts);
        Assert.Equal(3, celesteTasColumn);
    }

    [Fact]
    public void ExtendedVariantModeUsesShortestCurrentColumn() {
        List<float> columnBottoms = new List<float> { 100f, 160f, 110f, 160f, 80f, 120f, 90f, 130f };
        List<int> columnSectionCounts = new List<int> { 2, 2, 2, 2, 1, 2, 1, 2 };

        Assert.Equal(4, InvokeActionColumnIndex("Extended Variant Mode", 8, columnBottoms, columnSectionCounts));
    }

    [Fact]
    public void HudElementOverlapFadesEveryLabelWhenOnlyCurrentIsOff() {
        AkronModuleSettings settings = new AkronModuleSettings {
            CustomHudLabelObstructionEnabled = true,
            CustomHudLabelObstructionMode = AkronLabelObstructionMode.Fade,
            CustomHudLabelObstructedOpacity = 35,
            CustomHudLabelObstructionOnlyOverlappedLabel = false
        };
        Vector2 position = TestVector(20f, 20f);
        float opacity = 0.90f;

        bool applied = AkronHudRenderer.TryApplyHudElementPlayerOverlap(
            settings,
            new AkronHudRect(500f, 500f, 16f, 32f),
            true,
            TestVector(80f, 24f),
            ref position,
            ref opacity);

        Assert.True(applied);
        Assert.Equal(20f, position.X);
        Assert.Equal(20f, position.Y);
        Assert.Equal(0.35f, opacity, precision: 3);
    }

    [Fact]
    public void HudElementOverlapOnlyCurrentRequiresElementBoundsToIntersect() {
        AkronModuleSettings settings = new AkronModuleSettings {
            CustomHudLabelObstructionEnabled = true,
            CustomHudLabelObstructionMode = AkronLabelObstructionMode.Fade,
            CustomHudLabelObstructedOpacity = 35,
            CustomHudLabelObstructionOnlyOverlappedLabel = true,
            CustomHudLabelObstructionPaddingPixels = 0
        };
        AkronHudRect player = new AkronHudRect(500f, 500f, 16f, 32f);
        Vector2 farPosition = TestVector(20f, 20f);
        float farOpacity = 0.90f;

        bool farApplied = AkronHudRenderer.TryApplyHudElementPlayerOverlap(
            settings,
            player,
            true,
            TestVector(80f, 24f),
            ref farPosition,
            ref farOpacity);

        Vector2 overlappingPosition = TestVector(490f, 510f);
        float overlappingOpacity = 0.90f;
        bool overlappingApplied = AkronHudRenderer.TryApplyHudElementPlayerOverlap(
            settings,
            player,
            false,
            TestVector(80f, 24f),
            ref overlappingPosition,
            ref overlappingOpacity);

        Assert.False(farApplied);
        Assert.Equal(0.90f, farOpacity, precision: 3);
        Assert.True(overlappingApplied);
        Assert.Equal(0.35f, overlappingOpacity, precision: 3);
    }

    [Fact]
    public void HudElementOverlapMoveUsesConfiguredObstructedAnchor() {
        AkronModuleSettings settings = new AkronModuleSettings {
            CustomHudLabelObstructionEnabled = true,
            CustomHudLabelObstructionMode = AkronLabelObstructionMode.Move,
            CustomHudLabelPadding = 100,
            CustomHudLabelObstructedAnchor = AkronHudAnchor.BottomRight,
            CustomHudLabelObstructedOffsetX = -12,
            CustomHudLabelObstructedOffsetY = 8
        };
        Vector2 position = TestVector(20f, 20f);
        float opacity = 0.90f;

        bool applied = AkronHudRenderer.TryApplyHudElementPlayerOverlap(
            settings,
            new AkronHudRect(500f, 500f, 16f, 32f),
            true,
            TestVector(80f, 24f),
            ref position,
            ref opacity);

        Assert.True(applied);
        Assert.Equal(1728f, position.X);
        Assert.Equal(964f, position.Y);
        Assert.Equal(0.90f, opacity, precision: 3);
    }

    private static Vector2 TestVector(float x, float y) {
        Vector2 vector = default;
        vector.X = x;
        vector.Y = y;
        return vector;
    }

    private static string[] InvokeBuildVisibleTabs(bool speedrunToolLoaded, bool celesteTasLoaded, bool extendedVariantModeAvailable, bool extendedCameraDynamicsLoaded) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("BuildVisibleTabs", BindingFlags.Static | BindingFlags.NonPublic)!;
        return ((IEnumerable<string>) method.Invoke(null, new object[] { speedrunToolLoaded, celesteTasLoaded, extendedVariantModeAvailable, extendedCameraDynamicsLoaded })!).ToArray();
    }

    private static string[] InvokeStaticStringArray(string methodName) {
        MethodInfo method = typeof(AkronOverlay).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
        return ((IEnumerable<string>) method.Invoke(null, Array.Empty<object>())!).ToArray();
    }

    private static int InvokeActionColumnIndex(string tabName, int availableColumns, List<float> columnBottoms, List<int> columnSectionCounts) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("GetActionColumnIndex", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (int) method.Invoke(null, new object[] { tabName, availableColumns, columnBottoms, columnSectionCounts, 500f })!;
    }

    private static List<string> BuildOverlayEntryLabels(string tab) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("BuildDisplayEntriesForTab", BindingFlags.NonPublic | BindingFlags.Static)!;
        object entries = method.Invoke(null, new object?[] { tab, null })!;
        return ExtractOverlayEntryLabels(entries);
    }

    private static List<string> BuildGlobalEntryLabels(bool motionSmoothingLoaded) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("BuildGlobalEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        object entries = method.Invoke(null, new object[] { motionSmoothingLoaded })!;
        return ExtractOverlayEntryLabels(entries);
    }

    private static Dictionary<string, string> BuildGlobalEntryControls(bool motionSmoothingLoaded) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("BuildGlobalEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        object entries = method.Invoke(null, new object[] { motionSmoothingLoaded })!;
        Type entryType = entries.GetType().GetGenericArguments()[0];
        PropertyInfo labelProperty = entryType.GetProperty("Label", BindingFlags.Public | BindingFlags.Instance)!;
        PropertyInfo controlProperty = entryType.GetProperty("Control", BindingFlags.Public | BindingFlags.Instance)!;

        return ((System.Collections.IEnumerable) entries)
            .Cast<object>()
            .ToDictionary(
                entry => (string) labelProperty.GetValue(entry)!,
                entry => controlProperty.GetValue(entry)!.ToString()!,
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildOverlayEntryOptionsPopupKeys(string tab) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("BuildDisplayEntriesForTab", BindingFlags.NonPublic | BindingFlags.Static)!;
        object entries = method.Invoke(null, new object?[] { tab, null })!;
        Type entryType = entries.GetType().GetGenericArguments()[0];
        PropertyInfo labelProperty = entryType.GetProperty("Label", BindingFlags.Public | BindingFlags.Instance)!;
        PropertyInfo popupKeyProperty = entryType.GetProperty("OptionsPopupKey", BindingFlags.Public | BindingFlags.Instance)!;

        return ((System.Collections.IEnumerable) entries)
            .Cast<object>()
            .ToDictionary(
                entry => (string) labelProperty.GetValue(entry)!,
                entry => (string) (popupKeyProperty.GetValue(entry) ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> ExtractOverlayEntryLabels(object entries) {
        PropertyInfo labelProperty = entries.GetType().GetGenericArguments()[0].GetProperty("Label", BindingFlags.Public | BindingFlags.Instance)!;

        return ((System.Collections.IEnumerable) entries)
            .Cast<object>()
            .Select(entry => (string) labelProperty.GetValue(entry)!)
            .ToList();
    }

    private static bool HasOverlayOptionsPopup(string label) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("HasOptionsPopup", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool) method.Invoke(null, new object[] { label })!;
    }

    private static HashSet<string> GetCollapsedWindowTitles(AkronOverlay overlay) {
        FieldInfo field = typeof(AkronOverlay).GetField("collapsedWindowTitles", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (HashSet<string>) field.GetValue(overlay)!;
    }

    private static AkronOverlay CreateOverlayForStateTest() {
        AkronOverlay overlay = (AkronOverlay) RuntimeHelpers.GetUninitializedObject(typeof(AkronOverlay));
        SetPrivateField(overlay, "collapsedWindowTitles", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        SetPrivateField(overlay, "pendingImGuiCollapseSync", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        overlay.Visible = true;
        return overlay;
    }

    private static void SetPrivateField(object target, string fieldName, object value) {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(target, value);
    }

}
