using System;
using System.Collections.Generic;
using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot) {
        CreateModMenuSectionHeader(menu, inGame, snapshot);

        menu.Add(new TextMenu.SubHeader("Rulesets", topPadding: false));
        menu.Add(new TextMenu.SubHeader("Current Stack", topPadding: false));
        AddWrappedModMenuSubHeaders(menu, Settings.DescribeRulesetStack(), topPadding: false);
        AddWrappedModMenuSubHeaders(menu, Settings.DescribePrimaryRulesetBehavior(), topPadding: false);
        AddWrappedModMenuSubHeaders(menu, Settings.DescribeOverlayBehavior(), topPadding: false);

        Settings.CreateActiveProfileEntry(menu, inGame);
        menu.Add(new TextMenu.Button("Primary Ruleset: " + AkronModuleSettings.FormatPrimaryRuleset(Settings.PrimaryRuleset)) { Selectable = false });
        foreach (PrimaryRuleset ruleset in (PrimaryRuleset[]) Enum.GetValues(typeof(PrimaryRuleset))) {
            PrimaryRuleset capturedRuleset = ruleset;
            menu.Add(new TextMenu.Button("Use " + AkronModuleSettings.FormatPrimaryRuleset(capturedRuleset)).Pressed(() => ApplyRuleset(capturedRuleset)));
        }
        menu.Add(new TextMenu.OnOff("Streamer Mode", Settings.StreamerMode).Change(value => Settings.StreamerMode = value));
        menu.Add(new TextMenu.OnOff("Safe Mode", Settings.SafeMode).Change(value => Settings.SafeMode = value));

        menu.Add(new TextMenu.SubHeader("Overlay And HUD Defaults"));
        TextMenuExt.EnumSlider<IndicatorVisibility> visibilitySlider = new TextMenuExt.EnumSlider<IndicatorVisibility>("Indicator Visibility", Settings.IndicatorVisibility);
        visibilitySlider.Change(value => Settings.IndicatorVisibility = value);
        menu.Add(visibilitySlider);
        TextMenuExt.EnumSlider<IndicatorCorner> cornerSlider = new TextMenuExt.EnumSlider<IndicatorCorner>("Indicator Corner", Settings.IndicatorCorner);
        cornerSlider.Change(value => Settings.IndicatorCorner = value);
        menu.Add(cornerSlider);
        menu.Add(new TextMenu.Button("Indicator Nudge Left (" + Settings.IndicatorOffsetX + ", " + Settings.IndicatorOffsetY + ")").Pressed(() => Settings.IndicatorOffsetX -= 16));
        menu.Add(new TextMenu.Button("Indicator Nudge Right (" + Settings.IndicatorOffsetX + ", " + Settings.IndicatorOffsetY + ")").Pressed(() => Settings.IndicatorOffsetX += 16));
        menu.Add(new TextMenu.Button("Indicator Nudge Up (" + Settings.IndicatorOffsetX + ", " + Settings.IndicatorOffsetY + ")").Pressed(() => Settings.IndicatorOffsetY -= 16));
        menu.Add(new TextMenu.Button("Indicator Nudge Down (" + Settings.IndicatorOffsetX + ", " + Settings.IndicatorOffsetY + ")").Pressed(() => Settings.IndicatorOffsetY += 16));
        menu.Add(new TextMenu.Button("Reset Indicator Offset").Pressed(() => {
            Settings.IndicatorOffsetX = 0;
            Settings.IndicatorOffsetY = 0;
        }));
        menu.Add(new TextMenu.OnOff("Consume Input In Menu", Settings.ConsumeGameplayInputInMenu).Change(value => Settings.ConsumeGameplayInputInMenu = value));
        menu.Add(new TextMenu.OnOff("Pause Gameplay In Menu", Settings.PauseGameplayInMenu).Change(value => Settings.PauseGameplayInMenu = value));
        menu.Add(new TextMenu.OnOff("Room Labels", Settings.RoomLabels).Change(value => Settings.RoomLabels = value));
        menu.Add(new TextMenu.OnOff("Input Viewer", Settings.InputViewer).Change(value => Settings.InputViewer = value));
        menu.Add(new TextMenu.OnOff("Input History Panel", Settings.InputHistoryPanel).Change(value => Settings.InputHistoryPanel = value));
        menu.Add(new TextMenu.OnOff("Input History Compact", Settings.InputHistoryCompact).Change(value => Settings.InputHistoryCompact = value));
        menu.Add(new TextMenu.Button("Input History Length: " + Settings.InputHistoryLength).Pressed(() => Settings.InputHistoryLength = ClampCycle(Settings.InputHistoryLength + 2, 4, 20)));
        menu.Add(new TextMenu.Button("Input History Placement: " + Settings.InputHistoryPlacement).Pressed(() => Settings.InputHistoryPlacement = Settings.InputHistoryPlacement == AkronHudPlacement.Left ? AkronHudPlacement.Right : AkronHudPlacement.Left));
        menu.Add(new TextMenu.Button("Input History Opacity: " + Settings.InputHistoryOpacity + "%").Pressed(() => Settings.InputHistoryOpacity = ClampCycle(Settings.InputHistoryOpacity + 14, 30, 100)));
        menu.Add(new TextMenu.OnOff("Stamina Bar", Settings.StaminaBar).Change(value => Settings.StaminaBar = value));
        menu.Add(new TextMenu.OnOff("Stamina Player Bar", Settings.StaminaBarPlayer).Change(value => Settings.StaminaBarPlayer = value));
        menu.Add(new TextMenu.OnOff("Stamina HUD Bar", Settings.StaminaBarHud).Change(value => Settings.StaminaBarHud = value));
        menu.Add(new TextMenu.OnOff("Dash Bar", Settings.DashBar).Change(value => Settings.DashBar = value));
        menu.Add(new TextMenu.Button("Low Stamina Threshold: " + Settings.LowStaminaThreshold).Pressed(() => Settings.LowStaminaThreshold = ClampCycle(Settings.LowStaminaThreshold + 5, 5, 60)));
        menu.Add(new TextMenu.OnOff("Room Timer", Settings.RoomTimerWidget).Change(value => Settings.RoomTimerWidget = value));
        menu.Add(new TextMenu.OnOff("Death Stats", Settings.DeathStatsWidget).Change(value => Settings.DeathStatsWidget = value));
        menu.Add(new TextMenu.OnOff("Reduced Visual Noise", Settings.ReducedVisualNoise).Change(value => Settings.ReducedVisualNoise = value));
        menu.Add(new TextMenu.OnOff("No Particles", Settings.NoParticles).Change(Settings.SetNoParticles));
        menu.Add(new TextMenu.OnOff("No Trails", Settings.NoTrails).Change(Settings.SetNoTrails));
        menu.Add(new TextMenu.OnOff("No Glitch", Settings.NoGlitch).Change(Settings.SetNoGlitch));
        menu.Add(new TextMenu.OnOff("No Anxiety", Settings.NoAnxiety).Change(Settings.SetNoAnxiety));
        menu.Add(new TextMenu.OnOff("No Distortion", Settings.NoDistortion).Change(Settings.SetNoDistortion));
        menu.Add(new TextMenu.OnOff("Hide Snow", Settings.HideSnow).Change(value => Settings.HideSnow = value));
        menu.Add(new TextMenu.OnOff("Hide Wind Snow", Settings.HideWindSnow).Change(value => Settings.HideWindSnow = value));
        menu.Add(new TextMenu.OnOff("Hide Waterfalls", Settings.HideWaterfalls).Change(value => Settings.HideWaterfalls = value));
        menu.Add(new TextMenu.OnOff("Hide Tentacles", Settings.HideTentacles).Change(value => Settings.HideTentacles = value));
        menu.Add(new TextMenu.OnOff("Hide Heat Distortion", Settings.HideHeatDistortion).Change(value => Settings.HideHeatDistortion = value));
        menu.Add(new TextMenu.OnOff("No Death Wipe", Settings.NoDeathWipe).Change(value => Settings.NoDeathWipe = value));
        menu.Add(new TextMenu.OnOff("Hitbox Active Only", Settings.HitboxActiveOnly).Change(value => Settings.HitboxActiveOnly = value));
        menu.Add(new TextMenu.OnOff("Hitbox Hide Player", Settings.HitboxHidePlayer).Change(value => Settings.HitboxHidePlayer = value));
        menu.Add(new TextMenu.OnOff("Hitbox Hazards", Settings.HitboxShowHazards).Change(value => Settings.HitboxShowHazards = value));
        menu.Add(new TextMenu.OnOff("Hitbox Solids", Settings.HitboxShowSolids).Change(value => Settings.HitboxShowSolids = value));
        menu.Add(new TextMenu.OnOff("Hitbox Triggers", Settings.HitboxShowTriggers).Change(value => Settings.HitboxShowTriggers = value));
        menu.Add(new TextMenu.OnOff("Hitbox Last Death", Settings.HitboxShowLastDeath).Change(value => Settings.HitboxShowLastDeath = value));
        menu.Add(new TextMenu.OnOff("Hitbox Death All", Settings.HitboxShowAllOnDeath).Change(value => Settings.HitboxShowAllOnDeath = value));
        menu.Add(new TextMenu.OnOff("Fix Hitbox Pixels", Settings.FixHitboxPixels).Change(value => Settings.FixHitboxPixels = value));
        menu.Add(new TextMenu.OnOff("Show Hitbox Trail", Settings.ShowHitboxTrail).Change(value => Settings.ShowHitboxTrail = value));
        menu.Add(new TextMenu.Button("Hitbox Trail Length: " + Settings.HitboxTrailLength).Pressed(() => Settings.HitboxTrailLength = ClampCycle(Settings.HitboxTrailLength + 10, 10, 240)));

        menu.Add(new TextMenu.SubHeader("Training And Compatibility"));
        menu.Add(new TextMenu.OnOff("Frame Stepper Hold Repeat", Settings.StepHoldRepeat).Change(value => Settings.StepHoldRepeat = value));
        menu.Add(new TextMenu.Button("Frame Stepper Hold Delay: " + Settings.StepHoldDelayFrames + "f").Pressed(() => Settings.StepHoldDelayFrames = ClampCycle(Settings.StepHoldDelayFrames + 6, 6, 60)));
        menu.Add(new TextMenu.Button("Frame Stepper Repeat Interval: " + Settings.StepHoldIntervalFrames + "f").Pressed(() => Settings.StepHoldIntervalFrames = ClampCycle(Settings.StepHoldIntervalFrames + 1, 1, 12)));
        Settings.CreateCurrentMapCompatibilityEntry(menu, inGame);
        menu.Add(new TextMenu.OnOff("Broker Warnings", Settings.SpeedrunToolBrokerWarnings).Change(value => Settings.SpeedrunToolBrokerWarnings = value));
        menu.Add(new TextMenu.OnOff("Everest-safe Auto Block", Settings.EverestSafeAutoBlock).Change(value => Settings.EverestSafeAutoBlock = value));
        menu.Add(new TextMenu.OnOff("Preserve Time And Deaths", Settings.SaveTimeAndDeaths).Change(value => Settings.SaveTimeAndDeaths = value));
        menu.Add(new TextMenu.OnOff("Unsafe StartPos Override", Settings.UnsafeSavestateOverride).Change(value => Settings.UnsafeSavestateOverride = value));
        menu.Add(new TextMenu.OnOff("Autosave", Settings.Autosave).Change(value => Settings.Autosave = value));
        menu.Add(new TextMenu.Button("Autosave Interval: " + Settings.AutosaveIntervalSeconds + "s").Pressed(() => Settings.AutosaveIntervalSeconds = ClampCycle(Settings.AutosaveIntervalSeconds + 60, 60, 1800)));
        menu.Add(new TextMenu.OnOff("Hide Saving Icon", Settings.AutosaveHideSavingIcon).Change(value => Settings.AutosaveHideSavingIcon = value));
        menu.Add(new TextMenu.OnOff("Dash Stats", Settings.DashCountStats).Change(value => Settings.DashCountStats = value));
        menu.Add(new TextMenu.Button("Dash Stats Mode: " + Settings.DashCountStatsMode).Pressed(() => Settings.DashCountStatsMode = NextCounterDisplayMode(Settings.DashCountStatsMode)));
        menu.Add(new TextMenu.OnOff("Jump Stats", Settings.JumpCount).Change(value => Settings.JumpCount = value));
        menu.Add(new TextMenu.Button("Jump Stats Mode: " + Settings.JumpCountMode).Pressed(() => Settings.JumpCountMode = NextCounterDisplayMode(Settings.JumpCountMode)));

        menu.Add(new TextMenu.SubHeader("Community Rulesets"));
        IReadOnlyList<AkronCommunityRulesetManifest> communityRulesets = AkronCommunityRulesets.LoadAvailable();
        if (communityRulesets.Count == 0) {
            menu.Add(new TextMenu.Button("No manifests found.") { Selectable = false });
            AddWrappedModMenuSubHeaders(menu, "Bundle JSON in Rulesets/ or place local imports in Saves/AkronRulesets.", topPadding: false);
        } else {
            foreach (AkronCommunityRulesetManifest manifest in communityRulesets) {
                menu.Add(new TextMenu.Button("Apply " + manifest.Label).Pressed(() => {
                    AkronCommunityRulesets.Apply(manifest);
                    Engine.Scene?.Add(new AkronToast("Applied community ruleset: " + manifest.Label));
                }));
                if (!string.IsNullOrWhiteSpace(manifest.Description)) {
                    menu.Add(new TextMenu.SubHeader(manifest.Description, topPadding: false));
                }
            }
        }

        menu.Add(new TextMenu.SubHeader("Native Hotkey Fallbacks"));
        AddWrappedModMenuSubHeaders(menu, "Akron menu rows are bindable in the overlay by right-clicking or Shift-clicking them. These native config UIs remain for built-in keyboard/controller bindings.", topPadding: false);
        menu.Add(new TextMenu.Button(Dialog.Clean("options_keyconfig")).Pressed(() => OpenKeyboardConfig(menu)));
        menu.Add(new TextMenu.Button(Dialog.Clean("options_btnconfig")).Pressed(() => OpenButtonConfig(menu)));
        AddHotkeyPreview(menu, "Open Overlay", Settings.ToggleOverlay);
        AddHotkeyPreview(menu, "Retry", Settings.Retry);
        AddHotkeyPreview(menu, "Capture StartPos State", Settings.SaveState);
        AddHotkeyPreview(menu, "Restore StartPos State", Settings.LoadState);
        AddHotkeyPreview(menu, "Reload Room", Settings.ReloadRoom);
        AddHotkeyPreview(menu, "Open Debug Map", Settings.OpenDebugMap);
        AddHotkeyPreview(menu, "Freeze Gameplay", Settings.FreezeGameplay);
        AddHotkeyPreview(menu, "Step Frame", Settings.StepFrame);
        AddHotkeyPreview(menu, "Click Teleport Cursor", Settings.ClickTeleportCursor);
        AddHotkeyPreview(menu, "Cursor Zoom Hold", Settings.CursorZoomHold);
        AddHotkeyPreview(menu, "Set StartPos", Settings.SetStartPos);
        AddHotkeyPreview(menu, "Load StartPos", Settings.LoadStartPos);
        AddHotkeyPreview(menu, "Clear StartPos", Settings.ClearStartPos);
        AddHotkeyPreview(menu, "Previous StartPos", Settings.PreviousStartPos);
        AddHotkeyPreview(menu, "Next StartPos", Settings.NextStartPos);
        for (int slot = 1; slot <= 9; slot++) {
            AddHotkeyPreview(menu, "Load StartPos Slot " + slot, GetStartPosSlotBinding(slot));
        }
        AddHotkeyPreview(menu, "Toggle Hitboxes", Settings.ToggleHitboxes);
        AddHotkeyPreview(menu, "Toggle Entity Inspector", Settings.ToggleEntityInspector);
    }

    private void OpenKeyboardConfig(TextMenu menu) {
        if (Engine.Scene == null) {
            return;
        }

        menu.Focused = false;
        KeyboardConfigUI keyboardConfig = CreateKeyboardConfigUiMethod != null
            ? (KeyboardConfigUI) CreateKeyboardConfigUiMethod.Invoke(this, new object[] { menu })
            : new ModuleSettingsKeyboardConfigUI(this);
        keyboardConfig.OnClose = () => { menu.Focused = true; };
        Engine.Scene.Add(keyboardConfig);
        Engine.Scene.OnEndOfFrame += () => Engine.Scene.Entities.UpdateLists();
    }

    private void OpenButtonConfig(TextMenu menu) {
        if (Engine.Scene == null) {
            return;
        }

        menu.Focused = false;
        ButtonConfigUI buttonConfig = CreateButtonConfigUiMethod != null
            ? (ButtonConfigUI) CreateButtonConfigUiMethod.Invoke(this, new object[] { menu })
            : new ModuleSettingsButtonConfigUI(this);
        buttonConfig.OnClose = () => { menu.Focused = true; };
        Engine.Scene.Add(buttonConfig);
        Engine.Scene.OnEndOfFrame += () => Engine.Scene.Entities.UpdateLists();
    }

    private static void AddHotkeyPreview(TextMenu menu, string label, ButtonBinding binding) {
        menu.Add(new TextMenu.Button(label + ": " + AkronModuleSettings.DescribeBinding(binding)) { Selectable = false });
    }

    private static int ClampCycle(int value, int minimum, int maximum) {
        return value > maximum ? minimum : Calc.Clamp(value, minimum, maximum);
    }

    private static AkronCounterDisplayMode NextCounterDisplayMode(AkronCounterDisplayMode mode) {
        return (AkronCounterDisplayMode) (((int) mode + 1) % Enum.GetValues(typeof(AkronCounterDisplayMode)).Length);
    }

    private static void AddWrappedModMenuSubHeaders(TextMenu menu, string text, bool topPadding = true) {
        foreach (string line in WrapModMenuLine(text)) {
            menu.Add(new TextMenu.SubHeader(line, topPadding));
            topPadding = false;
        }
    }

    internal static IReadOnlyList<string> WrapModMenuLine(string text, int maxCharacters = 64) {
        List<string> lines = new List<string>();
        foreach (string rawParagraph in (text ?? string.Empty).Split('\n')) {
            string paragraph = rawParagraph.Trim();
            if (paragraph.Length == 0) {
                continue;
            }

            string current = string.Empty;
            foreach (string word in paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) {
                if (current.Length == 0) {
                    current = word;
                    continue;
                }

                if (current.Length + 1 + word.Length > maxCharacters) {
                    lines.Add(current);
                    current = word;
                    continue;
                }

                current += " " + word;
            }

            if (current.Length > 0) {
                lines.Add(current);
            }
        }

        return lines.Count == 0 ? new[] { string.Empty } : lines;
    }
}
