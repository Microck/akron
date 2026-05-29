using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Celeste;
using Celeste.Mod;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace Celeste.Mod.Akron;

[Tracked]
public sealed partial class AkronOverlay : Entity {
    private const float ScreenWidth = 1920f;
    private const float ScreenHeight = 1080f;

    private const float OverlayMargin = 4f;
    private const float PanelGap = 4f;
    private const float HeaderHeight = 24f;
    private const float BodyPadding = 4f;
    private const float RowHeight = 24f;
    private const float RowGap = 2f;
    private const float ColumnStackGap = 4f;
    private const int ActionColumnCount = 8;
    private const int CompactActionThreshold = 12;
    private const float TooltipDelaySeconds = 0.55f;
    private const float TooltipMaxWidth = 640f;
    private const float ImGuiTooltipWrapWidth = 620f;
    private const float PopupViewportMargin = 16f;
    private const float PopupLabelColumnMinWidth = 48f;
    private const float PopupLabelColumnPreferredWidth = 86f;
    private const float PopupStepperButtonWidth = 34f;
    private const int InputBoardUndoLimit = 32;
    private const float AkronTitleFontSize = 18f;
    private const float AkronRowFontSize = 18f;
    private const float AkronSmallFontSize = 16f;
    private const string OverlayToggleActionKey = "__akron_overlay_toggle";
    private const string ImGuiActionSearchInputId = "##akron_action_search_input";
    public enum OverlayCancelAction {
        KeepOverlayOpen,
        ClearSearch,
        CloseCommunityPackBrowser
    }

    private static Color AkronWindowBackground => AkronOverlayThemes.WindowColor();
    private static Color AkronFrameBackground => AkronOverlayThemes.FrameColor();
    private static Color AkronAccent => AkronOverlayThemes.HeaderColor();
    private static Color AkronAccentHovered => AkronOverlayThemes.HeaderHoverColor();
    private static Color AkronInactiveIndicator => AkronOverlayThemes.MutedColor();
    private static Color AkronDisabledText => AkronOverlayThemes.DisabledColor();

