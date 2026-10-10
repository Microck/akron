using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

// Menu Mouse: while the Menu Mouse hold bind is held (Left Alt by default), the cursor
// drives Celeste's own menus. Hovering selects, left click activates, right click goes
// back and the wheel scrolls.
//
// The pointer only chooses *what* is selected. Every action is delivered by marking the
// matching menu VirtualButton (MenuConfirm, MenuCancel, MenuLeft, ...) as pressed for one
// frame, so the menu runs its own confirm/back/step code with its own sounds, guards and
// mod hooks. Akron never re-implements what Confirm does on a file slot or a chapter tab.
//
// Hit-testing runs right after MInput.Update, before the scene updates, against the layout
// each menu drew on the previous frame. Supported surfaces: every focused TextMenu (pause
// menu, options, mod options, Akron prompts), the main menu, file select, chapter select,
// the chapter panel and the journal.
internal static class AkronMenuMouse {
    private const float FileSlotHalfHeight = 150f;
    private const float FileSlotButtonSpacing = 15f;
    private const float ChapterIconIdleSize = 100f;
    private const float ChapterIconHoverGrowth = 44f;

    private static readonly List<VirtualButton> syntheticPresses = new List<VirtualButton>();
    private static readonly Dictionary<Type, bool> adjustableItemTypes = new Dictionary<Type, bool>();
    // Reused every frame the hold is active, so hit-testing does not allocate.
    private static readonly List<MenuRow> rowBuffer = new List<MenuRow>();

    private static bool wasActive;
    private static Vector2 lastPointer;
    // The TextMenu the pointer drove this frame; see HoldsScroll.
    private static TextMenu pointerMenu;

    // Chapter select and the chapter panel only move one step per Left/Right press, and
    // chapter select also has a short input delay. A click on a far icon or tab is walked
    // there one native step at a time.
    private static Oui stepOwner;
    private static int stepTarget;
    private static int stepInjectedFrom = -1;

    // True while Menu Mouse owns the cursor this frame. Read by the module's cursor
    // visibility logic.
    internal static bool ShowsCursor { get; private set; }

    internal enum ItemZone {
        Body,
        Decrease,
        Increase
    }

    // One TextMenu item, reduced to what hit-testing needs.
    internal readonly struct MenuRow {
        public readonly bool Visible;
        public readonly float Height;
        public readonly bool Hoverable;

        public MenuRow(bool visible, float height, bool hoverable) {
            Visible = visible;
            Height = height;
            Hoverable = hoverable;
        }
    }

    private readonly struct PointerInput {
        public readonly Vector2 Position;
        public readonly bool Moved;
        public readonly bool Click;
        public readonly bool Back;
        // -1 scrolls toward the top of a list, 1 toward the bottom, 0 no scroll.
        public readonly int Scroll;

        public PointerInput(Vector2 position, bool moved, bool click, bool back, int scroll) {
            Position = position;
            Moved = moved;
            Click = click;
            Back = back;
            Scroll = scroll;
        }

        public bool Hovers => Moved || Click;
    }

    public static void Load() {
        On.Monocle.MInput.Update += MInputOnUpdate;
    }

    public static void Unload() {
        On.Monocle.MInput.Update -= MInputOnUpdate;
        ReleaseSyntheticPresses();
        Deactivate();
    }

    private static void MInputOnUpdate(On.Monocle.MInput.orig_Update orig) {
        ReleaseSyntheticPresses();
        orig();
        Update(Engine.Scene);
    }

    private static void Update(Scene scene) {
        if (scene == null || !AkronModule.IsMenuMouseHoldActive()) {
            Deactivate();
            return;
        }

        Vector2 pointer = MInput.Mouse.Position;
        // Hover only follows real pointer movement (or the first held frame), so a
        // stationary cursor never fights the wheel or the keyboard for the selection.
        PointerInput input = new PointerInput(
            pointer,
            !wasActive || pointer != lastPointer,
            MInput.Mouse.PressedLeftButton,
            MInput.Mouse.PressedRightButton,
            -Math.Sign(MInput.Mouse.WheelDelta));
        lastPointer = pointer;

        ShowsCursor = UpdateSurface(scene, input);
        wasActive = ShowsCursor;
        if (!ShowsCursor) {
            ClearStep();
        }
    }

