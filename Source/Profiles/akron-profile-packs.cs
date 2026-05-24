using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed class AkronProfilePack {
    public string Format { get; set; } = AkronProfilePacks.ProfilePackFormat;
    public string Name { get; set; } = "Akron Profile";
    public string CreatedUtc { get; set; } = string.Empty;
    public AkronProfileSection Section { get; set; } = AkronProfileSection.Whole;
    public AkronProfile ActiveProfile { get; set; } = AkronProfile.Practice;
    public AkronProfileState State { get; set; } = new AkronProfileState();
    public Dictionary<string, AkronButtonBindingPack> ButtonBindings { get; set; } = new Dictionary<string, AkronButtonBindingPack>();
    public Dictionary<string, string> MenuActionBindings { get; set; } = new Dictionary<string, string>();
    public Dictionary<int, AkronStartPosPackEntry> StartPositions { get; set; } = new Dictionary<int, AkronStartPosPackEntry>();
}

public enum AkronProfileSection {
    Whole,
    StartPos,
    Keybinds,
    AutoKill,
    AutoDeafen,
    Recorder,
    Audio,
    Hud
}

public sealed class AkronButtonBindingPack {
    public List<string> Keys { get; set; } = new List<string>();
    public List<string> Buttons { get; set; } = new List<string>();
    public List<string> MouseButtons { get; set; } = new List<string>();
}

public sealed class AkronStartPosPackEntry {
    public float X { get; set; }
    public float Y { get; set; }
    public string Room { get; set; } = string.Empty;
    public string AreaSid { get; set; } = string.Empty;
    public bool UsesSpawnConfig { get; set; }
    public int Dashes { get; set; } = -1;
    public int StaminaPercent { get; set; } = -1;
    public AkronStartPosFacing Facing { get; set; } = AkronStartPosFacing.Current;
    public bool Idle { get; set; }
    public bool Grab { get; set; }
}

public static partial class AkronProfilePacks {
    public const string ProfileArchiveKind = "profile";
    public const string ProfileArchivePayload = "profile.json";
    public const string ProfilePackFormat = "akron-profile-v1";

