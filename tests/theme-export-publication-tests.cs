using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ThemeExportPublicationTests : IDisposable {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "akron-theme-export-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime time = new(2026, 10, 16, 19, 44, 7, DateTimeKind.Utc);
    public ThemeExportPublicationTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private AkronArchiveManifest Manifest(int step = 0) => new() { Kind = AkronOverlayThemes.ThemeArchiveKind, CreatedAt = time.AddMilliseconds(step).ToString("O") };
    private string Target => Path.Combine(directory, "theme-20261016T194407Z.akr");
    private string Read(string path) => AkronArchive.ReadSinglePayloadArchive(path, AkronOverlayThemes.ThemeArchiveKind, "theme.json", 4096, out _);

    [Fact]
    public void RepeatedExportsPreserveAllArchivesAndReturnActualPaths() {
        string first = AkronArchive.WriteUniqueSinglePayloadArchive(Target, Manifest(), "theme.json", "first");
        byte[] original = File.ReadAllBytes(first);
        string second = AkronArchive.WriteUniqueSinglePayloadArchive(Target, Manifest(1), "theme.json", "second");
        string third = AkronArchive.WriteUniqueSinglePayloadArchive(Target, Manifest(2), "theme.json", "third");
        Assert.Equal(Target, first);
        Assert.EndsWith("-1.akr", second);
        Assert.EndsWith("-2.akr", third);
        Assert.Equal(original, File.ReadAllBytes(first));
        Assert.Equal("second", Read(second));
        Assert.Equal("third", Read(third));
        Assert.Equal(3, Directory.GetFiles(directory).Length);
    }

    [Fact]
    public void ConcurrentExportsPublishCompleteDistinctArchives() {
        Parallel.For(0, 12, i => AkronArchive.WriteUniqueSinglePayloadArchive(Target, Manifest(i), "theme.json", i.ToString()));
        string[] files = Directory.GetFiles(directory, "*.akr");
        Assert.Equal(12, files.Length);
        Assert.Equal(Enumerable.Range(0, 12), files.Select(Read).Select(int.Parse).OrderBy(i => i));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void ExistingReplaceWriterKeepsExplicitReplacementBehavior() {
        AkronArchive.WriteSinglePayloadArchive(Target, Manifest(), "theme.json", "old");
        AkronArchive.WriteSinglePayloadArchive(Target, Manifest(1), "theme.json", "replacement");
        Assert.Equal("replacement", Read(Target));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void FailedValidationDoesNotReplacePreviousArchiveOrLeaveTemporary() {
        AkronArchive.WriteUniqueSinglePayloadArchive(Target, Manifest(), "theme.json", "old");
        byte[] original = File.ReadAllBytes(Target);
        Assert.Throws<ArgumentException>(() => AkronArchive.WriteUniqueSinglePayloadArchive(Target, Manifest(1), "../invalid", "new"));
        Assert.Equal(original, File.ReadAllBytes(Target));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void PublicationFailureCleansCompleteTemporaryArchive() {
        Directory.CreateDirectory(Target);
        Assert.ThrowsAny<IOException>(() => AkronArchive.WriteUniqueSinglePayloadArchive(Target, Manifest(), "theme.json", "new"));
        Assert.Empty(Directory.GetFiles(directory));
        Assert.True(Directory.Exists(Target));
    }

    [Fact]
    public void LatestThemeUsesManifestTimeWhenFilesystemTimesTieAcrossNamesOrHoles() {
        string first = AkronArchive.WriteUniqueSinglePayloadArchive(Target, Manifest(), "theme.json", "first");
        string removed = AkronArchive.WriteUniqueSinglePayloadArchive(Target, Manifest(1), "theme.json", "removed");
        string third = AkronArchive.WriteUniqueSinglePayloadArchive(Target, Manifest(2), "theme.json", "third");
        File.Delete(removed);
        string newest = AkronArchive.WriteUniqueSinglePayloadArchive(Target, Manifest(3), "theme.json", "newest");
        foreach (string p in Directory.GetFiles(directory)) File.SetLastWriteTimeUtc(p, time);
        Assert.Equal(newest, AkronOverlayThemes.FindLatestTheme(directory));
        string differentlyNamed = AkronArchive.WriteUniqueSinglePayloadArchive(Path.Combine(directory, "aaa.akr"), Manifest(4), "theme.json", "last");
        File.SetLastWriteTimeUtc(differentlyNamed, time);
        Assert.Equal(differentlyNamed, AkronOverlayThemes.FindLatestTheme(directory));
        Assert.Equal("first", Read(first));
        Assert.Equal("third", Read(third));
    }

    [Fact]
    public void LatestThemeRetainsMtimePriorityAndHandlesMalformedTie() {
        Assert.Null(AkronOverlayThemes.FindLatestTheme(directory));
        string valid = AkronArchive.WriteUniqueSinglePayloadArchive(Target, Manifest(), "theme.json", "valid");
        string invalid = Path.Combine(directory, "bad.akr");
        File.WriteAllText(invalid, "not a zip");
        File.SetLastWriteTimeUtc(valid, time);
        File.SetLastWriteTimeUtc(invalid, time);
        Assert.Equal(valid, AkronOverlayThemes.FindLatestTheme(directory));
        File.SetLastWriteTimeUtc(invalid, time.AddSeconds(1));
        Assert.Equal(invalid, AkronOverlayThemes.FindLatestTheme(directory));
    }

    [Fact]
    public void EqualManifestAndFileTimesUseNumericSuffixWithoutReusingDeletedHoles() {
        string latest = null;
        for (int i = 0; i < 12; i++) {
            latest = AkronArchive.WriteUniqueSinglePayloadArchive(Target, Manifest(), "theme.json", i.ToString());
            File.SetLastWriteTimeUtc(latest, time);
        }
        Assert.EndsWith("-11.akr", latest);
        Assert.Equal(latest, AkronOverlayThemes.FindLatestTheme(directory));
        File.Delete(Path.Combine(directory, "theme-20261016T194407Z-1.akr"));
        string newest = AkronArchive.WriteUniqueSinglePayloadArchive(Target, Manifest(), "theme.json", "newest");
        File.SetLastWriteTimeUtc(newest, time);
        Assert.EndsWith("-12.akr", newest);
        Assert.Equal(newest, AkronOverlayThemes.FindLatestTheme(directory));
    }

    [Fact]
    public void CleanupKeepsLegacyPropagationAndReportsUniqueFailure() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Packs/akron-archive.cs"));
        string cleanup = source.Split("        } finally {")[1].Split("    public static string ReadSinglePayloadArchive")[0];
        Assert.Contains("if (!preserveExisting)", cleanup);
        Assert.Contains("if (File.Exists(temporaryPath)) File.Delete(temporaryPath);", cleanup);
        Assert.Contains("Logger.Log(LogLevel.Warn", cleanup);
    }

    [Fact]
    public void ActualThemeExportUsesReturnedUniquePath() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-themes.cs"));
        string method = source.Split("public static string ExportCurrentTheme")[1].Split("public static bool ImportTheme")[0];
        Assert.Contains("path = AkronArchive.WriteUniqueSinglePayloadArchive", method);
        Assert.Contains("return path;", method);
    }
}