    // Returns true when the scene shows a menu Menu Mouse can drive.
    private static bool UpdateSurface(Scene scene, in PointerInput input) {
        pointerMenu = null;
        TextMenu menu = FindTopFocusedMenu(scene, out bool anyVisibleMenu);
        if (menu != null) {
            ClearStep();
            UpdateTextMenu(menu, input);
            return true;
        }

        if (anyVisibleMenu) {
            // A visible but unfocused TextMenu means an Everest submenu (or similar child)
            // holds focus and reads input itself. Only back is safe to forward to it.
            ClearStep();
            PressIf(input.Back, Input.MenuCancel);
            return true;
        }

        if (scene is not Overworld overworld || overworld.Current is not { Focused: true } oui) {
            return false;
        }

        if (stepOwner != null && stepOwner != oui) {
            ClearStep();
        }

        switch (oui) {
            case OuiMainMenu mainMenu:
                UpdateMainMenu(mainMenu, input);
                return true;
            case OuiFileSelect fileSelect:
                UpdateFileSelect(fileSelect, input);
                return true;
            case OuiChapterSelect chapterSelect:
                UpdateChapterSelect(chapterSelect, input);
                return true;
            case OuiChapterPanel chapterPanel:
                UpdateChapterPanel(chapterPanel, input);
                return true;
            case OuiJournal journal:
                UpdateJournal(journal, input);
                return true;
            default:
                return false;
        }
    }

    private static TextMenu FindTopFocusedMenu(Scene scene, out bool anyVisibleMenu) {
        anyVisibleMenu = false;
        TextMenu top = null;
        foreach (Entity entity in scene.Entities) {
            if (entity is not TextMenu { Visible: true } menu) {
                continue;
            }

            anyVisibleMenu = true;
            // The last focused menu in the entity list is the one drawn on top.
            if (menu.Focused) {
                top = menu;
            }
        }

        return top;
    }

    private static void UpdateTextMenu(TextMenu menu, in PointerInput input) {
        pointerMenu = menu;
        if (input.Back) {
            Press(Input.MenuCancel);
            return;
        }

        float lineHeight = ActiveFont.LineHeight;
        bool scrolled = input.Scroll != 0 && ScrollTextMenu(menu, input.Scroll, lineHeight);
        if (!TryHitTextMenuItem(menu, input.Position, lineHeight, out int index, out ItemZone zone)) {
            return;
        }

        // After a wheel scroll the row under a still cursor has changed, so it is
        // selected as if the cursor had moved onto it.
        if (input.Hovers || scrolled) {
            SelectTextMenuItem(menu, index);
        }

        if (input.Click) {
            Press(zone switch {
                ItemZone.Decrease => Input.MenuLeft,
                ItemZone.Increase => Input.MenuRight,
                _ => Input.MenuConfirm
            });
        }
    }

    // While the pointer drives a menu, the menu must not slide under it: TextMenu's
    // auto-scroll centers the selection, so every hover would move the list and the
    // next row would end up under a still cursor. The module's TextMenu.Update hook
    // asks this and puts Y back after the native update. AutoScroll itself is left
    // alone because Everest submenus save and restore their container's flag. When
    // the hold is released, auto-scroll resumes and eases to the selection.
    internal static bool HoldsScroll(TextMenu menu) {
        return ShowsCursor && ReferenceEquals(menu, pointerMenu);
    }

    // The wheel scrolls the list itself, two lines per notch.
    private static bool ScrollTextMenu(TextMenu menu, int direction, float lineHeight) {
        if (!TryScrollMenuY(menu.Y, direction * lineHeight * 2f, menu.Height, menu.Justify.Y, Engine.Height, out float y)) {
            return false;
        }

        menu.Y = y;
        return true;
    }

