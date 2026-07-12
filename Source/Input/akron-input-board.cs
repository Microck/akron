using Celeste;
using Celeste.Mod;
using Monocle;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.Akron;

public enum AkronInputBoardBinding {
    Left,
    Right,
    Up,
    Down,
    Jump,
    Dash,
    Grab,
    CrouchDash,
    Talk,
    Pause,
    Confirm,
    Cancel
}

public sealed class AkronInputBoardElement {
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "Key";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 38;
    public int Height { get; set; } = 38;
    public List<AkronInputBoardBinding> Bindings { get; set; } = new List<AkronInputBoardBinding>();
    public List<Keys> KeyBindings { get; set; } = new List<Keys>();
    public bool Visible { get; set; } = true;
    public int FillColor { get; set; } = AkronInputBoard.DefaultFillColor;
    public int PressedFillColor { get; set; } = AkronInputBoard.DefaultPressedFillColor;
    public int StrokeColor { get; set; } = AkronInputBoard.DefaultStrokeColor;
    public int TextColor { get; set; } = AkronInputBoard.DefaultTextColor;
    public int OutlineWidth { get; set; } = 1;
    public int TextScale { get; set; } = 100;

    public AkronInputBoardElement Clone() {
        return new AkronInputBoardElement {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
            Label = Label,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            Bindings = Bindings?.ToList() ?? new List<AkronInputBoardBinding>(),
            KeyBindings = KeyBindings?.ToList() ?? new List<Keys>(),
            Visible = Visible,
            FillColor = FillColor,
            PressedFillColor = PressedFillColor,
            StrokeColor = StrokeColor,
            TextColor = TextColor,
            OutlineWidth = OutlineWidth,
            TextScale = TextScale
        };
    }
}

public static class AkronInputBoard {
    public const int MinimumElementSize = 18;
    public const int MaximumElementSize = 240;
    public const int MinimumPosition = -2000;
    public const int MaximumPosition = 2000;
    public const int MaximumElements = 48;
    public const int MinimumTextScale = 40;
    public const int MaximumTextScale = 220;
    public const int DefaultFillColor = 0x808080;
    public const int DefaultPressedFillColor = 0xB8B8B8;
    public const int DefaultStrokeColor = 0x202020;
    public const int DefaultTextColor = 0x000000;

    public static List<AkronInputBoardElement> BuildDefaultElements() {
        return BuildCompactElements();
    }

    public static List<AkronInputBoardElement> BuildSplitElements() {
        const int size = 42;
        return new List<AkronInputBoardElement> {
            CreateElement("menu", "Menu", 12, 0, 54, size, new[] { AkronInputBoardBinding.Pause }, new[] { Keys.Escape }),
            CreateElement("demo", "Demo", 72, 0, 54, size, new[] { AkronInputBoardBinding.CrouchDash }, new[] { Keys.V }),
            CreateElement("grab", "Grab", 0, 48, size, size, new[] { AkronInputBoardBinding.Grab }, new[] { Keys.LeftShift, Keys.RightShift }),
            CreateElement("dash", "Dash", 48, 48, size, size, new[] { AkronInputBoardBinding.Dash }, new[] { Keys.X }),
            CreateElement("jump", "Jump", 96, 48, size, size, new[] { AkronInputBoardBinding.Jump }, new[] { Keys.C, Keys.Space }),
            CreateElement("jump-2", "Jump", 24, 96, 90, size, new[] { AkronInputBoardBinding.Jump }, new[] { Keys.Space }),
            CreateElement("up", "W", 240, 30, size, size, new[] { AkronInputBoardBinding.Up }, new[] { Keys.W, Keys.Up }),
            CreateElement("left", "A", 194, 76, size, size, new[] { AkronInputBoardBinding.Left }, new[] { Keys.A, Keys.Left }),
            CreateElement("down", "S", 240, 76, size, size, new[] { AkronInputBoardBinding.Down }, new[] { Keys.S, Keys.Down }),
            CreateElement("right", "D", 286, 76, size, size, new[] { AkronInputBoardBinding.Right }, new[] { Keys.D, Keys.Right })
        };
    }

