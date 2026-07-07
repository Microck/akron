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

public sealed class AkronSetupPack {
    public string Format { get; set; } = AkronSetupPacks.SetupPackFormat;
    public string Name { get; set; } = "Akron Setup";
    public string CreatedUtc { get; set; } = string.Empty;
    public AkronSetupSection Section { get; set; } = AkronSetupSection.Whole;
    public AkronSetupState State { get; set; } = new AkronSetupState();
    public Dictionary<string, AkronButtonBindingPack> ButtonBindings { get; set; } = new Dictionary<string, AkronButtonBindingPack>();
    public Dictionary<string, string> MenuActionBindings { get; set; } = new Dictionary<string, string>();
    public Dictionary<int, AkronStartPosPackEntry> StartPositions { get; set; } = new Dictionary<int, AkronStartPosPackEntry>();
}

public enum AkronSetupSection {
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

public static partial class AkronSetupPacks {
    public const string SetupArchiveKind = "setup";
    public const string SetupArchivePayload = "setup.json";
    public const string SetupPackFormat = "akron-setup-v1";

    private const int MaxSetupPayloadBytes = 2 * 1024 * 1024;

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
        nameof(AkronSetupState.RoomLabels),
        nameof(AkronSetupState.LabelSystemVisible),
        nameof(AkronSetupState.RoomLabelColor),
        nameof(AkronSetupState.StaminaWidget),
        nameof(AkronSetupState.SpeedWidget),
        nameof(AkronSetupState.DashWidget),
        nameof(AkronSetupState.InputViewer),
        nameof(AkronSetupState.InputHistoryTextColor),
        nameof(AkronSetupState.InputHistoryEventColor),
        nameof(AkronSetupState.ShowTaps),
        nameof(AkronSetupState.TapDisplayCorner),
        nameof(AkronSetupState.TapDisplayScale),
        nameof(AkronSetupState.TapDisplayOpacity),
        nameof(AkronSetupState.InputBoardSource),
        nameof(AkronSetupState.InputBoardLabelPreset),
        nameof(AkronSetupState.InputBoardElements),
        nameof(AkronSetupState.InputsPerSecondCounter),
        nameof(AkronSetupState.InputsPerSecondPlacement),
        nameof(AkronSetupState.InputsPerSecondScale),
        nameof(AkronSetupState.InputsPerSecondOpacity),
        nameof(AkronSetupState.InputsPerSecondTextColor),
        nameof(AkronSetupState.InputsPerSecondShowTotal),
        nameof(AkronSetupState.InputsPerSecondShowMax),
        nameof(AkronSetupState.InputsPerSecondCountMovement),
        nameof(AkronSetupState.InputsPerSecondCountActions),
        nameof(AkronSetupState.InputsPerSecondCountMenu),
        nameof(AkronSetupState.RoomTimerWidget),
        nameof(AkronSetupState.RoomTimerColor),
        nameof(AkronSetupState.RoomStatTracker),
        nameof(AkronSetupState.RoomStatTrackerColor),
        nameof(AkronSetupState.RoomStatShowRoomName),
        nameof(AkronSetupState.RoomStatShowDeaths),
        nameof(AkronSetupState.RoomStatShowInGameTime),
        nameof(AkronSetupState.RoomStatShowStrawberries),
        nameof(AkronSetupState.RoomStatShowAliveTime),
        nameof(AkronSetupState.RoomStatHideIfGolden),
        nameof(AkronSetupState.RoomStatTimerFreezeMode),
        nameof(AkronSetupState.DeathStatsWidget),
        nameof(AkronSetupState.DeathStatsFormat),
        nameof(AkronSetupState.DeathStatsVisibility),
        nameof(AkronSetupState.DeathStatsColor),
        nameof(AkronSetupState.ResourceStaminaBar),
        nameof(AkronSetupState.StaminaBar),
        nameof(AkronSetupState.StaminaBarPlayer),
        nameof(AkronSetupState.StaminaBarHud),
        nameof(AkronSetupState.StaminaBarPlayerPosition),
        nameof(AkronSetupState.StaminaBarHudPosition),
        nameof(AkronSetupState.StaminaBarStyle),
        nameof(AkronSetupState.StaminaPlayerOffsetX),
        nameof(AkronSetupState.StaminaPlayerOffsetY),
        nameof(AkronSetupState.StaminaPlayerScale),
        nameof(AkronSetupState.StaminaAlwaysVisible),
        nameof(AkronSetupState.StaminaShowDangerMarker),
        nameof(AkronSetupState.StaminaShowChangePulse),
        nameof(AkronSetupState.StaminaShowOverflow),
        nameof(AkronSetupState.StaminaHideWhilePaused),
        nameof(AkronSetupState.StaminaHudOffsetX),
        nameof(AkronSetupState.StaminaHudOffsetY),
        nameof(AkronSetupState.StaminaNormalColor),
        nameof(AkronSetupState.StaminaLowColor),
        nameof(AkronSetupState.StaminaFillColor),
        nameof(AkronSetupState.StaminaLineColor),
        nameof(AkronSetupState.StaminaOverflowColor),
        nameof(AkronSetupState.DashBar),
        nameof(AkronSetupState.DashBarPlayer),
        nameof(AkronSetupState.DashBarHud),
        nameof(AkronSetupState.DashBarPlayerPosition),
        nameof(AkronSetupState.DashBarHudPosition),
        nameof(AkronSetupState.DashBarStyle),
        nameof(AkronSetupState.DashBarPlayerOffsetX),
        nameof(AkronSetupState.DashBarPlayerOffsetY),
        nameof(AkronSetupState.DashBarPlayerScale),
        nameof(AkronSetupState.DashBarAlwaysVisible),
        nameof(AkronSetupState.DashBarShowText),
        nameof(AkronSetupState.DashBarShowEmptyPips),
        nameof(AkronSetupState.DashBarHideWhilePaused),
        nameof(AkronSetupState.DashBarHudOffsetX),
        nameof(AkronSetupState.DashBarHudOffsetY),
        nameof(AkronSetupState.DashBarAvailableColor),
        nameof(AkronSetupState.DashBarEmptyColor),
        nameof(AkronSetupState.DashBarFillColor),
        nameof(AkronSetupState.DashBarLineColor),
        nameof(AkronSetupState.DashBarLowColor),
        nameof(AkronSetupState.DashNumber),
        nameof(AkronSetupState.DashNumberOffsetY),
        nameof(AkronSetupState.DashNumberColor),
        nameof(AkronSetupState.DashNumberOutlineColor),
        nameof(AkronSetupState.DashNumberOpacity),
        nameof(AkronSetupState.SpeedNumber),
        nameof(AkronSetupState.SpeedNumberMode),
        nameof(AkronSetupState.SpeedNumberOffsetY),
        nameof(AkronSetupState.SpeedNumberColor),
        nameof(AkronSetupState.SpeedNumberOutlineColor),
        nameof(AkronSetupState.SpeedNumberOpacity),
        nameof(AkronSetupState.TotalAttemptsWidget),
        nameof(AkronSetupState.TotalAttemptsColor),
        nameof(AkronSetupState.StatusLabelsWidget),
        nameof(AkronSetupState.StatusLabelsColor),
        nameof(AkronSetupState.ToastLabels),
        nameof(AkronSetupState.ToastLabelColor),
        nameof(AkronSetupState.ToastLabelAnchor),
        nameof(AkronSetupState.NoShortNumbers),
        nameof(AkronSetupState.HideVanillaHud),
        nameof(AkronSetupState.HideAkronHud),
        nameof(AkronSetupState.CustomHudLabels),
        nameof(AkronSetupState.CustomHudLabelsInNonLevelScenes),
        nameof(AkronSetupState.CustomHudLabelPadding),
        nameof(AkronSetupState.CustomHudLabelGap),
        nameof(AkronSetupState.CustomHudLabelObstructionEnabled),
        nameof(AkronSetupState.CustomHudLabelObstructionMode),
        nameof(AkronSetupState.CustomHudLabelObstructedOpacity),
        nameof(AkronSetupState.CustomHudLabelObstructionPaddingPixels),
        nameof(AkronSetupState.CustomHudLabelObstructionOnlyOverlappedLabel),
        nameof(AkronSetupState.CustomHudLabelObstructedAnchor),
        nameof(AkronSetupState.CustomHudLabelObstructedOffsetX),
        nameof(AkronSetupState.CustomHudLabelObstructedOffsetY),
        nameof(AkronSetupState.CustomHudLabelIndex),
        nameof(AkronSetupState.CustomHudLabelDefinitions),
        nameof(AkronSetupState.LabelRowOrder),
        nameof(AkronSetupState.LabelBulkStyle),
        nameof(AkronSetupState.RoomLabelStyle),
        nameof(AkronSetupState.InputHistoryLabelStyle),
        nameof(AkronSetupState.InputsPerSecondLabelStyle),
        nameof(AkronSetupState.StartPosLabelStyle),
        nameof(AkronSetupState.RoomTimerLabelStyle),
        nameof(AkronSetupState.DeathStatsLabelStyle),
        nameof(AkronSetupState.TotalAttemptsLabelStyle),
        nameof(AkronSetupState.StatusLabelsLabelStyle),
        nameof(AkronSetupState.ToastLabelStyle),
        nameof(AkronSetupState.HudCheatIndicator),
        nameof(AkronSetupState.HudCheatIndicatorOnlyFlagged),
        nameof(AkronSetupState.HudCheatIndicatorScale),
        nameof(AkronSetupState.HudCheatIndicatorOpacity),
        nameof(AkronSetupState.HudCheatIndicatorAnchor),
        nameof(AkronSetupState.HudCheatIndicatorStyle));

