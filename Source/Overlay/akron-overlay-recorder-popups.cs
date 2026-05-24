using System;
using System.Collections.Generic;
using Celeste;
using ImGuiNET;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private static IReadOnlyList<SelectorDropdownChoice> BuildRecordingContainerChoices() {
        return new List<SelectorDropdownChoice> {
            RecordingContainerChoice(AkronRecordingContainerFormat.Mkv),
            RecordingContainerChoice(AkronRecordingContainerFormat.Mp4),
            RecordingContainerChoice(AkronRecordingContainerFormat.Mov),
            RecordingContainerChoice(AkronRecordingContainerFormat.WebM)
        };
    }

    private static SelectorDropdownChoice RecordingContainerChoice(AkronRecordingContainerFormat format) {
        return new SelectorDropdownChoice(
            AkronModuleSettings.FormatRecordingContainer(format),
            () => AkronModule.Settings.RecordingContainerFormat == format,
            () => AkronModule.Settings.RecordingContainerFormat = format);
    }

    private static IReadOnlyList<SelectorDropdownChoice> BuildRecordingReplayAutoStartChoices() {
        return new List<SelectorDropdownChoice> {
            RecordingReplayAutoStartChoice(AkronRecordingReplayAutoStart.Off),
            RecordingReplayAutoStartChoice(AkronRecordingReplayAutoStart.InLevels),
            RecordingReplayAutoStartChoice(AkronRecordingReplayAutoStart.Always)
        };
    }

    private static SelectorDropdownChoice RecordingReplayAutoStartChoice(AkronRecordingReplayAutoStart mode) {
        return new SelectorDropdownChoice(
            AkronModuleSettings.FormatRecordingReplayAutoStart(mode),
            () => AkronModule.Settings.RecordingReplayAutoStart == mode,
            () => SetRecordingReplayAutoStart(mode));
    }

    private static void SetRecordingReplayAutoStart(AkronRecordingReplayAutoStart mode) {
        AkronModule.Settings.RecordingReplayAutoStart = mode;
        if (mode != AkronRecordingReplayAutoStart.Off &&
            AkronModuleSettings.ClampRecordingReplayBufferSeconds(AkronModule.Settings.RecordingReplayBufferSeconds) <= 0) {
            AkronModule.Settings.RecordingReplayBufferSeconds = AkronModuleSettings.DefaultRecordingReplayBufferSeconds;
        }
    }

    private static IReadOnlyList<SelectorDropdownChoice> BuildRecordingQualityChoices() {
        return new List<SelectorDropdownChoice> {
            RecordingQualityChoice(AkronRecordingQualityPreset.LowImpact),
            RecordingQualityChoice(AkronRecordingQualityPreset.Balanced),
            RecordingQualityChoice(AkronRecordingQualityPreset.HighQuality),
            RecordingQualityChoice(AkronRecordingQualityPreset.Lossless)
        };
    }

    private static SelectorDropdownChoice RecordingQualityChoice(AkronRecordingQualityPreset preset) {
        return new SelectorDropdownChoice(
            AkronModuleSettings.FormatRecordingQualityPreset(preset),
            () => AkronModule.Settings.RecordingQualityPreset == preset,
            () => AkronModule.Settings.RecordingQualityPreset = preset);
    }

    private static IReadOnlyList<SelectorDropdownChoice> BuildRecordingRateControlChoices() {
        return new List<SelectorDropdownChoice> {
            RecordingRateControlChoice(AkronRecordingRateControl.Cbr, "CBR"),
            RecordingRateControlChoice(AkronRecordingRateControl.Vbr, "VBR"),
            RecordingRateControlChoice(AkronRecordingRateControl.Cqp, "CQP"),
            RecordingRateControlChoice(AkronRecordingRateControl.Crf, "CRF"),
            RecordingRateControlChoice(AkronRecordingRateControl.Lossless, "Lossless")
        };
    }

    private static SelectorDropdownChoice RecordingRateControlChoice(AkronRecordingRateControl mode, string label) {
        return new SelectorDropdownChoice(label, () => AkronModule.Settings.RecordingRateControl == mode, () => AkronModule.Settings.RecordingRateControl = mode);
    }

    private static IReadOnlyList<SelectorDropdownChoice> BuildRecordingCodecChoices() {
        return new List<SelectorDropdownChoice> {
            RecordingCodecChoice(AkronRecordingCodec.Libx264),
            RecordingCodecChoice(AkronRecordingCodec.H264Nvenc),
            RecordingCodecChoice(AkronRecordingCodec.H264Amf),
            RecordingCodecChoice(AkronRecordingCodec.HevcNvenc),
            RecordingCodecChoice(AkronRecordingCodec.LibVpxVp9)
        };
    }

    private static SelectorDropdownChoice RecordingCodecChoice(AkronRecordingCodec codec) {
        return new SelectorDropdownChoice(
            AkronModuleSettings.FormatRecordingCodec(codec),
            () => AkronModule.Settings.RecordingCodec == codec,
            () => AkronModule.Settings.RecordingCodec = codec);
    }

    private static IReadOnlyList<SelectorDropdownChoice> BuildRecordingClipSortChoices() {
        return new List<SelectorDropdownChoice> {
            RecordingClipSortChoice(AkronRecordingClipSort.Date, "Date"),
            RecordingClipSortChoice(AkronRecordingClipSort.Chapter, "Chapter"),
            RecordingClipSortChoice(AkronRecordingClipSort.Room, "Room"),
            RecordingClipSortChoice(AkronRecordingClipSort.Death, "Death"),
            RecordingClipSortChoice(AkronRecordingClipSort.Clear, "Clear"),
            RecordingClipSortChoice(AkronRecordingClipSort.Pb, "PB"),
            RecordingClipSortChoice(AkronRecordingClipSort.Favorite, "Favorite")
        };
    }

    private static SelectorDropdownChoice RecordingClipSortChoice(AkronRecordingClipSort sort, string label) {
        return new SelectorDropdownChoice(label, () => AkronModule.Settings.RecordingClipBrowserSort == sort, () => AkronModule.Settings.RecordingClipBrowserSort = sort);
    }

    private static IReadOnlyList<SelectorDropdownChoice> BuildRecordingClipFilterChoices() {
        return new List<SelectorDropdownChoice> {
            RecordingClipFilterChoice(AkronRecordingClipFilter.All, "All"),
            RecordingClipFilterChoice(AkronRecordingClipFilter.Chapter, "Chapter"),
            RecordingClipFilterChoice(AkronRecordingClipFilter.Room, "Room"),
            RecordingClipFilterChoice(AkronRecordingClipFilter.Death, "Death"),
            RecordingClipFilterChoice(AkronRecordingClipFilter.Clear, "Clear"),
            RecordingClipFilterChoice(AkronRecordingClipFilter.Pb, "PB"),
            RecordingClipFilterChoice(AkronRecordingClipFilter.Favorite, "Favorite")
        };
    }

    private static SelectorDropdownChoice RecordingClipFilterChoice(AkronRecordingClipFilter filter, string label) {
        return new SelectorDropdownChoice(label, () => AkronModule.Settings.RecordingClipBrowserFilter == filter, () => AkronModule.Settings.RecordingClipBrowserFilter = filter);
    }

    private void DrawRecorderTextPopupControls(string label, string popupId) {
        if (string.Equals(label, "Output Folder", StringComparison.OrdinalIgnoreCase)) {
            string folder = AkronModule.Settings.RecordingOutputFolder ?? string.Empty;
            if (DrawPopupInputText("Folder", ref folder, 260, popupId, 280f)) {
                AkronModule.Settings.RecordingOutputFolder = folder.Trim();
                MarkValueEditFreeze();
            }
            if (ImGui.IsItemActive()) {
                MarkValueEditFreeze();
            }
            DrawPopupTooltip("Empty uses Saves/AkronRecordings. Absolute paths are accepted.");

            if (ImGui.Button("Use default##" + popupId)) {
                AkronModule.Settings.RecordingOutputFolder = string.Empty;
            }
            DrawPopupTooltip("Return to the default save folder.");
            return;
        }

        if (string.Equals(label, "Filename Template", StringComparison.OrdinalIgnoreCase)) {
            string template = AkronModule.Settings.RecordingFilenameTemplate ?? AkronModuleSettings.DefaultRecordingFilenameTemplate;
            if (DrawPopupInputText("Template", ref template, 160, popupId, 280f)) {
                AkronModule.Settings.RecordingFilenameTemplate = AkronModuleSettings.NormalizeRecordingFilenameTemplate(template);
                MarkValueEditFreeze();
            }
            if (ImGui.IsItemActive()) {
                MarkValueEditFreeze();
            }
            DrawPopupTooltip("Tokens: {chapter}, {room}, {timestamp}, {death}, and {attempt}.");

            if (ImGui.Button("Reset template##" + popupId)) {
                AkronModule.Settings.RecordingFilenameTemplate = AkronModuleSettings.DefaultRecordingFilenameTemplate;
            }
            DrawPopupTooltip("Restore Akron's default recorder filename template.");
            return;
        }

        if (string.Equals(label, "Colorspace Args", StringComparison.OrdinalIgnoreCase)) {
            string colorspaceArgs = AkronModule.Settings.RecordingColorspaceArgs ?? string.Empty;
            if (DrawPopupInputText("FFmpeg -vf", ref colorspaceArgs, 220, popupId, 280f)) {
                AkronModule.Settings.RecordingColorspaceArgs = colorspaceArgs.Trim();
                MarkValueEditFreeze();
            }
            if (ImGui.IsItemActive()) {
                MarkValueEditFreeze();
            }
            DrawPopupTooltip("Passed as the FFmpeg video filter argument, for example format=yuv420p.");

            if (ImGui.Button("Clear##" + popupId)) {
                AkronModule.Settings.RecordingColorspaceArgs = string.Empty;
            }
            DrawPopupTooltip("Use FFmpeg's default colorspace handling.");
        }
    }

    private void DrawRecorderReplayPopupControls(string popupId) {
        Scene scene = Scene;
        if (ImGui.Button("Start Replay Buffer##" + popupId)) {
            if (scene != null) {
                AkronActions.StartReplayBuffer(scene);
            }
        }
        DrawPopupTooltip("Start rolling replay capture for the current game scene.");

        ImGui.SameLine();
        if (ImGui.Button("Stop##replay-buffer-" + popupId)) {
            AkronActions.StopReplayBuffer();
        }
        DrawPopupTooltip("Stop rolling replay capture.");

        ImGui.SameLine();
        if (ImGui.Button("Save##replay-buffer-" + popupId)) {
            if (scene != null) {
                AkronActions.SaveReplayBuffer(scene);
            }
        }
        DrawPopupTooltip("Save the current replay buffer window.");

        ImGui.TextUnformatted("Status: " + AkronInternalRecorder.DescribeReplayBufferStatus());

        DrawIntStepperRow(
            "Buffer",
            () => AkronModule.Settings.RecordingReplayBufferSeconds,
            value => AkronModule.Settings.RecordingReplayBufferSeconds = AkronModuleSettings.ClampRecordingReplayBufferSeconds(value),
            -5,
            5,
            5,
            600,
            popupId,
            "Seconds of rolling FFmpeg segments kept available for manual replay saves.");

        DrawPopupChoiceCombo(
            "Auto-start",
            () => AkronModuleSettings.FormatRecordingReplayAutoStart(AkronModule.Settings.RecordingReplayAutoStart),
            BuildRecordingReplayAutoStartChoices(),
            popupId,
            "Off keeps replay manual. In Levels starts when gameplay renders. Always starts in menus, overworld, and levels.");

        string saveActionKey = BuildActionKey("Internal Recorder", "Save Replay Buffer");
        ImGui.TextUnformatted("Save key: " + DescribeEffectiveMenuBinding(saveActionKey, "Save Replay Buffer"));
        if (ImGui.Button("Bind save key##" + popupId)) {
            StartBindingCapture(saveActionKey, "Internal Recorder / Save Replay Buffer");
            ImGui.CloseCurrentPopup();
        }
        DrawPopupTooltip("Set the key used to save the replay buffer without opening the overlay.");

        ImGui.SameLine();
        if (ImGui.Button("Clear##replay-save-key-" + popupId)) {
            ClearMenuBinding(saveActionKey);
        }
        DrawPopupTooltip("Clear only the custom replay-buffer save binding.");
    }

    private void DrawRecorderOutputPopupControls(string popupId) {
        DrawRecorderTextPopupControls("Output Folder", popupId + "-folder");
        ImGui.Separator();
        DrawRecorderTextPopupControls("Filename Template", popupId + "-template");
        ImGui.Separator();
        DrawPopupChoiceCombo(
            "Container",
            () => AkronModuleSettings.FormatRecordingContainer(AkronModule.Settings.RecordingContainerFormat),
            BuildRecordingContainerChoices(),
            popupId,
            "MKV is safest for interrupted recordings; MP4, MOV, and WebM are easier to share.");

        bool autoRemux = AkronModule.Settings.RecordingAutoRemux;
        if (ImGui.Checkbox("Auto remux##" + popupId, ref autoRemux)) {
            AkronModule.Settings.RecordingAutoRemux = autoRemux;
        }
        DrawPopupTooltip("When enabled, keep the crash-resistant capture target and produce an MP4 share copy after recording.");

        ImGui.Separator();
        DrawPopupChoiceCombo(
            "Sort clips",
            () => AkronModule.Settings.RecordingClipBrowserSort.ToString(),
            BuildRecordingClipSortChoices(),
            popupId,
            "Saved clip browser grouping preference.");
        DrawPopupChoiceCombo(
            "Filter clips",
            () => AkronModule.Settings.RecordingClipBrowserFilter.ToString(),
            BuildRecordingClipFilterChoices(),
            popupId,
            "Saved clip browser filter preference.");
    }

    private void DrawRecorderEncoderPopupControls(string popupId) {
        DrawPopupChoiceCombo(
            "Quality",
            () => AkronModuleSettings.FormatRecordingQualityPreset(AkronModule.Settings.RecordingQualityPreset),
            BuildRecordingQualityChoices(),
            popupId,
            "Simple encoder quality target before fine-tuning bitrate or rate control.");
        DrawPopupChoiceCombo(
            "Rate ctrl",
            () => AkronModule.Settings.RecordingRateControl.ToString().ToUpperInvariant(),
            BuildRecordingRateControlChoices(),
            popupId,
            "Encoder rate-control mode for quality, file size, or lossless output.");

        DrawIntStepperRow("Keyframe", () => AkronModule.Settings.RecordingKeyframeIntervalSeconds, value => AkronModule.Settings.RecordingKeyframeIntervalSeconds = AkronModuleSettings.ClampRecordingKeyframeIntervalSeconds(value), -1, 1, 0, 20, popupId, "Seconds between keyframes. Zero lets the encoder decide.");

        bool droppedWarning = AkronModule.Settings.RecordingDroppedFrameWarning;
        if (ImGui.Checkbox("Dropped-frame warning##" + popupId, ref droppedWarning)) {
            AkronModule.Settings.RecordingDroppedFrameWarning = droppedWarning;
        }
        DrawPopupTooltip("Show a warning when the recorder cannot capture frames at the configured cadence.");
    }

    private void DrawRecorderAudioPopupControls(string popupId) {
        DrawPopupCheckbox("Full mix track", () => AkronModule.Settings.RecordingAudioFullMixTrack, value => AkronModule.Settings.RecordingAudioFullMixTrack = value, popupId, "Record the mixed game-audio track.");
        DrawPopupCheckbox("Music track", () => AkronModule.Settings.RecordingAudioMusicTrack, value => AkronModule.Settings.RecordingAudioMusicTrack = value, popupId, "Record an isolated music track when the backend can provide it.");
        DrawPopupCheckbox("SFX track", () => AkronModule.Settings.RecordingAudioSfxTrack, value => AkronModule.Settings.RecordingAudioSfxTrack = value, popupId, "Record an isolated sound-effects track when the backend can provide it.");
        DrawPopupCheckbox("Ambience track", () => AkronModule.Settings.RecordingAudioAmbienceTrack, value => AkronModule.Settings.RecordingAudioAmbienceTrack = value, popupId, "Record an isolated ambience track when the backend can provide it.");
        DrawPopupCheckbox("Record muted audio", () => AkronModule.Settings.RecordingRecordMutedAudio, value => AkronModule.Settings.RecordingRecordMutedAudio = value, popupId, "Capture game audio even when in-game playback is muted, when supported by the audio backend.");

        ImGui.Separator();
        DrawIntStepperRow("Mix", () => AkronModule.Settings.RecordingAudioFullMixLevel, value => AkronModule.Settings.RecordingAudioFullMixLevel = AkronModuleSettings.ClampRecordingAudioLevel(value), -5, 5, 0, 200, popupId, "Full-mix recording volume percentage.");
        DrawIntStepperRow("Music", () => AkronModule.Settings.RecordingAudioMusicLevel, value => AkronModule.Settings.RecordingAudioMusicLevel = AkronModuleSettings.ClampRecordingAudioLevel(value), -5, 5, 0, 200, popupId, "Music track recording volume percentage.");
        DrawIntStepperRow("SFX", () => AkronModule.Settings.RecordingAudioSfxLevel, value => AkronModule.Settings.RecordingAudioSfxLevel = AkronModuleSettings.ClampRecordingAudioLevel(value), -5, 5, 0, 200, popupId, "SFX track recording volume percentage.");
        DrawIntStepperRow("Amb", () => AkronModule.Settings.RecordingAudioAmbienceLevel, value => AkronModule.Settings.RecordingAudioAmbienceLevel = AkronModuleSettings.ClampRecordingAudioLevel(value), -5, 5, 0, 200, popupId, "Ambience track recording volume percentage.");
    }

    private void DrawRecorderClipTriggersPopupControls(string popupId) {
        DrawPopupCheckbox("Last death", () => AkronModule.Settings.RecordingTriggerLastDeath, value => AkronModule.Settings.RecordingTriggerLastDeath = value, popupId, "Auto-save a clip around the latest death trigger.");
        DrawPopupCheckbox("Respawn to death", () => AkronModule.Settings.RecordingTriggerRespawnToDeath, value => AkronModule.Settings.RecordingTriggerRespawnToDeath = value, popupId, "Auto-save clips from respawn through the next death.");
        DrawPopupCheckbox("Room entry to clear", () => AkronModule.Settings.RecordingTriggerRoomEntryToClear, value => AkronModule.Settings.RecordingTriggerRoomEntryToClear = value, popupId, "Auto-save clips from room entry through a clear.");
        DrawPopupCheckbox("Checkpoint clear", () => AkronModule.Settings.RecordingTriggerCheckpointClear, value => AkronModule.Settings.RecordingTriggerCheckpointClear = value, popupId, "Auto-save clips when a checkpoint is cleared.");
        DrawPopupCheckbox("Berry collect", () => AkronModule.Settings.RecordingTriggerBerryCollect, value => AkronModule.Settings.RecordingTriggerBerryCollect = value, popupId, "Auto-save clips when a berry is collected.");
        DrawPopupCheckbox("Golden death", () => AkronModule.Settings.RecordingTriggerGoldenDeath, value => AkronModule.Settings.RecordingTriggerGoldenDeath = value, popupId, "Auto-save clips when a golden attempt dies.");

        ImGui.Separator();
        DrawIntStepperRow("Pre-roll", () => AkronModule.Settings.RecordingPreRollSeconds, value => AkronModule.Settings.RecordingPreRollSeconds = AkronModuleSettings.ClampRecordingClipSeconds(value), -1, 1, 0, 120, popupId, "Seconds included before event-based clip triggers.");
        DrawIntStepperRow("Post-roll", () => AkronModule.Settings.RecordingPostRollSeconds, value => AkronModule.Settings.RecordingPostRollSeconds = AkronModuleSettings.ClampRecordingClipSeconds(value), -1, 1, 0, 120, popupId, "Seconds included after event-based clip triggers.");
    }
}