    public static List<AkronInputBoardElement> BuildCompactElements() {
        const int size = 36;
        const int gap = 4;
        int pitch = size + gap;
        return new List<AkronInputBoardElement> {
            CreateElement("dash", "Dash", 0, 0, size, size, new[] { AkronInputBoardBinding.Dash }, new[] { Keys.X }),
            CreateElement("up", "W", pitch, 0, size, size, new[] { AkronInputBoardBinding.Up }, new[] { Keys.W, Keys.Up }),
            CreateElement("demo", "Demo", pitch * 2, 0, size, size, new[] { AkronInputBoardBinding.CrouchDash }, new[] { Keys.V }),
            CreateElement("left", "A", 0, pitch, size, size, new[] { AkronInputBoardBinding.Left }, new[] { Keys.A, Keys.Left }),
            CreateElement("down", "S", pitch, pitch, size, size, new[] { AkronInputBoardBinding.Down }, new[] { Keys.S, Keys.Down }),
            CreateElement("right", "D", pitch * 2, pitch, size, size, new[] { AkronInputBoardBinding.Right }, new[] { Keys.D, Keys.Right }),
            CreateElement("grab", "Grab", 0, pitch * 2, size, size, new[] { AkronInputBoardBinding.Grab }, new[] { Keys.LeftShift, Keys.RightShift }),
            CreateElement("jump", "Jump", pitch, pitch * 2, size * 2 + gap, size, new[] { AkronInputBoardBinding.Jump }, new[] { Keys.C, Keys.Space })
        };
    }

    public static List<AkronInputBoardElement> BuildKeyboardElements() {
        const int key = 40;
        const int gap = 5;
        int pitch = key + gap;
        return new List<AkronInputBoardElement> {
            CreateElement("dash", "Dash", 0, 0, key, key, new[] { AkronInputBoardBinding.Dash }, new[] { Keys.X }),
            CreateElement("up", "^", pitch, 0, key, key, new[] { AkronInputBoardBinding.Up }, new[] { Keys.W, Keys.Up }),
            CreateElement("demo", "Demo", pitch * 2, 0, key, key, new[] { AkronInputBoardBinding.CrouchDash }, new[] { Keys.V }),
            CreateElement("left", "<", 0, pitch, key, key, new[] { AkronInputBoardBinding.Left }, new[] { Keys.A, Keys.Left }),
            CreateElement("down", "v", pitch, pitch, key, key, new[] { AkronInputBoardBinding.Down }, new[] { Keys.S, Keys.Down }),
            CreateElement("right", ">", pitch * 2, pitch, key, key, new[] { AkronInputBoardBinding.Right }, new[] { Keys.D, Keys.Right }),
            CreateElement("grab", "Grab", pitch * 3, pitch, key, key * 2 + gap, new[] { AkronInputBoardBinding.Grab }, new[] { Keys.LeftShift, Keys.RightShift }),
            CreateElement("jump", "Jump", pitch * 4, pitch, key * 2 + gap, key, new[] { AkronInputBoardBinding.Jump }, new[] { Keys.C }),
            CreateElement("jump-2", "Jump 2", pitch * 4, pitch * 2, key * 2 + gap, key, new[] { AkronInputBoardBinding.Jump }, new[] { Keys.Space })
        };
    }

    public static List<AkronInputBoardElement> BuildBarElements() {
        const int height = 36;
        const int width = 58;
        const int gap = 4;
        return new List<AkronInputBoardElement> {
            CreateElement("jump", "Jump", 0, 0, width, height, new[] { AkronInputBoardBinding.Jump }, new[] { Keys.C, Keys.Space }),
            CreateElement("grab", "Grab", width + gap, 0, width, height, new[] { AkronInputBoardBinding.Grab }, new[] { Keys.LeftShift, Keys.RightShift }),
            CreateElement("dash", "Dash", (width + gap) * 2, 0, width, height, new[] { AkronInputBoardBinding.Dash }, new[] { Keys.X }),
            CreateElement("demo", "Demo", (width + gap) * 3, 0, width, height, new[] { AkronInputBoardBinding.CrouchDash }, new[] { Keys.V })
        };
    }

