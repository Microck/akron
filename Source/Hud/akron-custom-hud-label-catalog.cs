using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed class AkronHudLabelPack {
    public string Format { get; set; } = AkronCustomHudLabels.FormatId;
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    public List<AkronCustomHudLabel> Labels { get; set; } = new List<AkronCustomHudLabel>();
}

public static partial class AkronCustomHudLabels {
    public const string FormatId = "akron-hud-labels-v1";
    public const string ArchiveKind = "hud-labels";
    public const string ArchivePayload = "hud-labels.json";
    private const int MaxLabelPackPayloadBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static List<AkronCustomHudLabel> BuildDefaultLabels() {
        return new List<AkronCustomHudLabel> {
            new AkronCustomHudLabel {
                Name = "Room Timer",
                Text = "Room {room_time}",
                Anchor = AkronHudAnchor.TopLeft,
                X = 48,
                Y = 72
            },
            new AkronCustomHudLabel {
                Name = "Run State",
                Text = "{overlays} / {status}",
                Anchor = AkronHudAnchor.TopRight,
                X = 1570,
                Y = 72,
                TextAlignment = AkronLabelTextAlignment.Right
            },
            new AkronCustomHudLabel {
                Name = "Player",
                Text = "XY {player_x}, {player_y}  V {speed}",
                Anchor = AkronHudAnchor.BottomLeft,
                X = 48,
                Y = 970,
                Color = 0xFFFFFF
            }
        };
    }

    public static List<AkronCustomHudLabel> CloneLabels(IEnumerable<AkronCustomHudLabel> labels) {
        List<AkronCustomHudLabel> cloned = (labels ?? BuildDefaultLabels()).Select(CloneLabel).ToList();
        return cloned.Count == 0 ? BuildDefaultLabels() : cloned;
    }

    public static AkronCustomHudLabel CloneLabel(AkronCustomHudLabel source) {
        if (source == null) {
            return BuildDefaultLabels()[0];
        }

        return new AkronCustomHudLabel {
            Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id,
            Name = string.IsNullOrWhiteSpace(source.Name) ? "Label" : source.Name,
            Text = string.IsNullOrWhiteSpace(source.Text) ? "{room}" : source.Text,
            Visible = source.Visible,
            Anchor = source.Anchor,
            AbsolutePosition = source.AbsolutePosition,
            X = source.X,
            Y = source.Y,
            OffsetX = source.OffsetX,
            OffsetY = source.OffsetY,
            Scale = ClampScale(source.Scale),
            Color = source.Color,
            Opacity = AkronModuleSettings.ClampOpacity(source.Opacity),
            LineSpacing = AkronModuleSettings.ClampCustomLabelLineSpacing(source.LineSpacing),
            Font = source.Font,
            TextAlignment = source.TextAlignment,
            Shadow = source.Shadow,
            ShadowColor = source.ShadowColor,
            ShadowOpacity = AkronModuleSettings.ClampOpacity(source.ShadowOpacity),
            ShadowOffsetX = ClampValue(source.ShadowOffsetX, -24, 24),
            ShadowOffsetY = ClampValue(source.ShadowOffsetY, -24, 24),
            EventMode = source.EventMode,
            EventDelaySeconds = Math.Max(0f, source.EventDelaySeconds),
            EventDurationSeconds = Math.Max(0.1f, source.EventDurationSeconds),
            EventOverridesStyle = source.EventOverridesStyle,
            EventScale = ClampScale(source.EventScale),
            EventColor = source.EventColor,
            EventOpacity = AkronModuleSettings.ClampOpacity(source.EventOpacity)
        };
    }

    public static AkronCustomHudLabel GetActiveLabel() {
        EnsureLabels();
        AkronModule.Settings.CustomHudLabelIndex = AkronModuleSettings.ClampCustomLabelIndex(
            AkronModule.Settings.CustomHudLabelIndex,
            AkronModule.Settings.CustomHudLabelDefinitions.Count);
        return AkronModule.Settings.CustomHudLabelDefinitions[AkronModule.Settings.CustomHudLabelIndex];
    }

