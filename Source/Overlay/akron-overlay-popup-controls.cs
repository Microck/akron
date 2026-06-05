using System;
using System.Collections.Generic;
using System.Globalization;
using Celeste;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Monocle;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawImGuiOptionsPopupContent(ActionEntry entry, string popupId) {
        string previousOptionsPopupLabel = activeOptionsPopupLabel;
        activeOptionsPopupLabel = entry.Label;
        try {
        if (entry.IsCustomHudLabelRow) {
            SelectCustomHudLabel(entry.CustomHudLabelId);
            DrawCustomHudLabelsPopupControls(popupId);
        } else if (string.Equals(entry.Label, "StartPos Snapshot Slot", StringComparison.OrdinalIgnoreCase)) {
            DrawSavestateSlotPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Grab Mode", StringComparison.OrdinalIgnoreCase)) {
            DrawGrabModePopupControls(popupId);
        } else if (string.Equals(entry.Label, "Overlay Appearance", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entry.Label, "Theme", StringComparison.OrdinalIgnoreCase)) {
            DrawOverlayAppearancePopupControls(popupId);
        } else if (string.Equals(entry.Label, "Keybinds", StringComparison.OrdinalIgnoreCase)) {
            DrawKeybindsPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Export Profile", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entry.Label, "Import Profile", StringComparison.OrdinalIgnoreCase)) {
            DrawProfilePopupControls(popupId);
        } else if (string.Equals(entry.Label, "Confirm Actions", StringComparison.OrdinalIgnoreCase)) {
            DrawConfirmActionsPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Noclip", StringComparison.OrdinalIgnoreCase)) {
            DrawNoclipPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Hazard Accuracy", StringComparison.OrdinalIgnoreCase)) {
            DrawHazardAccuracyPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Air Jumps", StringComparison.OrdinalIgnoreCase)) {
            DrawAirJumpsPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Frame Stepper", StringComparison.OrdinalIgnoreCase)) {
            DrawFrameStepperPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Respawn Time", StringComparison.OrdinalIgnoreCase)) {
            DrawRespawnTimePopupControls(popupId);
        } else if (IsPauseTimerLabel(entry.Label)) {
            DrawPauseCountdownPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Fast Lookout", StringComparison.OrdinalIgnoreCase)) {
            DrawFastLookoutPopupControls(popupId);
        } else if (string.Equals(entry.Label, "No Death Wipe", StringComparison.OrdinalIgnoreCase)) {
            DrawNoDeathWipePopupControls(popupId);
        } else if (string.Equals(entry.Label, "Allow Low Volume", StringComparison.OrdinalIgnoreCase)) {
            DrawAllowLowVolumePopupControls(popupId);
        } else if (string.Equals(entry.Label, "Audio Speed", StringComparison.OrdinalIgnoreCase)) {
            DrawAudioSpeedPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Pitch Shift", StringComparison.OrdinalIgnoreCase)) {
            DrawPitchShiftPopupControls(popupId);
        } else if (string.Equals(entry.Label, "FPS Bypass", StringComparison.OrdinalIgnoreCase)) {
            DrawFpsBypassPopupControls(popupId);
        } else if (string.Equals(entry.Label, "TPS Bypass", StringComparison.OrdinalIgnoreCase)) {
            DrawTpsBypassPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Safe Mode", StringComparison.OrdinalIgnoreCase)) {
            DrawSafeModePopupControls(popupId);
        } else if (string.Equals(entry.Label, "Screenshake", StringComparison.OrdinalIgnoreCase)) {
            DrawScreenshakePopupControls(popupId);
        } else if (string.Equals(entry.Label, "Light Level", StringComparison.OrdinalIgnoreCase)) {
            DrawLightLevelPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Bloom Level", StringComparison.OrdinalIgnoreCase)) {
            DrawBloomLevelPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Screen Tint", StringComparison.OrdinalIgnoreCase)) {
            DrawScreenTintPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Lag Pauser", StringComparison.OrdinalIgnoreCase)) {
            DrawLagPauserPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Golden Start", StringComparison.OrdinalIgnoreCase)) {
            DrawGoldenStartPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Free Camera", StringComparison.OrdinalIgnoreCase)) {
            DrawFreeCameraPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Camera Offset", StringComparison.OrdinalIgnoreCase)) {
            DrawCameraOffsetPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Cursor Zoom", StringComparison.OrdinalIgnoreCase)) {
            DrawCursorZoomPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Show Trajectory", StringComparison.OrdinalIgnoreCase)) {
            DrawShowTrajectoryPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Control Display", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entry.Label, "Show Taps", StringComparison.OrdinalIgnoreCase)) {
            DrawShowTapsPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Inputs per second", StringComparison.OrdinalIgnoreCase)) {
            DrawInputsPerSecondPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Visible", StringComparison.OrdinalIgnoreCase)) {
            DrawLabelSystemPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Player Overlap", StringComparison.OrdinalIgnoreCase)) {
            DrawLabelPlayerOverlapPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Room", StringComparison.OrdinalIgnoreCase)) {
            DrawBuiltInLabelStylePopupControls("Color", () => AkronModule.Settings.RoomLabelColor, value => AkronModule.Settings.RoomLabelColor = value, AkronModule.Settings.RoomLabelStyle, popupId);
        } else if (string.Equals(entry.Label, "Death Stats", StringComparison.OrdinalIgnoreCase)) {
            DrawDeathStatsPopupControls(popupId);
            DrawBuiltInLabelStylePopupControls("Color", () => AkronModule.Settings.DeathStatsColor, value => AkronModule.Settings.DeathStatsColor = value, AkronModule.Settings.DeathStatsLabelStyle, popupId);
        } else if (string.Equals(entry.Label, "Room Timer", StringComparison.OrdinalIgnoreCase)) {
            DrawBuiltInLabelStylePopupControls("Color", () => AkronModule.Settings.RoomTimerColor, value => AkronModule.Settings.RoomTimerColor = value, AkronModule.Settings.RoomTimerLabelStyle, popupId);
        } else if (string.Equals(entry.Label, "Room Stat Tracker", StringComparison.OrdinalIgnoreCase)) {
            DrawRoomStatTrackerPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Attempts", StringComparison.OrdinalIgnoreCase)) {
            DrawBuiltInLabelStylePopupControls("Color", () => AkronModule.Settings.TotalAttemptsColor, value => AkronModule.Settings.TotalAttemptsColor = value, AkronModule.Settings.TotalAttemptsLabelStyle, popupId);
        } else if (string.Equals(entry.Label, "Status", StringComparison.OrdinalIgnoreCase)) {
            DrawBuiltInLabelStylePopupControls("Color", () => AkronModule.Settings.StatusLabelsColor, value => AkronModule.Settings.StatusLabelsColor = value, AkronModule.Settings.StatusLabelsLabelStyle, popupId);
        } else if (string.Equals(entry.Label, "Toasts", StringComparison.OrdinalIgnoreCase)) {
            DrawToastLabelPopupControls(popupId);
        } else if (string.Equals(entry.Label, "StartPos HUD", StringComparison.OrdinalIgnoreCase)) {
            DrawStartPosLabelPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Custom", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entry.Label, "Custom HUD Labels", StringComparison.OrdinalIgnoreCase)) {
            DrawCustomHudLabelsPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Refill Clarity", StringComparison.OrdinalIgnoreCase)) {
            DrawRefillClarityPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Room Capture", StringComparison.OrdinalIgnoreCase)) {
            DrawScreenshotCapturePopupControls(popupId, chapter: false);
        } else if (string.Equals(entry.Label, "Map Capture", StringComparison.OrdinalIgnoreCase)) {
            DrawScreenshotCapturePopupControls(popupId, chapter: true);
        } else if (string.Equals(entry.Label, "Autosave", StringComparison.OrdinalIgnoreCase)) {
            DrawAutosavePopupControls(popupId);
        } else if (string.Equals(entry.Label, "Deload Spinners", StringComparison.OrdinalIgnoreCase)) {
            DrawDeloadSpinnersPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Dash Stats", StringComparison.OrdinalIgnoreCase)) {
            DrawCounterStatsPopupControls(true, popupId);
        } else if (string.Equals(entry.Label, "Jump Stats", StringComparison.OrdinalIgnoreCase)) {
            DrawCounterStatsPopupControls(false, popupId);
        } else if (string.Equals(entry.Label, "Audio Splitter", StringComparison.OrdinalIgnoreCase)) {
            DrawAudioSplitterPopupControls(popupId);
        } else if (TryGetSoundDefinitionByLabel(entry.Label, out AkronEarAid.SoundDefinition sound)) {
            DrawSoundVolumePopupControls(sound, popupId);
        } else if (string.Equals(entry.Label, "Replay Settings", StringComparison.OrdinalIgnoreCase)) {
            DrawRecorderReplayPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Output", StringComparison.OrdinalIgnoreCase)) {
            DrawRecorderOutputPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Codec", StringComparison.OrdinalIgnoreCase)) {
            DrawRecorderEncoderPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Audio", StringComparison.OrdinalIgnoreCase)) {
            DrawRecorderAudioPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Clip Triggers", StringComparison.OrdinalIgnoreCase)) {
            DrawRecorderClipTriggersPopupControls(popupId);
        } else if (IsRecorderTextOptionsLabel(entry.Label)) {
            DrawRecorderTextPopupControls(entry.Label, popupId);
        } else if (string.Equals(entry.Label, "Cheat Indicator", StringComparison.OrdinalIgnoreCase)) {
            DrawHudCheatIndicatorPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Berry Obtain Options", StringComparison.OrdinalIgnoreCase)) {
            DrawBerryObtainOptionsPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Auto Kill", StringComparison.OrdinalIgnoreCase)) {
            DrawAutoKillPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Auto Deafen", StringComparison.OrdinalIgnoreCase)) {
            DrawAutoDeafenPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Transition Speed", StringComparison.OrdinalIgnoreCase)) {
            DrawTransitionSpeedPopupControls(popupId);
        } else if (string.Equals(entry.Label, "StartPos", StringComparison.OrdinalIgnoreCase)) {
            DrawStartPosPopupControls(popupId);
        } else if (string.Equals(entry.Label, "StartPos Switcher", StringComparison.OrdinalIgnoreCase)) {
            DrawStartPosSwitcherPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Place StartPos", StringComparison.OrdinalIgnoreCase)) {
            DrawPlaceStartPosPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Input History", StringComparison.OrdinalIgnoreCase)) {
            DrawInputDisplayPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Stamina Bar", StringComparison.OrdinalIgnoreCase)) {
            DrawStaminaBarPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Dash Bar", StringComparison.OrdinalIgnoreCase)) {
            DrawDashBarPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Dash Count", StringComparison.OrdinalIgnoreCase)) {
            DrawDashCountPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Dash Number", StringComparison.OrdinalIgnoreCase)) {
            DrawDashNumberPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Speed Number", StringComparison.OrdinalIgnoreCase)) {
            DrawSpeedNumberPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Ground Refills", StringComparison.OrdinalIgnoreCase)) {
            DrawGroundRefillsPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Prevent Down Dash Redirects", StringComparison.OrdinalIgnoreCase)) {
            DrawPreventDownDashRedirectsPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Madeline Colors", StringComparison.OrdinalIgnoreCase)) {
            DrawMadelineColorsPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Madeline Hair Length", StringComparison.OrdinalIgnoreCase)) {
            DrawMadelineHairLengthPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Madeline Effect Sync", StringComparison.OrdinalIgnoreCase)) {
            DrawMadelineEffectSyncPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Trail Visibility", StringComparison.OrdinalIgnoreCase)) {
            DrawTrailVisibilityPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Custom Trail", StringComparison.OrdinalIgnoreCase)) {
            DrawCustomTrailPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Golden Transparency", StringComparison.OrdinalIgnoreCase)) {
            DrawGoldenTransparencyPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Show Hitboxes", StringComparison.OrdinalIgnoreCase)) {
            DrawHitboxPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Show Hitbox Trail", StringComparison.OrdinalIgnoreCase)) {
            DrawHitboxTrailPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Show Hitboxes On Death", StringComparison.OrdinalIgnoreCase)) {
            DrawHitboxOnDeathPopupControls(popupId);
        } else if (string.Equals(entry.Label, "Extended Variants Randomizer", StringComparison.OrdinalIgnoreCase)) {
            DrawExtendedVariantsRandomizerPopupControls(popupId);
        } else if (HasExtendedVariantOptionsPopup(entry.Label)) {
            DrawExtendedVariantPopupControls(entry.Label, popupId);
        }
        } finally {
            activeOptionsPopupLabel = previousOptionsPopupLabel;
        }
    }

    private void DrawProfilePopupControls(string popupId) {
        if (string.Equals(activeOptionsPopupLabel, "Export Profile", StringComparison.OrdinalIgnoreCase)) {
            string exportName = AkronModule.Settings.ProfilePackExportName ?? string.Empty;
            if (DrawPopupInputText("Name", ref exportName, 80, popupId, 280f)) {
                AkronModule.Settings.ProfilePackExportName = exportName;
            }
            ImGui.TextWrapped("Blank uses the active Akron profile name.");
            ImGui.Separator();
        }

        ImGui.TextUnformatted("Scope: " + AkronProfilePacks.FormatSection(AkronModule.Settings.ProfilePackSection));
        DrawProfileSectionChoice("Whole", AkronProfileSection.Whole, popupId);
        DrawProfileSectionChoice("StartPos", AkronProfileSection.StartPos, popupId);
        DrawProfileSectionChoice("Keybinds", AkronProfileSection.Keybinds, popupId);
        DrawProfileSectionChoice("Auto Kill", AkronProfileSection.AutoKill, popupId);
        DrawProfileSectionChoice("Auto Deafen", AkronProfileSection.AutoDeafen, popupId);
        DrawProfileSectionChoice("Recorder", AkronProfileSection.Recorder, popupId);
        DrawProfileSectionChoice("Audio", AkronProfileSection.Audio, popupId);
        DrawProfileSectionChoice("HUD", AkronProfileSection.Hud, popupId);
        ImGui.TextWrapped("Whole applies the full profile. Scoped imports only replace the selected system.");
    }

    private static void DrawProfileSectionChoice(string label, AkronProfileSection section, string popupId) {
        bool selected = AkronModule.Settings.ProfilePackSection == section;
        if (ImGui.RadioButton(label + "##profile-section-" + popupId, selected)) {
            AkronModule.Settings.ProfilePackSection = section;
        }
    }

    private static void OpenCommunityPackBrowser() {
        communityPackBrowserOpen = true;
    }

    private static string TruncateImGuiText(string text, int maxPixels) {
        text = string.IsNullOrWhiteSpace(text) ? "No description provided." : text.Trim();
        if (maxPixels <= 0 || ImGui.CalcTextSize(text).X <= maxPixels) {
            return text;
        }

        while (text.Length > 3 && ImGui.CalcTextSize(text + "...").X > maxPixels) {
            text = text.Substring(0, text.Length - 1);
        }

        return text + "...";
    }

    private static string CurrentCommunityMapSid() {
        return Engine.Scene is Level level ? level.Session?.Area.GetSID() ?? string.Empty : string.Empty;
    }

    private static string DescribeCommunityPackBrowser() {
        if (Engine.Scene is not Level level) {
            return "No map";
        }

        string mapSid = level.Session?.Area.GetSID() ?? string.Empty;
        return string.IsNullOrWhiteSpace(mapSid) ? "No map" : "Map catalog";
    }

    private static IReadOnlyList<SelectorDropdownChoice> BuildGrabModeDropdownChoices() {
        return new List<SelectorDropdownChoice> {
            GrabModeDropdownChoice("Hold", GrabModes.Hold),
            GrabModeDropdownChoice("Toggle", GrabModes.Toggle),
            GrabModeDropdownChoice("Invert", GrabModes.Invert)
        };
    }

    private static SelectorDropdownChoice GrabModeDropdownChoice(string label, GrabModes mode) {
        return new SelectorDropdownChoice(label, () => Settings.Instance.GrabMode == mode, () => SetGrabMode(mode));
    }

    private static IReadOnlyList<SelectorDropdownChoice> BuildRoomStatTimerFreezeChoices() {
        return new List<SelectorDropdownChoice> {
            new SelectorDropdownChoice("Never", () => AkronModule.Settings.RoomStatTimerFreezeMode == AkronRoomStatTimerFreezeMode.Never, () => AkronModule.Settings.RoomStatTimerFreezeMode = AkronRoomStatTimerFreezeMode.Never),
            new SelectorDropdownChoice("Paused", () => AkronModule.Settings.RoomStatTimerFreezeMode == AkronRoomStatTimerFreezeMode.Paused, () => AkronModule.Settings.RoomStatTimerFreezeMode = AkronRoomStatTimerFreezeMode.Paused),
            new SelectorDropdownChoice("Inactive", () => AkronModule.Settings.RoomStatTimerFreezeMode == AkronRoomStatTimerFreezeMode.Inactive, () => AkronModule.Settings.RoomStatTimerFreezeMode = AkronRoomStatTimerFreezeMode.Inactive),
            new SelectorDropdownChoice("Cutscene", () => AkronModule.Settings.RoomStatTimerFreezeMode == AkronRoomStatTimerFreezeMode.Cutscene, () => AkronModule.Settings.RoomStatTimerFreezeMode = AkronRoomStatTimerFreezeMode.Cutscene),
            new SelectorDropdownChoice("Paused/inactive", () => AkronModule.Settings.RoomStatTimerFreezeMode == AkronRoomStatTimerFreezeMode.PausedOrInactive, () => AkronModule.Settings.RoomStatTimerFreezeMode = AkronRoomStatTimerFreezeMode.PausedOrInactive),
            new SelectorDropdownChoice("All pauses", () => AkronModule.Settings.RoomStatTimerFreezeMode == AkronRoomStatTimerFreezeMode.PausedInactiveOrCutscene, () => AkronModule.Settings.RoomStatTimerFreezeMode = AkronRoomStatTimerFreezeMode.PausedInactiveOrCutscene)
        };
    }

    private static bool DrawPopupInputText(string label, ref string value, int maxLength, string popupId, float preferredControlWidth) {
        float labelWidth = CalculatePopupLabelWidth(preferredControlWidth);
        float controlWidth = CalculatePopupControlWidth(labelWidth, preferredControlWidth, 96f);
        DrawPopupRowLabel(label, labelWidth);
        ImGui.PushItemWidth(controlWidth);
        bool changed = ImGui.InputText("##" + label + popupId, ref value, (uint) Math.Max(1, maxLength));
        ImGui.PopItemWidth();
        return changed;
    }

    private static void DrawPopupRowLabel(string label, float width) {
        float frameHeight = ImGui.GetFrameHeight();
        float textHeight = ImGui.GetTextLineHeight();
        NumericsVector2 min = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddText(
            new NumericsVector2(min.X, min.Y + Math.Max(0f, (frameHeight - textHeight) * 0.5f)),
            AkronImGuiTheme.ToU32(AkronImGuiTheme.Muted),
            TruncateImGuiTextToWidth(label, Math.Max(12f, width - 4f)));
        ImGui.Dummy(new NumericsVector2(width, frameHeight));
        ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X);
    }

    private static float CalculatePopupLabelWidth(float controlWidth) {
        float available = ImGui.GetContentRegionAvail().X;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        return Math.Min(
            PopupLabelColumnPreferredWidth,
            Math.Max(PopupLabelColumnMinWidth, available - controlWidth - spacing));
    }

    private static float CalculatePopupControlWidth(float labelWidth, float preferredControlWidth, float minimumControlWidth) {
        float available = ImGui.GetContentRegionAvail().X;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        return Math.Min(
            preferredControlWidth,
            Math.Max(minimumControlWidth, available - labelWidth - spacing));
    }

    private void DrawPopupChoiceCombo(
        string label,
        Func<string> value,
        IReadOnlyList<SelectorDropdownChoice> choices,
        string popupId,
        string tooltip) {
        const float comboWidth = 132f;
        DrawPopupRowLabel(label, CalculatePopupLabelWidth(comboWidth));
        ImGui.PushItemWidth(comboWidth);
        if (ImGui.BeginCombo("##" + label + popupId, value())) {
            for (int index = 0; index < choices.Count; index++) {
                SelectorDropdownChoice choice = choices[index];
                bool enabled = choice.Enabled();
                bool selected = choice.Selected();
                if (!enabled) {
                    ImGui.PushStyleColor(ImGuiCol.Text, AkronImGuiTheme.DisabledText);
                }

                if (ImGui.Selectable(choice.Label + "##" + label + popupId + index, selected) && enabled) {
                    string previousValue = value();
                    choice.Apply();
                    string nextValue = value();
                    if (!string.Equals(previousValue, nextValue, StringComparison.Ordinal)) {
                        AkronShowcaseMarkers.MarkPopupDetail(activeOptionsPopupLabel, label, "choice", nextValue);
                    }
                }

                if (selected) {
                    ImGui.SetItemDefaultFocus();
                }

                if (!enabled) {
                    ImGui.PopStyleColor();
                }
            }

            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();
        DrawPopupTooltip(tooltip, label);
    }

    private void DrawPopupCheckbox(string label, Func<bool> getter, Action<bool> setter, string popupId, string tooltip) {
        bool enabled = getter();
        const float checkboxWidth = 24f;
        DrawPopupRowLabel(label, CalculatePopupLabelWidth(checkboxWidth));
        if (ImGui.Checkbox("##" + label + popupId, ref enabled)) {
            setter(enabled);
            AkronShowcaseMarkers.MarkPopupDetail(activeOptionsPopupLabel, label, "checkbox", enabled.ToString().ToLowerInvariant());
        }
        DrawPopupTooltip(tooltip, label);
    }

    private static string FormatPreventDownDashRedirectMode(AkronPreventDownDashRedirectMode mode) {
        return mode switch {
            AkronPreventDownDashRedirectMode.Normal => "Normal",
            AkronPreventDownDashRedirectMode.Diagonal => "Diagonal",
            _ => "Disabled"
        };
    }

    private void DrawHitboxColorRow(string label, Func<int> getter, Action<int> setter, string popupId, string tooltip) {
        NumericsVector3 color = RgbToVector(getter());
        const float colorWidth = 168f;
        DrawPopupRowLabel(label, CalculatePopupLabelWidth(colorWidth));
        ImGui.PushItemWidth(colorWidth);
        if (ImGui.ColorEdit3("##" + label + popupId, ref color, ImGuiColorEditFlags.DisplayHex | ImGuiColorEditFlags.InputRGB)) {
            int next = VectorToRgb(color);
            setter(next);
            AkronShowcaseMarkers.MarkPopupDetail(activeOptionsPopupLabel, label, "color", next.ToString("X6", CultureInfo.InvariantCulture));
        }
        ImGui.PopItemWidth();
        DrawPopupTooltip(tooltip, label);
    }

    private static AkronStaminaHudPosition NextStaminaHudPosition(AkronStaminaHudPosition position) {
        return position switch {
            AkronStaminaHudPosition.TopLeft => AkronStaminaHudPosition.TopCenter,
            AkronStaminaHudPosition.TopCenter => AkronStaminaHudPosition.TopRight,
            AkronStaminaHudPosition.TopRight => AkronStaminaHudPosition.BottomRight,
            AkronStaminaHudPosition.BottomRight => AkronStaminaHudPosition.BottomCenter,
            AkronStaminaHudPosition.BottomCenter => AkronStaminaHudPosition.BottomLeft,
            _ => AkronStaminaHudPosition.TopLeft
        };
    }

    private static AkronSpeedNumberMode NextSpeedNumberMode(AkronSpeedNumberMode mode) {
        return mode switch {
            AkronSpeedNumberMode.Total => AkronSpeedNumberMode.Horizontal,
            AkronSpeedNumberMode.Horizontal => AkronSpeedNumberMode.Vertical,
            _ => AkronSpeedNumberMode.Total
        };
    }

    private static string FormatStaminaHudPosition(AkronStaminaHudPosition position) {
        return position switch {
            AkronStaminaHudPosition.TopLeft => "Top Left",
            AkronStaminaHudPosition.TopCenter => "Top Center",
            AkronStaminaHudPosition.TopRight => "Top Right",
            AkronStaminaHudPosition.BottomRight => "Bottom Right",
            AkronStaminaHudPosition.BottomCenter => "Bottom Center",
            AkronStaminaHudPosition.BottomLeft => "Bottom Left",
            _ => position.ToString()
        };
    }

    private static string FormatDeathStatsVisibility(AkronDeathStatsVisibility visibility) {
        return visibility switch {
            AkronDeathStatsVisibility.AfterDeath => "After death",
            AkronDeathStatsVisibility.InMenu => "Pause menu",
            AkronDeathStatsVisibility.AfterDeathAndInMenu => "Death + menu",
            AkronDeathStatsVisibility.Always => "Always",
            _ => "Disabled"
        };
    }

    private static AkronDeathStatsVisibility NextDeathStatsVisibility(AkronDeathStatsVisibility visibility) {
        return visibility switch {
            AkronDeathStatsVisibility.Disabled => AkronDeathStatsVisibility.AfterDeath,
            AkronDeathStatsVisibility.AfterDeath => AkronDeathStatsVisibility.InMenu,
            AkronDeathStatsVisibility.InMenu => AkronDeathStatsVisibility.AfterDeathAndInMenu,
            AkronDeathStatsVisibility.AfterDeathAndInMenu => AkronDeathStatsVisibility.Always,
            _ => AkronDeathStatsVisibility.Disabled
        };
    }

    private static AkronRoomStatTimerFreezeMode NextRoomStatTimerFreezeMode(AkronRoomStatTimerFreezeMode mode, int delta) {
        AkronRoomStatTimerFreezeMode[] modes = {
            AkronRoomStatTimerFreezeMode.Never,
            AkronRoomStatTimerFreezeMode.Paused,
            AkronRoomStatTimerFreezeMode.Inactive,
            AkronRoomStatTimerFreezeMode.Cutscene,
            AkronRoomStatTimerFreezeMode.PausedOrInactive,
            AkronRoomStatTimerFreezeMode.PausedInactiveOrCutscene
        };
        return CycleEnum(mode, modes, delta);
    }

    private static AkronPreventDownDashRedirectMode NextPreventDownDashRedirectMode(AkronPreventDownDashRedirectMode mode, int delta) {
        AkronPreventDownDashRedirectMode[] modes = {
            AkronPreventDownDashRedirectMode.Normal,
            AkronPreventDownDashRedirectMode.Diagonal
        };
        return CycleEnum(mode, modes, delta);
    }

    private static GrabModes NextGrabMode(GrabModes mode, int delta) {
        GrabModes[] modes = {
            GrabModes.Hold,
            GrabModes.Toggle,
            GrabModes.Invert
        };
        return CycleEnum(mode, modes, delta);
    }

    private static T CycleEnum<T>(T current, IReadOnlyList<T> values, int delta) {
        int index = -1;
        for (int i = 0; i < values.Count; i++) {
            if (EqualityComparer<T>.Default.Equals(values[i], current)) {
                index = i;
                break;
            }
        }

        if (index < 0) {
            index = 0;
        }

        int next = (index + delta) % values.Count;
        if (next < 0) {
            next += values.Count;
        }

        return values[next];
    }

    private static AkronTrailVisibility NextTrailVisibility(AkronTrailVisibility visibility) {
        return visibility switch {
            AkronTrailVisibility.Vanilla => AkronTrailVisibility.Hidden,
            AkronTrailVisibility.Hidden => AkronTrailVisibility.Always,
            _ => AkronTrailVisibility.Vanilla
        };
    }

    private static string FormatMadelineEffectSyncMode(AkronMadelineEffectSyncMode mode) {
        return AkronModuleSettings.NormalizeMadelineEffectSyncMode(mode) == AkronMadelineEffectSyncMode.MatchHair
            ? "Match hair"
            : "Off";
    }

    private static AkronAudioSpeedPolicy NextAudioSpeedPolicy(AkronAudioSpeedPolicy policy) {
        return policy switch {
            AkronAudioSpeedPolicy.Normal => AkronAudioSpeedPolicy.SyncTimescale,
            AkronAudioSpeedPolicy.SyncTimescale => AkronAudioSpeedPolicy.Independent,
            _ => AkronAudioSpeedPolicy.Normal
        };
    }

    private static AkronPitchPolicy NextPitchPolicy(AkronPitchPolicy policy) {
        return policy switch {
            AkronPitchPolicy.Preserve => AkronPitchPolicy.FollowSpeed,
            AkronPitchPolicy.FollowSpeed => AkronPitchPolicy.Independent,
            _ => AkronPitchPolicy.Preserve
        };
    }

    private static AkronFrameIncreaseMethod NextFrameIncreaseMethod(AkronFrameIncreaseMethod method) {
        return method == AkronFrameIncreaseMethod.Interval ? AkronFrameIncreaseMethod.Dynamic : AkronFrameIncreaseMethod.Interval;
    }

    private static AkronCameraSmoothingMode NextCameraSmoothing(AkronCameraSmoothingMode mode) {
        return mode switch {
            AkronCameraSmoothingMode.Fancy => AkronCameraSmoothingMode.Fast,
            AkronCameraSmoothingMode.Fast => AkronCameraSmoothingMode.Off,
            _ => AkronCameraSmoothingMode.Fancy
        };
    }

    private static AkronObjectSmoothingMode NextObjectSmoothing(AkronObjectSmoothingMode mode) {
        return mode == AkronObjectSmoothingMode.Extrapolate ? AkronObjectSmoothingMode.Interpolate : AkronObjectSmoothingMode.Extrapolate;
    }

    private static string FormatCameraSmoothing(AkronCameraSmoothingMode mode) {
        return mode switch {
            AkronCameraSmoothingMode.Fancy => "Fancy",
            AkronCameraSmoothingMode.Fast => "Fast",
            _ => "Off"
        };
    }

    private static IndicatorCorner NextIndicatorCorner(IndicatorCorner corner) {
        return corner switch {
            IndicatorCorner.TopLeft => IndicatorCorner.TopRight,
            IndicatorCorner.TopRight => IndicatorCorner.BottomRight,
            IndicatorCorner.BottomRight => IndicatorCorner.BottomLeft,
            _ => IndicatorCorner.TopLeft
        };
    }

    private static AkronHudAnchor NextHudAnchor(AkronHudAnchor anchor) {
        return anchor switch {
            AkronHudAnchor.TopLeft => AkronHudAnchor.TopCenter,
            AkronHudAnchor.TopCenter => AkronHudAnchor.TopRight,
            AkronHudAnchor.TopRight => AkronHudAnchor.MiddleLeft,
            AkronHudAnchor.MiddleLeft => AkronHudAnchor.Center,
            AkronHudAnchor.Center => AkronHudAnchor.MiddleRight,
            AkronHudAnchor.MiddleRight => AkronHudAnchor.BottomLeft,
            AkronHudAnchor.BottomLeft => AkronHudAnchor.BottomCenter,
            AkronHudAnchor.BottomCenter => AkronHudAnchor.BottomRight,
            _ => AkronHudAnchor.TopLeft
        };
    }

    private static AkronLabelEventMode NextLabelEventMode(AkronLabelEventMode mode) {
        return mode switch {
            AkronLabelEventMode.Always => AkronLabelEventMode.OnDeath,
            AkronLabelEventMode.OnDeath => AkronLabelEventMode.OnButtonHold,
            AkronLabelEventMode.OnButtonHold => AkronLabelEventMode.OnNoclipDeath,
            _ => AkronLabelEventMode.Always
        };
    }

    private static AkronLabelTextAlignment NextLabelTextAlignment(AkronLabelTextAlignment alignment) {
        return alignment switch {
            AkronLabelTextAlignment.Left => AkronLabelTextAlignment.Center,
            AkronLabelTextAlignment.Center => AkronLabelTextAlignment.Right,
            _ => AkronLabelTextAlignment.Left
        };
    }

    private static AkronLabelFontTheme NextLabelFontTheme(AkronLabelFontTheme font) {
        return font switch {
            AkronLabelFontTheme.Tiny => AkronLabelFontTheme.Small,
            AkronLabelFontTheme.Small => AkronLabelFontTheme.Default,
            AkronLabelFontTheme.Default => AkronLabelFontTheme.Large,
            AkronLabelFontTheme.Large => AkronLabelFontTheme.Huge,
            _ => AkronLabelFontTheme.Tiny
        };
    }

    private static AkronHudCheatIndicatorStyle NextHudCheatIndicatorStyle(AkronHudCheatIndicatorStyle style) {
        return style == AkronHudCheatIndicatorStyle.Text
            ? AkronHudCheatIndicatorStyle.Dot
            : AkronHudCheatIndicatorStyle.Text;
    }

    private static NumericsVector3 RgbToVector(int rgb) {
        return new NumericsVector3(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f);
    }

    private static int VectorToRgb(NumericsVector3 color) {
        int red = Calc.Clamp((int) Math.Round(color.X * 255f), 0, 255);
        int green = Calc.Clamp((int) Math.Round(color.Y * 255f), 0, 255);
        int blue = Calc.Clamp((int) Math.Round(color.Z * 255f), 0, 255);
        return red << 16 | green << 8 | blue;
    }

    private void DrawFloatStepperRow(
        string label,
        Func<float> getter,
        Action<float> setter,
        float decrement,
        float increment,
        float minimum,
        float maximum,
        string format,
        string popupId,
        string tooltip) {
        const float valueWidth = 50f;
        float controlsWidth = PopupStepperButtonWidth * 2f + valueWidth + ImGui.CalcTextSize("x").X + ImGui.GetStyle().ItemSpacing.X * 3f + 2f;
        DrawPopupRowLabel(label, CalculatePopupLabelWidth(controlsWidth));
        if (ImGui.Button("-##" + label + popupId, new NumericsVector2(PopupStepperButtonWidth, 0f))) {
            float next = Calc.Clamp(getter() + decrement, minimum, maximum);
            setter(next);
            AkronShowcaseMarkers.MarkPopupDetail(activeOptionsPopupLabel, label, "number", next.ToString(CultureInfo.InvariantCulture));
            MarkValueEditFreeze();
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("x");
        ImGui.SameLine(0f, 2f);
        float value = getter();
        ImGui.PushItemWidth(valueWidth);
        if (ImGui.InputFloat("##" + label + popupId, ref value, 0f, 0f, format)) {
            float next = Calc.Clamp(value, minimum, maximum);
            setter(next);
            AkronShowcaseMarkers.MarkPopupDetail(activeOptionsPopupLabel, label, "number", next.ToString(CultureInfo.InvariantCulture));
            MarkValueEditFreeze();
        }
        if (ImGui.IsItemActive()) {
            MarkValueEditFreeze();
        }
        ImGui.PopItemWidth();
        DrawPopupTooltip(tooltip, label);
        ImGui.SameLine();
        if (ImGui.Button("+##" + label + popupId, new NumericsVector2(PopupStepperButtonWidth, 0f))) {
            float next = Calc.Clamp(getter() + increment, minimum, maximum);
            setter(next);
            AkronShowcaseMarkers.MarkPopupDetail(activeOptionsPopupLabel, label, "number", next.ToString(CultureInfo.InvariantCulture));
            MarkValueEditFreeze();
        }
        DrawPopupTooltip(tooltip, label);
    }

    private void DrawFloatValueRow(
        string label,
        Func<float> getter,
        Action<float> setter,
        float decrement,
        float increment,
        float minimum,
        float maximum,
        string format,
        string popupId,
        string tooltip) {
        const float valueWidth = 58f;
        float controlsWidth = PopupStepperButtonWidth * 2f + valueWidth + ImGui.GetStyle().ItemSpacing.X * 2f;
        DrawPopupRowLabel(label, CalculatePopupLabelWidth(controlsWidth));
        if (ImGui.Button("-##" + label + popupId, new NumericsVector2(PopupStepperButtonWidth, 0f))) {
            float next = Calc.Clamp(getter() + decrement, minimum, maximum);
            setter(next);
            AkronShowcaseMarkers.MarkPopupDetail(activeOptionsPopupLabel, label, "number", next.ToString(CultureInfo.InvariantCulture));
            MarkValueEditFreeze();
        }
        ImGui.SameLine();
        float value = getter();
        ImGui.PushItemWidth(valueWidth);
        if (ImGui.InputFloat("##" + label + popupId, ref value, 0f, 0f, format)) {
            float next = Calc.Clamp(value, minimum, maximum);
            setter(next);
            AkronShowcaseMarkers.MarkPopupDetail(activeOptionsPopupLabel, label, "number", next.ToString(CultureInfo.InvariantCulture));
            MarkValueEditFreeze();
        }
        if (ImGui.IsItemActive()) {
            MarkValueEditFreeze();
        }
        ImGui.PopItemWidth();
        DrawPopupTooltip(tooltip, label);
        ImGui.SameLine();
        if (ImGui.Button("+##" + label + popupId, new NumericsVector2(PopupStepperButtonWidth, 0f))) {
            float next = Calc.Clamp(getter() + increment, minimum, maximum);
            setter(next);
            AkronShowcaseMarkers.MarkPopupDetail(activeOptionsPopupLabel, label, "number", next.ToString(CultureInfo.InvariantCulture));
            MarkValueEditFreeze();
        }
        DrawPopupTooltip(tooltip, label);
    }

    private void DrawIntStepperRow(
        string label,
        Func<int> getter,
        Action<int> setter,
        int decrement,
        int increment,
        int minimum,
        int maximum,
        string popupId,
        string tooltip) {
        const float valueWidth = 58f;
        float controlsWidth = PopupStepperButtonWidth * 2f + valueWidth + ImGui.GetStyle().ItemSpacing.X * 2f;
        DrawPopupRowLabel(label, CalculatePopupLabelWidth(controlsWidth));
        if (ImGui.Button("-##" + label + popupId, new NumericsVector2(PopupStepperButtonWidth, 0f))) {
            int next = Calc.Clamp(getter() + decrement, minimum, maximum);
            setter(next);
            AkronShowcaseMarkers.MarkPopupDetail(activeOptionsPopupLabel, label, "number", next.ToString(CultureInfo.InvariantCulture));
            MarkValueEditFreeze();
        }
        ImGui.SameLine();
        int value = getter();
        ImGui.PushItemWidth(valueWidth);
        if (ImGui.InputInt("##" + label + popupId, ref value, 0, 0)) {
            int next = Calc.Clamp(value, minimum, maximum);
            setter(next);
            AkronShowcaseMarkers.MarkPopupDetail(activeOptionsPopupLabel, label, "number", next.ToString(CultureInfo.InvariantCulture));
            MarkValueEditFreeze();
        }
        if (ImGui.IsItemActive()) {
            MarkValueEditFreeze();
        }
        ImGui.PopItemWidth();
        DrawPopupTooltip(tooltip, label);
        ImGui.SameLine();
        if (ImGui.Button("+##" + label + popupId, new NumericsVector2(PopupStepperButtonWidth, 0f))) {
            int next = Calc.Clamp(getter() + increment, minimum, maximum);
            setter(next);
            AkronShowcaseMarkers.MarkPopupDetail(activeOptionsPopupLabel, label, "number", next.ToString(CultureInfo.InvariantCulture));
            MarkValueEditFreeze();
        }
        DrawPopupTooltip(tooltip, label);
    }

    private void MarkValueEditFreeze() {
        valueEditFreezeFrames = Math.Max(valueEditFreezeFrames, 2);
        SearchOwnsGameplayInputThisFrame = true;
    }

    private void DrawPopupTooltip(string text, string suboptionLabel = null) {
        if (!string.IsNullOrWhiteSpace(text) && ImGui.IsItemHovered(ImGuiHoveredFlags.DelayNormal)) {
            DrawImGuiItemTooltip(FormatPopupTooltipText(text, suboptionLabel));
        }
    }

    private static string FormatPopupTooltipText(string text, string suboptionLabel) {
        string classification = DescribePopupTooltipClassification(suboptionLabel);
        return string.IsNullOrWhiteSpace(classification)
            ? text
            : text + "\n\nClassification: " + classification;
    }

    private static string DescribePopupTooltipClassification(string suboptionLabel) {
        if (string.IsNullOrWhiteSpace(activeOptionsPopupLabel)) {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(suboptionLabel) &&
            AkronFeatureRegistry.TryClassifyUiSuboption(activeOptionsPopupLabel, suboptionLabel, out AkronStatus suboptionStatus)) {
            return AkronModuleSettings.FormatStatus(suboptionStatus);
        }

        return TryClassifyOverlayUiLabel(activeOptionsPopupLabel, out AkronStatus parentStatus)
            ? AkronModuleSettings.FormatStatus(parentStatus)
            : string.Empty;
    }

    private static bool TryClassifyOverlayUiLabel(string label, out AkronStatus status) {
        if (AkronFeatureRegistry.TryClassifyUiLabel(label, out status)) {
            return true;
        }

        if (IsSoundVolumeEntryLabel(label)) {
            status = AkronStatus.RegularClean;
            return true;
        }

        if (IsExtendedVariantEntryLabel(label)) {
            status = AkronStatus.Cheat;
            return true;
        }

        status = default;
        return false;
    }

    private void DrawImGuiItemTooltip(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return;
        }

        NumericsVector2 min = ImGui.GetItemRectMin();
        NumericsVector2 max = ImGui.GetItemRectMax();
        pendingImGuiTextTooltip = text;
        pendingImGuiTextTooltipAnchor = RectCeiling(min.X, min.Y, Math.Max(1f, max.X - min.X), Math.Max(1f, max.Y - min.Y));
    }

    private void DrawPendingImGuiItemTooltip() {
        if (string.IsNullOrWhiteSpace(pendingImGuiTextTooltip) || pendingImGuiTextTooltipAnchor == Rectangle.Empty) {
            return;
        }

        string text = pendingImGuiTextTooltip;
        string tooltipKey = "text\n" + text;
        NumericsVector2 cachedSize = imguiTooltipSizes.TryGetValue(tooltipKey, out NumericsVector2 size)
            ? size
            : new NumericsVector2(TooltipMaxWidth, 160f);
        NumericsVector2 actualSize = DrawAnchoredTooltipWindow(pendingImGuiTextTooltipAnchor, cachedSize, () => {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + GetTooltipWrapWidth());
            ImGui.TextWrapped(text);
            ImGui.PopTextWrapPos();
        });
        imguiTooltipSizes[tooltipKey] = actualSize;
    }

}