    public static List<AkronInputBoardElement> CloneElements(IEnumerable<AkronInputBoardElement> elements) {
        return NormalizeElements(elements);
    }

    public static List<AkronInputBoardElement> NormalizeElements(IEnumerable<AkronInputBoardElement> elements) {
        List<AkronInputBoardElement> normalized = new List<AkronInputBoardElement>();
        foreach (AkronInputBoardElement source in elements ?? Enumerable.Empty<AkronInputBoardElement>()) {
            if (normalized.Count >= MaximumElements) {
                break;
            }

            if (source == null) {
                continue;
            }

            AkronInputBoardElement element = source.Clone();
            element.Id = string.IsNullOrWhiteSpace(element.Id) ? Guid.NewGuid().ToString("N") : element.Id.Trim();
            element.Label = string.IsNullOrWhiteSpace(element.Label) ? "Key" : element.Label.Trim();
            element.X = ClampInt(element.X, MinimumPosition, MaximumPosition);
            element.Y = ClampInt(element.Y, MinimumPosition, MaximumPosition);
            element.Width = ClampInt(element.Width, MinimumElementSize, MaximumElementSize);
            element.Height = ClampInt(element.Height, MinimumElementSize, MaximumElementSize);
            element.FillColor = AkronModuleSettings.ClampRgb(element.FillColor);
            element.PressedFillColor = AkronModuleSettings.ClampRgb(element.PressedFillColor);
            element.StrokeColor = AkronModuleSettings.ClampRgb(element.StrokeColor);
            element.TextColor = AkronModuleSettings.ClampRgb(element.TextColor);
            element.OutlineWidth = ClampInt(element.OutlineWidth, 0, 8);
            element.TextScale = ClampInt(element.TextScale, MinimumTextScale, MaximumTextScale);
            element.Bindings = element.Bindings?
                .Distinct()
                .ToList() ?? new List<AkronInputBoardBinding>();
            element.KeyBindings = element.KeyBindings?
                .Distinct()
                .Where(key => key != Keys.None)
                .ToList() ?? new List<Keys>();
            normalized.Add(element);
        }

        return normalized.Count == 0 ? BuildDefaultElements() : normalized;
    }

    public static AkronInputBoardElement CreateCustomElement(int index) {
        return CreateElement(
            "custom-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Key",
            0,
            ClampInt(96 + index * 44, MinimumPosition, MaximumPosition),
            38,
            38,
            Array.Empty<AkronInputBoardBinding>(),
            Array.Empty<Keys>());
    }

    public static string Describe(IReadOnlyList<AkronInputBoardElement> elements) {
        int count = elements?.Count(element => element?.Visible == true) ?? 0;
        return count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " keys";
    }

    public static bool IsPressed(AkronInputBoardElement element, AkronInputBoardSource source) {
        if (element == null) {
            return false;
        }

        if (source == AkronInputBoardSource.KeyboardKeys) {
            foreach (Keys key in element.KeyBindings ?? Enumerable.Empty<Keys>()) {
                if (key != Keys.None && MInput.Keyboard.Check(key)) {
                    return true;
                }
            }

            return false;
        }

        if (element.Bindings == null) {
            return false;
        }

        foreach (AkronInputBoardBinding binding in element.Bindings) {
            if (IsPressed(binding)) {
                return true;
            }
        }

        return false;
    }

    public static string FormatBindings(AkronInputBoardElement element) {
        return element?.Bindings == null || element.Bindings.Count == 0
            ? "Unbound"
            : string.Join("+", element.Bindings);
    }

    public static string FormatKeyBindings(AkronInputBoardElement element) {
        return element?.KeyBindings == null || element.KeyBindings.Count == 0
            ? "Unbound"
            : string.Join("+", element.KeyBindings);
    }