    // Moves a menu's Y by `distance` (positive scrolls toward the bottom of the list)
    // within the bounds TextMenu.ScrollTargetY clamps auto-scroll to. Menus that fit
    // TextMenu.ScrollableMinSize (screen height minus 300) do not scroll. Returns false
    // when Y would not change, including at either end.
    internal static bool TryScrollMenuY(float currentY, float distance, float menuHeight, float justifyY, float screenHeight, out float y) {
        y = currentY;
        if (menuHeight <= screenHeight - 300f) {
            return false;
        }

        float lowest = screenHeight - 150f - menuHeight * justifyY;
        float highest = 150f + menuHeight * justifyY;
        // Calc.Clamp's order: never throws when the bounds cross, unlike Math.Clamp.
        y = Math.Min(Math.Max(currentY - distance, lowest), highest);
        return y != currentY;
    }

    // Reads the menu's layout and resolves the row under the pointer. The geometry
    // itself lives in FindRowAt and ResolveValueZone, which take plain values.
    private static bool TryHitTextMenuItem(TextMenu menu, Vector2 pointer, float lineHeight, out int index, out ItemZone zone) {
        index = -1;
        zone = ItemZone.Body;
        float width = menu.Width;
        float left = menu.X - menu.Justify.X * width;
        if (pointer.X < left || pointer.X > left + width) {
            return false;
        }

        List<TextMenu.Item> items = menu.Items;
        rowBuffer.Clear();
        foreach (TextMenu.Item item in items) {
            rowBuffer.Add(new MenuRow(item.Visible, item.Visible ? item.Height() : 0f, item.Hoverable));
        }

        index = FindRowAt(rowBuffer, menu.Y - menu.Justify.Y * menu.Height, menu.ItemSpacing, pointer.Y, out float rowTop);
        if (index < 0) {
            return false;
        }

        TextMenu.Item hit = items[index];
        zone = ResolveValueZone(pointer.X, pointer.Y, rowTop, lineHeight, left + width, hit.RightWidth(), IsAdjustable(hit));
        return true;
    }

    // Mirrors TextMenu.renderItems: rows stack down from `top`, separated by `spacing`,
    // and hidden rows take no space. Returns the index of the row under pointerY, or -1
    // over a gap, a header or a disabled row.
    internal static int FindRowAt(List<MenuRow> rows, float top, float spacing, float pointerY, out float rowTop) {
        rowTop = top;
        for (int i = 0; i < rows.Count; i++) {
            MenuRow row = rows[i];
            if (!row.Visible) {
                continue;
            }

            if (pointerY >= rowTop && pointerY < rowTop + row.Height) {
                return row.Hoverable ? i : -1;
            }

            rowTop += row.Height + spacing;
        }

        return -1;
    }

    // Choice rows draw their value right-aligned in a column valueWidth wide, with "<"
    // at its left edge and ">" at its right edge. Clicking the left half of that column
    // steps the value down, the right half steps it up. Only the row's first line counts,
    // so an expanded option submenu's children still confirm.
    internal static ItemZone ResolveValueZone(float pointerX, float pointerY, float rowTop, float lineHeight, float menuRight, float valueWidth, bool adjustable) {
        if (!adjustable || valueWidth <= 0f || pointerY >= rowTop + lineHeight) {
            return ItemZone.Body;
        }

        float valueLeft = menuRight - valueWidth;
        if (pointerX < valueLeft) {
            return ItemZone.Body;
        }

        return pointerX < valueLeft + valueWidth * 0.5f ? ItemZone.Decrease : ItemZone.Increase;
    }

    // Sliders and choice rows override LeftPressed to step their value. Buttons and key
    // binding rows do not, so a click anywhere on them confirms.
    internal static bool IsAdjustable(TextMenu.Item item) {
        Type type = item.GetType();
        if (!adjustableItemTypes.TryGetValue(type, out bool adjustable)) {
            adjustable = type.GetMethod(nameof(TextMenu.Item.LeftPressed), Type.EmptyTypes)?.DeclaringType != typeof(TextMenu.Item);
            adjustableItemTypes[type] = adjustable;
        }

        return adjustable;
    }

