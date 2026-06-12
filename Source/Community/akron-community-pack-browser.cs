using System;
using System.Collections.Generic;
using System.Globalization;
using Celeste;
using ImGuiNET;
using Monocle;
using NumericsVector2 = System.Numerics.Vector2;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawCommunityPackBrowserWindow() {
        if (!communityPackBrowserOpen) {
            return;
        }

        NumericsVector2 displaySize = ImGui.GetIO().DisplaySize;
        NumericsVector2 windowSize = new NumericsVector2(
            Math.Min(1040f, Math.Max(360f, displaySize.X - 96f)),
            Math.Min(560f, Math.Max(360f, displaySize.Y - 96f)));
        ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);
        ImGui.SetNextWindowPos(
            new NumericsVector2(
                Math.Max(24f, (displaySize.X - windowSize.X) * 0.5f),
                Math.Max(24f, (displaySize.Y - windowSize.Y) * 0.5f)),
            ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(1f);

        bool open = communityPackBrowserOpen;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new NumericsVector2(14f, 12f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new NumericsVector2(8f, 5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 3f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, ToImGuiColor(0x252525, 0.94f));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, ToImGuiColor(0xC92735, 1f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, ToImGuiColor(0xE03745, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, ToImGuiColor(0x303030, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, ToImGuiColor(0x3A3A3A, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, ToImGuiColor(0x464646, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, ToImGuiColor(0xC92735, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ToImGuiColor(0xE03745, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ToImGuiColor(0xF04C58, 1f));
        ImGui.PushStyleColor(ImGuiCol.Header, ToImGuiColor(0xC92735, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ToImGuiColor(0xE03745, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, ToImGuiColor(0xF04C58, 1f));
        if (ImGui.Begin(
            "Community Packs##akron_community_pack_catalog",
            ref open,
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings)) {
            DrawCommunityPackBrowserContents("window");
        }

        ImGui.End();
        ImGui.PopStyleColor(12);
        ImGui.PopStyleVar(5);
        communityPackBrowserOpen = open;
    }

    private void DrawCommunityPackBrowserContents(string popupId) {
        if (AkronCommunityPacks.CompleteDownloadIfReady(out AkronCommunityPackEntry downloaded, out string downloadedPath, out string downloadMessage)) {
            bool imported = downloaded != null && AkronSetupPacks.Import(downloadedPath, downloaded.Section);
            Engine.Scene?.Add(new AkronToast(imported ? "Imported " + downloaded.Title + "." : "Community import failed."));
        } else if (!string.IsNullOrWhiteSpace(downloadMessage)) {
            Engine.Scene?.Add(new AkronToast("Community import failed."));
        }

        float fullWidth = ImGui.GetContentRegionAvail().X;
        string indexUrl = AkronCommunityPacks.ResolveIndexUrl(AkronModule.Settings.CommunityPackIndexUrl);
        DrawPopupRowLabel("Index URL", 92f);
        ImGui.PushItemWidth(Math.Max(220f, fullWidth - 108f));
        if (ImGui.InputTextWithHint("##community-index-" + popupId, "file:///... or https://...", ref indexUrl, 512)) {
            AkronModule.Settings.CommunityPackIndexUrl = indexUrl;
            MarkValueEditFreeze();
        }
        if (ImGui.IsItemActive()) {
            MarkValueEditFreeze();
        }
        ImGui.PopItemWidth();

        string query = AkronModule.Settings.CommunityPackSearchQuery ?? string.Empty;
        DrawPopupRowLabel("Search", 92f);
        ImGui.PushItemWidth(Math.Max(220f, fullWidth - 108f));
        if (ImGui.InputTextWithHint("##community-search-" + popupId, "Search map packs", ref query, 80)) {
            AkronModule.Settings.CommunityPackSearchQuery = query;
            selectedCommunityPackIndex = 0;
            MarkValueEditFreeze();
        }
        if (ImGui.IsItemActive()) {
            MarkValueEditFreeze();
        }
        ImGui.PopItemWidth();

        ImGui.Spacing();
        ImGui.TextDisabled("Category:");
        ImGui.SameLine();
        DrawCommunityPackSectionChoice("All", AkronSetupSection.Whole, popupId);
        ImGui.SameLine();
        DrawCommunityPackSectionChoice("StartPos", AkronSetupSection.StartPos, popupId);
        ImGui.SameLine();
        DrawCommunityPackSectionChoice("Auto Kill", AkronSetupSection.AutoKill, popupId);
        ImGui.SameLine();
        DrawCommunityPackSectionChoice("Auto Deafen", AkronSetupSection.AutoDeafen, popupId);

        string mapSid = CurrentCommunityMapSid();
        AkronCommunityPackFilter filter = new AkronCommunityPackFilter {
            MapSid = mapSid,
            Section = AkronModule.Settings.CommunityPackSection,
            Query = AkronModule.Settings.CommunityPackSearchQuery
        };

        ImGui.SameLine();
        if (ImGui.Button((AkronCommunityPacks.RefreshInProgress ? "Refreshing..." : "Refresh") + "##community-packs-" + popupId)) {
            AkronCommunityPacks.BeginRefresh(AkronModule.Settings.CommunityPackIndexUrl);
            selectedCommunityPackIndex = 0;
        }
        DrawPopupTooltip("Connect to the configured index and reload map-specific packs.");

        AkronCommunityPackSearchResult result = AkronCommunityPacks.Search(filter);
        ImGui.SameLine();
        TextDisabledLiteral(string.IsNullOrWhiteSpace(mapSid) ? "No active map." : "Map: " + mapSid);
        TextDisabledLiteral(result.Status);
        if (result.FetchedUtc.HasValue) {
            ImGui.SameLine();
            ImGui.TextDisabled("Updated: " + result.FetchedUtc.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC");
        }

        if (string.IsNullOrWhiteSpace(mapSid)) {
            ImGui.Separator();
            ImGui.TextWrapped("Open this browser while inside a map so Akron can filter packs to the current map SID.");
            return;
        }

        IReadOnlyList<AkronCommunityPackEntry> entries = result.Entries;
        if (entries.Count == 0) {
            ImGui.Separator();
            ImGui.TextWrapped("No packs found for this map and filter.");
            return;
        }

        selectedCommunityPackIndex = Calc.Clamp(selectedCommunityPackIndex, 0, entries.Count - 1);
        float availableWidth = Math.Max(320f, ImGui.GetContentRegionAvail().X);
        float availableHeight = Math.Max(260f, ImGui.GetContentRegionAvail().Y);
        float listWidth = Math.Min(Math.Max(460f, availableWidth * 0.58f), availableWidth - 260f);

        ImGui.Columns(2, "##community-pack-browser-columns-" + popupId, false);
        ImGui.SetColumnWidth(0, listWidth);
        ImGui.BeginChild("##community-pack-list-" + popupId, new NumericsVector2(0f, availableHeight), ImGuiChildFlags.None);
        for (int index = 0; index < entries.Count; index++) {
            AkronCommunityPackEntry pack = entries[index];
            if (DrawCommunityPackCard(pack, selectedCommunityPackIndex == index, popupId, index)) {
                selectedCommunityPackIndex = index;
            }
        }
        ImGui.EndChild();

        ImGui.NextColumn();
        ImGui.BeginChild("##community-pack-details-" + popupId, new NumericsVector2(0f, availableHeight), ImGuiChildFlags.None);
        DrawCommunityPackDetails(entries[selectedCommunityPackIndex], popupId);
        ImGui.EndChild();

        ImGui.Columns(1);
    }

    private static void DrawCommunityPackSectionChoice(string label, AkronSetupSection section, string popupId) {
        bool selected = AkronModule.Settings.CommunityPackSection == section;
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? ToImGuiColor(0xC92735, 1f) : ToImGuiColor(0x2F2F2F, 0.75f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ToImGuiColor(0xE03745, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ToImGuiColor(0xF04C58, 1f));
        if (ImGui.Button(label + "##community-section-" + popupId, new NumericsVector2(96f, 28f))) {
            AkronModule.Settings.CommunityPackSection = section;
        }
        ImGui.PopStyleColor(3);
    }

    private void DrawCommunityPackDetails(AkronCommunityPackEntry pack, string popupId) {
        TextWrappedLiteral(pack.Title);
        ImGui.TextDisabled("Type: " + AkronSetupPacks.FormatSection(pack.Section));
        TextDisabledLiteral("Author: " + (string.IsNullOrWhiteSpace(pack.AuthorName) ? "Unknown" : pack.AuthorName));
        ImGui.Separator();
        TextWrappedLiteral(string.IsNullOrWhiteSpace(pack.Description) ? "No description provided." : pack.Description.Trim());
        ImGui.Spacing();
        TextDisabledLiteral("Image: " + (string.IsNullOrWhiteSpace(pack.ImageUrl) ? "placeholder until pack art is supplied." : pack.ImageUrl));
        TextDisabledLiteral("Avatar: " + (string.IsNullOrWhiteSpace(pack.AuthorAvatarUrl) ? "placeholder until author art is supplied." : pack.AuthorAvatarUrl));
        if (!string.IsNullOrWhiteSpace(pack.MapUrl)) {
            TextWrappedLiteral("Map link: " + pack.MapUrl);
        }

        ImGui.Spacing();
        if (ImGui.Button("Import Selected##community-import-" + popupId, new NumericsVector2(148f, 28f)) &&
            !AkronCommunityPacks.BeginDownload(pack, out string message) &&
            !string.IsNullOrWhiteSpace(message)) {
            Engine.Scene?.Add(new AkronToast(message));
        }
        DrawPopupTooltip("Download and import this .akr pack.");
    }

    private static bool DrawCommunityPackCard(AkronCommunityPackEntry pack, bool selected, string popupId, int index) {
        NumericsVector2 cursor = ImGui.GetCursorScreenPos();
        float width = Math.Max(320f, ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X);
        const float height = 96f;
        const float imageWidth = 116f;
        const float imageHeight = 72f;
        ImGui.InvisibleButton("##community-card-" + popupId + "-" + index, new NumericsVector2(width, height));
        bool clicked = ImGui.IsItemClicked();
        NumericsVector2 min = cursor;
        NumericsVector2 max = new NumericsVector2(cursor.X + width, cursor.Y + height);
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        bool hovered = ImGui.IsItemHovered();
        uint bg = AkronImGuiTheme.ToU32(ToImGuiColor(selected ? 0xC92735 : hovered ? 0x343434 : 0x2C2C2C, selected ? 0.98f : 0.94f));
        uint border = AkronImGuiTheme.ToU32(ToImGuiColor(selected ? 0xF04C58 : 0x3E3E3E, 1f));
        drawList.AddRectFilled(min, max, bg, 3f);
        drawList.AddRect(min, max, border, 3f, ImDrawFlags.None, selected ? 2f : 1f);

        NumericsVector2 imageMin = new NumericsVector2(min.X + 10f, min.Y + 12f);
        NumericsVector2 imageMax = new NumericsVector2(imageMin.X + imageWidth, imageMin.Y + imageHeight);
        drawList.AddRectFilled(imageMin, imageMax, AkronImGuiTheme.ToU32(ToImGuiColor(0x232323, 1f)), 2f);
        if (string.IsNullOrWhiteSpace(pack.ImageUrl)) {
            IntPtr placeholderTexture = AkronImGuiRenderer.GetEmbeddedTextureId("community-pack-placeholder.jpg");
            if (placeholderTexture != IntPtr.Zero) {
                drawList.AddImage(placeholderTexture, imageMin, imageMax);
            } else {
                drawList.AddText(new NumericsVector2(imageMin.X + 16f, imageMin.Y + imageHeight * 0.5f - 8f), AkronImGuiTheme.ToU32(ToImGuiColor(0x778096, 1f)), "No image");
            }
        } else {
            drawList.AddText(new NumericsVector2(imageMin.X + 16f, imageMin.Y + imageHeight * 0.5f - 8f), AkronImGuiTheme.ToU32(ToImGuiColor(0x778096, 1f)), "Pack image");
        }

        NumericsVector2 avatarCenter = new NumericsVector2(max.X - 28f, min.Y + 28f);
        drawList.AddCircleFilled(avatarCenter, 15f, AkronImGuiTheme.ToU32(ToImGuiColor(0x222222, 1f)));
        string initials = BuildCommunityPackInitials(pack.AuthorName);
        drawList.AddText(new NumericsVector2(avatarCenter.X - 8f, avatarCenter.Y - 8f), AkronImGuiTheme.ToU32(ToImGuiColor(0xFFFFFF, 1f)), initials);

        float textX = imageMax.X + 12f;
        float textMaxWidth = Math.Max(80f, max.X - textX - 64f);
        string metadata = AkronSetupPacks.FormatSection(pack.Section) + " - " +
                          (string.IsNullOrWhiteSpace(pack.AuthorName) ? "Unknown" : pack.AuthorName) + " - " +
                          FormatCommunityPackUpdated(pack);
        drawList.AddText(new NumericsVector2(textX, min.Y + 16f), AkronImGuiTheme.ToU32(ToImGuiColor(0xFFFFFF, 1f)), TruncateImGuiText(pack.Title, (int) textMaxWidth));
        drawList.AddText(new NumericsVector2(textX, min.Y + 40f), AkronImGuiTheme.ToU32(ToImGuiColor(selected ? 0xB9CADF : 0xA5ADBE, 1f)), TruncateImGuiText(metadata, (int) textMaxWidth));
        drawList.AddText(new NumericsVector2(textX, min.Y + 64f), AkronImGuiTheme.ToU32(ToImGuiColor(selected ? 0xC8D2DE : 0xA5ADBE, 1f)), TruncateImGuiText(string.IsNullOrWhiteSpace(pack.Description) ? "No description provided." : pack.Description, (int) textMaxWidth));
        return clicked;
    }

    private static void TextWrappedLiteral(string text) {
        ImGui.TextWrapped(EscapeImGuiFormat(text));
    }

    private static void TextDisabledLiteral(string text) {
        ImGui.TextDisabled(EscapeImGuiFormat(text));
    }

    private static string EscapeImGuiFormat(string text) {
        return (text ?? string.Empty).Replace("%", "%%");
    }

    private static string FormatCommunityPackUpdated(AkronCommunityPackEntry pack) {
        if (DateTime.TryParse(pack?.UpdatedUtc ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime updated)) {
            TimeSpan age = DateTime.UtcNow - updated.ToUniversalTime();
            if (age.TotalDays >= 14d) {
                return updated.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            if (age.TotalDays >= 1d) {
                return ((int) Math.Floor(age.TotalDays)).ToString(CultureInfo.InvariantCulture) + "d ago";
            }

            if (age.TotalHours >= 1d) {
                return ((int) Math.Floor(age.TotalHours)).ToString(CultureInfo.InvariantCulture) + "h ago";
            }

            return "updated today";
        }

        return "updated recently";
    }

    private static string BuildCommunityPackInitials(string authorName) {
        string value = (authorName ?? string.Empty).Trim();
        if (value.Length == 0) {
            return "?";
        }

        string[] parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2) {
            return (parts[0][0].ToString() + parts[1][0].ToString()).ToUpperInvariant();
        }

        return value.Substring(0, Math.Min(2, value.Length)).ToUpperInvariant();
    }
}
