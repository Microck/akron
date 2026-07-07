using System;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class StartPosPersistenceTests {
    [Fact]
    public void PersistentStartPosSnapshotDoesNotSerializeLiveLevelGraph() {
        string source = File.ReadAllText(GetPersistentSnapshotSourcePath());

        Assert.DoesNotContain("SavedLevel", source);
        Assert.DoesNotContain("SessionState", source);
        Assert.DoesNotContain("SaveDataState", source);
        Assert.DoesNotContain("TypeNameHandling", source);
        Assert.DoesNotContain("DefaultContractResolver", source);
        Assert.DoesNotContain("FormatterServices", source);
        Assert.DoesNotContain("ISerializationBinder", source);
    }

    [Fact]
    public void PersistentStartPosSnapshotStoresCuratedGameplayFields() {
        string source = File.ReadAllText(GetPersistentSnapshotSourcePath());

        Assert.Contains("akron-startpos-gameplay-v2", source);
        Assert.Contains("PlayerX", source);
        Assert.Contains("PlayerSpeedX", source);
        Assert.Contains("PlayerState", source);
        Assert.Contains("Stamina", source);
        Assert.Contains("Dashes", source);
        Assert.Contains("Facing", source);
        Assert.Contains("RespawnX", source);
        Assert.Contains("LevelTimeActive", source);
        Assert.Contains("AreaDeaths", source);
        Assert.Contains("SessionFlags", source);
        Assert.Contains("SessionCounters", source);
        Assert.Contains("SessionDoNotLoad", source);
        Assert.Contains("InventoryDashes", source);
    }

    [Fact]
    public void PersistentStartPosHydratesIntoCurrentSessionNonce() {
        string source = File.ReadAllText(GetPersistentSnapshotSourcePath());

        Assert.Contains("CurrentSessionNonce", source);
        Assert.Contains("SaveData.Instance?.FileSlot", source);
        Assert.DoesNotContain("SessionNonce = SessionNonce", source);
    }


    [Fact]
    public void SetupPackStartPosImportDoesNotResetEveryPersistedMap() {
        string source = File.ReadAllText(GetActionsSourcePath());

        Assert.DoesNotContain("saveData.StartPositionsByMap = new Dictionary<string, AkronPersistedStartPosMap>();", source);
        Assert.Contains("startPositionsByArea", source);
        Assert.Contains("DeletePersistedSnapshotsForArea", source);
    }

    [Fact]
    public void ImportedPositionOnlyStartPosDoesNotHydrateStaleCanonicalSnapshot() {
        string source = File.ReadAllText(GetActionsSourcePath());

        Assert.DoesNotContain("canonicalSnapshotPath", source);
        Assert.Contains("SnapshotPath = string.Empty", source);
        Assert.Contains("AkronPersistentStartPosSnapshots.Delete(GetStartPosSnapshotPath", source);
        Assert.Contains("ClearStartPosRuntimeState(areaPair.Key, importedSlot)", source);
    }

    [Fact]
    public void StartPosSnapshotKeysDoNotUseLossyAreaSidSanitization() {
        string source = File.ReadAllText(GetActionsSourcePath());

        Assert.Contains("Encoding.UTF8", source);
        Assert.Contains("valueByte.ToString(\"x2\"", source);
        Assert.DoesNotContain("char.IsLetterOrDigit(character)", source);
    }

    private static string GetPersistentSnapshotSourcePath() {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null) {
            string candidate = Path.Combine(directory.FullName, "Source", "SaveLoad", "akron-persistent-startpos-snapshots.cs");
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Akron repository root.");
    }
    private static string GetActionsSourcePath() {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null) {
            string candidate = Path.Combine(directory.FullName, "Source", "Actions", "akron-startpos-actions.cs");
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Akron repository root.");
    }

}
