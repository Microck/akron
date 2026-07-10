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
    public void ParseIndexAcceptsBotContractShape() {
        AkronCommunityPackIndex index = AkronCommunityPacks.ParseIndex("""
        {
          "format": "akron-community-pack-index-v2",
          "version": 2,
          "packs": [
            {
              "id": "spring-collab-startpos",
              "title": "Spring Collab StartPos",
              "section": "StartPos",
              "mapSid": "SpringCollab2020/1-Beginner",
              "mapUrl": "https://gamebanana.com/mods/150453",
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
    }

    [Fact]
    public void ParseIndexRejectsLegacyOrUnverifiedEntries() {
        Assert.Throws<InvalidDataException>(() => AkronCommunityPacks.ParseIndex("""
        { "format": "akron-community-pack-index-v1", "version": 1, "packs": [] }
        """));
        Assert.Throws<InvalidDataException>(() => AkronCommunityPacks.ParseIndex("""
        {
          "format": "akron-community-pack-index-v2",
          "version": 2,
          "packs": [{ "id": "x", "title": "X", "section": "StartPos", "downloadUrl": "https://akron.micr.dev/x.akr" }]
        }
        """));
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
          "format": "akron-community-pack-index-v2",
          "version": 2,
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
    public void BeginDownloadCopiesArchiveFromCatalogEntry() {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "akron-community-download-test");
        Directory.CreateDirectory(tempDirectory);
        AkronCommunityPacks.SetSetupDirectoryProviderForTest(() => tempDirectory);
        string sourcePath = Path.Combine(tempDirectory, "akron-community-source.akr");
        AkronSetupPacks.Write(
            new AkronModuleSettings(),
            new AkronModuleSession {
                StartPositions = new System.Collections.Generic.Dictionary<int, AkronStartPos> {
                    [1] = new AkronStartPos { AreaSid = "Maps/Current", Room = "a-00" }
                }
            },
            sourcePath,
            "Downloaded StartPos",
            AkronSetupSection.StartPos);
        byte[] sourceBytes = File.ReadAllBytes(sourcePath);
        try {
            AkronCommunityPackEntry entry = new AkronCommunityPackEntry {
                Id = "download-test",
                Title = "Download Test Pack",
                Section = AkronSetupSection.StartPos,
                MapSid = "Maps/Current",
                DownloadUrl = new System.Uri(sourcePath).AbsoluteUri,
                Sha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant(),
                SizeBytes = sourceBytes.Length
            };

            Assert.True(AkronCommunityPacks.BeginDownload(entry, out string beginMessage), beginMessage);
            AkronCommunityPackEntry downloaded = null!;
            string downloadedPath = string.Empty;
            string completeMessage = string.Empty;
            bool completed = SpinWait.SpinUntil(
                () => AkronCommunityPacks.CompleteDownloadIfReady(out downloaded, out downloadedPath, out completeMessage),
                10000);
            Assert.True(completed, completeMessage);

            Assert.Same(entry, downloaded);
            Assert.True(File.Exists(downloadedPath));
            AkronSetupPack pack = AkronSetupPacks.Read(downloadedPath);
            Assert.Equal("Downloaded StartPos", pack.Name);
            Assert.Equal(AkronSetupSection.StartPos, pack.Section);
            Assert.EndsWith(".akr", downloadedPath);
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
