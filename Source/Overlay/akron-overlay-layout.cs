using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void RebuildLayoutOncePerFrame(Level level) {
        if (layoutFrame == Engine.FrameCounter) {
            return;
        }

        layoutFrame = Engine.FrameCounter;
        RebuildLayout(level);
    }

    public void PrewarmLayout(Level level) {
        AkronImGuiRenderer.WarmUp();

        if (ReferenceEquals(prewarmedLayoutLevel, level) &&
            prewarmedLayoutMenuBindingRevision == menuBindingRevision &&
            displayActionEntryCache.Count >= GetVisibleTabs().Length) {
            return;
        }

        // The first visible frame should only draw the menu, not discover every
        // row and options popup target at the same time. Building the same cache
        // while hidden moves that one-time cost away from the toggle key press.
        foreach (string tabName in GetVisibleTabs()) {
            GetDisplayActionEntries(tabName, level);
        }
        RebuildLayout(level);
        prewarmedLayoutLevel = level;
        prewarmedLayoutMenuBindingRevision = menuBindingRevision;
    }

    private void RebuildLayout(Level level) {
        lastSections.Clear();
        lastVisibleActionRows.Clear();
        currentEntries.Clear();

        selectedTabIndex = Calc.Clamp(selectedTabIndex, 0, GetVisibleTabs().Length - 1);
        currentEntries.AddRange(BuildCurrentEntries(level));

        if (currentEntries.Count == 0) {
            selectedActionIndex = 0;
            actionScrollIndex = 0;
            if (selectedPanel == SelectionPanel.Actions) {
                selectedPanel = SelectionPanel.Categories;
            }
        } else {
            selectedActionIndex = Calc.Clamp(selectedActionIndex, 0, currentEntries.Count - 1);
        }

        // OpenHack/MegaHack present features as independent compact windows laid
        // out in columns. Akron follows that structure directly here: no
        // category sidebar, no large dashboard panel, and no empty action well.
        const float menuLeft = 4f;
        const float menuTop = 4f;
        const float menuColumnWidth = 208f;
        const float menuColumnGap = PanelGap;

        List<float> columnXPositions = CalculateActionColumnXPositions(menuLeft, menuColumnWidth, menuColumnGap, ScreenWidth);
        if (columnXPositions.Count == 0) {
            return;
        }

        string[] visibleTabs = GetVisibleTabs();
        ExternalToolPlacementPlan externalPlacementPlan = BuildExternalToolPlacementPlan(visibleTabs, level, columnXPositions.Count, menuTop, ScreenHeight);
        List<float> columnBottoms = columnXPositions.Select(_ => menuTop).ToList();
        List<int> columnSectionCounts = columnXPositions.Select(_ => 0).ToList();
        for (int tabIndex = 0; tabIndex < visibleTabs.Length; tabIndex++) {
            string tabName = visibleTabs[tabIndex];
            List<ActionEntry> tabEntries = GetFilteredDisplayActionEntries(tabName, level);
            if (!string.IsNullOrWhiteSpace(searchQuery) && tabEntries.Count == 0) {
                continue;
            }

            int columnIndex = GetPlannedActionColumnIndex(tabName, columnXPositions.Count, columnBottoms, columnSectionCounts, externalPlacementPlan, ScreenHeight);
            float x = columnXPositions[columnIndex];
            float y = columnBottoms[columnIndex];
            float sectionScreenHeight = GetSectionScreenHeight(tabName, columnIndex, externalPlacementPlan, ScreenHeight);
            float height = CalculateActionSectionHeight(tabEntries.Count, y, sectionScreenHeight);
            SectionLayout actionSection = CreateSectionLayout(tabName, x, y, menuColumnWidth, height, new List<RowSpec>());
            lastSections.Add(actionSection);
            BuildActionLayouts(actionSection, tabEntries, tabIndex, useScrolling: ShouldUseFallbackScrolling(actionSection, tabEntries.Count));

            columnBottoms[columnIndex] = y + GetStackedActionSectionHeight(tabName, height) + ColumnStackGap;
            columnSectionCounts[columnIndex]++;
        }
    }

    private List<ActionEntry> GetDisplayActionEntries(string tabName, Level level) {
        if (!ReferenceEquals(displayActionEntryCacheLevel, level)) {
            displayActionEntryCacheLevel = level;
            displayActionEntryCache.Clear();
        }
        if (displayActionEntryCacheMenuBindingRevision != menuBindingRevision) {
            displayActionEntryCacheMenuBindingRevision = menuBindingRevision;
            displayActionEntryCache.Clear();
        }

        if (displayActionEntryCache.TryGetValue(tabName, out List<ActionEntry> entries)) {
            return entries;
        }

        List<OverlayEntry> displayEntries = string.Equals(tabName, "Sound", StringComparison.OrdinalIgnoreCase)
            ? BuildSoundDisplayEntries()
            : BuildDisplayEntriesForTab(tabName, level);
        entries = displayEntries
            .Select(entry => new ActionEntry(tabName, entry))
            .ToList();
        displayActionEntryCache[tabName] = entries;
        return entries;
    }

    private List<ActionEntry> GetFilteredDisplayActionEntries(string tabName, Level level) {
        List<ActionEntry> entries = GetDisplayActionEntries(tabName, level);
        if (string.IsNullOrWhiteSpace(searchQuery)) {
            return entries;
        }

        if (string.Equals(tabName, "Sound", StringComparison.OrdinalIgnoreCase)) {
            return BuildFilteredSoundActionEntries(tabName);
        }

        return entries
            .Where(entry => entry.Control == OverlayEntryControl.SearchInput || MatchesSearch(tabName, entry))
            .ToList();
    }

    private List<ActionEntry> BuildFilteredSoundActionEntries(string tabName) {
        List<ActionEntry> filtered = new List<ActionEntry>();
        foreach (OverlayEntry entry in BuildSoundTopLevelEntries()) {
            ActionEntry action = new ActionEntry(tabName, entry);
            if (MatchesSearch(tabName, action)) {
                filtered.Add(action);
            }
        }

        foreach (SoundGroupSpec group in SoundGroups) {
            ActionEntry header = new ActionEntry(tabName, SoundGroupHeader(group, () => ToggleSoundGroup(group.Label)));
            List<ActionEntry> children = BuildSoundVolumeEntries(group.SoundLabels, group.Label)
                .Select(entry => new ActionEntry(tabName, entry))
                .ToList();
            if (MatchesSearch(tabName, header)) {
                filtered.Add(header);
                filtered.AddRange(children);
                continue;
            }

            List<ActionEntry> matchingChildren = children
                .Where(entry => MatchesSearch(tabName, entry))
                .ToList();
            if (matchingChildren.Count == 0) {
                continue;
            }

            filtered.Add(header);
            filtered.AddRange(matchingChildren);
        }

        return filtered;
    }

    private static List<float> CalculateActionColumnXPositions(float firstX, float width, float gap, float screenWidth) {
        List<float> positions = new List<float>();
        for (float x = firstX; positions.Count < ActionColumnCount && x + width <= screenWidth - OverlayMargin; x += width + gap) {
            positions.Add(x);
        }

        return positions;
    }

    private static int GetActionColumnIndex(string tabName, int availableColumns, IReadOnlyList<float> columnBottoms = null, IReadOnlyList<int> columnSectionCounts = null, float screenHeight = float.PositiveInfinity) {
        if (availableColumns <= 1) {
            return 0;
        }

        if (IsExternalToolTab(tabName)) {
            return GetExternalToolColumnIndex(tabName, availableColumns, columnBottoms, columnSectionCounts);
        }

        int preferredColumn = tabName switch {
            "Global" => 0,
            "Level" => 1,
            "StartPos" => 0,
            "Backups" => 0,
            "Bypass" or "Keybinds" => 2,
            "Player" => 3,
            "Sound" => 4,
            "Creator" or "Interface" => 5,
            "Labels" => 6,
            "Shortcuts" or "Internal Recorder" => 7,
            _ => 0
        };

        if (availableColumns >= ActionColumnCount) {
            return preferredColumn;
        }

        // The 1280px game window cannot fit every preferred overlay column.
        // Scale the preferred slot into the columns that actually fit so late
        // panels such as Internal Recorder do not get buried under every other
        // high-index panel in one unusable stack.
        float scaledColumn = preferredColumn * (availableColumns - 1f) / (ActionColumnCount - 1f);
        return Calc.Clamp((int) Math.Round(scaledColumn), 0, availableColumns - 1);
    }

    private static int GetExternalToolColumnIndex(string tabName, int availableColumns, IReadOnlyList<float> columnBottoms, IReadOnlyList<int> columnSectionCounts) {
        if (columnBottoms == null || columnBottoms.Count == 0) {
            return Calc.Clamp(GetActionColumnIndex("Level", availableColumns), 0, availableColumns - 1);
        }

        int cappedColumns = Math.Min(availableColumns, columnBottoms.Count);
        if (string.Equals(tabName, "Extended Variant Mode", StringComparison.OrdinalIgnoreCase)) {
            return FindShortestColumn(columnBottoms, cappedColumns, index => true);
        }

        if (columnSectionCounts != null && columnSectionCounts.Count > 0) {
            int cappedCountColumns = Math.Min(cappedColumns, columnSectionCounts.Count);
            int singleSectionColumn = FindShortestColumn(columnBottoms, cappedCountColumns, index => columnSectionCounts[index] == 1);
            if (singleSectionColumn >= 0) {
                return singleSectionColumn;
            }
        }

        return FindShortestColumn(columnBottoms, cappedColumns, index => true);
    }

    private ExternalToolPlacementPlan BuildExternalToolPlacementPlan(string[] visibleTabs, Level level, int availableColumns, float menuTop, float screenHeight) {
        ExternalToolPlacementPlan plan = new ExternalToolPlacementPlan(availableColumns);
        if (!visibleTabs.Any(IsExternalToolTab)) {
            return plan;
        }

        List<float> columnBottoms = Enumerable.Repeat(menuTop, availableColumns).ToList();
        List<int> columnSectionCounts = Enumerable.Repeat(0, availableColumns).ToList();

        foreach (string tabName in visibleTabs.Where(tab => !IsExternalToolTab(tab))) {
            List<ActionEntry> entries = GetFilteredDisplayActionEntries(tabName, level);
            if (!string.IsNullOrWhiteSpace(searchQuery) && entries.Count == 0) {
                continue;
            }

            int columnIndex = GetActionColumnIndex(tabName, availableColumns);
            float height = CalculateActionSectionHeight(entries.Count, columnBottoms[columnIndex], screenHeight);
            columnBottoms[columnIndex] += GetStackedActionSectionHeight(tabName, height) + ColumnStackGap;
            columnSectionCounts[columnIndex]++;
        }

        foreach (string tabName in visibleTabs.Where(IsExternalToolTab)) {
            List<ActionEntry> entries = GetFilteredDisplayActionEntries(tabName, level);
            if (!string.IsNullOrWhiteSpace(searchQuery) && entries.Count == 0) {
                continue;
            }

            int columnIndex = GetActionColumnIndex(tabName, availableColumns, columnBottoms, columnSectionCounts, screenHeight);
            float height = CalculateActionSectionHeight(entries.Count, columnBottoms[columnIndex], screenHeight);
            float reservedHeight = GetStackedActionSectionHeight(tabName, height) + ColumnStackGap;
            plan.SetColumn(tabName, columnIndex);
            plan.ReservedHeightByColumn[columnIndex] += reservedHeight;
            columnBottoms[columnIndex] += reservedHeight;
            columnSectionCounts[columnIndex]++;
        }

        return plan;
    }

    private int GetPlannedActionColumnIndex(string tabName, int availableColumns, IReadOnlyList<float> columnBottoms, IReadOnlyList<int> columnSectionCounts, ExternalToolPlacementPlan externalPlacementPlan, float screenHeight) {
        if (externalPlacementPlan.TryGetColumn(tabName, out int plannedColumn)) {
            return Calc.Clamp(plannedColumn, 0, availableColumns - 1);
        }

        return GetActionColumnIndex(tabName, availableColumns, columnBottoms, columnSectionCounts, screenHeight);
    }

    private float GetSectionScreenHeight(string tabName, int columnIndex, ExternalToolPlacementPlan externalPlacementPlan, float screenHeight) {
        if (IsExternalToolTab(tabName)) {
            return screenHeight;
        }

        float reservedHeight = externalPlacementPlan.GetReservedHeight(columnIndex);
        if (reservedHeight <= 0f) {
            return screenHeight;
        }

        float minimumSectionScreenHeight = OverlayMargin + HeaderHeight + BodyPadding * 2f + CurrentRowHeight();
        return Math.Max(minimumSectionScreenHeight, screenHeight - reservedHeight);
    }

    private float GetStackedActionSectionHeight(string tabName, float expandedHeight) {
        return collapsedWindowTitles.Contains(tabName) ? HeaderHeight : expandedHeight;
    }

    private sealed class ExternalToolPlacementPlan {
        private readonly Dictionary<string, int> columnByTab = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly List<float> ReservedHeightByColumn;

        public ExternalToolPlacementPlan(int columnCount) {
            ReservedHeightByColumn = Enumerable.Repeat(0f, columnCount).ToList();
        }

        public void SetColumn(string tabName, int columnIndex) {
            columnByTab[tabName] = columnIndex;
        }

        public bool TryGetColumn(string tabName, out int columnIndex) {
            return columnByTab.TryGetValue(tabName, out columnIndex);
        }

        public float GetReservedHeight(int columnIndex) {
            return columnIndex >= 0 && columnIndex < ReservedHeightByColumn.Count ? ReservedHeightByColumn[columnIndex] : 0f;
        }
    }

    private static int FindShortestColumn(IReadOnlyList<float> columnBottoms, int availableColumns, Func<int, bool> predicate) {
        int selectedColumn = -1;
        float selectedBottom = float.MaxValue;
        int cappedColumns = Math.Min(availableColumns, columnBottoms.Count);
        for (int index = 0; index < cappedColumns; index++) {
            if (!predicate(index)) {
                continue;
            }

            float bottom = columnBottoms[index];
            if (selectedColumn < 0 || bottom < selectedBottom) {
                selectedColumn = index;
                selectedBottom = bottom;
            }
        }

        return selectedColumn;
    }

    private float CalculateActionSectionHeight(int rowCount, float y, float screenHeight) {
        int desiredRows = Math.Max(1, rowCount);
        int visibleRows = Math.Min(CalculateVisibleActionRows(y, screenHeight), desiredRows);
        float rowHeight = CurrentRowHeight();
        float rowGap = CurrentRowGap();
        return HeaderHeight + BodyPadding * 2f + visibleRows * rowHeight + Math.Max(0, visibleRows - 1) * rowGap;
    }

    private int CalculateVisibleActionRows(float y, float screenHeight) {
        float rowHeight = CurrentRowHeight();
        float rowGap = CurrentRowGap();
        float availableBodyHeight = screenHeight - y - OverlayMargin - HeaderHeight - BodyPadding * 2f;
        return Math.Max(1, (int) Math.Floor((availableBodyHeight + rowGap) / (rowHeight + rowGap)));
    }

    private float CalculateInfoSectionHeight(int rowCount) {
        float rowHeight = CurrentRowHeight();
        float rowGap = CurrentRowGap();
        return HeaderHeight + BodyPadding * 2f + rowCount * rowHeight + Math.Max(0, rowCount - 1) * rowGap;
    }

    private SectionLayout CreateSectionLayout(string title, float x, float y, float width, float height, List<RowSpec> rows) {
        bool collapsed = collapsedWindowTitles.Contains(title);
        Rectangle headerRect = Rect(x, y, width, HeaderHeight);
        float bodyHeight = collapsed ? 0f : Math.Max(0f, height - HeaderHeight);
        Rectangle bodyRect = Rect(x, y + HeaderHeight, width, bodyHeight);
        Rectangle bounds = Rect(x, y, width, HeaderHeight + bodyHeight);
        SectionLayout section = new SectionLayout(title, bounds, headerRect, bodyRect, collapsed);

        if (collapsed) {
            return section;
        }

        float rowHeight = CurrentRowHeight();
        float rowGap = CurrentRowGap();
        float rowY = bodyRect.Y + BodyPadding;
        foreach (RowSpec row in rows) {
            Rectangle rowRect = Rect(bodyRect.X + BodyPadding, rowY, bodyRect.Width - BodyPadding * 2f, rowHeight);
            section.Rows.Add(new InfoRowLayout(row, rowRect));
            rowY += rowHeight + rowGap;
        }

        return section;
    }

    private void BuildActionLayouts(SectionLayout section, List<ActionEntry> entries, int tabIndex, bool useScrolling) {
        if (section.Collapsed || entries.Count == 0) {
            return;
        }

        int visibleRowCapacity = GetVisibleActionCapacity(section);
        if (useScrolling) {
            actionScrollIndex = Calc.Clamp(actionScrollIndex, 0, Math.Max(0, entries.Count - visibleRowCapacity));
        }

        int startIndex = useScrolling ? actionScrollIndex : 0;
        int endIndex = Math.Min(entries.Count, startIndex + visibleRowCapacity);
        float rowHeight = CurrentRowHeight();
        float rowGap = CurrentRowGap();
        float rowY = section.BodyRect.Y + BodyPadding;

        for (int entryIndex = startIndex; entryIndex < endIndex; entryIndex++) {
            Rectangle rowRect = Rect(section.BodyRect.X + BodyPadding, rowY, section.BodyRect.Width - BodyPadding * 2f, rowHeight);
            lastVisibleActionRows.Add(new ActionLayout(entries[entryIndex], entryIndex, tabIndex, rowRect));
            rowY += rowHeight + rowGap;
        }
    }

    private int GetVisibleActionCapacity(SectionLayout section) {
        float rowHeight = CurrentRowHeight();
        float rowGap = CurrentRowGap();
        return Math.Max(1, (int) ((section.BodyRect.Height - BodyPadding * 2f + rowGap) / (rowHeight + rowGap)));
    }

    private bool ShouldUseFallbackScrolling(SectionLayout section, int entryCount) {
        return entryCount > GetVisibleActionCapacity(section);
    }

    private static float CurrentRowHeight() => RowHeight;

    private static float CurrentRowGap() => RowGap;
}
