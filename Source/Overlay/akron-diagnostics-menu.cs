using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

// Unlike gameplay action prompts, diagnostics can open from the overworld mod menu.
// Keep the caller alive and restore its focus instead of replacing Celeste's pause menu.
internal sealed class AkronDiagnosticsMenu : TextMenu {
    private static AkronDiagnosticsMenu current;
    private readonly TextMenu parent;
    private readonly bool parentWasVisible;
    private readonly bool parentWasFocused;
    private readonly Oui overworldPage;
    private readonly bool overworldPageWasActive;
    private readonly bool overworldPageWasFocused;
    private readonly bool restoreOverlay;
    private readonly bool ownsPause;
    private readonly Level level;
    private readonly Scene ownerScene;
    private AkronDiagnosticStatus displayedStatus;
    private enum Page { Consent, Description, Status }
    private Page page;
    private string description = string.Empty;
    private string descriptionMessage = string.Empty;
    private string descriptionCount = string.Empty;
    private int descriptionCaret;
    private int descriptionAnchor;
    private int descriptionScroll;
    private bool previousCommandsEnabled;
    private KeyboardState previousDescriptionKeyboard;
    private readonly Queue<char> descriptionInput = new Queue<char>();
    private readonly List<DescriptionLine> descriptionLines = new List<DescriptionLine>();
    private readonly record struct DescriptionLine(int Start, int End, string Text);
    private const float DescriptionScale = 0.6f;
    private const float DescriptionWidth = 1400f;
    private const int DescriptionVisibleLines = 8;
    private string endpoint;
    private bool closed;

    private AkronDiagnosticsMenu(Scene scene, TextMenu parentMenu) {
        ownerScene = scene;
        parent = parentMenu;
        if (parent != null) {
            parentWasVisible = parent.Visible;
            parentWasFocused = parent.Focused;
            parent.Focused = false;
            parent.Visible = false;
        }
        overworldPage = (scene as Overworld)?.Current;
        if (overworldPage != null) {
            // The title screen checks Selected, not Focused. Pause its input updates as well.
            overworldPageWasActive = overworldPage.Active;
            overworldPageWasFocused = overworldPage.Focused;
            overworldPage.Active = false;
            overworldPage.Focused = false;
        }
        level = scene as Level;
        restoreOverlay = AkronModule.IsOverlayVisible;
        if (restoreOverlay) AkronModule.SetOverlayVisible(scene, false);
        if (level != null && !level.Paused) {
            ownsPause = true;
            level.wasPaused = true;
            level.StartPauseEffects();
            level.Paused = true;
        }
        Tag = Tags.HUD | Tags.PauseUpdate;
        Depth = Depths.Top;
        // Native TextMenu waits for the scene clock to tick before setting this.
        // Diagnostics also works while that clock is held by a gameplay freeze.
        HighlightColor = HighlightColorA;
        Add(new AkronIgnoreSaveStateComponent(based: false));
        AutoScroll = false;
        ItemSpacing = 2f;
        OnESC = OnCancel = OnPause = CloseMenu;
        if (AkronDiagnostics.Status.Phase == "idle") ShowConsent();
        else ShowStatus();
    }

    internal static void Open(TextMenu parent = null) {
        Scene scene = Engine.Scene;
        if (scene == null || current != null) return;
        parent ??= scene.Entities.OfType<TextMenu>().FirstOrDefault(menu => menu.Focused);
        current = new AkronDiagnosticsMenu(scene, parent);
        scene.Add(current);
        if (scene is Level level) AkronRuntimeOptions.ApplyPauseMenuVisibility(level);
        Input.MenuConfirm.ConsumeBuffer();
        Input.MenuCancel.ConsumeBuffer();
    }

    internal static bool IsOpen => current != null && !current.closed;

    internal static bool UpdatePausedLevel(Level level) {
        if (!IsOpen || !ReferenceEquals(current.ownerScene, level)) return false;
        // The level may already be frozen by StartPos, Free Camera or Freeze Gameplay.
        // Keep menu input alive without resuming the level or consuming its input wait.
        current.Update();
        return true;
    }

    internal static void CloseActive() {
        current?.CloseMenu();
        AkronDiagnostics.Cancel();
    }

    internal static string DescribeAction() => AkronDiagnostics.Status.Busy ? "Sending..." : AkronDiagnostics.Status.Phase == "idle" ? "Review consent" : "View result";

    internal static string DescribeState() {
        AkronDiagnosticStatus state = AkronDiagnostics.Status;
        return "menu=" + (current == null ? "closed" : current.page.ToString().ToLowerInvariant()) +
            ";descriptionLength=" + (current?.description.Length ?? 0) +
            ";phase=" + state.Phase + ";reportId=" + state.ReportId + ";message=" + state.Message;
    }