    public static AkronSetupPack Capture(AkronModuleSettings settings, AkronModuleSession session, string name = "", AkronSetupSection section = AkronSetupSection.Whole) {
        if (settings == null) {
            throw new ArgumentNullException(nameof(settings));
        }

        string created = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        section = NormalizeSection(section);
        return new AkronSetupPack {
            Name = BuildPackName(settings, name, section),
            CreatedUtc = created,
            Section = section,
            State = settings.CaptureSetupPackState(),
            ButtonBindings = CaptureButtonBindings(settings),
            MenuActionBindings = new Dictionary<string, string>(settings.MenuActionBindings ?? new Dictionary<string, string>(), StringComparer.Ordinal),
            StartPositions = CaptureStartPositions(session)
        };
    }

    public static void Apply(AkronModuleSettings settings, AkronModuleSession session, AkronSetupPack pack, AkronSetupSection? requestedSection = null) {
        if (settings == null) {
            throw new ArgumentNullException(nameof(settings));
        }

        if (pack == null || !string.Equals(pack.Format, SetupPackFormat, StringComparison.Ordinal)) {
            throw new InvalidDataException("Unsupported setup pack.");
        }

        AkronSetupSection section = NormalizeSection(requestedSection ?? pack.Section);
        if (section == AkronSetupSection.Whole) {
            settings.ApplySetupPackState(pack.State);
            ApplyButtonBindings(settings, pack.ButtonBindings);
            settings.MenuActionBindings = new Dictionary<string, string>(pack.MenuActionBindings ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            if (session != null) {
                AkronActions.ReplaceAllStartPositions(BuildStartPositions(pack.StartPositions), session);
            }
            return;
        }

        AkronSetupState merged = settings.CaptureSetupPackState();
        ApplyStateSection(merged, pack.State, section);
        settings.ApplySetupPackState(merged);
        if (section == AkronSetupSection.Keybinds) {
            ApplyButtonBindings(settings, pack.ButtonBindings);
            settings.MenuActionBindings = new Dictionary<string, string>(pack.MenuActionBindings ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        } else if (section == AkronSetupSection.StartPos && session != null) {
            AkronActions.ReplaceAllStartPositions(MergeScopedStartPositions(session.StartPositions, pack.StartPositions), session);
        }
    }

    public static string ExportCurrent(string name = "", AkronSetupSection section = AkronSetupSection.Whole) {
        section = NormalizeSection(section);
        AkronSetupPack pack = Capture(AkronModule.Settings, AkronModule.Session, name, section);
        Directory.CreateDirectory(GetSetupDirectory());
        string fileName = SanitizeFileName(pack.Name) + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + AkronArchive.Extension;
        string path = Path.Combine(GetSetupDirectory(), fileName);
        Write(AkronModule.Settings, AkronModule.Session, path, name, section);
        Engine.Scene?.Add(new AkronToast("Exported " + FormatSection(section) + " setup " + pack.Name + "."));
        return path;
    }

    public static bool Import(string pathOrName, AkronSetupSection? section = null) {
        string path = ResolveSetupPath(pathOrName);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            Engine.Scene?.Add(new AkronToast("Setup pack not found."));
            return false;
        }

        AkronSetupPack pack;
        try {
            pack = Read(path);
            Apply(AkronModule.Settings, AkronModule.Session, pack, section);
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is JsonException || ex is IOException || ex is UnauthorizedAccessException) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to import Akron setup archive: " + ex.Message);
            Engine.Scene?.Add(new AkronToast("Unsupported setup pack."));
            return false;
        }

        Engine.Scene?.Add(new AkronToast("Imported " + FormatSection(NormalizeSection(section ?? pack.Section)) + " setup " + pack.Name + "."));
        return true;
    }

