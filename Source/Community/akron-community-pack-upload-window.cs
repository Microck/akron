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
        bool hasStatus = AkronCommunityPackUploads.HasUploadStatus;
        float targetHeight = hasStatus ? 296f : 260f;
        float minimumHeight = hasStatus ? 272f : 236f;
        NumericsVector2 windowSize = new NumericsVector2(
            Math.Min(640f, Math.Max(360f, displaySize.X - 96f)),
            Math.Min(targetHeight, Math.Max(minimumHeight, displaySize.Y - 96f)));
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
        AkronCommunityPackUploadDraft draft = AkronCommunityPackUploads.BuildDraft(
            level,
            AkronModule.Settings.CommunityPackUploadSection,
            AkronModule.Settings.CommunityPackUploadUseDiscordAttribution);

        DrawCommunityPackUploadForm(level, draft, popupId);
    }

    private void DrawCommunityPackUploadForm(Level level, AkronCommunityPackUploadDraft draft, string popupId) {
        const float preferredFieldWidth = 420f;
        float labelWidth = CalculatePopupLabelWidth(preferredFieldWidth, 100f);

        DrawPopupRowLabel("Map", labelWidth);
        TextWrappedLiteral(string.IsNullOrWhiteSpace(draft.MapSid) ? "No active map" : draft.MapSid);

        DrawPopupRowLabel("Category", labelWidth);
        DrawCommunityPackUploadSectionChoice("StartPos", AkronSetupSection.StartPos, popupId);
        ImGui.SameLine();
        DrawCommunityPackUploadSectionChoice("Auto Kill", AkronSetupSection.AutoKill, popupId);
        ImGui.SameLine();
        DrawCommunityPackUploadSectionChoice("Auto Deafen", AkronSetupSection.AutoDeafen, popupId);

        DrawPopupRowLabel("Attribution", labelWidth);
        DrawCommunityPackUploadAttributionChoice("Anonymous", false, popupId);
        ImGui.SameLine();
        DrawCommunityPackUploadAttributionChoice("Discord", true, popupId);

        if (AkronModule.Settings.CommunityPackUploadUseDiscordAttribution) {
            string discordUserId = AkronModule.Settings.CommunityPackUploadDiscordUserId ?? string.Empty;
            DrawPopupRowLabel("Discord ID", labelWidth);
            ImGui.PushItemWidth(CalculatePopupControlWidth(labelWidth, preferredFieldWidth, 220f));
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

        string generatedTitle = AkronCommunityPackUploads.GenerateTitle(draft.MapDisplayName, AkronModule.Settings.CommunityPackUploadSection);
        string title = draft.Title;
        if (DrawCommunityPackUploadTextInput(
            "Title",
            "##upload-title-" + popupId,
            ref title,
            120,
            preferredFieldWidth,
            labelWidth)) {
            AkronModule.Settings.CommunityPackUploadTitleOverride = string.Equals(title.Trim(), generatedTitle, StringComparison.Ordinal)
                ? string.Empty
                : title.Trim();
        }

        string generatedDescription = AkronCommunityPackUploads.GenerateDescription(draft.MapDisplayName, AkronModule.Settings.CommunityPackUploadSection);
        string description = draft.Description;
        if (DrawCommunityPackUploadTextInput(
            "Description",
            "##upload-description-" + popupId,
            ref description,
            240,
            preferredFieldWidth,
            labelWidth)) {
            AkronModule.Settings.CommunityPackUploadDescriptionOverride = string.Equals(description.Trim(), generatedDescription, StringComparison.Ordinal)
                ? string.Empty
                : description.Trim();
        }

        bool busy = AkronCommunityPackUploads.IsUploadInProgress || AkronScreenshotScanner.IsScanning;
        string buttonLabel = busy ? "Uploading..." : "Submit Upload";
        ImGui.Spacing();
        DrawPopupRowLabel("", labelWidth);
        if (busy) {
            ImGui.BeginDisabled();
        }
        if (ImGui.Button(buttonLabel + "##upload-submit-window", new NumericsVector2(148f, 30f))) {
            AkronCommunityPackUploads.OpenUploadPrompt(level);
        }
        if (busy) {
            ImGui.EndDisabled();
        }
        DrawPopupTooltip("Create the .akr pack, capture the full map, and submit both for Discord review.");

        if (AkronCommunityPackUploads.HasUploadStatus) {
            DrawPopupRowLabel("Status", labelWidth);
            ImGui.PushItemWidth(CalculatePopupControlWidth(labelWidth, preferredFieldWidth, 220f));
            ImGui.ProgressBar(
                AkronCommunityPackUploads.UploadProgressFraction,
                new NumericsVector2(-1f, ImGui.GetFrameHeight()),
                AkronCommunityPackUploads.DescribeUploadStatus());
            ImGui.PopItemWidth();
        }
    }

    private static void DrawCommunityPackUploadSectionChoice(string label, AkronSetupSection section, string popupId) {
        bool selected = AkronModule.Settings.CommunityPackUploadSection == section;
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? ToImGuiColor(0xC92735, 1f) : ToImGuiColor(0x2F2F2F, 0.75f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ToImGuiColor(0xE03745, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ToImGuiColor(0xF04C58, 1f));
        float buttonWidth = Math.Max(96f, ImGui.CalcTextSize(label).X + 26f);
        if (ImGui.Button(label + "##upload-section-" + popupId, new NumericsVector2(buttonWidth, 28f))) {
            AkronModule.Settings.CommunityPackUploadSection = section;
        }
        ImGui.PopStyleColor(3);
    }

    private bool DrawCommunityPackUploadTextInput(
        string label,
        string id,
        ref string value,
        int maxLength,
        float preferredFieldWidth,
        float labelWidth) {
        DrawPopupRowLabel(label, labelWidth);
        ImGui.PushItemWidth(CalculatePopupControlWidth(labelWidth, preferredFieldWidth, 220f));
        bool changed = ImGui.InputText(id, ref value, (uint) maxLength);
        if (changed) {
            MarkValueEditFreeze();
        }
        if (ImGui.IsItemActive()) {
            MarkValueEditFreeze();
        }
        ImGui.PopItemWidth();
        return changed;
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