    // Automation has the same two-step consent gate as the buttons. "send" cannot open or accept hidden consent.
    internal static bool Execute(string action) {
        if (action == "open") { Open(); return current != null; }
        if (current == null || current.closed || !ReferenceEquals(Engine.Scene, current.ownerScene)) return false;
        switch (action) {
            case "consent":
                if (AkronDiagnostics.Status.Busy) return false;
                current.ShowConsent();
                return current.page == Page.Consent;
            case "send":
                if (current.page != Page.Consent) return false;
                current.Send();
                return true;
            case "cancel":
                current.CloseMenu();
                return true;
            case "copy":
                return current.CopyReportId();
            default:
                return false;
        }
    }

    public override void Update() {
        if (page == Page.Description) {
            UpdateDescription();
            return;
        }
        base.Update();
        if (!closed && page == Page.Status && !ReferenceEquals(displayedStatus, AkronDiagnostics.Status)) ShowStatus();
    }

    public override void Render() {
        Draw.Rect(0, 0, 1920, 1080, Color.Black * 0.92f);
        if (page == Page.Description) {
            RenderDescription();
            return;
        }
        base.Render();
    }

    public override void Removed(Scene scene) {
        Finish(scene);
        base.Removed(scene);
    }

    public override void SceneEnd(Scene scene) {
        Finish(scene);
        base.SceneEnd(scene);
    }

    private void ClearItems() {
        foreach (Item item in Items.ToArray()) Remove(item);
        Selection = -1;
    }

    private void AddMessage(string text) {
        foreach (string line in AkronModule.WrapModMenuLine(text, 76)) Add(new SubHeader(line, topPadding: false));
    }

    private void ShowConsent() {
        if (AkronDiagnostics.Status.Busy) { ShowStatus(); return; }
        StopEditingDescription();
        page = Page.Status;
        ClearItems();
        Add(new Header("Send diagnostics"));
        try {
            endpoint = AkronDiagnostics.ResolveEndpoint(AkronModule.TryGetSettings()?.CommunityPackUploadEndpoint);
        } catch (Exception) {
            displayedStatus = AkronDiagnostics.Status;
            AddMessage("Diagnostics cannot be sent. Set the community upload endpoint to an HTTPS URL without credentials, query, or fragment, then reopen this menu.");
            Add(new Button("Back").Pressed(CloseMenu));
            FirstSelection();
            return;
        }
        Add(new Button(description.Length == 0 ? "Describe the problem (optional)..." : "Edit description (" + description.Length + " characters)...").Pressed(EditDescription));
        AddMessage("Uploads your description, game / mod versions, map / room, and CPU / GPU / RAM / OS / runtime details.");
        AddMessage("Includes available tails of log.txt, akron-current.log, akron-previous.log and performance.jsonl. At most 1 MiB each, 4 MiB total.");
        AddMessage("Automatic details and logs redact common credentials, names and paths, but may contain personal text. No saves or environment variables are collected.");
        AddMessage("Your description is sent as written. Do not include passwords or other private information.");
        AddMessage("Stored privately on Cloudflare and sent to Akron's private Discord channel #diagnostic-alert. People with access can read and download the report.");
        AddMessage("Upload endpoint: " + endpoint);
        AddMessage("Nothing is sent until Send now. Closing cancels the request, but cannot recall a received report. The service retries Discord delivery; the game does not retry uploads.");
        Item cancel = new Button("Cancel").Pressed(CloseMenu);
        Add(cancel);
        Add(new Button("Send now").Pressed(Send));
        page = Page.Consent;
        Selection = Items.IndexOf(cancel);
    }

    private void Send() {
        if (page != Page.Consent) return;
        page = Page.Status;
        AkronDiagnostics.StartConsentedUpload(endpoint, description);
        ShowStatus();
        Input.MenuConfirm.ConsumeBuffer();
    }

    private void ShowStatus() {
        StopEditingDescription();
        page = Page.Status;
        displayedStatus = AkronDiagnostics.Status;
        ClearItems();
        Add(new Header("Diagnostics"));
        AddMessage(displayedStatus.Message);
        if (!string.IsNullOrEmpty(displayedStatus.ReportId)) {
            AddMessage("Report ID: " + displayedStatus.ReportId);
            Add(new Button("Copy report ID").Pressed(() => CopyReportId()));
        }
        if (displayedStatus.Busy) {
            Add(new Button("Cancel upload and go back").Pressed(CloseMenu));
        } else {
            Add(new Button("Back").Pressed(CloseMenu));
            Add(new Button("Review and send a new report...").Pressed(ShowConsent));
        }
        FirstSelection();
    }