    public static string ImportLatest(AkronSetupSection? section = null) {
        string path = Directory.Exists(GetSetupDirectory())
            ? Directory.GetFiles(GetSetupDirectory(), "*" + AkronArchive.Extension, SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (string.IsNullOrWhiteSpace(path)) {
            Engine.Scene?.Add(new AkronToast("No setup packs found."));
            return string.Empty;
        }

        Import(path, section);
        return path;
    }

    public static bool ImportFromFileBrowser(AkronSetupSection? section = null) {
        string directory = GetSetupDirectory();
        Directory.CreateDirectory(directory);

        AkronSetupFilePickerResult result = TryPickSetupArchive(directory, out string path, out string error);
        if (result == AkronSetupFilePickerResult.Selected) {
            return Import(path, section);
        }

        if (result == AkronSetupFilePickerResult.Canceled) {
            Engine.Scene?.Add(new AkronToast("No setup pack selected."));
            return false;
        }

        Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to open Akron setup file browser: " + error);
        Engine.Scene?.Add(new AkronToast("Could not open setup file browser."));
        return false;
    }

    public static void Write(AkronModuleSettings settings, AkronModuleSession session, string path, string name = "", AkronSetupSection section = AkronSetupSection.Whole) {
        AkronSetupPack pack = Capture(settings, session, name, section);
        AkronArchive.WriteSinglePayloadArchive(
            path,
            new AkronArchiveManifest {
                Kind = SetupArchiveKind,
                KindVersion = 1,
                CreatedAt = pack.CreatedUtc,
                Target = new AkronArchiveTarget {
                    Game = "Celeste",
                    MapSid = ResolveArchiveMapSid(session)
                }
            },
            SetupArchivePayload,
            JsonSerializer.Serialize(pack, JsonOptions));
    }

    public static AkronSetupPack Read(string path) {
        string payload = AkronArchive.ReadSinglePayloadArchive(path, SetupArchiveKind, SetupArchivePayload, MaxSetupPayloadBytes, out _);
        AkronSetupPack pack = JsonSerializer.Deserialize<AkronSetupPack>(payload, JsonOptions);
        if (pack == null || !string.Equals(pack.Format, SetupPackFormat, StringComparison.Ordinal) || pack.State == null) {
            throw new InvalidDataException("Unsupported setup pack.");
        }

        return pack;
    }

    private static string ResolveArchiveMapSid(AkronModuleSession session) {
        string currentLevelMapSid = TryGetCurrentLevelMapSid();
        if (!string.IsNullOrWhiteSpace(currentLevelMapSid)) {
            return currentLevelMapSid;
        }

        if (session?.StartPositions == null) {
            return string.Empty;
        }

        HashSet<string> areaSids = new HashSet<string>(StringComparer.Ordinal);
        foreach (AkronStartPos startPos in session.StartPositions.Values) {
            if (!string.IsNullOrWhiteSpace(startPos?.AreaSid)) {
                areaSids.Add(startPos.AreaSid);
            }
        }

        return areaSids.Count == 1 ? areaSids.First() : string.Empty;
    }

    private static string TryGetCurrentLevelMapSid() {
        try {
            return Engine.Scene is Level level ? level.Session?.Area.GetSID() ?? string.Empty : string.Empty;
        } catch (Exception exception) when (exception is InvalidProgramException || exception is NullReferenceException) {
            return string.Empty;
        }
    }

    public static string FormatSection(AkronSetupSection section) {
        return NormalizeSection(section) switch {
            AkronSetupSection.StartPos => "StartPos",
            AkronSetupSection.Keybinds => "Keybinds",
            AkronSetupSection.AutoKill => "Auto Kill",
            AkronSetupSection.AutoDeafen => "Auto Deafen",
            AkronSetupSection.Recorder => "Recorder",
            AkronSetupSection.Audio => "Audio",
            AkronSetupSection.Hud => "HUD",
            _ => "Whole"
        };
    }

    public static bool TryParseSection(string value, out AkronSetupSection section) {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant().Replace("_", "-").Replace(" ", "-")) {
            case "":
            case "whole":
            case "all":
            case "setup":
                section = AkronSetupSection.Whole;
                return true;
            case "startpos":
            case "start-pos":
                section = AkronSetupSection.StartPos;
                return true;
            case "keybind":
            case "keybinds":
            case "bindings":
                section = AkronSetupSection.Keybinds;
                return true;
            case "autokill":
            case "auto-kill":
                section = AkronSetupSection.AutoKill;
                return true;
            case "autodeafen":
            case "auto-deafen":
                section = AkronSetupSection.AutoDeafen;
                return true;
            case "recorder":
            case "recording":
                section = AkronSetupSection.Recorder;
                return true;
            case "audio":
            case "sound":
                section = AkronSetupSection.Audio;
                return true;
            case "hud":
            case "labels":
            case "label":
            case "custom-hud":
            case "custom-labels":
                section = AkronSetupSection.Hud;
                return true;
            default:
                section = AkronSetupSection.Whole;
                return false;
        }
    }