    public static void AddPreset(string preset) {
        EnsureLabels();
        AkronCustomHudLabel label = preset switch {
            "fps" => new AkronCustomHudLabel { Name = "FPS/TPS", Text = "FPS {round(fps)}  TPS {tps}", Anchor = AkronHudAnchor.TopLeft },
            "attempt" => new AkronCustomHudLabel { Name = "Attempt", Text = "Attempt {attempt}  Deaths {deaths}", Anchor = AkronHudAnchor.TopLeft },
            "timers" => new AkronCustomHudLabel { Name = "Timers", Text = "Map {map_time}  Room {room_time}", Anchor = AkronHudAnchor.TopLeft },
            "resources" => new AkronCustomHudLabel { Name = "Resources", Text = "STA {stamina}  Dash {dashes}", Anchor = AkronHudAnchor.BottomLeft },
            "inputs" => new AkronCustomHudLabel { Name = "Inputs", Text = "{inputs}", Anchor = AkronHudAnchor.BottomLeft, Color = 0xFFFFFF },
            "proof" => new AkronCustomHudLabel { Name = "Proof", Text = "{status}: {reason}", Anchor = AkronHudAnchor.TopRight, TextAlignment = AkronLabelTextAlignment.Right },
            "clock" => new AkronCustomHudLabel { Name = "Clock", Text = "{clock}", Anchor = AkronHudAnchor.BottomRight, Color = 0xFFFFFF, TextAlignment = AkronLabelTextAlignment.Right },
            "hazard" => new AkronCustomHudLabel { Name = "Hazard Accuracy", Text = "Accuracy {hazard_accuracy}%  Invalid {hazard_invalid}", Anchor = AkronHudAnchor.TopCenter, Visible = false, EventMode = AkronLabelEventMode.OnNoclipDeath, EventOverridesStyle = true, EventScale = 0.6f, EventColor = 0xFFFFFF },
            _ => new AkronCustomHudLabel { Name = "New Label", Text = "{room}" }
        };

        AkronModule.Settings.CustomHudLabelDefinitions.Add(label);
        AkronModule.Settings.CustomHudLabelIndex = AkronModule.Settings.CustomHudLabelDefinitions.Count - 1;
        AkronModule.Settings.LabelRowOrder = AkronModuleSettings.NormalizeLabelRowOrder(AkronModule.Settings.LabelRowOrder, AkronModule.Settings.CustomHudLabelDefinitions);
        Engine.Scene?.Add(new AkronToast("Added label: " + label.Name));
    }

