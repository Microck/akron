using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

// The feature guide is hand-written, so its Classification column can drift from what the overlay
// actually shows. These tests rebuild every overlay row and call the same private methods the
// overlay uses for its row chip and popup tooltip, then compare them with the documented value.
// Deriving the expected value from code keeps this free of a second hand-maintained table.
[Collection(AkronSharedStateCollection.Name)]
public sealed class DocsClassificationTests
{
    private const string NoClassification = "None";

    // Feature-guide pages that document one overlay tab each. external-tools.mdx names its tab in a
    // Tool column instead. Pages without overlay tables (index, custom-labels) are absent on purpose.
    private static readonly Dictionary<string, string> TabByPage = new(StringComparer.Ordinal)
    {
        ["global"] = "Global",
        ["level"] = "Level",
        ["startpos"] = "StartPos",
        ["backups"] = "Backups",
        ["bypass"] = "Bypass",
        ["keybinds"] = "Keybinds",
        ["player"] = "Player",
        ["sound"] = "Sound",
        ["creator"] = "Creator",
        ["interface"] = "Interface",
        ["labels"] = "Labels",
        ["shortcuts"] = "Shortcuts",
        ["internal-recorder"] = "Internal Recorder",
    };

    // FPS Bypass and TPS Bypass live on the Global tab but only exist while Motion Smoothing is
    // loaded, so the docs list them under that tool.
    private const string MotionSmoothingTool = "Motion Smoothing";

    // A few sections document the controls of a window or popup opened from one row, in a table
    // whose first column names the control instead of the row. The section heading identifies the
    // window; this maps it to the row that opens it, whose popup context classifies the controls.
    private static readonly Dictionary<string, string> ParentRowByHeading = new(StringComparer.Ordinal)
    {
        ["Trigger controls"] = "Triggers",
        ["Placement editor"] = "Place StartPos",
        ["Community packs browser"] = "Community Packs",
    };

    // sound.mdx documents the shared popup of every per-sound override row with one placeholder
    // row instead of repeating it for each sound.
    private const string SoundEventPlaceholder = "[Sound event]";

    // Keybind rows have no chip of their own, so the guide classifies the action each binding
    // fires. Most action keys name an overlay row or popup action, which classifies them. The
    // Speedrun Tool slot bindings and the menu toggle have no overlay row, so they are listed here
    // with the feature their input handler records (null when it records none).
    private static readonly Dictionary<string, AkronFeatureKind?> KindByUnlistedActionKey = new(StringComparer.Ordinal)
    {
        ["StartPos/Capture State"] = AkronFeatureKind.Savestates,
        ["StartPos/Restore State"] = AkronFeatureKind.Savestates,
        ["__akron_overlay_toggle"] = null,
    };

    // Rows where the overlay chip disagrees with what the feature records, so the guide keeps the
    // recorded classification. The randomizer row and its popup controls classify through
    // ExtendedVariantMode (Goldberry/Hardlist clear), but each of these controls calls
    // RecordRandomizerCheatUseIfEnabled, which records Cheat while the randomizer is on.
    // Remove an entry once the code classifies it correctly; the test fails until then.
    private static readonly HashSet<string> KnownCodeMisclassifications = new(StringComparer.Ordinal)
    {
        "Extended Variant Mode | Extended Variants Randomizer",
        "Extended Variant Mode | Extended Variants Randomizer > Change variants randomly",
        "Extended Variant Mode | Extended Variants Randomizer > Reroll mode",
        "Extended Variant Mode | Extended Variants Randomizer > Interval",
        "Extended Variant Mode | Extended Variants Randomizer > Max",
    };

