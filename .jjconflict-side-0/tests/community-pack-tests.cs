using System;
using System.Linq;
using System.IO;
using System.Threading;
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
          "format": "akron-community-pack-index-v1",
          "version": 1,
          "packs": [
            {
              "id": "spring-collab-startpos",
              "title": "Spring Collab StartPos",
              "section": "StartPos",
              "mapSid": "SpringCollab2020/1-Beginner",
              "mapUrl": "https://gamebanana.com/mods/150453",
              "downloadUrl": "file:///tmp/spring.akr",
              "authorName": "Microck",
              "tags": ["startpos", "beginner"]
            }
          ]
        }
        """);

        AkronCommunityPackEntry pack = Assert.Single(index.Packs);
        Assert.Equal(AkronProfileSection.StartPos, pack.Section);
        Assert.Equal("https://gamebanana.com/mods/150453", pack.MapUrl);
    }

    [Fact]
    public void FilterRequiresCurrentMapAndSelectedCategory() {
        AkronCommunityPackEntry matching = new AkronCommunityPackEntry {
            Id = "match",
            Title = "Start room setup",
            Section = AkronProfileSection.StartPos,
            MapSid = "Maps/Current",
            DownloadUrl = "file:///tmp/match.akr"
        };
        AkronCommunityPackEntry wrongMap = new AkronCommunityPackEntry {
            Id = "wrong-map",
            Title = "Start room setup",
            Section = AkronProfileSection.StartPos,
            MapSid = "Maps/Other",
            DownloadUrl = "file:///tmp/wrong.akr"
        };
        AkronCommunityPackEntry wrongCategory = new AkronCommunityPackEntry {
            Id = "wrong-category",
            Title = "Auto kill setup",
            Section = AkronProfileSection.AutoKill,
            MapSid = "Maps/Current",
            DownloadUrl = "file:///tmp/kill.akr"
        };

        AkronCommunityPackEntry[] results = AkronCommunityPacks.Filter(
            new[] { matching, wrongMap, wrongCategory },
            new AkronCommunityPackFilter {
                MapSid = "Maps/Current",
                Section = AkronProfileSection.StartPos,
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
            Section = AkronProfileSection.AutoDeafen,
            MapSid = "Maps/Current",
            DownloadUrl = "file:///tmp/deafen.akr",
            AuthorName = "Mapper",
            Tags = { "voice", "focus" }
        };

        AkronCommunityPackEntry[] results = AkronCommunityPacks.Filter(
            new[] { pack },
            new AkronCommunityPackFilter {
                MapSid = "Maps/Current",
                Section = AkronProfileSection.AutoDeafen,
                Query = "mapper focus"
            }).ToArray();

        Assert.Same(pack, Assert.Single(results));
    }

    [Fact]
    public void BeginRefreshLoadsFileIndexWithoutBlockingCaller() {
        string path = Path.Combine(Path.GetTempPath(), "akron-community-index-test.json");
        File.WriteAllText(path, """
        {
          "format": "akron-community-pack-index-v1",
          "version": 1,
          "packs": [
            {
              "id": "file-refresh",
              "title": "File refresh pack",
              "section": "AutoKill",
              "mapSid": "Maps/Current",
              "downloadUrl": "file:///tmp/file-refresh.akr"
            }
          ]
        }
        """);

        AkronCommunityPacks.BeginRefresh(new System.Uri(path).AbsoluteUri);
        Assert.True(SpinWait.SpinUntil(() => !AkronCommunityPacks.RefreshInProgress, 2000));

        AkronCommunityPackSearchResult result = AkronCommunityPacks.Search(new AkronCommunityPackFilter {
            MapSid = "Maps/Current",
            Section = AkronProfileSection.AutoKill
        });

        Assert.Equal("File refresh pack", Assert.Single(result.Entries).Title);
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
        AkronCommunityPacks.SetProfileDirectoryProviderForTest(() => tempDirectory);
        string sourcePath = Path.Combine(tempDirectory, "akron-community-source.akr");
        AkronArchive.WriteSinglePayloadArchive(
            sourcePath,
            new AkronArchiveManifest {
                Kind = AkronProfilePacks.ProfileArchiveKind,
                KindVersion = 1,
                Target = new AkronArchiveTarget {
                    Game = "Celeste",
                    MapSid = "Maps/Current"
                }
            },
            AkronProfilePacks.ProfileArchivePayload,
            """
            {
              "Format": "akron-profile-v1",
              "Name": "Downloaded StartPos",
              "Section": "StartPos",
              "State": {}
            }
            """);
        try {
            AkronCommunityPackEntry entry = new AkronCommunityPackEntry {
                Id = "download-test",
                Title = "Download Test Pack",
                Section = AkronProfileSection.StartPos,
                MapSid = "Maps/Current",
                DownloadUrl = new System.Uri(sourcePath).AbsoluteUri
            };

            Assert.True(AkronCommunityPacks.BeginDownload(entry, out string beginMessage), beginMessage);
            AkronCommunityPackEntry downloaded = null!;
            string downloadedPath = string.Empty;
            string completeMessage = string.Empty;
            bool completed = SpinWait.SpinUntil(
                () => AkronCommunityPacks.CompleteDownloadIfReady(out downloaded, out downloadedPath, out completeMessage),
                2000);
            Assert.True(completed, completeMessage);

            Assert.Same(entry, downloaded);
            Assert.True(File.Exists(downloadedPath));
            AkronProfilePack pack = AkronProfilePacks.Read(downloadedPath);
            Assert.Equal("Downloaded StartPos", pack.Name);
            Assert.Equal(AkronProfileSection.StartPos, pack.Section);
            Assert.EndsWith(".akr", downloadedPath);
        } finally {
            AkronCommunityPacks.SetProfileDirectoryProviderForTest(null);
        }
    }

    [Fact]
    public void ImportRejectsOversizedFilePack() {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "akron-community-download-too-large-test");
        Directory.CreateDirectory(tempDirectory);
        AkronCommunityPacks.SetProfileDirectoryProviderForTest(() => tempDirectory);
        string sourcePath = Path.Combine(tempDirectory, "oversized.akr");
        File.WriteAllBytes(sourcePath, new byte[4 * 1024 * 1024 + 1]);
        try {
            AkronCommunityPackEntry entry = new AkronCommunityPackEntry {
                Id = "oversized",
                Title = "Oversized Pack",
                Section = AkronProfileSection.StartPos,
                MapSid = "Maps/Current",
                DownloadUrl = new Uri(sourcePath).AbsoluteUri
            };

            Assert.False(AkronCommunityPacks.Import(entry, out string message));
            Assert.Equal("Pack is too large.", message);
        } finally {
            AkronCommunityPacks.SetProfileDirectoryProviderForTest(null);
        }
    }
}