    private const int MaxProfilePayloadBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly PropertyInfo[] ButtonBindingProperties = typeof(AkronModuleSettings)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property.PropertyType == typeof(ButtonBinding) && property.CanRead && property.CanWrite)
        .OrderBy(property => property.Name, StringComparer.Ordinal)
        .ToArray();

    private static readonly PropertyInfo[] HudStateProperties = BuildStatePropertyList(
        nameof(AkronProfileState.RoomLabels),
        nameof(AkronProfileState.LabelSystemVisible),
        nameof(AkronProfileState.RoomLabelColor),
        nameof(AkronProfileState.StaminaWidget),
        nameof(AkronProfileState.SpeedWidget),
        nameof(AkronProfileState.DashWidget),
        nameof(AkronProfileState.InputViewer),
        nameof(AkronProfileState.InputHistoryTextColor),
        nameof(AkronProfileState.InputHistoryEventColor),
        nameof(AkronProfileState.ShowTaps),
        nameof(AkronProfileState.TapDisplayCorner),
        nameof(AkronProfileState.TapDisplayScale),
        nameof(AkronProfileState.TapDisplayOpacity),
        nameof(AkronProfileState.InputBoardSource),
        nameof(AkronProfileState.InputBoardLabelPreset),
        nameof(AkronProfileState.InputBoardElements),
        nameof(AkronProfileState.InputsPerSecondCounter),
        nameof(AkronProfileState.InputsPerSecondPlacement),
        nameof(AkronProfileState.InputsPerSecondScale),
        nameof(AkronProfileState.InputsPerSecondOpacity),
        nameof(AkronProfileState.InputsPerSecondTextColor),
        nameof(AkronProfileState.InputsPerSecondShowTotal),
        nameof(AkronProfileState.InputsPerSecondShowMax),
        nameof(AkronProfileState.InputsPerSecondCountMovement),
        nameof(AkronProfileState.InputsPerSecondCountActions),
        nameof(AkronProfileState.InputsPerSecondCountMenu),
        nameof(AkronProfileState.RoomTimerWidget),
        nameof(AkronProfileState.RoomTimerColor),
        nameof(AkronProfileState.RoomStatTracker),
        nameof(AkronProfileState.RoomStatTrackerColor),
        nameof(AkronProfileState.RoomStatShowRoomName),
        nameof(AkronProfileState.RoomStatShowDeaths),
        nameof(AkronProfileState.RoomStatShowInGameTime),
        nameof(AkronProfileState.RoomStatShowStrawberries),
        nameof(AkronProfileState.RoomStatShowAliveTime),
        nameof(AkronProfileState.RoomStatHideIfGolden),
        nameof(AkronProfileState.RoomStatTimerFreezeMode),
        nameof(AkronProfileState.DeathStatsWidget),
        nameof(AkronProfileState.DeathStatsFormat),
        nameof(AkronProfileState.DeathStatsVisibility),
        nameof(AkronProfileState.DeathStatsColor),
        nameof(AkronProfileState.ResourceStaminaBar),
        nameof(AkronProfileState.StaminaBar),
        nameof(AkronProfileState.StaminaBarPlayer),
        nameof(AkronProfileState.StaminaBarHud),
        nameof(AkronProfileState.StaminaBarPlayerPosition),
        nameof(AkronProfileState.StaminaBarHudPosition),
        nameof(AkronProfileState.StaminaBarStyle),
        nameof(AkronProfileState.StaminaPlayerOffsetX),
        nameof(AkronProfileState.StaminaPlayerOffsetY),
        nameof(AkronProfileState.StaminaPlayerScale),
        nameof(AkronProfileState.StaminaAlwaysVisible),
        nameof(AkronProfileState.StaminaShowDangerMarker),
        nameof(AkronProfileState.StaminaShowChangePulse),
        nameof(AkronProfileState.StaminaShowOverflow),
        nameof(AkronProfileState.StaminaHideWhilePaused),
        nameof(AkronProfileState.StaminaHudOffsetX),
        nameof(AkronProfileState.StaminaHudOffsetY),
        nameof(AkronProfileState.StaminaNormalColor),
        nameof(AkronProfileState.StaminaLowColor),
        nameof(AkronProfileState.StaminaFillColor),
        nameof(AkronProfileState.StaminaLineColor),
        nameof(AkronProfileState.StaminaOverflowColor),
        nameof(AkronProfileState.DashBar),
        nameof(AkronProfileState.DashBarPlayer),
        nameof(AkronProfileState.DashBarHud),
        nameof(AkronProfileState.DashBarPlayerPosition),
        nameof(AkronProfileState.DashBarHudPosition),
        nameof(AkronProfileState.DashBarStyle),
        nameof(AkronProfileState.DashBarPlayerOffsetX),
        nameof(AkronProfileState.DashBarPlayerOffsetY),
        nameof(AkronProfileState.DashBarPlayerScale),
        nameof(AkronProfileState.DashBarAlwaysVisible),
        nameof(AkronProfileState.DashBarShowText),
        nameof(AkronProfileState.DashBarShowEmptyPips),
        nameof(AkronProfileState.DashBarHideWhilePaused),
        nameof(AkronProfileState.DashBarHudOffsetX),
        nameof(AkronProfileState.DashBarHudOffsetY),
        nameof(AkronProfileState.DashBarAvailableColor),
        nameof(AkronProfileState.DashBarEmptyColor),
        nameof(AkronProfileState.DashBarFillColor),
        nameof(AkronProfileState.DashBarLineColor),
        nameof(AkronProfileState.DashBarLowColor),
        nameof(AkronProfileState.DashNumber),
        nameof(AkronProfileState.DashNumberOffsetY),
        nameof(AkronProfileState.DashNumberColor),
        nameof(AkronProfileState.DashNumberOutlineColor),
        nameof(AkronProfileState.DashNumberOpacity),
        nameof(AkronProfileState.SpeedNumber),
        nameof(AkronProfileState.SpeedNumberMode),
        nameof(AkronProfileState.SpeedNumberOffsetY),
        nameof(AkronProfileState.SpeedNumberColor),
        nameof(AkronProfileState.SpeedNumberOutlineColor),
        nameof(AkronProfileState.SpeedNumberOpacity),
        nameof(AkronProfileState.TotalAttemptsWidget),
        nameof(AkronProfileState.TotalAttemptsColor),
        nameof(AkronProfileState.StatusLabelsWidget),
        nameof(AkronProfileState.StatusLabelsColor),
        nameof(AkronProfileState.ToastLabels),
        nameof(AkronProfileState.ToastLabelColor),
        nameof(AkronProfileState.ToastLabelAnchor),
        nameof(AkronProfileState.NoShortNumbers),
        nameof(AkronProfileState.HideVanillaHud),
        nameof(AkronProfileState.HideAkronHud),
        nameof(AkronProfileState.CustomHudLabels),
        nameof(AkronProfileState.CustomHudLabelsInNonLevelScenes),
        nameof(AkronProfileState.CustomHudLabelPadding),
        nameof(AkronProfileState.CustomHudLabelGap),
        nameof(AkronProfileState.CustomHudLabelObstructionEnabled),
        nameof(AkronProfileState.CustomHudLabelObstructionMode),
        nameof(AkronProfileState.CustomHudLabelObstructedOpacity),
        nameof(AkronProfileState.CustomHudLabelObstructionPaddingPixels),
        nameof(AkronProfileState.CustomHudLabelObstructionOnlyOverlappedLabel),
        nameof(AkronProfileState.CustomHudLabelObstructedAnchor),
        nameof(AkronProfileState.CustomHudLabelObstructedOffsetX),
        nameof(AkronProfileState.CustomHudLabelObstructedOffsetY),
        nameof(AkronProfileState.CustomHudLabelIndex),
        nameof(AkronProfileState.CustomHudLabelDefinitions),
        nameof(AkronProfileState.LabelRowOrder),
        nameof(AkronProfileState.LabelBulkStyle),
        nameof(AkronProfileState.RoomLabelStyle),
        nameof(AkronProfileState.InputHistoryLabelStyle),
        nameof(AkronProfileState.InputsPerSecondLabelStyle),
        nameof(AkronProfileState.StartPosLabelStyle),
        nameof(AkronProfileState.RoomTimerLabelStyle),
        nameof(AkronProfileState.DeathStatsLabelStyle),
        nameof(AkronProfileState.TotalAttemptsLabelStyle),
        nameof(AkronProfileState.StatusLabelsLabelStyle),
        nameof(AkronProfileState.ToastLabelStyle),
        nameof(AkronProfileState.HudCheatIndicator),
        nameof(AkronProfileState.HudCheatIndicatorOnlyFlagged),
        nameof(AkronProfileState.HudCheatIndicatorScale),
        nameof(AkronProfileState.HudCheatIndicatorOpacity),
        nameof(AkronProfileState.HudCheatIndicatorAnchor),
        nameof(AkronProfileState.HudCheatIndicatorStyle));

    public static AkronProfilePack Capture(AkronModuleSettings settings, AkronModuleSession session, string name = "", AkronProfileSection section = AkronProfileSection.Whole) {
        if (settings == null) {
            throw new ArgumentNullException(nameof(settings));
        }

        string created = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        section = NormalizeSection(section);
        return new AkronProfilePack {
            Name = BuildPackName(settings, name, section),
            CreatedUtc = created,
            Section = section,
            ActiveProfile = settings.ActiveProfile,
            State = settings.CaptureProfilePackState(),
            ButtonBindings = CaptureButtonBindings(settings),
            MenuActionBindings = new Dictionary<string, string>(settings.MenuActionBindings ?? new Dictionary<string, string>(), StringComparer.Ordinal),
            StartPositions = CaptureStartPositions(session)
        };
    }

    public static void Apply(AkronModuleSettings settings, AkronModuleSession session, AkronProfilePack pack, AkronProfileSection? requestedSection = null) {
        if (settings == null) {
            throw new ArgumentNullException(nameof(settings));
        }

        if (pack == null || !string.Equals(pack.Format, ProfilePackFormat, StringComparison.Ordinal)) {
            throw new InvalidDataException("Unsupported profile pack.");
        }

        AkronProfileSection section = NormalizeSection(requestedSection ?? pack.Section);
        if (section == AkronProfileSection.Whole) {
            settings.ApplyProfilePackState(pack.ActiveProfile, pack.State);
            ApplyButtonBindings(settings, pack.ButtonBindings);
            settings.MenuActionBindings = new Dictionary<string, string>(pack.MenuActionBindings ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            if (session != null) {
                session.StartPositions = BuildStartPositions(pack.StartPositions);
            }
            return;
        }

        AkronProfileState merged = settings.CaptureProfilePackState();
        ApplyStateSection(merged, pack.State, section);
        settings.ApplyProfilePackState(settings.ActiveProfile, merged);
        if (section == AkronProfileSection.Keybinds) {
            ApplyButtonBindings(settings, pack.ButtonBindings);
            settings.MenuActionBindings = new Dictionary<string, string>(pack.MenuActionBindings ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        } else if (section == AkronProfileSection.StartPos && session != null) {
            session.StartPositions = BuildStartPositions(pack.StartPositions);
        }
    }

    public static string ExportCurrent(string name = "", AkronProfileSection section = AkronProfileSection.Whole) {
        section = NormalizeSection(section);
        AkronProfilePack pack = Capture(AkronModule.Settings, AkronModule.Session, name, section);
        Directory.CreateDirectory(GetProfileDirectory());
        string fileName = SanitizeFileName(pack.Name) + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + AkronArchive.Extension;
        string path = Path.Combine(GetProfileDirectory(), fileName);
        Write(AkronModule.Settings, AkronModule.Session, path, name, section);
        Engine.Scene?.Add(new AkronToast("Exported " + FormatSection(section) + " profile " + pack.Name + "."));
        return path;
    }

    public static bool Import(string pathOrName, AkronProfileSection? section = null) {
        string path = ResolveProfilePath(pathOrName);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            Engine.Scene?.Add(new AkronToast("Profile pack not found."));
            return false;
        }

        AkronProfilePack pack;
        try {
            pack = Read(path);
            Apply(AkronModule.Settings, AkronModule.Session, pack, section);
        } catch (Exception ex) when (ex is InvalidDataException || ex is JsonException || ex is IOException || ex is UnauthorizedAccessException) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to import Akron profile archive: " + ex.Message);
            Engine.Scene?.Add(new AkronToast("Unsupported profile pack."));
            return false;
        }

        Engine.Scene?.Add(new AkronToast("Imported " + FormatSection(NormalizeSection(section ?? pack.Section)) + " profile " + pack.Name + "."));
        return true;
    }

    public static string ImportLatest(AkronProfileSection? section = null) {
        string path = Directory.Exists(GetProfileDirectory())
            ? Directory.GetFiles(GetProfileDirectory(), "*" + AkronArchive.Extension, SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (string.IsNullOrWhiteSpace(path)) {
            Engine.Scene?.Add(new AkronToast("No profile packs found."));
            return string.Empty;
        }

        Import(path, section);
        return path;
    }

    public static bool ImportFromFileBrowser(AkronProfileSection? section = null) {
        string directory = GetProfileDirectory();
        Directory.CreateDirectory(directory);

        AkronProfileFilePickerResult result = TryPickProfileArchive(directory, out string path, out string error);
        if (result == AkronProfileFilePickerResult.Selected) {
            return Import(path, section);
        }

        if (result == AkronProfileFilePickerResult.Canceled) {
            Engine.Scene?.Add(new AkronToast("No profile pack selected."));
            return false;
        }

        Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to open Akron profile file browser: " + error);
        Engine.Scene?.Add(new AkronToast("Could not open profile file browser."));
        return false;
    }

    public static void Write(AkronModuleSettings settings, AkronModuleSession session, string path, string name = "", AkronProfileSection section = AkronProfileSection.Whole) {
        AkronProfilePack pack = Capture(settings, session, name, section);
        AkronArchive.WriteSinglePayloadArchive(
            path,
            new AkronArchiveManifest {
                Kind = ProfileArchiveKind,
                KindVersion = 1,
                CreatedAt = pack.CreatedUtc,
                Target = new AkronArchiveTarget { Game = "Celeste" }
            },
            ProfileArchivePayload,
            JsonSerializer.Serialize(pack, JsonOptions));
    }

    public static AkronProfilePack Read(string path) {
        string payload = AkronArchive.ReadSinglePayloadArchive(path, ProfileArchiveKind, ProfileArchivePayload, MaxProfilePayloadBytes, out _);
        AkronProfilePack pack = JsonSerializer.Deserialize<AkronProfilePack>(payload, JsonOptions);
        if (pack == null || !string.Equals(pack.Format, ProfilePackFormat, StringComparison.Ordinal) || pack.State == null) {
            throw new InvalidDataException("Unsupported profile pack.");
        }

        return pack;
    }

    public static string FormatSection(AkronProfileSection section) {
        return NormalizeSection(section) switch {
            AkronProfileSection.StartPos => "StartPos",
            AkronProfileSection.Keybinds => "Keybinds",
            AkronProfileSection.AutoKill => "Auto Kill",
            AkronProfileSection.AutoDeafen => "Auto Deafen",
            AkronProfileSection.Recorder => "Recorder",
            AkronProfileSection.Audio => "Audio",
            AkronProfileSection.Hud => "HUD",
            _ => "Whole"
        };
    }

    public static bool TryParseSection(string value, out AkronProfileSection section) {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant().Replace("_", "-").Replace(" ", "-")) {
            case "":
            case "whole":
            case "all":
            case "profile":
                section = AkronProfileSection.Whole;
                return true;
            case "startpos":
            case "start-pos":
                section = AkronProfileSection.StartPos;
                return true;
            case "keybind":
            case "keybinds":
            case "bindings":
                section = AkronProfileSection.Keybinds;
                return true;
            case "autokill":
            case "auto-kill":
                section = AkronProfileSection.AutoKill;
                return true;
            case "autodeafen":
            case "auto-deafen":
                section = AkronProfileSection.AutoDeafen;
                return true;
            case "recorder":
            case "recording":
                section = AkronProfileSection.Recorder;
                return true;
            case "audio":
            case "sound":
                section = AkronProfileSection.Audio;
                return true;
            case "hud":
            case "labels":
            case "label":
            case "custom-hud":
            case "custom-labels":
                section = AkronProfileSection.Hud;
                return true;
            default:
                section = AkronProfileSection.Whole;
                return false;
        }
    }

    public static string GetProfileDirectory() {
        return Path.Combine(Everest.PathGame, "Saves", "AkronProfiles");
    }

    private static AkronProfileSection NormalizeSection(AkronProfileSection section) {
        return Enum.IsDefined(typeof(AkronProfileSection), section) ? section : AkronProfileSection.Whole;
    }

    private static string BuildPackName(AkronModuleSettings settings, string name, AkronProfileSection section) {
        if (!string.IsNullOrWhiteSpace(name)) {
            return name.Trim();
        }

        string profileName = AkronModuleSettings.FormatProfile(settings.ActiveProfile);
        return section == AkronProfileSection.Whole
            ? profileName
            : profileName + " " + FormatSection(section);
    }

    private static void ApplyStateSection(AkronProfileState target, AkronProfileState source, AkronProfileSection section) {
        if (target == null || source == null) {
            return;
        }

        switch (section) {
            case AkronProfileSection.StartPos:
                CopyStartPosState(target, source);
                break;
            case AkronProfileSection.AutoKill:
                CopyAutoKillState(target, source);
                break;
            case AkronProfileSection.AutoDeafen:
                CopyAutoDeafenState(target, source);
                break;
            case AkronProfileSection.Recorder:
                CopyRecorderState(target, source);
                break;
            case AkronProfileSection.Audio:
                CopyAudioState(target, source);
                break;
            case AkronProfileSection.Hud:
                CopyHudState(target, source);
                break;
        }
    }

    private static void CopyStartPosState(AkronProfileState target, AkronProfileState source) {
        target.SmartStartPos = source.SmartStartPos;
        target.RespawnAtStartPos = source.RespawnAtStartPos;
        target.StartPosShowLabel = source.StartPosShowLabel;
        target.StartPosLabelColor = source.StartPosLabelColor;
        target.StartPosLabelAnchor = source.StartPosLabelAnchor;
        target.StartPosLabelFormat = source.StartPosLabelFormat;
        target.StartPosLabelStyle = AkronModuleSettings.CloneLabelStyle(source.StartPosLabelStyle);
        target.StartPosMousePlacement = source.StartPosMousePlacement;
        target.StartPosPlacementPanelX = source.StartPosPlacementPanelX;
        target.StartPosPlacementPanelY = source.StartPosPlacementPanelY;
        target.StartPosPlacementPanelMinimized = source.StartPosPlacementPanelMinimized;
        target.StartPosPreviewOpacity = source.StartPosPreviewOpacity;
        target.StartPosConfiguredDashes = source.StartPosConfiguredDashes;
        target.StartPosConfiguredStaminaPercent = source.StartPosConfiguredStaminaPercent;
        target.StartPosConfiguredFacing = source.StartPosConfiguredFacing;
        target.StartPosConfiguredIdle = source.StartPosConfiguredIdle;
        target.StartPosConfiguredGrab = source.StartPosConfiguredGrab;
        target.StartPosSlotCount = source.StartPosSlotCount;
    }

    private static void CopyAutoKillState(AkronProfileState target, AkronProfileState source) {
        target.AutoKill = source.AutoKill;
        target.AutoKillTimer = source.AutoKillTimer;
        target.AutoKillSeconds = source.AutoKillSeconds;
        target.AutoKillArea = source.AutoKillArea;
        target.AutoKillShowArea = source.AutoKillShowArea;
        target.AutoKillShowAreaOnDeath = source.AutoKillShowAreaOnDeath;
        target.AutoKillAreas = CopyRectangles(source.AutoKillAreas);
        target.AutoKillAreaX = source.AutoKillAreaX;
        target.AutoKillAreaY = source.AutoKillAreaY;
        target.AutoKillAreaWidth = source.AutoKillAreaWidth;
        target.AutoKillAreaHeight = source.AutoKillAreaHeight;
    }

    private static void CopyAutoDeafenState(AkronProfileState target, AkronProfileState source) {
        target.AutoDeafen = source.AutoDeafen;
        target.AutoDeafenHotkey = source.AutoDeafenHotkey;
        target.AutoDeafenArea = source.AutoDeafenArea;
        target.AutoDeafenShowArea = source.AutoDeafenShowArea;
        target.AutoDeafenAreas = CopyRectangles(source.AutoDeafenAreas);
        target.AutoDeafenAreaX = source.AutoDeafenAreaX;
        target.AutoDeafenAreaY = source.AutoDeafenAreaY;
        target.AutoDeafenAreaWidth = source.AutoDeafenAreaWidth;
        target.AutoDeafenAreaHeight = source.AutoDeafenAreaHeight;
    }

    private static void CopyRecorderState(AkronProfileState target, AkronProfileState source) {
        target.RecordingOutputFolder = source.RecordingOutputFolder;
        target.RecordingFilenameTemplate = source.RecordingFilenameTemplate;
        target.RecordingContainerFormat = source.RecordingContainerFormat;
        target.RecordingReplayBufferSeconds = source.RecordingReplayBufferSeconds;
        target.RecordingReplayAutoStart = source.RecordingReplayAutoStart;
        target.RecordingTriggerLastDeath = source.RecordingTriggerLastDeath;
        target.RecordingTriggerRespawnToDeath = source.RecordingTriggerRespawnToDeath;
        target.RecordingTriggerRoomEntryToClear = source.RecordingTriggerRoomEntryToClear;
        target.RecordingTriggerCheckpointClear = source.RecordingTriggerCheckpointClear;
        target.RecordingTriggerBerryCollect = source.RecordingTriggerBerryCollect;
        target.RecordingTriggerGoldenDeath = source.RecordingTriggerGoldenDeath;
        target.RecordingPreRollSeconds = source.RecordingPreRollSeconds;
        target.RecordingPostRollSeconds = source.RecordingPostRollSeconds;
        target.RecordingAudioFullMixTrack = source.RecordingAudioFullMixTrack;
        target.RecordingAudioMusicTrack = source.RecordingAudioMusicTrack;
        target.RecordingAudioSfxTrack = source.RecordingAudioSfxTrack;
        target.RecordingAudioAmbienceTrack = source.RecordingAudioAmbienceTrack;
        target.RecordingRecordMutedAudio = source.RecordingRecordMutedAudio;
        target.RecordingAudioFullMixLevel = source.RecordingAudioFullMixLevel;
        target.RecordingAudioMusicLevel = source.RecordingAudioMusicLevel;
        target.RecordingAudioSfxLevel = source.RecordingAudioSfxLevel;
        target.RecordingAudioAmbienceLevel = source.RecordingAudioAmbienceLevel;
        target.RecordingQualityPreset = source.RecordingQualityPreset;
        target.RecordingRateControl = source.RecordingRateControl;
        target.RecordingKeyframeIntervalSeconds = source.RecordingKeyframeIntervalSeconds;
        target.RecordingDroppedFrameWarning = source.RecordingDroppedFrameWarning;
        target.RecordingAutoRemux = source.RecordingAutoRemux;
        target.RecordingClipBrowserSort = source.RecordingClipBrowserSort;
        target.RecordingClipBrowserFilter = source.RecordingClipBrowserFilter;
        target.RecordingFramerate = source.RecordingFramerate;
        target.RecordingEndscreenDurationSeconds = source.RecordingEndscreenDurationSeconds;
        target.RecordingBitrateMbps = source.RecordingBitrateMbps;
        target.RecordingResolutionX = source.RecordingResolutionX;
        target.RecordingResolutionY = source.RecordingResolutionY;
        target.RecordingHidePreview = source.RecordingHidePreview;
        target.RecordingCodec = source.RecordingCodec;
        target.RecordingColorspaceArgs = source.RecordingColorspaceArgs;
        target.RecordingPreset = source.RecordingPreset;
    }

    private static void CopyAudioState(AkronProfileState target, AkronProfileState source) {
        target.AudioSpeed = source.AudioSpeed;
        target.AudioSpeedPolicy = source.AudioSpeedPolicy;
        target.AudioSpeedMultiplier = source.AudioSpeedMultiplier;
        target.PitchShift = source.PitchShift;
        target.PitchShiftPolicy = source.PitchShiftPolicy;
        target.PitchShiftMultiplier = source.PitchShiftMultiplier;
        target.SoundVolumes = new Dictionary<string, int>(source.SoundVolumes ?? AkronEarAid.CreateDefaultVolumes(), StringComparer.Ordinal);
        target.SoundVolumeOverrides = new Dictionary<string, bool>(source.SoundVolumeOverrides ?? AkronEarAid.CreateDefaultOverrideToggles(), StringComparer.Ordinal);
        target.AudioSplitter = source.AudioSplitter;
        target.AudioSplitterMainDevice = source.AudioSplitterMainDevice;
        target.AudioSplitterMusicDevice = source.AudioSplitterMusicDevice;
        target.AudioSplitterSfxDevice = source.AudioSplitterSfxDevice;
    }

    private static void CopyHudState(AkronProfileState target, AkronProfileState source) {
        CopyStateProperties(target, source, HudStateProperties);
    }

    private static void CopyStateProperties(AkronProfileState target, AkronProfileState source, IEnumerable<PropertyInfo> properties) {
        foreach (PropertyInfo property in properties) {
            property.SetValue(target, property.GetValue(source));
        }
    }

    private static PropertyInfo[] BuildStatePropertyList(params string[] names) {
        return names.Select(name => typeof(AkronProfileState).GetProperty(name)
            ?? throw new InvalidOperationException("Missing Akron profile state property: " + name)).ToArray();
    }

    private static List<AkronRectangleData> CopyRectangles(IEnumerable<AkronRectangleData> rectangles) {
        return (rectangles ?? Enumerable.Empty<AkronRectangleData>())
            .Where(rectangle => rectangle != null)
            .Select(rectangle => new AkronRectangleData {
                X = rectangle.X,
                Y = rectangle.Y,
                Width = rectangle.Width,
                Height = rectangle.Height
            })
            .ToList();
    }

    private static Dictionary<string, AkronButtonBindingPack> CaptureButtonBindings(AkronModuleSettings settings) {
        Dictionary<string, AkronButtonBindingPack> bindings = new Dictionary<string, AkronButtonBindingPack>(StringComparer.Ordinal);
        foreach (PropertyInfo property in ButtonBindingProperties) {
            bindings[property.Name] = CaptureButtonBinding((ButtonBinding) property.GetValue(settings));
        }

        return bindings;
    }

    private static AkronButtonBindingPack CaptureButtonBinding(ButtonBinding binding) {
        try {
            return new AkronButtonBindingPack {
                Keys = binding?.Keys?.Where(key => key != Keys.None).Select(key => key.ToString()).ToList() ?? new List<string>(),
                Buttons = binding?.Buttons?.Where(button => button != 0).Select(button => button.ToString()).ToList() ?? new List<string>(),
                MouseButtons = binding?.MouseButtons?.Select(button => button.ToString()).ToList() ?? new List<string>()
            };
        } catch (InvalidProgramException) {
            // Some test stubs for Everest's ButtonBinding expose invalid getters.
            // The real game binding object has normal mutable lists, but keeping
            // archive capture tolerant lets non-game tests exercise the rest of
            // the profile contract.
            return new AkronButtonBindingPack();
        }
    }

    private static void ApplyButtonBindings(AkronModuleSettings settings, Dictionary<string, AkronButtonBindingPack> bindings) {
        if (bindings == null) {
            return;
        }

        foreach (PropertyInfo property in ButtonBindingProperties) {
            if (bindings.TryGetValue(property.Name, out AkronButtonBindingPack binding)) {
                property.SetValue(settings, BuildButtonBinding(binding));
            }
        }
    }

    private static ButtonBinding BuildButtonBinding(AkronButtonBindingPack pack) {
        ButtonBinding binding = new ButtonBinding(0, Keys.None) {
            Keys = ParseEnumList<Keys>(pack?.Keys).Where(key => key != Keys.None).ToList(),
            Buttons = ParseEnumList<Buttons>(pack?.Buttons).Where(button => button != 0).ToList(),
            MouseButtons = ParseEnumList<MInput.MouseData.MouseButtons>(pack?.MouseButtons).ToList()
        };
        return binding;
    }

    private static List<T> ParseEnumList<T>(IEnumerable<string> values) where T : struct {
        List<T> parsed = new List<T>();
        foreach (string value in values ?? Array.Empty<string>()) {
            if (Enum.TryParse(value, ignoreCase: true, out T parsedValue)) {
                parsed.Add(parsedValue);
            }
        }

        return parsed;
    }

    private static Dictionary<int, AkronStartPosPackEntry> CaptureStartPositions(AkronModuleSession session) {
        Dictionary<int, AkronStartPosPackEntry> entries = new Dictionary<int, AkronStartPosPackEntry>();
        foreach (KeyValuePair<int, AkronStartPos> pair in session?.StartPositions ?? new Dictionary<int, AkronStartPos>()) {
            if (pair.Value == null) {
                continue;
            }

            entries[pair.Key] = new AkronStartPosPackEntry {
                X = pair.Value.Position.X,
                Y = pair.Value.Position.Y,
                Room = pair.Value.Room,
                AreaSid = pair.Value.AreaSid,
                UsesSpawnConfig = pair.Value.UsesSpawnConfig,
                Dashes = pair.Value.Dashes,
                StaminaPercent = pair.Value.StaminaPercent,
                Facing = pair.Value.Facing,
                Idle = pair.Value.Idle,
                Grab = pair.Value.Grab
            };
        }

        return entries;
    }

    private static Dictionary<int, AkronStartPos> BuildStartPositions(Dictionary<int, AkronStartPosPackEntry> entries) {
        Dictionary<int, AkronStartPos> startPositions = new Dictionary<int, AkronStartPos>();
        foreach (KeyValuePair<int, AkronStartPosPackEntry> pair in entries ?? new Dictionary<int, AkronStartPosPackEntry>()) {
            AkronStartPosPackEntry entry = pair.Value;
            if (entry == null) {
                continue;
            }

            startPositions[pair.Key] = new AkronStartPos {
                Position = new Vector2(entry.X, entry.Y),
                Room = entry.Room ?? string.Empty,
                AreaSid = entry.AreaSid ?? string.Empty,
                UsesSpawnConfig = entry.UsesSpawnConfig,
                Dashes = entry.Dashes,
                StaminaPercent = entry.StaminaPercent,
                Facing = entry.Facing,
                Idle = entry.Idle,
                Grab = entry.Grab,
                StateSlotName = string.Empty
            };
        }

        return startPositions;
    }

    private static string ResolveProfilePath(string pathOrName) {
        string trimmed = pathOrName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return string.Empty;
        }

        if (Path.IsPathRooted(trimmed) || File.Exists(trimmed)) {
            return trimmed;
        }

        string withExtension = trimmed.EndsWith(AkronArchive.Extension, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + AkronArchive.Extension;
        return Path.Combine(GetProfileDirectory(), withExtension);
    }

    private static string SanitizeFileName(string value) {
        string safe = new string((value ?? "profile")
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)
            .ToArray()).Trim('-', ' ');
        return string.IsNullOrWhiteSpace(safe) ? "profile" : safe;
    }
}
