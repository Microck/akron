using System;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Celeste.Mod.Akron;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class CommunityPackTests {
    [Fact]
    public void LocalFileReaderEnforcesLimitWhileReading()
    {
        string path = Path.Combine(Path.GetTempPath(), "akron-capped-file-" + Guid.NewGuid().ToString("N"));
        try {
            File.WriteAllBytes(path, new byte[1025]);

            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                AkronCommunityPacks.ReadFileBytesCapped(path, 1024, "too large"));

            Assert.Equal("too large", error.Message);
        } finally {
            if (File.Exists(path)) File.Delete(path);
        }
    }
    [Fact]
    public void DefaultIndexUrlUsesOfficialAkronCatalog() {
        Assert.Equal("https://akron.micr.dev/catalog/index.json", AkronCommunityPacks.DefaultIndexUrl);
        Assert.Equal(AkronCommunityPacks.DefaultIndexUrl, new AkronModuleSettings().CommunityPackIndexUrl);
        Assert.Equal(AkronCommunityPacks.DefaultIndexUrl, AkronCommunityPacks.ResolveIndexUrl(string.Empty));
    }

    [Fact]
    public void PlaceholderImageIsEmbeddedForPacksWithoutImages() {
        Assert.Contains(
            typeof(AkronOverlay).Assembly.GetManifestResourceNames(),
            name => name.EndsWith("community-pack-placeholder.jpg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BrowserRendersCatalogAuthorAvatarInsteadOfPrintingItsUrl() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Community/akron-community-pack-browser.cs"));

        Assert.Contains("pack.AuthorAvatarUrl", source);
        Assert.Contains("\"Author avatar\"", source);
        Assert.DoesNotContain("TextDisabledLiteral(\"Avatar: \"", source);
    }

    [Fact]
    public void ParseIndexAcceptsBotContractShape() {
        AkronCommunityPackIndex index = AkronCommunityPacks.ParseIndex("""
        {
          "format": "akron-community-pack-index-v3",
          "version": 3,
          "packs": [
            {
              "id": "spring-collab-startpos",
              "title": "Spring Collab StartPos",
              "section": "StartPos",
              "mapSid": "SpringCollab2020/1-Beginner",
              "mapUrl": "https://gamebanana.com/mods/150453",
              "discordUrl": "https://discord.com/channels/123456789012345678/234567890123456789",
              "downloadUrl": "file:///tmp/spring.akr",
              "sha256": "0000000000000000000000000000000000000000000000000000000000000000",
              "sizeBytes": 1234,
              "authorName": "Microck",
              "tags": ["startpos", "beginner"]
            }
          ]
        }
        """);

        AkronCommunityPackEntry pack = Assert.Single(index.Packs);
        Assert.Equal(AkronSetupSection.StartPos, pack.Section);
        Assert.Equal("https://gamebanana.com/mods/150453", pack.MapUrl);
        Assert.Equal("https://discord.com/channels/123456789012345678/234567890123456789", pack.DiscordUrl);
    }

    [Fact]
    public void ParseIndexRejectsLegacyOrUnverifiedEntries() {
        Assert.Throws<InvalidDataException>(() => AkronCommunityPacks.ParseIndex("""
        { "format": "akron-community-pack-index-v1", "version": 1, "packs": [] }
        """));
        Assert.Throws<InvalidDataException>(() => AkronCommunityPacks.ParseIndex("""
        { "format": "akron-community-pack-index-v2", "version": 2, "packs": [] }
        """));
        Assert.Throws<InvalidDataException>(() => AkronCommunityPacks.ParseIndex("""
        {
          "format": "akron-community-pack-index-v3",
          "version": 3,
          "packs": [{ "id": "x", "title": "X", "section": "StartPos", "downloadUrl": "https://akron.micr.dev/x.akr" }]
        }
        """));
    }

    [Theory]
    [InlineData("http://discord.com/channels/123/456")]
    [InlineData("https://discord.example.com/channels/123/456")]
    [InlineData("https://discord.com/channels/@me/456")]
    [InlineData("https://discord.com/channels/123/456/789")]
    [InlineData("https://discord.com/channels/123/456?source=akron")]
    public void DiscordLinksRejectNonThreadDestinations(string url) {
        Assert.Throws<InvalidDataException>(() => AkronCommunityPacks.ResolveDiscordUri(url));
    }

    [Fact]
    public void DiscordLinksAcceptServerThreadUrls() {
        const string url = "https://discord.com/channels/123456789012345678/234567890123456789";

        Assert.Equal(url, AkronCommunityPacks.ResolveDiscordUri(url).AbsoluteUri);
    }

    [Fact]
    public void BrowserProvidesLargePreviewAndDiscordActions() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Community/akron-community-pack-browser.cs"));

        Assert.Contains("Click to enlarge", source);
        Assert.Contains("DrawCommunityPackPreviewLightbox", source);
        Assert.Contains("See in Discord", source);
    }

    [Theory]
    [InlineData(1920, 1080, 900, 600, 900, 506.25)]
    [InlineData(800, 1200, 900, 600, 400, 600)]
    public void LargePreviewPreservesImageAspectRatio(
        int imageWidth,
        int imageHeight,
        float maximumWidth,
        float maximumHeight,
        float expectedWidth,
        float expectedHeight) {
        System.Numerics.Vector2 size = AkronOverlay.FitCommunityPackPreviewSize(
            imageWidth,
            imageHeight,
            maximumWidth,
            maximumHeight);

        Assert.Equal(expectedWidth, size.X, 2);
        Assert.Equal(expectedHeight, size.Y, 2);
    }

    [Theory]
    [InlineData(288, 8, true)]
    [InlineData(287.99, 8, false)]
    [InlineData(260, 8, false)]
    public void CommunityPackActionsWrapBeforeImportWouldBeClipped(
        float availableWidth,
        float itemSpacing,
        bool expected) {
        Assert.Equal(expected, AkronOverlay.CommunityPackActionsFitSameLine(availableWidth, itemSpacing));
    }

    [Theory]
    [InlineData("http://akron.micr.dev/catalog/index.json")]
    [InlineData("https://127.0.0.1/catalog/index.json")]
    [InlineData("https://100.100.100.100/catalog/index.json")]
    [InlineData("https://169.254.169.254/catalog/index.json")]
    [InlineData("file://remote-host/tmp/index.json")]
    public void RefreshRejectsUnsafeCatalogSources(string url) {
        AkronCommunityPackSearchResult result = AkronCommunityPacks.Refresh(url, new AkronCommunityPackFilter());
        Assert.StartsWith("Connection failed:", result.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewImagesPreferRoomNamedImagesAndFallbackToLegacyFields() {
        AkronCommunityPackEntry richPack = new AkronCommunityPackEntry {
            ImageUrl = "https://cdn.example.test/legacy.png",
            ImageUrls = { "https://cdn.example.test/list.png" },
            Images = {
                new AkronCommunityPackImage {
                    Url = "https://cdn.example.test/room-a.png",
                    RoomName = "room-a"
                }
            }
        };
        AkronCommunityPackImage[] richImages = AkronCommunityPacks.GetPreviewImages(richPack).ToArray();
        Assert.Equal(3, richImages.Length);
        Assert.Equal("https://cdn.example.test/room-a.png", richImages[0].Url);
        Assert.Equal("room-a", richImages[0].RoomName);
        Assert.Equal("https://cdn.example.test/list.png", richImages[1].Url);
        Assert.Equal("https://cdn.example.test/legacy.png", richImages[2].Url);

        AkronCommunityPackEntry listPack = new AkronCommunityPackEntry {
            ImageUrls = { "https://cdn.example.test/one.png", " " }
        };
        AkronCommunityPackImage listImage = Assert.Single(AkronCommunityPacks.GetPreviewImages(listPack));
        Assert.Equal("https://cdn.example.test/one.png", listImage.Url);
        Assert.Equal(string.Empty, listImage.RoomName);

        AkronCommunityPackEntry legacyPack = new AkronCommunityPackEntry {
            ImageUrl = "https://cdn.example.test/legacy.png"
        };
        AkronCommunityPackImage legacyImage = Assert.Single(AkronCommunityPacks.GetPreviewImages(legacyPack));
        Assert.Equal("https://cdn.example.test/legacy.png", legacyImage.Url);
    }

    [Fact]
    public void PreviewValidationRejectsOversizedDecodedDimensions() {
        byte[] pngHeader = {
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
            0x00, 0x00, 0x00, 0x0d, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x20, 0x00
        };

        Assert.False(AkronOverlay.TryValidateCommunityPackPreviewImage(pngHeader, out _, out _, out string error));
        Assert.Equal("Preview image dimensions are too large.", error);
    }

    [Fact]
    public void PreviewValidationAcceptsBoundedPngDimensions() {
        byte[] pngHeader = {
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
            0x00, 0x00, 0x00, 0x0d, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x07, 0x80, 0x00, 0x00, 0x04, 0x38
        };

        Assert.True(AkronOverlay.TryValidateCommunityPackPreviewImage(pngHeader, out int width, out int height, out string error), error);
        Assert.Equal(1920, width);
        Assert.Equal(1080, height);
    }

    [Fact]
    public void PreviewValidationRejectsWebpThatFnaCannotDecode() {
        byte[] webpHeader = {
            0x52, 0x49, 0x46, 0x46, 0x44, 0x1c, 0x02, 0x00,
            0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x20,
            0x38, 0x1c, 0x02, 0x00, 0xf0, 0x3c, 0x09, 0x9d,
            0x01, 0x2a, 0x40, 0x06, 0xa0, 0x05
        };

        Assert.False(AkronOverlay.TryValidateCommunityPackPreviewImage(webpHeader, out _, out _, out string error));
        Assert.Equal("Preview image format is unsupported or invalid.", error);
    }

    [Fact]
    public void CatalogResourcesOnlyAllowDiscordCdnForAuthorAvatars() {
        AkronCommunityPackEntry pack = new AkronCommunityPackEntry();
        string avatarUrl = "https://cdn.discordapp.com/avatars/1267825421781831815/avatar-hash.jpg?size=128";

        Assert.Equal(
            avatarUrl,
            AkronCommunityPacks.ResolveCatalogResourceUri(pack, avatarUrl, "Author avatar").AbsoluteUri);
        Assert.Throws<InvalidDataException>(() =>
            AkronCommunityPacks.ResolveCatalogResourceUri(pack, avatarUrl, "Preview image"));
        Assert.Throws<InvalidDataException>(() =>
            AkronCommunityPacks.ResolveCatalogResourceUri(pack, "https://example.com/avatar.jpg", "Author avatar"));
    }

    [Fact]
    public void PreviewCacheSeparatesResourcePolicies() {
        const string url = "https://cdn.discordapp.com/avatars/1267825421781831815/avatar-hash.jpg?size=128";

        Assert.NotEqual(
            AkronOverlay.CommunityPackPreviewImageCacheKey(url, "Author avatar"),
            AkronOverlay.CommunityPackPreviewImageCacheKey(url, "Preview image"));
    }

    [Fact]
    public void PreviewValidationCapsDecodedImageAtDiscordOutputContract() {
        byte[] pngHeader = {
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
            0x00, 0x00, 0x00, 0x0d, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x08, 0x01, 0x00, 0x00, 0x04, 0x00
        };

        Assert.False(AkronOverlay.TryValidateCommunityPackPreviewImage(pngHeader, out _, out _, out string error));
        Assert.Equal("Preview image dimensions are too large.", error);
    }

    [Fact]
    public void PreviewSchedulingOnlyLoadsVisibleSelectedOrDetailImages() {
        Assert.True(AkronOverlay.ShouldScheduleCommunityPackPreview(selectedCard: true, detailView: false, visible: true));
        Assert.True(AkronOverlay.ShouldScheduleCommunityPackPreview(selectedCard: false, detailView: true, visible: true));
        Assert.False(AkronOverlay.ShouldScheduleCommunityPackPreview(selectedCard: false, detailView: false, visible: true));
        Assert.False(AkronOverlay.ShouldScheduleCommunityPackPreview(selectedCard: true, detailView: false, visible: false));
    }

    [Fact]
    public void FilterRequiresCurrentMapAndSelectedCategory() {
        AkronCommunityPackEntry matching = new AkronCommunityPackEntry {
            Id = "match",
            Title = "Start room setup",
            Section = AkronSetupSection.StartPos,
            MapSid = "Maps/Current",
            DownloadUrl = "file:///tmp/match.akr"
        };
        AkronCommunityPackEntry wrongMap = new AkronCommunityPackEntry {
            Id = "wrong-map",
            Title = "Start room setup",
            Section = AkronSetupSection.StartPos,
            MapSid = "Maps/Other",
            DownloadUrl = "file:///tmp/wrong.akr"
        };
        AkronCommunityPackEntry wrongCategory = new AkronCommunityPackEntry {
            Id = "wrong-category",
            Title = "Auto kill setup",
            Section = AkronSetupSection.AutoKill,
            MapSid = "Maps/Current",
            DownloadUrl = "file:///tmp/kill.akr"
        };

        AkronCommunityPackEntry[] results = AkronCommunityPacks.Filter(
            new[] { matching, wrongMap, wrongCategory },
            new AkronCommunityPackFilter {
                MapSid = "Maps/Current",
                Section = AkronSetupSection.StartPos,
                Query = "start"
            }).ToArray();

        Assert.Same(matching, Assert.Single(results));
    }

    [Fact]
    public void FilterSearchesAuthorDescriptionAndTags() {
        AkronCommunityPackEntry pack = new AkronCommunityPackEntry {
            Id = "auto-deafen",
            Title = "Quiet rooms",
            Description = "Discord deafen regions for berry rooms",
            Section = AkronSetupSection.AutoDeafen,
            MapSid = "Maps/Current",
            DownloadUrl = "file:///tmp/deafen.akr",
            AuthorName = "Mapper",
            Tags = { "voice", "focus" }
        };

        AkronCommunityPackEntry[] results = AkronCommunityPacks.Filter(
            new[] { pack },
            new AkronCommunityPackFilter {
                MapSid = "Maps/Current",
                Section = AkronSetupSection.AutoDeafen,
                Query = "mapper focus"
            }).ToArray();

        Assert.Same(pack, Assert.Single(results));
    }

    [Fact]
    public void BeginRefreshLoadsFileIndexWithoutBlockingCaller() {
        string path = Path.Combine(Path.GetTempPath(), "akron-community-index-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """
        {
          "format": "akron-community-pack-index-v3",
          "version": 3,
          "packs": [
            {
              "id": "file-refresh",
              "title": "File refresh pack",
              "section": "AutoKill",
              "mapSid": "Maps/Current",
              "downloadUrl": "file:///tmp/file-refresh.akr",
              "sha256": "0000000000000000000000000000000000000000000000000000000000000000",
              "sizeBytes": 1234
            }
          ]
        }
        """);

        try {
            AkronCommunityPacks.BeginRefresh(new System.Uri(path).AbsoluteUri);
            Assert.True(AkronCommunityPacks.CompleteRefreshForTesting(TimeSpan.FromSeconds(10)));

            AkronCommunityPackSearchResult result = AkronCommunityPacks.Search(new AkronCommunityPackFilter {
                MapSid = "Maps/Current",
                Section = AkronSetupSection.AutoKill
            });

            Assert.Equal("File refresh pack", Assert.Single(result.Entries).Title);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void RefreshRejectsOversizedFileIndex() {
        string path = Path.Combine(Path.GetTempPath(), "akron-community-index-too-large.json");
        File.WriteAllBytes(path, new byte[1024 * 1024 + 1]);

        AkronCommunityPackSearchResult result = AkronCommunityPacks.Refresh(
            new Uri(path).AbsoluteUri,
            new AkronCommunityPackFilter());

        Assert.Contains("Community index is too large.", result.Status);
    }

    [Fact]
    public void BeginDownloadReportsCompletionWithVisibleStatus() {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "akron-community-download-test");
        Directory.CreateDirectory(tempDirectory);
        AkronCommunityPacks.SetSetupDirectoryProviderForTest(() => tempDirectory);
        string sourcePath = Path.Combine(tempDirectory, "akron-community-source.akr");
        File.WriteAllText(sourcePath, "invalid digest fixture");
        byte[] sourceBytes = File.ReadAllBytes(sourcePath);
        try {
            AkronCommunityPackEntry entry = new AkronCommunityPackEntry {
                Id = "download-test",
                Title = "Download Test Pack",
                Section = AkronSetupSection.StartPos,
                MapSid = "Maps/Current",
                DownloadUrl = new System.Uri(sourcePath).AbsoluteUri,
                Sha256 = new string('0', 64),
                SizeBytes = sourceBytes.Length
            };

            Assert.True(AkronCommunityPacks.BeginDownload(entry, out string beginMessage), beginMessage);
            Assert.Equal("Downloading Download Test Pack...", beginMessage);
            Assert.True(AkronCommunityPacks.DownloadInProgress);
            Assert.Equal(beginMessage, AkronCommunityPacks.Search(new AkronCommunityPackFilter()).Status);
            bool imported = false;
            string completeMessage = string.Empty;
            bool completed = SpinWait.SpinUntil(
                () => AkronCommunityPacks.CompleteImportIfReady(out imported, out completeMessage),
                10000);
            Assert.True(completed, completeMessage);

            Assert.False(imported);
            Assert.Equal("Pack checksum does not match the catalog.", completeMessage);
            Assert.False(AkronCommunityPacks.DownloadInProgress);
            Assert.Equal(completeMessage, AkronCommunityPacks.Search(new AkronCommunityPackFilter()).Status);
        } finally {
            AkronCommunityPacks.SetSetupDirectoryProviderForTest(null);
        }
    }

    [Fact]
    public void ImportRejectsOversizedFilePack() {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "akron-community-download-too-large-test");
        Directory.CreateDirectory(tempDirectory);
        AkronCommunityPacks.SetSetupDirectoryProviderForTest(() => tempDirectory);
        string sourcePath = Path.Combine(tempDirectory, "oversized.akr");
        File.WriteAllBytes(sourcePath, new byte[4 * 1024 * 1024 + 1]);
        try {
            AkronCommunityPackEntry entry = new AkronCommunityPackEntry {
                Id = "oversized",
                Title = "Oversized Pack",
                Section = AkronSetupSection.StartPos,
                MapSid = "Maps/Current",
                DownloadUrl = new Uri(sourcePath).AbsoluteUri,
                Sha256 = new string('0', 64),
                SizeBytes = 4 * 1024 * 1024 + 1
            };

            Assert.False(AkronCommunityPacks.Import(entry, out string message));
            Assert.Equal("Pack is too large.", message);
        } finally {
            AkronCommunityPacks.SetSetupDirectoryProviderForTest(null);
        }
    }

    [Fact]
    public void ImportRejectsPackWhoseDigestDoesNotMatchCatalog() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-community-digest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        AkronCommunityPacks.SetSetupDirectoryProviderForTest(() => directory);
        string path = Path.Combine(directory, "source.akr");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        AkronCommunityPackEntry entry = new AkronCommunityPackEntry {
            Id = "digest-mismatch",
            Title = "Digest mismatch",
            Section = AkronSetupSection.StartPos,
            MapSid = "Maps/Current",
            DownloadUrl = new Uri(path).AbsoluteUri,
            Sha256 = new string('0', 64),
            SizeBytes = 4
        };
        try {
            Assert.False(AkronCommunityPacks.Import(entry, out string message));
            Assert.Equal("Pack checksum does not match the catalog.", message);
        } finally {
            AkronCommunityPacks.SetSetupDirectoryProviderForTest(null);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UploadErrorsExposeOnlyStatusAndValidatedRequestId() {
        using HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.BadRequest) {
            Content = new StringContent("secret response body with https://signed.example/token?secret=value")
        };
        response.Headers.TryAddWithoutValidation("cf-ray", "abc-123:DFW");

        string message = AkronCommunityPackUploads.BuildUploadFailureMessageForTesting(response);

        Assert.Equal("Upload request failed with HTTP 400 (request abc-123:DFW)", message);
        Assert.DoesNotContain("secret", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadResponseReaderRejectsBodiesPastItsStreamingCap() {
        using HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(new string('x', 4097))
        };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            AkronCommunityPackUploads.ReadUploadResponseForTesting(response, 4096));
    }
}
