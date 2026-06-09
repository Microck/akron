using System;
using System.Globalization;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private static bool IsSettingsOnlyOptionsRow(string label) {
        return string.Equals(label, "Berry Obtain Options", StringComparison.OrdinalIgnoreCase) ||
               IsRecorderSettingsGroupLabel(label) ||
               IsRecorderTextOptionsLabel(label);
    }

    private static bool IsUtilityButtonTab(string tab) {
        return string.Equals(tab, "Sound", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tab, "Bypass", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tab, "Labels", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tab, "StartPos", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tab, "Creator", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tab, "Shortcuts", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tab, "Keybinds", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tab, "Interface", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tab, "Internal Recorder", StringComparison.OrdinalIgnoreCase);
    }

    private static void DrawOptionsTriangle(Rectangle rect, Color color) {
        Rectangle buttonRect = GetOptionsButtonRect(rect);
        int top = (int) Math.Round(rect.Y + 4.5f);
        int bottom = (int) Math.Round(rect.Bottom - 4.5f);
        int side = Math.Max(8, bottom - top);
        int right = (int) Math.Round(buttonRect.Right - 4.5f);
        for (int row = 0; row < side; row++) {
            int width = row + 1;
            Draw.Rect(right - width + 1, top + row, width, 1, color * 0.96f);
        }
    }

    private void RenderOptionsPopup() {
        openOptionsPopupRect = Rectangle.Empty;
        openOptionsMinusRect = Rectangle.Empty;
        openOptionsPlusRect = Rectangle.Empty;

        if (string.IsNullOrWhiteSpace(openOptionsLabel)) {
            return;
        }

        ActionLayout anchor = lastVisibleActionRows.FirstOrDefault(action => string.Equals(action.Entry.Label, openOptionsLabel, StringComparison.OrdinalIgnoreCase));
        if (anchor == null || !HasOptionsPopup(anchor.Entry.Label)) {
            return;
        }

        const float width = 240f;
        const float height = 34f;
        (float x, float y) = CalculateAnchoredPopupPosition(
            anchor.Rect.X,
            anchor.Rect.Y - 4f,
            anchor.Rect.Width,
            width,
            height,
            ScreenWidth,
            ScreenHeight);

        openOptionsPopupRect = RectCeiling(x, y, width, height);
        float opacity = AkronModuleSettings.ClampOverlayOpacity(AkronModule.Settings.OverlayOpacity) / 100f;
        Draw.Rect(openOptionsPopupRect, AkronWindowBackground * opacity);
        Draw.HollowRect(
            openOptionsPopupRect.X,
            openOptionsPopupRect.Y,
            openOptionsPopupRect.Width,
            openOptionsPopupRect.Height,
            AkronPopupOutline);

        if (HasStepperPopup(openOptionsLabel)) {
            openOptionsMinusRect = Rect(openOptionsPopupRect.X + 4f, openOptionsPopupRect.Y + 4f, 34f, 26f);
            openOptionsPlusRect = Rect(openOptionsPopupRect.Right - 38f, openOptionsPopupRect.Y + 4f, 34f, 26f);

            Draw.Rect(openOptionsMinusRect, AkronFrameBackground * 0.28f);
            Draw.Rect(openOptionsPlusRect, AkronFrameBackground * 0.28f);

            DrawMenuText("-", new Vector2(openOptionsMinusRect.X + 13f, openOptionsMinusRect.Y + 1f), AkronRowFontSize, Color.White);
            string value = DescribeOptionsPopupValue(openOptionsLabel);
            float valueWidth = MeasureMenuText(value, AkronSmallFontSize).X;
            DrawMenuText(value, new Vector2(openOptionsPopupRect.Center.X - valueWidth / 2f, openOptionsPopupRect.Y + 5f), AkronSmallFontSize, Color.White);
            DrawMenuText("+", new Vector2(openOptionsPlusRect.X + 11f, openOptionsPlusRect.Y + 1f), AkronRowFontSize, Color.White);
        }
    }

    private bool TryHandleOptionsPopupClick(Vector2 mouse) {
        if (string.IsNullOrWhiteSpace(openOptionsLabel)) {
            return false;
        }

        if (HasStepperPopup(openOptionsLabel)) {
            if (Contains(openOptionsMinusRect, mouse)) {
                ApplyOptionsPopupDelta(openOptionsLabel, -1);
                return true;
            }
            if (Contains(openOptionsPlusRect, mouse)) {
                ApplyOptionsPopupDelta(openOptionsLabel, 1);
                return true;
            }
        }

        return Contains(openOptionsPopupRect, mouse);
    }

    private bool IsOptionsClick(ActionLayout action, Vector2 mouse) {
        return action != null &&
               action.Entry.HasOptionsPopup &&
               Contains(GetOptionsButtonRect(action.Rect), mouse);
    }

    private void ToggleOptionsPopup(string label) {
        if (!HasOptionsPopup(label)) {
            return;
        }

        openOptionsLabel = IsOptionsPopupOpen(label) ? string.Empty : label;
    }

    private void OpenOptionsPopup(string label) {
        if (!string.IsNullOrWhiteSpace(label)) {
            openOptionsLabel = label;
        }
    }

    private void CloseOptionsPopup() {
        openOptionsLabel = string.Empty;
    }

    private bool IsOptionsPopupOpen(string label) {
        return string.Equals(openOptionsLabel, label, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasOptionsPopup(string label) {
        return string.Equals(label, "Overlay Appearance", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Theme", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Keybinds", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Export Profile", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Import Profile", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Confirm Actions", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "StartPos Snapshot Slot", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Grab Mode", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Noclip", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Hazard Accuracy", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Air Jumps", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Core Mode", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Set Inventory", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Dream State", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Ground Refills", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Frame Stepper", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Respawn Time", StringComparison.OrdinalIgnoreCase) ||
               IsPauseTimerLabel(label) ||
               string.Equals(label, "Fast Lookout", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Lag Pauser", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "No Death Wipe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Allow Low Volume", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Audio Speed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Pitch Shift", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "FPS Bypass", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Safe Mode", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Screenshake", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Screen Tint", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Free Camera", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Camera Offset", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Cursor Zoom", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Golden Start", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Golden Transparency", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Light Level", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Bloom Level", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Show Trajectory", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Control Display", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Show Taps", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Visible", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Player Overlap", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Room", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Status", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Toasts", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Attempts", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Inputs per second", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Death Stats", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Room Timer", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Room Stat Tracker", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Custom", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Custom HUD Labels", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Refill Clarity", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Room Capture", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Map Capture", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Autosave", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Deload Spinners", StringComparison.OrdinalIgnoreCase) ||
               IsSoundVolumeEntryLabel(label) ||
               string.Equals(label, "Dash Stats", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Jump Stats", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Audio Splitter", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Codec", StringComparison.OrdinalIgnoreCase) ||
               IsRecorderSettingsGroupLabel(label) ||
               IsRecorderTextOptionsLabel(label) ||
               string.Equals(label, "Cheat Indicator", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Berry Obtain Options", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "StartPos HUD", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "StartPos Switcher", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "StartPos", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Restore", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Last Result", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Triggers", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Retention", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Input History", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Stamina Bar", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Dash Bar", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Dash Count", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Dash Number", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Speed Number", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Prevent Down Dash Redirects", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Auto Kill", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Auto Deafen", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Transition Speed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Madeline Colors", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Madeline Hair Length", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Madeline Effect Sync", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Trail Visibility", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Custom Trail", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Show Hitboxes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Show Hitbox Trail", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Show Hitboxes On Death", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Extended Variants Randomizer", StringComparison.OrdinalIgnoreCase) ||
               HasExtendedVariantOptionsPopup(label);
    }

    private static bool HasStepperPopup(string label) {
        return HasOptionsPopup(label) &&
               !IsRecorderTextOptionsLabel(label) &&
               !IsRecorderSettingsGroupLabel(label) &&
               !string.Equals(label, "Codec", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(label, "Golden Start", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecorderSettingsGroupLabel(string label) {
        return string.Equals(label, "Replay Settings", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Output", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Audio", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Clip Triggers", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecorderTextOptionsLabel(string label) {
        return string.Equals(label, "Output Folder", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Filename Template", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Colorspace Args", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPauseTimerLabel(string label) {
        return string.Equals(label, "Pause Timer", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(label, "Pause Countdown", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSoundVolumeEntryLabel(string label) {
        return TryGetSoundDefinitionByLabel(label, out _);
    }

    private static bool TryGetSoundDefinitionByLabel(string label, out AkronEarAid.SoundDefinition sound) {
        sound = AkronEarAid.Sounds.FirstOrDefault(candidate => string.Equals(candidate.Label, label, StringComparison.OrdinalIgnoreCase));
        return sound != null;
    }

    private static string DescribeOptionsPopupValue(string label) {
        if (string.Equals(label, "Timescale", StringComparison.OrdinalIgnoreCase)) {
            AkronModuleSession session = AkronModule.Session;
            return session == null ? "Unavailable" : session.TimescaleMultiplier.ToString("0.0x");
        }

        if (string.Equals(label, "StartPos Snapshot Slot", StringComparison.OrdinalIgnoreCase)) {
            return "Slot " + AkronModule.Settings.ActiveSavestateSlot;
        }

        if (string.Equals(label, "Overlay Appearance", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(label, "Theme", StringComparison.OrdinalIgnoreCase)) {
            return AkronOverlayThemes.CurrentDisplayName() + " / " + AkronModule.Settings.OverlayScale + "%";
        }

        if (string.Equals(label, "Keybinds", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.MenuBindingsInGameOnly ? "In-game only" : "Global";
        }

        if (string.Equals(label, "Confirm Actions", StringComparison.OrdinalIgnoreCase)) {
            return DescribeConfirmActionsValue();
        }

        if (string.Equals(label, "Grab Mode", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.GrabModeOverrideEnabled ? "On" : "Off";
        }

        if (string.Equals(label, "Noclip", StringComparison.OrdinalIgnoreCase)) {
            return FormatNoclipMultiplier(AkronModule.Settings.NoclipSpeed);
        }

        if (string.Equals(label, "Air Jumps", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.JumpHackInfinite
                ? "Infinite"
                : AkronModule.Settings.JumpHackExtraJumps + " extra";
        }

        if (string.Equals(label, "Ground Refills", StringComparison.OrdinalIgnoreCase)) {
            if (!AkronModule.Settings.GroundRefillRules) {
                return "Off";
            }

            return (AkronModule.Settings.GroundDashRefill ? "Dash" : "No dash") + " / " +
                   (AkronModule.Settings.GroundStaminaRefill ? "Stamina" : "No stamina");
        }

        if (string.Equals(label, "Frame Stepper", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.StepHoldIntervalFrames + "f";
        }

        if (string.Equals(label, "Respawn Time", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.RespawnTimeSeconds.ToString("0.0s");
        }

        if (IsPauseTimerLabel(label)) {
            return AkronModule.Settings.PauseCountdown
                ? AkronModule.Settings.PauseCountdownSeconds.ToString("0.0s")
                : "Off";
        }

        if (string.Equals(label, "Fast Lookout", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.FastLookout
                ? AkronModule.Settings.FastLookoutMultiplier + "x"
                : "Off";
        }

        if (string.Equals(label, "Lag Pauser", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.LagPauser
                ? AkronModule.Settings.LagPauserThresholdMs + " ms"
                : "Off";
        }

        if (string.Equals(label, "Golden Start", StringComparison.OrdinalIgnoreCase)) {
            return Engine.Scene is Level level ? AkronActions.DescribeGoldenStartHelper(level) : "No level";
        }

        if (string.Equals(label, "Golden Transparency", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.GoldenTransparency
                ? AkronModule.Settings.GoldenTransparencyOpacity + "%"
                : "Off";
        }

        if (string.Equals(label, "No Death Wipe", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.NoDeathWipe ? FormatNoDeathWipeMode(AkronModule.Settings.NoDeathWipeMode) : "Off";
        }

        if (string.Equals(label, "Allow Low Volume", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.LowVolumeMusic.ToString("0.0") + "/" + AkronModule.Settings.LowVolumeSfx.ToString("0.0");
        }

        if (string.Equals(label, "Audio Speed", StringComparison.OrdinalIgnoreCase)) {
            return AkronRuntimeOptions.DescribeAudioSpeed();
        }

        if (string.Equals(label, "Pitch Shift", StringComparison.OrdinalIgnoreCase)) {
            return AkronRuntimeOptions.DescribePitchShift();
        }

        if (string.Equals(label, "FPS Bypass", StringComparison.OrdinalIgnoreCase)) {
            return AkronRuntimeOptions.DescribeFpsBypass();
        }

        if (string.Equals(label, "TPS Bypass", StringComparison.OrdinalIgnoreCase)) {
            return AkronRuntimeOptions.DescribeTpsBypass();
        }

        if (string.Equals(label, "Safe Mode", StringComparison.OrdinalIgnoreCase)) {
            return AkronRuntimeOptions.DescribeSafeModeStats();
        }

        if (string.Equals(label, "Screenshake", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.Screenshake
                ? AkronModule.Settings.ScreenshakeIntensity + "%"
                : "Off";
        }

        if (string.Equals(label, "Screen Tint", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.ScreenTint
                ? AkronModule.Settings.ScreenTintOpacity + "%"
                : "Off";
        }

        if (string.Equals(label, "Free Camera", StringComparison.OrdinalIgnoreCase)) {
            return AkronRuntimeOptions.DescribeFreeCamera();
        }

        if (string.Equals(label, "Cursor Zoom", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.CursorZoom
                ? AkronModule.Settings.CursorZoomPercent + "%"
                : "Off";
        }

        if (string.Equals(label, "Light Level", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.LightLevel ? "On" : "Off";
        }

        if (string.Equals(label, "Bloom Level", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.BloomLevel ? "On" : "Off";
        }

        if (string.Equals(label, "Show Trajectory", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.ShowTrajectory
                ? (AkronModule.Settings.ShowTrajectoryMapAware ? "Map / " : "Simple / ") + AkronModule.Settings.ShowTrajectoryFrames + "f / " + AkronModule.Settings.ShowTrajectoryOpacity + "%"
                : "Off";
        }

        if (string.Equals(label, "Control Display", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(label, "Show Taps", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.ShowTaps
                ? AkronInputBoard.Describe(AkronModule.Settings.InputBoardElements) + " / " + AkronModule.Settings.TapDisplayOpacity + "%"
                : "Off";
        }

        if (string.Equals(label, "Inputs per second", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.InputsPerSecondCounter ? AkronInputHistory.DescribeInputsPerSecond() + " IPS" : "Off";
        }

        if (string.Equals(label, "Visible", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.LabelSystemVisible ? "Visible" : "Hidden";
        }

        if (string.Equals(label, "Player Overlap", StringComparison.OrdinalIgnoreCase)) {
            return !IsLabelPlayerOverlapEnabled()
                ? "Off"
                : FormatLabelPlayerOverlapMode(GetLabelPlayerOverlapMode());
        }

        if (string.Equals(label, "Room", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.RoomLabels ? FormatRgb(AkronModule.Settings.RoomLabelColor) : "Off";
        }

        if (string.Equals(label, "Death Stats", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.DeathStatsWidget ? FormatDeathStatsVisibility(AkronModule.Settings.DeathStatsVisibility) : "Off";
        }

        if (string.Equals(label, "Room Timer", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.RoomTimerWidget ? FormatRgb(AkronModule.Settings.RoomTimerColor) : "Off";
        }

        if (string.Equals(label, "Room Stat Tracker", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.RoomStatTracker ? FormatRgb(AkronModule.Settings.RoomStatTrackerColor) : "Off";
        }

        if (string.Equals(label, "Attempts", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.TotalAttemptsWidget ? FormatRgb(AkronModule.Settings.TotalAttemptsColor) : "Off";
        }

        if (string.Equals(label, "Status", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.StatusLabelsWidget ? FormatRgb(AkronModule.Settings.StatusLabelsColor) : "Off";
        }

        if (string.Equals(label, "Toasts", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.ToastLabels ? AkronModule.Settings.ToastLabelAnchor + " / " + FormatRgb(AkronModule.Settings.ToastLabelColor) : "Off";
        }

        if (string.Equals(label, "StartPos Switcher", StringComparison.OrdinalIgnoreCase)) {
            return DescribeStartPosSwitcherBindings();
        }

        if (string.Equals(label, "StartPos HUD", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.StartPosShowLabel ? FormatRgb(AkronModule.Settings.StartPosLabelColor) : "Off";
        }

        if (string.Equals(label, "Place StartPos", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.StartPosMousePlacement ? "Placement on" : "Off";
        }

        if (string.Equals(label, "Custom", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(label, "Custom HUD Labels", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.CustomHudLabels ? AkronCustomHudLabels.DescribeActive() : "Off";
        }

        if (string.Equals(label, "Refill Clarity", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.RefillClarity ? AkronModule.Settings.RefillClarityOpacity + "%" : "Off";
        }

        if (string.Equals(label, "Cheat Indicator", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.HudCheatIndicator ? AkronModule.Settings.HudCheatIndicatorAnchor.ToString() : "Off";
        }

        if (string.Equals(label, "Berry Obtain Options", StringComparison.OrdinalIgnoreCase)) {
            return AkronActions.DescribeBerryObtainOptions();
        }

        if (string.Equals(label, "StartPos", StringComparison.OrdinalIgnoreCase)) {
            return Engine.Scene is Level level
                ? AkronActions.DescribeStartPosIndex(level) + " | Slot " + AkronModule.Settings.ActiveStartPosSlot
                : "Slot " + AkronModule.Settings.ActiveStartPosSlot;
        }

        if (string.Equals(label, "Input History", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.InputViewer || AkronModule.Settings.InputHistoryPanel ? "On" : "Off";
        }

        if (string.Equals(label, "Stamina Bar", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.StaminaBar ? "On" : "Off";
        }

        if (string.Equals(label, "Dash Bar", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.DashBar ? "On" : "Off";
        }

        if (string.Equals(label, "Dash Count", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.DashCountOverride
                ? AkronModule.Settings.DashCountOverrideValue + " max"
                : "Off";
        }

        if (string.Equals(label, "Dash Number", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.DashNumber
                ? AkronModule.Settings.DashNumberOffsetY + "y / " + AkronModule.Settings.DashNumberOpacity + "%"
                : "Off";
        }

        if (string.Equals(label, "Dash Stats", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.DashCountStats ? AkronModule.Settings.DashCountStatsMode.ToString() : "Off";
        }

        if (string.Equals(label, "Jump Stats", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.JumpCount ? AkronModule.Settings.JumpCountMode.ToString() : "Off";
        }

        if (string.Equals(label, "Room Capture", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(label, "Map Capture", StringComparison.OrdinalIgnoreCase)) {
            return AkronScreenshotScanner.Describe();
        }

        if (string.Equals(label, "Autosave", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.Autosave ? AkronModule.Settings.AutosaveIntervalSeconds + "s" : "Off";
        }

        if (string.Equals(label, "Deload Spinners", StringComparison.OrdinalIgnoreCase)) {
            return AkronDeloadSimulator.Describe();
        }

        if (string.Equals(label, "Audio Splitter", StringComparison.OrdinalIgnoreCase)) {
            return AkronAudioSplitter.Describe();
        }

        if (TryGetSoundDefinitionByLabel(label, out AkronEarAid.SoundDefinition sound)) {
            return AkronEarAid.OverrideEnabled(sound.Key) ? "On" : "Off";
        }

        if (string.Equals(label, "Speed Number", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.SpeedNumber
                ? AkronModule.Settings.SpeedNumberMode + " / " + AkronModule.Settings.SpeedNumberOpacity + "%"
                : "Off";
        }

        if (string.Equals(label, "Prevent Down Dash Redirects", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.PreventDownDashRedirectsEnabled ? "On" : "Off";
        }

        if (string.Equals(label, "Auto Kill", StringComparison.OrdinalIgnoreCase)) {
            int areaCount = AkronModule.GetAutoKillAreas().Count;
            return areaCount > 0 ? areaCount + " areas" : AkronModule.Settings.AutoKillSeconds + "s";
        }

        if (string.Equals(label, "Auto Deafen", StringComparison.OrdinalIgnoreCase)) {
            int areaCount = AkronModule.GetAutoDeafenAreas().Count;
            if (AkronActions.AutoDeafenActive) {
                return "Deafened";
            }

            return areaCount > 0 ? areaCount + " areas / " + AkronActions.DescribeAutoDeafenHotkey() : "No area";
        }

        if (string.Equals(label, "Transition Speed", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.TransitionSpeedMultiplier.ToString("0.0x");
        }

        if (string.Equals(label, "Madeline Colors", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.MadelineColors ? "Custom" : "Off";
        }

        if (string.Equals(label, "Madeline Hair Length", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.MadelineHairLength
                ? AkronModule.Settings.MadelineOneDashHairLength.ToString(CultureInfo.InvariantCulture) + " / " +
                  AkronModule.Settings.MadelineTwoDashHairLength.ToString(CultureInfo.InvariantCulture)
                : "Off";
        }

        if (string.Equals(label, "Madeline Effect Sync", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.MadelineEffectSync ? "Match hair" : "Off";
        }

        if (string.Equals(label, "Trail Visibility", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.TrailVisibility == AkronTrailVisibility.Always
                ? "Always / " + AkronModule.Settings.TrailCuttingRate + "f"
                : AkronModule.Settings.TrailVisibility.ToString();
        }

        if (string.Equals(label, "Custom Trail", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.CustomTrail
                ? AkronModule.Settings.CustomTrailMode + " / " + AkronModule.Settings.CustomTrailOpacity + "%"
                : "Off";
        }

        if (string.Equals(label, "Show Hitboxes", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.HitboxViewer ? "On" : "Off";
        }

        if (string.Equals(label, "Show Hitbox Trail", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.HitboxViewer && AkronModule.Settings.ShowHitboxTrail
                ? AkronModule.Settings.HitboxTrailLength + "f / " + AkronModule.Settings.HitboxTrailOpacity + "%"
                : "Off";
        }

        if (string.Equals(label, "Show Hitboxes On Death", StringComparison.OrdinalIgnoreCase)) {
            return AkronModule.Settings.HitboxShowLastDeath ? "On" : "Off";
        }

        if (string.Equals(label, "Extended Variants Randomizer", StringComparison.OrdinalIgnoreCase)) {
            return AkronExtendedVariants.RandomizerEnabled ? "On" : "Off";
        }

        if (IsExtendedVariantEntryLabel(label)) {
            AkronExtendedVariantOption option = GetExtendedVariantOptionFromLabel(label);
            return option == null ? string.Empty : AkronExtendedVariants.DescribeConfiguredState(option);
        }

        return string.Empty;
    }

    private static string FormatRgb(int rgb) {
        return "#" + AkronModuleSettings.ClampRgb(rgb).ToString("X6", CultureInfo.InvariantCulture);
    }

    private static void ApplyOptionsPopupDelta(string label, int delta) {
        if (string.Equals(label, "Timescale", StringComparison.OrdinalIgnoreCase)) {
            AkronActions.AdjustTimescale(delta * 0.1f);
            return;
        }

        if (string.Equals(label, "StartPos Snapshot Slot", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.SetActiveSavestateSlot(AkronModule.Settings.ActiveSavestateSlot + delta);
            Engine.Scene?.Add(new AkronToast("Active StartPos snapshot slot: " + AkronModule.Settings.ActiveSavestateSlot));
            return;
        }

        if (string.Equals(label, "Grab Mode", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.GrabModeOverrideMode = NextGrabMode(AkronModule.Settings.GrabModeOverrideMode, delta);
            ApplyGrabModeOverrideIfEnabled();
            return;
        }

        if (string.Equals(label, "Noclip", StringComparison.OrdinalIgnoreCase)) {
            AdjustNoclipSpeed(delta * 24);
            return;
        }

        if (string.Equals(label, "Frame Stepper", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.StepHoldIntervalFrames = CycleInt(AkronModule.Settings.StepHoldIntervalFrames + delta, 1, 12);
            Engine.Scene?.Add(new AkronToast("Frame step repeat interval: " + AkronModule.Settings.StepHoldIntervalFrames + "f."));
            return;
        }

        if (string.Equals(label, "Respawn Time", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.RespawnTimeSeconds = AkronModuleSettings.ClampRespawnTimeSeconds(AkronModule.Settings.RespawnTimeSeconds + delta * 0.1f);
            return;
        }

        if (IsPauseTimerLabel(label)) {
            AkronModule.Settings.PauseCountdownSeconds = AkronModuleSettings.ClampPauseCountdownSeconds(AkronModule.Settings.PauseCountdownSeconds + delta * 0.1f);
            return;
        }

        if (string.Equals(label, "Fast Lookout", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.FastLookoutMultiplier = AkronModuleSettings.ClampFastLookoutMultiplier(AkronModule.Settings.FastLookoutMultiplier + delta);
            return;
        }

        if (string.Equals(label, "Lag Pauser", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.LagPauserThresholdMs = AkronModuleSettings.ClampLagPauserThresholdMs(AkronModule.Settings.LagPauserThresholdMs + delta * 25);
            return;
        }

        if (string.Equals(label, "Golden Transparency", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.GoldenTransparencyOpacity = AkronModuleSettings.ClampGoldenTransparencyOpacity(AkronModule.Settings.GoldenTransparencyOpacity + delta * 5);
            return;
        }

        if (string.Equals(label, "Allow Low Volume", StringComparison.OrdinalIgnoreCase)) {
            AkronActions.SetLowVolumeMusic(AkronModule.Settings.LowVolumeMusic + delta * 0.1f);
            AkronActions.SetLowVolumeSfx(AkronModule.Settings.LowVolumeSfx + delta * 0.1f);
            return;
        }

        if (string.Equals(label, "Audio Speed", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.AudioSpeedMultiplier = AkronModuleSettings.ClampAudioMultiplier(AkronModule.Settings.AudioSpeedMultiplier + delta * 0.1f);
            return;
        }

        if (string.Equals(label, "Pitch Shift", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.PitchShiftMultiplier = AkronModuleSettings.ClampAudioMultiplier(AkronModule.Settings.PitchShiftMultiplier + delta * 0.1f);
            return;
        }

        if (string.Equals(label, "FPS Bypass", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.FpsBypassTarget = AkronModuleSettings.ClampFpsTarget(AkronModule.Settings.FpsBypassTarget + delta * 10);
            return;
        }

        if (string.Equals(label, "TPS Bypass", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.TpsBypassTarget = AkronModuleSettings.ClampTpsTarget(AkronModule.Settings.TpsBypassTarget + delta * 10);
            return;
        }

        if (string.Equals(label, "Screenshake", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.ScreenshakeIntensity = AkronModuleSettings.ClampScreenshakeIntensity(AkronModule.Settings.ScreenshakeIntensity + delta * 10);
            return;
        }

        if (string.Equals(label, "Light Level", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.LightLevelPercent = AkronModuleSettings.ClampLightLevelPercent(AkronModule.Settings.LightLevelPercent + delta * 5);
            return;
        }

        if (string.Equals(label, "Bloom Level", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.BloomLevelPercent = AkronModuleSettings.ClampBloomLevelPercent(AkronModule.Settings.BloomLevelPercent + delta * 5);
            return;
        }

        if (string.Equals(label, "Show Trajectory", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.ShowTrajectoryFrames = AkronModuleSettings.ClampShowTrajectoryFrames(AkronModule.Settings.ShowTrajectoryFrames + delta * 10);
            return;
        }

        if (string.Equals(label, "Show Hitbox Trail", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.HitboxTrailLength = AkronModuleSettings.ClampHitboxTrailLength(AkronModule.Settings.HitboxTrailLength + delta * 10);
            return;
        }

        if (string.Equals(label, "Free Camera", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.FreeCameraSpeed = AkronModuleSettings.ClampFreeCameraSpeed(AkronModule.Settings.FreeCameraSpeed + delta * 20);
            return;
        }

        if (string.Equals(label, "Control Display", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(label, "Show Taps", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.TapDisplayScale = AkronModuleSettings.ClampPercent(AkronModule.Settings.TapDisplayScale + delta * 5, 50, 250);
            return;
        }

        if (string.Equals(label, "Inputs per second", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.InputsPerSecondScale = AkronModuleSettings.ClampPercent(AkronModule.Settings.InputsPerSecondScale + delta * 5, 50, 250);
            return;
        }

        if (string.Equals(label, "Death Stats", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.DeathStatsVisibility = NextDeathStatsVisibility(AkronModule.Settings.DeathStatsVisibility);
            return;
        }

        if (string.Equals(label, "Room Stat Tracker", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.RoomStatTimerFreezeMode = NextRoomStatTimerFreezeMode(AkronModule.Settings.RoomStatTimerFreezeMode, delta);
            return;
        }

        if (string.Equals(label, "Prevent Down Dash Redirects", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.PreventDownDashRedirects = NextPreventDownDashRedirectMode(AkronModule.Settings.PreventDownDashRedirects, delta);
            return;
        }

        if (string.Equals(label, "Custom", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(label, "Custom HUD Labels", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.CustomHudLabelIndex = AkronModuleSettings.ClampCustomLabelIndex(
                AkronModule.Settings.CustomHudLabelIndex + delta,
                AkronModule.Settings.CustomHudLabelDefinitions?.Count ?? 0);
            return;
        }

        if (string.Equals(label, "Refill Clarity", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.RefillClarityOpacity = AkronModuleSettings.ClampOpacity(AkronModule.Settings.RefillClarityOpacity + delta * 5);
            return;
        }

        if (string.Equals(label, "Cheat Indicator", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.HudCheatIndicatorAnchor = NextHudAnchor(AkronModule.Settings.HudCheatIndicatorAnchor);
            return;
        }

        if (string.Equals(label, "Berry Obtain Options", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.BerryObtainIncludeRegular = !AkronModule.Settings.BerryObtainIncludeRegular;
            return;
        }

        if (string.Equals(label, "Overlay Appearance", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(label, "Theme", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.OverlayThemePreset = NextOverlayThemePreset(AkronModule.Settings.OverlayThemePreset);
            return;
        }

        if (string.Equals(label, "Keybinds", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.MenuBindingsInGameOnly = !AkronModule.Settings.MenuBindingsInGameOnly;
            return;
        }

        if (TryGetSoundDefinitionByLabel(label, out AkronEarAid.SoundDefinition sound)) {
            AkronEarAid.SetVolume(sound.Key, AkronEarAid.VolumeFor(sound.Key) + delta * 5);
            return;
        }

        if (string.Equals(label, "Confirm Actions", StringComparison.OrdinalIgnoreCase)) {
            bool next = DescribeConfirmActionsValue() == "Off";
            AkronModule.Settings.ConfirmRestart = next;
            AkronModule.Settings.ConfirmReloadRoom = next;
            AkronModule.Settings.ConfirmFullReset = next;
            AkronModule.Settings.ConfirmLoadState = next;
            return;
        }

        if (string.Equals(label, "StartPos", StringComparison.OrdinalIgnoreCase)) {
            if (Engine.Scene is Level level) {
                AkronActions.ShiftStartPos(level, delta);
            } else {
                AkronActions.ShiftStartPosSlot(delta);
            }
            return;
        }

        if (string.Equals(label, "Stamina Bar", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.LowStaminaThreshold = CycleInt(AkronModule.Settings.LowStaminaThreshold + delta, 1, 100);
            Engine.Scene?.Add(new AkronToast("Low stamina threshold: " + AkronModule.Settings.LowStaminaThreshold + "."));
            return;
        }

        if (string.Equals(label, "Dash Count", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.DashCountOverrideValue = AkronModuleSettings.ClampDashCountOverride(AkronModule.Settings.DashCountOverrideValue + delta);
            Engine.Scene?.Add(new AkronToast("Dash count: " + AkronModule.Settings.DashCountOverrideValue + "."));
            return;
        }

        if (string.Equals(label, "Dash Number", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.DashNumberOffsetY = AkronModuleSettings.ClampDashNumberOffsetY(AkronModule.Settings.DashNumberOffsetY + delta);
            return;
        }

        if (string.Equals(label, "Speed Number", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.SpeedNumberOffsetY = AkronModuleSettings.ClampSpeedNumberOffsetY(AkronModule.Settings.SpeedNumberOffsetY + delta);
            return;
        }

        if (string.Equals(label, "Ground Refills", StringComparison.OrdinalIgnoreCase)) {
            if (delta >= 0) {
                AkronModule.Settings.GroundDashRefill = !AkronModule.Settings.GroundDashRefill;
            } else {
                AkronModule.Settings.GroundStaminaRefill = !AkronModule.Settings.GroundStaminaRefill;
            }
            return;
        }

        if (string.Equals(label, "Auto Kill", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.AutoKillSeconds = AkronModuleSettings.ClampAutoKillSeconds(AkronModule.Settings.AutoKillSeconds + delta * 5);
            return;
        }

        if (string.Equals(label, "Auto Deafen", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.AutoDeafenShowArea = !AkronModule.Settings.AutoDeafenShowArea;
            return;
        }

        if (string.Equals(label, "Transition Speed", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.TransitionSpeedMultiplier = AkronModuleSettings.ClampTransitionSpeedMultiplier(AkronModule.Settings.TransitionSpeedMultiplier + delta * 0.1f);
            return;
        }

        if (string.Equals(label, "Extended Variants Randomizer", StringComparison.OrdinalIgnoreCase)) {
            bool next = !AkronExtendedVariants.RandomizerEnabled;
            if (next && !AkronModule.TryUse(AkronFeatureKind.ExtendedVariantMode)) {
                return;
            }

            AkronExtendedVariants.MasterSwitch = true;
            AkronExtendedVariants.RandomizerEnabled = next;
            return;
        }

        if (IsExtendedVariantEntryLabel(label)) {
            AkronExtendedVariantOption option = GetExtendedVariantOptionFromLabel(label);
            if (option == null || !AkronModule.TryUse(AkronFeatureKind.ExtendedVariantMode)) {
                return;
            }

            if (AkronExtendedVariants.TryToggleConfigured(option.Name, out string message)) {
                Engine.Scene?.Add(new AkronToast(message));
            } else {
                Engine.Scene?.Add(new AkronToast(message));
            }
            return;
        }

        if (string.Equals(label, "Madeline Colors", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.MadelineColors = !AkronModule.Settings.MadelineColors;
            return;
        }

        if (string.Equals(label, "Madeline Hair Length", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.MadelineHairLength = !AkronModule.Settings.MadelineHairLength;
            return;
        }

        if (string.Equals(label, "Madeline Effect Sync", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.MadelineEffectSync = !AkronModule.Settings.MadelineEffectSync;
            return;
        }

        if (string.Equals(label, "Trail Visibility", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.TrailVisibility = delta >= 0
                ? NextTrailVisibility(AkronModule.Settings.TrailVisibility)
                : AkronModule.Settings.TrailVisibility == AkronTrailVisibility.Always ? AkronTrailVisibility.Hidden : AkronModule.Settings.TrailVisibility == AkronTrailVisibility.Hidden ? AkronTrailVisibility.Vanilla : AkronTrailVisibility.Always;
            AkronModule.Settings.SetNoTrails(AkronModule.Settings.TrailVisibility == AkronTrailVisibility.Hidden);
            if (AkronModule.Settings.TrailVisibility == AkronTrailVisibility.Always) {
                AkronModule.Settings.TrailCuttingRate = AkronModuleSettings.ClampTrailCuttingRate(AkronModule.Settings.TrailCuttingRate);
            }
            return;
        }

        if (string.Equals(label, "Custom Trail", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.CustomTrailOpacity = AkronModuleSettings.ClampOpacity(AkronModule.Settings.CustomTrailOpacity + delta * 5);
        }
    }

    private static Rectangle GetOptionsButtonRect(Rectangle rect) {
        int width = Math.Max(32, (int) Math.Round(rect.Width * 0.10f));
        return new Rectangle(rect.Right - width, rect.Y, width, rect.Height);
    }

}