    [Fact]
    public void DocumentedRowClassificationsMatchTheOverlay()
    {
        Dictionary<string, Dictionary<string, OverlayRow>> rowsByTab = BuildOverlayRowsByTab();
        List<string> mismatches = new();

        foreach (DocRow doc in ReadDocRows())
        {
            string subject = doc.Tab + " | " + doc.Option + (doc.MenuControl == null ? string.Empty : " > " + doc.MenuControl);
            if (!rowsByTab.TryGetValue(doc.Tab, out Dictionary<string, OverlayRow>? rows))
            {
                mismatches.Add($"{doc.Location}: no overlay tab named {doc.Tab}.");
                continue;
            }

            List<OverlayRow> parents = doc.Option == SoundEventPlaceholder && doc.Tab == "Sound"
                ? rows.Values.Where(row => row.IsSoundOverride).ToList()
                : rows.TryGetValue(doc.Option, out OverlayRow? row) ? new List<OverlayRow> { row } : new List<OverlayRow>();
            if (parents.Count == 0)
            {
                mismatches.Add($"{doc.Location}: {doc.Tab} has no row named {doc.Option}.");
                continue;
            }

            foreach (OverlayRow parent in parents)
            {
                string expected = doc.MenuControl == null
                    ? parent.Classification
                    : DescribeMenuControlClassification(parent, doc.MenuControl, doc.Classification);
                bool matches = string.Equals(expected, doc.Classification, StringComparison.Ordinal);
                if (KnownCodeMisclassifications.Contains(subject))
                {
                    if (matches)
                    {
                        mismatches.Add($"{doc.Location}: {subject} now matches the overlay; remove it from KnownCodeMisclassifications.");
                    }
                }
                else if (!matches)
                {
                    mismatches.Add($"{doc.Location}: {subject} is documented as {doc.Classification} but the overlay shows {expected} (row {parent.Label}).");
                }
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void EveryClassifiedOverlayRowIsDocumented()
    {
        List<DocRow> docs = ReadDocRows();
        HashSet<(string Tab, string Option)> documented = docs
            .Where(doc => doc.MenuControl == null)
            .Select(doc => (doc.Tab, doc.Option.ToLowerInvariant()))
            .ToHashSet();
        bool soundOverridesDocumented = docs.Any(doc => doc.Tab == "Sound" && doc.Option == SoundEventPlaceholder);
        HashSet<string> defaultKeybindLabels = BuildDefaultKeybindLabels();
        List<string> missing = new();

        foreach ((string tab, Dictionary<string, OverlayRow> rows) in BuildOverlayRowsByTab())
        {
            foreach (OverlayRow row in rows.Values)
            {
                // Custom HUD label rows are user data, documented by + Custom and custom-labels.mdx.
                // The Keybinds tab lists popup actions only once the player binds them, so only
                // its built-in default rows need a guide entry.
                if (row.Classification == NoClassification ||
                    row.IsCustomHudLabel ||
                    row.IsSoundOverride && soundOverridesDocumented ||
                    tab == "Keybinds" && row.IsKeybind && !defaultKeybindLabels.Contains(row.Label))
                {
                    continue;
                }

                if (!documented.Contains((tab, row.Label.ToLowerInvariant())))
                {
                    missing.Add($"{tab} | {row.Label} ({row.Classification}) has no feature-guide row.");
                }
            }
        }

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    private static HashSet<string> BuildDefaultKeybindLabels()
    {
        MethodInfo specs = typeof(AkronOverlay).GetMethod("BuildDefaultKeybindOverviewSpecs", BindingFlags.NonPublic | BindingFlags.Static)!;
        return ((System.Collections.IEnumerable)specs.Invoke(null, null)!)
            .Cast<object>()
            .Select(spec => (string)spec.GetType().GetProperty("Label")!.GetValue(spec)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record DocRow(string Location, string Tab, string Option, string? MenuControl, string Classification);

    private sealed record OverlayRow(
        string Label,
        AkronFeatureKind? FeatureKind,
        string Classification,
        string ActionKey,
        bool IsKeybind,
        bool IsSoundOverride,
        bool IsCustomHudLabel);

    // Reads every markdown table whose last column is Classification. Top-level tables start with
    // Option | Control (or Tool | Option | Control); popup tables start with Option | Menu control.
    // Any other classified table shape fails, so a new layout cannot be skipped silently.
    private static List<DocRow> ReadDocRows()
    {
        string guide = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../docs/feature-guide"));
        List<DocRow> rows = new();
        foreach (string path in Directory.GetFiles(guide, "*.mdx").OrderBy(path => path, StringComparer.Ordinal))
        {
            string page = Path.GetFileNameWithoutExtension(path);
            string[] lines = File.ReadAllLines(path);
            // external-tools.mdx spans several tabs; its popup rows take the tab of their parent row.
            Dictionary<string, string> tabByOption = new(StringComparer.OrdinalIgnoreCase);
            string heading = string.Empty;
            string[]? header = null;
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.StartsWith('#'))
                {
                    heading = line.TrimStart('#').Trim();
                }

                if (!line.StartsWith('|'))
                {
                    header = null;
                    continue;
                }

                string[] cells = SplitTableRow(line);
                if (header == null)
                {
                    header = cells;
                    continue;
                }

                if (cells.All(cell => cell.Trim('-', ':').Length == 0) || header[^1] != "Classification")
                {
                    continue;
                }

                string location = $"{page}.mdx:{index + 1}";
                Assert.True(cells.Length == header.Length, $"{location}: expected {header.Length} cells, found {cells.Length}.");
                DocRow row = ToDocRow(page, location, heading, header, cells, tabByOption);
                if (row.MenuControl == null)
                {
                    tabByOption.TryAdd(row.Option, row.Tab);
                }

                rows.AddRange(ExpandNumberedRange(row));
            }
        }

        Assert.NotEmpty(rows);
        return rows;
    }

    private static DocRow ToDocRow(string page, string location, string heading, string[] header, string[] cells, Dictionary<string, string> tabByOption)
    {
        string classification = cells[^1];
        if (ParentRowByHeading.TryGetValue(heading, out string? parent))
        {
            return new DocRow(location, ResolveTab(page, location, parent, tabByOption), parent, cells[0], classification);
        }

        if (header[0] == "Tool" && header[1] == "Option")
        {
            string tab = cells[0] == MotionSmoothingTool ? "Global" : cells[0];
            return new DocRow(location, tab, cells[1], null, classification);
        }

        Assert.True(header[0] == "Option", $"{location}: unsupported classified table header '{string.Join(" | ", header)}'.");
        string option = cells[0];
        return header[1] == "Menu control"
            ? new DocRow(location, ResolveTab(page, location, option, tabByOption), option, cells[1], classification)
            : new DocRow(location, ResolveTab(page, location, option, new()), option, null, classification);
    }

    private static string ResolveTab(string page, string location, string option, Dictionary<string, string> tabByOption)
    {
        if (tabByOption.TryGetValue(option, out string? tab))
        {
            return tab;
        }

        Assert.True(TabByPage.TryGetValue(page, out string? pageTab), $"{location}: {page}.mdx is not mapped to an overlay tab.");
        return pageTab!;
    }

    // keybinds.mdx folds numbered rows such as "StartPos Slot 1-9" into one documented row.
    private static IEnumerable<DocRow> ExpandNumberedRange(DocRow row)
    {
        Match range = Regex.Match(row.Option, @"^(?<prefix>.*\D)(?<first>\d+)-(?<last>\d+)$");
        if (!range.Success || row.MenuControl != null)
        {
            yield return row;
            yield break;
        }

        int first = int.Parse(range.Groups["first"].Value);
        int last = int.Parse(range.Groups["last"].Value);
        for (int number = first; number <= last; number++)
        {
            yield return row with { Option = range.Groups["prefix"].Value + number };
        }
    }

    private static string[] SplitTableRow(string line)
    {
        return line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
    }

    // Builds every base and external tool tab the way the overlay does with each optional mod
    // loaded, keyed by row label. Built-in rows come first, matching the overlay's own lookup.
    private static Dictionary<string, Dictionary<string, OverlayRow>> BuildOverlayRowsByTab()
    {
        Type overlay = typeof(AkronOverlay);
        string[] baseTabs = (string[])overlay.GetField("BaseTabs", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        string[] externalTabs = (string[])overlay.GetField("ExternalToolTabs", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        MethodInfo buildEntries = overlay.GetMethod("BuildDisplayEntriesForTab", BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo buildGlobal = overlay.GetMethod("BuildGlobalEntries", BindingFlags.NonPublic | BindingFlags.Static)!;

        Dictionary<string, Dictionary<string, OverlayRow>> rowsByTab = new(StringComparer.Ordinal);
        foreach (string tab in baseTabs.Concat(externalTabs))
        {
            IEnumerable<object> entries = ((System.Collections.IEnumerable)(tab == "Global"
                ? buildGlobal.Invoke(null, new object[] { true })!
                : buildEntries.Invoke(null, new object?[] { tab, null })!)).Cast<object>();
            // The Sound tab opens with its groups collapsed; the per-sound rows are real rows too.
            if (tab == "Sound")
            {
                entries = entries.Concat(BuildSoundOverrideEntries());
            }

            Dictionary<string, OverlayRow> rows = new(StringComparer.OrdinalIgnoreCase);
            foreach (object entry in entries)
            {
                OverlayRow row = ToOverlayRow(tab, entry);
                rows.TryAdd(row.Label, row);
            }

            rowsByTab[tab] = rows;
        }

        foreach (OverlayRow keybind in rowsByTab["Keybinds"].Values.Where(row => row.IsKeybind).ToList())
        {
            rowsByTab["Keybinds"][keybind.Label] = keybind with { Classification = DescribeBoundActionClassification(keybind.ActionKey, rowsByTab) };
        }

        return rowsByTab;
    }

    private static OverlayRow ToOverlayRow(string tab, object entry)
    {
        Type overlay = typeof(AkronOverlay);
        Type actionEntryType = overlay.GetNestedType("ActionEntry", BindingFlags.NonPublic)!;
        MethodInfo describe = overlay.GetMethod("DescribeEntryClassification", BindingFlags.NonPublic | BindingFlags.Static)!;
        object action = Activator.CreateInstance(actionEntryType, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new[] { tab, entry }, null)!;
        T Get<T>(string property) => (T)actionEntryType.GetProperty(property)!.GetValue(action)!;

        string classification = (string)describe.Invoke(null, new[] { action })!;
        string control = Get<object>("Control").ToString()!;
        return new OverlayRow(
            Get<string>("Label"),
            (AkronFeatureKind?)actionEntryType.GetProperty("FeatureKind")!.GetValue(action),
            classification.Length == 0 ? NoClassification : classification,
            Get<string>("ActionKey"),
            control is "Keybind" or "KeybindReadOnly",
            control != "GroupHeader" && !string.IsNullOrEmpty((string?)actionEntryType.GetProperty("SoundGroupLabel")!.GetValue(action)),
            Get<bool>("IsCustomHudLabelRow"));
    }

    private static IEnumerable<object> BuildSoundOverrideEntries()
    {
        Type overlay = typeof(AkronOverlay);
        MethodInfo buildVolumeEntries = overlay.GetMethod("BuildSoundVolumeEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        System.Collections.IEnumerable groups = (System.Collections.IEnumerable)overlay.GetField("SoundGroups", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        foreach (object group in groups)
        {
            Type groupType = group.GetType();
            object labels = groupType.GetProperty("SoundLabels")!.GetValue(group)!;
            object groupLabel = groupType.GetProperty("Label")!.GetValue(group)!;
            foreach (object entry in (System.Collections.IEnumerable)buildVolumeEntries.Invoke(null, new[] { labels, groupLabel })!)
            {
                yield return entry;
            }
        }
    }

    // Action keys are "Tab/Label" for overlay rows and "popup/PopupKey/Action" for popup actions.
    private static string DescribeBoundActionClassification(string actionKey, Dictionary<string, Dictionary<string, OverlayRow>> rowsByTab)
    {
        if (KindByUnlistedActionKey.TryGetValue(actionKey, out AkronFeatureKind? unlistedKind))
        {
            return unlistedKind.HasValue ? AkronModuleSettings.FormatStatus(AkronFeatureRegistry.Classify(unlistedKind.Value)) : NoClassification;
        }

        IEnumerable<OverlayRow> sourceRows = rowsByTab.Where(tab => tab.Key != "Keybinds").SelectMany(tab => tab.Value.Values);
        const string popupPrefix = "popup/";
        if (actionKey.StartsWith(popupPrefix, StringComparison.Ordinal))
        {
            // Labels such as "Journal Snapshot / Compare" contain slashes, so match the parent
            // label as a prefix instead of splitting the key.
            OverlayRow? parent = sourceRows.FirstOrDefault(row => actionKey.StartsWith(popupPrefix + row.Label + "/", StringComparison.OrdinalIgnoreCase));
            return parent == null
                ? $"unresolved action {actionKey}"
                : DescribePopupTooltipClassification(parent, actionKey[(popupPrefix.Length + parent.Label.Length + 1)..]);
        }

        OverlayRow? source = sourceRows.FirstOrDefault(row => string.Equals(row.ActionKey, actionKey, StringComparison.OrdinalIgnoreCase));
        return source?.Classification ?? $"unresolved action {actionKey}";
    }

    // Popup tooltips classify a menu control through the parent row's label and feature kind.
    // A documented split such as "Normal clear for Extrapolate; Cheat for Interpolate" names the
    // choice-level labels ("Objects: Extrapolate"), which the registry classifies separately; the
    // expected value is rebuilt in the same shape so each choice is checked.
    private static string DescribeMenuControlClassification(OverlayRow parent, string menuControl, string documented)
    {
        string[] segments = documented.Split("; ");
        if (segments.Length == 1)
        {
            return DescribePopupTooltipClassification(parent, menuControl);
        }

        return string.Join("; ", segments.Select(segment =>
        {
            int split = segment.LastIndexOf(" for ", StringComparison.Ordinal);
            string choice = split < 0 ? segment : segment[(split + " for ".Length)..];
            return DescribePopupTooltipClassification(parent, menuControl + ": " + choice) + " for " + choice;
        }));
    }

    private static string DescribePopupTooltipClassification(OverlayRow parent, string suboptionLabel)
    {
        Type overlay = typeof(AkronOverlay);
        FieldInfo labelField = overlay.GetField("activeOptionsPopupLabel", BindingFlags.NonPublic | BindingFlags.Static)!;
        FieldInfo kindField = overlay.GetField("activeOptionsPopupFeatureKind", BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo describe = overlay.GetMethod("DescribePopupTooltipClassification", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? previousLabel = labelField.GetValue(null);
        object? previousKind = kindField.GetValue(null);
        try
        {
            labelField.SetValue(null, parent.Label);
            kindField.SetValue(null, parent.FeatureKind);
            string classification = (string)describe.Invoke(null, new object[] { suboptionLabel })!;
            return classification.Length == 0 ? NoClassification : classification;
        }
        finally
        {
            labelField.SetValue(null, previousLabel);
            kindField.SetValue(null, previousKind);
        }
    }
}