    // Same enter/leave callbacks as TextMenu.MoveSelection, without its sound: hover
    // sweeps would otherwise play a rollover for every row crossed.
    private static void SelectTextMenuItem(TextMenu menu, int index) {
        if (menu.Selection == index) {
            return;
        }

        menu.Current?.OnLeave?.Invoke();
        menu.Selection = index;
        menu.Current?.OnEnter?.Invoke();
    }

    private static void UpdateMainMenu(OuiMainMenu mainMenu, in PointerInput input) {
        if (input.Back) {
            Press(Input.MenuCancel);
            return;
        }

        PressIf(input.Scroll < 0, Input.MenuUp);
        PressIf(input.Scroll > 0, Input.MenuDown);

        MenuButton hit = null;
        foreach (MenuButton button in mainMenu.Buttons) {
            if (button.Scene == mainMenu.Scene && button.Visible && HitsMainMenuButton(button, input.Position)) {
                hit = button;
                break;
            }
        }

        if (hit == null) {
            return;
        }

        if (input.Hovers && !hit.Selected) {
            hit.Selected = true;
            // Selecting a MenuButton makes it ignore input for one update, so the key
            // press that moved the selection cannot also confirm. A click that lands
            // the cursor and presses in the same frame means "open this", so it has to
            // reach the button it just selected.
            if (input.Click) {
                hit.canAcceptInput = true;
            }
        }

        PressIf(input.Click, Input.MenuConfirm);
    }

    // Bounds follow each button's Render. Buttons added by other mods with their own
    // layouts are not hit-tested; the keyboard still reaches them.
    private static bool HitsMainMenuButton(MenuButton button, Vector2 pointer) {
        Vector2 position = button.Position;
        switch (button) {
            case MainMenuClimb climb: {
                float labelWidth = ActiveFont.Measure(climb.label).X * 1.5f * climb.labelScale;
                float halfWidth = Math.Max(climb.icon.Width, labelWidth) * 0.5f;
                return Contains(position.X - halfWidth, position.Y, halfWidth * 2f, climb.ButtonHeight, pointer);
            }
            case MainMenuSmallButton small: {
                // Icon at x+0 (eased 32px right when selected), label 84px after it.
                float width = 32f + 84f + ActiveFont.Measure(small.label).X * small.labelScale;
                float centerY = position.Y + ActiveFont.LineHeight * 0.5f;
                return Contains(position.X, centerY - small.ButtonHeight * 0.5f, width, small.ButtonHeight, pointer);
            }
            default:
                return false;
        }
    }

    private static void UpdateFileSelect(OuiFileSelect fileSelect, in PointerInput input) {
        if (input.Back) {
            Press(Input.MenuCancel);
            return;
        }

        if (!fileSelect.SlotSelected) {
            UpdateFileSlots(fileSelect, input);
            return;
        }

        OuiFileSelectSlot slot = fileSelect.Slots[fileSelect.SlotIndex];
        if (slot == null || slot.StartingGame) {
            return;
        }

        PressIf(input.Scroll < 0, Input.MenuUp);
        PressIf(input.Scroll > 0, Input.MenuDown);
        if (slot.deleting) {
            UpdateFileDeletePrompt(slot, input);
        } else {
            UpdateFileSlotButtons(slot, input);
        }
    }

    private static void UpdateFileSlots(OuiFileSelect fileSelect, in PointerInput input) {
        OuiFileSelectSlot[] slots = fileSelect.Slots;
        int next = fileSelect.SlotIndex + input.Scroll;
        // Everest wraps Up on the first slot and Down on the last; the wheel stops instead.
        if (input.Scroll != 0 && next >= 0 && next < slots.Length) {
            Press(input.Scroll < 0 ? Input.MenuUp : Input.MenuDown);
        }

        int hit = -1;
        for (int i = 0; i < slots.Length; i++) {
            OuiFileSelectSlot slot = slots[i];
            if (slot == null || !slot.Visible) {
                continue;
            }

            float halfWidth = slot.Card.Width * 0.5f;
            if (Contains(slot.X - halfWidth, slot.Y - FileSlotHalfHeight, halfWidth * 2f, FileSlotHalfHeight * 2f, input.Position)) {
                hit = i;
                break;
            }
        }

        if (hit < 0) {
            return;
        }

        if (input.Hovers && fileSelect.SlotIndex != hit) {
            fileSelect.SlotIndex = hit;
            // OuiFileSelect.Update only rescrolls when its own input changed the index.
            foreach (OuiFileSelectSlot slot in slots) {
                slot?.MoveTo(slot.IdlePosition.X, slot.IdlePosition.Y);
            }
        }

        PressIf(input.Click, Input.MenuConfirm);
    }

