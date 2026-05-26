using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using ImGuiNET;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawHitboxPopupControls(string popupId) {
        bool activeOnly = AkronModule.Settings.HitboxActiveOnly;
        if (ImGui.Checkbox("Active only##" + popupId, ref activeOnly)) {
            AkronModule.Settings.HitboxActiveOnly = activeOnly;
        }
        DrawPopupTooltip("Hide inactive entities from live hitbox rendering.");

        bool hidePlayer = AkronModule.Settings.HitboxHidePlayer;
        if (ImGui.Checkbox("Hide player##" + popupId, ref hidePlayer)) {
            AkronModule.Settings.HitboxHidePlayer = hidePlayer;
        }
        DrawPopupTooltip("Hide the player collider from live hitbox rendering.");

        bool hazards = AkronModule.Settings.HitboxShowHazards;
        if (ImGui.Checkbox("Hazards##" + popupId, ref hazards)) {
            AkronModule.Settings.HitboxShowHazards = hazards;
        }
        DrawPopupTooltip("Draw spike, blade, and death-object hitboxes.");

        bool solids = AkronModule.Settings.HitboxShowSolids;
        if (ImGui.Checkbox("Solids##" + popupId, ref solids)) {
            AkronModule.Settings.HitboxShowSolids = solids;
        }
        DrawPopupTooltip("Draw solid collision boxes.");

        bool triggers = AkronModule.Settings.HitboxShowTriggers;
        if (ImGui.Checkbox("Triggers##" + popupId, ref triggers)) {
            AkronModule.Settings.HitboxShowTriggers = triggers;
        }
        DrawPopupTooltip("Draw trigger areas.");

        if (ImGui.Button("Sync Hitboxes##" + popupId)) {
            AkronEntityInspector.SyncHitboxes();
            AkronModule.Settings.HitboxViewer = true;
        }
        DrawPopupTooltip("Clear cached hitbox draw state and force live hitboxes to rebuild on the next frame.");

        ImGui.Separator();
        DrawHitboxTrailPopupControls(popupId + "_live_hitboxes");

        ImGui.Separator();
        DrawFloatValueRow(
            "Line",
            () => AkronModule.Settings.HitboxLineThickness,
            value => AkronModule.Settings.HitboxLineThickness = AkronModuleSettings.ClampHitboxLineThickness(value),
            -0.5f,
            0.5f,
            1f,
            8f,
            "%.1f",
            popupId,
            "Outline thickness in fifths of a native Celeste pixel. 5.0 is one game pixel.");

        bool blackOutline = AkronModule.Settings.HitboxBlackOutline;
        if (ImGui.Checkbox("Black outline##" + popupId, ref blackOutline)) {
            AkronModule.Settings.HitboxBlackOutline = blackOutline;
        }
        DrawPopupTooltip("Add a black contrast border behind colored hitbox lines.");

        DrawIntStepperRow(
            "Fill %",
            () => AkronModule.Settings.HitboxFillOpacity,
            value => AkronModule.Settings.HitboxFillOpacity = AkronModuleSettings.ClampHitboxFillOpacity(value),
            -5,
            5,
            0,
            100,
            popupId,
            "Interior opacity for drawn hitboxes.");

        DrawHitboxColorRow("Player", () => AkronModule.Settings.HitboxPlayerColor, value => AkronModule.Settings.HitboxPlayerColor = value, popupId, "Madeline/player hitbox color.");
        DrawHitboxColorRow("Solids", () => AkronModule.Settings.HitboxSolidColor, value => AkronModule.Settings.HitboxSolidColor = value, popupId, "Solid collision grid color.");
        DrawHitboxColorRow("Hazards", () => AkronModule.Settings.HitboxHazardColor, value => AkronModule.Settings.HitboxHazardColor = value, popupId, "Spike, blade, and death-object color.");
        DrawHitboxColorRow("Triggers", () => AkronModule.Settings.HitboxTriggerColor, value => AkronModule.Settings.HitboxTriggerColor = value, popupId, "Trigger area color.");
        DrawHitboxColorRow("Other", () => AkronModule.Settings.HitboxOtherColor, value => AkronModule.Settings.HitboxOtherColor = value, popupId, "Fallback color for other collidable entities.");

        if (ImGui.Button("Reset style##" + popupId)) {
            AkronModule.Settings.ResetHitboxStyle();
        }
        DrawPopupTooltip("Restore Akron's default hitbox colors, line thickness, outline, and fill opacity.");
    }

    private void DrawHitboxTrailPopupControls(string popupId) {
        bool showHitboxTrail = AkronModule.Settings.ShowHitboxTrail;
        if (ImGui.Checkbox("Show Hitbox Trail##" + popupId, ref showHitboxTrail)) {
            AkronModule.Settings.ShowHitboxTrail = showHitboxTrail;
            if (showHitboxTrail) {
                AkronModule.Settings.HitboxViewer = true;
            }
        }
        DrawPopupTooltip("Draw recent player collision boxes behind Madeline.");

        DrawIntStepperRow(
            "Length",
            () => AkronModule.Settings.HitboxTrailLength,
            value => AkronModule.Settings.HitboxTrailLength = AkronModuleSettings.ClampHitboxTrailLength(value),
            -1,
            1,
            1,
            240,
            popupId,
            "Number of previous player hitbox positions to keep.");

        DrawIntStepperRow(
            "Trail %",
            () => AkronModule.Settings.HitboxTrailOpacity,
            value => AkronModule.Settings.HitboxTrailOpacity = AkronModuleSettings.ClampOpacity(value),
            -5,
            5,
            0,
            100,
            popupId,
            "Maximum opacity for older hitbox trail samples.");
    }

    private void DrawHitboxOnDeathPopupControls(string popupId) {
        bool allHitboxes = AkronModule.Settings.HitboxShowAllOnDeath;
        if (ImGui.Checkbox("All hitboxes##" + popupId, ref allHitboxes)) {
            AkronModule.Settings.HitboxShowAllOnDeath = allHitboxes;
        }
        DrawPopupTooltip("During the death window, draw the regular hitbox set using the same filters, colors, line thickness, fill, and outline settings as Show Hitboxes.");

        bool playerMarker = AkronModule.Settings.HitboxShowDeathPlayerMarker;
        if (ImGui.Checkbox("Player marker##" + popupId, ref playerMarker)) {
            AkronModule.Settings.HitboxShowDeathPlayerMarker = playerMarker;
        }
        DrawPopupTooltip("Always mark Madeline's recorded death position, even when All hitboxes or Hide player is on.");

        ImGui.Separator();
        DrawHitboxColorRow("Death object", () => AkronModule.Settings.HitboxDeathColor, value => AkronModule.Settings.HitboxDeathColor = value, popupId, "Last recorded death-object hitbox color.");
        DrawHitboxColorRow("Player marker", () => AkronModule.Settings.HitboxDeathPlayerColor, value => AkronModule.Settings.HitboxDeathPlayerColor = value, popupId, "Last recorded player-position marker color.");
    }

    private void DrawRefillClarityPopupControls(string popupId) {
        DrawIntStepperRow(
            "Opacity",
            () => AkronModule.Settings.RefillClarityOpacity,
            value => AkronModule.Settings.RefillClarityOpacity = AkronModuleSettings.ClampOpacity(value),
            -5,
            5,
            0,
            100,
            popupId,
            "Overlay opacity for visible one-use refill outlines.");

        DrawHitboxColorRow("Outline", () => AkronModule.Settings.RefillClarityColor, value => AkronModule.Settings.RefillClarityColor = AkronModuleSettings.ClampRgb(value), popupId, "Outline color for visible one-use refills.");
    }

    private void DrawScreenshotCapturePopupControls(string popupId, bool chapter) {
        Level level = Scene as Level;
        if (ImGui.Button((chapter ? "Capture Map" : "Capture Room") + "##" + popupId) && level != null) {
            if (chapter) {
                AkronScreenshotScanner.ScanChapter(level);
            } else {
                AkronScreenshotScanner.ScanRoom(level);
            }
        }
        DrawPopupTooltip(chapter ? "Capture each room in the current map." : "Capture overlapping tiles across the current room.");

        if (AkronScreenshotScanner.IsScanning && ImGui.Button("Stop Scan##" + popupId)) {
            AkronScreenshotScanner.Stop();
        }

        string exportPath = AkronModule.Settings.ScreenshotScannerExportPath ?? AkronModuleSettings.DefaultScreenshotScannerExportPath;
        if (DrawPopupInputText("Export path", ref exportPath, 180, popupId, 240f)) {
            AkronModule.Settings.ScreenshotScannerExportPath = AkronModuleSettings.NormalizeScreenshotScannerExportPath(exportPath);
            MarkValueEditFreeze();
        }
        DrawPopupTooltip("Relative to the Celeste install folder, matching ScreenshotTool's export layout.");

        DrawScreenshotScannerFormatRow(popupId);

        DrawIntStepperRow("Wait", () => AkronModule.Settings.ScreenshotScannerWaitFrames, value => AkronModule.Settings.ScreenshotScannerWaitFrames = AkronModuleSettings.ClampScreenshotScannerWaitFrames(value), -1, 1, 0, 240, popupId, "Frames to wait before each tile capture.");
        DrawIntStepperRow("Horizontal", () => AkronModule.Settings.ScreenshotScannerHorizontalOffsetTiles, value => AkronModule.Settings.ScreenshotScannerHorizontalOffsetTiles = AkronModuleSettings.ClampScreenshotScannerOffsetTiles(value), -1, 1, 1, 80, popupId, "Horizontal tile offset between captures.");
        DrawIntStepperRow("Vertical", () => AkronModule.Settings.ScreenshotScannerVerticalOffsetTiles, value => AkronModule.Settings.ScreenshotScannerVerticalOffsetTiles = AkronModuleSettings.ClampScreenshotScannerOffsetTiles(value), -1, 1, 1, 80, popupId, "Vertical tile offset between captures.");

        bool exportMarkers = AkronModule.Settings.ScreenshotScannerExportMarkers;
        if (ImGui.Checkbox("Export markers##" + popupId, ref exportMarkers)) {
            AkronModule.Settings.ScreenshotScannerExportMarkers = exportMarkers;
        }
        DrawPopupTooltip("Draw configured StartPos, Auto Kill, and Auto Deafen overlays into the exported image tiles.");

        bool startPositions = AkronModule.Settings.ScreenshotScannerExportStartPositions;
        if (ImGui.Checkbox("StartPos markers##" + popupId, ref startPositions)) {
            AkronModule.Settings.ScreenshotScannerExportStartPositions = startPositions;
        }
        DrawPopupTooltip("Include all saved StartPos slots for each captured room when Export markers is on.");

        bool autoKillAreas = AkronModule.Settings.ScreenshotScannerExportAutoKillAreas;
        if (ImGui.Checkbox("Auto Kill areas##" + popupId, ref autoKillAreas)) {
            AkronModule.Settings.ScreenshotScannerExportAutoKillAreas = autoKillAreas;
        }
        DrawPopupTooltip("Include configured Auto Kill areas when Export markers is on.");

        bool autoDeafenAreas = AkronModule.Settings.ScreenshotScannerExportAutoDeafenAreas;
        if (ImGui.Checkbox("Auto Deafen areas##" + popupId, ref autoDeafenAreas)) {
            AkronModule.Settings.ScreenshotScannerExportAutoDeafenAreas = autoDeafenAreas;
        }
        DrawPopupTooltip("Include configured Auto Deafen areas when Export markers is on.");

        bool freeze = AkronModule.Settings.ScreenshotScannerFreezeTime;
        if (ImGui.Checkbox("Freeze time##" + popupId, ref freeze)) {
            AkronModule.Settings.ScreenshotScannerFreezeTime = freeze;
        }
        DrawPopupTooltip("Keep level timers pinned while capture camera positions settle.", "Freeze timers");

        bool suppressMadeline = AkronModule.Settings.ScreenshotScannerNoclipHideMadeline;
        if (ImGui.Checkbox("Noclip + hide Madeline##" + popupId, ref suppressMadeline)) {
            AkronModule.Settings.ScreenshotScannerNoclipHideMadeline = suppressMadeline;
        }
        DrawPopupTooltip("During capture, put Madeline in a dummy non-collidable hidden state without changing the global Noclip or Hide Player settings.");

        bool removeBg = AkronModule.Settings.ScreenshotScannerRemoveBackground;
        if (ImGui.Checkbox("Remove background##" + popupId, ref removeBg)) {
            AkronModule.Settings.ScreenshotScannerRemoveBackground = removeBg;
        }
        DrawPopupTooltip("Infer transparent pixels by rendering each tile against black and white backgrounds.");
        bool removeFg = AkronModule.Settings.ScreenshotScannerRemoveForeground;
        if (ImGui.Checkbox("Remove foreground##" + popupId, ref removeFg)) {
            AkronModule.Settings.ScreenshotScannerRemoveForeground = removeFg;
        }
        DrawPopupTooltip("Hide foreground effects during capture so parallax layers do not smear when tiles are merged.");
    }

    private static void DrawScreenshotScannerFormatRow(string popupId) {
        AkronScreenshotImageFormat format = AkronModuleSettings.NormalizeScreenshotScannerImageFormat(AkronModule.Settings.ScreenshotScannerImageFormat);
        const float comboWidth = 88f;
        DrawPopupRowLabel("Format", CalculatePopupLabelWidth(comboWidth));
        ImGui.PushItemWidth(comboWidth);
        if (ImGui.BeginCombo("##ScreenshotScannerFormat" + popupId, FormatScreenshotScannerImageFormat(format))) {
            foreach (AkronScreenshotImageFormat option in Enum.GetValues(typeof(AkronScreenshotImageFormat))) {
                bool selected = option == format;
                if (ImGui.Selectable(FormatScreenshotScannerImageFormat(option) + "##ScreenshotScannerFormat" + popupId + option, selected)) {
                    AkronModule.Settings.ScreenshotScannerImageFormat = option;
                }

                if (selected) {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();
        DrawPopupTooltip("Image format used for room and map capture tiles.");
    }

    private static string FormatScreenshotScannerImageFormat(AkronScreenshotImageFormat format) {
        return AkronModuleSettings.NormalizeScreenshotScannerImageFormat(format) == AkronScreenshotImageFormat.Jpeg
            ? "JPG"
            : "PNG";
    }

    private void DrawAutosavePopupControls(string popupId) {
        DrawIntStepperRow("Interval", () => AkronModule.Settings.AutosaveIntervalSeconds, value => AkronModule.Settings.AutosaveIntervalSeconds = AkronModuleSettings.ClampAutosaveIntervalSeconds(value), -5, 5, 1, 600, popupId, "Seconds between automatic save attempts.");
        DrawIntStepperRow("Minimum delay", () => AkronModule.Settings.AutosaveMinimumDelaySeconds, value => AkronModule.Settings.AutosaveMinimumDelaySeconds = AkronModuleSettings.ClampAutosaveMinimumDelaySeconds(value), -5, 5, 0, 3600, popupId, "Cooldown before another automatic save can run.");
        DrawAutosaveCheckbox("Room load", () => AkronModule.Settings.AutosaveOnRoomLoad, value => AkronModule.Settings.AutosaveOnRoomLoad = value, popupId);
        DrawAutosaveCheckbox("Spawn update", () => AkronModule.Settings.AutosaveOnSpawnUpdate, value => AkronModule.Settings.AutosaveOnSpawnUpdate = value, popupId);
        DrawAutosaveCheckbox("Respawn", () => AkronModule.Settings.AutosaveOnRespawn, value => AkronModule.Settings.AutosaveOnRespawn = value, popupId);
        DrawAutosaveCheckbox("Pause", () => AkronModule.Settings.AutosaveOnPause, value => AkronModule.Settings.AutosaveOnPause = value, popupId);
        DrawAutosaveCheckbox("Avoid gameplay", () => AkronModule.Settings.AutosaveAvoidGameplay, value => AkronModule.Settings.AutosaveAvoidGameplay = value, popupId);
        DrawAutosaveCheckbox("Save settings", () => AkronModule.Settings.AutosaveSaveSettings, value => AkronModule.Settings.AutosaveSaveSettings = value, popupId);
        DrawAutosaveCheckbox("Hide saving icon", () => AkronModule.Settings.AutosaveHideSavingIcon, value => AkronModule.Settings.AutosaveHideSavingIcon = value, popupId);
        if (ImGui.Button("Save now##" + popupId)) {
            AkronAutosave.SaveNow();
        }
    }

    private void DrawDeloadSpinnersPopupControls(string popupId) {
        Level level = Scene as Level;
        if (ImGui.Button("Deload now##" + popupId) && level != null) {
            AkronActions.DeloadSpinners(level, AkronModule.Settings.DeloadSpinnerDelaySeconds);
        }
        DrawPopupTooltip("Simulate spinner deloading using the configured seconds-before-deload value.");

        DrawFloatStepperRow(
            "Before",
            () => AkronModule.Settings.DeloadSpinnerDelaySeconds,
            value => AkronModule.Settings.DeloadSpinnerDelaySeconds = AkronDeloadSimulator.ClampDelaySeconds(value),
            -0.25f,
            0.25f,
            0f,
            3600f,
            "%.2f",
            popupId,
            "Matches the optional deload [number] argument: seconds before the deload to simulate from.");
    }

    private static void DrawAutosaveCheckbox(string label, Func<bool> getter, Action<bool> setter, string popupId) {
        bool value = getter();
        if (ImGui.Checkbox(label + "##" + popupId, ref value)) {
            setter(value);
        }
    }

    private void DrawCounterStatsPopupControls(bool dash, string popupId) {
        AkronCounterDisplayMode mode = dash ? AkronModule.Settings.DashCountStatsMode : AkronModule.Settings.JumpCountMode;
        if (ImGui.Button("Mode: " + mode + "##" + popupId)) {
            AkronCounterDisplayMode next = (AkronCounterDisplayMode) (((int) mode + 1) % Enum.GetValues(typeof(AkronCounterDisplayMode)).Length);
            if (dash) {
                AkronModule.Settings.DashCountStatsMode = next;
            } else {
                AkronModule.Settings.JumpCountMode = next;
            }
        }
        DrawPopupTooltip("Cycle Off, Session, Chapter, File, and Both display modes.");
        bool noReset = dash ? AkronModule.Settings.DashCountStatsDoNotResetOnDeath : AkronModule.Settings.JumpCountDoNotResetOnDeath;
        if (ImGui.Checkbox("Do not reset on death##" + popupId, ref noReset)) {
            if (dash) {
                AkronModule.Settings.DashCountStatsDoNotResetOnDeath = noReset;
            } else {
                AkronModule.Settings.JumpCountDoNotResetOnDeath = noReset;
            }
        }
    }

    private void DrawAudioSplitterPopupControls(string popupId) {
        ImGui.TextUnformatted(AkronAudioSplitter.Status());
        IReadOnlyList<string> devices = AkronAudioSplitter.ListDevices();
        if (ImGui.Button("Reload Devices##" + popupId)) {
            Engine.Scene?.Add(new AkronToast("Audio devices: " + devices.Count + "."));
        }
        DrawPopupTooltip("Refresh and report FMOD output devices visible to Celeste.");
        DrawAudioDeviceCombo("Main", () => AkronModule.Settings.AudioSplitterMainDevice, value => AkronModule.Settings.AudioSplitterMainDevice = value, devices, popupId);
        DrawAudioDeviceCombo("Music", () => AkronModule.Settings.AudioSplitterMusicDevice, value => AkronModule.Settings.AudioSplitterMusicDevice = value, devices, popupId);
        DrawAudioDeviceCombo("SFX", () => AkronModule.Settings.AudioSplitterSfxDevice, value => AkronModule.Settings.AudioSplitterSfxDevice = value, devices, popupId);
    }

    private void DrawSoundVolumePopupControls(AkronEarAid.SoundDefinition sound, string popupId) {
        DrawIntStepperRow(
            "Volume",
            () => AkronEarAid.VolumeFor(sound.Key),
            value => AkronEarAid.SetVolume(sound.Key, value),
            -5,
            5,
            0,
            200,
            popupId,
            "Configured volume percentage. It only affects matching game sounds while this sound row is on.");
    }

    private static void DrawAudioDeviceCombo(string label, Func<string> getter, Action<string> setter, IReadOnlyList<string> devices, string popupId) {
        const float comboWidth = 240f;
        string selectedDevice = string.IsNullOrWhiteSpace(getter()) ? "Default" : getter();
        DrawPopupRowLabel(label, CalculatePopupLabelWidth(comboWidth));
        ImGui.PushItemWidth(comboWidth);
        if (ImGui.BeginCombo("##" + label + popupId, selectedDevice)) {
            DrawAudioDeviceChoice(selectedDevice, "Default", setter, label, popupId, -1);
            for (int index = 0; index < devices.Count; index++) {
                string device = string.IsNullOrWhiteSpace(devices[index]) ? "Device " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) : devices[index];
                if (string.Equals(device, "Default", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                DrawAudioDeviceChoice(selectedDevice, device, setter, label, popupId, index);
            }

            if (!devices.Contains(selectedDevice, StringComparer.OrdinalIgnoreCase) &&
                !string.Equals(selectedDevice, "Default", StringComparison.OrdinalIgnoreCase)) {
                DrawAudioDeviceChoice(selectedDevice, selectedDevice, setter, label, popupId, -2);
            }

            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();
        DrawPopupTooltip("Choose the output device for this audio route.");
    }

    private static void DrawAudioDeviceChoice(string selectedDevice, string device, Action<string> setter, string label, string popupId, int index) {
        bool selected = string.Equals(selectedDevice, device, StringComparison.OrdinalIgnoreCase);
        if (ImGui.Selectable(device + "##" + label + popupId + index, selected)) {
            setter(device);
        }
        if (selected) {
            ImGui.SetItemDefaultFocus();
        }
    }
}
