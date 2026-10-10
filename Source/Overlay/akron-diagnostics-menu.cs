using System;
using System.Linq;
using System.Text;
using ImGuiNET;
using Microsoft.Xna.Framework.Input;
using Monocle;
using NumericsVector2 = System.Numerics.Vector2;

namespace Celeste.Mod.Akron;

// The form uses Akron's ImGui frame, including when opened from Everest's mod options.
internal static class AkronDiagnosticsMenu {
    private static Scene ownerScene;
    private static TextMenu parent;
    private static bool parentWasVisible;
    private static bool parentWasFocused;
    private static Oui overworldPage;
    private static bool overworldPageWasActive;
    private static bool overworldPageWasFocused;
    private static bool overlayWasVisible;
    private static string endpoint;
    private static string endpointError;
    private static string title = string.Empty;
    private static string problem = string.Empty;
    private static string steps = string.Empty;
    private static string expected = string.Empty;
    private static string extra = string.Empty;
    private static bool showStatus;
    private static bool popupNeedsOpen;
    private static bool emptyConfirmationPending;
    private static bool emptyConfirmationActive;
    private static bool emptyConfirmationShown;
    private static bool controllerInputReady;
    private static bool controllerSelectionActive;
    private static bool controllerSendSelected;
    private static int controllerStatusSelection;

    internal static bool IsOpen => ownerScene != null;
    internal static bool AcceptsTextInput => IsOpen && !showStatus && !emptyConfirmationActive && endpointError == null;

    internal static void Open() {
        Scene scene = Engine.Scene;
        if (scene == null || IsOpen) return;
        // Release the hide-pause cache before capturing the parent's visibility.
        if (scene is Level) AkronRuntimeOptions.RestorePauseMenuVisibility();
        ownerScene = scene;
        parent = scene.Entities.OfType<TextMenu>().FirstOrDefault(menu => menu.Focused);
        if (parent != null) {
            parentWasVisible = parent.Visible;
            parentWasFocused = parent.Focused;
            parent.Visible = false;
            parent.Focused = false;
        }
        overworldPage = (scene as Overworld)?.Current;
        if (overworldPage != null) {
            overworldPageWasActive = overworldPage.Active;
            overworldPageWasFocused = overworldPage.Focused;
            overworldPage.Active = false;
            overworldPage.Focused = false;
        }
        overlayWasVisible = AkronModule.IsOverlayVisible;
        title = problem = steps = expected = extra = string.Empty;
        AkronModule.SetOverlayVisible(scene, true);
        AkronModule.GetOverlay(scene)?.ClearSearchQuery();
        showStatus = AkronDiagnostics.Status.Phase != "idle";
        popupNeedsOpen = true;
        emptyConfirmationPending = false;
        emptyConfirmationActive = false;
        emptyConfirmationShown = false;
        controllerInputReady = false;
        controllerSelectionActive = false;
        controllerSendSelected = false;
        controllerStatusSelection = 0;
        ResolveEndpoint();
        Input.MenuConfirm.ConsumeBuffer();
        Input.MenuCancel.ConsumeBuffer();
    }

    internal static void CloseActive() {
        Close();
        AkronDiagnostics.Cancel();
    }

    internal static void CloseIfSceneChanged(Scene scene) {
        if (IsOpen && !ReferenceEquals(ownerScene, scene)) Close();
    }

    internal static string DescribeAction() => AkronDiagnostics.Status.Busy ? "Sending..." : AkronDiagnostics.Status.Phase == "idle" ? "Write report" : "View result";

    internal static string DescribeState() {
        AkronDiagnosticStatus state = AkronDiagnostics.Status;
        return "menu=" + (!IsOpen ? "closed" : showStatus ? "status" : emptyConfirmationActive ? "confirmation" : "form") +
            ";descriptionLength=" + (IsOpen ? BuildDescription().Length : 0) +
            ";phase=" + state.Phase + ";reportId=" + state.ReportId + ";message=" + state.Message;
    }

