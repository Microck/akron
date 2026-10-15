using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class BackupSaveRootTests
{
    [Fact]
    public void RuntimeResolverUsesCelestesAuthoritativeCachedSaveRoot()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "../../../../Source/Actions/akron-backup-actions.cs"));
        int start = source.IndexOf("private static string GetSavesFolder()", StringComparison.Ordinal);
        int end = source.IndexOf("internal static string FormatBackupTextForDisplay", start, StringComparison.Ordinal);
        string resolver = source[start..end];
        Assert.Contains("return Everest.PathSettings;", resolver);
        Assert.DoesNotContain("Everest.PathGame", resolver);
        Assert.DoesNotContain("GetEnvironmentVariable", resolver);
        Assert.Contains("Path.Combine(GetSavesFolder(), BackupFolderName)", source);
        Assert.Equal(2, source.Split("string savesFolder = GetSavesFolder();").Length - 1);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("xdg-data/Celeste")]
    [InlineData("installation")]
    public void ArchiveAndRestoreStayWithinSelectedSaveRoot(string relativeRoot)
    {
        string root = Path.Combine(Path.GetTempPath(), "akron-active-root-" + Guid.NewGuid().ToString("N"));
        string saves = Path.Combine(root, relativeRoot, "Saves");
        string decoys = Path.Combine(root, "other-installation", "Saves");
        string backups = Path.Combine(saves, "AkronBackups");
        Directory.CreateDirectory(backups);
        Directory.CreateDirectory(decoys);
        string[] names = { "0.celeste", "settings.celeste", "0-modsave-Akron.celeste", "0-modsession-Akron.celeste", "modsettings-Akron.celeste" };
        try
        {
            foreach (string name in names)
            {
                File.WriteAllText(Path.Combine(saves, name), "active:" + name);
                File.WriteAllText(Path.Combine(decoys, name), "decoy:" + name);
            }
            Directory.CreateDirectory(Path.Combine(saves, "AkronNative"));
            File.WriteAllText(Path.Combine(saves, "AkronNative", "held.dll"), "runtime");
            File.WriteAllText(Path.Combine(backups, "legacy.zip"), "untouched archive");
            string archivePath = Path.Combine(backups, "current.zip");
            Assert.Empty(AkronBackupActions.WriteSavesArchive(saves, archivePath, _ => "{}"));
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                Assert.Equal(names.OrderBy(x => x), archive.Entries.Where(x => x.FullName != "_akron-backup.json").Select(x => x.FullName).OrderBy(x => x));
                foreach (string name in names)
                {
                    using StreamReader reader = new(archive.GetEntry(name)!.Open());
                    Assert.Equal("active:" + name, reader.ReadToEnd());
                }
            }
            foreach (string name in names) File.WriteAllText(Path.Combine(saves, name), "changed");
            AkronBackupActions.RestoreSavesFolder(saves, archivePath, Path.Combine(saves, "AkronRestore", "run"), () => { });
            foreach (string name in names)
            {
                Assert.Equal("active:" + name, File.ReadAllText(Path.Combine(saves, name)));
                Assert.Equal("decoy:" + name, File.ReadAllText(Path.Combine(decoys, name)));
            }
            Assert.Equal("runtime", File.ReadAllText(Path.Combine(saves, "AkronNative", "held.dll")));
            Assert.Equal("untouched archive", File.ReadAllText(Path.Combine(backups, "legacy.zip")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
