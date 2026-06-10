using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed class AkronOverlayThemeDefinition {
    public string Format { get; set; } = AkronOverlayThemes.ThemePackFormat;
    public string Name { get; set; } = "Custom";
    public string CreatedUtc { get; set; } = string.Empty;
    public int WindowColor { get; set; } = 0x292929;
    public int HeaderColor { get; set; } = 0xC42A30;
    public int HeaderHoverColor { get; set; } = 0xDC3C42;
    public int FrameColor { get; set; } = 0x000000;
    public int TextColor { get; set; } = 0xFFFFFF;
    public int MutedColor { get; set; } = 0x7D8080;
    public int DisabledColor { get; set; } = 0x909090;
    public int Opacity { get; set; } = 96;
    public int Scale { get; set; } = 100;
    public int Blur { get; set; }
    public int AnimationMs { get; set; } = 80;
}

public static class AkronOverlayThemes {
    public const string ThemeArchiveKind = "theme";
    public const string ThemeArchivePayload = "theme.json";
    public const string ThemePackFormat = "akron-overlay-theme-v1";
    private const int MaxThemePayloadBytes = 64 * 1024;

    private static readonly JsonSerializerOptions ThemeJsonOptions = new JsonSerializerOptions {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Dictionary<AkronOverlayThemePreset, AkronOverlayThemeDefinition> BuiltIns = new Dictionary<AkronOverlayThemePreset, AkronOverlayThemeDefinition> {
        {
            AkronOverlayThemePreset.Default,
            new AkronOverlayThemeDefinition {
                Name = "Default",
                WindowColor = 0x292929,
                HeaderColor = 0xC42A30,
                HeaderHoverColor = 0xDC3C42,
                FrameColor = 0x000000,
                TextColor = 0xFFFFFF,
                MutedColor = 0x7D8080,
                DisabledColor = 0x909090
            }
        },
        {
            AkronOverlayThemePreset.Monochrome,
            new AkronOverlayThemeDefinition {
                Name = "Monochrome",
                WindowColor = 0x202020,
                HeaderColor = 0x555555,
                HeaderHoverColor = 0x777777,
                FrameColor = 0x222222,
                TextColor = 0xFFFFFF,
                MutedColor = 0xB0B0B0,
                DisabledColor = 0x777777
            }
        },
        {
            AkronOverlayThemePreset.HighContrast,
            new AkronOverlayThemeDefinition {
                Name = "High Contrast",
                WindowColor = 0x000000,
                HeaderColor = 0xFFD400,
                HeaderHoverColor = 0xFFF067,
                FrameColor = 0x202020,
                TextColor = 0xFFFFFF,
                MutedColor = 0xE8E8E8,
                DisabledColor = 0xA0A0A0
            }
        },
        {
            AkronOverlayThemePreset.Midnight,
            new AkronOverlayThemeDefinition {
                Name = "Midnight",
                WindowColor = 0x111A24,
                HeaderColor = 0x2A8DCC,
                HeaderHoverColor = 0x36A4E8,
                FrameColor = 0x142334,
                TextColor = 0xF4FAFF,
                MutedColor = 0x8EA8BA,
                DisabledColor = 0x60727F
            }
        },
        {
            AkronOverlayThemePreset.Crimson,
            new AkronOverlayThemeDefinition {
                Name = "Crimson",
                WindowColor = 0x241214,
                HeaderColor = 0xB4132F,
                HeaderHoverColor = 0xE02043,
                FrameColor = 0x241014,
                TextColor = 0xFFF4F5,
                MutedColor = 0xBD8C92,
                DisabledColor = 0x8C6066
            }
        },
        {
            AkronOverlayThemePreset.Terminal,
            new AkronOverlayThemeDefinition {
                Name = "Terminal",
                WindowColor = 0x071008,
                HeaderColor = 0x20A05A,
                HeaderHoverColor = 0x30C774,
                FrameColor = 0x0F2415,
                TextColor = 0xD7FFD9,
                MutedColor = 0x72A879,
                DisabledColor = 0x44684B
            }
        },
        {
            AkronOverlayThemePreset.Symbiote,
            new AkronOverlayThemeDefinition {
                Name = "Symbiote",
                WindowColor = 0x241C2B,
                HeaderColor = 0xB86C8B,
                HeaderHoverColor = 0xD88AA5,
                FrameColor = 0x33263B,
                TextColor = 0xF6E4EE,
                MutedColor = 0xB69AAF,
                DisabledColor = 0x7F6878
            }
        },
        {
            AkronOverlayThemePreset.Carbon,
            new AkronOverlayThemeDefinition {
                Name = "Carbon",
                WindowColor = 0x393B3B,
                HeaderColor = 0xEE6900,
                HeaderHoverColor = 0xFF8524,
                FrameColor = 0x171718,
                TextColor = 0xD8D2C3,
                MutedColor = 0xACA693,
                DisabledColor = 0x727474
            }
        },
        {
            AkronOverlayThemePreset.Retro,
            new AkronOverlayThemeDefinition {
                Name = "Retro",
                WindowColor = 0xACA693,
                HeaderColor = 0x67635B,
                HeaderHoverColor = 0x91867A,
                FrameColor = 0xD8D2C3,
                TextColor = 0x171718,
                MutedColor = 0x67635B,
                DisabledColor = 0x91867A
            }
        },
        {
            AkronOverlayThemePreset.Coniferous,
            new AkronOverlayThemeDefinition {
                Name = "Coniferous",
                WindowColor = 0x393B3B,
                HeaderColor = 0x00773A,
                HeaderHoverColor = 0x689B34,
                FrameColor = 0x171718,
                TextColor = 0xD8D2C3,
                MutedColor = 0xACA693,
                DisabledColor = 0x768E72
            }
        },
        {
            AkronOverlayThemePreset.Wine,
            new AkronOverlayThemeDefinition {
                Name = "Wine",
                WindowColor = 0x241014,
                HeaderColor = 0x7F1D2D,
                HeaderHoverColor = 0xA52A3A,
                FrameColor = 0x16090C,
                TextColor = 0xFFF1F3,
                MutedColor = 0xC58A92,
                DisabledColor = 0x8D5D65
            }
        }
    };

    public static AkronOverlayThemeDefinition CurrentDefinition() {
        if (AkronModule.Settings.OverlayThemePreset == AkronOverlayThemePreset.Custom) {
            return CaptureCustomDefinition();
        }

        return BuiltIns.TryGetValue(AkronModule.Settings.OverlayThemePreset, out AkronOverlayThemeDefinition definition)
            ? CloneDefinition(definition)
            : CloneDefinition(BuiltIns[AkronOverlayThemePreset.Default]);
    }

    public static string DisplayName(AkronOverlayThemePreset preset) {
        return preset == AkronOverlayThemePreset.Custom
            ? (string.IsNullOrWhiteSpace(AkronModule.Settings.CustomOverlayThemeName) ? "Custom" : AkronModule.Settings.CustomOverlayThemeName.Trim())
            : BuiltIns.TryGetValue(preset, out AkronOverlayThemeDefinition definition) ? definition.Name : "Default";
    }

    public static string CurrentDisplayName() {
        return DisplayName(AkronModule.Settings.OverlayThemePreset);
    }

    public static AkronOverlayThemePreset NextPreset(AkronOverlayThemePreset preset) {
        return preset switch {
            AkronOverlayThemePreset.Default => AkronOverlayThemePreset.Monochrome,
            AkronOverlayThemePreset.Monochrome => AkronOverlayThemePreset.HighContrast,
            AkronOverlayThemePreset.HighContrast => AkronOverlayThemePreset.Midnight,
            AkronOverlayThemePreset.Midnight => AkronOverlayThemePreset.Crimson,
            AkronOverlayThemePreset.Crimson => AkronOverlayThemePreset.Terminal,
            AkronOverlayThemePreset.Terminal => AkronOverlayThemePreset.Symbiote,
            AkronOverlayThemePreset.Symbiote => AkronOverlayThemePreset.Carbon,
            AkronOverlayThemePreset.Carbon => AkronOverlayThemePreset.Retro,
            AkronOverlayThemePreset.Retro => AkronOverlayThemePreset.Coniferous,
            AkronOverlayThemePreset.Coniferous => AkronOverlayThemePreset.Wine,
            AkronOverlayThemePreset.Wine => AkronOverlayThemePreset.Custom,
            _ => AkronOverlayThemePreset.Default
        };
    }

    public static Color WindowColor() => ToColor(CurrentDefinition().WindowColor);
    public static Color HeaderColor() => ToColor(CurrentDefinition().HeaderColor);
    public static Color HeaderHoverColor() => ToColor(CurrentDefinition().HeaderHoverColor);
    public static Color FrameColor() => ToColor(CurrentDefinition().FrameColor);
    public static Color TextColor() => ToColor(CurrentDefinition().TextColor);
    public static Color MutedColor() => ToColor(CurrentDefinition().MutedColor);
    public static Color DisabledColor() => ToColor(CurrentDefinition().DisabledColor);

    public static Color ToColor(int rgb) {
        int clamped = AkronModuleSettings.ClampRgb(rgb);
        return new Color((clamped >> 16) & 0xFF, (clamped >> 8) & 0xFF, clamped & 0xFF);
    }

    public static string ExportCurrentTheme(string name = "") {
        AkronOverlayThemeDefinition definition = CaptureCurrentDefinition();
        if (!string.IsNullOrWhiteSpace(name)) {
            definition.Name = name.Trim();
        }

        definition.CreatedUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        AkronArchiveManifest manifest = new AkronArchiveManifest {
            Kind = ThemeArchiveKind,
            KindVersion = 1,
            CreatedAt = definition.CreatedUtc,
            Target = new AkronArchiveTarget {
                Game = "Celeste"
            }
        };

        Directory.CreateDirectory(GetThemeDirectory());
        string fileName = SanitizeFileName(definition.Name) + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", System.Globalization.CultureInfo.InvariantCulture) + AkronArchive.Extension;
        string path = Path.Combine(GetThemeDirectory(), fileName);
        AkronArchive.WriteSinglePayloadArchive(path, manifest, ThemeArchivePayload, JsonSerializer.Serialize(definition, ThemeJsonOptions));
        Engine.Scene?.Add(new AkronToast("Exported theme " + definition.Name + "."));
        return path;
    }

    public static bool ImportTheme(string pathOrName) {
        string path = ResolveThemePath(pathOrName);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            Engine.Scene?.Add(new AkronToast("Theme pack not found."));
            return false;
        }

        AkronOverlayThemeDefinition definition;
        try {
            string payload = AkronArchive.ReadSinglePayloadArchive(path, ThemeArchiveKind, ThemeArchivePayload, MaxThemePayloadBytes, out _);
            definition = JsonSerializer.Deserialize<AkronOverlayThemeDefinition>(payload, ThemeJsonOptions);
        } catch (Exception ex) when (ex is InvalidDataException || ex is JsonException || ex is IOException || ex is UnauthorizedAccessException) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to import Akron theme archive: " + ex.Message);
            Engine.Scene?.Add(new AkronToast("Unsupported theme pack."));
            return false;
        }