    public static string GetSetupDirectory() {
        return Path.Combine(Everest.PathGame, "Saves", "AkronSetups");
    }

    private static AkronSetupSection NormalizeSection(AkronSetupSection section) {
        return Enum.IsDefined(typeof(AkronSetupSection), section) ? section : AkronSetupSection.Whole;
    }

    private static string BuildPackName(AkronModuleSettings settings, string name, AkronSetupSection section) {
        if (!string.IsNullOrWhiteSpace(name)) {
            return name.Trim();
        }

        return section == AkronSetupSection.Whole
            ? "Akron Setup"
            : "Akron " + FormatSection(section) + " Setup";
    }

    private static void ApplyStateSection(AkronSetupState target, AkronSetupState source, AkronSetupSection section) {
        if (target == null || source == null) {
            return;
        }

        switch (section) {
            case AkronSetupSection.StartPos:
                CopyStartPosState(target, source);
                break;
            case AkronSetupSection.AutoKill:
                CopyAutoKillState(target, source);
                break;
            case AkronSetupSection.AutoDeafen:
                CopyAutoDeafenState(target, source);
                break;
            case AkronSetupSection.Recorder:
                CopyRecorderState(target, source);
                break;
            case AkronSetupSection.Audio:
                CopyAudioState(target, source);
                break;
            case AkronSetupSection.Hud:
                CopyHudState(target, source);
                break;
        }
    }