    private bool CopyReportId() {
        string id = AkronDiagnostics.Status.ReportId;
        if (string.IsNullOrEmpty(id)) return false;
        try {
            TextInput.SetClipboardText(id);
            Scene?.Add(new AkronToast("Diagnostic report ID copied.", forceVisible: true));
            return true;
        } catch (Exception) {
            Scene?.Add(new AkronToast("Clipboard unavailable. Copy the report ID displayed above.", forceVisible: true));
            return false;
        }
    }

    private void CloseMenu() {
        Finish(ownerScene);
        RemoveSelf();
    }

    private void Finish(Scene scene) {
        if (closed) return;
        closed = true;
        StopEditingDescription();
        Focused = false;
        if (ReferenceEquals(current, this)) current = null;
        if (AkronDiagnostics.Status.Busy) AkronDiagnostics.Cancel();
        if (!ReferenceEquals(Engine.Scene, scene)) return;
        if (parent != null && ReferenceEquals(parent.Scene, scene)) {
            parent.Visible = parentWasVisible;
            parent.Focused = parentWasFocused;
        }
        if (overworldPage != null && ReferenceEquals(overworldPage.Scene, scene)) {
            overworldPage.Active = overworldPageWasActive;
            if (scene is Overworld overworld && ReferenceEquals(overworld.Current, overworldPage)) {
                overworldPage.Focused = overworldPageWasFocused;
            }
        }
        if (ownsPause && level != null) {
            level.Paused = false;
            level.unpauseTimer = 0.15f;
            Audio.Play(SFX.ui_game_unpause);
        }
        if (restoreOverlay) AkronModule.SetOverlayVisible(scene, true);
        Input.MenuConfirm.ConsumeBuffer();
        Input.MenuCancel.ConsumeBuffer();
        Input.Pause.ConsumeBuffer();
    }

    private void EditDescription() {
        if (page != Page.Consent) return;
        page = Page.Description;
        Focused = false;
        previousCommandsEnabled = Engine.Commands.Enabled;
        Engine.Commands.Enabled = false;
        previousDescriptionKeyboard = Keyboard.GetState();
        descriptionCaret = descriptionAnchor = description.Length;
        descriptionMessage = string.Empty;
        descriptionInput.Clear();
        RebuildDescriptionLines();
        TextInput.OnInput += QueueDescriptionInput;
        Input.MenuConfirm.ConsumeBuffer();
    }

    private void QueueDescriptionInput(char value) {
        descriptionInput.Enqueue(value);
    }

    private void StopEditingDescription() {
        if (page != Page.Description) return;
        TextInput.OnInput -= QueueDescriptionInput;
        descriptionInput.Clear();
        Engine.Commands.Enabled = previousCommandsEnabled;
        Focused = true;
        page = Page.Consent;
        ConsumeDescriptionInput();
    }

    private static void ConsumeDescriptionInput() {
        foreach (VirtualInput input in MInput.VirtualInputs) {
            if (input is VirtualButton button) button.ConsumePress();
        }
    }

    private void UpdateDescription() {
        KeyboardState keyboard = Keyboard.GetState();
        bool Pressed(Keys key) => keyboard.IsKeyDown(key) && previousDescriptionKeyboard.IsKeyUp(key);
        bool control = (keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl)) &&
            keyboard.IsKeyUp(Keys.LeftAlt) && keyboard.IsKeyUp(Keys.RightAlt);
        bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        bool handled = descriptionInput.Count > 0;
        bool backspaceFromText = false;
        bool newlineFromText = false;
        if (Pressed(Keys.Escape) || Pressed(Keys.Tab) || (control && Pressed(Keys.Enter))) {
            ShowConsent();
            return;
        }