        if (definition == null || !string.Equals(definition.Format, ThemePackFormat, StringComparison.Ordinal)) {
            Engine.Scene?.Add(new AkronToast("Unsupported theme pack."));
            return false;
        }

        ApplyCustomDefinition(definition);
        Engine.Scene?.Add(new AkronToast("Imported theme " + AkronModule.Settings.CustomOverlayThemeName + "."));
        return true;
    }

    public static string ImportLatestTheme() {
        string path = Directory.Exists(GetThemeDirectory())
            ? Directory.GetFiles(GetThemeDirectory(), "*" + AkronArchive.Extension, SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (string.IsNullOrWhiteSpace(path)) {
            Engine.Scene?.Add(new AkronToast("No theme packs found."));
            return string.Empty;
        }

        ImportTheme(path);
        return path;
    }

    public static string GetThemeDirectory() {
        return Path.Combine(Everest.PathGame, "Saves", "AkronThemes");
    }

    public static void CopyPresetToCustom() {
        ApplyCustomDefinition(CaptureCurrentDefinition());
    }

    public static AkronOverlayThemeDefinition CaptureCurrentDefinition() {
        AkronOverlayThemeDefinition definition = CurrentDefinition();
        definition.Opacity = AkronModuleSettings.ClampOverlayOpacity(AkronModule.Settings.OverlayOpacity);
        definition.Scale = AkronModuleSettings.ClampOverlayScale(AkronModule.Settings.OverlayScale);
        definition.Blur = AkronModuleSettings.ClampOverlayBlur(AkronModule.Settings.OverlayBlur);
        definition.AnimationMs = AkronModuleSettings.ClampOverlayAnimationMs(AkronModule.Settings.OverlayAnimationMs);
        return definition;
    }

    private static AkronOverlayThemeDefinition CaptureCustomDefinition() {
        return new AkronOverlayThemeDefinition {
            Name = string.IsNullOrWhiteSpace(AkronModule.Settings.CustomOverlayThemeName) ? "Custom" : AkronModule.Settings.CustomOverlayThemeName.Trim(),
            WindowColor = AkronModuleSettings.ClampRgb(AkronModule.Settings.CustomOverlayWindowColor),
            HeaderColor = AkronModuleSettings.ClampRgb(AkronModule.Settings.CustomOverlayHeaderColor),
            HeaderHoverColor = AkronModuleSettings.ClampRgb(AkronModule.Settings.CustomOverlayHeaderHoverColor),
            FrameColor = AkronModuleSettings.ClampRgb(AkronModule.Settings.CustomOverlayFrameColor),
            TextColor = AkronModuleSettings.ClampRgb(AkronModule.Settings.CustomOverlayTextColor),
            MutedColor = AkronModuleSettings.ClampRgb(AkronModule.Settings.CustomOverlayMutedColor),
            DisabledColor = AkronModuleSettings.ClampRgb(AkronModule.Settings.CustomOverlayDisabledColor),
            Opacity = AkronModuleSettings.ClampOverlayOpacity(AkronModule.Settings.OverlayOpacity),
            Scale = AkronModuleSettings.ClampOverlayScale(AkronModule.Settings.OverlayScale),
            Blur = AkronModuleSettings.ClampOverlayBlur(AkronModule.Settings.OverlayBlur),
            AnimationMs = AkronModuleSettings.ClampOverlayAnimationMs(AkronModule.Settings.OverlayAnimationMs)
        };
    }

    private static void ApplyCustomDefinition(AkronOverlayThemeDefinition definition) {
        AkronModule.Settings.CustomOverlayThemeName = string.IsNullOrWhiteSpace(definition.Name) ? "Custom" : definition.Name.Trim();
        AkronModule.Settings.CustomOverlayWindowColor = AkronModuleSettings.ClampRgb(definition.WindowColor);
        AkronModule.Settings.CustomOverlayHeaderColor = AkronModuleSettings.ClampRgb(definition.HeaderColor);
        AkronModule.Settings.CustomOverlayHeaderHoverColor = AkronModuleSettings.ClampRgb(definition.HeaderHoverColor);
        AkronModule.Settings.CustomOverlayFrameColor = AkronModuleSettings.ClampRgb(definition.FrameColor);
        AkronModule.Settings.CustomOverlayTextColor = AkronModuleSettings.ClampRgb(definition.TextColor);
        AkronModule.Settings.CustomOverlayMutedColor = AkronModuleSettings.ClampRgb(definition.MutedColor);
        AkronModule.Settings.CustomOverlayDisabledColor = AkronModuleSettings.ClampRgb(definition.DisabledColor);
        AkronModule.Settings.OverlayOpacity = AkronModuleSettings.ClampOverlayOpacity(definition.Opacity);
        AkronModule.Settings.OverlayScale = AkronModuleSettings.ClampOverlayScale(definition.Scale);
        AkronModule.Settings.OverlayBlur = AkronModuleSettings.ClampOverlayBlur(definition.Blur);
        AkronModule.Settings.OverlayAnimationMs = AkronModuleSettings.ClampOverlayAnimationMs(definition.AnimationMs);
        AkronModule.Settings.OverlayThemePreset = AkronOverlayThemePreset.Custom;
    }

    private static AkronOverlayThemeDefinition CloneDefinition(AkronOverlayThemeDefinition definition) {
        return new AkronOverlayThemeDefinition {
            Name = definition.Name,
            CreatedUtc = definition.CreatedUtc,
            WindowColor = definition.WindowColor,
            HeaderColor = definition.HeaderColor,
            HeaderHoverColor = definition.HeaderHoverColor,
            FrameColor = definition.FrameColor,
            TextColor = definition.TextColor,
            MutedColor = definition.MutedColor,
            DisabledColor = definition.DisabledColor,
            Opacity = definition.Opacity,
            Scale = definition.Scale,
            Blur = definition.Blur,
            AnimationMs = definition.AnimationMs
        };
    }

    private static string ResolveThemePath(string pathOrName) {
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
        return Path.Combine(GetThemeDirectory(), withExtension);
    }

    private static string SanitizeFileName(string value) {
        string safe = new string((value ?? "theme")
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)
            .ToArray()).Trim('-', ' ');
        return string.IsNullOrWhiteSpace(safe) ? "theme" : safe;
    }
}
