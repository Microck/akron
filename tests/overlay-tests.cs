using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste.Mod.Akron;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
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
    public void OptionsPopupsBindRegisteredSuboptionsFromTheirControls() {
        string popupSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-popup-controls.cs"));
        string bindingSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-bindable-actions.cs"));
        string frameStepperSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-bypass-popups.cs"));
        string gameplaySource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-gameplay-popups.cs"));
        string noclipSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-noclip-popups.cs"));
        string startPosSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-startpos-popups.cs"));

        Assert.DoesNotContain("DrawRegisteredPopupActionBindings(entry.Label, popupId);", popupSource);
        Assert.DoesNotContain("private void DrawRegisteredPopupActionBindings", bindingSource);
        Assert.Contains("private void DrawPopupActionBindingContext", bindingSource);
        Assert.Contains("PopupActionKey(\"Frame Stepper\", \"Step Once\")", bindingSource);
        Assert.Contains("\"Frame Stepper / Step Once\"", bindingSource);
        Assert.Contains("DrawPopupActionBindingContext(\"Frame Stepper\", \"Step Once\"", frameStepperSource);
        Assert.Contains("DrawPopupActionBindingContext(\"Frame Stepper\", \"Repeat\"", frameStepperSource);
        Assert.Contains("\"Frame Stepper\", \"Delay Down\", \"Delay Up\"", frameStepperSource);
        Assert.Contains("DrawTimescalePopupControls(popupId)", popupSource);
        Assert.Contains("DrawPopupActionBindingContext(\"Timescale\", \"Toggle\"", frameStepperSource);
        Assert.Contains("\"Timescale\", \"Decrease\", \"Increase\"", frameStepperSource);
        Assert.Contains("DrawPopupActionBindingContext(\"Timescale\", \"Reset\"", frameStepperSource);
        Assert.Contains("StepHoldDelayFrames - 1, 1, 120", bindingSource);
        Assert.Contains("StepHoldDelayFrames + 1, 1, 120", bindingSource);
        Assert.Contains("StepHoldIntervalFrames - 1, 1, 60", bindingSource);
        Assert.Contains("StepHoldIntervalFrames + 1, 1, 60", bindingSource);
        Assert.Contains("DrawPopupActionBindingContext(\"Grab Mode\", label", gameplaySource);
        Assert.Contains("\"Noclip\", \"Speed Down\", \"Speed Up\"", noclipSource);
        Assert.Contains("DrawPopupActionBindingContext(\"Noclip\", \"Toggle\"", noclipSource);
        Assert.Contains("DrawPopupActionBindingContext(\"Hazard Accuracy\", \"Toggle\"", noclipSource);
        Assert.Contains("for (int slot = 1; slot <= 9; slot++)", startPosSource);
        Assert.Contains("AkronActions.LoadStartPosSlot(level, slot)", startPosSource);
        Assert.Contains("DrawPopupActionBindingContext(\"StartPos\", actionName)", startPosSource);
        Assert.Contains("DrawPopupActionBindingContext(\"StartPos\", \"Respawn\"", startPosSource);
        Assert.Contains("entry.OptionsPopupKey, \"StartPos Snapshot Slot\"", popupSource);
        Assert.Contains("DrawSavestateSlotPopupControls(popupId);", popupSource);
        Assert.Contains("DrawStartPosPopupControls(popupId);", popupSource);
        Assert.DoesNotContain("entry.Label, \"StartPos Snapshot Slot\"", popupSource);
    }

    [Fact]
    public void AkronDotBindingsSuspendEverestDebugConsoleBinding() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-bindable-actions.cs"));
        string moduleSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/AkronModule.cs"));
        string overlaySource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/AkronOverlay.cs"));

        Assert.Contains("CommandsOnUpdateClosed", source);
        Assert.Contains("Keyboard.GetState().IsKeyDown(Keys.OemPeriod)", source);
        Assert.Contains("AkronModule.IsOverlayBindingCaptureActive", source);
        Assert.Contains("AkronModule.IsOverlayKeyboardInputOwned", source);
        Assert.Contains("AkronUsesPressedKeyboardKey(AkronModule.Settings, Keys.OemPeriod)", source);
        Assert.Contains("ShouldSuppressEverestDebugConsole(", source);
        Assert.Contains("ActiveMenuBindingMatchesPressedKey(", source);
        Assert.Contains("GetBindableActions(level)", source);
        Assert.Contains("Keys.OemPeriod", source);
        Assert.Contains("return;", source);
        Assert.Contains("internal static bool IsOverlayBindingCaptureActive", moduleSource);
        Assert.Contains("internal static bool IsOverlayKeyboardInputOwned", moduleSource);
        Assert.Contains("internal bool HasActiveBindingCapture", overlaySource);
        Assert.DoesNotContain("MInput.Keyboard.Pressed(Keys.OemPeriod)", source);
        Assert.DoesNotContain("ConsumePress()", source);
        Assert.DoesNotContain("debugConsole.Keys.RemoveAll", source);
        Assert.DoesNotContain("settings.MenuActionBindings.Keys.ToHashSet", source);
    }

    [Theory]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, false, false, true)]
    [InlineData(true, false, true, false, true)]
    [InlineData(true, false, false, true, true)]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, false, true, false, true)]
    public void EverestDebugConsoleSuppressionIncludesAkronTextInput(
        bool periodDown,
        bool bindingCaptureActive,
        bool keyboardInputOwned,
        bool usesPeriodBinding,
        bool expected) {
        Assert.Equal(
            expected,
            AkronOverlay.ShouldSuppressEverestDebugConsole(
                periodDown,
                bindingCaptureActive,
                keyboardInputOwned,
                usesPeriodBinding));
    }

    [Fact]
    public void OverlayKeyboardOwnershipIncludesActiveNumericInput() {
        Assert.True(AkronOverlay.ShouldOwnCurrentKeyboardFrame(
            transientUiActive: false,
            overlayVisible: true,
            promptOpen: false,
            imguiWantsKeyboard: false,
            imguiWantsTextInput: false,
            searchInputActive: false,
            valueEditActive: true,
            anyKeyPressed: false));
    }

    [Fact]
    public void OverlayKeyboardOwnershipIncludesImGuiTextInputFocus() {
        Assert.True(AkronOverlay.ShouldOwnCurrentKeyboardFrame(
            transientUiActive: false,
            overlayVisible: true,
            promptOpen: false,
            imguiWantsKeyboard: false,
            imguiWantsTextInput: true,
            searchInputActive: false,
            valueEditActive: false,
            anyKeyPressed: false));
    }

    [Fact]
    public void InlineNumericInputLatchesKeyboardOwnershipWhileFocused() {
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../Source/Overlay/akron-overlay-imgui-value-rows.cs"));

        Assert.Contains(
            "if (valueActive) {\n            MarkValueEditFreeze();\n        }",
            source);
    }

    [Fact]
    public void KeyboardInputSessionBlocksHeldKeysUntilRelease() {
        HashSet<Keys> blockedKeys = new HashSet<Keys> {
            Keys.Tab,
            Keys.D
        };

        Keys[] held = AkronImGuiRenderer.FilterPressedKeys(
            new[] { Keys.Tab, Keys.D },
            blockedKeys);
        Keys[] released = AkronImGuiRenderer.FilterPressedKeys(
            Array.Empty<Keys>(),
            blockedKeys);
        Keys[] pressedAgain = AkronImGuiRenderer.FilterPressedKeys(
            new[] { Keys.D },
            blockedKeys);

        Assert.Empty(held);
        Assert.Empty(released);
        Assert.Empty(blockedKeys);
        Assert.Equal(new[] { Keys.D }, pressedAgain);
    }

    [Fact]
    public void HiddenOverlayConsumesExactlyTwoCleanupFrames() {
        AkronImGuiFrameLifecycle lifecycle = new AkronImGuiFrameLifecycle();

        lifecycle.RequestCleanup();

        Assert.True(lifecycle.NeedsFrame(rendererInitialized: true));
        Assert.Equal(
            AkronImGuiFrameKind.Cleanup,
            lifecycle.TakeNextFrame(rendererInitialized: true, surfaceRequested: false));
        lifecycle.MarkCleanupFrameSubmitted();
        Assert.True(lifecycle.NeedsFrame(rendererInitialized: true));
        Assert.Equal(
            AkronImGuiFrameKind.Cleanup,
            lifecycle.TakeNextFrame(rendererInitialized: true, surfaceRequested: false));
        lifecycle.MarkCleanupFrameSubmitted();
        Assert.False(lifecycle.NeedsFrame(rendererInitialized: true));
        Assert.Equal(
            AkronImGuiFrameKind.None,
            lifecycle.TakeNextFrame(rendererInitialized: true, surfaceRequested: false));
    }

    [Fact]
    public void FailedCleanupFrameRemainsPending() {
        AkronImGuiFrameLifecycle lifecycle = new AkronImGuiFrameLifecycle();

        lifecycle.RequestCleanup();

        Assert.Equal(
            AkronImGuiFrameKind.Cleanup,
            lifecycle.TakeNextFrame(rendererInitialized: true, surfaceRequested: false));
        Assert.True(lifecycle.NeedsFrame(rendererInitialized: true));
        Assert.Equal(
            AkronImGuiFrameKind.Cleanup,
            lifecycle.TakeNextFrame(rendererInitialized: true, surfaceRequested: false));
    }

    [Fact]
    public void ImmediateReopenCleansBeforeSubmittingTheOverlayAgain() {
        AkronImGuiFrameLifecycle lifecycle = new AkronImGuiFrameLifecycle();

        lifecycle.RequestCleanup();

        Assert.Equal(
            AkronImGuiFrameKind.Cleanup,
            lifecycle.TakeNextFrame(rendererInitialized: true, surfaceRequested: true));
        lifecycle.MarkCleanupFrameSubmitted();
        Assert.Equal(
            AkronImGuiFrameKind.Cleanup,
            lifecycle.TakeNextFrame(rendererInitialized: true, surfaceRequested: true));
        lifecycle.MarkCleanupFrameSubmitted();
        Assert.Equal(
            AkronImGuiFrameKind.Surface,
            lifecycle.TakeNextFrame(rendererInitialized: true, surfaceRequested: true));
    }

    [Fact]
    public void SuppressingASubmittedSurfaceQueuesTwoCleanupFrames() {
        AkronImGuiFrameLifecycle lifecycle = new AkronImGuiFrameLifecycle();
        lifecycle.MarkSurfaceSubmitted();

        Assert.True(lifecycle.NeedsFrame(rendererInitialized: true));
        Assert.Equal(
            AkronImGuiFrameKind.Cleanup,
            lifecycle.TakeNextFrame(rendererInitialized: true, surfaceRequested: false));
        lifecycle.MarkCleanupFrameSubmitted();
        Assert.Equal(
            AkronImGuiFrameKind.Cleanup,
            lifecycle.TakeNextFrame(rendererInitialized: true, surfaceRequested: false));
        lifecycle.MarkCleanupFrameSubmitted();
        Assert.Equal(
            AkronImGuiFrameKind.None,
            lifecycle.TakeNextFrame(rendererInitialized: true, surfaceRequested: false));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ImGuiSurfaceRequestIncludesStartPosPlacement(
        bool overlaySurfaceRequested,
        bool startPosPlacementActive,
        bool expected) {
        Assert.Equal(
            expected,
            AkronOverlay.ShouldRequestImGuiSurface(
                overlaySurfaceRequested,
                startPosPlacementActive));
    }

    [Fact]
    public void CleanupDoesNotInitializeImGuiForAnOverlayThatWasNeverRendered() {
        AkronImGuiFrameLifecycle lifecycle = new AkronImGuiFrameLifecycle();

        lifecycle.RequestCleanup();

        Assert.False(lifecycle.NeedsFrame(rendererInitialized: false));
        Assert.Equal(
            AkronImGuiFrameKind.Surface,
            lifecycle.TakeNextFrame(rendererInitialized: false, surfaceRequested: true));
    }

    [Fact]
    public void PeriodOwnershipHooksTheRuntimeActionWithoutMutatingSetupImports() {
        string moduleSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/AkronModule.cs"));
        string bindingSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-bindable-actions.cs"));
        string setupSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Setups/akron-setup-packs.cs"));

        Assert.Contains("On.Monocle.Commands.UpdateClosed += AkronOverlay.CommandsOnUpdateClosed;", moduleSource);
        Assert.Contains("On.Monocle.Commands.UpdateClosed -= AkronOverlay.CommandsOnUpdateClosed;", moduleSource);
        Assert.Contains("internal static void CommandsOnUpdateClosed", bindingSource);
        Assert.DoesNotContain("CoreModule.Settings", setupSource);
    }

    [Fact]
    public void PeriodChordRequiresItsModifiersBeforeSuppressingEverest() {
        List<Keys> binding = new List<Keys> { Keys.LeftControl, Keys.OemPeriod };

        Assert.False(AkronOverlay.KeyboardChordMatches(
            binding,
            Keys.OemPeriod,
            new List<Keys> { Keys.OemPeriod }));
        Assert.True(AkronOverlay.KeyboardChordMatches(
            binding,
            Keys.OemPeriod,
            new List<Keys> { Keys.LeftControl, Keys.OemPeriod }));
    }

    [Fact]
    public void PeriodOwnershipIgnoresInactivePersistedMenuActions() {
        Dictionary<string, string> bindings = new Dictionary<string, string> {
            ["popup/Noclip/Toggle"] = "LeftControl+OemPeriod",
            ["popup/Removed/Action"] = "OemPeriod"
        };
        HashSet<string> activeActionKeys = new HashSet<string> {
            "popup/Noclip/Toggle"
        };

        Assert.False(AkronOverlay.ActiveMenuBindingMatchesPressedKey(
            bindings,
            activeActionKeys,
            Keys.OemPeriod,
            new List<Keys> { Keys.OemPeriod }));
        Assert.True(AkronOverlay.ActiveMenuBindingMatchesPressedKey(
            bindings,
            activeActionKeys,
            Keys.OemPeriod,
            new List<Keys> { Keys.LeftControl, Keys.OemPeriod }));
    }

    [Fact]
    public void NonGameplayPeriodOwnershipOnlyChecksGlobalBuiltInActions() {
        AkronModuleSettings settings = new AkronModuleSettings();
        AkronModuleSettings.EnsureCurrentKeybindDefaults(settings);

        List<ButtonBinding> bindings =
            AkronModule.GetActiveGlobalButtonBindings(
            settings,
            overlayVisible: false,
            suppressOverlayToggle: false,
            frameBypassAvailable: true,
            frameBypassActive: false).ToList();

        Assert.Contains(settings.ToggleOverlay, bindings);
        Assert.Contains(settings.ToggleFrameBypass, bindings);
        Assert.DoesNotContain(settings.CycleFrameBypassCameraSmoothing, bindings);
        Assert.DoesNotContain(settings.Retry, bindings);
        Assert.DoesNotContain(settings.SetStartPos, bindings);

        bindings = AkronModule.GetActiveGlobalButtonBindings(
            settings,
            overlayVisible: false,
            suppressOverlayToggle: false,
            frameBypassAvailable: true,
            frameBypassActive: true).ToList();
        Assert.Contains(settings.CycleFrameBypassCameraSmoothing, bindings);

        bindings = AkronModule.GetActiveGlobalButtonBindings(
            settings,
            overlayVisible: true,
            suppressOverlayToggle: false,
            frameBypassAvailable: true,
            frameBypassActive: true).ToList();
        Assert.Contains(settings.ToggleOverlay, bindings);
        Assert.DoesNotContain(settings.ToggleFrameBypass, bindings);

        bindings = AkronModule.GetActiveGlobalButtonBindings(
            settings,
            overlayVisible: false,
            suppressOverlayToggle: true,
            frameBypassAvailable: true,
            frameBypassActive: true).ToList();
        Assert.Empty(bindings);

        bindings = AkronModule.GetActiveGlobalButtonBindings(
            settings,
            overlayVisible: false,
            suppressOverlayToggle: false,
            frameBypassAvailable: false,
            frameBypassActive: true).ToList();
        Assert.Contains(settings.ToggleOverlay, bindings);
        Assert.DoesNotContain(settings.ToggleFrameBypass, bindings);
    }

    [Fact]
    public void IneligibleLevelPeriodOwnershipKeepsOnlyTheOverlayToggleActive() {
        AkronModuleSettings settings = new AkronModuleSettings();
        AkronModuleSettings.EnsureCurrentKeybindDefaults(settings);

        List<ButtonBinding> bindings =
            AkronOverlay.GetActiveLevelButtonBindings(
                settings,
                canExecuteLevelActionBindings: false).ToList();

        Assert.Contains(settings.ToggleOverlay, bindings);
        Assert.DoesNotContain(settings.Retry, bindings);
        Assert.DoesNotContain(settings.StepFrame, bindings);
    }

    [Fact]
    public void LevelPeriodOwnershipExcludesDisabledFeatureHoldBindings() {
        AkronModuleSettings settings = new AkronModuleSettings();
        AkronModuleSettings.EnsureCurrentKeybindDefaults(settings);
        AkronModuleSession session = new AkronModuleSession {
            FreezeGameplay = true
        };

        List<ButtonBinding> bindings =
            AkronOverlay.GetActiveLevelButtonBindings(
                settings,
                canExecuteLevelActionBindings: true,
                session,
                frameBypassAvailable: true,
                frameBypassActive: false).ToList();

        Assert.Contains(settings.Retry, bindings);
        Assert.DoesNotContain(settings.FastLookoutHold, bindings);
        Assert.DoesNotContain(settings.CursorZoomHold, bindings);
        Assert.DoesNotContain(settings.StepFrame, bindings);

        bindings = AkronOverlay.GetActiveLevelButtonBindings(
            settings,
            canExecuteLevelActionBindings: true,
            session,
            frameBypassAvailable: false,
            frameBypassActive: true).ToList();
        Assert.DoesNotContain(settings.ToggleFrameBypass, bindings);
        Assert.DoesNotContain(settings.CycleFrameBypassCameraSmoothing, bindings);

        settings.FastLookout = true;
        settings.CursorZoom = true;
        settings.FrameStepper = true;
        bindings = AkronOverlay.GetActiveLevelButtonBindings(
            settings,
            canExecuteLevelActionBindings: true,
            session,
            frameBypassAvailable: true,
            frameBypassActive: false).ToList();

        Assert.Contains(settings.FastLookoutHold, bindings);
        Assert.Contains(settings.CursorZoomHold, bindings);
        Assert.Contains(settings.StepFrame, bindings);
    }

    [Fact]
    public void NativeChordActionsRequireTheirModifiersBeforeClaimingPeriod() {
        AkronModuleSettings settings = new AkronModuleSettings();
        AkronModuleSettings.EnsureCurrentKeybindDefaults(settings);
        List<Keys> chord = new List<Keys> {
            Keys.LeftControl,
            Keys.OemPeriod
        };

        Assert.True(AkronOverlay.UsesKeyboardChordDispatch(
            settings,
            settings.ToggleOverlay));
        Assert.False(AkronOverlay.UsesKeyboardChordDispatch(
            settings,
            settings.Retry));
        Assert.False(AkronOverlay.NativeBindingMatchesPressedKey(
            usesKeyboardChord: true,
            chord,
            Keys.OemPeriod,
            new List<Keys> { Keys.OemPeriod },
            bindingPressed: true));
        Assert.True(AkronOverlay.NativeBindingMatchesPressedKey(
            usesKeyboardChord: true,
            chord,
            Keys.OemPeriod,
            new List<Keys> { Keys.LeftControl, Keys.OemPeriod },
            bindingPressed: true));
        Assert.True(AkronOverlay.NativeBindingMatchesPressedKey(
            usesKeyboardChord: false,
            chord,
            Keys.OemPeriod,
            new List<Keys> { Keys.OemPeriod },
            bindingPressed: true));
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    public void LevelActionBindingsUseTheSameOverlayAndPauseEligibility(
        bool overlayVisible,
        bool levelPaused,
        bool allowPauseBuffering,
        bool expected) {
        Assert.Equal(
            expected,
            AkronModule.CanExecuteLevelActionBindings(
                overlayVisible,
                levelPaused,
                allowPauseBuffering));
    }

    [Fact]
    public void UploadPackWindowOffersAllSupportedUploadSections() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Community/akron-community-pack-upload-window.cs"));
        int methodStart = source.IndexOf("private void DrawCommunityPackUploadForm", StringComparison.Ordinal);
        int nextMethod = source.IndexOf("private static void DrawCommunityPackUploadSectionChoice", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(nextMethod > methodStart);
        string method = source[methodStart..nextMethod];

        Assert.Contains("DrawCommunityPackUploadSectionChoice(\"StartPos\", AkronSetupSection.StartPos", method);
        Assert.Contains("DrawCommunityPackUploadSectionChoice(\"Auto Kill\", AkronSetupSection.AutoKill", method);
        Assert.Contains("DrawCommunityPackUploadSectionChoice(\"Auto Deafen\", AkronSetupSection.AutoDeafen", method);
    }

    [Fact]
    public void UploadPackWindowLetsDiscordUsersEnterIdInline() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Community/akron-community-pack-upload-window.cs"));
        int methodStart = source.IndexOf("private void DrawCommunityPackUploadForm", StringComparison.Ordinal);
        int nextMethod = source.IndexOf("private static void DrawCommunityPackUploadSectionChoice", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(nextMethod > methodStart);
        string method = source[methodStart..nextMethod];

        Assert.Contains("DrawCommunityPackUploadAttributionChoice(\"Discord\", true", method);
        Assert.Contains("\"Discord ID\"", method);
        Assert.Contains("CommunityPackUploadDiscordUserId = discordUserId.Trim();", method);
        Assert.DoesNotContain("Set a saved Discord user ID from the Upload Pack row submenu", method);
    }

    [Fact]
    public void UploadPackWindowUsesCompactAlignedSingleColumnForm() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Community/akron-community-pack-upload-window.cs"));

        Assert.Contains("Math.Min(640f", source);
        Assert.Contains("float targetHeight = hasStatus ? 296f : 260f;", source);
        Assert.Contains("Math.Min(targetHeight", source);
        Assert.Contains("DrawPopupRowLabel(\"Map\"", source);
        Assert.Contains("DrawPopupRowLabel(\"Category\"", source);
        Assert.Contains("DrawPopupRowLabel(\"Attribution\"", source);
        Assert.Contains("DrawPopupRowLabel(\"Status\"", source);
        Assert.Contains("ImGui.ProgressBar(", source);
        Assert.Contains("AkronCommunityPackUploads.DescribeUploadStatus()", source);
        Assert.DoesNotContain("uploadPackWindowOpen = false;", source);
        Assert.DoesNotContain("DrawPopupRowLabel(\"Review\"", source);
        Assert.DoesNotContain("DrawPopupRowLabel(\"Confirm\"", source);
        Assert.DoesNotContain("I created this pack and it can be shared publicly", source);
        Assert.DoesNotContain("Use Generated Text", source);
        Assert.DoesNotContain("Preview:", source);
        Assert.DoesNotContain("private void DrawCommunityPackUploadSummary", source);
        Assert.DoesNotContain("ImGui.Columns(2, \"##upload-pack-columns-\"", source);
        Assert.DoesNotContain("DrawCommunityPackUploadPreview", source);
    }

    [Fact]
    public void OverlayResetClosesUploadPackWindow() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/AkronOverlay.cs"));
        int methodStart = source.IndexOf("internal void ResetTransientUiState(bool searchAutofocus)", StringComparison.Ordinal);
        int nextMethod = source.IndexOf("public void OpenTab", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(nextMethod > methodStart);
        string method = source[methodStart..nextMethod];

        Assert.Contains("uploadPackWindowOpen = false;", method);
    }

    [Fact]
    public void UploadPackRowSubmenuOnlyEditsUploadDefaults() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-popup-controls.cs"));
        int methodStart = source.IndexOf("private void DrawUploadPackPopupControls", StringComparison.Ordinal);
        int nextMethod = source.IndexOf("private void DrawLoggingPopupControls", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(nextMethod > methodStart);
        string method = source[methodStart..nextMethod];

        Assert.Contains("\"Discord ID\"", method);
        Assert.Contains("\"Click Upload Pack to open the submission form.\"", method);
        Assert.DoesNotContain("\"Title\"", method);
        Assert.DoesNotContain("\"Desc\"", method);
        Assert.DoesNotContain("\"Submit Upload\"", method);
        Assert.DoesNotContain("DrawCommunityPackUploadSectionChoice", method);
        Assert.DoesNotContain("DrawUploadSectionChoice", method);
        Assert.DoesNotContain("DrawUploadAttributionChoice", method);
    }

    [Fact]
    public void UploadPackRowOpensStandaloneUploadWindow() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-entries.cs"));
        int methodStart = source.IndexOf("private static OverlayEntry UploadPackRow", StringComparison.Ordinal);
        int nextMethod = source.IndexOf("private static OverlayEntry StartPosRow", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(nextMethod > methodStart);
        string method = source[methodStart..nextMethod];

        Assert.Contains("OpenUploadPackWindow();", method);
        Assert.DoesNotContain("OpenUploadPrompt", method);
    }

    [Fact]
    public void FreezeGameplayAndFrameStepperUseSeparateFeatureKinds() {
        Dictionary<string, AkronFeatureKind?> globalKinds = BuildOverlayEntryFeatureKinds("Global");
        Dictionary<string, AkronFeatureKind?> levelKinds = BuildOverlayEntryFeatureKinds("Level");
        Dictionary<string, bool> globalToggleStates = BuildOverlayEntryIsToggles("Global");
        Dictionary<string, bool> levelToggleStates = BuildOverlayEntryIsToggles("Level");

        Assert.Equal(AkronFeatureKind.Freeze, levelKinds["Freeze Gameplay"]);
        Assert.Equal(AkronFeatureKind.FrameAdvance, globalKinds["Frame Stepper"]);
        Assert.True(levelToggleStates["Freeze Gameplay"]);
        Assert.True(globalToggleStates["Frame Stepper"]);
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
        int frameStepperIndex = loadedLabels.IndexOf("Frame Stepper");
        Assert.Equal("FPS Bypass", loadedLabels[frameStepperIndex + 1]);
        Assert.Equal("TPS Bypass", loadedLabels[frameStepperIndex + 2]);
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

    [Fact]
    public void TransientResetClosesCommunityPackBrowser() {
        AkronOverlay overlay = CreateOverlayForStateTest();
        FieldInfo browserOpen = typeof(AkronOverlay).GetField("communityPackBrowserOpen", BindingFlags.Static | BindingFlags.NonPublic)!;
        FieldInfo expandedPreview = typeof(AkronOverlay).GetField("expandedCommunityPackPreview", BindingFlags.Instance | BindingFlags.NonPublic)!;
        browserOpen.SetValue(null, true);
        expandedPreview.SetValue(overlay, new AkronCommunityPackEntry());

        try {
            overlay.ResetTransientUiState(searchAutofocus: false);

            Assert.False((bool) browserOpen.GetValue(null)!);
            Assert.Null(expandedPreview.GetValue(overlay));
        } finally {
            browserOpen.SetValue(null, false);
            expandedPreview.SetValue(overlay, null);
        }
    }

    [Fact]
    public void OverlayCollapseStateUsesSettingsPersistence() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/AkronOverlay.cs"));

        Assert.Contains("ApplyPersistedWindowCollapseState();", source);
        Assert.Contains("AkronModule.TryGetSettings()?.CollapsedOverlaySections", source);
        Assert.Contains("PersistWindowCollapseState();", source);
        Assert.Contains("SaveAkronSettingsNow(\"overlay-collapse\")", source);
        Assert.DoesNotContain("CollapseExternalToolWindowsByDefault", source);
    }

    [Theory]
    [InlineData(true, false, false, false, false, AkronOverlay.OverlayCancelAction.CloseCommunityPackPreview)]
    [InlineData(false, true, false, false, false, AkronOverlay.OverlayCancelAction.ClearSearch)]
    [InlineData(false, false, true, false, false, AkronOverlay.OverlayCancelAction.ClearSearch)]
    [InlineData(false, false, false, true, false, AkronOverlay.OverlayCancelAction.CloseOptionsPopup)]
    [InlineData(false, false, false, false, true, AkronOverlay.OverlayCancelAction.CloseCommunityPackBrowser)]
    [InlineData(false, false, false, false, false, AkronOverlay.OverlayCancelAction.KeepOverlayOpen)]
    public void CancelInputDoesNotCloseBaseOverlay(bool previewOpen, bool searchInputActive, bool hasSearchQuery, bool optionsPopupOpen, bool communityPackBrowserOpen, AkronOverlay.OverlayCancelAction expected) {
        Assert.Equal(expected, AkronOverlay.ResolveCancelAction(previewOpen, searchInputActive, hasSearchQuery, optionsPopupOpen, communityPackBrowserOpen));
    }

    [Fact]
    public void OptionsPopupBlocksBackgroundActionRows() {
        string overlay = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/AkronOverlay.cs"));
        string optionsPopup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-options-popup.cs"));
        string renderer = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-imgui-renderer.cs"));

        Assert.Contains("private bool IsAnyOptionsPopupOpen()", optionsPopup);
        Assert.Contains("IsAnyOptionsPopupOpen() || IsBackgroundActionRowsSuppressedAfterPopupClose()", overlay);
        Assert.Contains("SuppressBackgroundActionRowsUntilMouseMoves();", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-imgui-popups.cs")));
        Assert.Contains("IsBackgroundActionRowsSuppressedAfterPopupClose()", renderer);
        Assert.Contains("ImGui.BeginDisabled();", renderer);
        Assert.Contains("!backgroundRowInputBlocked", renderer);
    }

    [Theory]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, true, true, false, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(false, false, false, false, false)]
    public void OptionsPopupClosesOnlyForClicksOutsideItsPopupTree(
        bool openedThisFrame,
        bool mouseClicked,
        bool parentHovered,
        bool nestedPopupOpen,
        bool expected) {
        Assert.Equal(
            expected,
            AkronOverlay.ShouldCloseOptionsPopupOnMouseClick(openedThisFrame, mouseClicked, parentHovered, nestedPopupOpen));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, true)]
    public void DeathOnlyHitboxModeSuppressesLiveHitboxes(
        bool liveViewer,
        bool deathHitboxVisible,
        bool showAllOnDeath,
        bool expected) {
        Assert.Equal(
            expected,
            AkronEntityInspector.ShouldRenderLiveHitboxes(liveViewer, deathHitboxVisible, showAllOnDeath));
    }

    [Fact]
    public void DeathOnlyHitboxModeRequiresARecordedObjectHitbox()
    {
        AkronModuleSession session = new AkronModuleSession {
            LastDeathHitboxVisible = true,
            LastDeathPosition = new Vector2(10, 20)
        };

        Assert.False(AkronEntityInspector.HasVisibleLastDeathObjectHitbox(session));

        session.LastDeathHitbox = new Rectangle(8, 18, 4, 4);
        Assert.True(AkronEntityInspector.HasVisibleLastDeathObjectHitbox(session));
    }

    [Fact]
    public void DeathHazardColliderIsCapturedLazilyBeforeNativeDeath()
    {
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../Source/Module/akron-module-player-runtime.cs"));
        int colliderHook = source.IndexOf("private static bool PlayerColliderOnCheckForDeathHazard(", StringComparison.Ordinal);
        int matcher = source.IndexOf("internal static PlayerColliderDeathHazardContext? MatchPlayerColliderDeathHazard(", colliderHook, StringComparison.Ordinal);
        int suppressionCheck = source.IndexOf("if (ShouldSuppressNormalDeathForAkronInvincibility(", StringComparison.Ordinal);
        int suppressedReturn = source.IndexOf("return null;", suppressionCheck, StringComparison.Ordinal);
        int nativeAssistSetup = source.IndexOf("EnsureNativeAssistInvincibility();", suppressedReturn, StringComparison.Ordinal);
        int captureDecision = source.IndexOf("ShouldCaptureDeathHazard(", nativeAssistSetup, StringComparison.Ordinal);
        int capture = source.IndexOf("CaptureDeathHazard(\n                    activeHazard.Value.Entity,", captureDecision, StringComparison.Ordinal);
        int nativeDeath = source.IndexOf("PlayerDeadBody deadBody = orig(", StringComparison.Ordinal);
        int record = source.IndexOf("RecordLastDeath(level, deathPosition, deathHazard);", StringComparison.Ordinal);

        Assert.True(colliderHook >= 0);
        Assert.True(matcher > colliderHook);
        string hookBody = source.Substring(colliderHook, matcher - colliderHook);
        Assert.DoesNotContain("CaptureDeathHazard(", hookBody);
        Assert.DoesNotContain("self.OnCollide =", hookBody);
        Assert.True(suppressionCheck >= 0);
        Assert.True(suppressedReturn > suppressionCheck);
        Assert.True(nativeAssistSetup > suppressedReturn);
        Assert.True(captureDecision > nativeAssistSetup);
        Assert.True(capture > captureDecision);
        Assert.True(nativeDeath > capture);
        Assert.True(record > nativeDeath);
    }

    [Fact]
    public void AutoKillAreaCanRenderWithDeathHitboxesWhenLiveAreaDisplayIsOff() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Hud/akron-hud-world-overlay-renderer.cs"));

        Assert.Contains("settings.AutoKillShowAreaOnDeath", source);
        Assert.Contains("AkronModule.Session?.LastDeathHitbox is Rectangle deathArea", source);
        Assert.Contains("DrawWorldRect(level, deathArea", source);
    }

    [Fact]
    public void DeathObjectRenderingKeepsTheRecordedColliderShape() {
        int spinnerLeft = 94;
        const int spinnerTop = 44;
        Rectangle sampleBounds = new Rectangle {
            X = 93,
            Y = 43,
            Width = 14,
            Height = 14
        };
        AkronEntityInspector.DeathColliderSnapshot snapshot =
            AkronEntityInspector.CaptureDeathColliderSnapshot(
                sampleBounds,
                (x, y) =>
                    x >= spinnerLeft &&
                    x < spinnerLeft + 12 &&
                    y >= spinnerTop &&
                    y < spinnerTop + 12);
        Rectangle recordedBounds = snapshot.Bounds;
        int recordedCenterX = recordedBounds.X + recordedBounds.Width / 2;
        int recordedCenterY = recordedBounds.Y + recordedBounds.Height / 2;

        spinnerLeft += 20;

        Assert.False(recordedCenterX >= spinnerLeft && recordedCenterX < spinnerLeft + 12);
        Assert.Equal(93, recordedBounds.X);
        Assert.Equal(43, recordedBounds.Y);
        Assert.Equal(14, recordedBounds.Width);
        Assert.Equal(14, recordedBounds.Height);
        Assert.Equal(recordedBounds.X, snapshot.Bounds.X);
        Assert.Equal(recordedBounds.Y, snapshot.Bounds.Y);
        Assert.Equal(recordedBounds.Width, snapshot.Bounds.Width);
        Assert.Equal(recordedBounds.Height, snapshot.Bounds.Height);
        Assert.True(snapshot.ContainsPixel(recordedCenterX, recordedCenterY));
        Assert.False(snapshot.ContainsPixel(recordedCenterX + 20, recordedCenterY));
    }

    [Fact]
    public void DeathObjectRenderingKeepsOverlappingSpinnerColliderPartsSeparate() {
        AkronEntityInspector.DeathColliderSnapshot circle =
            AkronEntityInspector.CaptureDeathColliderSnapshot(
                new Rectangle {
                    X = 94,
                    Y = 44,
                    Width = 12,
                    Height = 12
                },
                (x, y) => {
                    float dx = x + 0.5f - 100f;
                    float dy = y + 0.5f - 50f;
                    return dx * dx + dy * dy <= 36f;
                });
        AkronEntityInspector.DeathColliderSnapshot bar =
            AkronEntityInspector.CaptureDeathColliderSnapshot(
                new Rectangle {
                    X = 92,
                    Y = 47,
                    Width = 16,
                    Height = 4
                },
                (_, _) => true);

        AkronEntityInspector.DeathColliderSnapshot spinner =
            AkronEntityInspector.CombineDeathColliderSnapshots(new[] { circle, bar });

        Assert.Equal(2, spinner.PartCount);
        Assert.True(spinner.PartContainsPixel(0, 100, 44));
        Assert.False(spinner.PartContainsPixel(1, 100, 44));
        Assert.True(spinner.PartContainsPixel(0, 100, 48));
        Assert.True(spinner.PartContainsPixel(1, 100, 48));
    }

    [Fact]
    public void DeathObjectRenderingKeepsNativeSpinnerGeometrySeparateFromPixelFixes() {
        AkronEntityInspector.DeathColliderSnapshot circle =
            AkronEntityInspector.CaptureDeathColliderSnapshot(
                new Rectangle { X = 94, Y = 44, Width = 12, Height = 12 },
                (x, y) => {
                    float dx = x + 0.5f - 100f;
                    float dy = y + 0.5f - 50f;
                    return dx * dx + dy * dy <= 36f;
                },
                new Rectangle { X = 93, Y = 43, Width = 14, Height = 14 },
                (x, y) => x >= 93 && x < 107 && y >= 43 && y < 57);
        AkronEntityInspector.DeathColliderSnapshot bar =
            AkronEntityInspector.CaptureDeathColliderSnapshot(
                new Rectangle { X = 92, Y = 48, Width = 16, Height = 4 },
                (_, _) => true,
                new Rectangle { X = 91, Y = 47, Width = 18, Height = 6 },
                (_, _) => true);
        AkronEntityInspector.DeathColliderSnapshot snapshot =
            AkronEntityInspector.CombineDeathColliderSnapshots(new[] { circle, bar });

        AssertRectangleBounds(
            snapshot.PartBounds(0, fixHitboxPixels: false),
            94, 44, 12, 12);
        AssertRectangleBounds(
            snapshot.PartBounds(0, fixHitboxPixels: true),
            93, 43, 14, 14);
        AssertRectangleBounds(
            snapshot.PartBounds(1, fixHitboxPixels: false),
            92, 48, 16, 4);
        AssertRectangleBounds(
            snapshot.PartBounds(1, fixHitboxPixels: true),
            91, 47, 18, 6);
    }

    [Fact]
    public void MultipartDeathColliderSnapshotCountsBothDistinctGeometryBuffers() {
        AkronEntityInspector.DeathColliderSnapshot first =
            AkronEntityInspector.CaptureDeathColliderSnapshot(
                new Rectangle { X = 0, Y = 0, Width = 128, Height = 256 },
                (_, _) => false,
                new Rectangle { X = 0, Y = 0, Width = 128, Height = 256 },
                (_, _) => false);
        AkronEntityInspector.DeathColliderSnapshot second =
            AkronEntityInspector.CaptureDeathColliderSnapshot(
                new Rectangle { X = 128, Y = 0, Width = 128, Height = 256 },
                (_, _) => false,
                new Rectangle { X = 128, Y = 0, Width = 128, Height = 256 },
                (_, _) => false);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(AkronEntityInspector.CombineDeathColliderSnapshots(
            new[] { first, second }));
    }

    [Fact]
    public void SingleDeathColliderSnapshotRejectsCombinedGeometryOverBudgetBeforeSampling() {
        int sampledPixels = 0;

        AkronEntityInspector.DeathColliderSnapshot snapshot =
            AkronEntityInspector.CaptureDeathColliderSnapshot(
                new Rectangle { X = 0, Y = 0, Width = 200, Height = 200 },
                (_, _) => {
                    sampledPixels++;
                    return false;
                },
                new Rectangle { X = 0, Y = 0, Width = 202, Height = 202 },
                (_, _) => {
                    sampledPixels++;
                    return false;
                });

        Assert.Null(snapshot);
        Assert.Equal(0, sampledPixels);
    }

    private static void AssertRectangleBounds(
        Rectangle rectangle,
        int x,
        int y,
        int width,
        int height) {
        Assert.Equal(x, rectangle.X);
        Assert.Equal(y, rectangle.Y);
        Assert.Equal(width, rectangle.Width);
        Assert.Equal(height, rectangle.Height);
    }

    [Fact]
    public void MultipartDeathColliderSnapshotKeepsTheAggregatePixelBudget() {
        int snapshotsRead = 0;
        AkronEntityInspector.DeathColliderSnapshot first =
            AkronEntityInspector.CaptureDeathColliderSnapshot(
                new Rectangle {
                    X = 0,
                    Y = 0,
                    Width = 256,
                    Height = 256
                },
                (_, _) => false);
        AkronEntityInspector.DeathColliderSnapshot second =
            AkronEntityInspector.CaptureDeathColliderSnapshot(
                new Rectangle {
                    X = 256,
                    Y = 0,
                    Width = 256,
                    Height = 256
                },
                (_, _) => false);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(AkronEntityInspector.CombineDeathColliderSnapshots(Snapshots()));
        Assert.Equal(2, snapshotsRead);

        IEnumerable<AkronEntityInspector.DeathColliderSnapshot> Snapshots() {
            snapshotsRead++;
            yield return first;
            snapshotsRead++;
            yield return second;
            snapshotsRead++;
            yield return first;
        }
    }

    [Fact]
    public void MultipartDeathColliderSnapshotRejectsMissingParts() {
        AkronEntityInspector.DeathColliderSnapshot capturedPart =
            AkronEntityInspector.CaptureDeathColliderSnapshot(
                new Rectangle {
                    X = 0,
                    Y = 0,
                    Width = 1,
                    Height = 1
                },
                (_, _) => true);

        Assert.Null(AkronEntityInspector.CombineDeathColliderSnapshots(
            new[] { capturedPart, null }));
    }

    [Fact]
    public void MultipartDeathColliderSnapshotSkipsNullEntriesBeforeCapture() {
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../Source/Tools/akron-entity-inspector.cs"));

        Assert.Contains(".Where(child => child != null)", source);
        Assert.Contains(".Select(CaptureDeathColliderSnapshot)", source);
    }

    [Fact]
    public void DeathColliderSnapshotDoesNotCrossModuleSessionRestores() {
        AkronModuleSession capturedSession = new AkronModuleSession();
        AkronModuleSession restoredSession = new AkronModuleSession();
        Rectangle bounds = new Rectangle {
            X = 10,
            Y = 20,
            Width = 2,
            Height = 2
        };
        AkronEntityInspector.DeathColliderSnapshot snapshot =
            AkronEntityInspector.CaptureDeathColliderSnapshot(
                bounds,
                (_, _) => true);

        Assert.NotNull(snapshot);
        Assert.True(AkronEntityInspector.CanRenderDeathColliderSnapshot(
            snapshot,
            capturedSession,
            capturedSession));
        Assert.False(AkronEntityInspector.CanRenderDeathColliderSnapshot(
            snapshot,
            capturedSession,
            restoredSession));
    }

    [Fact]
    public void OversizedDeathCollidersFallBackWithoutRasterizing() {
        int sampledPixels = 0;

        AkronEntityInspector.DeathColliderSnapshot snapshot =
            AkronEntityInspector.CaptureDeathColliderSnapshot(
                new Rectangle {
                    Width = 1024,
                    Height = 1024
                },
                (_, _) => {
                    sampledPixels++;
                    return true;
                });

        Assert.Null(snapshot);
        Assert.Equal(0, sampledPixels);
    }

    [Fact]
    public void TriggerViewerUsesFivePixelOutlines() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Hud/AkronHudRenderer.cs"));

        Assert.Contains("ColorFromRgb(AkronModule.Settings.HitboxTriggerColor), 0.08f, 5)", source);
    }

    [Fact]
    public void PopupLabelsUseAvailableWidthBeforeTruncating() {
        Assert.Equal(150f, AkronOverlay.CalculatePopupLabelWidth(540f, 240f, 8f, 150f));
        Assert.Equal(72f, AkronOverlay.CalculatePopupLabelWidth(320f, 240f, 8f, 150f));
    }

    [Fact]
    public void SessionOnlyRowsAreDisabledWithoutASession() {
        Assert.False(BuildOverlayEntryEnabled("Global", "Timescale", null)());
        Assert.False(BuildOverlayEntryEnabled("Global", "Frame Stepper", null)());
    }

    [Fact]
    public void RestoreBackupBrowserRemainsReachableWhenTheCacheIsEmpty() {
        Assert.True(BuildOverlayEntryEnabled("Backups", "Restore", null)());
    }

    [Fact]
    public void TimescaleNumericRowRetainsToggleState() {
        Assert.True(BuildOverlayEntryIsToggles("Global")["Timescale"]);
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
    public void CapturingStartPosKeepsCapturedSlotActive() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Actions/akron-startpos-actions.cs"));
        int methodStart = source.IndexOf("private static void CaptureStartPos", StringComparison.Ordinal);
        int nextMethod = source.IndexOf("private static void ApplyPlacedStartPosBeforeCapture", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(nextMethod > methodStart);
        string method = source[methodStart..nextMethod];

        Assert.Contains("AkronModule.Session.StartPositions[slot]", method);
        Assert.DoesNotContain("SetStartPosSlot", method);
        Assert.DoesNotContain("NextStartPosSlot", source);
    }

    [Fact]
    public void Issue18RowsStayInExistingGameplayTabs() {
        List<string> levelLabels = BuildOverlayEntryLabels("Level");
        List<string> playerLabels = BuildOverlayEntryLabels("Player");
        List<string> shortcutLabels = BuildOverlayEntryLabels("Shortcuts");

        Assert.Contains("Core Mode", levelLabels);
        Assert.Contains("Death Particles", playerLabels);
        Assert.Contains("Set Inventory", playerLabels);
        Assert.Contains("Dream State", playerLabels);
        Assert.Contains("Spawn Jelly", shortcutLabels);
        Assert.Contains("Spawn Theo", shortcutLabels);

        Assert.True(HasOverlayOptionsPopup("Core Mode"));
        Assert.True(HasOverlayOptionsPopup("Death Particles"));
        Assert.True(HasOverlayOptionsPopup("Set Inventory"));
        Assert.True(HasOverlayOptionsPopup("Dream State"));
        Assert.False(HasOverlayOptionsPopup("Spawn Jelly"));
        Assert.False(HasOverlayOptionsPopup("Spawn Theo"));
    }

    [Fact]
    public void DeathParticleCustomShapeEditorUsesDrawableCanvas() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-madeline-popups.cs"));
        int methodStart = source.IndexOf("private void DrawDeathParticleCanvas", StringComparison.Ordinal);
        int nextMethod = source.IndexOf("private void DrawCustomTrailPopupControls", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(nextMethod > methodStart);
        string method = source[methodStart..nextMethod];

        Assert.Contains("ImGui.InvisibleButton", method);
        Assert.Contains("AddRectFilled", method);
        Assert.Contains("ImGuiMouseButton.Left", method);
        Assert.Contains("ImGuiMouseButton.Right", method);
        Assert.DoesNotContain("ImGui.Checkbox", method);
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
    public void AutoKillConditionRadioGroupsUseDistinctImGuiIdScopes() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-automation-popups.cs"));
        int start = source.IndexOf("private void DrawAutoKillConditionControls", StringComparison.Ordinal);
        int end = source.IndexOf("private void DrawAutoDeafenPopupControls", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        string conditionControls = source[start..end];

        Assert.Contains("popupId + \"_ground\"", conditionControls);
        Assert.Contains("popupId + \"_horizontal\"", conditionControls);
        Assert.Contains("popupId + \"_vertical\"", conditionControls);
    }

    [Fact]
    public void ShowHitboxesPopupOmitsRedundantTrailAndBlackOutlineControls() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-runtime-popups.cs"));
        int start = source.IndexOf("private void DrawHitboxPopupControls", StringComparison.Ordinal);
        int end = source.IndexOf("private void DrawEntityInspectorPopupControls", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        string hitboxPopup = source[start..end];

        Assert.DoesNotContain("DrawHitboxTrailPopupControls", hitboxPopup);
        Assert.DoesNotContain("HitboxBlackOutline", hitboxPopup);
    }

    [Fact]
    public void DeloadCommandUsesOneShotSimulatorEntrypoint() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Commands/akron-utility-commands.cs"));

        Assert.Contains("AkronDeloadSimulator.TrySimulate", source);
        Assert.DoesNotContain("AkronDeloadSimulator.Simulate", source);
    }

    [Fact]
    public void DeloadOverlayRowDisablesAfterLevelIsClaimed() {
        Level level = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level));
        AkronDeloadSimulator.BeginLevel(level);
        Func<bool> enabled = BuildOverlayEntryEnabled("Level", "Deload Spinners", level);

        Assert.True(enabled());

        FieldInfo claimsField = typeof(AkronDeloadSimulator).GetField("Claims", BindingFlags.NonPublic | BindingFlags.Static)!;
        object claims = claimsField.GetValue(null)!;
        MethodInfo tryClaim = claims.GetType().GetMethod("TryClaim", BindingFlags.Public | BindingFlags.Instance)!;
        Assert.True((bool) tryClaim.Invoke(claims, new object[] { level })!);
        Assert.False(enabled());

        AkronDeloadSimulator.BeginLevel(level);
    }

    [Fact]
    public void ToggleEntriesUseExplicitActiveStateInsteadOfDisplayText() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-entries.cs"));

        Assert.Contains("active: getter", source);
        Assert.Contains("active: () => AkronModule.Settings.Invincibility", source);
        Assert.Contains("active: () => AkronModule.Settings.Logging", source);
        Assert.Contains("active: () => AkronEarAid.OverrideEnabled(key)", source);
        Assert.Contains("AkronActions.IsDreamStateActive", source);

        string imguiRenderer = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-imgui-renderer.cs"));
        string renderer = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-sprite-renderer.cs"));
        Assert.Contains("if (entry.IsToggle)", renderer);
        Assert.Contains("return selectedOrHovered ? AkronAccentHovered : AkronAccent;", renderer);
        Assert.Contains("entry.Active?.Invoke() ??", imguiRenderer);
        Assert.Contains("action.Entry.Active?.Invoke() ??", renderer);
        Assert.DoesNotContain("entry.Active?.Invoke() == true ||", imguiRenderer);
        Assert.DoesNotContain("action.Entry.Active?.Invoke() == true ||", renderer);
    }

    [Fact]
    public void DreamStateActiveStateReadsSessionInventory() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Actions/akron-actions.cs"));
        int describeStart = source.IndexOf("public static string DescribeDreamState", StringComparison.Ordinal);
        int activeStart = source.IndexOf("public static bool IsDreamStateActive", describeStart, StringComparison.Ordinal);
        int end = source.IndexOf("public static void ToggleDreamState", activeStart, StringComparison.Ordinal);

        Assert.True(describeStart >= 0);
        Assert.True(activeStart > describeStart);
        Assert.True(end > activeStart);
        string description = source[describeStart..activeStart];
        string activeState = source[activeStart..end];

        Assert.Contains("level.Session.Inventory.DreamDash", description);
        Assert.DoesNotContain("player.Inventory.DreamDash", description);
        Assert.Contains("level?.Session.Inventory.DreamDash == true", activeState);
        Assert.DoesNotContain("Tracker.GetEntity", activeState);
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
    public void GlobalOverlayToggleIsAlwaysSuppressedOnlyInEverestModToggler(string? ouiTypeName, bool expected) {
        Assert.Equal(expected, AkronModule.ShouldSuppressGlobalOverlayToggleForOuiType(ouiTypeName));
    }

    [Theory]
    [InlineData("Celeste.OuiChapterSelect", true)]
    [InlineData("Celeste.OuiJournal", true)]
    [InlineData("Celeste.OuiChapterPanel", false)]
    [InlineData("Celeste.OuiMainMenu", false)]
    [InlineData("Celeste.Mod.UI.OuiModOptions", false)]
    [InlineData(null, false)]
    public void PlainTabOverlayToggleGivesJournalPriorityInJournalOuiStates(string? ouiTypeName, bool expected) {
        Assert.Equal(expected, AkronModule.ShouldGiveJournalPriorityForOverlayToggle(
            ouiTypeName,
            new List<Keys> { Keys.Tab }));
    }

    [Fact]
    public void ModifiedTabOverlayToggleDoesNotGiveJournalPriority() {
        List<Keys> modifiedTab = new List<Keys> { Keys.Tab, Keys.LeftControl };

        Assert.False(AkronModule.ShouldGiveJournalPriorityForOverlayToggle("Celeste.OuiChapterSelect", modifiedTab));
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
    public void WorldProjectionRemovesLevelZoomAroundFocus() {
        Vector2 unzoomed = AkronScreenProjection.RemoveLevelZoom(TestVector(40f, 70f), 2f, TestVector(160f, 90f));

        Assert.Equal(100f, unzoomed.X);
        Assert.Equal(80f, unzoomed.Y);
    }

    [Fact]
    public void WorldProjectionRemovesZoomForCursorToolsTeleportNearClampedFocus() {
        Vector2 unzoomed = AkronScreenProjection.RemoveLevelZoom(TestVector(280f, 90f), 2f, TestVector(240f, 90f));
        Vector2 renderedAgain = AkronScreenProjection.ApplyLevelZoom(unzoomed, 2f, TestVector(240f, 90f));

        Assert.Equal(260f, unzoomed.X);
        Assert.Equal(90f, unzoomed.Y);
        Assert.Equal(280f, renderedAgain.X);
        Assert.Equal(90f, renderedAgain.Y);
    }

    [Fact]
    public void ClickTeleportUsesPlayerHairMoveHairByForTeleportAnimationState() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/akron-module-visual-runtime.cs"));
        int methodStart = source.IndexOf("internal static void MoveHairForTeleport", StringComparison.Ordinal);
        int nextMethod = source.IndexOf("private static void UpdateCursorZoom", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(nextMethod > methodStart);
        string method = source[methodStart..nextMethod];

        Assert.Contains("player.Hair.MoveHairBy(delta);", method);
        Assert.DoesNotContain("player.Hair.Nodes", method);
    }

    [Fact]
    public void ClickTeleportSamplesMouseTargetBeforeFreeCameraCanMoveTheCamera() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/akron-module-player-runtime.cs"));
        int methodStart = source.IndexOf("private static void ApplyEnabledRuntimeFeatures", StringComparison.Ordinal);
        int captureTarget = source.IndexOf("CaptureClickTeleportTargetBeforeCameraMovement(level, player);", methodStart, StringComparison.Ordinal);
        int applyRuntimeOptions = source.IndexOf("AkronRuntimeOptions.Apply(level, player);", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.InRange(captureTarget, methodStart, applyRuntimeOptions - 1);
    }

    [Fact]
    public void ClickTeleportTargetUsesCurrentCursorZoomFocusBeforeRuntimeCameraMovement() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/akron-module-visual-runtime.cs"));
        int methodStart = source.IndexOf("internal static Vector2 MouseScreenToWorldForClickTeleport", StringComparison.Ordinal);
        int nextMethod = source.IndexOf("private static float CurrentClickTeleportLevelZoom", methodStart, StringComparison.Ordinal);
        int focusMethodStart = source.IndexOf("private static Vector2 CurrentClickTeleportZoomFocus", StringComparison.Ordinal);
        int moveHairMethodStart = source.IndexOf("internal static void MoveHairForTeleport", focusMethodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(nextMethod > methodStart);
        Assert.Contains("CurrentClickTeleportZoomFocus(level, mouseGamePosition, zoom)", source[methodStart..nextMethod]);
        Assert.Contains("ClampCursorZoomFocus(mouseGamePosition, zoom)", source[focusMethodStart..moveHairMethodStart]);
    }

    [Fact]
    public void FreeCameraDoesNotMoveOnTheClickTeleportFrame() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Runtime/akron-runtime-options.cs"));
        int methodStart = source.IndexOf("private static void ApplyFreeCamera", StringComparison.Ordinal);
        int suppressMovement = source.IndexOf("ShouldSuppressFreeCameraMovementForClickTeleport", methodStart, StringComparison.Ordinal);
        int readAim = source.IndexOf("Vector2 aim = Input.Aim.Value;", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.InRange(suppressMovement, methodStart, readAim - 1);
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
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-imgui-popups.cs"));

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
    public void AkronOverlayStaysOnFinalRenderPassWhileWorldDebugGeometryUsesSplitPasses() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/AkronModule.cs"));

        Assert.DoesNotContain("On.Celeste.Level.Render += LevelOnRender", source);
        Assert.DoesNotContain("private static void LevelOnRender(", source);
        Assert.Contains("On.Monocle.Engine.RenderCore += EngineOnRenderCore", source);
        Assert.Contains("RenderAkronLevelHud(postRenderLevel);", source);
        Assert.DoesNotContain("AkronEntityInspector.RenderHitboxesToHud(level", source);
        Assert.Contains("bool overlayImGuiFrameRequested = overlayVisible || Overlay?.NeedsImGuiFrame == true;", source);
        Assert.Contains("if (overlayVisible && !overlayImGuiRendered)", source);
        Assert.Contains("On.Celeste.GameplayRenderer.Render += GameplayRendererOnRender", source);
        Assert.Contains("ShouldHideAkronRenderSurfaces()", source);
        Assert.Contains("!ShouldRenderGameplayDebugPass(level)", source);
        Assert.Contains("AkronHudRenderer.RenderAutomationAreasToGameplayBuffer(level);", source);
        Assert.Contains("AkronEntityInspector.RenderHitboxesToGameplayBuffer(level", source);
        Assert.Contains("AkronEntityInspector.ShouldRenderInspectorPinImGui(inspectorPinLevel)", source);
        Assert.Contains("AkronEntityInspector.RenderInspectorPinImGui(inspectorPinLevel);", source);
        Assert.DoesNotContain("RenderInspectorPinHud", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-entity-inspector-pin.cs")));
        Assert.DoesNotContain("RenderAkronHitboxHud(postRenderLevel);", source);
        Assert.DoesNotContain("RenderAutomationDeathAreasToGameplayBuffer", source);
    }

    [Fact]
    public void DeathWipeDrawsHitboxesImmediatelyBeforeWipePrimitives() {
        string moduleSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/AkronModule.cs"));
        string wipeSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Runtime/akron-death-wipe-rendering.cs"));
        int helperStart = moduleSource.IndexOf("private static void RenderAkronHitboxesUnderDeathWipe", StringComparison.Ordinal);
        int helperEnd = moduleSource.IndexOf("private static void GameplayRendererOnRender", helperStart, StringComparison.Ordinal);
        int predicateStart = moduleSource.IndexOf("private static bool ShouldRenderAkronHitboxes", StringComparison.Ordinal);
        int drawStart = wipeSource.IndexOf("private static void ScreenWipeOnDrawPrimitives", StringComparison.Ordinal);
        int captureGuard = wipeSource.IndexOf("!AkronCapture.IsCapturingGameFrame", drawStart, StringComparison.Ordinal);
        int visibilityGuard = wipeSource.IndexOf("ShouldRenderAkronHitboxes()", captureGuard, StringComparison.Ordinal);
        int hitboxDraw = wipeSource.IndexOf("RenderAkronHitboxesUnderDeathWipe(level);", drawStart, StringComparison.Ordinal);
        int wipeDraw = wipeSource.IndexOf("orig(vertices);", drawStart, StringComparison.Ordinal);

        Assert.True(helperStart >= 0);
        Assert.True(helperEnd > helperStart);
        Assert.True(predicateStart >= 0);
        Assert.True(drawStart >= 0);
        Assert.True(captureGuard > drawStart);
        Assert.True(visibilityGuard > captureGuard);
        Assert.True(hitboxDraw > visibilityGuard);
        Assert.True(wipeDraw > hitboxDraw);
        string helper = moduleSource[helperStart..helperEnd];
        Assert.Contains("Viewport viewport = Engine.Viewport;", helper);
        Assert.Contains("Matrix.CreateTranslation(-viewport.X, -viewport.Y, 0f)", helper);
        Assert.Contains("AkronEntityInspector.RenderHitboxes(level, level.Tracker.GetEntity<Player>());", helper);
    }

    [Fact]
    public void Issue72OverlayRenderDoesNotRefilterRowsDuringExternalToolPlacement() {
        string layoutSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-layout.cs"));
        string rendererSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-imgui-renderer.cs"));
        string planMethod = ExtractMethod(layoutSource, "private ExternalToolPlacementPlan BuildExternalToolPlacementPlan");
        string drawMethod = ExtractMethod(rendererSource, "private void DrawImGuiMenu()");

        Assert.Contains("BuildVisibleTabActionEntries(visibleTabs, level)", drawMethod);
        Assert.Contains("BuildExternalToolPlacementPlan(visibleTabs, actionEntriesByTab", drawMethod);
        Assert.DoesNotContain("GetFilteredDisplayActionEntries", planMethod);
    }

    [Fact]
    public void ImGuiProgressBarsUseAkronThemeAccent() {
        string rendererSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-imgui-renderer.cs"));
        string overlayRendererSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-imgui-renderer.cs"));

        Assert.Contains("style.Colors[(int) ImGuiCol.PlotHistogram] = AkronImGuiTheme.Accent;", rendererSource);
        Assert.Contains("style.Colors[(int) ImGuiCol.PlotHistogramHovered] = AkronImGuiTheme.AccentHovered;", rendererSource);
        Assert.Contains("style.Colors[(int) ImGuiCol.PlotHistogram] = AkronImGuiTheme.Accent;", overlayRendererSource);
        Assert.Contains("style.Colors[(int) ImGuiCol.PlotHistogramHovered] = AkronImGuiTheme.AccentHovered;", overlayRendererSource);
    }

    [Fact]
    public void InspectorPinPopupUsesAkronImGuiPanelTheme() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-entity-inspector-pin.cs"));

        Assert.Contains("AkronOverlay.ApplyOverlayThemePreset(scale);", source);
        Assert.Contains("AkronModuleSettings.ClampOverlayOpacity(AkronModule.Settings.OverlayOpacity)", source);
        Assert.Contains("ImGui.Begin(\"Entity Inspector \" + cycle + \"##akron_inspector_pin\", flags)", source);
        Assert.Contains("DrawInspectorPinInfoRow(\"Target\", data.Filter.ToString())", source);
        Assert.DoesNotContain("DrawInspectorPinFilterRadios", source);
        Assert.DoesNotContain("akron_inspector_filter_entities", source);
        Assert.Contains("SaveInspectorPinWindowPosition", source);
        Assert.Contains("!inspectorPinPositionInitialized || IsFixedInspectorPinPlacement(placement)", source);
        Assert.Contains("private static bool IsFixedInspectorPinPlacement", source);
        Assert.Contains("inspectorPinPositionInitialized = false;", source);
        Assert.Contains("collapsedWindowPos = ImGui.GetWindowPos()", source);
        Assert.Contains("collapsedWindowSize = ImGui.GetWindowSize()", source);
        Assert.Contains("ClampInspectorPinImGuiPosition(displaySize, windowSize, windowPos)", source);
        Assert.Contains("ClampInspectorPinImGuiPosition(displaySize, collapsedWindowSize, collapsedWindowPos)", source);
        Assert.Contains("inspectorPinCardRect = ToRectangle(", source);
        Assert.DoesNotContain("ImGuiWindowFlags.NoMove", source);
        Assert.Contains("DrawInspectorPinInfoRow(\"Type\", FormatCompactType(data))", source);
        Assert.Contains("DrawInspectorPinInfoRow(\"Position\", FormatCompactPosition(data))", source);
        Assert.Contains("DrawInspectorPinInfoRow(\"Size\", compactSize)", source);
        Assert.Contains("DrawInspectorPinRows(data.RuntimeRows)", source);
        Assert.Contains("DrawInspectorPinRows(data.PlacementRows)", source);
        Assert.Contains("DrawInspectorPinRows(data.AuthoredRows)", source);
        Assert.Contains("\"Copy\"", source);
        Assert.Contains("\"Close##akron_inspector_pin_close\"", source);
        Assert.Contains("ClearInspectorPinSelection();", source);
        Assert.Contains("CopyInspectorReport(BuildVisibleCopyReport(data, inspectorPinPropertiesOpen))", source);
        Assert.Contains("internal static string BuildVisibleCopyReport(AkronInspectorReportData data, bool includeProperties)", source);
        Assert.Contains("builder.AppendLine(\"type: \" + FormatCompactType(data))", source);
        Assert.Contains("builder.AppendLine(\"position: \" + FormatCompactPosition(data))", source);
        Assert.DoesNotContain("Copy Report", source);
        Assert.DoesNotContain("more in copy report", source);
        Assert.DoesNotContain("ImGuiWindowFlags.NoTitleBar", source);
        Assert.DoesNotContain("DrawInspectorPinActionButton", source);
        Assert.DoesNotContain("ImGui.PushStyleColor(ImGuiCol.Button", source);
        Assert.DoesNotContain("AddRectFilled", source);
    }

    [Fact]
    public void EntityInspectorRowUsesSettingForActiveColorAndPopupBindings() {
        string entriesSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-entries.cs"));
        string imguiRendererSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-imgui-renderer.cs"));
        string spriteRendererSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-sprite-renderer.cs"));
        string popupSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-runtime-popups.cs"));
        string moduleInputSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/akron-module-overlay-input.cs"));
        string inspectorSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-entity-inspector-pin.cs"));
        int popupStart = popupSource.IndexOf("private void DrawEntityInspectorPopupControls", StringComparison.Ordinal);
        int popupEnd = popupSource.IndexOf("private void DrawCursorHoldBindingRow", popupStart, StringComparison.Ordinal);

        Assert.True(popupStart >= 0);
        Assert.True(popupEnd > popupStart);
        string entityInspectorPopup = popupSource[popupStart..popupEnd];

        Assert.Contains("active: () => AkronModule.Settings.EntityInspector", entriesSource);
        Assert.DoesNotContain("AkronModule.ArmEntityInspectorPickMode();", entriesSource);
        Assert.DoesNotContain("AkronModule.SetOverlayVisible(Engine.Scene, false);", entriesSource);
        Assert.Contains("entry.Active?.Invoke() ??", imguiRendererSource);
        Assert.Contains("action.Entry.Active?.Invoke() ??", spriteRendererSource);
        Assert.Contains("DrawPopupRowLabel(\"Target\"", entityInspectorPopup);
        Assert.Contains("DrawPopupChoiceRadioButton(", entityInspectorPopup);
        Assert.Contains("\"Entities\"", entityInspectorPopup);
        Assert.Contains("\"Triggers\"", entityInspectorPopup);
        Assert.Contains("\"Both\"", entityInspectorPopup);
        Assert.Contains("AkronModule.Settings.EntityInspectorCursorHold", entityInspectorPopup);
        Assert.Contains("AkronModule.Settings.EntityInspectorPinHoverPreview", entityInspectorPopup);
        Assert.Contains("\"Hover preview\"", entityInspectorPopup);
        Assert.Contains("DrawPopupRowLabel(\"Cursor\"", popupSource);
        Assert.Contains("DrawEntityInspectorReportPlacementRows(popupId)", entityInspectorPopup);
        Assert.Contains("EntityInspectorPinPlacement", popupSource);
        Assert.Contains("EntityInspectorPinShowPropertiesByDefault", popupSource);
        Assert.Contains("\"Near click\"", popupSource);
        Assert.Contains("\"Top left\"", popupSource);
        Assert.Contains("\"Top right\"", popupSource);
        Assert.Contains("\"Bottom left\"", popupSource);
        Assert.Contains("\"Bottom right\"", popupSource);
        Assert.Contains("\"Custom\"", popupSource);
        Assert.Contains("\"Expanded\"", popupSource);
        Assert.Contains("\"Collapsed\"", popupSource);
        Assert.DoesNotContain("Enabled##", entityInspectorPopup);
        Assert.DoesNotContain("DrawEntityInspectorTargetButton", popupSource);
        Assert.DoesNotContain("AkronModule.Settings.CursorToolsHold", entityInspectorPopup);
        Assert.DoesNotContain("AkronModule.Settings.CursorZoomHold", entityInspectorPopup);
        Assert.DoesNotContain("AkronModule.Settings.ClickTeleportCursor", entityInspectorPopup);
        Assert.Contains("\"Entity Inspector / Cursor hold\"", entityInspectorPopup);
        Assert.Contains("StartButtonBindingCapture(actionKey, displayName, setter, clearMenuBinding: false)", popupSource);
        Assert.DoesNotContain("entityInspectorPickModeArmed", moduleInputSource);
        Assert.DoesNotContain("ArmEntityInspectorPickMode", moduleInputSource);
        Assert.DoesNotContain("ClearEntityInspectorPickMode", moduleInputSource);
        Assert.Contains("IsEntityInspectorCursorHoldActive() || IsCursorToolsInspectorPinActive()", moduleInputSource);
        Assert.Contains("IsEntityInspectorCursorHoldActive()", moduleInputSource);
        Assert.Contains("IsButtonBindingHeld(AkronModuleSettings.ResolveEntityInspectorCursorHoldBinding(Settings))", moduleInputSource);
        Assert.Contains("ShouldShowEntityInspectorCursor()", moduleInputSource);
        Assert.Contains("AkronPolicy.CanUse(AkronFeatureKind.EntityInspector).Allowed", inspectorSource);
        int updateStart = inspectorSource.IndexOf("public static void UpdateInspectorPin", StringComparison.Ordinal);
        int previewStart = inspectorSource.IndexOf("private static void UpdateInspectorPinHoverPreview", StringComparison.Ordinal);
        Assert.True(updateStart >= 0);
        Assert.True(previewStart > updateStart);
        Assert.Contains("!AkronModule.ShouldShowEntityInspectorCursor()", inspectorSource[updateStart..previewStart]);
        Assert.Contains("InspectorPinProbeRadiusPixels", inspectorSource);
        Assert.Contains("TryGetInspectorHitBounds", inspectorSource);
        Assert.Contains("TryGetSolidTileProbeBounds", inspectorSource);
        Assert.Contains("record.SourceData.Width > 0", inspectorSource);
        Assert.Contains("MouseScreenToWorld(level, screenPoint, clampToViewport: false)", inspectorSource);
        Assert.Contains("ColliderBacked", inspectorSource);
        Assert.Contains("EnumerateInspectorEntities", inspectorSource);
        Assert.Contains("entities.Add(level.SolidTiles)", inspectorSource);
        Assert.Contains("UpdateInspectorPinHoverPreview", inspectorSource);
        Assert.Contains("previewStack", inspectorSource);
        Assert.Contains("ClearInspectorPinPreview();", inspectorSource);
        Assert.Contains("if (sameStack && inspectorPinSelectedIndex + 1 >= currentStack.Count)", inspectorSource);
        Assert.Contains("return \"cleared\";", inspectorSource);
        Assert.Contains("renderingToGameplayBuffer = true;", inspectorSource);
        Assert.Contains("renderingToGameplayBuffer = false;", inspectorSource);
        Assert.Contains("DrawCollider(level, hit.Entity.Collider, color, cameraBounds);", inspectorSource);
        Assert.DoesNotContain("DrawInspectorWorldRect(level, hit.Bounds, color, selected ? fillColor : Color.Transparent, dashed: false);\n                DrawCollider(level, hit.Entity.Collider, color, cameraBounds);", inspectorSource);
        Assert.Contains("private const float InspectorHighlightOutlineThickness", inspectorSource);
        Assert.Contains("Color.Black * 0.9f", inspectorSource);
        Assert.Contains("if (hit.Entity is SolidTiles)", inspectorSource);
        Assert.DoesNotContain("DrawWorldRect(level, hit.Bounds, color);", inspectorSource);
        Assert.Contains("HasInspectorPinPreview()", inspectorSource);
        Assert.DoesNotContain("entity is PlayerDeadBody || entity is SolidTiles", inspectorSource);
        int ignoreStart = inspectorSource.IndexOf("private static bool ShouldIgnoreInspectorPinGameplayClick", StringComparison.Ordinal);
        int ignoreEnd = inspectorSource.IndexOf("private static bool IsInsideGameplayViewport", ignoreStart, StringComparison.Ordinal);
        Assert.True(ignoreStart >= 0);
        Assert.True(ignoreEnd > ignoreStart);
        Assert.DoesNotContain("WantCaptureMouse", inspectorSource[ignoreStart..ignoreEnd]);
    }

    [Fact]
    public void CursorFeaturePopupsExposeCustomCursorBindings() {
        string popupRouterSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-popup-controls.cs"));
        string optionsSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-options-popup.cs"));
        string visualPopupSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-visual-popups.cs"));
        string runtimePopupSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-runtime-popups.cs"));

        Assert.Contains("string.Equals(entry.Label, \"Click Teleport\"", popupRouterSource);
        Assert.Contains("string.Equals(label, \"Click Teleport\"", optionsSource);

        Assert.Contains("DrawClickTeleportPopupControls", visualPopupSource);
        Assert.Contains("AkronModule.Settings.ClickTeleportCursor", visualPopupSource);
        Assert.Contains("StartButtonBindingCapture(actionKey, displayName, setter, clearMenuBinding: false)", runtimePopupSource);
        Assert.Contains("Bind##\" + idPrefix + \"-cursor-bind-\"", runtimePopupSource);
        Assert.Contains("Clear##\" + idPrefix + \"-cursor-clear-\"", runtimePopupSource);
        Assert.Contains("Default##\" + idPrefix + \"-cursor-default-\"", runtimePopupSource);
        Assert.Contains("\"Click Teleport / Cursor hold\"", visualPopupSource);

        Assert.Contains("DrawCursorToolsPopupControls", visualPopupSource);
        Assert.Contains("AkronModule.Settings.CursorToolsHold", visualPopupSource);
        Assert.Contains("\"Cursor Tools / Cursor hold\"", visualPopupSource);
        Assert.Contains("AkronModule.Settings.CursorToolsClickAction", visualPopupSource);
        Assert.Contains("AkronCursorToolsClickAction.ClickTeleport", visualPopupSource);
        Assert.Contains("AkronCursorToolsClickAction.InspectorPin", visualPopupSource);
        Assert.Contains("\"Click action\"", visualPopupSource);
        Assert.Contains("\"Entity Inspector\"", visualPopupSource);

        Assert.Contains("DrawCursorZoomPopupControls", visualPopupSource);
        Assert.Contains("AkronModule.Settings.CursorZoomHold", visualPopupSource);
        Assert.Contains("\"Cursor Zoom / Cursor hold\"", visualPopupSource);
        Assert.Contains("AkronModuleSettings.CreateLeftAltHoldBinding()", visualPopupSource);
    }

    [Fact]
    public void StartPosPopupBindingsUseNativeButtonBindingFields() {
        string startPosPopupSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-startpos-popups.cs"));
        string imguiRendererSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-imgui-renderer.cs"));
        string bindableActionsSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-bindable-actions.cs"));

        Assert.Contains("DrawPopupActionBindingContext(\"StartPos\", \"Set\")", startPosPopupSource);
        Assert.Contains("DrawPopupActionBindingContext(\"StartPos\", \"Load\")", startPosPopupSource);
        Assert.Contains("DrawPopupActionBindingContext(\"StartPos\", \"Clear\")", startPosPopupSource);
        Assert.Contains("DescribePopupActionBinding(actionKey)", bindableActionsSource);
        Assert.Contains("PopupActionKey(\"StartPos\", \"Set\")", bindableActionsSource);
        Assert.Contains("PopupActionKey(\"StartPos\", \"Load\")", bindableActionsSource);
        Assert.Contains("PopupActionKey(\"StartPos\", \"Clear\")", bindableActionsSource);
        Assert.Contains("PopupActionKey(\"StartPos\", \"Set\")", imguiRendererSource);
        Assert.Contains("PopupActionKey(\"StartPos\", \"Load\")", imguiRendererSource);
        Assert.Contains("PopupActionKey(\"StartPos\", \"Clear\")", imguiRendererSource);
        Assert.Contains("StartButtonBindingCapture(actionKey, displayName, binding => TrySetDefaultButtonBinding(actionKey, binding))", imguiRendererSource);
        Assert.Contains("Right-click or Shift-click to bind.", imguiRendererSource);
        Assert.Contains("StartButtonBindingCapture(actionKey, resolvedDisplayName, binding => TrySetDefaultButtonBinding(actionKey, binding))", bindableActionsSource);
        Assert.Contains("ClearDefaultButtonBinding(actionKey)", bindableActionsSource);
        Assert.Contains("TryGetDefaultButtonBinding(actionKey, out ButtonBinding binding) && !IsEmptyBinding(binding)", bindableActionsSource);
        Assert.Contains("AkronModuleSettings.DescribeBinding(binding)", bindableActionsSource);
    }

    [Fact]
    public void SpeedrunToolStateTransitionsSuppressAkronRenderSurfacesBriefly() {
        string interopSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Interop/akron-interop.cs"));
        string popupSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-gameplay-popups.cs"));
        string moduleSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/AkronModule.cs"));
        string suppressionSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Runtime/akron-render-transition-suppression.cs"));

        Assert.Contains("Celeste.Mod.SpeedrunTool.ModInterop.SaveLoadInterop+SaveLoadExports", interopSource);
        Assert.Contains("EnsureSpeedrunToolSaveLoadHooksRegistered", interopSource);
        Assert.Contains("AkronModule.SuppressAkronRenderSurfacesAfterStateTransition", interopSource);
        Assert.Contains("AkronModule.SuppressLagPauserForSpeedrunToolLoadState", interopSource);
        Assert.Contains("Action clearState = () =>", interopSource);
        Assert.Contains("AkronInterop.SpeedrunToolLoaded", popupSource);
        Assert.Contains("\"Ignore SRT\"", popupSource);
        Assert.Contains("LagPauserIgnoreSpeedrunToolLoadStates", popupSource);
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

    private static Dictionary<string, AkronFeatureKind?> BuildOverlayEntryFeatureKinds(string tab) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("BuildDisplayEntriesForTab", BindingFlags.NonPublic | BindingFlags.Static)!;
        object entries = method.Invoke(null, new object?[] { tab, null })!;
        Type entryType = entries.GetType().GetGenericArguments()[0];
        PropertyInfo labelProperty = entryType.GetProperty("Label", BindingFlags.Public | BindingFlags.Instance)!;
        PropertyInfo featureKindProperty = entryType.GetProperty("FeatureKind", BindingFlags.Public | BindingFlags.Instance)!;

        return ((System.Collections.IEnumerable) entries)
            .Cast<object>()
            .ToDictionary(
                entry => (string) labelProperty.GetValue(entry)!,
                entry => (AkronFeatureKind?) featureKindProperty.GetValue(entry),
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, bool> BuildOverlayEntryIsToggles(string tab) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("BuildDisplayEntriesForTab", BindingFlags.NonPublic | BindingFlags.Static)!;
        object entries = method.Invoke(null, new object?[] { tab, null })!;
        Type entryType = entries.GetType().GetGenericArguments()[0];
        PropertyInfo labelProperty = entryType.GetProperty("Label", BindingFlags.Public | BindingFlags.Instance)!;
        PropertyInfo isToggleProperty = entryType.GetProperty("IsToggle", BindingFlags.Public | BindingFlags.Instance)!;

        return ((System.Collections.IEnumerable) entries)
            .Cast<object>()
            .ToDictionary(
                entry => (string) labelProperty.GetValue(entry)!,
                entry => (bool) isToggleProperty.GetValue(entry)!,
                StringComparer.OrdinalIgnoreCase);
    }

    private static Func<bool> BuildOverlayEntryEnabled(string tab, string label, Level? level) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("BuildDisplayEntriesForTab", BindingFlags.NonPublic | BindingFlags.Static)!;
        object entries = method.Invoke(null, new object?[] { tab, level })!;
        Type entryType = entries.GetType().GetGenericArguments()[0];
        PropertyInfo labelProperty = entryType.GetProperty("Label", BindingFlags.Public | BindingFlags.Instance)!;
        PropertyInfo enabledProperty = entryType.GetProperty("Enabled", BindingFlags.Public | BindingFlags.Instance)!;
        object entry = ((System.Collections.IEnumerable) entries)
            .Cast<object>()
            .Single(candidate => string.Equals((string) labelProperty.GetValue(candidate)!, label, StringComparison.OrdinalIgnoreCase));
        return (Func<bool>) enabledProperty.GetValue(entry)!;
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

    private static string ExtractMethod(string source, string signature) {
        int methodStart = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(methodStart >= 0);

        int bodyStart = source.IndexOf('{', methodStart);
        Assert.True(bodyStart > methodStart);

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++) {
            if (source[index] == '{') {
                depth++;
            } else if (source[index] == '}') {
                depth--;
                if (depth == 0) {
                    return source[methodStart..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException("Could not extract method: " + signature);
    }

    private static HashSet<string> GetCollapsedWindowTitles(AkronOverlay overlay) {
        FieldInfo field = typeof(AkronOverlay).GetField("collapsedWindowTitles", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (HashSet<string>) field.GetValue(overlay)!;
    }

    private static AkronOverlay CreateOverlayForStateTest() {
        AkronOverlay overlay = (AkronOverlay) RuntimeHelpers.GetUninitializedObject(typeof(AkronOverlay));
        SetPrivateField(overlay, "collapsedWindowTitles", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        SetPrivateField(overlay, "pendingImGuiCollapseSync", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        SetPrivateField(overlay, "imguiFrameLifecycle", new AkronImGuiFrameLifecycle());
        overlay.Visible = true;
        return overlay;
    }

    private static void SetPrivateField(object target, string fieldName, object value) {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(target, value);
    }

}
