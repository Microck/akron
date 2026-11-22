using System;
using System.Globalization;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_overlay", "control Akron overlay: toggle|show|hide|status")]
    public static void Overlay(string action = "toggle") {
        Scene scene = RequireScene();
        if (scene == null) {
            return;
        }

        switch ((action ?? string.Empty).Trim().ToLowerInvariant()) {
            case "":
            case "toggle":
                Log("overlay: " + (AkronModule.ToggleOverlayVisible(scene) ? "visible" : "hidden"));
                break;
            case "show":
                Log("overlay: " + (AkronModule.SetOverlayVisible(scene, true) ? "visible" : "hidden"));
                break;
            case "hide":
                Log("overlay: " + (AkronModule.SetOverlayVisible(scene, false) ? "visible" : "hidden"));
                break;
            case "status":
                Log("overlay: " + (AkronModule.IsOverlayVisible ? "visible" : "hidden"));
                break;
            default:
                Log("unknown overlay action: " + action);
                break;
        }
    }

    // Command-only automation controls for live verification. These operate the
    // overlay without requiring synthetic keyboard/controller input.
    [Command("akron_overlay_move", "move Akron overlay selection: left|right|up|down")]
    public static void OverlayMove(string direction = "") {
        Scene scene = RequireScene();
        if (scene == null) {
            return;
        }

        AkronOverlay overlay = AkronModule.GetOverlay(scene, ensureVisible: true);
        if (overlay == null || !overlay.MoveSelection(direction)) {
            Log("overlay move failed: " + direction);
            return;
        }

        Log("overlay: " + overlay.DescribeState());
    }

    [Command("akron_overlay_select", "select an Akron overlay action by exact label")]
    public static void OverlaySelect(string label = "", string part2 = "", string part3 = "", string part4 = "", string part5 = "", string part6 = "") {
        Scene scene = RequireScene();
        if (scene == null) {
            return;
        }

        label = JoinCommandText(label, part2, part3, part4, part5, part6);
        AkronOverlay overlay = AkronModule.GetOverlay(scene, ensureVisible: true);
        if (overlay == null || !overlay.SelectAction(label)) {
            Log("overlay select failed: " + label);
            return;
        }

        Log("overlay: " + overlay.DescribeState());
    }

    [Command("akron_overlay_options", "open an Akron overlay action options popup by exact label")]
    public static void OverlayOptions(string label = "", string part2 = "", string part3 = "", string part4 = "", string part5 = "", string part6 = "") {
        Scene scene = RequireScene();
        if (scene == null) {
            return;
        }

        label = JoinCommandText(label, part2, part3, part4, part5, part6);
        AkronOverlay overlay = AkronModule.GetOverlay(scene, ensureVisible: true);
        if (overlay == null || !overlay.OpenActionOptions(label)) {
            Log("overlay options failed: " + label);
            return;
        }

        Log("overlay: " + overlay.DescribeState());
    }

    [Command("akron_overlay_exec", "execute the current Akron overlay action or an exact label")]
    public static void OverlayExec(string label = "", string part2 = "", string part3 = "", string part4 = "", string part5 = "", string part6 = "") {
        Scene scene = RequireScene();
        if (scene == null) {
            return;
        }

        label = JoinCommandText(label, part2, part3, part4, part5, part6);
        AkronOverlay overlay = AkronModule.GetOverlay(scene, ensureVisible: true);
        if (overlay == null) {
            Log("overlay execute failed");
            return;
        }

        if (!string.IsNullOrWhiteSpace(label) && !overlay.SelectAction(label)) {
            Log("overlay select failed: " + label);
            return;
        }

        if (!overlay.ExecuteSelected()) {
            Log("overlay execute failed");
            return;
        }

        Log("overlay: " + overlay.DescribeState());
    }

    [Command("akron_overlay_select_area", "start overlay area selection: auto-kill|auto-deafen")]
    public static void OverlaySelectArea(string kind = "") {
        Scene scene = RequireScene();
        if (scene == null) {
            return;
        }

        AkronOverlay overlay = AkronModule.GetOverlay(scene, ensureVisible: true);
        if (overlay == null) {
            Log("overlay area selection failed: " + kind);
            return;
        }

        switch (NormalizeToken(kind)) {
            case "autokill":
            case "kill":
                overlay.BeginAutoKillAreaSelectionFromAutomation();
                Log("overlay-area-selection: auto-kill");
                return;
            case "autodeafen":
            case "deafen":
                overlay.BeginAutoDeafenAreaSelectionFromAutomation();
                Log("overlay-area-selection: auto-deafen");
                return;
            default:
                Log("unknown overlay area selection: " + kind);
                return;
        }
    }

    [Command("akron_overlay_search", "set Akron overlay search query")]
    public static void OverlaySearch(string query = "", string part2 = "", string part3 = "", string part4 = "", string part5 = "", string part6 = "") {
        Scene scene = RequireScene();
        if (scene == null) {
            return;
        }

        query = JoinCommandText(query, part2, part3, part4, part5, part6);
        AkronOverlay overlay = AkronModule.GetOverlay(scene, ensureVisible: true);
        if (overlay == null) {
            Log("overlay search failed");
            return;
        }

        overlay.SetSearchQuery(query ?? string.Empty);
        Log("overlay: " + overlay.DescribeState());
    }

    [Command("akron_overlay_collapse", "toggle Akron overlay window collapse by exact title")]
    public static void OverlayCollapse(string title = "", string part2 = "", string part3 = "", string part4 = "", string part5 = "", string part6 = "") {
        Scene scene = RequireScene();
        if (scene == null) {
            return;
        }

        title = JoinCommandText(title, part2, part3, part4, part5, part6);
        if (!AkronModule.IsOverlayVisible) {
            AkronModule.SetOverlayVisible(scene, true);
        }

        AkronOverlay overlay = AkronModule.GetOverlay(scene);
        if (overlay == null || !overlay.ToggleCollapsedWindow(title)) {
            Log("overlay collapse failed: " + title);
            return;
        }

        Log("overlay: " + overlay.DescribeState());
    }

    [Command("akron_overlay_state", "show Akron overlay state for automation")]
    public static void OverlayState() {
        Scene scene = RequireScene();
        if (scene == null) {
            return;
        }

        AkronOverlay overlay = AkronModule.GetOverlay(scene);
        Log("overlay: " + (overlay == null ? "missing" : overlay.DescribeState()));
    }
}