    public static AkronCustomHudLabel AddCustom() {
        EnsureLabels();
        int next = 1;
        HashSet<string> names = AkronModule.Settings.CustomHudLabelDefinitions
            .Select(label => label.Name ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (names.Contains("Custom " + next)) {
            next++;
        }

        AkronCustomHudLabel label = new AkronCustomHudLabel {
            Name = "Custom " + next,
            Text = "{room}",
            Color = 0xFFFFFF
        };
        AkronModule.Settings.CustomHudLabelDefinitions.Add(label);
        AkronModule.Settings.CustomHudLabelIndex = AkronModule.Settings.CustomHudLabelDefinitions.Count - 1;
        AkronModule.Settings.CustomHudLabels = true;
        AkronModule.Settings.LabelRowOrder = AkronModuleSettings.NormalizeLabelRowOrder(AkronModule.Settings.LabelRowOrder, AkronModule.Settings.CustomHudLabelDefinitions);
        Engine.Scene?.Add(new AkronToast("Added label: " + label.Name));
        return label;
    }

    public static void DuplicateActive() {
        AkronCustomHudLabel active = GetActiveLabel();
        AkronCustomHudLabel copy = CloneLabel(active);
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name += " Copy";
        AkronModule.Settings.CustomHudLabelDefinitions.Add(copy);
        AkronModule.Settings.CustomHudLabelIndex = AkronModule.Settings.CustomHudLabelDefinitions.Count - 1;
        AkronModule.Settings.LabelRowOrder = AkronModuleSettings.NormalizeLabelRowOrder(AkronModule.Settings.LabelRowOrder, AkronModule.Settings.CustomHudLabelDefinitions);
        Engine.Scene?.Add(new AkronToast("Duplicated label."));
    }

    public static void DeleteActive() {
        EnsureLabels();
        if (AkronModule.Settings.CustomHudLabelDefinitions.Count <= 1) {
            Engine.Scene?.Add(new AkronToast("At least one label is kept."));
            return;
        }

        AkronModule.Settings.CustomHudLabelDefinitions.RemoveAt(AkronModule.Settings.CustomHudLabelIndex);
        AkronModule.Settings.CustomHudLabelIndex = AkronModuleSettings.ClampCustomLabelIndex(
            AkronModule.Settings.CustomHudLabelIndex,
            AkronModule.Settings.CustomHudLabelDefinitions.Count);
        AkronModule.Settings.LabelRowOrder = AkronModuleSettings.NormalizeLabelRowOrder(AkronModule.Settings.LabelRowOrder, AkronModule.Settings.CustomHudLabelDefinitions);
        Engine.Scene?.Add(new AkronToast("Deleted label."));
    }

    public static string Export() {
        EnsureLabels();
        Directory.CreateDirectory(GetDirectory());
        string path = Path.Combine(GetDirectory(), "hud-labels-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + AkronArchive.Extension);
        Write(AkronModule.Settings.CustomHudLabelDefinitions, path);
        Engine.Scene?.Add(new AkronToast("Exported HUD labels."));
        return path;
    }

    public static void Write(IEnumerable<AkronCustomHudLabel> labels, string path) {
        AkronHudLabelPack pack = new AkronHudLabelPack {
            CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Labels = CloneLabels(labels)
        };
        AkronArchive.WriteSinglePayloadArchive(
            path,
            new AkronArchiveManifest {
                Kind = ArchiveKind,
                KindVersion = 1,
                CreatedAt = pack.CreatedUtc,
                Target = new AkronArchiveTarget { Game = "Celeste" }
            },
            ArchivePayload,
            JsonSerializer.Serialize(pack, JsonOptions));
    }

    public static AkronHudLabelPack Read(string path) {
        string payload = AkronArchive.ReadSinglePayloadArchive(path, ArchiveKind, ArchivePayload, MaxLabelPackPayloadBytes, out _);
        AkronHudLabelPack pack = JsonSerializer.Deserialize<AkronHudLabelPack>(payload, JsonOptions);
        if (pack == null || pack.Format != FormatId || pack.Labels == null) {
            throw new InvalidDataException("Unsupported HUD label pack.");
        }

        pack.Labels = CloneLabels(pack.Labels);
        return pack;
    }

    public static int Import(string pathOrName) {
        string path = ResolvePackPath(pathOrName);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            Engine.Scene?.Add(new AkronToast("HUD label pack not found."));
            return 0;
        }

        AkronHudLabelPack pack;
        try {
            pack = Read(path);
        } catch (Exception ex) when (ex is InvalidDataException || ex is JsonException || ex is IOException || ex is UnauthorizedAccessException) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to import Akron HUD label archive: " + ex.Message);
            Engine.Scene?.Add(new AkronToast("Unsupported HUD label pack."));
            return 0;
        }

        ApplyPack(pack);
        Engine.Scene?.Add(new AkronToast("Imported " + AkronModule.Settings.CustomHudLabelDefinitions.Count + " HUD labels."));
        return AkronModule.Settings.CustomHudLabelDefinitions.Count;
    }

    public static int ImportLatest() {
        if (!Directory.Exists(GetDirectory())) {
            Engine.Scene?.Add(new AkronToast("No HUD label packs found."));
            return 0;
        }

        string path = Directory.GetFiles(GetDirectory(), "*" + AkronArchive.Extension, SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path)) {
            Engine.Scene?.Add(new AkronToast("No HUD label packs found."));
            return 0;
        }

        return Import(path);
    }

    private static void ApplyPack(AkronHudLabelPack pack) {
        AkronModule.Settings.CustomHudLabelDefinitions = CloneLabels(pack.Labels);
        AkronModule.Settings.CustomHudLabelIndex = 0;
        AkronModule.Settings.LabelRowOrder = AkronModuleSettings.NormalizeLabelRowOrder(AkronModule.Settings.LabelRowOrder, AkronModule.Settings.CustomHudLabelDefinitions);
    }

    private static string ResolvePackPath(string pathOrName) {
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
        return Path.Combine(GetDirectory(), withExtension);
    }

    public static string DescribeActive() {
        AkronCustomHudLabel label = GetActiveLabel();
        return AkronModule.Settings.CustomHudLabelDefinitions.Count + " labels / " + label.Name;
    }

    public static string VariableCatalog() {
        return "app_version, overlays, status, reason, chapter, room, map, player_x, player_y, speed, stamina, dashes, deaths, room_deaths, attempt, room_time, map_time, fps, tps, inputs, hazard_accuracy, hazard_invalid, savestate_slot, tas, speedrun_tool, clock";
    }

    private static void EnsureLabels() {
        if (AkronModule.Settings.CustomHudLabelDefinitions == null ||
            AkronModule.Settings.CustomHudLabelDefinitions.Count == 0) {
            AkronModule.Settings.CustomHudLabelDefinitions = BuildDefaultLabels();
        }
    }

    private static string GetDirectory() {
        return Path.Combine(Everest.PathGame, "Saves", "AkronHudLabels");
    }
}
