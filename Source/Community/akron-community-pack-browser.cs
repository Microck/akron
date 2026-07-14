using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Celeste;
using ImGuiNET;
using Monocle;
using NumericsVector2 = System.Numerics.Vector2;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private const int MaxCommunityPackPreviewImageBytes = 2 * 1024 * 1024;
    private const int MaxCommunityPackPreviewImageDimension = 2048;
    private const long MaxCommunityPackPreviewImagePixels = 4L * 1024 * 1024;
    private const int MaxCommunityPackPreviewImageCacheEntries = 8;
    private const long MaxCommunityPackPreviewDecodedBytes = 64L * 1024L * 1024L;
    private static readonly SemaphoreSlim CommunityPackPreviewImageDownloadSlots = new SemaphoreSlim(2, 2);
    private static readonly HttpClient CommunityPackPreviewImageHttp = AkronCommunityPacks.CreateSafeHttpClient(TimeSpan.FromSeconds(8));
    private static readonly Dictionary<string, AkronCommunityPackPreviewImageState> CommunityPackPreviewImages = new Dictionary<string, AkronCommunityPackPreviewImageState>(StringComparer.Ordinal);
    private static long communityPackPreviewAccessCounter;

    private static void PushCommunityPackWindowStyle() {
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
    }

    private static void PopCommunityPackWindowStyle() {
        ImGui.PopStyleColor(12);
        ImGui.PopStyleVar(5);
    }

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
            selectedCommunityPackImageIndex = 0;
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
            selectedCommunityPackImageIndex = 0;
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
                if (selectedCommunityPackIndex != index) {
                    selectedCommunityPackIndex = index;
                    selectedCommunityPackImageIndex = 0;
                }
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
        DrawCommunityPackPreviewCarousel(pack, popupId);
        if (!string.IsNullOrWhiteSpace(pack.MapUrl)) {
            TextWrappedLiteral("Map link: " + pack.MapUrl);
        }

        ImGui.Spacing();
        bool importInProgress = AkronCommunityPacks.DownloadInProgress;
        if (importInProgress) {
            ImGui.BeginDisabled();
        }
        if (ImGui.Button((importInProgress ? "Importing..." : "Import Selected") + "##community-import-" + popupId, new NumericsVector2(148f, 28f))) {
            AkronCommunityPacks.BeginDownload(pack, out string message);
            if (!string.IsNullOrWhiteSpace(message)) {
                Engine.Scene?.Add(new AkronToast(message));
            }
        }
        if (importInProgress) {
            ImGui.EndDisabled();
        }
        DrawPopupTooltip(importInProgress ? "The selected pack is being downloaded and imported." : "Download and import this .akr pack.");
    }

    private void DrawCommunityPackPreviewCarousel(AkronCommunityPackEntry pack, string popupId) {
        IReadOnlyList<AkronCommunityPackImage> images = AkronCommunityPacks.GetPreviewImages(pack);
        selectedCommunityPackImageIndex = images.Count == 0 ? 0 : Calc.Clamp(selectedCommunityPackImageIndex, 0, images.Count - 1);
        if (images.Count == 0) {
            TextDisabledLiteral("Image: placeholder until pack art is supplied.");
            return;
        }

        AkronCommunityPackImage image = images[selectedCommunityPackImageIndex];
        string roomLabel = string.IsNullOrWhiteSpace(image.RoomName) ? "Preview" : image.RoomName;
        TextDisabledLiteral("Image: " + roomLabel + " (" + (selectedCommunityPackImageIndex + 1).ToString(CultureInfo.InvariantCulture) + "/" + images.Count.ToString(CultureInfo.InvariantCulture) + ")");
        NumericsVector2 imageSize = new NumericsVector2(Math.Min(260f, Math.Max(180f, ImGui.GetContentRegionAvail().X)), 146f);
        if (!DrawCommunityPackPreviewImage(pack, image.Url, imageSize)) {
            TextDisabledLiteral(image.Url);
        }
        if (images.Count <= 1) {
            return;
        }

        if (ImGui.Button("<##community-image-prev-" + popupId, new NumericsVector2(32f, 24f))) {
            selectedCommunityPackImageIndex = (selectedCommunityPackImageIndex + images.Count - 1) % images.Count;
        }
        ImGui.SameLine();
        if (ImGui.Button(">##community-image-next-" + popupId, new NumericsVector2(32f, 24f))) {
            selectedCommunityPackImageIndex = (selectedCommunityPackImageIndex + 1) % images.Count;
        }
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
        IReadOnlyList<AkronCommunityPackImage> previewImages = AkronCommunityPacks.GetPreviewImages(pack);
        if (previewImages.Count == 0) {
            IntPtr placeholderTexture = AkronImGuiRenderer.GetEmbeddedTextureId("community-pack-placeholder.jpg");
            if (placeholderTexture != IntPtr.Zero) {
                drawList.AddImage(placeholderTexture, imageMin, imageMax);
            } else {
                drawList.AddText(new NumericsVector2(imageMin.X + 16f, imageMin.Y + imageHeight * 0.5f - 8f), AkronImGuiTheme.ToU32(ToImGuiColor(0x778096, 1f)), "No image");
            }
        } else if (!DrawCommunityPackPreviewImage(
                       drawList,
                       pack,
                       previewImages[0].Url,
                       imageMin,
                       imageMax,
                       ShouldScheduleCommunityPackPreview(selectedCard: selected, detailView: false, visible: ImGui.IsRectVisible(imageMin, imageMax)))) {
            drawList.AddText(new NumericsVector2(imageMin.X + 16f, imageMin.Y + imageHeight * 0.5f - 8f), AkronImGuiTheme.ToU32(ToImGuiColor(0x778096, 1f)), "Pack image");
        }

        NumericsVector2 avatarMin = new NumericsVector2(max.X - 43f, min.Y + 13f);
        NumericsVector2 avatarMax = new NumericsVector2(avatarMin.X + 30f, avatarMin.Y + 30f);
        drawList.AddRectFilled(avatarMin, avatarMax, AkronImGuiTheme.ToU32(ToImGuiColor(0x222222, 1f)), 3f);
        bool avatarLoaded = TryDrawCommunityPackImage(
            drawList,
            pack,
            pack.AuthorAvatarUrl,
            "Author avatar",
            avatarMin,
            avatarMax,
            selected && ImGui.IsRectVisible(avatarMin, avatarMax),
            out _);
        if (!avatarLoaded) {
            string initials = BuildCommunityPackInitials(pack.AuthorName);
            NumericsVector2 initialsSize = ImGui.CalcTextSize(initials);
            drawList.AddText(
                new NumericsVector2(avatarMin.X + (30f - initialsSize.X) * 0.5f, avatarMin.Y + (30f - initialsSize.Y) * 0.5f),
                AkronImGuiTheme.ToU32(ToImGuiColor(0xFFFFFF, 1f)),
                initials);
        }
        drawList.AddRect(avatarMin, avatarMax, AkronImGuiTheme.ToU32(ToImGuiColor(0x4A4A4A, 1f)), 3f);

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

    private static bool DrawCommunityPackPreviewImage(AkronCommunityPackEntry pack, string url, NumericsVector2 size) {
        if (string.IsNullOrWhiteSpace(url)) {
            return false;
        }

        NumericsVector2 min = ImGui.GetCursorScreenPos();
        NumericsVector2 max = new NumericsVector2(min.X + size.X, min.Y + size.Y);
        ImGui.Dummy(size);
        bool visible = ImGui.IsRectVisible(min, max);
        return DrawCommunityPackPreviewImage(
            ImGui.GetWindowDrawList(),
            pack,
            url,
            min,
            max,
            ShouldScheduleCommunityPackPreview(selectedCard: false, detailView: true, visible: visible));
    }

    private static bool DrawCommunityPackPreviewImage(ImDrawListPtr drawList, AkronCommunityPackEntry pack, string url, NumericsVector2 min, NumericsVector2 max, bool scheduleDownload) {
        if (string.IsNullOrWhiteSpace(url)) {
            return false;
        }

        drawList.AddRectFilled(min, max, AkronImGuiTheme.ToU32(ToImGuiColor(0x232323, 1f)), 2f);
        if (!scheduleDownload) {
            return false;
        }
        if (TryDrawCommunityPackImage(drawList, pack, url, "Preview image", min, max, scheduleDownload, out string error)) {
            return true;
        }

        string label = string.IsNullOrWhiteSpace(error) ? "Loading image" : "Image unavailable";
        NumericsVector2 labelSize = ImGui.CalcTextSize(label);
        drawList.AddText(
            new NumericsVector2(min.X + Math.Max(8f, ((max.X - min.X) - labelSize.X) * 0.5f), min.Y + Math.Max(8f, ((max.Y - min.Y) - labelSize.Y) * 0.5f)),
            AkronImGuiTheme.ToU32(ToImGuiColor(0x778096, 1f)),
            label);
        return true;
    }

    private static bool TryDrawCommunityPackImage(
        ImDrawListPtr drawList,
        AkronCommunityPackEntry pack,
        string url,
        string resourceLabel,
        NumericsVector2 min,
        NumericsVector2 max,
        bool scheduleDownload,
        out string error) {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(url) || !scheduleDownload) {
            return false;
        }

        AkronCommunityPackPreviewImageState state = GetCommunityPackPreviewImageState(pack, url, resourceLabel);
        CompleteCommunityPackPreviewImage(state);
        error = state.Error;
        if (state.TextureId == IntPtr.Zero) {
            return false;
        }

        drawList.AddImage(state.TextureId, min, max);
        return true;
    }

    internal static bool ShouldScheduleCommunityPackPreview(bool selectedCard, bool detailView, bool visible) {
        return visible && (selectedCard || detailView);
    }

    internal static string CommunityPackPreviewImageCacheKey(string url, string resourceLabel) {
        return resourceLabel + "\n" + url.Trim();
    }

    private static AkronCommunityPackPreviewImageState GetCommunityPackPreviewImageState(AkronCommunityPackEntry pack, string url, string resourceLabel) {
        url = url.Trim();
        string cacheKey = CommunityPackPreviewImageCacheKey(url, resourceLabel);
        if (CommunityPackPreviewImages.TryGetValue(cacheKey, out AkronCommunityPackPreviewImageState state)) {
            state.LastAccess = ++communityPackPreviewAccessCounter;
            return state;
        }

        EvictCommunityPackPreviewImageIfNeeded();
        if (CommunityPackPreviewImages.Count >= MaxCommunityPackPreviewImageCacheEntries) {
            return new AkronCommunityPackPreviewImageState {
                Url = url,
                Error = "Preview image queue is full.",
                TextureFailed = true,
                LastAccess = ++communityPackPreviewAccessCounter
            };
        }

        state = new AkronCommunityPackPreviewImageState {
            Url = url,
            LastAccess = ++communityPackPreviewAccessCounter,
            DownloadTask = Task.Run(() => ReadCommunityPackPreviewImageBytes(pack, url, resourceLabel))
        };
        CommunityPackPreviewImages[cacheKey] = state;
        return state;
    }

    private static void CompleteCommunityPackPreviewImage(AkronCommunityPackPreviewImageState state) {
        if (state == null || state.TextureId != IntPtr.Zero || state.TextureFailed) {
            return;
        }

        if (state.Bytes == null && state.DownloadTask?.IsCompleted == true) {
            try {
                state.Bytes = state.DownloadTask.GetAwaiter().GetResult();
            } catch (Exception exception) when (exception is IOException || exception is HttpRequestException || exception is TaskCanceledException || exception is UnauthorizedAccessException || exception is InvalidDataException || exception is ArgumentException || exception is FormatException) {
                state.Error = exception.Message;
                state.TextureFailed = true;
                return;
            }
        }

        if (state.Bytes == null) {
            return;
        }

        if (!TryValidateCommunityPackPreviewImage(state.Bytes, out int width, out int height, out string validationError)) {
            state.Bytes = null;
            state.Error = validationError;
            state.TextureFailed = true;
            return;
        }
        long decodedBytes = checked((long) width * height * 4L);
        if (!EvictCommunityPackPreviewImagesForBudget(state, decodedBytes)) {
            state.Bytes = null;
            state.Error = "Preview image cache is full.";
            state.TextureFailed = true;
            return;
        }

        state.TextureId = AkronImGuiRenderer.GetTextureIdFromBytes("community-pack-preview:" + state.Url, state.Bytes);
        state.Bytes = null;
        if (state.TextureId == IntPtr.Zero) {
            state.Error = "Could not load preview image.";
            state.TextureFailed = true;
        } else {
            state.DecodedBytes = decodedBytes;
        }
    }

    private static byte[] ReadCommunityPackPreviewImageBytes(AkronCommunityPackEntry pack, string url, string resourceLabel) {
        CommunityPackPreviewImageDownloadSlots.Wait();
        try {
            Uri uri = AkronCommunityPacks.ResolveCatalogResourceUri(pack, url, resourceLabel);
            byte[] bytes;
            if (uri.Scheme == Uri.UriSchemeFile) {
                bytes = AkronCommunityPacks.ReadFileBytesCapped(
                    uri.LocalPath,
                    MaxCommunityPackPreviewImageBytes,
                    "Preview image is too large.");
            } else {
                using HttpResponseMessage response = CommunityPackPreviewImageHttp.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                if ((int)response.StatusCode is >= 300 and < 400) {
                    throw new InvalidDataException("Preview image redirects are not allowed.");
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxCommunityPackPreviewImageBytes) {
                    throw new InvalidDataException("Preview image is too large.");
                }

                using Stream stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                using MemoryStream buffer = new MemoryStream(Math.Min(MaxCommunityPackPreviewImageBytes, 81920));
                byte[] chunk = new byte[81920];
                int total = 0;
                while (true) {
                    int read = stream.Read(chunk, 0, Math.Min(chunk.Length, MaxCommunityPackPreviewImageBytes + 1 - total));
                    if (read == 0) {
                        bytes = buffer.ToArray();
                        break;
                    }

                    total += read;
                    if (total > MaxCommunityPackPreviewImageBytes) {
                        throw new InvalidDataException("Preview image is too large.");
                    }
                    buffer.Write(chunk, 0, read);
                }
            }

            if (!TryValidateCommunityPackPreviewImage(bytes, out _, out _, out string error)) {
                throw new InvalidDataException(error);
            }
            return bytes;
        } finally {
            CommunityPackPreviewImageDownloadSlots.Release();
        }
    }

    private static void EvictCommunityPackPreviewImageIfNeeded() {
        if (CommunityPackPreviewImages.Count < MaxCommunityPackPreviewImageCacheEntries) {
            return;
        }

        KeyValuePair<string, AkronCommunityPackPreviewImageState>? oldest = null;
        foreach (KeyValuePair<string, AkronCommunityPackPreviewImageState> candidate in CommunityPackPreviewImages) {
            if (candidate.Value.DownloadTask?.IsCompleted != true ||
                (oldest.HasValue && candidate.Value.LastAccess >= oldest.Value.Value.LastAccess)) {
                continue;
            }
            oldest = candidate;
        }

        if (!oldest.HasValue) {
            return;
        }

        AkronCommunityPackPreviewImageState state = oldest.Value.Value;
        if (state.TextureId != IntPtr.Zero) {
            AkronImGuiRenderer.ReleaseTextureId("community-pack-preview:" + state.Url);
        }
        CommunityPackPreviewImages.Remove(oldest.Value.Key);
    }

    private static bool EvictCommunityPackPreviewImagesForBudget(AkronCommunityPackPreviewImageState incoming, long incomingDecodedBytes) {
        if (incomingDecodedBytes <= 0 || incomingDecodedBytes > MaxCommunityPackPreviewDecodedBytes) {
            return false;
        }

        while (CommunityPackPreviewImages.Values
            .Where(state => !ReferenceEquals(state, incoming))
            .Sum(state => state.DecodedBytes) > MaxCommunityPackPreviewDecodedBytes - incomingDecodedBytes) {
            KeyValuePair<string, AkronCommunityPackPreviewImageState> oldest = CommunityPackPreviewImages
                .Where(candidate => !ReferenceEquals(candidate.Value, incoming) && candidate.Value.DecodedBytes > 0)
                .OrderBy(candidate => candidate.Value.LastAccess)
                .FirstOrDefault();
            if (oldest.Value == null) {
                return false;
            }

            AkronCommunityPackPreviewImageState state = oldest.Value;
            if (state.TextureId != IntPtr.Zero) {
                AkronImGuiRenderer.ReleaseTextureId("community-pack-preview:" + state.Url);
            }
            CommunityPackPreviewImages.Remove(oldest.Key);
        }
        return true;
    }

    internal static bool TryValidateCommunityPackPreviewImage(byte[] bytes, out int width, out int height, out string error) {
        width = 0;
        height = 0;
        error = string.Empty;
        if (bytes == null || bytes.Length == 0 || bytes.Length > MaxCommunityPackPreviewImageBytes ||
            !TryReadCommunityPackPreviewDimensions(bytes, out width, out height)) {
            error = bytes?.Length > MaxCommunityPackPreviewImageBytes ? "Preview image is too large." : "Preview image format is unsupported or invalid.";
            return false;
        }

        if (width <= 0 || height <= 0 || width > MaxCommunityPackPreviewImageDimension || height > MaxCommunityPackPreviewImageDimension ||
            (long)width * height > MaxCommunityPackPreviewImagePixels) {
            error = "Preview image dimensions are too large.";
            return false;
        }
        return true;
    }

    private static bool TryReadCommunityPackPreviewDimensions(byte[] bytes, out int width, out int height) {
        width = 0;
        height = 0;
        if (bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47 &&
            bytes[12] == 0x49 && bytes[13] == 0x48 && bytes[14] == 0x44 && bytes[15] == 0x52) {
            width = ReadBigEndianInt32(bytes, 16);
            height = ReadBigEndianInt32(bytes, 20);
            return true;
        }

        if (bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xd8) {
            return TryReadJpegDimensions(bytes, out width, out height);
        }
        return false;
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) {
        return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    private static bool TryReadJpegDimensions(byte[] bytes, out int width, out int height) {
        width = 0;
        height = 0;
        int offset = 2;
        while (offset + 8 < bytes.Length) {
            if (bytes[offset] != 0xff) {
                offset++;
                continue;
            }
            byte marker = bytes[offset + 1];
            offset += 2;
            if (marker is 0xd8 or 0xd9 || marker is >= 0xd0 and <= 0xd7) {
                continue;
            }
            if (offset + 2 > bytes.Length) {
                return false;
            }
            int segmentLength = (bytes[offset] << 8) | bytes[offset + 1];
            if (segmentLength < 2 || offset + segmentLength > bytes.Length) {
                return false;
            }
            if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf) {
                if (segmentLength < 7) {
                    return false;
                }
                height = (bytes[offset + 3] << 8) | bytes[offset + 4];
                width = (bytes[offset + 5] << 8) | bytes[offset + 6];
                return true;
            }
            offset += segmentLength;
        }
        return false;
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

    private sealed class AkronCommunityPackPreviewImageState {
        public Task<byte[]> DownloadTask { get; set; }
        public byte[] Bytes { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public IntPtr TextureId { get; set; }
        public bool TextureFailed { get; set; }
        public long LastAccess { get; set; }
        public long DecodedBytes { get; set; }
    }
}