    public static void ApplyLabelPreset(IList<AkronInputBoardElement> elements, AkronInputBoardLabelPreset preset) {
        if (elements == null) {
            return;
        }

        foreach (AkronInputBoardElement element in elements) {
            if (element == null) {
                continue;
            }

            AkronInputBoardBinding binding = element.Bindings?.FirstOrDefault() ?? default;
            element.Label = LabelFor(binding, preset, element.Label);
        }
    }

    public static bool TryParseKeyBindings(string text, out List<Keys> keys) {
        keys = new List<Keys>();
        if (string.IsNullOrWhiteSpace(text)) {
            return true;
        }

        string[] parts = text
            .Split(new[] { '+', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .ToArray();
        foreach (string part in parts) {
            if (!Enum.TryParse(part, ignoreCase: true, out Keys key) || key == Keys.None) {
                keys.Clear();
                return false;
            }

            if (!keys.Contains(key)) {
                keys.Add(key);
            }
        }

        return true;
    }

    private static AkronInputBoardElement CreateElement(
        string id,
        string label,
        int x,
        int y,
        int width,
        int height,
        AkronInputBoardBinding[] bindings,
        Keys[] keys) {
        return new AkronInputBoardElement {
            Id = id,
            Label = label,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Bindings = bindings.ToList(),
            KeyBindings = keys.ToList()
        };
    }

    private static int ClampInt(int value, int minimum, int maximum) {
        if (value < minimum) {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }

    private static bool IsPressed(AkronInputBoardBinding binding) {
        return binding switch {
            AkronInputBoardBinding.Left => Input.MoveX.Value < 0,
            AkronInputBoardBinding.Right => Input.MoveX.Value > 0,
            AkronInputBoardBinding.Up => Input.MoveY.Value < 0,
            AkronInputBoardBinding.Down => Input.MoveY.Value > 0,
            AkronInputBoardBinding.Jump => Input.Jump.Check,
            AkronInputBoardBinding.Dash => Input.Dash.Check,
            AkronInputBoardBinding.Grab => Input.Grab.Check,
            AkronInputBoardBinding.CrouchDash => Input.CrouchDash.Check,
            AkronInputBoardBinding.Talk => Input.Talk.Check,
            AkronInputBoardBinding.Pause => Input.Pause.Check,
            AkronInputBoardBinding.Confirm => Input.MenuConfirm.Check,
            AkronInputBoardBinding.Cancel => Input.MenuCancel.Check,
            _ => false
        };
    }

    private static string LabelFor(AkronInputBoardBinding binding, AkronInputBoardLabelPreset preset, string fallback) {
        return preset switch {
            AkronInputBoardLabelPreset.Names => binding switch {
                AkronInputBoardBinding.Left => "Left",
                AkronInputBoardBinding.Right => "Right",
                AkronInputBoardBinding.Up => "Up",
                AkronInputBoardBinding.Down => "Down",
                AkronInputBoardBinding.Jump => "Jump",
                AkronInputBoardBinding.Dash => "Dash",
                AkronInputBoardBinding.Grab => "Grab",
                AkronInputBoardBinding.CrouchDash => "Demo",
                AkronInputBoardBinding.Talk => "Talk",
                AkronInputBoardBinding.Pause => "Menu",
                AkronInputBoardBinding.Confirm => "Confirm",
                AkronInputBoardBinding.Cancel => "Cancel",
                _ => fallback
            },
            AkronInputBoardLabelPreset.Keyboard => binding switch {
                AkronInputBoardBinding.Left => "A",
                AkronInputBoardBinding.Right => "D",
                AkronInputBoardBinding.Up => "W",
                AkronInputBoardBinding.Down => "S",
                AkronInputBoardBinding.Jump => "Jump",
                AkronInputBoardBinding.Dash => "Dash",
                AkronInputBoardBinding.Grab => "Grab",
                AkronInputBoardBinding.CrouchDash => "Demo",
                AkronInputBoardBinding.Talk => "Talk",
                AkronInputBoardBinding.Pause => "Menu",
                AkronInputBoardBinding.Confirm => "Enter",
                AkronInputBoardBinding.Cancel => "Esc",
                _ => fallback
            },
            AkronInputBoardLabelPreset.Arrows => binding switch {
                AkronInputBoardBinding.Left => "<",
                AkronInputBoardBinding.Right => ">",
                AkronInputBoardBinding.Up => "^",
                AkronInputBoardBinding.Down => "v",
                AkronInputBoardBinding.Jump => "Jump",
                AkronInputBoardBinding.Dash => "Dash",
                AkronInputBoardBinding.Grab => "Grab",
                AkronInputBoardBinding.CrouchDash => "Demo",
                AkronInputBoardBinding.Talk => "Talk",
                AkronInputBoardBinding.Pause => "Menu",
                AkronInputBoardBinding.Confirm => "OK",
                AkronInputBoardBinding.Cancel => "Back",
                _ => fallback
            },
            _ => binding switch {
                AkronInputBoardBinding.Left => "L",
                AkronInputBoardBinding.Right => "R",
                AkronInputBoardBinding.Up => "U",
                AkronInputBoardBinding.Down => "D",
                AkronInputBoardBinding.Jump => "J",
                AkronInputBoardBinding.Dash => "X",
                AkronInputBoardBinding.Grab => "G",
                AkronInputBoardBinding.CrouchDash => "C",
                AkronInputBoardBinding.Talk => "T",
                AkronInputBoardBinding.Pause => "P",
                AkronInputBoardBinding.Confirm => "O",
                AkronInputBoardBinding.Cancel => "B",
                _ => fallback
            }
        };
    }
}

public sealed class AkronControlDisplayPreset {
    public string Format { get; set; } = AkronControlDisplayPresets.PresetFormat;
    public string Name { get; set; } = "Control Display";
    public string CreatedUtc { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public IndicatorCorner Corner { get; set; } = IndicatorCorner.BottomRight;
    public int Scale { get; set; } = 100;
    public int Opacity { get; set; } = 80;
    public AkronInputBoardSource Source { get; set; }
    public AkronInputBoardLabelPreset LabelPreset { get; set; } = AkronInputBoardLabelPreset.Keyboard;
    public List<AkronInputBoardElement> Elements { get; set; } = AkronInputBoard.BuildDefaultElements();
}

public static class AkronControlDisplayPresets {
    public const string PresetArchiveKind = "control-display";
    public const string PresetArchivePayload = "control-display.json";
    public const string PresetFormat = "akron-control-display-v1";
    private const int MaxPresetPayloadBytes = 256 * 1024;

    private static readonly JsonSerializerOptions PresetJsonOptions = new JsonSerializerOptions {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AkronControlDisplayPreset Capture(AkronModuleSettings settings, string name = "") {
        if (settings == null) {
            throw new ArgumentNullException(nameof(settings));
        }

        string presetName = string.IsNullOrWhiteSpace(name) ? "Control Display" : name.Trim();
        return new AkronControlDisplayPreset {
            Name = presetName,
            CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Enabled = settings.ShowTaps,
            Corner = settings.TapDisplayCorner,
            Scale = AkronModuleSettings.ClampPercent(settings.TapDisplayScale, 50, 250),
            Opacity = AkronModuleSettings.ClampOpacity(settings.TapDisplayOpacity),
            Source = settings.InputBoardSource,
            LabelPreset = settings.InputBoardLabelPreset,
            Elements = AkronInputBoard.CloneElements(settings.InputBoardElements)
        };
    }

    public static void Apply(AkronModuleSettings settings, AkronControlDisplayPreset preset) {
        if (settings == null) {
            throw new ArgumentNullException(nameof(settings));
        }

        if (preset == null || !string.Equals(preset.Format, PresetFormat, StringComparison.Ordinal)) {
            throw new InvalidDataException("Unsupported control-display preset.");
        }

        settings.ShowTaps = preset.Enabled;
        settings.TapDisplayCorner = preset.Corner;
        settings.TapDisplayScale = AkronModuleSettings.ClampPercent(preset.Scale, 50, 250);
        settings.TapDisplayOpacity = AkronModuleSettings.ClampOpacity(preset.Opacity);
        settings.InputBoardSource = preset.Source;
        settings.InputBoardLabelPreset = preset.LabelPreset;
        settings.InputBoardElements = preset.Elements;
    }

    public static string ExportCurrent(string name = "") {
        AkronControlDisplayPreset preset = Capture(AkronModule.Settings, name);
        AkronArchiveManifest manifest = new AkronArchiveManifest {
            Kind = PresetArchiveKind,
            KindVersion = 1,
            CreatedAt = preset.CreatedUtc,
            Target = new AkronArchiveTarget {
                Game = "Celeste"
            }
        };

        Directory.CreateDirectory(GetPresetDirectory());
        string fileName = SanitizeFileName(preset.Name) + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + AkronArchive.Extension;
        string path = Path.Combine(GetPresetDirectory(), fileName);
        AkronArchive.WriteSinglePayloadArchive(path, manifest, PresetArchivePayload, JsonSerializer.Serialize(preset, PresetJsonOptions));
        Engine.Scene?.Add(new AkronToast("Exported control-display preset."));
        return path;
    }

    public static bool Import(string pathOrName) {
        string path = ResolvePresetPath(pathOrName);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            Engine.Scene?.Add(new AkronToast("Control-display preset not found."));
            return false;
        }

        AkronControlDisplayPreset preset;
        try {
            string payload = AkronArchive.ReadSinglePayloadArchive(path, PresetArchiveKind, PresetArchivePayload, MaxPresetPayloadBytes, out _);
            preset = JsonSerializer.Deserialize<AkronControlDisplayPreset>(payload, PresetJsonOptions);
        } catch (Exception ex) when (ex is InvalidDataException || ex is JsonException || ex is IOException || ex is UnauthorizedAccessException) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to import Akron control-display archive: " + ex.Message);
            Engine.Scene?.Add(new AkronToast("Unsupported control-display preset."));
            return false;
        }

        try {
            Apply(AkronModule.Settings, preset);
        } catch (InvalidDataException) {
            Engine.Scene?.Add(new AkronToast("Unsupported control-display preset."));
            return false;
        }

        Engine.Scene?.Add(new AkronToast("Imported control-display preset."));
        return true;
    }

    public static string ImportLatest() {
        string path = Directory.Exists(GetPresetDirectory())
            ? Directory.GetFiles(GetPresetDirectory(), "*" + AkronArchive.Extension, SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (string.IsNullOrWhiteSpace(path)) {
            Engine.Scene?.Add(new AkronToast("No control-display presets found."));
            return string.Empty;
        }

        Import(path);
        return path;
    }

    public static string GetPresetDirectory() {
        return Path.Combine(Everest.PathGame, "Saves", "AkronControlDisplay");
    }

    public static void Write(AkronModuleSettings settings, string path, string name = "") {
        AkronControlDisplayPreset preset = Capture(settings, name);
        AkronArchive.WriteSinglePayloadArchive(
            path,
            new AkronArchiveManifest {
                Kind = PresetArchiveKind,
                KindVersion = 1,
                CreatedAt = preset.CreatedUtc,
                Target = new AkronArchiveTarget { Game = "Celeste" }
            },
            PresetArchivePayload,
            JsonSerializer.Serialize(preset, PresetJsonOptions));
    }

    public static AkronControlDisplayPreset Read(string path) {
        string payload = AkronArchive.ReadSinglePayloadArchive(path, PresetArchiveKind, PresetArchivePayload, MaxPresetPayloadBytes, out _);
        AkronControlDisplayPreset preset = JsonSerializer.Deserialize<AkronControlDisplayPreset>(payload, PresetJsonOptions);
        if (preset == null || !string.Equals(preset.Format, PresetFormat, StringComparison.Ordinal)) {
            throw new InvalidDataException("Unsupported control-display preset.");
        }

        return preset;
    }

    private static string ResolvePresetPath(string pathOrName) {
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
        return Path.Combine(GetPresetDirectory(), withExtension);
    }

    private static string SanitizeFileName(string value) {
        string safe = new string((value ?? "control-display")
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)
            .ToArray()).Trim('-', ' ');
        return string.IsNullOrWhiteSpace(safe) ? "control-display" : safe;
    }
}