    // Commands require the visible form and its blank-report confirmation.
    internal static bool Execute(string action) {
        if (action == "open") { Open(); return IsOpen; }
        if (!IsOpen || !ReferenceEquals(ownerScene, Engine.Scene)) return false;
        switch (action) {
            case "consent":
                if (AkronDiagnostics.Status.Busy) return false;
                showStatus = false;
                emptyConfirmationActive = false;
                emptyConfirmationPending = false;
                ResolveEndpoint();
                return true;
            case "send": return Send();
            case "confirm":
                if (showStatus || !emptyConfirmationActive || !emptyConfirmationShown) return false;
                emptyConfirmationActive = false;
                SendConfirmed(string.Empty);
                return true;
            case "cancel": Close(); return true;
            case "copy": return CopyReportId();
            default: return false;
        }
    }

    internal static void Draw() {
        const string popupId = "Send diagnostics##akron_diagnostics";
        if (!IsOpen) {
            // ImGui keeps modal state after its owner closes outside a frame.
            if (ImGui.BeginPopupModal(popupId, ImGuiWindowFlags.NoSavedSettings)) {
                ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
            return;
        }
        if (!ReferenceEquals(ownerScene, Engine.Scene)) { Close(); return; }
        float scale = AkronModule.Settings.OverlayScale / 100f;
        NumericsVector2 display = ImGui.GetIO().DisplaySize;
        float width = Math.Min(720f * scale, display.X - 32f);
        float height = Math.Min((showStatus ? 220f : 560f) * scale, display.Y - 32f);
        if (popupNeedsOpen) {
            ImGui.OpenPopup(popupId);
            popupNeedsOpen = false;
        }
        ImGui.SetNextWindowPos(new NumericsVector2((display.X - width) / 2f, (display.Y - height) / 2f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new NumericsVector2(width, height), ImGuiCond.Always);
        if (!ImGui.BeginPopupModal(popupId, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings)) return;
        if (showStatus) DrawStatus();
        else {
            DrawForm(scale);
            if (IsOpen) DrawEmptyConfirmation(scale);
        }
        if (!IsOpen) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        controllerInputReady = true;
    }

    private static void DrawForm(float scale) {
        if (endpointError != null) {
            ImGui.TextWrapped(endpointError);
            if (ImGui.Button("Close") || (controllerInputReady &&
                (CancelPressed() || ControllerPressed(Buttons.A)))) Close();
            return;
        }
        // Keep the editor scrollable without pushing Send off a short screen.
        ImGui.BeginChild("##diagnostics_form_content", new NumericsVector2(0f, -42f * scale));
        ImGui.TextWrapped("Send to " + endpoint + ": recent logs, versions, mods, map/room, CPU/GPU/RAM, OS and runtime. Your text is sent as written; leave out private information.");
        ImGui.Spacing();
        ImGui.TextUnformatted("Title");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##diagnostics_title", ref title, 121);
        ImGui.TextUnformatted("What happened?");
        ImGui.InputTextMultiline("##diagnostics_problem", ref problem, 2001, new NumericsVector2(-1f, 90f * scale));
        ImGui.TextUnformatted("How can someone reproduce it?");
        ImGui.InputTextMultiline("##diagnostics_steps", ref steps, 2001, new NumericsVector2(-1f, 90f * scale));
        ImGui.TextUnformatted("What did you expect?");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##diagnostics_expected", ref expected, 501);
        ImGui.TextUnformatted("Anything else?");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##diagnostics_extra", ref extra, 501);
        string description = BuildDescription();
        if (description.Length > AkronDiagnostics.MaxDescriptionLength) {
            ImGui.TextWrapped("Report is over 4,000 characters. Shorten it before sending.");
        }
        ImGui.EndChild();
        ImGui.Spacing();
        if (ImGui.Button(controllerSelectionActive && !controllerSendSelected ? "> Cancel" : "Cancel")) { Close(); return; }
        ImGui.SameLine();
        ImGui.BeginDisabled(description.Length > AkronDiagnostics.MaxDescriptionLength);
        if (ImGui.Button(controllerSelectionActive && controllerSendSelected ? "> Send now" : "Send now")) Send();
        ImGui.EndDisabled();
        if (!controllerInputReady || emptyConfirmationActive) return;
        if (CancelPressed()) { Close(); return; }
        UpdateControllerSelection();
        if (ControllerPressed(Buttons.A)) {
            if (controllerSendSelected) Send();
            else Close();
        }
    }

    private static void DrawEmptyConfirmation(float scale) {
        const string popupId = "Send without a description?##akron_empty_diagnostics";
        if (emptyConfirmationPending) {
            ImGui.OpenPopup(popupId);
            emptyConfirmationPending = false;
        }
        ImGui.SetNextWindowSize(new NumericsVector2(430f * scale, 150f * scale), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(popupId, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings)) return;
        if (!emptyConfirmationActive) {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }
        emptyConfirmationShown = true;
        ImGui.TextWrapped("You're sending diagnostics without describing the issue. Are you sure?");
        ImGui.Spacing();
        if (ImGui.Button(controllerSelectionActive && !controllerSendSelected ? "> Go back" : "Go back")) {
            emptyConfirmationActive = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button(controllerSelectionActive && controllerSendSelected ? "> Send without description" : "Send without description")) {
            emptyConfirmationActive = false;
            ImGui.CloseCurrentPopup();
            SendConfirmed(string.Empty);
        }
        if (controllerInputReady) {
            UpdateControllerSelection();
            if (CancelPressed() ||
                (ControllerPressed(Buttons.A) && !controllerSendSelected)) {
                emptyConfirmationActive = false;
                ImGui.CloseCurrentPopup();
            }
            else if (ControllerPressed(Buttons.A) && controllerSendSelected) {
                emptyConfirmationActive = false;
                ImGui.CloseCurrentPopup();
                SendConfirmed(string.Empty);
            }
        }
        ImGui.EndPopup();
    }

    private static void DrawStatus() {
        // An automation command can confirm the visible child popup between frames.
        if (ImGui.BeginPopupModal("Send without a description?##akron_empty_diagnostics", ImGuiWindowFlags.NoSavedSettings)) {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        AkronDiagnosticStatus status = AkronDiagnostics.Status;
        ImGui.TextWrapped(status.Message);
        if (!string.IsNullOrEmpty(status.ReportId)) {
            ImGui.TextUnformatted("Report ID: " + status.ReportId);
            if (ImGui.Button(controllerSelectionActive && controllerStatusSelection == 1 ? "> Copy report ID" : "Copy report ID")) CopyReportId();
        }
        ImGui.Spacing();
        string closeLabel = status.Busy ? "Cancel upload and close" : "Close";
        if (ImGui.Button(controllerSelectionActive && controllerStatusSelection == 0 ? "> " + closeLabel : closeLabel)) { Close(); return; }
        if (!status.Busy) {
            ImGui.SameLine();
            int sendSelection = string.IsNullOrEmpty(status.ReportId) ? 1 : 2;
            if (ImGui.Button(controllerSelectionActive && controllerStatusSelection == sendSelection ? "> Send another report" : "Send another report")) StartNewReport();
        }
        if (!controllerInputReady) return;
        if (CancelPressed()) { Close(); return; }
        if (!status.Busy) {
            int lastSelection = string.IsNullOrEmpty(status.ReportId) ? 1 : 2;
            if (ControllerPressed(Buttons.DPadLeft) || ControllerPressed(Buttons.LeftThumbstickLeft)) {
                controllerSelectionActive = true;
                controllerStatusSelection = Math.Max(0, controllerStatusSelection - 1);
            } else if (ControllerPressed(Buttons.DPadRight) || ControllerPressed(Buttons.LeftThumbstickRight)) {
                controllerSelectionActive = true;
                controllerStatusSelection = Math.Min(lastSelection, controllerStatusSelection + 1);
            }
        }
        if (ControllerPressed(Buttons.A)) {
            if (controllerStatusSelection == 1 && !string.IsNullOrEmpty(status.ReportId)) CopyReportId();
            else if (!status.Busy && controllerStatusSelection > 0) StartNewReport();
            else Close();
        }
    }

    private static void StartNewReport() {
        title = problem = steps = expected = extra = string.Empty;
        controllerSelectionActive = false;
        controllerSendSelected = false;
        controllerStatusSelection = 0;
        ResolveEndpoint();
        showStatus = false;
    }

    private static bool Send() {
        if (!IsOpen || showStatus || endpointError != null) return false;
        string description = BuildDescription();
        if (description.Length > AkronDiagnostics.MaxDescriptionLength) return false;
        if (description.Length == 0) {
            AkronImGuiRenderer.EndTextInputSession();
            emptyConfirmationPending = true;
            emptyConfirmationActive = true;
            emptyConfirmationShown = false;
            controllerInputReady = false;
            controllerSendSelected = false;
            return true;
        }
        SendConfirmed(description);
        return true;
    }

    private static void SendConfirmed(string description) {
        AkronImGuiRenderer.EndTextInputSession();
        showStatus = true;
        controllerSelectionActive = false;
        controllerSendSelected = false;
        controllerStatusSelection = 0;
        AkronDiagnostics.StartConsentedUpload(endpoint, description);
        Input.MenuConfirm.ConsumeBuffer();
    }

    private static string BuildDescription() => FormatDescription(title, problem, steps, expected, extra);

    private static void UpdateControllerSelection() {
        if (ControllerPressed(Buttons.DPadLeft) || ControllerPressed(Buttons.LeftThumbstickLeft)) {
            controllerSelectionActive = true;
            controllerSendSelected = false;
        } else if (ControllerPressed(Buttons.DPadRight) || ControllerPressed(Buttons.LeftThumbstickRight)) {
            controllerSelectionActive = true;
            controllerSendSelected = true;
        }
    }

    private static bool ControllerPressed(Buttons button) =>
        Input.Gamepad >= 0 && Input.Gamepad < MInput.GamePads.Length && MInput.GamePads[Input.Gamepad].Pressed(button);

    private static bool CancelPressed() =>
        MInput.Keyboard.Pressed(Keys.Escape) || Input.MenuCancel.Pressed ||
        ControllerPressed(Buttons.B) || ControllerPressed(Buttons.Back);

    internal static string FormatDescription(string title, string problem, string steps, string expected, string extra) {
        StringBuilder report = new StringBuilder();
        AppendSection(report, "Title", title);
        AppendSection(report, "What happened", problem);
        AppendSection(report, "Steps to reproduce", steps);
        AppendSection(report, "Expected behavior", expected);
        AppendSection(report, "Additional context", extra);
        return report.ToString();
    }

    private static void AppendSection(StringBuilder report, string label, string value) {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (report.Length > 0) report.Append("\n\n");
        report.Append("## ").Append(label).Append("\n").Append(value.Trim());
    }

    private static void ResolveEndpoint() {
        try {
            endpoint = AkronDiagnostics.ResolveEndpoint(AkronModule.TryGetSettings()?.CommunityPackUploadEndpoint);
            endpointError = null;
        } catch (Exception) {
            endpoint = null;
            endpointError = "Diagnostics cannot be sent. Set the community upload endpoint to an HTTPS URL without credentials, query or fragment, then reopen this form.";
        }
    }

    private static bool CopyReportId() {
        string id = AkronDiagnostics.Status.ReportId;
        if (string.IsNullOrEmpty(id)) return false;
        try {
            TextInput.SetClipboardText(id);
            ownerScene?.Add(new AkronToast("Diagnostic report ID copied.", forceVisible: true));
            return true;
        } catch (Exception) {
            ownerScene?.Add(new AkronToast("Clipboard unavailable. Copy the report ID shown above.", forceVisible: true));
            return false;
        }
    }

    private static void Close() {
        if (!IsOpen) return;
        Scene scene = ownerScene;
        ownerScene = null;
        AkronImGuiRenderer.EndTextInputSession();
        if (AkronDiagnostics.Status.Busy) AkronDiagnostics.Cancel();
        if (ReferenceEquals(Engine.Scene, scene)) {
            if (parent != null && ReferenceEquals(parent.Scene, scene)) {
                parent.Visible = parentWasVisible;
                parent.Focused = parentWasFocused;
            }
            if (overworldPage != null && ReferenceEquals(overworldPage.Scene, scene)) {
                overworldPage.Active = overworldPageWasActive;
                if (scene is Overworld overworld && ReferenceEquals(overworld.Current, overworldPage)) overworldPage.Focused = overworldPageWasFocused;
            }
            if (!overlayWasVisible && AkronModule.IsOverlayVisible) AkronModule.SetOverlayVisible(scene, false);
        }
        parent = null;
        overworldPage = null;
        title = problem = steps = expected = extra = string.Empty;
        emptyConfirmationPending = false;
        emptyConfirmationActive = false;
        emptyConfirmationShown = false;
        controllerInputReady = false;
        Input.MenuConfirm.ConsumeBuffer();
        Input.MenuCancel.ConsumeBuffer();
        Input.Pause.ConsumeBuffer();
    }
}