        if (control) {
            if (Pressed(Keys.A)) {
                descriptionAnchor = 0;
                descriptionCaret = description.Length;
                handled = true;
            } else if (Pressed(Keys.C) || Pressed(Keys.X)) {
                int start = Math.Min(descriptionAnchor, descriptionCaret);
                int count = Math.Abs(descriptionAnchor - descriptionCaret);
                if (count > 0) {
                    try {
                        TextInput.SetClipboardText(description.Substring(start, count));
                        if (Pressed(Keys.X)) InsertDescription(string.Empty);
                    } catch (Exception) {
                        descriptionMessage = "Clipboard unavailable. Your description is unchanged.";
                    }
                }
                handled = true;
            } else if (Pressed(Keys.V)) {
                try { InsertDescription(TextInput.GetClipboardText()); }
                catch (Exception) { descriptionMessage = "Clipboard unavailable. Type your description instead."; }
                handled = true;
            }
        } else {
            while (descriptionInput.Count > 0) {
                char value = descriptionInput.Dequeue();
                if (value == '\b') {
                    DeleteDescription(-1);
                    backspaceFromText = true;
                } else if (value is '\r' or '\n') {
                    InsertDescription("\n");
                    newlineFromText = true;
                } else if (!char.IsControl(value)) {
                    if (char.IsHighSurrogate(value)) {
                        if (descriptionInput.TryPeek(out char next) && char.IsLowSurrogate(next)) {
                            InsertDescription(char.ConvertFromUtf32(char.ConvertToUtf32(value, descriptionInput.Dequeue())));
                        }
                    } else if (!char.IsLowSurrogate(value)) {
                        InsertDescription(value.ToString());
                    }
                }
            }
            if (Pressed(Keys.Enter) && !newlineFromText) { InsertDescription("\n"); handled = true; }
        }
        descriptionInput.Clear();

