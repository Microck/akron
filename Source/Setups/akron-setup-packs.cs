using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    [JsonIgnore]
    public string ArchiveMapSid { get; set; } = string.Empty;

    [JsonIgnore]
    internal string ArchivePath { get; set; } = string.Empty;

    [JsonIgnore]
    internal Dictionary<int, string> SnapshotSourcePaths { get; set; } = new Dictionary<int, string>();
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

// A pack payload written against a different setup contract than this build reads.
//
// Its own type, and not an InvalidDataException like the reader's other refusals,
// because the import path has to tell it apart to say what happened instead of the flat
// "Unsupported setup pack." a malformed archive gets: this pack is not damaged, it was
// written by an Akron that captured StartPos room state against a fresh room this build
// no longer produces, so its snapshots describe objects that are not where it says they
// are. InvalidDataException is sealed, so this cannot derive from it; every catch that
// handles a pack read names this type as well.
public sealed class AkronSetupPackFormatException : Exception {
    public AkronSetupPackFormatException(string message) : base(message) {
    }
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
    public string SnapshotEntry { get; set; } = string.Empty;
    public string SnapshotSha256 { get; set; } = string.Empty;
}

public static partial class AkronSetupPacks {
    public const string SetupArchiveKind = "setup";
    public const string SetupArchivePayload = "setup.json";
    // Bumped in lockstep with AkronReconstructionDocument.CurrentFormat, because a
    // StartPos or Whole pack carries one of those documents per slot as an attachment.
    // The documents address room objects by their place in a clean reload of the room,
    // and Akron now rebuilds that room differently, so a pack written by an older build
    // describes a room this build does not produce.
    //
    // Bumped for every pack rather than only for the ones carrying StartPos slots. The
    // payload has one format string covering the whole pack, a Whole pack always carries
    // StartPos, and one story - older setups must be recreated in the current build - is
    // easier to act on than a rule about which sections survive.
    //
    // v4 -> v5: fresh-room baseline changed, so the v7 snapshots inside a v4 pack cannot
    // be rebuilt here.
    // v5 -> v6: snapshots now carry the capture-side identity evidence a rebuild needs -
    // whether a saved resource's key names it, and, for the room half of a snapshot,
    // whether the map laid a saved entity's id out - so the v8 snapshots inside a v5
    // pack cannot be rebuilt here either.
    public const string SetupPackFormat = "akron-setup-v9";

    public const int MaxStartPositions = 99;
    public const int MaxAutoKillAreas = 128;
    public const int MaxAutoDeafenAreas = 128;
    public const int MaxCustomHudLabels = 64;
    public const int MaxInputBoardElements = AkronInputBoard.MaximumElements;
    public const int MaxLabelRowOrder = 128;
    public const int MaxButtonBindings = 128;
    public const int MaxBindingInputs = 16;
    public const int MaxMenuActionBindings = 256;
    public const int MaxAudioDictionaryEntries = 64;
    // The recorder's own clamps are the contract: a value Akron accepts must survive a
    // Recorder pack round trip, so these mirror AkronModuleSettings.ClampRecording*.
    public const int MaxPortableRecordingWidth = 15360;
    public const int MaxPortableRecordingHeight = 8640;
    public const long MaxPortableRecordingPixels = 15360L * 8640L;
    public const int MaxPortableRecordingFramerate = 360;
    public const int MaxPortableRecordingBitrateMbps = 1000;
    public const int MaxPortableReplayBufferSeconds = 600;
    public const int MaxPortableClipSeconds = 120;
    public const int MaxPortableKeyframeSeconds = 20;
    public const float MaxPortableEndscreenSeconds = 30f;
    public const float MaxPortableStartPosCoordinate = 16_777_216f;