    private readonly HashSet<string> collapsedWindowTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> pendingImGuiCollapseSync = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<SectionLayout> lastSections = new List<SectionLayout>();
    private readonly List<ActionLayout> lastVisibleActionRows = new List<ActionLayout>();
    private readonly List<ActionEntry> currentEntries = new List<ActionEntry>();
    private readonly Dictionary<string, List<ActionEntry>> displayActionEntryCache = new Dictionary<string, List<ActionEntry>>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Rectangle> imguiOptionsPopupAnchorRects = new Dictionary<string, Rectangle>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NumericsVector2> imguiOptionsPopupSizes = new Dictionary<string, NumericsVector2>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NumericsVector2> imguiTooltipSizes = new Dictionary<string, NumericsVector2>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string Text, int Width), string> imguiTextTruncationCache = new Dictionary<(string Text, int Width), string>();
    private static readonly List<BindableAction> bindableActionCache = new List<BindableAction>();
    private static string activeOptionsPopupLabel = string.Empty;
    private static Level bindableActionCacheLevel;
    private static int menuBindingRevision;
    private static int bindableActionCacheRevision = -1;

    private int selectedTabIndex;
    private int selectedActionIndex;
    private int actionScrollIndex;
    private int hoveredActionIndex = -1;
    private int hoveredActionTabIndex = -1;
    private int displayActionEntryCacheMenuBindingRevision;
    private int lastMouseX = int.MinValue;
    private int lastMouseY = int.MinValue;
    private int lastScrollWheelValue = int.MinValue;
    private string hoveredHeaderTitle;
    private ActionLayout hoveredActionLayout;
    private string hoveredTooltipKey = string.Empty;
    private float hoveredTooltipSeconds;
    private string openOptionsLabel = string.Empty;
    private string bindingCaptureActionKey = string.Empty;
    private string bindingCaptureDisplayName = string.Empty;
    private ActionEntry pendingInternalRecorderExperimentalAction;
    private ActionEntry pendingImGuiOptionsPopupEntry;
    private ActionEntry pendingImGuiTooltipEntry;
    private Rectangle pendingImGuiTooltipAnchor;
    private bool bindingCaptureOverlayToggle;
    private bool bindingCaptureAutoDeafenHotkey;
    private bool bindingCaptureWaitingForRelease;
    private bool internalRecorderExperimentalWarningDontShowAgain;
    private bool imguiPopupBlockedRowsLastFrame;
    private bool suppressImGuiRowPressesThisFrame;
    private bool openedImGuiOptionsPopupThisFrame;
    private static bool communityPackBrowserOpen;
    private int selectedCommunityPackIndex;
    private Rectangle openOptionsPopupRect;
    private Rectangle openOptionsMinusRect;
    private Rectangle openOptionsPlusRect;
    private bool autoKillAreaSelectionActive;
    private bool autoKillAreaHasFirstCorner;
    private Vector2 autoKillAreaFirstCorner;
    private bool autoKillAreaLastLeftDown;
    private bool autoKillSelectionPreviousFreeze;
    private bool autoDeafenAreaSelectionActive;
    private bool autoDeafenAreaHasFirstCorner;
    private Vector2 autoDeafenAreaFirstCorner;
    private bool autoDeafenAreaLastLeftDown;
    private bool autoDeafenSelectionPreviousFreeze;
    private bool startPosPlacementActive;
    private bool startPosPlacementLastLeftDown;
    private bool startPosPlacementPreviousFreeze;
    private bool startPosPlacementPreviousFreeCamera;
    private bool startPosPlacementPreviousFreeCameraFreeze;
    private NumericsVector2 startPosPlacementPanelMin;
    private NumericsVector2 startPosPlacementPanelMax;
    private int selectedInputBoardElementIndex;
    private string inputBoardKeyBindingText = string.Empty;
    private string inputBoardKeyBindingElementId = string.Empty;
    private readonly List<List<AkronInputBoardElement>> inputBoardUndoStack = new List<List<AkronInputBoardElement>>();
    private bool inputBoardDragUndoCaptured;
    private bool inputBoardKeyboardMoveUndoCaptured;
    private string draggingLabelRowKey = string.Empty;
    private NumericsVector2 labelRowDragStart;
    private bool labelRowDragActive;
    private int valueEditFreezeFrames;
    private string searchQuery = string.Empty;
    private bool searchInputActive;
    private bool searchInputUsesImGui;
    private bool searchInputFocusRequested;
    private string searchInputFocusTargetId = string.Empty;
    private SelectionPanel selectedPanel = SelectionPanel.Actions;
    private Level displayActionEntryCacheLevel;
    private Level prewarmedLayoutLevel;
    private int prewarmedLayoutMenuBindingRevision = -1;
    private ulong layoutFrame = ulong.MaxValue;
    private KeyboardState previousSearchKeyboard;

    public bool SearchInputConsumedThisFrame { get; private set; }
    public bool SearchOwnsGameplayInputThisFrame { get; private set; }
    public bool IsStartPosPlacementActive => startPosPlacementActive;

    public bool SearchOwnsCurrentKeyboardFrame {
        get {
            if (autoKillAreaSelectionActive || autoDeafenAreaSelectionActive || startPosPlacementActive) {
                return true;
            }

            if (!Visible || AkronPromptMenu.IsOpen) {
                return false;
            }

            return (AkronImGuiRenderer.WantCaptureKeyboard || searchInputActive) &&
                   IsAnyKeyboardPressed();
        }
    }

    public AkronOverlay() {
        Tag = Tags.HUD | Tags.Global | Tags.PauseUpdate;
        Visible = false;
        Active = false;
        // Akron's overlay is runtime UI, not game state. Keeping it out of
        // native room snapshots prevents StartPos restores from cloning stale
        // UI internals back over the live overlay.
        Add(new AkronIgnoreSaveStateComponent(based: false));
        ExpandAllWindows();
        CollapseExternalToolWindowsByDefault();
    }

    public override void Update() {
        base.Update();

        if (AkronModule.Settings.StartPosMousePlacement && !startPosPlacementActive) {
            BeginStartPosPlacement();
        } else if (!AkronModule.Settings.StartPosMousePlacement && startPosPlacementActive) {
            EndStartPosPlacement(false);
        }

        if ((!Visible && !autoKillAreaSelectionActive && !autoDeafenAreaSelectionActive && !startPosPlacementActive) || AkronPromptMenu.IsOpen) {
            Active = false;
            SearchInputConsumedThisFrame = false;
            SearchOwnsGameplayInputThisFrame = false;
            return;
        }

        if (UpdateStartPosPlacement()) {
            return;
        }

        if (UpdateAutoKillAreaSelection()) {
            return;
        }

        if (UpdateAutoDeafenAreaSelection()) {
            return;
        }

        if (UpdateBindingCapture()) {
            return;
        }

        UpdateSearchQuery();
        if (valueEditFreezeFrames > 0) {
            valueEditFreezeFrames--;
            SearchOwnsGameplayInputThisFrame = true;
        }
        if (AkronImGuiRenderer.WantCaptureKeyboard && IsAnyKeyboardPressed()) {
            SearchOwnsGameplayInputThisFrame = true;
        }
        RebuildLayoutOncePerFrame(Scene as Level);
        UpdateFallbackScrollWheel();

        if (IsCancelPressed(searchInputActive || AkronImGuiRenderer.WantCaptureKeyboard)) {
            OverlayCancelAction cancelAction = ResolveCancelAction(searchInputActive, !string.IsNullOrEmpty(searchQuery), communityPackBrowserOpen);
            if (cancelAction == OverlayCancelAction.ClearSearch) {
                searchQuery = string.Empty;
                searchInputActive = false;
                searchInputUsesImGui = false;
                ClearSearchInputFocusRequest();
                selectedActionIndex = 0;
                actionScrollIndex = 0;
            } else if (cancelAction == OverlayCancelAction.CloseCommunityPackBrowser) {
                communityPackBrowserOpen = false;
            }
            return;
        }

        // Keyboard navigation caused regular gameplay/menu binds to affect Akron
        // rows while the overlay was open. Keep keyboard ownership scoped to text
        // entry, search, binding capture, and explicit close behavior.
    }

    public override void Render() {
    }

    public bool RenderImGui() {
        if (!ShouldRenderOverlaySurface(Visible, AkronPromptMenu.IsOpen, autoKillAreaSelectionActive, autoDeafenAreaSelectionActive)) {
            return false;
        }

        if (startPosPlacementActive) {
            return AkronImGuiRenderer.Render(DrawStartPosPlacementEditor);
        }

        if (!Visible) {
            return false;
        }

        RebuildLayoutOncePerFrame(Scene as Level);
        return AkronImGuiRenderer.Render(DrawImGuiMenu);
    }

    public void RenderSpriteBatchFallback() {
        if (!ShouldRenderOverlaySurface(Visible, AkronPromptMenu.IsOpen, autoKillAreaSelectionActive, autoDeafenAreaSelectionActive)) {
            return;
        }

        try {
            RebuildLayoutOncePerFrame(Scene as Level);
            UpdateMouseState();
            UpdateTooltipHover();
            foreach (SectionLayout section in lastSections) {
                RenderSection(section);
            }

            foreach (ActionLayout action in lastVisibleActionRows) {
                RenderActionEntry(action);
            }

            RenderActionScrollbar();
            RenderOptionsPopup();
            RenderTooltip();
        } catch (Exception exception) {
            Visible = false;
            Active = false;
            Logger.Log(LogLevel.Error, nameof(AkronOverlay), "Akron SpriteBatch overlay fallback failed; hiding overlay to avoid a render crash: " + exception);
        }
    }

    public bool MoveSelection(string direction) {
        if (!Visible) {
            return false;
        }

        RebuildLayout(Scene as Level);
        switch ((direction ?? string.Empty).Trim().ToLowerInvariant()) {
            case "left":
                SelectAdjacentPanel(-1);
                return true;
            case "right":
                SelectAdjacentPanel(1);
                return true;
            case "up":
                MoveVertical(-1);
                return true;
            case "down":
                MoveVertical(1);
                return true;
            default:
                return false;
        }
    }

    public bool ExecuteSelected() {
        if (!Visible) {
            return false;
        }

        RebuildLayout(Scene as Level);
        if (selectedPanel == SelectionPanel.Categories) {
            selectedActionIndex = 0;
            actionScrollIndex = 0;
            if (currentEntries.Count > 0) {
                selectedPanel = SelectionPanel.Actions;
            }
            return true;
        }

        return ExecuteCurrentAction();
    }

    public bool SelectAction(string label) {
        if (!Visible || string.IsNullOrWhiteSpace(label)) {
            return false;
        }

        Level level = Scene as Level;
        RebuildLayout(level);

        if (!string.IsNullOrWhiteSpace(searchQuery)) {
            int filteredIndex = currentEntries.FindIndex(entry => string.Equals(entry.Label, label, StringComparison.OrdinalIgnoreCase));
            if (filteredIndex >= 0) {
                selectedPanel = SelectionPanel.Actions;
                selectedActionIndex = filteredIndex;
                return true;
            }
        }

        if (TryFindAction(label, level, out int tabIndex, out int entryIndex)) {
            selectedTabIndex = tabIndex;
            selectedPanel = SelectionPanel.Actions;
            selectedActionIndex = entryIndex;
            actionScrollIndex = 0;
            RebuildLayout(level);
            return true;
        }

        return false;
    }

    public bool OpenActionOptions(string label) {
        if (!SelectAction(label) && !SelectActionOptionsPopup(label)) {
            return false;
        }

        if (currentEntries.Count == 0) {
            return false;
        }

        selectedActionIndex = Calc.Clamp(selectedActionIndex, 0, currentEntries.Count - 1);
        ActionEntry selectedEntry = currentEntries[selectedActionIndex];
        if (string.Equals(selectedEntry.Label, "Community Packs", StringComparison.OrdinalIgnoreCase)) {
            OpenCommunityPackBrowser();
            return true;
        }

        if (!selectedEntry.HasOptionsPopup) {
            return false;
        }

        SelectCustomHudLabel(selectedEntry.CustomHudLabelId);
        OpenOptionsPopup(selectedEntry.OptionsPopupKey);
        return true;
    }

    private bool SelectActionOptionsPopup(string popupKey) {
        if (!Visible || string.IsNullOrWhiteSpace(popupKey)) {
            return false;
        }

        Level level = Scene as Level;
        string[] visibleTabs = GetVisibleTabs();
        for (int tab = 0; tab < visibleTabs.Length; tab++) {
            List<ActionEntry> entries = GetDisplayActionEntries(visibleTabs[tab], level);
            int entry = entries.FindIndex(candidate => string.Equals(candidate.OptionsPopupKey, popupKey, StringComparison.OrdinalIgnoreCase));
            if (entry < 0) {
                continue;
            }

            selectedTabIndex = tab;
            selectedPanel = SelectionPanel.Actions;
            selectedActionIndex = entry;
            actionScrollIndex = 0;
            RebuildLayout(level);
            return true;
        }

        return false;
    }

    public bool EndStartPosPlacementForLoad() {
        if (!startPosPlacementActive) {
            return false;
        }

        EndStartPosPlacement(false);
        return true;
    }

    public bool ToggleCollapsedWindow(string title) {
        if (!Visible || string.IsNullOrWhiteSpace(title) || !GetToggleableSections().Contains(title, StringComparer.OrdinalIgnoreCase)) {
            return false;
        }

        ToggleWindowCollapse(title);
        return true;
    }

    public void ResetTransientUiState() {
        ResetTransientUiState(AkronModule.Settings.SearchAutofocus);
    }

    internal void ResetTransientUiState(bool searchAutofocus) {
        CloseOptionsPopup();
        CancelBindingCapture();
        imguiPopupBlockedRowsLastFrame = false;
        suppressImGuiRowPressesThisFrame = false;
        openedImGuiOptionsPopupThisFrame = false;
        autoKillAreaSelectionActive = false;
        autoKillAreaHasFirstCorner = false;
        autoDeafenAreaSelectionActive = false;
        autoDeafenAreaHasFirstCorner = false;
        valueEditFreezeFrames = 0;
        ClearSearchQuery();
        if (searchAutofocus) {
            RequestSearchInputFocus();
        } else {
            ClearSearchInputFocusRequest();
        }
        SearchInputConsumedThisFrame = false;
        SearchOwnsGameplayInputThisFrame = false;
    }

    public void OpenTab(string tabName) {
        int index = Array.FindIndex(GetVisibleTabs(), tab => string.Equals(tab, tabName, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) {
            selectedTabIndex = index;
            selectedActionIndex = 0;
            selectedPanel = SelectionPanel.Actions;
            actionScrollIndex = 0;
            searchQuery = string.Empty;
            searchInputActive = false;
            searchInputUsesImGui = false;
            ClearSearchInputFocusRequest();
        }
    }

    public string DescribeState() {
        RebuildLayout(Scene as Level);
        string selectedLabel = selectedPanel == SelectionPanel.Categories
            ? GetSelectedTabName()
            : currentEntries.Count == 0
                ? "none"
                : currentEntries[Calc.Clamp(selectedActionIndex, 0, currentEntries.Count - 1)].Label;
        string selectedWindow = selectedPanel == SelectionPanel.Categories
            ? "Categories"
            : GetSelectedTabName();
        string collapsed = collapsedWindowTitles.Count == 0 ? "none" : string.Join(", ", collapsedWindowTitles.OrderBy(title => title));

        return "visible=" + Visible.ToString().ToLowerInvariant() +
               ";search=" + (string.IsNullOrWhiteSpace(searchQuery) ? "<empty>" : searchQuery) +
               ";selected-window=" + selectedWindow +
               ";selected-label=" + selectedLabel +
               ";action-count=" + currentEntries.Count +
               ";visible-actions=" + lastVisibleActionRows.Count +
               ";scroll-index=" + actionScrollIndex +
               ";options=" + (string.IsNullOrWhiteSpace(openOptionsLabel) ? "none" : openOptionsLabel) +
               ";startpos-placement=" + startPosPlacementActive.ToString().ToLowerInvariant() +
               ";collapsed=" + collapsed +
               ";imgui=" + AkronImGuiRenderer.StatusSummary;
    }


    private List<RowSpec> BuildDetailsRows(Level level) {
        if (selectedPanel == SelectionPanel.Categories || currentEntries.Count == 0) {
            string activeTab = GetSelectedTabName();
            return new List<RowSpec> {
                new RowSpec("Focus", () => activeTab, RowKind.Info),
                new RowSpec("Matches", () => GetCategoryCount(activeTab, level).ToString(), RowKind.Info),
                new RowSpec("Binding", () => "Arrow keys choose section", RowKind.Info),
                new RowSpec("Confirm", () => "Moves focus to actions", RowKind.Info),
                new RowSpec("Search", () => string.IsNullOrWhiteSpace(searchQuery) ? "All actions in current tab" : "Filtering all tabs", RowKind.Info),
                new RowSpec("Hint", DescribeOverlayBindingCaveat, RowKind.Info)
            };
        }

        ActionEntry selectedEntry = currentEntries[Calc.Clamp(selectedActionIndex, 0, currentEntries.Count - 1)];
        return new List<RowSpec> {
            new RowSpec("Focus", () => selectedEntry.Label, RowKind.Info),
            new RowSpec("Tab", () => selectedEntry.Tab, RowKind.Info),
            new RowSpec("State", selectedEntry.Value, RowKind.Info),
            new RowSpec("Binding", () => DescribeBindingForAction(selectedEntry.Label), RowKind.Info),
            new RowSpec("Aliases", () => DescribeAliases(selectedEntry.Label), RowKind.Info),
            new RowSpec("Action", () => "Confirm or click executes", RowKind.Info)
        };
    }

    private List<RowSpec> BuildSelectionRows(Level level) {
        if (selectedPanel == SelectionPanel.Categories || currentEntries.Count == 0) {
            string activeTab = GetSelectedTabName();
            return new List<RowSpec> {
                new RowSpec("Tab", () => activeTab, RowKind.Info),
                new RowSpec("Actions", () => GetCategoryCount(activeTab, level) + " available", RowKind.Info),
                new RowSpec("Search", () => string.IsNullOrWhiteSpace(searchQuery) ? "Tab-local list" : "Filtering all tabs", RowKind.Info),
                new RowSpec("Move", () => "Left/right switches rail and list", RowKind.Info),
                new RowSpec("Confirm", () => currentEntries.Count > 0 ? "Enter moves into actions" : "No actions available", RowKind.Info),
                new RowSpec("Room", () => DescribeRoom(level), RowKind.Info)
            };
        }

        ActionEntry selectedEntry = currentEntries[Calc.Clamp(selectedActionIndex, 0, currentEntries.Count - 1)];
        return new List<RowSpec> {
            new RowSpec("Selected", () => selectedEntry.Label, RowKind.Info),
            new RowSpec("Current", selectedEntry.Value, RowKind.Info),
            new RowSpec("Scope", () => selectedEntry.Tab + " tab", RowKind.Info),
            new RowSpec("Invoke", () => "Enter, A, or left click", RowKind.Info),
            new RowSpec("Binding", () => DescribeBindingForAction(selectedEntry.Label), RowKind.Info),
            new RowSpec("Search", () => string.IsNullOrWhiteSpace(searchQuery) ? DescribeAliases(selectedEntry.Label) : "\"" + searchQuery + "\"", RowKind.Info)
        };
    }

    private List<RowSpec> BuildStatusRows(Level level) {
        return new List<RowSpec> {
            new RowSpec("Attempt", DescribeAttemptStatus, RowKind.Info, DescribeAttemptStatusColorRgb),
            new RowSpec("Profile", () => AkronModuleSettings.FormatProfile(AkronModule.Settings.ActiveProfile), RowKind.Info),
            new RowSpec("Ruleset", () => AkronModule.Settings.DescribeRulesetStack(), RowKind.Info),
            new RowSpec("Map", () => DescribeMap(level), RowKind.Info),
            new RowSpec("Room", () => DescribeRoom(level), RowKind.Info),
            new RowSpec("Slot", () => AkronModule.Settings.ActiveSavestateSlot.ToString(), RowKind.Info),
            new RowSpec("Capture", DescribeCapture, RowKind.Info)
        };
    }

    private List<ActionEntry> BuildCurrentEntries(Level level) {
        if (string.IsNullOrWhiteSpace(searchQuery)) {
            return GetDisplayActionEntries(GetSelectedTabName(), level);
        }

        List<ActionEntry> entries = new List<ActionEntry>();
        foreach (string tabName in GetVisibleTabs()) {
            entries.AddRange(GetDisplayActionEntries(tabName, level)
                .Where(entry => entry.Control != OverlayEntryControl.SearchInput && MatchesSearch(tabName, entry)));
        }

        return entries;
    }

    private int GetCategoryCount(string tabName, Level level) {
        if (string.IsNullOrWhiteSpace(searchQuery)) {
            return GetDisplayActionEntries(tabName, level).Count;
        }

        return GetDisplayActionEntries(tabName, level).Count(entry => entry.Control != OverlayEntryControl.SearchInput && MatchesSearch(tabName, entry));
    }

    private string GetSelectedTabName() {
        string[] visibleTabs = GetVisibleTabs();
        selectedTabIndex = Calc.Clamp(selectedTabIndex, 0, visibleTabs.Length - 1);
        return visibleTabs[selectedTabIndex];
    }

    private bool TryFindAction(string label, Level level, out int tabIndex, out int entryIndex) {
        string[] visibleTabs = GetVisibleTabs();
        for (int index = 0; index < visibleTabs.Length; index++) {
            string tabName = visibleTabs[index];
            List<ActionEntry> entries = GetDisplayActionEntries(tabName, level);
            int foundIndex = entries.FindIndex(entry => string.Equals(entry.Label, label, StringComparison.OrdinalIgnoreCase));
            if (foundIndex >= 0) {
                tabIndex = index;
                entryIndex = foundIndex;
                return true;
            }
        }

        tabIndex = -1;
        entryIndex = -1;
        return false;
    }

    private void UpdateMouseState() {
        hoveredHeaderTitle = null;
        hoveredActionIndex = -1;
        hoveredActionTabIndex = -1;
        hoveredActionLayout = null;
        Vector2 mouse = MInput.Mouse.Position;

        foreach (SectionLayout section in lastSections) {
            if (Contains(section.HeaderRect, mouse)) {
                hoveredHeaderTitle = section.Title;
                break;
            }
        }

        foreach (ActionLayout action in lastVisibleActionRows) {
            if (Contains(action.Rect, mouse)) {
                hoveredActionIndex = action.ActualIndex;
                hoveredActionTabIndex = action.TabIndex;
                hoveredActionLayout = action;
                break;
            }
        }
    }

    private void UpdateTooltipHover() {
        string nextKey = hoveredActionLayout == null
            ? string.Empty
            : hoveredActionLayout.Entry.Tab + "\n" + hoveredActionLayout.Entry.Label;

        if (string.IsNullOrWhiteSpace(nextKey) || !TryGetActionDescription(hoveredActionLayout.Entry.Label, out _)) {
            hoveredTooltipKey = string.Empty;
            hoveredTooltipSeconds = 0f;
            return;
        }

        if (!string.Equals(hoveredTooltipKey, nextKey, StringComparison.Ordinal)) {
            hoveredTooltipKey = nextKey;
            hoveredTooltipSeconds = 0f;
            return;
        }

        hoveredTooltipSeconds += Engine.RawDeltaTime;
    }

    private void ToggleWindowCollapse(string title) {
        if (!collapsedWindowTitles.Add(title)) {
            collapsedWindowTitles.Remove(title);
        }
        pendingImGuiCollapseSync.Add(title);
    }

    private void ExpandAllWindows() {
        collapsedWindowTitles.Clear();
        foreach (string title in GetToggleableSections()) {
            pendingImGuiCollapseSync.Add(title);
        }
    }

    private void CollapseExternalToolWindowsByDefault() {
        foreach (string title in GetVisibleTabs().Where(tab => ExternalToolTabs.Contains(tab))) {
            collapsedWindowTitles.Add(title);
            pendingImGuiCollapseSync.Add(title);
        }
    }

    private void SelectAdjacentPanel(int delta) {
        if (currentEntries.Count == 0) {
            selectedPanel = SelectionPanel.Categories;
            return;
        }

        selectedPanel = SelectionPanel.Actions;
        if (string.IsNullOrWhiteSpace(searchQuery)) {
            selectedTabIndex = (selectedTabIndex + delta + GetVisibleTabs().Length) % GetVisibleTabs().Length;
            selectedActionIndex = 0;
            actionScrollIndex = 0;
            RebuildLayout(Scene as Level);
        }
    }

    private void MoveVertical(int delta) {
        if (selectedPanel == SelectionPanel.Categories) {
            selectedTabIndex = (selectedTabIndex + delta + GetVisibleTabs().Length) % GetVisibleTabs().Length;
            selectedActionIndex = 0;
            actionScrollIndex = 0;
            return;
        }

        if (currentEntries.Count == 0) {
            selectedPanel = SelectionPanel.Categories;
            return;
        }

        selectedActionIndex = (selectedActionIndex + delta + currentEntries.Count) % currentEntries.Count;
    }

    private bool ExecuteCurrentAction() {
        if (currentEntries.Count == 0) {
            return false;
        }

        selectedActionIndex = Calc.Clamp(selectedActionIndex, 0, currentEntries.Count - 1);
        ActionEntry selectedEntry = currentEntries[selectedActionIndex];
        if (selectedEntry.Control == OverlayEntryControl.Keybind) {
            StartBindingCaptureForEntry(selectedEntry);
        } else if (selectedEntry.Control == OverlayEntryControl.KeybindReadOnly) {
            return false;
        } else if (selectedEntry.Control == OverlayEntryControl.SearchInput) {
            searchInputActive = true;
            searchInputUsesImGui = false;
            RequestSearchInputFocus();
            selectedPanel = SelectionPanel.Actions;
        } else {
            selectedEntry.Execute?.Invoke();
            if (selectedEntry.IsAddCustomLabelRow) {
                InvalidateDisplayActionEntryCache();
            }
        }
        return true;
    }

    private void UpdateFallbackScrollWheel() {
        MouseState mouse = Mouse.GetState();
        if (lastScrollWheelValue == int.MinValue) {
            lastScrollWheelValue = mouse.ScrollWheelValue;
            return;
        }

        int wheelDelta = mouse.ScrollWheelValue - lastScrollWheelValue;
        lastScrollWheelValue = mouse.ScrollWheelValue;
        if (wheelDelta == 0 || currentEntries.Count == 0) {
            return;
        }

        SectionLayout actionsSection = FindSelectedActionSection();
        if (actionsSection == null || actionsSection.Collapsed || !Contains(actionsSection.BodyRect, MInput.Mouse.Position)) {
            return;
        }

        int visibleCapacity = GetVisibleActionCapacity(actionsSection);
        int maxScroll = Math.Max(0, currentEntries.Count - visibleCapacity);
        if (maxScroll == 0) {
            actionScrollIndex = 0;
            return;
        }

        int wheelNotches = Math.Max(1, Math.Abs(wheelDelta) / 120);
        int rowDelta = wheelNotches * 3 * (wheelDelta < 0 ? 1 : -1);
        actionScrollIndex = Calc.Clamp(actionScrollIndex + rowDelta, 0, maxScroll);
    }

    private SectionLayout FindSelectedActionSection() {
        return lastSections.FirstOrDefault(section => string.Equals(section.Title, GetSelectedTabName(), StringComparison.OrdinalIgnoreCase));
    }

    private bool HasMouseMoved() {
        int mouseX = (int) MInput.Mouse.Position.X;
        int mouseY = (int) MInput.Mouse.Position.Y;
        bool moved = mouseX != lastMouseX || mouseY != lastMouseY;
        lastMouseX = mouseX;
        lastMouseY = mouseY;
        return moved;
    }

    private static bool IsCancelPressed(bool keyboardInputOwned) {
        if (MInput.Keyboard.Pressed(Keys.Escape)) {
            return true;
        }

        if (keyboardInputOwned) {
            return IsGamePadPressed(Buttons.B) ||
                   IsGamePadPressed(Buttons.Back);
        }

        return Input.MenuCancel.Pressed ||
               Input.Pause.Pressed ||
               IsGamePadPressed(Buttons.B) ||
               IsGamePadPressed(Buttons.Back);
    }

    internal static OverlayCancelAction ResolveCancelAction(bool searchInputActive, bool hasSearchQuery, bool communityPackBrowserOpen) {
        if (searchInputActive || hasSearchQuery) {
            return OverlayCancelAction.ClearSearch;
        }

        if (communityPackBrowserOpen) {
            return OverlayCancelAction.CloseCommunityPackBrowser;
        }

        return OverlayCancelAction.KeepOverlayOpen;
    }

    internal static bool ShouldRenderOverlaySurface(bool visible, bool promptMenuOpen, bool autoKillAreaSelectionActive, bool autoDeafenAreaSelectionActive) {
        return visible &&
               !promptMenuOpen &&
               !autoKillAreaSelectionActive &&
               !autoDeafenAreaSelectionActive;
    }

    private static bool IsConfirmPressed() {
        return Input.MenuConfirm.Pressed ||
               MInput.Keyboard.Pressed(Keys.Enter) ||
               IsGamePadPressed(Buttons.A);
    }

    private static bool IsNavigateLeftPressed() {
        return Input.MenuLeft.Pressed ||
               MInput.Keyboard.Pressed(Keys.Left) ||
               IsGamePadPressed(Buttons.DPadLeft) ||
               IsGamePadPressed(Buttons.LeftShoulder);
    }

    private static bool IsNavigateRightPressed() {
        return Input.MenuRight.Pressed ||
               MInput.Keyboard.Pressed(Keys.Right) ||
               IsGamePadPressed(Buttons.DPadRight) ||
               IsGamePadPressed(Buttons.RightShoulder);
    }

    private static bool IsNavigateUpPressed() {
        return Input.MenuUp.Pressed ||
               MInput.Keyboard.Pressed(Keys.Up) ||
               IsGamePadPressed(Buttons.DPadUp);
    }

    private static bool IsNavigateDownPressed() {
        return Input.MenuDown.Pressed ||
               MInput.Keyboard.Pressed(Keys.Down) ||
               IsGamePadPressed(Buttons.DPadDown);
    }

    private static bool IsGamePadPressed(Buttons button) {
        return Input.Gamepad >= 0 &&
               Input.Gamepad < MInput.GamePads.Length &&
               MInput.GamePads[Input.Gamepad].Pressed(button);
    }

    private static bool IsAnyGamePadButtonDown() {
        if (Input.Gamepad < 0 || Input.Gamepad >= MInput.GamePads.Length) {
            return false;
        }

        foreach (Buttons button in Enum.GetValues(typeof(Buttons))) {
            if (IsBindableButton(button) &&
                MInput.GamePads[Input.Gamepad].CurrentState.IsButtonDown(button)) {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetPressedGamePadButton(out Buttons pressedButton) {
        pressedButton = 0;
        if (Input.Gamepad < 0 || Input.Gamepad >= MInput.GamePads.Length) {
            return false;
        }

        foreach (Buttons button in Enum.GetValues(typeof(Buttons))) {
            if (IsBindableButton(button) && MInput.GamePads[Input.Gamepad].Pressed(button)) {
                pressedButton = button;
                return true;
            }
        }

        return false;
    }

    private static bool IsOnState(string value) {
        return string.Equals(GetStateToken(value), "On", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOffState(string value) {
        return string.Equals(GetStateToken(value), "Off", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStateToken(string value) {
        string token = (value ?? string.Empty).Trim();
        int separatorIndex = token.IndexOf('|');
        if (separatorIndex >= 0) {
            token = token[..separatorIndex].Trim();
        }

        return token;
    }

    private static Rectangle Rect(float x, float y, float width, float height) {
        return new Rectangle((int) x, (int) y, (int) width, (int) height);
    }

    private static Rectangle RectCeiling(float x, float y, float width, float height) {
        return new Rectangle(
            (int) Math.Floor(x),
            (int) Math.Floor(y),
            (int) Math.Ceiling(width),
            (int) Math.Ceiling(height));
    }

    private static float CalculateCompactSectionHeight(int rowCount, float minHeight, float maxHeight) {
        float rowsHeight = BodyPadding * 2f +
                           rowCount * RowHeight +
                           Math.Max(0, rowCount - 1) * RowGap;
        float desiredHeight = HeaderHeight + rowsHeight + 16f;
        return Calc.Clamp(desiredHeight, minHeight, maxHeight);
    }

    private static bool Contains(Rectangle rect, Vector2 point) {
        return rect.Contains((int) point.X, (int) point.Y);
    }

    private static List<string> WrapTooltipText(string text, float maxWidth, float pixelSize) {
        List<string> lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) {
            return lines;
        }

        foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n')) {
            string[] words = paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string line = string.Empty;
            foreach (string word in words) {
                string candidate = string.IsNullOrWhiteSpace(line) ? word : line + " " + word;
                if (MeasureMenuText(candidate, pixelSize).X <= maxWidth) {
                    line = candidate;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(line)) {
                    lines.Add(line);
                }
                line = word;
            }

            if (!string.IsNullOrWhiteSpace(line)) {
                lines.Add(line);
            }
        }

        return lines;
    }

    private static string TruncateToWidth(string text, float maxWidth, float pixelSize) {
        if (string.IsNullOrWhiteSpace(text)) {
            return string.Empty;
        }

        if (MeasureMenuText(text, pixelSize).X <= maxWidth) {
            return text;
        }

        string trimmed = text;
        while (trimmed.Length > 3 && MeasureMenuText(trimmed + "...", pixelSize).X > maxWidth) {
            trimmed = trimmed[..^1];
        }

        return trimmed + "...";
    }

    private static Vector2 MeasureMenuText(string text, float pixelSize) {
        return AkronBitmapFont.Available
            ? AkronBitmapFont.Measure(text, pixelSize)
            : ActiveFont.Measure(text) * (pixelSize / 72f);
    }

    private static void DrawMenuText(string text, Vector2 position, float pixelSize, Color color) {
        if (AkronBitmapFont.Available) {
            AkronBitmapFont.Draw(text, position, pixelSize, color);
            return;
        }

        ActiveFont.Draw(text, position, Vector2.Zero, Vector2.One * (pixelSize / 72f), color);
    }
}