        if ((Pressed(Keys.Back) && !backspaceFromText) || Pressed(Keys.Delete)) {
            DeleteDescription(Pressed(Keys.Back) ? -1 : 1);
            handled = true;
        }
        if (Pressed(Keys.Left) || Pressed(Keys.Right)) {
            int direction = Pressed(Keys.Left) ? -1 : 1;
            descriptionCaret = !shift && descriptionAnchor != descriptionCaret
                ? direction < 0 ? Math.Min(descriptionAnchor, descriptionCaret) : Math.Max(descriptionAnchor, descriptionCaret)
                : AdjacentDescriptionPosition(descriptionCaret, direction);
            if (!shift) descriptionAnchor = descriptionCaret;
            handled = true;
        }
        int line = DescriptionCaretLine();
        if (Pressed(Keys.Home) || Pressed(Keys.End)) {
            descriptionCaret = Pressed(Keys.Home)
                ? control ? 0 : descriptionLines[line].Start
                : control ? description.Length : descriptionLines[line].End;
            if (!shift) descriptionAnchor = descriptionCaret;
            handled = true;
        }
        if (Pressed(Keys.Up) || Pressed(Keys.Down)) {
            int column = descriptionCaret - descriptionLines[line].Start;
            DescriptionLine target = descriptionLines[Math.Clamp(line + (Pressed(Keys.Up) ? -1 : 1), 0, descriptionLines.Count - 1)];
            descriptionCaret = Math.Min(target.Start + column, target.End);
            if (descriptionCaret > 0 && descriptionCaret < description.Length && char.IsLowSurrogate(description[descriptionCaret])) descriptionCaret--;
            if (!shift) descriptionAnchor = descriptionCaret;
            handled = true;
        }
        KeepDescriptionCaretVisible();
        previousDescriptionKeyboard = keyboard;
        if (handled) ConsumeDescriptionInput();
        else if (Input.MenuCancel.Pressed) ShowConsent();
    }

    private int AdjacentDescriptionPosition(int position, int direction) {
        int next = Math.Clamp(position + direction, 0, description.Length);
        if (next > 0 && next < description.Length && char.IsLowSurrogate(description[next])) next += direction;
        return next;
    }

    private void DeleteDescription(int direction) {
        if (descriptionAnchor == descriptionCaret) {
            descriptionAnchor = AdjacentDescriptionPosition(descriptionCaret, direction);
        }
        InsertDescription(string.Empty);
    }

    private void InsertDescription(string value) {
        value = (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ");
        int start = Math.Min(descriptionAnchor, descriptionCaret);
        int end = Math.Max(descriptionAnchor, descriptionCaret);
        if (start == end && value.Length == 0) return;
        if (description.Length - (end - start) + value.Length > AkronDiagnostics.MaxDescriptionLength) {
            descriptionMessage = "Limit: 4,000 characters. Shorten the text before adding more.";
            return;
        }
        description = string.Concat(description.AsSpan(0, start), value, description.AsSpan(end));
        descriptionCaret = descriptionAnchor = start + value.Length;
        descriptionMessage = string.Empty;
        RebuildDescriptionLines();
    }

    private void RebuildDescriptionLines() {
        descriptionLines.Clear();
        int start = 0;
        int lastSpace = -1;
        float width = 0;
        for (int index = 0; index < description.Length; index++) {
            char value = description[index];
            if (value == '\n') {
                descriptionLines.Add(new DescriptionLine(start, index, description.Substring(start, index - start)));
                start = index + 1;
                lastSpace = -1;
                width = 0;
                continue;
            }
            float characterWidth = ActiveFont.Measure(value).X * DescriptionScale;
            if (index > start && width + characterWidth > DescriptionWidth) {
                int end = lastSpace >= start ? lastSpace + 1 : index;
                descriptionLines.Add(new DescriptionLine(start, end, description.Substring(start, end - start)));
                start = end;
                lastSpace = -1;
                width = 0;
                index = end - 1;
                continue;
            }
            if (char.IsWhiteSpace(value)) lastSpace = index;
            width += characterWidth;
        }
        descriptionLines.Add(new DescriptionLine(start, description.Length, description.Substring(start)));
        descriptionCount = description.Length + " / " + AkronDiagnostics.MaxDescriptionLength + " characters";
        KeepDescriptionCaretVisible();
    }

    private int DescriptionCaretLine() {
        for (int index = descriptionLines.Count - 1; index >= 0; index--) {
            if (descriptionCaret >= descriptionLines[index].Start) return index;
        }
        return 0;
    }

    private void KeepDescriptionCaretVisible() {
        int line = DescriptionCaretLine();
        descriptionScroll = Math.Clamp(descriptionScroll, Math.Max(0, line - DescriptionVisibleLines + 1), line);
    }

    private float DescriptionTextWidth(int start, int end) {
        float width = 0;
        for (int index = start; index < end; index++) width += ActiveFont.Measure(description[index]).X * DescriptionScale;
        return width;
    }

    private void RenderDescription() {
        ActiveFont.DrawOutline("What went wrong?", new Vector2(960, 160), new Vector2(0.5f, 0.5f), Vector2.One, Color.White, 2f, Color.Black);
        ActiveFont.DrawOutline("Optional. Include what happened, what you expected, and how to reproduce it.",
            new Vector2(960, 240), new Vector2(0.5f, 0.5f), Vector2.One * DescriptionScale, Color.LightGray, 2f, Color.Black);
        float lineHeight = ActiveFont.LineHeight * DescriptionScale;
        Vector2 origin = new Vector2(260, 330);
        Draw.Rect(origin - new Vector2(20, 16), DescriptionWidth + 40, lineHeight * DescriptionVisibleLines + 32, Color.DarkSlateGray * 0.8f);
        int selectionStart = Math.Min(descriptionAnchor, descriptionCaret);
        int selectionEnd = Math.Max(descriptionAnchor, descriptionCaret);
        int caretLine = DescriptionCaretLine();
        for (int index = descriptionScroll; index < Math.Min(descriptionLines.Count, descriptionScroll + DescriptionVisibleLines); index++) {
            DescriptionLine line = descriptionLines[index];
            Vector2 position = origin + Vector2.UnitY * ((index - descriptionScroll) * lineHeight);
            int start = Math.Max(line.Start, selectionStart);
            int end = Math.Min(line.End, selectionEnd);
            if (end > start) Draw.Rect(position + Vector2.UnitX * DescriptionTextWidth(line.Start, start), DescriptionTextWidth(start, end), lineHeight, HighlightColor * 0.4f);
            ActiveFont.DrawOutline(line.Text, position, Vector2.Zero, Vector2.One * DescriptionScale, Color.White, 2f, Color.Black);
            if (index == caretLine) Draw.Rect(position + Vector2.UnitX * DescriptionTextWidth(line.Start, descriptionCaret), 2f, lineHeight, Color.White);
        }
        float bottom = origin.Y + lineHeight * DescriptionVisibleLines + 50;
        ActiveFont.DrawOutline(descriptionCount, new Vector2(960, bottom), new Vector2(0.5f, 0.5f), Vector2.One * DescriptionScale, Color.LightGray, 2f, Color.Black);
        ActiveFont.DrawOutline("Type or paste with Ctrl+V. Enter: new line. Esc / Ctrl+Enter / controller Cancel: done.",
            new Vector2(960, bottom + 60), new Vector2(0.5f, 0.5f), Vector2.One * DescriptionScale, Color.LightGray, 2f, Color.Black);
        ActiveFont.DrawOutline("Sent as written. Do not include passwords or other private information.",
            new Vector2(960, bottom + 110), new Vector2(0.5f, 0.5f), Vector2.One * DescriptionScale, Color.LightGray, 2f, Color.Black);
        if (descriptionMessage.Length > 0) ActiveFont.DrawOutline(descriptionMessage,
            new Vector2(960, bottom + 170), new Vector2(0.5f, 0.5f), Vector2.One * DescriptionScale, Color.Yellow, 2f, Color.Black);
    }
}
