using System;
using Celeste;
using ImGuiNET;
using NumericsVector2 = System.Numerics.Vector2;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawCommunityPackUploadWindow() {
        if (!uploadPackWindowOpen) {
            return;
        }

        NumericsVector2 displaySize = ImGui.GetIO().DisplaySize;
        NumericsVector2 windowSize = new NumericsVector2(
            Math.Min(660f, Math.Max(360f, displaySize.X - 96f)),
            Math.Min(430f, Math.Max(360f, displaySize.Y - 96f)));
        ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);
        ImGui.SetNextWindowPos(
            new NumericsVector2(
                Math.Max(24f, (displaySize.X - windowSize.X) * 0.5f),
                Math.Max(24f, (displaySize.Y - windowSize.Y) * 0.5f)),
            ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(1f);

        bool open = uploadPackWindowOpen;
        PushCommunityPackWindowStyle();
        if (ImGui.Begin(
            "Upload Pack##akron_community_pack_upload",
            ref open,
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings)) {
            DrawCommunityPackUploadContents("window");
        }

        ImGui.End();
        PopCommunityPackWindowStyle();
        uploadPackWindowOpen = open;
    }

    private void DrawCommunityPackUploadContents(string popupId) {
        Level level = Scene as Level;
        if (level == null) {
            ImGui.TextWrapped("Open Upload Pack while inside a map so Akron can attach the current map SID and capture the full map.");
            return;
        }

        AkronModule.Settings.CommunityPackUploadSection = AkronCommunityPackUploads.NormalizeUploadSection(AkronModule.Settings.CommunityPackUploadSection);
        string mapSid = level.Session?.Area.GetSID() ?? string.Empty;
        string mapName = string.IsNullOrWhiteSpace(mapSid) ? "No active map" : mapSid;
        AkronCommunityPackUploadDraft draft = AkronCommunityPackUploads.BuildDraft(
            level,
            AkronModule.Settings.CommunityPackUploadSection,
            AkronModule.Settings.CommunityPackUploadUseDiscordAttribution);

        DrawCommunityPackUploadForm(mapName, popupId);
        DrawCommunityPackUploadSummary(level, draft);
    }

    private void DrawCommunityPackUploadForm(string mapName, string popupId) {
        ImGui.TextDisabled("Map:");
        ImGui.SameLine();
        TextWrappedLiteral(mapName);

        ImGui.Spacing();
        ImGui.TextDisabled("Category:");
        ImGui.SameLine();
        DrawCommunityPackUploadSectionChoice("StartPos", AkronSetupSection.StartPos, popupId);

        ImGui.Spacing();
        ImGui.TextDisabled("Attribution:");
        ImGui.SameLine();
        DrawCommunityPackUploadAttributionChoice("Anonymous", false, popupId);
        ImGui.SameLine();
        DrawCommunityPackUploadAttributionChoice("Discord", true, popupId);

        if (AkronModule.Settings.CommunityPackUploadUseDiscordAttribution) {
            string discordUserId = AkronModule.Settings.CommunityPackUploadDiscordUserId ?? string.Empty;
            DrawPopupRowLabel("Discord ID", 92f);
            ImGui.PushItemWidth(Math.Max(220f, ImGui.GetContentRegionAvail().X - 108f));
            if (ImGui.InputTextWithHint("##upload-discord-id-" + popupId, "Your Discord user ID", ref discordUserId, 32)) {
                AkronModule.Settings.CommunityPackUploadDiscordUserId = discordUserId.Trim();
                MarkValueEditFreeze();
            }
            if (ImGui.IsItemActive()) {
                MarkValueEditFreeze();
            }
            ImGui.PopItemWidth();
            DrawPopupTooltip("Saved for future Upload Pack submissions and also editable from the row submenu.");
        }

        ImGui.Spacing();
        string title = AkronModule.Settings.CommunityPackUploadTitleOverride ?? string.Empty;
        DrawPopupRowLabel("Title", 92f);
        ImGui.PushItemWidth(Math.Max(220f, ImGui.GetContentRegionAvail().X - 108f));
        if (ImGui.InputTextWithHint("##upload-title-" + popupId, "Generated from the current map", ref title, 120)) {
            AkronModule.Settings.CommunityPackUploadTitleOverride = title.Trim();
            MarkValueEditFreeze();
        }
        if (ImGui.IsItemActive()) {
            MarkValueEditFreeze();
        }
        ImGui.PopItemWidth();

        string description = AkronModule.Settings.CommunityPackUploadDescriptionOverride ?? string.Empty;
        DrawPopupRowLabel("Desc", 92f);
        ImGui.PushItemWidth(Math.Max(220f, ImGui.GetContentRegionAvail().X - 108f));
        if (ImGui.InputTextWithHint("##upload-description-" + popupId, "Generated from the current map", ref description, 240)) {
            AkronModule.Settings.CommunityPackUploadDescriptionOverride = description.Trim();
            MarkValueEditFreeze();
        }
        if (ImGui.IsItemActive()) {
            MarkValueEditFreeze();
        }
        ImGui.PopItemWidth();

        if (ImGui.Button("Use Generated Text##upload-generated-" + popupId, new NumericsVector2(160f, 28f))) {
            AkronModule.Settings.CommunityPackUploadTitleOverride = string.Empty;
            AkronModule.Settings.CommunityPackUploadDescriptionOverride = string.Empty;
        }
        DrawPopupTooltip("Blank title and description fields use generated text for the current map and category.");

        ImGui.Separator();
        bool acceptedTerms = AkronModule.Settings.CommunityPackUploadAcceptedTermsVersion >= AkronCommunityPackUploads.CurrentTermsVersion;
        if (ImGui.Checkbox("I have permission to share this pack##upload-terms-" + popupId, ref acceptedTerms)) {
            AkronModule.Settings.CommunityPackUploadAcceptedTermsVersion = acceptedTerms ? AkronCommunityPackUploads.CurrentTermsVersion : 0;
        }
        ImGui.TextWrapped("Uploads are reviewed in Discord before publication. Akron captures the full map automatically.");
    }

    private void DrawCommunityPackUploadSummary(Level level, AkronCommunityPackUploadDraft draft) {
        ImGui.Separator();
        TextDisabledLiteral("Preview: " + draft.Title);
        TextDisabledLiteral("Map: " + (string.IsNullOrWhiteSpace(draft.MapSid) ? "No active map" : draft.MapSid) + " - Capture: full map - Install ID: private");
        if (AkronModule.Settings.CommunityPackUploadUseDiscordAttribution) {
            TextDisabledLiteral(string.IsNullOrWhiteSpace(AkronModule.Settings.CommunityPackUploadDiscordUserId)
                ? "Discord user: required for Discord attribution"
                : "Discord user: saved");
        }

        ImGui.Spacing();
        bool busy = AkronCommunityPackUploads.IsUploadInProgress || AkronScreenshotScanner.IsScanning;
        string buttonLabel = busy ? "Uploading..." : "Submit Upload";
        if (ImGui.Button(buttonLabel + "##upload-submit-window", new NumericsVector2(148f, 30f))) {
            AkronCommunityPackUploads.OpenUploadPrompt(level);
            if (AkronCommunityPackUploads.IsUploadInProgress || AkronScreenshotScanner.IsScanning) {
                uploadPackWindowOpen = false;
            }
        }
        DrawPopupTooltip("Create the .akr pack, capture the full map, and submit both for Discord review.");
    }

    private static void DrawCommunityPackUploadSectionChoice(string label, AkronSetupSection section, string popupId) {
        bool selected = AkronModule.Settings.CommunityPackUploadSection == section;
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? ToImGuiColor(0xC92735, 1f) : ToImGuiColor(0x2F2F2F, 0.75f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ToImGuiColor(0xE03745, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ToImGuiColor(0xF04C58, 1f));
        if (ImGui.Button(label + "##upload-section-" + popupId, new NumericsVector2(96f, 28f))) {
            AkronModule.Settings.CommunityPackUploadSection = section;
        }
        ImGui.PopStyleColor(3);
    }

    private static void DrawCommunityPackUploadAttributionChoice(string label, bool useDiscord, string popupId) {
        bool selected = AkronModule.Settings.CommunityPackUploadUseDiscordAttribution == useDiscord;
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? ToImGuiColor(0xC92735, 1f) : ToImGuiColor(0x2F2F2F, 0.75f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ToImGuiColor(0xE03745, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ToImGuiColor(0xF04C58, 1f));
        if (ImGui.Button(label + "##upload-attribution-" + popupId, new NumericsVector2(104f, 28f))) {
            AkronModule.Settings.CommunityPackUploadUseDiscordAttribution = useDiscord;
        }
        ImGui.PopStyleColor(3);
    }
}
