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

    [Command("akron_community_packs", "control community packs: status|url|refresh|search|category|list|import")]
    public static void CommunityPacks(string action = "status", string value = "", string part2 = "", string part3 = "", string part4 = "", string part5 = "") {
        string normalizedAction = (action ?? string.Empty).Trim().ToLowerInvariant();
        string text = JoinCommandText(value, part2, part3, part4, part5);
        switch (normalizedAction) {
            case "":
            case "status":
                LogCommunityPackStatus();
                return;
            case "url":
                AkronModule.Settings.CommunityPackIndexUrl = text ?? string.Empty;
                Log("community-pack-url: " + AkronCommunityPacks.ResolveIndexUrl(AkronModule.Settings.CommunityPackIndexUrl));
                return;
            case "refresh":
                AkronCommunityPacks.Refresh(AkronModule.Settings.CommunityPackIndexUrl, BuildCommunityPackCommandFilter());
                LogCommunityPackStatus();
                return;
            case "search":
                AkronModule.Settings.CommunityPackSearchQuery = text ?? string.Empty;
                LogCommunityPackStatus();
                return;
            case "category":
                if (!AkronProfilePacks.TryParseSection(text, out AkronProfileSection section)) {
                    Log("unknown community-pack category: " + text);
                    return;
                }

                AkronModule.Settings.CommunityPackSection = section;
                LogCommunityPackStatus();
                return;
            case "list":
                LogCommunityPackList();
                return;
            case "import":
                ImportCommunityPackByIndex(text);
                return;
            default:
                Log("unknown community-packs action: " + action);
                return;
        }
    }

    private static AkronCommunityPackFilter BuildCommunityPackCommandFilter() {
        return new AkronCommunityPackFilter {
            MapSid = Engine.Scene is Level level ? level.Session?.Area.GetSID() ?? string.Empty : string.Empty,
            Section = AkronModule.Settings.CommunityPackSection,
            Query = AkronModule.Settings.CommunityPackSearchQuery
        };
    }

    private static void LogCommunityPackStatus() {
        AkronCommunityPackSearchResult result = AkronCommunityPacks.Search(BuildCommunityPackCommandFilter());
        Log("community-pack-url: " + AkronCommunityPacks.ResolveIndexUrl(AkronModule.Settings.CommunityPackIndexUrl));
        Log("community-pack-map: " + BuildCommunityPackCommandFilter().MapSid);
        Log("community-pack-category: " + AkronCommunityPacks.DescribeCategory(AkronModule.Settings.CommunityPackSection));
        Log("community-pack-search: " + (AkronModule.Settings.CommunityPackSearchQuery ?? string.Empty));
        Log("community-pack-status: " + result.Status);
        Log("community-pack-count: " + result.Entries.Count.ToString(CultureInfo.InvariantCulture));
    }

    private static void LogCommunityPackList() {
        AkronCommunityPackSearchResult result = AkronCommunityPacks.Search(BuildCommunityPackCommandFilter());
        Log("community-pack-count: " + result.Entries.Count.ToString(CultureInfo.InvariantCulture));
        for (int index = 0; index < result.Entries.Count; index++) {
            AkronCommunityPackEntry entry = result.Entries[index];
            Log("community-pack-" + index.ToString(CultureInfo.InvariantCulture) + ": " +
                entry.Title + " | " +
                AkronProfilePacks.FormatSection(entry.Section) + " | " +
                entry.AuthorName + " | " +
                entry.MapUrl);
        }
    }

    private static void ImportCommunityPackByIndex(string text) {
        AkronCommunityPackSearchResult result = AkronCommunityPacks.Search(BuildCommunityPackCommandFilter());
        if (!int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ||
            index < 0 ||
            index >= result.Entries.Count) {
            Log("usage: akron_community_packs import <zero-based-index>");
            return;
        }

        AkronCommunityPackEntry entry = result.Entries[index];
        bool imported = AkronCommunityPacks.Import(entry, out string message);
        Log("community-pack-import: " + (imported ? "ok" : "failed") + " | " + message);
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
