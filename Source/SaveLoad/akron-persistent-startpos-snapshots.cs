using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using Newtonsoft.Json;

namespace Celeste.Mod.Akron;

internal static class AkronPersistentStartPosSnapshots {
    private const string SnapshotRootDirectoryName = "AkronStartPosSnapshots";
    private const string SnapshotFormat = "akron-startpos-gameplay-v2";

    private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings {
        Formatting = Formatting.None,
        ObjectCreationHandling = ObjectCreationHandling.Replace
    };

    public static bool Save(string relativePath, AkronSaveLoadSlot saveSlot, out string error) {
        error = string.Empty;
        if (saveSlot == null) {
            error = "snapshot slot is missing";
            return false;
        }

        string tempPath = string.Empty;
        try {
            string path = ResolveSnapshotPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            AkronPersistentStartPosSnapshotFile snapshot = AkronPersistentStartPosSnapshotFile.FromSlot(saveSlot);
            tempPath = path + ".tmp";
            using (FileStream file = File.Create(tempPath))
            using (GZipStream gzip = new GZipStream(file, CompressionLevel.Optimal))
            using (StreamWriter writer = new StreamWriter(gzip)) {
                JsonSerializer serializer = JsonSerializer.Create(SerializerSettings);
                serializer.Serialize(writer, snapshot);
            }

            if (File.Exists(path)) {
                File.Delete(path);
            }
            File.Move(tempPath, path);
            return true;
        } catch (Exception exception) {
            if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath)) {
                File.Delete(tempPath);
            }
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    public static bool TryLoad(string relativePath, string expectedSlotName, string expectedAreaSid, out AkronSaveLoadSlot saveSlot, out string error) {
        saveSlot = null;
        error = string.Empty;
        try {
            string path = ResolveSnapshotPath(relativePath);
            if (!File.Exists(path)) {
                error = "snapshot file is missing";
                return false;
            }

            AkronPersistentStartPosSnapshotFile snapshot;
            using (FileStream file = File.OpenRead(path))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
            using (StreamReader reader = new StreamReader(gzip)) {
                JsonSerializer serializer = JsonSerializer.Create(SerializerSettings);
                snapshot = serializer.Deserialize<AkronPersistentStartPosSnapshotFile>(new JsonTextReader(reader));
            }

            if (snapshot == null || !string.Equals(snapshot.Format, SnapshotFormat, StringComparison.Ordinal)) {
                error = "snapshot format is unsupported";
                return false;
            }

            if (!string.Equals(snapshot.SlotName, expectedSlotName, StringComparison.Ordinal)) {
                error = "snapshot slot name mismatch";
                return false;
            }

            if (!string.Equals(snapshot.MapSid, expectedAreaSid, StringComparison.Ordinal)) {
                error = "snapshot map mismatch";
                return false;
            }

            saveSlot = snapshot.ToSlot();
            return true;
        } catch (Exception exception) {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    public static void Delete(string relativePath) {
        try {
            string path = ResolveSnapshotPath(relativePath);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch (Exception exception) {
            Logger.Log(LogLevel.Warn, nameof(AkronPersistentStartPosSnapshots), "Failed to delete persistent StartPos snapshot: " + exception.Message);
        }
    }

    private static string ResolveSnapshotPath(string relativePath) {
        if (string.IsNullOrWhiteSpace(relativePath)) {
            throw new ArgumentException("Snapshot path is required.", nameof(relativePath));
        }

        string normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized) ||
            normalized.Split(Path.DirectorySeparatorChar).Any(part => part == "..")) {
            throw new InvalidDataException("Snapshot path escapes the Akron snapshot directory.");
        }

        return Path.Combine(Everest.PathGame, "Saves", SnapshotRootDirectoryName, normalized);
    }

    private sealed class AkronPersistentStartPosSnapshotFile {
        public string Format { get; set; } = SnapshotFormat;
        public string CreatedAtUtc { get; set; } = string.Empty;
        public string SlotName { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;
        public string MapSid { get; set; } = string.Empty;
        public bool SaveTimeAndDeaths { get; set; }
        public int FileSlot { get; set; }
        public float PlayerX { get; set; }
        public float PlayerY { get; set; }
        public float PlayerSpeedX { get; set; }
        public float PlayerSpeedY { get; set; }
        public int PlayerState { get; set; }
        public float Stamina { get; set; }
        public int Dashes { get; set; }
        public Facings Facing { get; set; }
        public bool HasRespawnPoint { get; set; }
        public float RespawnX { get; set; }
        public float RespawnY { get; set; }
        public long Time { get; set; }
        public int Deaths { get; set; }
        public int DeathsInCurrentLevel { get; set; }
        public long SaveDataTime { get; set; }
        public int SaveDataTotalDeaths { get; set; }
        public long AreaTimePlayed { get; set; }
        public int AreaDeaths { get; set; }
        public float LevelTimeActive { get; set; }
        public float LevelRawTimeActive { get; set; }
        public GrabModes GrabMode { get; set; }
        public CrouchDashModes CrouchDashMode { get; set; }
        public float EngineTimeRate { get; set; }
        public float GlitchValue { get; set; }
        public float DistortAnxiety { get; set; }
        public float DistortGameRate { get; set; }
        public string[] SessionFlags { get; set; } = Array.Empty<string>();
        public string[] SessionLevelFlags { get; set; } = Array.Empty<string>();
        public Dictionary<string, int> SessionCounters { get; set; } = new Dictionary<string, int>();
        public List<AkronSessionEntityId> SessionStrawberries { get; set; } = new List<AkronSessionEntityId>();
        public List<AkronSessionEntityId> SessionDoNotLoad { get; set; } = new List<AkronSessionEntityId>();
        public List<AkronSessionEntityId> SessionKeys { get; set; } = new List<AkronSessionEntityId>();
        public bool[] SessionSummitGems { get; set; }
        public int InventoryDashes { get; set; }
        public bool InventoryDreamDash { get; set; }
        public bool InventoryBackpack { get; set; }
        public bool InventoryNoRefills { get; set; }
        public int SessionDashes { get; set; }
        public int SessionDashesAtLevelStart { get; set; }
        public bool SessionDreaming { get; set; }
        public string SessionStartCheckpoint { get; set; } = string.Empty;
        public string SessionFurthestSeenLevel { get; set; } = string.Empty;
        public Session.CoreModes SessionCoreMode { get; set; }

        public static AkronPersistentStartPosSnapshotFile FromSlot(AkronSaveLoadSlot saveSlot) {
            return new AkronPersistentStartPosSnapshotFile {
                Format = SnapshotFormat,
                CreatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                SlotName = saveSlot.SlotName,
                LevelName = saveSlot.LevelName,
                MapSid = saveSlot.MapSid,
                SaveTimeAndDeaths = saveSlot.SaveTimeAndDeaths,
                FileSlot = saveSlot.FileSlot,
                PlayerX = saveSlot.PlayerPosition.X,
                PlayerY = saveSlot.PlayerPosition.Y,
                PlayerSpeedX = saveSlot.PlayerSpeed.X,
                PlayerSpeedY = saveSlot.PlayerSpeed.Y,
                PlayerState = saveSlot.PlayerState,
                Stamina = saveSlot.Stamina,
                Dashes = saveSlot.Dashes,
                Facing = saveSlot.Facing,
                HasRespawnPoint = saveSlot.RespawnPoint.HasValue,
                RespawnX = saveSlot.RespawnPoint?.X ?? 0f,
                RespawnY = saveSlot.RespawnPoint?.Y ?? 0f,
                Time = saveSlot.Time,
                Deaths = saveSlot.Deaths,
                DeathsInCurrentLevel = saveSlot.DeathsInCurrentLevel,
                SaveDataTime = saveSlot.SaveDataTime,
                SaveDataTotalDeaths = saveSlot.SaveDataTotalDeaths,
                AreaTimePlayed = saveSlot.AreaTimePlayed,
                AreaDeaths = saveSlot.AreaDeaths,
                LevelTimeActive = saveSlot.LevelTimeActive,
                LevelRawTimeActive = saveSlot.LevelRawTimeActive,
                GrabMode = saveSlot.GrabMode,
                CrouchDashMode = saveSlot.CrouchDashMode,
                EngineTimeRate = saveSlot.EngineTimeRate,
                GlitchValue = saveSlot.GlitchValue,
                DistortAnxiety = saveSlot.DistortAnxiety,
                DistortGameRate = saveSlot.DistortGameRate,
                SessionFlags = (saveSlot.SessionFlags ?? new HashSet<string>()).OrderBy(flag => flag, StringComparer.Ordinal).ToArray(),
                SessionLevelFlags = (saveSlot.SessionLevelFlags ?? new HashSet<string>()).OrderBy(flag => flag, StringComparer.Ordinal).ToArray(),
                SessionCounters = new Dictionary<string, int>(saveSlot.SessionCounters ?? new Dictionary<string, int>(), StringComparer.Ordinal),
                SessionStrawberries = new List<AkronSessionEntityId>(saveSlot.SessionStrawberries ?? new List<AkronSessionEntityId>()),
                SessionDoNotLoad = new List<AkronSessionEntityId>(saveSlot.SessionDoNotLoad ?? new List<AkronSessionEntityId>()),
                SessionKeys = new List<AkronSessionEntityId>(saveSlot.SessionKeys ?? new List<AkronSessionEntityId>()),
                SessionSummitGems = saveSlot.SessionSummitGems == null ? null : (bool[]) saveSlot.SessionSummitGems.Clone(),
                InventoryDashes = saveSlot.InventoryDashes,
                InventoryDreamDash = saveSlot.InventoryDreamDash,
                InventoryBackpack = saveSlot.InventoryBackpack,
                InventoryNoRefills = saveSlot.InventoryNoRefills,
                SessionDashes = saveSlot.SessionDashes,
                SessionDashesAtLevelStart = saveSlot.SessionDashesAtLevelStart,
                SessionDreaming = saveSlot.SessionDreaming,
                SessionStartCheckpoint = saveSlot.SessionStartCheckpoint ?? string.Empty,
                SessionFurthestSeenLevel = saveSlot.SessionFurthestSeenLevel ?? string.Empty,
                SessionCoreMode = saveSlot.SessionCoreMode
            };
        }

        public AkronSaveLoadSlot ToSlot() {
            AkronSaveLoadSlot saveSlot = new AkronSaveLoadSlot(SlotName, LevelName, MapSid, SaveTimeAndDeaths) {
                // Persistent StartPos snapshots intentionally bind to the current
                // Akron process session when they are loaded. The disk format is a
                // gameplay bookmark, not a same-process clone identity token.
                SessionNonce = AkronModule.Session?.CurrentSessionNonce ?? string.Empty,
                PlayerPosition = new Vector2(PlayerX, PlayerY),
                PlayerSpeed = new Vector2(PlayerSpeedX, PlayerSpeedY),
                PlayerState = PlayerState,
                Stamina = Stamina,
                Dashes = Dashes,
                Facing = Facing,
                RespawnPoint = HasRespawnPoint ? new Vector2(RespawnX, RespawnY) : null,
                Time = Time,
                Deaths = Deaths,
                DeathsInCurrentLevel = DeathsInCurrentLevel,
                FileSlot = SaveData.Instance?.FileSlot ?? FileSlot,
                SaveDataTime = SaveDataTime,
                SaveDataTotalDeaths = SaveDataTotalDeaths,
                AreaTimePlayed = AreaTimePlayed,
                AreaDeaths = AreaDeaths,
                LevelTimeActive = LevelTimeActive,
                LevelRawTimeActive = LevelRawTimeActive,
                GrabMode = GrabMode,
                CrouchDashMode = CrouchDashMode,
                EngineTimeRate = EngineTimeRate,
                GlitchValue = GlitchValue,
                DistortAnxiety = DistortAnxiety,
                DistortGameRate = DistortGameRate,
                SessionFlags = new HashSet<string>(SessionFlags ?? Array.Empty<string>()),
                SessionLevelFlags = new HashSet<string>(SessionLevelFlags ?? Array.Empty<string>()),
                SessionCounters = new Dictionary<string, int>(SessionCounters ?? new Dictionary<string, int>(), StringComparer.Ordinal),
                SessionStrawberries = new List<AkronSessionEntityId>(SessionStrawberries ?? new List<AkronSessionEntityId>()),
                SessionDoNotLoad = new List<AkronSessionEntityId>(SessionDoNotLoad ?? new List<AkronSessionEntityId>()),
                SessionKeys = new List<AkronSessionEntityId>(SessionKeys ?? new List<AkronSessionEntityId>()),
                SessionSummitGems = SessionSummitGems == null ? null : (bool[]) SessionSummitGems.Clone(),
                InventoryDashes = InventoryDashes,
                InventoryDreamDash = InventoryDreamDash,
                InventoryBackpack = InventoryBackpack,
                InventoryNoRefills = InventoryNoRefills,
                SessionDashes = SessionDashes,
                SessionDashesAtLevelStart = SessionDashesAtLevelStart,
                SessionDreaming = SessionDreaming,
                SessionStartCheckpoint = SessionStartCheckpoint ?? string.Empty,
                SessionFurthestSeenLevel = SessionFurthestSeenLevel ?? string.Empty,
                SessionCoreMode = SessionCoreMode
            };
            return saveSlot;
        }
    }
}
