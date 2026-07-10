using System;
using System.Collections.Generic;
using System.IO;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
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

    [Fact]
    public void LoadingStartPosArmsNativeDeathReloadAfterSuccessfulRestore() {
        string source = File.ReadAllText(GetActionsSourcePath());
        string playerRuntimeSource = File.ReadAllText(GetPlayerRuntimeSourcePath());

        Assert.Contains("enableRespawnAtStartPosAfterRestore: true", source);
        Assert.Contains("enableRespawnAtStartPosAfterRestore && restoredStartPos", source);
        Assert.Contains("restoredStartPos && loadedSlot > 0", source);
        Assert.Contains("RestoreStartPosAfterDeath(Level level, AkronStartPos startPos)", source);
        Assert.Contains("deadBody.DeathAction = () =>", playerRuntimeSource);
        Assert.Contains("deadBody.DeathAction == null", playerRuntimeSource);
        Assert.Contains("!deadBody.HasGolden", playerRuntimeSource);
        Assert.Contains("if (Engine.Scene != level)", source);
        Assert.Contains("SpotlightWipe.FocusPoint = respawnPoint - restoredLevel.Camera.Position;", source);
        Assert.Contains("restoredLevel.DoScreenWipe(wipeIn: true);", source);
        Assert.Contains("level.Reload();", source);
        Assert.Equal(1, playerRuntimeSource.Split("AkronActions.RestoreStartPosAfterDeath(level, startPosRespawn)").Length - 1);
    }

    [Fact]
    public void StartPosSnapshotsDoNotKeepDormantSoundHandles() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        string deepCloneSource = File.ReadAllText(GetDeepCloneSourcePath());
        string eventInstanceSource = File.ReadAllText(GetEventInstanceSourcePath());

        Assert.Contains("SavedLevelEventInstances = AkronDeepClone.CopyIntoDormant", source);
        Assert.Contains("restoredEventInstances.AddRange(AkronDeepClone.CopyIntoDormant(savedLevel, level));", source);
        Assert.Contains("ActivateDormantEventInstances(restoredEventInstances);", source);
        Assert.Contains("ReleaseDormantEventInstances(saveSlot.SavedLevelEventInstances);", source);
        Assert.Contains("saveSlot.PreCloneState = null;", source);
        Assert.Contains("ReleaseDormantEventInstances(saveSlot);", source);
        Assert.Contains("AkronEventInstanceUtils.Clone(eventInstance, cloneEventInstancesAsDormant)", deepCloneSource);
        Assert.Contains("DormantPlaybackStates.Add(clone", eventInstanceSource);
        Assert.Contains("eventInstance.start();", eventInstanceSource);
        Assert.Contains("eventInstance.release();", eventInstanceSource);
        Assert.DoesNotContain("DetachClonedSoundSourceInstances", source);
    }

    [Fact]
    public void FullStateStartPosDeathRespawnCanCrossRooms() {
        string source = File.ReadAllText(GetActionsSourcePath());
        string playerRuntimeSource = File.ReadAllText(GetPlayerRuntimeSourcePath());

        Assert.Contains("IsStartPosUsableForDeath(level, lastLoaded)", source);
        Assert.Contains("!string.IsNullOrWhiteSpace(startPos.StateSlotName)", source);
        Assert.Contains("if (string.Equals(startPos.Room, level.Session.Level", playerRuntimeSource);
        Assert.DoesNotContain("string.Equals(startPos.Room, level.Session.Level) &&\n                (string.IsNullOrWhiteSpace(startPos.AreaSid)", playerRuntimeSource);
    }

    [Fact]
    public void PortableRoomStateSnapshotStripsStatsAndKeepsGameplayState() {
        AkronSaveLoadSlot source = new AkronSaveLoadSlot("Akron StartPos test 7", "room-a", "Maps/Current", saveTimeAndDeaths: true) {
            SessionNonce = "original-session",
            PlayerPosition = new Vector2(12.5f, 34.25f),
            PlayerSpeed = new Vector2(90f, -15f),
            PlayerState = 7,
            Stamina = 42f,
            Dashes = 2,
            Facing = Facings.Right,
            RespawnPoint = new Vector2(10f, 20f),
            Time = 123456L,
            Deaths = 8,
            DeathsInCurrentLevel = 3,
            FileSlot = 2,
            SaveDataTime = 987654L,
            SaveDataTotalDeaths = 99,
            AreaTimePlayed = 456789L,
            AreaDeaths = 12,
            LevelTimeActive = 98.5f,
            LevelRawTimeActive = 111.5f,
            EngineTimeRate = 0.5f,
            GlitchValue = 0.25f,
            DistortAnxiety = 0.75f,
            DistortGameRate = 1.25f,
            SessionFlags = new HashSet<string> { "berry-collected" },
            SessionLevelFlags = new HashSet<string> { "room-switch" },
            SessionCounters = new Dictionary<string, int> { ["cycles"] = 4 },
            SessionStrawberries = new List<AkronSessionEntityId> { new AkronSessionEntityId { Level = "room-a", ID = 10 } },
            SessionDoNotLoad = new List<AkronSessionEntityId> { new AkronSessionEntityId { Level = "room-a", ID = 11 } },
            SessionKeys = new List<AkronSessionEntityId> { new AkronSessionEntityId { Level = "room-a", ID = 12 } },
            SessionSummitGems = new[] { true, false, true },
            InventoryDashes = 2,
            InventoryDreamDash = true,
            InventoryBackpack = true,
            InventoryNoRefills = true,
            SessionDashes = 2,
            SessionDashesAtLevelStart = 1,
            SessionDreaming = true,
            SessionStartCheckpoint = "checkpoint-a",
            SessionFurthestSeenLevel = "room-b",
            SessionCoreMode = Session.CoreModes.Hot
        };

        Assert.True(AkronPersistentStartPosSnapshots.TrySerializePortableRoomState(source, out string payload, out string serializeError), serializeError);
        Assert.True(AkronPersistentStartPosSnapshots.TryDeserializePortableRoomStateForTesting(payload, "Akron StartPos test 7", "Maps/Current", out AkronSaveLoadSlot restored, out string deserializeError), deserializeError);

        Assert.False(restored.SaveTimeAndDeaths);
        Assert.Equal(0L, restored.Time);
        Assert.Equal(0, restored.Deaths);
        Assert.Equal(0, restored.DeathsInCurrentLevel);
        Assert.Equal(0L, restored.SaveDataTime);
        Assert.Equal(0, restored.SaveDataTotalDeaths);
        Assert.Equal(0L, restored.AreaTimePlayed);
        Assert.Equal(0, restored.AreaDeaths);
        Assert.Equal(0f, restored.LevelTimeActive);
        Assert.Equal(0f, restored.LevelRawTimeActive);
        Assert.Equal(source.PlayerPosition.X, restored.PlayerPosition.X);
        Assert.Equal(source.PlayerPosition.Y, restored.PlayerPosition.Y);
        Assert.Equal(source.PlayerSpeed.X, restored.PlayerSpeed.X);
        Assert.Equal(source.PlayerSpeed.Y, restored.PlayerSpeed.Y);
        Assert.Equal(source.PlayerState, restored.PlayerState);
        Assert.Equal(source.Stamina, restored.Stamina);
        Assert.Equal(source.Dashes, restored.Dashes);
        Assert.Equal(source.Facing, restored.Facing);
        Assert.True(restored.RespawnPoint.HasValue);
        Assert.Equal(source.RespawnPoint!.Value.X, restored.RespawnPoint.Value.X);
        Assert.Equal(source.RespawnPoint.Value.Y, restored.RespawnPoint.Value.Y);
        Assert.Contains("berry-collected", restored.SessionFlags);
        Assert.Contains("room-switch", restored.SessionLevelFlags);
        Assert.Equal(4, restored.SessionCounters["cycles"]);
        Assert.Contains(restored.SessionDoNotLoad, id => id.Level == "room-a" && id.ID == 11);
        Assert.Equal(2, restored.InventoryDashes);
        Assert.True(restored.InventoryDreamDash);
        Assert.True(restored.SessionDreaming);
        Assert.Equal(Session.CoreModes.Hot, restored.SessionCoreMode);
    }

    [Fact]
    public void PortableRoomStateSnapshotRejectsWrongMap() {
        AkronSaveLoadSlot source = new AkronSaveLoadSlot("Akron StartPos test 2", "room-a", "Maps/Current", saveTimeAndDeaths: false);

        Assert.True(AkronPersistentStartPosSnapshots.TrySerializePortableRoomState(source, out string payload, out string serializeError), serializeError);
        Assert.False(AkronPersistentStartPosSnapshots.TryDeserializePortableRoomStateForTesting(payload, "Akron StartPos test 2", "Maps/Other", out _, out string deserializeError));
        Assert.Equal("snapshot map mismatch", deserializeError);
    }

    [Fact]
    public void PortableRoomStateSnapshotRejectsOversizedExports() {
        Random random = new Random(1234);
        HashSet<string> flags = new HashSet<string>();
        for (int index = 0; index < 35000; index++) {
            byte[] bytes = new byte[32];
            random.NextBytes(bytes);
            flags.Add(Convert.ToBase64String(bytes));
        }

        AkronSaveLoadSlot source = new AkronSaveLoadSlot("Akron StartPos test 8", "room-a", "Maps/Current", saveTimeAndDeaths: false) {
            SessionFlags = flags
        };

        Assert.False(AkronPersistentStartPosSnapshots.TrySerializePortableRoomState(source, out string payload, out string serializeError));
        Assert.Empty(payload);
        Assert.Contains("portable room-state snapshot is too large", serializeError);
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

    private static string GetPlayerRuntimeSourcePath() {
        return GetSourcePath("Module", "akron-module-player-runtime.cs");
    }

    private static string GetSaveLoadSourcePath() {
        return GetSourcePath("SaveLoad", "AkronSaveLoad.cs");
    }

    private static string GetDeepCloneSourcePath() {
        return GetSourcePath("Core", "AkronDeepClone.cs");
    }

    private static string GetEventInstanceSourcePath() {
        return GetSourcePath("Core", "akron-event-instance-utils.cs");
    }

    private static string GetSourcePath(string directoryName, string fileName) {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null) {
            string candidate = Path.Combine(directory.FullName, "Source", directoryName, fileName);
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Akron repository root.");
    }

}