    // Mirrors OuiFileSelectSlot.orig_Render: buttons stack centered under the card,
    // starting 150px above it and easing down 350px as the slot opens.
    private static void UpdateFileSlotButtons(OuiFileSelectSlot slot, in PointerInput input) {
        List<OuiFileSelectSlot.Button> buttons = slot.buttons;
        float y = slot.Y - 150f + 350f * slot.selectedEase;
        for (int i = 0; i < buttons.Count; i++) {
            OuiFileSelectSlot.Button button = buttons[i];
            float height = ActiveFont.LineHeight * button.Scale;
            float halfWidth = ActiveFont.Measure(button.Label).X * button.Scale * 0.5f;
            if (Contains(slot.X - halfWidth, y, halfWidth * 2f, height, input.Position)) {
                if (input.Hovers) {
                    slot.buttonIndex = i;
                }

                PressIf(input.Click, Input.MenuConfirm);
                return;
            }

            y += height + FileSlotButtonSpacing;
        }
    }

    // Mirrors the delete confirmation in OuiFileSelectSlot.orig_Render: "yes" then "no",
    // centered on screen and sliding up 64px as the prompt eases in.
    private static void UpdateFileDeletePrompt(OuiFileSelectSlot slot, in PointerInput input) {
        const float scale = 0.8f;
        float lineHeight = ActiveFont.LineHeight;
        float top = 540f + 16f + 64f * (1f - Ease.CubeOut(slot.deletingEase));
        string[] labels = { Dialog.Clean("file_delete_yes"), Dialog.Clean("file_delete_no") };
        for (int i = 0; i < labels.Length; i++) {
            float halfWidth = ActiveFont.Measure(labels[i]).X * scale * 0.5f;
            if (Contains(960f - halfWidth, top + lineHeight * i, halfWidth * 2f, lineHeight * scale, input.Position)) {
                if (input.Hovers) {
                    slot.deleteIndex = i;
                }

                PressIf(input.Click, Input.MenuConfirm);
                return;
            }
        }
    }

    // Chapter icons move the mountain camera, so selection happens on click rather than
    // on hover: click an icon to walk to it, click the selected icon to open it.
    private static void UpdateChapterSelect(OuiChapterSelect chapterSelect, in PointerInput input) {
        if (input.Back) {
            ClearStep();
            Press(Input.MenuCancel);
            return;
        }

        int area = chapterSelect.area;
        // OuiChapterSelect ignores Left/Right/Confirm until its input delay runs out.
        bool ready = chapterSelect.inputDelay <= Engine.DeltaTime;
        if (ContinueStep(chapterSelect, area, ready)) {
            return;
        }

        if (ready) {
            PressIf(input.Scroll < 0, Input.MenuLeft);
            PressIf(input.Scroll > 0, Input.MenuRight);
        }

        if (!input.Click) {
            return;
        }

        List<OuiChapterSelectIcon> icons = chapterSelect.icons;
        for (int i = 0; i < icons.Count; i++) {
            OuiChapterSelectIcon icon = icons[i];
            if (icon == null || icon.IsHidden || icon.HideIcon || !icon.Visible) {
                continue;
            }

            // OuiChapterSelectIcon.Render scales every icon to 100px, growing to 144px
            // while it is the selected one.
            float halfSize = (ChapterIconIdleSize + ChapterIconHoverGrowth * Ease.CubeInOut(icon.sizeEase)) * 0.5f;
            if (!Contains(icon.X - halfSize, icon.Y - halfSize, halfSize * 2f, halfSize * 2f, input.Position)) {
                continue;
            }

            if (i == area) {
                PressIf(ready, Input.MenuConfirm);
            } else {
                StartStep(chapterSelect, i, area, ready);
            }

            return;
        }
    }

