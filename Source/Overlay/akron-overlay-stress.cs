using System;
using System.Collections.Generic;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
#if DEBUG
    internal void StressMutateUi(Random rng, int iteration) {
        if (rng == null) {
            return;
        }

        // Stress mode fuzzes Akron-owned UI state only. It deliberately avoids
        // invoking action delegates because those can mutate gameplay or saves.
        if (iteration % 30 == 0) {
            ToggleRandomWindow(rng);
        }

        if (iteration % 45 == 0) {
            SelectRandomInspectorTarget(rng);
        }

        if (iteration % 60 == 0) {
            ScrollOrChangeFilter(rng);
        }
    }

    private void ToggleRandomWindow(Random rng) {
        string[] titles = GetToggleableSections();
        if (titles.Length == 0) {
            return;
        }

        ToggleWindowCollapse(titles[rng.Next(titles.Length)]);
    }

    private void SelectRandomInspectorTarget(Random rng) {
        Level level = Scene as Level;
        string[] tabs = GetVisibleTabs();
        if (tabs.Length == 0) {
            return;
        }

        selectedTabIndex = rng.Next(tabs.Length);
        List<ActionEntry> entries = GetFilteredDisplayActionEntries(tabs[selectedTabIndex], level);
        selectedPanel = entries.Count == 0 ? SelectionPanel.Categories : SelectionPanel.Actions;
        selectedActionIndex = entries.Count == 0 ? 0 : rng.Next(entries.Count);
        actionScrollIndex = selectedActionIndex;
    }

    private void ScrollOrChangeFilter(Random rng) {
        string[] filters = { string.Empty, "level", "player", "global", "record", "start" };
        searchQuery = filters[rng.Next(filters.Length)];
        searchInputActive = searchQuery.Length > 0;
        searchInputUsesImGui = searchInputActive;
        actionScrollIndex = rng.Next(0, Math.Max(1, currentEntries.Count + 1));
        displayActionEntryCache.Clear();
    }
#endif
}