    private const int MaxSetupPayloadBytes = 2 * 1024 * 1024;
    internal const int MaxSnapshotAttachmentBytes = 128 * 1024 * 1024;
    // Leave room for setup.json, manifest.json, and ZIP metadata so the complete
    // archive stays within the community pack reader's 512 MiB file limit.
    internal const long MaxSnapshotAttachmentsBytes = 509L * 1024L * 1024L;
    private const long MaxDecompressedSnapshotBytes = AkronStartPosReconstruction.MaxDecompressedSnapshotBytes;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    private static readonly PropertyInfo[] ButtonBindingProperties = typeof(AkronModuleSettings)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property.PropertyType == typeof(ButtonBinding) && property.CanRead && property.CanWrite)
        .OrderBy(property => property.Name, StringComparer.Ordinal)
        .ToArray();

    private static readonly PropertyInfo[] StartPosStateProperties = BuildStatePropertyList(
        nameof(AkronSetupState.SmartStartPos),
        nameof(AkronSetupState.RespawnAtStartPos),
        nameof(AkronSetupState.StartPosWaitForInput),
        nameof(AkronSetupState.StartPosShowLabel),
        nameof(AkronSetupState.StartPosLabelColor),
        nameof(AkronSetupState.StartPosLabelAnchor),
        nameof(AkronSetupState.StartPosLabelFormat),
        nameof(AkronSetupState.StartPosLabelStyle),
        nameof(AkronSetupState.StartPosMousePlacement),
        nameof(AkronSetupState.StartPosPlacementPanelX),
        nameof(AkronSetupState.StartPosPlacementPanelY),
        nameof(AkronSetupState.StartPosPlacementPanelMinimized),
        nameof(AkronSetupState.StartPosPreviewOpacity),
        nameof(AkronSetupState.StartPosConfiguredDashes),
        nameof(AkronSetupState.StartPosConfiguredStaminaPercent),
        nameof(AkronSetupState.StartPosConfiguredFacing),
        nameof(AkronSetupState.StartPosConfiguredIdle),
        nameof(AkronSetupState.StartPosConfiguredGrab),
        nameof(AkronSetupState.StartPosSlotCount));

    private static readonly PropertyInfo[] AutoKillStateProperties = BuildStatePropertyList(
        nameof(AkronSetupState.AutoKill),
        nameof(AkronSetupState.AutoKillTimer),
        nameof(AkronSetupState.AutoKillSeconds),
        nameof(AkronSetupState.AutoKillArea),
        nameof(AkronSetupState.AutoKillShowArea),
        nameof(AkronSetupState.AutoKillShowAreaOnDeath),
        nameof(AkronSetupState.AutoKillDefaultAreaConditions),
        nameof(AkronSetupState.AutoKillAreas),
        nameof(AkronSetupState.AutoKillAreaX),
        nameof(AkronSetupState.AutoKillAreaY),
        nameof(AkronSetupState.AutoKillAreaWidth),
        nameof(AkronSetupState.AutoKillAreaHeight));

    private static readonly PropertyInfo[] AutoDeafenStateProperties = BuildStatePropertyList(
        nameof(AkronSetupState.AutoDeafen),
        nameof(AkronSetupState.AutoDeafenArea),
        nameof(AkronSetupState.AutoDeafenShowArea),
        nameof(AkronSetupState.AutoDeafenAreas),
        nameof(AkronSetupState.AutoDeafenAreaX),
        nameof(AkronSetupState.AutoDeafenAreaY),
        nameof(AkronSetupState.AutoDeafenAreaWidth),
        nameof(AkronSetupState.AutoDeafenAreaHeight));

    private static readonly PropertyInfo[] RecorderStateProperties = BuildStatePropertyList(
        nameof(AkronSetupState.RecordingContainerFormat),
        nameof(AkronSetupState.RecordingReplayBufferSeconds),
        nameof(AkronSetupState.RecordingTriggerLastDeath),
        nameof(AkronSetupState.RecordingTriggerRespawnToDeath),
        nameof(AkronSetupState.RecordingTriggerRoomEntryToClear),
        nameof(AkronSetupState.RecordingTriggerCheckpointClear),
        nameof(AkronSetupState.RecordingTriggerBerryCollect),
        nameof(AkronSetupState.RecordingTriggerGoldenDeath),
        nameof(AkronSetupState.RecordingPreRollSeconds),
        nameof(AkronSetupState.RecordingPostRollSeconds),
        nameof(AkronSetupState.RecordingAudioFullMixTrack),
        nameof(AkronSetupState.RecordingAudioMusicTrack),
        nameof(AkronSetupState.RecordingAudioSfxTrack),
        nameof(AkronSetupState.RecordingAudioAmbienceTrack),
        nameof(AkronSetupState.RecordingRecordMutedAudio),
        nameof(AkronSetupState.RecordingAudioFullMixLevel),
        nameof(AkronSetupState.RecordingAudioMusicLevel),
        nameof(AkronSetupState.RecordingAudioSfxLevel),
        nameof(AkronSetupState.RecordingAudioAmbienceLevel),
        nameof(AkronSetupState.RecordingQualityPreset),
        nameof(AkronSetupState.RecordingRateControl),
        nameof(AkronSetupState.RecordingKeyframeIntervalSeconds),
        nameof(AkronSetupState.RecordingDroppedFrameWarning),
        nameof(AkronSetupState.RecordingAutoRemux),
        nameof(AkronSetupState.RecordingClipBrowserSort),
        nameof(AkronSetupState.RecordingClipBrowserFilter),
        nameof(AkronSetupState.RecordingFramerate),
        nameof(AkronSetupState.RecordingEndscreenDurationSeconds),
        nameof(AkronSetupState.RecordingBitrateMbps),
        nameof(AkronSetupState.RecordingResolutionX),
        nameof(AkronSetupState.RecordingResolutionY),
        nameof(AkronSetupState.RecordingHidePreview),
        nameof(AkronSetupState.RecordingCodec),
        nameof(AkronSetupState.RecordingPreset));

    private static readonly PropertyInfo[] AudioStateProperties = BuildStatePropertyList(
        nameof(AkronSetupState.AudioSpeed),
        nameof(AkronSetupState.AudioSpeedPolicy),
        nameof(AkronSetupState.AudioSpeedMultiplier),
        nameof(AkronSetupState.PitchShift),
        nameof(AkronSetupState.PitchShiftPolicy),
        nameof(AkronSetupState.PitchShiftMultiplier),
        nameof(AkronSetupState.SoundVolumes),
        nameof(AkronSetupState.SoundVolumeOverrides));

    private static readonly HashSet<string> UnsafeWholeStatePropertyNames = new HashSet<string>(StringComparer.Ordinal) {
        nameof(AkronSetupState.ScreenshotScannerExportPath),
        nameof(AkronSetupState.RecordingOutputFolder),
        nameof(AkronSetupState.RecordingFilenameTemplate),
        nameof(AkronSetupState.RecordingReplayAutoStart),
        nameof(AkronSetupState.RecordingColorspaceArgs),
        nameof(AkronSetupState.AudioSplitter),
        nameof(AkronSetupState.AudioSplitterMainDevice),
        nameof(AkronSetupState.AudioSplitterMusicDevice),
        nameof(AkronSetupState.AudioSplitterSfxDevice),
        nameof(AkronSetupState.AutoDeafenHotkey)
    };

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
        string mapSid = ResolveArchiveMapSid(session);
        Dictionary<int, string> snapshotSourcePaths = new Dictionary<int, string>();
        Dictionary<int, AkronStartPosPackEntry> startPositions = section is AkronSetupSection.StartPos or AkronSetupSection.Whole
            ? CaptureStartPositions(session, mapSid, out snapshotSourcePaths)
            : new Dictionary<int, AkronStartPosPackEntry>();
        return new AkronSetupPack {
            Name = BuildPackName(settings, name, section),
            CreatedUtc = created,
            Section = section,
            State = settings.CaptureSetupPackState(),
            // Capture the complete in-memory setup so callers can re-scope it.
            // SerializePortablePack strips fields outside the archive section.
            ButtonBindings = CaptureButtonBindings(settings),
            MenuActionBindings = new Dictionary<string, string>(settings.MenuActionBindings ?? new Dictionary<string, string>(), StringComparer.Ordinal),
            StartPositions = startPositions,
            SnapshotSourcePaths = snapshotSourcePaths,
            ArchiveMapSid = mapSid
        };
    }

    public static void Apply(AkronModuleSettings settings, AkronModuleSession session, AkronSetupPack pack, AkronSetupSection? requestedSection = null) {
        Apply(
            settings,
            session,
            pack,
            requestedSection,
            () => AkronModule.Instance == null || AkronActions.SaveAkronStartPosData());
    }

    internal static void Apply(
        AkronModuleSettings settings,
        AkronModuleSession session,
        AkronSetupPack pack,
        AkronSetupSection? requestedSection,
        Func<bool> persistStartPosMetadata
    ) {
        if (settings == null) {
            throw new ArgumentNullException(nameof(settings));
        }

        RequireCurrentPackFormat(pack);

        AkronSetupSection section = NormalizeSection(requestedSection ?? pack.Section);
        string targetMapSid = ResolvePackTargetMapSid(pack);
        pack.ArchiveMapSid = targetMapSid;
        ValidatePortablePack(pack, section, targetMapSid);
        int[] existingTargetSlots = (session?.StartPositions ?? new Dictionary<int, AkronStartPos>())
            .Where(pair => string.Equals(pair.Value?.AreaSid, targetMapSid, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .Concat(AkronModule.Instance == null
                ? Enumerable.Empty<int>()
                : AkronActions.GetStartPositionsForArea(targetMapSid).Select(pair => pair.Key))
            .Distinct()
            .ToArray();
        using SetupImportStateTransaction stateTransaction = section is AkronSetupSection.StartPos or AkronSetupSection.Whole
            ? new SetupImportStateTransaction(settings, session, targetMapSid)
            : null;
        using PreparedStartPosImport prepared = section is AkronSetupSection.StartPos or AkronSetupSection.Whole
            ? PrepareStartPosImport(pack, targetMapSid)
            : null;
        if (section == AkronSetupSection.Whole) {
            Dictionary<int, AkronStartPos> imported = null;
            if (session != null && !string.IsNullOrWhiteSpace(pack.ArchiveMapSid)) {
                imported = BuildStartPositions(pack.StartPositions, pack.ArchiveMapSid);
                // Install remains reversible until the matching metadata and
                // settings have committed below.
                prepared?.Install(previousSlots: existingTargetSlots);
            }
            ApplyPortableWholeState(settings, pack.State);
            ApplyButtonBindings(settings, pack.ButtonBindings);
            settings.MenuActionBindings = new Dictionary<string, string>(pack.MenuActionBindings ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            if (imported != null) {
                AkronActions.ReplaceAllStartPositions(
                    imported,
                    session,
                    pack.ArchiveMapSid,
                    persistMetadata: false,
                    replacementTransaction: stateTransaction?.StartPosReplacement);
                if (!persistStartPosMetadata()) {
                    throw new IOException("Could not persist imported StartPos metadata.");
                }
                prepared?.Commit();
            }
            stateTransaction?.Commit();
            if (imported != null && prepared != null && AkronModule.Instance != null) {
                AkronActions.RefreshStartPositionsAfterSnapshotImport(pack.ArchiveMapSid, session);
            }
            return;
        }

        Dictionary<int, AkronStartPos> mergedStartPositions = null;
        Dictionary<int, AkronStartPos> importedForMap = null;
        if (section == AkronSetupSection.StartPos && session != null && !string.IsNullOrWhiteSpace(pack.ArchiveMapSid)) {
            Dictionary<int, AkronStartPos> imported = BuildStartPositions(pack.StartPositions, pack.ArchiveMapSid);
            mergedStartPositions = MergeScopedStartPositions(
                session.StartPositions,
                imported,
                pack.ArchiveMapSid,
                out Dictionary<int, int> importedSlotMap);
            // A failed file move must leave both the old files and the old
            // in-memory setup untouched.
            prepared?.Install(importedSlotMap, existingTargetSlots);
            importedForMap = mergedStartPositions
                .Where(pair => string.Equals(pair.Value?.AreaSid, pack.ArchiveMapSid, StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }
        AkronSetupState merged = settings.CaptureSetupPackState();
        ApplyStateSection(merged, pack.State, section);
        settings.ApplySetupPackState(merged);
        if (section == AkronSetupSection.Keybinds) {
            ApplyButtonBindings(settings, pack.ButtonBindings);
            settings.MenuActionBindings = new Dictionary<string, string>(pack.MenuActionBindings ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        } else if (importedForMap != null) {
            AkronActions.ReplaceAllStartPositions(
                importedForMap,
                targetSession: null,
                targetAreaSid: pack.ArchiveMapSid,
                persistMetadata: false,
                replacementTransaction: stateTransaction?.StartPosReplacement);
            session.StartPositions = mergedStartPositions;
            if (!persistStartPosMetadata()) {
                throw new IOException("Could not persist imported StartPos metadata.");
            }
            prepared?.Commit();
        }
        stateTransaction?.Commit();
        if (importedForMap != null && prepared != null && AkronModule.Instance != null) {
            AkronActions.RefreshStartPositionsAfterSnapshotImport(pack.ArchiveMapSid, session);
        }
    }

    public static string ExportCurrent(string name = "", AkronSetupSection section = AkronSetupSection.Whole) {
        section = NormalizeSection(section);
        try {
            AkronSetupPack pack = Capture(AkronModule.Settings, AkronModule.Session, name, section);
            Directory.CreateDirectory(GetSetupDirectory());
            string fileName = SanitizeFileName(pack.Name) + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + AkronArchive.Extension;
            string path = Path.Combine(GetSetupDirectory(), fileName);
            Write(AkronModule.Settings, AkronModule.Session, path, name, section);
            Engine.Scene?.Add(new AkronToast("Exported " + FormatSection(section) + " setup " + pack.Name + "."));
            return path;
        } catch (Exception exception) when (exception is InvalidDataException || exception is AkronSetupPackFormatException || exception is IOException || exception is UnauthorizedAccessException) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to export Akron setup archive: " + exception.Message);
            Engine.Scene?.Add(new AkronToast("Could not export setup pack."));
            return string.Empty;
        }
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
        // A pack from a different build is refused for a reason the player can act on,
        // and it is the one refusal every player gets after a format bump, so it says
        // what happened rather than sharing the flat message a damaged archive gets.
        catch (AkronSetupPackFormatException ex) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to import Akron setup archive: " + ex.Message);
            Engine.Scene?.Add(new AkronToast(ex.Message));
            return false;
        }
        // Every InvalidDataException raised while reading a pack is a sentence written for the
        // player (checksum, wrong map, too many areas, bad binding), so it is shown as is.
        catch (InvalidDataException ex) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to import Akron setup archive: " + ex.Message);
            Engine.Scene?.Add(new AkronToast(ex.Message));
            return false;
        }
        catch (JsonException ex) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to import Akron setup archive: " + ex.Message);
            Engine.Scene?.Add(new AkronToast("Setup pack is not valid JSON."));
            return false;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to import Akron setup archive: " + ex.Message);
            Engine.Scene?.Add(new AkronToast("Could not read setup pack."));
            return false;
        }

        AkronSetupSection imported = NormalizeSection(section ?? pack.Section);
        Engine.Scene?.Add(new AkronToast("Imported " + FormatSection(imported) + " setup " + pack.Name + "."));
        if (PackOverlayToggleWasReplaced(pack, imported)) {
            Engine.Scene?.Add(new AkronToast("The pack had no usable Open Menu binding, so Tab was restored."));
        }
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
        ValidatePortablePack(pack, pack.Section, ResolveArchiveMapSid(session));
        WriteArchive(
            path,
            pack,
            new AkronArchiveManifest {
                Kind = SetupArchiveKind,
                KindVersion = 1,
                CreatedAt = pack.CreatedUtc,
                Target = new AkronArchiveTarget {
                    Game = "Celeste",
                    MapSid = pack.ArchiveMapSid
                }
            });
    }

    internal static string SerializePackPayloadForArchive(AkronSetupPack pack) {
        string payload = SerializePortablePack(pack);
        if (Encoding.UTF8.GetByteCount(payload) <= MaxSetupPayloadBytes) {
            return payload;
        }

        throw new InvalidDataException("Setup archive payload is too large.");
    }

    internal static void WriteArchive(string path, AkronSetupPack pack, AkronArchiveManifest manifest) {
        HashSet<string> expectedEntries = GetExpectedSnapshotEntries(pack);
        Dictionary<string, string> attachments = new Dictionary<string, string>(StringComparer.Ordinal);
        long attachmentBytes = 0;
        foreach (KeyValuePair<int, AkronStartPosPackEntry> pair in pack.StartPositions ?? new Dictionary<int, AkronStartPosPackEntry>()) {
            if (!pack.SnapshotSourcePaths.TryGetValue(pair.Key, out string snapshotPath) || !File.Exists(snapshotPath)) {
                throw new InvalidDataException("StartPos slot " + pair.Key.ToString(CultureInfo.InvariantCulture) + " has no exact snapshot to export.");
            }
            long snapshotBytes = new FileInfo(snapshotPath).Length;
            if (snapshotBytes > MaxSnapshotAttachmentBytes) {
                throw new InvalidDataException(
                    "StartPos slot " + pair.Key.ToString(CultureInfo.InvariantCulture) + " snapshot is too large to export.");
            }
            if (attachmentBytes > MaxSnapshotAttachmentsBytes - snapshotBytes) {
                throw new InvalidDataException("StartPos snapshot attachments are too large to export.");
            }
            attachmentBytes += snapshotBytes;
            attachments[pair.Value.SnapshotEntry] = snapshotPath;
        }
        if (!expectedEntries.SetEquals(attachments.Keys)) {
            throw new InvalidDataException("StartPos snapshot attachments are incomplete.");
        }

        foreach (KeyValuePair<int, AkronStartPosPackEntry> pair in pack.StartPositions ?? new Dictionary<int, AkronStartPosPackEntry>()) {
            string snapshotPath = attachments[pair.Value.SnapshotEntry];
            if (!string.Equals(ComputeFileSha256(snapshotPath), pair.Value.SnapshotSha256, StringComparison.Ordinal)) {
                throw new InvalidDataException("StartPos slot " + pair.Key.ToString(CultureInfo.InvariantCulture) + " changed during export.");
            }
        }

        AkronArchive.WritePayloadArchive(
            path,
            manifest,
            SetupArchivePayload,
            SerializePackPayloadForArchive(pack),
            attachments);
    }

    public static AkronSetupPack Read(string path) {
        return Read(path, out _);
    }

    internal static AkronSetupPack Read(string path, out AkronArchiveManifest manifest) {
        string payload = AkronArchive.ReadPayloadArchive(
            path,
            SetupArchiveKind,
            SetupArchivePayload,
            MaxSetupPayloadBytes,
            MaxStartPositions,
            MaxSnapshotAttachmentsBytes,
            out manifest,
            out string[] attachmentNames);
        ValidatePortablePackJson(payload);
        AkronSetupPack pack = JsonSerializer.Deserialize<AkronSetupPack>(payload, JsonOptions);
        RequireCurrentPackFormat(pack);
        if (pack.State == null) {
            throw new InvalidDataException("Unsupported setup pack.");
        }

        AkronSetupSection section = NormalizeSection(pack.Section);
        ValidatePackTimestamp(pack.CreatedUtc, manifest.CreatedAt);
        ValidatePortablePack(pack, section, manifest.Target?.MapSid);
        HashSet<string> expectedAttachments = GetExpectedSnapshotEntries(pack);
        if (!expectedAttachments.SetEquals(attachmentNames)) {
            throw new InvalidDataException("Setup pack snapshot entries do not match its StartPos data.");
        }
        pack.ArchiveMapSid = manifest.Target.MapSid;
        pack.ArchivePath = path;
        return pack;
    }

    private static HashSet<string> GetExpectedSnapshotEntries(AkronSetupPack pack) {
        HashSet<string> entries = new HashSet<string>(StringComparer.Ordinal);
        if (pack?.Section is not AkronSetupSection.StartPos and not AkronSetupSection.Whole) {
            return entries;
        }

        foreach (KeyValuePair<int, AkronStartPosPackEntry> pair in pack.StartPositions ?? new Dictionary<int, AkronStartPosPackEntry>()) {
            AkronStartPosPackEntry entry = pair.Value ?? throw new InvalidDataException("Setup pack has an invalid StartPos entry.");
            string expectedName = GetSnapshotEntryName(pair.Key);
            if (!string.Equals(entry.SnapshotEntry, expectedName, StringComparison.Ordinal) ||
                entry.SnapshotSha256?.Length != 64 ||
                !entry.SnapshotSha256.All(Uri.IsHexDigit) ||
                !string.Equals(entry.SnapshotSha256, entry.SnapshotSha256.ToLowerInvariant(), StringComparison.Ordinal) ||
                !entries.Add(entry.SnapshotEntry)) {
                throw new InvalidDataException("Setup pack has invalid StartPos snapshot metadata.");
            }
        }
        return entries;
    }

    private static PreparedStartPosImport PrepareStartPosImport(AkronSetupPack pack, string targetMapSid) {
        if (pack == null || string.IsNullOrWhiteSpace(pack.ArchivePath)) {
            return null;
        }

        string snapshotRoot = Path.GetDirectoryName(AkronStartPosReconstruction.GetSnapshotPath(string.Empty));
        string stagingDirectory = Path.Combine(snapshotRoot, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        Dictionary<int, string> stagedSnapshots = new Dictionary<int, string>();
        Level recipientLevel = TryGetCurrentLevel();
        AkronBerryProgressSnapshot recipientBerryProgress =
            string.Equals(recipientLevel?.Session?.Area.GetSID(), targetMapSid, StringComparison.Ordinal)
                ? AkronBerryProgressSnapshot.Capture(recipientLevel)
                : null;
        try {
            foreach (KeyValuePair<int, AkronStartPosPackEntry> pair in pack.StartPositions.OrderBy(pair => pair.Key)) {
                AkronStartPosPackEntry entry = pair.Value;
                byte[] compressedSnapshot = AkronArchive.ReadBinaryEntry(pack.ArchivePath, entry.SnapshotEntry, MaxSnapshotAttachmentBytes);
                string digest = Convert.ToHexString(SHA256.HashData(compressedSnapshot)).ToLowerInvariant();
                if (!string.Equals(digest, entry.SnapshotSha256, StringComparison.Ordinal)) {
                    throw new InvalidDataException("StartPos slot " + pair.Key.ToString(CultureInfo.InvariantCulture) + " snapshot checksum differs.");
                }

                using MemoryStream snapshotStream = new MemoryStream(compressedSnapshot, writable: false);
                if (!AkronStartPosReconstruction.TryReadSnapshot(
                        snapshotStream,
                        out AkronReconstructionDocument document,
                        out string readError,
                        MaxDecompressedSnapshotBytes)) {
                    throw new InvalidDataException("StartPos slot " + pair.Key.ToString(CultureInfo.InvariantCulture) + " snapshot is invalid: " + readError);
                }
                if (!string.Equals(document.MapSid, targetMapSid, StringComparison.Ordinal) ||
                    !string.Equals(document.Room, entry.Room, StringComparison.Ordinal)) {
                    throw new InvalidDataException("StartPos snapshot identity does not match its pack entry.");
                }
                document.BerryProgress = recipientBerryProgress;

                string targetSlotName = AkronActions.GetStartPosStateSlotName(targetMapSid, pair.Key);
                int recipientFileSlot = SaveData.Instance?.FileSlot ?? -1;
                if (!AkronStartPosReconstruction.SaveSnapshot(
                        targetSlotName,
                        targetMapSid,
                        entry.Room,
                        recipientFileSlot,
                        document,
                        out string saveError,
                        stagingDirectory)) {
                    throw new InvalidDataException("Could not stage StartPos slot " + pair.Key.ToString(CultureInfo.InvariantCulture) + ": " + saveError);
                }
                stagedSnapshots[pair.Key] = AkronStartPosReconstruction.GetSnapshotPath(targetSlotName, stagingDirectory);
            }
            return new PreparedStartPosImport(stagingDirectory, targetMapSid, stagedSnapshots);
        } catch {
            Directory.Delete(stagingDirectory, recursive: true);
            throw;
        }
    }

    // The one gate on a pack's contract version, so it is also the one place that can
    // say why a pack is refused. Every caller that used to compare pack.Format itself
    // goes through here, so a pack cannot be applied down a path that skipped the check.
    private static void RequireCurrentPackFormat(AkronSetupPack pack) {
        if (pack == null) {
            throw new InvalidDataException("Unsupported setup pack.");
        }
        if (string.Equals(pack.Format, SetupPackFormat, StringComparison.Ordinal)) {
            return;
        }

        throw new AkronSetupPackFormatException(
            "This setup pack is " + DescribePackFormat(pack) + " and Akron now reads " + SetupPackFormat +
            ". Packs from an older Akron built rooms differently. Recreate the setup and its StartPos slots in this build, then export a new pack.");
    }

    // The format string comes out of a pack payload, which is allowed to be 2 MiB, so it
    // cannot go into a message that reaches a toast unbounded. Real format names are
    // under twenty characters.
    private static string DescribePackFormat(AkronSetupPack pack) {
        string format = pack?.Format;
        if (string.IsNullOrWhiteSpace(format)) {
            return "unnamed";
        }
        return format.Length <= MaxReportedPackFormatChars
            ? format
            : format.Substring(0, MaxReportedPackFormatChars) + "...";
    }

    private const int MaxReportedPackFormatChars = 32;

    // Tracks AkronReconstructionDocument.CurrentFormat, so the entry name states which
    // fresh-room baseline the attachment was measured against.
    private static string GetSnapshotEntryName(int slot) {
        return "startpos/" + slot.ToString(CultureInfo.InvariantCulture) + ".v10.json.gz";
    }

    private static string ComputeFileSha256(string path) {
        using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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

    private static string ResolvePackTargetMapSid(AkronSetupPack pack) {
        if (!string.IsNullOrWhiteSpace(pack.ArchiveMapSid)) {
            return pack.ArchiveMapSid.Trim();
        }

        string[] areaSids = (pack.StartPositions ?? new Dictionary<int, AkronStartPosPackEntry>())
            .Values
            .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.AreaSid))
            .Select(entry => entry.AreaSid.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (areaSids.Length > 1) {
            throw new InvalidDataException("Setup pack contains StartPos entries for multiple maps.");
        }
        return areaSids.SingleOrDefault() ?? string.Empty;
    }

    private static string TryGetCurrentLevelMapSid() {
        Level level = TryGetCurrentLevel();
        return level?.Session?.Area.GetSID() ?? string.Empty;
    }

    private static Level TryGetCurrentLevel() {
        try {
            return Engine.Scene as Level;
        } catch (Exception exception) when (exception is InvalidProgramException || exception is NullReferenceException) {
            // The test reference assemblies strip the Engine getter body.
            return null;
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
        if (!Enum.IsDefined(typeof(AkronSetupSection), section)) {
            throw new InvalidDataException("Setup pack section is unsupported.");
        }

        return section;
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
        target.StartPosWaitForInput = source.StartPosWaitForInput;
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
        target.AutoDeafenArea = source.AutoDeafenArea;
        target.AutoDeafenShowArea = source.AutoDeafenShowArea;
        target.AutoDeafenAreas = CopyRectangles(source.AutoDeafenAreas);
        target.AutoDeafenAreaX = source.AutoDeafenAreaX;
        target.AutoDeafenAreaY = source.AutoDeafenAreaY;
        target.AutoDeafenAreaWidth = source.AutoDeafenAreaWidth;
        target.AutoDeafenAreaHeight = source.AutoDeafenAreaHeight;
    }

    private static void CopyRecorderState(AkronSetupState target, AkronSetupState source) {
        target.RecordingContainerFormat = source.RecordingContainerFormat;
        target.RecordingReplayBufferSeconds = source.RecordingReplayBufferSeconds;
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
    }

    private static void CopyHudState(AkronSetupState target, AkronSetupState source) {
        CopyStateProperties(target, source, HudStateProperties);
    }

    private static void CopyStateProperties(AkronSetupState target, AkronSetupState source, IEnumerable<PropertyInfo> properties) {
        foreach (PropertyInfo property in properties) {
            property.SetValue(target, property.GetValue(source));
        }
    }

    private static void ApplyPortableWholeState(AkronModuleSettings settings, AkronSetupState source) {
        AkronSetupState merged = settings.CaptureSetupPackState();
        IEnumerable<PropertyInfo> portableProperties = typeof(AkronSetupState)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.CanWrite && !UnsafeWholeStatePropertyNames.Contains(property.Name));
        CopyStateProperties(merged, source, portableProperties);
        settings.ApplySetupPackState(merged);
    }

    private static string SerializePortablePack(AkronSetupPack pack) {
        JsonObject root = JsonNode.Parse(JsonSerializer.Serialize(pack, JsonOptions))?.AsObject()
            ?? throw new InvalidDataException("Setup pack is invalid.");
        JsonObject state = root["state"]?.AsObject()
            ?? throw new InvalidDataException("Setup pack state is missing.");
        HashSet<string> allowedStateProperties = GetStateJsonPropertyNames(pack.Section);
        foreach (string propertyName in state.Select(property => property.Key).ToArray()) {
            if (!allowedStateProperties.Contains(propertyName)) {
                state.Remove(propertyName);
            }
        }

        if (pack.Section is not AkronSetupSection.Keybinds and not AkronSetupSection.Whole) {
            root.Remove("buttonBindings");
            root.Remove("menuActionBindings");
        }

        if (pack.Section is not AkronSetupSection.StartPos and not AkronSetupSection.Whole) {
            root.Remove("startPositions");
        }

        return root.ToJsonString(JsonOptions);
    }

    private static void ValidatePortablePackJson(string payload) {
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("section", out JsonElement sectionElement) ||
            sectionElement.ValueKind != JsonValueKind.String ||
            !Enum.TryParse(sectionElement.GetString(), ignoreCase: false, out AkronSetupSection section) ||
            !Enum.IsDefined(typeof(AkronSetupSection), section)) {
            throw new InvalidDataException("Setup pack section is unsupported.");
        }

        HashSet<string> expectedTopLevel = new HashSet<string>(StringComparer.Ordinal) {
            "format", "name", "createdUtc", "section", "state"
        };
        if (section is AkronSetupSection.Keybinds or AkronSetupSection.Whole) {
            expectedTopLevel.Add("buttonBindings");
            expectedTopLevel.Add("menuActionBindings");
        }
        if (section is AkronSetupSection.StartPos or AkronSetupSection.Whole) {
            expectedTopLevel.Add("startPositions");
        }

        RequireExactJsonProperties(root, expectedTopLevel, "Setup pack");
        if (!root.TryGetProperty("state", out JsonElement state) || state.ValueKind != JsonValueKind.Object) {
            throw new InvalidDataException("Setup pack state is invalid.");
        }

        RequireExactJsonProperties(state, GetStateJsonPropertyNames(section), "Setup pack state");
        ValidateEnumJsonValues(state, GetStateProperties(section));
        foreach (PropertyInfo property in GetStateProperties(section)) {
            string jsonName = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            ValidateJsonContractValue(state.GetProperty(jsonName), property.PropertyType, "Setup pack state " + jsonName);
        }
        if (section is AkronSetupSection.Keybinds or AkronSetupSection.Whole) {
            ValidateJsonContractValue(root.GetProperty("buttonBindings"), typeof(Dictionary<string, AkronButtonBindingPack>), "Setup pack bindings");
            ValidateJsonContractValue(root.GetProperty("menuActionBindings"), typeof(Dictionary<string, string>), "Setup pack menu bindings");
        }
        if (section is AkronSetupSection.StartPos or AkronSetupSection.Whole) {
            ValidateJsonContractValue(root.GetProperty("startPositions"), typeof(Dictionary<int, AkronStartPosPackEntry>), "Setup pack StartPos entries");
        }
    }

    private static void ValidateJsonContractValue(JsonElement element, Type declaredType, string label) {
        Type type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        if (type.IsEnum) {
            if (element.ValueKind != JsonValueKind.String) {
                throw new InvalidDataException(label + " must use a named enum value.");
            }
            string value = element.GetString();
            if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse(type, value, ignoreCase: false, out object parsed) ||
                !Enum.IsDefined(type, parsed) || !string.Equals(parsed.ToString(), value, StringComparison.Ordinal)) {
                throw new InvalidDataException(label + " has an invalid enum value.");
            }
            return;
        }

        if (type == typeof(string) || type.IsPrimitive || type == typeof(decimal)) {
            return;
        }

        Type dictionaryInterface = type
            .GetInterfaces()
            .Concat(new[] { type })
            .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (dictionaryInterface != null) {
            if (element.ValueKind != JsonValueKind.Object) {
                throw new InvalidDataException(label + " must be an object.");
            }
            Type[] dictionaryTypes = dictionaryInterface.GetGenericArguments();
            Type keyType = dictionaryTypes[0];
            Type valueType = dictionaryTypes[1];
            JsonProperty[] entries = element.EnumerateObject().ToArray();
            if (entries.Length != entries.Select(property => property.Name).ToHashSet(StringComparer.Ordinal).Count) {
                throw new InvalidDataException(label + " contains a duplicate key.");
            }
            foreach (JsonProperty property in entries) {
                if (keyType == typeof(int) &&
                    (!int.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedKey) ||
                     !string.Equals(property.Name, parsedKey.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))) {
                    throw new InvalidDataException(label + " contains a non-canonical integer key.");
                }
                ValidateJsonContractValue(property.Value, valueType, label + " entry");
            }
            return;
        }

        Type collectionInterface = type.IsArray
            ? null
            : type.GetInterfaces().Concat(new[] { type })
                .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (type.IsArray || collectionInterface != null) {
            if (element.ValueKind != JsonValueKind.Array) {
                throw new InvalidDataException(label + " must be an array.");
            }
            Type itemType = type.IsArray ? type.GetElementType() : collectionInterface.GetGenericArguments()[0];
            foreach (JsonElement item in element.EnumerateArray()) {
                ValidateJsonContractValue(item, itemType, label + " item");
            }
            return;
        }

        if (element.ValueKind != JsonValueKind.Object) {
            throw new InvalidDataException(label + " must be an object.");
        }
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.CanWrite && property.GetCustomAttribute<JsonIgnoreAttribute>() == null)
            .ToArray();
        HashSet<string> expected = properties
            .Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name))
            .ToHashSet(StringComparer.Ordinal);
        RequireExactJsonProperties(element, expected, label);
        foreach (PropertyInfo property in properties) {
            string jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            ValidateJsonContractValue(element.GetProperty(jsonName), property.PropertyType, label + " " + jsonName);
        }
    }

    private static void ValidatePackTimestamp(string createdUtc, string manifestCreatedAt) {
        if (!AkronArchive.IsValidUtcTimestamp(createdUtc) || !string.Equals(createdUtc, manifestCreatedAt, StringComparison.Ordinal)) {
            throw new InvalidDataException("Setup pack creation timestamp is invalid or does not match the archive manifest.");
        }
    }

    private static void RequireExactJsonProperties(JsonElement element, HashSet<string> expected, string label) {
        JsonProperty[] properties = element.EnumerateObject().ToArray();
        HashSet<string> actual = properties.Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        if (properties.Length != expected.Count || !actual.SetEquals(expected)) {
            throw new InvalidDataException(label + " fields do not match the " + SetupPackFormat + " contract.");
        }
    }

    private static void ValidateEnumJsonValues(JsonElement state, IEnumerable<PropertyInfo> properties) {
        foreach (PropertyInfo property in properties) {
            if (!property.PropertyType.IsEnum) {
                continue;
            }

            string jsonName = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!state.TryGetProperty(jsonName, out JsonElement value) || value.ValueKind != JsonValueKind.String) {
                throw new InvalidDataException("Setup pack enum fields must use named string values.");
            }
        }
    }

    private static PropertyInfo[] GetStateProperties(AkronSetupSection section) {
        return section switch {
            AkronSetupSection.StartPos => StartPosStateProperties,
            AkronSetupSection.Keybinds => Array.Empty<PropertyInfo>(),
            AkronSetupSection.AutoKill => AutoKillStateProperties,
            AkronSetupSection.AutoDeafen => AutoDeafenStateProperties,
            AkronSetupSection.Recorder => RecorderStateProperties,
            AkronSetupSection.Audio => AudioStateProperties,
            AkronSetupSection.Hud => HudStateProperties,
            AkronSetupSection.Whole => typeof(AkronSetupState)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && property.CanWrite && !UnsafeWholeStatePropertyNames.Contains(property.Name))
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToArray(),
            _ => throw new InvalidDataException("Setup pack section is unsupported.")
        };
    }

    private static HashSet<string> GetStateJsonPropertyNames(AkronSetupSection section) {
        return GetStateProperties(section)
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static bool IsCommunitySection(AkronSetupSection section) {
        return section is AkronSetupSection.StartPos or AkronSetupSection.AutoKill or AkronSetupSection.AutoDeafen;
    }

    private static void ValidatePortablePack(AkronSetupPack pack, AkronSetupSection section, string expectedMapSid) {
        RequireCurrentPackFormat(pack);
        if (pack.State == null) {
            throw new InvalidDataException("Unsupported setup pack.");
        }

        if (pack.Section != section && pack.Section != AkronSetupSection.Whole) {
            throw new InvalidDataException("Setup pack section does not match the requested section.");
        }

        foreach (PropertyInfo property in GetStateProperties(section).Where(property => property.PropertyType.IsEnum)) {
            object value = property.GetValue(pack.State);
            if (value == null || !Enum.IsDefined(property.PropertyType, value)) {
                throw new InvalidDataException("Setup pack contains an unsupported enum value.");
            }
        }

        ValidateLength(pack.Name, 256, "Setup pack name");
        switch (section) {
            case AkronSetupSection.StartPos:
                if (string.IsNullOrWhiteSpace(expectedMapSid)) {
                    throw new InvalidDataException("StartPos setup pack must target a map.");
                }
                ValidateStartPositions(pack.StartPositions, expectedMapSid);
                break;
            case AkronSetupSection.Keybinds:
                ValidateBindings(pack);
                break;
            case AkronSetupSection.AutoKill:
                if ((pack.State.AutoKillAreas?.Count ?? 0) > MaxAutoKillAreas) {
                    throw new InvalidDataException("Setup pack has too many Auto Kill areas.");
                }
                ValidateAutoKillAreas(pack.State.AutoKillAreas);
                break;
            case AkronSetupSection.AutoDeafen:
                if ((pack.State.AutoDeafenAreas?.Count ?? 0) > MaxAutoDeafenAreas) {
                    throw new InvalidDataException("Setup pack has too many Auto Deafen areas.");
                }
                ValidateRectangles(pack.State.AutoDeafenAreas, "Auto Deafen");
                break;
            case AkronSetupSection.Recorder:
                ValidateRecorderState(pack.State);
                break;
            case AkronSetupSection.Audio:
                ValidateAudioState(pack.State);
                break;
            case AkronSetupSection.Hud:
                ValidateHudState(pack.State);
                break;
            case AkronSetupSection.Whole:
                ValidateBindings(pack);
                if (string.IsNullOrWhiteSpace(expectedMapSid) && (pack.StartPositions?.Count ?? 0) > 0) {
                    throw new InvalidDataException("Whole setup pack StartPos data must target a map.");
                }
                ValidateStartPositions(pack.StartPositions, expectedMapSid);
                ValidateRecorderState(pack.State);
                ValidateAudioState(pack.State);
                ValidateHudState(pack.State);
                if ((pack.State.AutoKillAreas?.Count ?? 0) > MaxAutoKillAreas ||
                    (pack.State.AutoDeafenAreas?.Count ?? 0) > MaxAutoDeafenAreas) {
                    throw new InvalidDataException("Setup pack has too many area definitions.");
                }
                ValidateAutoKillAreas(pack.State.AutoKillAreas);
                ValidateRectangles(pack.State.AutoDeafenAreas, "Auto Deafen");
                break;
        }
    }

    private static void ValidateStartPositions(Dictionary<int, AkronStartPosPackEntry> entries, string expectedMapSid) {
        entries ??= new Dictionary<int, AkronStartPosPackEntry>();
        if (entries.Count > MaxStartPositions) {
            throw new InvalidDataException("Setup pack has too many StartPos entries.");
        }

        foreach (KeyValuePair<int, AkronStartPosPackEntry> pair in entries) {
            AkronStartPosPackEntry entry = pair.Value ?? throw new InvalidDataException("Setup pack has an invalid StartPos entry.");
            if (pair.Key < 1 || pair.Key > MaxStartPositions || !float.IsFinite(entry.X) || !float.IsFinite(entry.Y) ||
                Math.Abs(entry.X) > MaxPortableStartPosCoordinate || Math.Abs(entry.Y) > MaxPortableStartPosCoordinate ||
                entry.Dashes < -1 || entry.Dashes > 5 || entry.StaminaPercent < -1 || entry.StaminaPercent > 100 ||
                !Enum.IsDefined(typeof(AkronStartPosFacing), entry.Facing)) {
                throw new InvalidDataException("Setup pack has an invalid StartPos entry.");
            }
            ValidateLength(entry.Room, 256, "StartPos room");
            ValidateLength(entry.AreaSid, 256, "StartPos map SID");
            if (!string.IsNullOrWhiteSpace(expectedMapSid) && !string.Equals(entry.AreaSid, expectedMapSid, StringComparison.Ordinal)) {
                throw new InvalidDataException("Setup pack StartPos belongs to a different map.");
            }
        }
    }

    private static void ValidateBindings(AkronSetupPack pack) {
        Dictionary<string, AkronButtonBindingPack> bindings = pack.ButtonBindings ?? new Dictionary<string, AkronButtonBindingPack>();
        if (bindings.Count > MaxButtonBindings || (pack.MenuActionBindings?.Count ?? 0) > MaxMenuActionBindings) {
            throw new InvalidDataException("Setup pack has too many bindings.");
        }

        foreach (KeyValuePair<string, AkronButtonBindingPack> pair in bindings) {
            ValidateLength(pair.Key, 128, "Binding name");
            if (!ButtonBindingProperties.Any(property => string.Equals(property.Name, pair.Key, StringComparison.Ordinal))) {
                throw new InvalidDataException("Setup pack contains an unknown binding name.");
            }
            AkronButtonBindingPack binding = pair.Value ?? throw new InvalidDataException("Setup pack has an invalid binding.");
            List<string> inputs = (binding.Keys ?? new List<string>())
                .Concat(binding.Buttons ?? new List<string>())
                .Concat(binding.MouseButtons ?? new List<string>())
                .ToList();
            if (inputs.Count > MaxBindingInputs) {
                throw new InvalidDataException("Setup pack binding has too many inputs.");
            }
            ValidateBindingEnumNames<Keys>(binding.Keys, "keyboard");
            ValidateBindingEnumNames<Buttons>(binding.Buttons, "controller");
            ValidateBindingEnumNames<MInput.MouseData.MouseButtons>(binding.MouseButtons, "mouse");
        }

        foreach (KeyValuePair<string, string> pair in pack.MenuActionBindings ?? new Dictionary<string, string>()) {
            ValidateLength(pair.Key, 128, "Menu action name");
            ValidateLength(pair.Value, 256, "Menu action binding");
        }
    }

    private static void ValidateBindingEnumNames<TEnum>(IEnumerable<string> values, string label) where TEnum : struct, Enum {
        foreach (string input in values ?? Enumerable.Empty<string>()) {
            ValidateLength(input, 64, "Binding input");
            if (string.IsNullOrWhiteSpace(input) ||
                !Enum.TryParse(input, ignoreCase: false, out TEnum parsed) ||
                !Enum.IsDefined(typeof(TEnum), parsed) ||
                !string.Equals(parsed.ToString(), input, StringComparison.Ordinal)) {
                throw new InvalidDataException("Setup pack contains an invalid " + label + " binding.");
            }
        }
    }

    private static void ValidateRecorderState(AkronSetupState state) {
        if (state.RecordingResolutionX < 1 || state.RecordingResolutionX > MaxPortableRecordingWidth ||
            state.RecordingResolutionY < 1 || state.RecordingResolutionY > MaxPortableRecordingHeight ||
            (long)state.RecordingResolutionX * state.RecordingResolutionY > MaxPortableRecordingPixels ||
            state.RecordingFramerate < 1 || state.RecordingFramerate > MaxPortableRecordingFramerate ||
            state.RecordingBitrateMbps < 1 || state.RecordingBitrateMbps > MaxPortableRecordingBitrateMbps ||
            state.RecordingReplayBufferSeconds < 0 || state.RecordingReplayBufferSeconds > MaxPortableReplayBufferSeconds ||
            state.RecordingPreRollSeconds < 0 || state.RecordingPreRollSeconds > MaxPortableClipSeconds ||
            state.RecordingPostRollSeconds < 0 || state.RecordingPostRollSeconds > MaxPortableClipSeconds ||
            state.RecordingKeyframeIntervalSeconds < 0 || state.RecordingKeyframeIntervalSeconds > MaxPortableKeyframeSeconds ||
            !float.IsFinite(state.RecordingEndscreenDurationSeconds) ||
            state.RecordingEndscreenDurationSeconds < 0f || state.RecordingEndscreenDurationSeconds > MaxPortableEndscreenSeconds ||
            !IsRecordingAudioLevelValid(state.RecordingAudioFullMixLevel) ||
            !IsRecordingAudioLevelValid(state.RecordingAudioMusicLevel) ||
            !IsRecordingAudioLevelValid(state.RecordingAudioSfxLevel) ||
            !IsRecordingAudioLevelValid(state.RecordingAudioAmbienceLevel)) {
            throw new InvalidDataException("Setup pack recorder values exceed portable safety limits.");
        }
    }

    private static bool IsRecordingAudioLevelValid(int level) {
        return level >= 0 && level <= 200;
    }

    private static void ValidateAudioState(AkronSetupState state) {
        if ((state.SoundVolumes?.Count ?? 0) > MaxAudioDictionaryEntries ||
            (state.SoundVolumeOverrides?.Count ?? 0) > MaxAudioDictionaryEntries ||
            !float.IsFinite(state.AudioSpeedMultiplier) || state.AudioSpeedMultiplier < 0.1f || state.AudioSpeedMultiplier > 4f ||
            !float.IsFinite(state.PitchShiftMultiplier) || state.PitchShiftMultiplier < 0.1f || state.PitchShiftMultiplier > 4f ||
            (state.SoundVolumes ?? new Dictionary<string, int>()).Values.Any(volume => volume < 0 || volume > 200)) {
            throw new InvalidDataException("Setup pack audio values exceed portable safety limits.");
        }

        foreach (string key in (state.SoundVolumes ?? new Dictionary<string, int>()).Keys.Concat(
                     (state.SoundVolumeOverrides ?? new Dictionary<string, bool>()).Keys)) {
            ValidateLength(key, 128, "Audio channel name");
        }
    }

    private static void ValidateHudState(AkronSetupState state) {
        if ((state.CustomHudLabelDefinitions?.Count ?? 0) > MaxCustomHudLabels ||
            (state.InputBoardElements?.Count ?? 0) > MaxInputBoardElements ||
            (state.LabelRowOrder?.Count ?? 0) > MaxLabelRowOrder) {
            throw new InvalidDataException("Setup pack HUD collections exceed portable safety limits.");
        }

        foreach (AkronCustomHudLabel label in state.CustomHudLabelDefinitions ?? new List<AkronCustomHudLabel>()) {
            if (label == null) {
                throw new InvalidDataException("Setup pack has an invalid custom HUD label.");
            }
            ValidateLength(label.Id, 128, "Custom HUD label id");
            ValidateMaximumLength(label.Name, 128, "Custom HUD label name");
            ValidateMaximumLength(label.Text, 4096, "Custom HUD label text");
        }
        foreach (AkronInputBoardElement element in state.InputBoardElements ?? new List<AkronInputBoardElement>()) {
            if (element == null || (element.Bindings?.Count ?? 0) > MaxBindingInputs || (element.KeyBindings?.Count ?? 0) > MaxBindingInputs ||
                (element.Bindings?.Any(binding => !Enum.IsDefined(typeof(AkronInputBoardBinding), binding)) ?? false) ||
                (element.KeyBindings?.Any(key => !Enum.IsDefined(typeof(Keys), key)) ?? false)) {
                throw new InvalidDataException("Setup pack has an invalid input board element.");
            }
            ValidateLength(element.Id, 128, "Input board element id");
            ValidateMaximumLength(element.Label, 128, "Input board element label");
        }
        foreach (string row in state.LabelRowOrder ?? new List<string>()) {
            ValidateLength(row, 128, "HUD row key");
        }
    }

    private static void ValidateAutoKillAreas(IEnumerable<AkronAutoKillAreaData> areas) {
        foreach (AkronAutoKillAreaData area in areas ?? Enumerable.Empty<AkronAutoKillAreaData>()) {
            if (area == null || area.Width < 0 || area.Height < 0) {
                throw new InvalidDataException("Setup pack has an invalid Auto Kill area.");
            }

            ValidateLength(area.MapSid, 256, "Auto Kill area map SID");
        }
    }

    private static void ValidateRectangles(IEnumerable<AkronRectangleData> areas, string label) {
        foreach (AkronRectangleData area in areas ?? Enumerable.Empty<AkronRectangleData>()) {
            if (area == null || area.Width < 0 || area.Height < 0) {
                throw new InvalidDataException("Setup pack has an invalid " + label + " area.");
            }

            ValidateLength(area.MapSid, 256, label + " area map SID");
        }
    }

    private static void ValidateLength(string value, int maximum, string label) {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) {
            throw new InvalidDataException(label + " is invalid.");
        }
    }

    private static void ValidateMaximumLength(string value, int maximum, string label) {
        if ((value?.Length ?? 0) > maximum) {
            throw new InvalidDataException(label + " is too long.");
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
                Height = rectangle.Height,
                MapSid = rectangle.MapSid ?? string.Empty
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

        // A pack exported with Open Menu unbound would otherwise leave the recipient with no
        // keyboard way into the overlay. Load re-seeds the same default; Import tells the player.
        if (bindings.TryGetValue(nameof(AkronModuleSettings.ToggleOverlay), out AkronButtonBindingPack toggle) &&
            PackOverlayToggleNeedsDefault(toggle)) {
            settings.ToggleOverlay = AkronModuleSettings.CreateDefaultOverlayToggleBinding();
        }
    }

    // Decided from the pack's own key and button names, not from a built ButtonBinding: the
    // test reference assemblies cannot run ButtonBinding's getters (see IsUninitializedButtonBinding).
    internal static bool PackOverlayToggleNeedsDefault(AkronButtonBindingPack toggle) {
        return AkronModuleSettings.ShouldUseDefaultOverlayToggleBinding(
            ParseEnumList<Keys>(toggle?.Keys).Where(key => key != Keys.None).ToList(),
            ParseEnumList<Buttons>(toggle?.Buttons).Where(button => button != 0).ToList());
    }

    // Whether applying the pack's bindings replaced its Open Menu binding with the default.
    private static bool PackOverlayToggleWasReplaced(AkronSetupPack pack, AkronSetupSection section) {
        if (section is not AkronSetupSection.Keybinds and not AkronSetupSection.Whole ||
            pack.ButtonBindings == null ||
            !pack.ButtonBindings.TryGetValue(nameof(AkronModuleSettings.ToggleOverlay), out AkronButtonBindingPack toggle)) {
            return false;
        }

        return PackOverlayToggleNeedsDefault(toggle);
    }

    private static ButtonBinding BuildButtonBinding(AkronButtonBindingPack pack) {
        ButtonBinding binding = new ButtonBinding(0, Keys.None) {
            Keys = ParseEnumList<Keys>(pack?.Keys).Where(key => key != Keys.None).ToList(),
            Buttons = ParseEnumList<Buttons>(pack?.Buttons).Where(button => button != 0).ToList(),
            MouseButtons = ParseEnumList<MInput.MouseData.MouseButtons>(pack?.MouseButtons).ToList()
        };
        return binding;
    }

    private static List<T> ParseEnumList<T>(IEnumerable<string> values) where T : struct, Enum {
        List<T> parsed = new List<T>();
        foreach (string value in values ?? Array.Empty<string>()) {
            if (!Enum.TryParse(value, ignoreCase: false, out T parsedValue) ||
                !Enum.IsDefined(typeof(T), parsedValue) ||
                !string.Equals(parsedValue.ToString(), value, StringComparison.Ordinal)) {
                throw new InvalidDataException("Setup pack contains an invalid binding value.");
            }
            parsed.Add(parsedValue);
        }

        return parsed;
    }

    internal static Dictionary<int, AkronStartPosPackEntry> CaptureStartPositions(
        AkronModuleSession session,
        string mapSid,
        out Dictionary<int, string> snapshotSourcePaths
    ) {
        Dictionary<int, AkronStartPosPackEntry> entries = new Dictionary<int, AkronStartPosPackEntry>();
        snapshotSourcePaths = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(mapSid)) {
            return entries;
        }
        if (AkronActions.HasPendingStartPosForArea(mapSid)) {
            throw new InvalidDataException(
                "A StartPos restart copy is still saving. Wait for it to finish before exporting or uploading this pack.");
        }
        foreach (KeyValuePair<int, AkronStartPos> pair in session?.StartPositions ?? new Dictionary<int, AkronStartPos>()) {
            if (pair.Value == null ||
                (!string.IsNullOrWhiteSpace(mapSid) && !string.Equals(pair.Value.AreaSid, mapSid, StringComparison.Ordinal))) {
                continue;
            }

            string snapshotPath = AkronStartPosReconstruction.GetSnapshotPath(pair.Value.StateSlotName);
            string snapshotEntry = GetSnapshotEntryName(pair.Key);
            bool hasSnapshot = !string.IsNullOrWhiteSpace(pair.Value.StateSlotName) && File.Exists(snapshotPath);
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
                Grab = pair.Value.Grab,
                SnapshotEntry = hasSnapshot ? snapshotEntry : string.Empty,
                SnapshotSha256 = hasSnapshot ? ComputeFileSha256(snapshotPath) : string.Empty
            };
            if (hasSnapshot) {
                snapshotSourcePaths[pair.Key] = snapshotPath;
            }
        }

        return entries;
    }

    private static Dictionary<int, AkronStartPos> BuildStartPositions(
        Dictionary<int, AkronStartPosPackEntry> entries,
        string targetMapSid
    ) {
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
                StateSlotName = AkronActions.GetStartPosStateSlotName(targetMapSid, pair.Key)
            };
        }

        return startPositions;
    }

    private static Dictionary<int, AkronStartPos> MergeScopedStartPositions(
        Dictionary<int, AkronStartPos> existing,
        Dictionary<int, AkronStartPos> imported,
        string targetMapSid,
        out Dictionary<int, int> importedSlotMap) {
        importedSlotMap = new Dictionary<int, int>();
        HashSet<string> importedAreaSids = new HashSet<string>(
            (imported ?? new Dictionary<int, AkronStartPos>()).Values.Select(startPos => startPos?.AreaSid ?? string.Empty),
            StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(targetMapSid)) {
            importedAreaSids.Add(targetMapSid);
        }

        Dictionary<int, AkronStartPos> merged = new Dictionary<int, AkronStartPos>();
        foreach (KeyValuePair<int, AkronStartPos> pair in existing ?? new Dictionary<int, AkronStartPos>()) {
            if (pair.Value != null && importedAreaSids.Contains(pair.Value.AreaSid ?? string.Empty)) {
                continue;
            }

            merged[pair.Key] = pair.Value;
        }

        foreach (KeyValuePair<int, AkronStartPos> pair in imported ?? new Dictionary<int, AkronStartPos>()) {
            // StartPos slot numbers belong to their map. An unrelated map can
            // use the same number in this transient session dictionary, but it
            // must not renumber the imported map or its snapshot file.
            int slot = pair.Key;
            if (pair.Value != null) {
                pair.Value.StateSlotName = AkronActions.GetStartPosStateSlotName(targetMapSid, slot);
            }
            merged[slot] = pair.Value;
            importedSlotMap[pair.Key] = slot;
        }

        return merged;
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

    private sealed class PreparedStartPosImport : IDisposable {
        private readonly string stagingDirectory;
        private readonly string targetMapSid;
        private readonly Dictionary<int, string> stagedSnapshots;
        private readonly List<string> installedPaths = new List<string>();
        private readonly Dictionary<string, string> replacedPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool installed;
        private bool committed;

        public PreparedStartPosImport(string stagingDirectory, string targetMapSid, Dictionary<int, string> stagedSnapshots) {
            this.stagingDirectory = stagingDirectory;
            this.targetMapSid = targetMapSid;
            this.stagedSnapshots = stagedSnapshots;
        }

        public void Install(
            IReadOnlyDictionary<int, int> importedSlotMap = null,
            IEnumerable<int> previousSlots = null
        ) {
            if (installed) {
                throw new InvalidOperationException("Prepared StartPos snapshots are already installed.");
            }
            try {
                // Back up removed slots too. Metadata rollback can only be
                // correct when every snapshot from the previous map state is
                // still available, not just paths replaced by imported files.
                foreach (int previousSlot in previousSlots ?? Enumerable.Empty<int>()) {
                    BackUpDestination(AkronStartPosReconstruction.GetSnapshotPath(
                        AkronActions.GetStartPosStateSlotName(targetMapSid, previousSlot)));
                }
                foreach (KeyValuePair<int, string> snapshot in stagedSnapshots) {
                    int targetSlot = importedSlotMap != null && importedSlotMap.TryGetValue(snapshot.Key, out int remappedSlot)
                        ? remappedSlot
                        : snapshot.Key;
                    string destinationPath = AkronStartPosReconstruction.GetSnapshotPath(
                        AkronActions.GetStartPosStateSlotName(targetMapSid, targetSlot));
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    BackUpDestination(destinationPath);
                    if (targetSlot == snapshot.Key) {
                        File.Move(snapshot.Value, destinationPath, overwrite: true);
                    } else {
                        using FileStream source = new FileStream(snapshot.Value, FileMode.Open, FileAccess.Read, FileShare.Read);
                        if (!AkronStartPosReconstruction.TryReadSnapshot(source, out AkronReconstructionDocument document, out string readError)) {
                            throw new InvalidDataException("Could not remap imported StartPos snapshot: " + readError);
                        }
                        if (!AkronStartPosReconstruction.SaveSnapshot(
                                AkronActions.GetStartPosStateSlotName(targetMapSid, targetSlot),
                                targetMapSid,
                                document.Room,
                                document.FileSlot,
                                document,
                                out string saveError)) {
                            throw new InvalidDataException("Could not remap imported StartPos snapshot: " + saveError);
                        }
                    }
                    installedPaths.Add(destinationPath);
                }
                installed = true;
            } catch {
                RollBack();
                throw;
            } finally {
                // This transaction moves files into and out of the canonical snapshot
                // directory without going through PreparedSnapshotInstall, so the
                // cached File.Exists answers behind HasSnapshot must be dropped whether
                // the install succeeded or rolled back.
                InvalidateTouchedSnapshots();
            }
        }

        // Every other writer of the snapshot directory funnels through
        // InvalidateSnapshotExistence, which is what drops a prewarmed document for that
        // path and marks the write for any read still in flight. This transaction is the
        // one that did not, and leaned on the consume-time file stamp instead - which
        // cannot tell a replacement apart if the new file happens to share a length and
        // a modification time. Naming the paths costs nothing and closes that.
        private void InvalidateTouchedSnapshots() {
            foreach (string installedPath in installedPaths) {
                AkronStartPosReconstruction.InvalidateSnapshotExistence(installedPath);
            }
            foreach (string replacedPath in replacedPaths.Keys) {
                AkronStartPosReconstruction.InvalidateSnapshotExistence(replacedPath);
            }
            // Still a broad flush as well: a rollback has already emptied both
            // collections by the time this runs, and a path this transaction never
            // recorded is exactly the case a per-path list cannot cover.
            AkronStartPosReconstruction.ResetSnapshotExistenceCache();
        }

        private void BackUpDestination(string destinationPath) {
            if (!File.Exists(destinationPath) || replacedPaths.ContainsKey(destinationPath)) {
                return;
            }
            string backupPath = Path.Combine(stagingDirectory, "replaced-" + Guid.NewGuid().ToString("N"));
            File.Move(destinationPath, backupPath);
            replacedPaths[destinationPath] = backupPath;
        }

        public void Commit() {
            if (!installed) {
                throw new InvalidOperationException("Prepared StartPos snapshots have not been installed.");
            }
            committed = true;
        }

        private void RollBack() {
            foreach (string installedPath in installedPaths) {
                if (File.Exists(installedPath)) {
                    File.Delete(installedPath);
                }
            }
            foreach (KeyValuePair<string, string> replaced in replacedPaths) {
                if (File.Exists(replaced.Value)) {
                    File.Move(replaced.Value, replaced.Key, overwrite: true);
                }
            }
            // Before the lists are cleared, so every path this rollback touched is
            // named rather than left to the consume-time file stamp.
            InvalidateTouchedSnapshots();
            installedPaths.Clear();
            replacedPaths.Clear();
            installed = false;
        }

        public void Dispose() {
            if (installed && !committed) {
                RollBack();
            }
            try {
                if (Directory.Exists(stagingDirectory)) {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
            } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) {
                // The import is already committed or rolled back. A temporary
                // directory that could not be removed must not change the
                // reported result of that transaction.
                Logger.Log(LogLevel.Warn, nameof(AkronSetupPacks),
                    "Could not delete staged StartPos import: " + exception.Message);
            }
        }
    }

    private sealed class SetupImportStateTransaction : IDisposable {
        private readonly AkronModuleSettings settings;
        private readonly AkronModuleSession session;
        private readonly AkronModuleSaveData saveData;
        private readonly string targetMapSid;
        private readonly AkronSetupState previousSettings;
        private readonly Dictionary<string, AkronButtonBindingPack> previousButtonBindings;
        private readonly Dictionary<string, string> previousMenuActionBindings;
        private readonly Dictionary<int, AkronStartPos> previousStartPositions;
        private readonly string previousLoadedAreaSid;
        private readonly int previousLastLoadedSlot;
        private readonly Dictionary<string, AkronPersistedStartPosMap> previousMaps;
        private readonly AkronPersistedStartPosMap previousMap;
        private readonly bool hadPreviousMap;
        private AkronActions.StartPosReplacementTransaction startPosReplacement;
        private bool committed;

        public AkronActions.StartPosReplacementTransaction StartPosReplacement =>
            startPosReplacement ??= AkronActions.BeginStartPosReplacement(targetMapSid);

        public SetupImportStateTransaction(
            AkronModuleSettings settings,
            AkronModuleSession session,
            string targetMapSid
        ) {
            this.settings = settings;
            this.session = session;
            this.targetMapSid = targetMapSid;
            saveData = AkronModule.Instance == null ? null : AkronModule.SaveData;
            previousSettings = settings.CaptureSetupPackState();
            previousButtonBindings = CaptureButtonBindings(settings);
            previousMenuActionBindings = new Dictionary<string, string>(
                settings.MenuActionBindings ?? new Dictionary<string, string>(),
                StringComparer.Ordinal);
            previousStartPositions = session?.StartPositions;
            previousLoadedAreaSid = session?.LoadedStartPositionsAreaSid ?? string.Empty;
            previousLastLoadedSlot = session?.LastLoadedStartPosSlot ?? 0;
            previousMaps = saveData?.StartPositionsByMap;
            hadPreviousMap = previousMaps != null &&
                             previousMaps.TryGetValue(targetMapSid, out previousMap);
        }

        public void Commit() {
            startPosReplacement?.Commit();
            committed = true;
        }

        public void Dispose() {
            startPosReplacement?.Dispose();
            if (committed) {
                return;
            }

            settings.ApplySetupPackState(previousSettings);
            ApplyButtonBindings(settings, previousButtonBindings);
            settings.MenuActionBindings = new Dictionary<string, string>(
                previousMenuActionBindings,
                StringComparer.Ordinal);
            if (session != null) {
                session.StartPositions = previousStartPositions;
                session.LoadedStartPositionsAreaSid = previousLoadedAreaSid;
                session.LastLoadedStartPosSlot = previousLastLoadedSlot;
            }
            if (saveData != null) {
                if (previousMaps == null) {
                    saveData.StartPositionsByMap = null;
                } else {
                    if (hadPreviousMap) {
                        previousMaps[targetMapSid] = previousMap;
                    } else {
                        previousMaps.Remove(targetMapSid);
                    }
                    saveData.StartPositionsByMap = previousMaps;
                }
            }
        }
    }
}