    // The A/B/C side tabs and checkpoint tabs: click a tab to walk to it, click the
    // selected tab to confirm it.
    private static void UpdateChapterPanel(OuiChapterPanel panel, in PointerInput input) {
        if (input.Back) {
            ClearStep();
            Press(Input.MenuCancel);
            return;
        }

        int selected = panel.option;
        if (ContinueStep(panel, selected, ready: true)) {
            return;
        }

        PressIf(input.Scroll < 0, Input.MenuLeft);
        PressIf(input.Scroll > 0, Input.MenuRight);
        if (!input.Click) {
            return;
        }

        Vector2 center = panel.OptionsRenderPosition;
        List<OuiChapterPanel.Option> options = panel.options;
        for (int i = 0; i < options.Count; i++) {
            OuiChapterPanel.Option option = options[i];
            // Option.Render centers the tab background 10px below the render position.
            Vector2 tabCenter = option.GetRenderPosition(center) + new Vector2(0f, 10f);
            float halfWidth = option.Bg.Width * option.Scale * 0.5f;
            float halfHeight = option.Bg.Height * option.Scale * 0.5f;
            if (!Contains(tabCenter.X - halfWidth, tabCenter.Y - halfHeight, halfWidth * 2f, halfHeight * 2f, input.Position)) {
                continue;
            }

            if (i == selected) {
                Press(Input.MenuConfirm);
            } else {
                StartStep(panel, i, selected, ready: true);
            }

            return;
        }
    }

    // The journal is a full-screen book: click the left or right half, or scroll, to
    // turn pages.
    private static void UpdateJournal(OuiJournal journal, in PointerInput input) {
        if (input.Back) {
            Press(Input.MenuCancel);
            return;
        }

        bool previous = input.Scroll < 0 || input.Click && input.Position.X < 960f;
        bool next = input.Scroll > 0 || input.Click && input.Position.X >= 960f;
        PressIf(previous, Input.MenuLeft);
        PressIf(next && !previous, Input.MenuRight);
    }

    internal static void StartStep(Oui owner, int target, int current, bool ready) {
        stepOwner = owner;
        stepTarget = target;
        stepInjectedFrom = -1;
        ContinueStep(owner, current, ready);
    }

    // Presses Left or Right once per accepted step until the owner's selection reaches
    // the target. Returns true while a walk is in progress so other pointer input waits.
    // A press that did not move the selection (a locked chapter, a hidden icon) ends the
    // walk where it stopped.
    internal static bool ContinueStep(Oui owner, int current, bool ready) {
        if (stepOwner != owner) {
            return false;
        }

        if (current == stepTarget || current == stepInjectedFrom) {
            ClearStep();
            return false;
        }

        if (ready) {
            Press(stepTarget > current ? Input.MenuRight : Input.MenuLeft);
            stepInjectedFrom = current;
        }

        return true;
    }

    internal static void ClearStep() {
        stepOwner = null;
        stepInjectedFrom = -1;
    }

    private static void Deactivate() {
        ShowsCursor = false;
        wasActive = false;
        pointerMenu = null;
        ClearStep();
    }

    private static void PressIf(bool condition, VirtualButton button) {
        if (condition) {
            Press(button);
        }
    }

    // VirtualButton.Pressed reports true while its buffer counter is positive. The next
    // VirtualButton.Update resets the counter unless the real binding is held, and
    // ReleaseSyntheticPresses resets it just before that update in case it is, so a
    // synthetic press lasts exactly one frame either way.
    internal static void Press(VirtualButton button) {
        button.bufferCounter = Math.Max(button.bufferCounter, 1f);
        syntheticPresses.Add(button);
    }

    internal static void ReleaseSyntheticPresses() {
        foreach (VirtualButton button in syntheticPresses) {
            button.bufferCounter = 0f;
        }

        syntheticPresses.Clear();
    }

    private static bool Contains(float x, float y, float width, float height, Vector2 point) {
        return point.X >= x && point.X <= x + width && point.Y >= y && point.Y <= y + height;
    }
}