    private static void CopyStartPosState(AkronSetupState target, AkronSetupState source) {
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

    private static void CopyAutoKillState(AkronSetupState target, AkronSetupState source) {
        target.AutoKill = source.AutoKill;
        target.AutoKillTimer = source.AutoKillTimer;
        target.AutoKillSeconds = source.AutoKillSeconds;
        target.AutoKillArea = source.AutoKillArea;
        target.AutoKillShowArea = source.AutoKillShowArea;
        target.AutoKillShowAreaOnDeath = source.AutoKillShowAreaOnDeath;
        target.AutoKillDefaultAreaConditions = CopyAutoKillArea(source.AutoKillDefaultAreaConditions ?? new AkronAutoKillAreaData());
        target.AutoKillAreas = CopyAutoKillAreas(source.AutoKillAreas);
        target.AutoKillAreaX = source.AutoKillAreaX;
        target.AutoKillAreaY = source.AutoKillAreaY;
        target.AutoKillAreaWidth = source.AutoKillAreaWidth;
        target.AutoKillAreaHeight = source.AutoKillAreaHeight;
    }

    private static void CopyAutoDeafenState(AkronSetupState target, AkronSetupState source) {
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

    private static void CopyRecorderState(AkronSetupState target, AkronSetupState source) {
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

    private static void CopyAudioState(AkronSetupState target, AkronSetupState source) {
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

    private static void CopyHudState(AkronSetupState target, AkronSetupState source) {
        CopyStateProperties(target, source, HudStateProperties);
    }

    private static void CopyStateProperties(AkronSetupState target, AkronSetupState source, IEnumerable<PropertyInfo> properties) {
        foreach (PropertyInfo property in properties) {
            property.SetValue(target, property.GetValue(source));
        }
    }

    private static PropertyInfo[] BuildStatePropertyList(params string[] names) {
        return names.Select(name => typeof(AkronSetupState).GetProperty(name)
            ?? throw new InvalidOperationException("Missing Akron setup state property: " + name)).ToArray();
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

    private static List<AkronAutoKillAreaData> CopyAutoKillAreas(IEnumerable<AkronAutoKillAreaData> areas) {
        return (areas ?? Enumerable.Empty<AkronAutoKillAreaData>())
            .Where(area => area != null)
            .Select(CopyAutoKillArea)
            .ToList();
    }

    private static AkronAutoKillAreaData CopyAutoKillArea(AkronAutoKillAreaData area) {
        return new AkronAutoKillAreaData(area);
    }

    private static Dictionary<string, AkronButtonBindingPack> CaptureButtonBindings(AkronModuleSettings settings) {
        Dictionary<string, AkronButtonBindingPack> bindings = new Dictionary<string, AkronButtonBindingPack>(StringComparer.Ordinal);
        foreach (PropertyInfo property in ButtonBindingProperties) {
            bindings[property.Name] = CaptureButtonBinding((ButtonBinding)property.GetValue(settings));
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
        }
        catch (InvalidProgramException) {
            // Some test stubs for Everest's ButtonBinding expose invalid getters.
            // The real game binding object has normal mutable lists, but keeping
            // archive capture tolerant lets non-game tests exercise the rest of
            // the setup contract.
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

    private static Dictionary<int, AkronStartPos> MergeScopedStartPositions(
        Dictionary<int, AkronStartPos> existing,
        Dictionary<int, AkronStartPosPackEntry> entries) {
        Dictionary<int, AkronStartPos> imported = BuildStartPositions(entries);
        HashSet<string> importedAreaSids = new HashSet<string>(
            imported.Values.Select(startPos => startPos?.AreaSid ?? string.Empty),
            StringComparer.Ordinal);
        if (importedAreaSids.Count == 0) {
            return new Dictionary<int, AkronStartPos>(existing ?? new Dictionary<int, AkronStartPos>());
        }

        Dictionary<int, AkronStartPos> merged = new Dictionary<int, AkronStartPos>();
        foreach (KeyValuePair<int, AkronStartPos> pair in existing ?? new Dictionary<int, AkronStartPos>()) {
            if (pair.Value != null && importedAreaSids.Contains(pair.Value.AreaSid ?? string.Empty)) {
                continue;
            }

            merged[pair.Key] = pair.Value;
        }

        foreach (KeyValuePair<int, AkronStartPos> pair in imported) {
            int slot = NextAvailableStartPosSlot(merged, pair.Key);
            merged[slot] = pair.Value;
        }

        return merged;
    }

    private static int NextAvailableStartPosSlot(Dictionary<int, AkronStartPos> startPositions, int preferredSlot) {
        int slot = Math.Max(1, preferredSlot);
        while (startPositions.ContainsKey(slot)) {
            slot++;
        }
        return slot;
    }

    private static string ResolveSetupPath(string pathOrName) {
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
        return Path.Combine(GetSetupDirectory(), withExtension);
    }

    private static string SanitizeFileName(string value) {
        string safe = new string((value ?? "setup")
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)
            .ToArray()).Trim('-', ' ');
        return string.IsNullOrWhiteSpace(safe) ? "setup" : safe;
    }
}
