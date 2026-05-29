using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Celeste.Mod.Akron;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ArchiveTests : IDisposable {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "akron-archive-tests-" + Guid.NewGuid().ToString("N"));

    public ArchiveTests() {
        Directory.CreateDirectory(directory);
    }

    public void Dispose() {
        if (Directory.Exists(directory)) {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SingleKindArchiveRoundTripsPayloadAndManifest() {
        string path = Path.Combine(directory, "hud-labels.akr");
        AkronArchiveManifest manifest = new AkronArchiveManifest {
            Kind = "hud-labels",
            KindVersion = 1,
            CreatedAt = "2026-04-30T15:00:00Z",
            Target = new AkronArchiveTarget {
                Game = "Celeste",
                MapSid = "Celeste/7-Summit"
            }
        };

        AkronArchive.WriteSinglePayloadArchive(path, manifest, "hud-labels.json", "{\"labels\":[]}");

        string payload = AkronArchive.ReadSinglePayloadArchive(path, "hud-labels", "hud-labels.json", 4096, out AkronArchiveManifest readManifest);

        Assert.Equal("{\"labels\":[]}", payload);
        Assert.Equal("akron-archive", readManifest.Format);
        Assert.Equal("hud-labels", readManifest.Kind);
        Assert.Equal("Celeste/7-Summit", readManifest.Target.MapSid);
    }

    [Fact]
    public void MixedKindArchiveIsRejectedBeforePayloadRead() {
        string path = Path.Combine(directory, "mixed.akr");
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create)) {
            WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new AkronArchiveManifest {
                Kind = "hud-labels",
                Target = new AkronArchiveTarget {
                    Game = "Celeste",
                    MapSid = "Celeste/1-ForsakenCity"
                }
            }));
            WriteEntry(archive, "hud-labels.json", "{}");
            WriteEntry(archive, "savestate/state.bin", "not allowed");
        }

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            AkronArchive.ReadSinglePayloadArchive(path, "hud-labels", "hud-labels.json", 4096, out _));
        Assert.Contains("exactly one manifest and one payload", ex.Message);
    }

    [Fact]
    public void WrongKindArchiveIsRejected() {
        string path = Path.Combine(directory, "profile.akr");
        AkronArchive.WriteSinglePayloadArchive(path, new AkronArchiveManifest {
            Kind = "profile"
        }, "hud-labels.json", "{}");

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            AkronArchive.ReadSinglePayloadArchive(path, "hud-labels", "hud-labels.json", 4096, out _));
        Assert.Contains("expected hud-labels", ex.Message);
    }

    [Theory]
    [InlineData("../hud-labels.json")]
    [InlineData("nested/hud-labels.json")]
    [InlineData("nested\\hud-labels.json")]
    public void UnexpectedArchiveEntryPathsAreRejected(string entryName) {
        string path = Path.Combine(directory, "unsafe-entry.akr");
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create)) {
            WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new AkronArchiveManifest {
                Kind = "hud-labels"
            }));
            WriteEntry(archive, entryName, "{}");
        }

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            AkronArchive.ReadSinglePayloadArchive(path, "hud-labels", "hud-labels.json", 4096, out _));
        Assert.Contains("unexpected entry", ex.Message);
    }

    [Fact]
    public void OversizedPayloadIsRejectedBeforeReturningText() {
        string path = Path.Combine(directory, "oversized.akr");
        AkronArchive.WriteSinglePayloadArchive(path, new AkronArchiveManifest {
            Kind = "hud-labels"
        }, "hud-labels.json", new string('x', 17));

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            AkronArchive.ReadSinglePayloadArchive(path, "hud-labels", "hud-labels.json", 16, out _));
        Assert.Contains("payload is too large", ex.Message);
    }

    private static void WriteEntry(ZipArchive archive, string name, string content) {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using Stream stream = entry.Open();
        using StreamWriter writer = new StreamWriter(stream);
        writer.Write(content);
    }
}
