using Celeste;
using Monocle;
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Celeste.Mod.Akron;

public static class AkronPracticeStats {
    public static string ExportRoomStats(Level level) {
        if (level == null || !AkronModule.TryUse(AkronFeatureKind.SplitHelper)) {
            return string.Empty;
        }

        FinalizeCurrentRoom(level);
        string directory = Path.Combine(Everest.PathGame, "Saves", "AkronRoomStats");
        Directory.CreateDirectory(directory);
        string sid = SanitizeFilename(level.Session.Area.GetSID());
        string path = Path.Combine(directory, sid + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".tsv");
        WriteRoomStatsTsv(level.Session.Area.GetSID(), path);
        AkronModule.Session.LastRoomStatsExportPath = path;
        Engine.Scene?.Add(new AkronToast("Room stats exported: " + Path.GetFileName(path)));
        return path;
    }

    public static void WriteRoomStatsTsv(string areaSid, string path) {
        using StreamWriter writer = new StreamWriter(path);
        writer.WriteLine("area\troom\tvisits\tdeaths\tstrawberries\tlast_igt\tbest_igt\tlast_alive");
        foreach (AkronRoomStatRecord record in AkronModule.SaveData.RoomStats.Values
                     .Where(record => record != null && string.Equals(record.AreaSid, areaSid, StringComparison.Ordinal))
                     .OrderBy(record => record.Room, StringComparer.OrdinalIgnoreCase)) {
            writer.WriteLine(
                EscapeTsv(record.AreaSid) + "\t" +
                EscapeTsv(record.Room) + "\t" +
                record.Visits.ToString(CultureInfo.InvariantCulture) + "\t" +
                record.Deaths.ToString(CultureInfo.InvariantCulture) + "\t" +
                record.Strawberries.ToString(CultureInfo.InvariantCulture) + "\t" +
                FormatTicks(record.LastInGameTime) + "\t" +
                FormatTicks(record.BestInGameTime) + "\t" +
                FormatTicks(record.LastAliveTime));
        }
    }

    public static void OnLevelBegin(Level level) {
        AkronModule.Session.TrackedRoom = level.Session.Level;
        AkronModule.Session.RoomEnteredAt = level.Session.Time;
        AkronModule.Session.AttemptStartedAt = level.Session.Time;
        ResetRoomStatTracker(level.Session.Time);
        AkronModule.Session.LastRoomTime = 0;
        AkronModule.Session.DeathPbLossPromptShown = false;
    }

    public static void OnLevelUpdate(Level level) {
        UpdateRoomStatFreeze(level);

        if (AkronModule.Session.TrackedRoom == level.Session.Level) {
            return;
        }

        FinalizeRoom(level, AkronModule.Session.TrackedRoom);
        AkronModule.Session.TrackedRoom = level.Session.Level;
        AkronModule.Session.RoomEnteredAt = level.Session.Time;
        ResetRoomStatTracker(level.Session.Time);
    }

    public static long GetCurrentRoomTime(Level level) {
        return level.Session.Time - AkronModule.Session.RoomEnteredAt;
    }

    public static long GetCurrentMapTime(Level level) {
        return level.Session.Time;
    }

    public static long GetCurrentSegmentTime(Level level) {
        return level.Session.Time - AkronModule.Session.AttemptStartedAt;
    }

    public static long GetCurrentAliveTime(Level level) {
        return level.Session.Time - AkronModule.Session.RoomStatAliveStartedAt;
    }

    public static long GetCurrentRoomStatTime(Level level) {
        return Math.Max(0, GetCurrentRoomTime(level) - GetFrozenDuration(level.Session.Time, AkronModule.Session.RoomStatFrozenStartedAt, AkronModule.Session.RoomStatFrozenDuration));
    }

    public static long GetCurrentRoomStatAliveTime(Level level) {
        return Math.Max(0, GetCurrentAliveTime(level) - GetFrozenDuration(level.Session.Time, AkronModule.Session.RoomStatAliveFrozenStartedAt, AkronModule.Session.RoomStatAliveFrozenDuration));
    }

    public static void NotifyRespawnTimerReset(Level level) {
        long now = level?.Session?.Time ?? 0;
        AkronModule.Session.RoomStatAliveStartedAt = now;
        AkronModule.Session.RoomStatAliveFrozenDuration = 0;
        AkronModule.Session.RoomStatAliveFrozenStartedAt = level != null && IsRoomStatFreezeActive(level) ? now : -1;
    }

    public static void NotifyRoomStrawberryCollected() {
        AkronModule.Session.RoomStatStrawberries++;
    }

    public static long? GetBestRoomTime(Level level) {
        string key = BuildKey(level.Session.Area.GetSID(), level.Session.Level);
        return AkronModule.SaveData.BestRoomTimes.TryGetValue(key, out long best) ? best : null;
    }

    public static long? GetBestSegmentTime(Level level) {
        string key = BuildKey(level.Session.Area.GetSID(), level.Session.Level);
        return AkronModule.SaveData.BestSegmentTimes.TryGetValue(key, out long best) ? best : null;
    }

    public static void ResetAttemptTimer(Level level) {
        AkronModule.Session.AttemptStartedAt = level.Session.Time;
    }

    public static void FinalizeCurrentRoom(Level level) {
        if (level == null || AkronModule.Session == null) {
            return;
        }

        FinalizeRoom(level, AkronModule.Session.TrackedRoom);
    }

    private static void FinalizeRoom(Level level, string roomName) {
        long roomTime = GetCurrentRoomStatTime(level);
        if (string.IsNullOrWhiteSpace(roomName) || roomTime <= 0) {
            return;
        }

        AkronModule.Session.LastRoomTime = roomTime;
        string key = BuildKey(level.Session.Area.GetSID(), roomName);
        AkronRoomStatRecord record = GetOrCreateRoomStat(level, roomName, key);
        record.Visits++;
        record.Deaths += Math.Max(0, AkronModule.Session.DeathsSinceRoomTransition);
        record.Strawberries += Math.Max(0, AkronModule.Session.RoomStatStrawberries);
        record.LastInGameTime = roomTime;
        record.LastAliveTime = GetCurrentRoomStatAliveTime(level);
        if (record.BestInGameTime <= 0 || roomTime < record.BestInGameTime) {
            record.BestInGameTime = roomTime;
        }

        if (!AkronModule.SaveData.BestRoomTimes.TryGetValue(key, out long bestRoom) || roomTime < bestRoom) {
            AkronModule.SaveData.BestRoomTimes[key] = roomTime;
        }

        long segmentTime = level.Session.Time - AkronModule.Session.AttemptStartedAt;
        if (segmentTime > 0 && (!AkronModule.SaveData.BestSegmentTimes.TryGetValue(key, out long bestSegment) || segmentTime < bestSegment)) {
            AkronModule.SaveData.BestSegmentTimes[key] = segmentTime;
        }
    }

    private static AkronRoomStatRecord GetOrCreateRoomStat(Level level, string roomName, string key) {
        if (!AkronModule.SaveData.RoomStats.TryGetValue(key, out AkronRoomStatRecord record) || record == null) {
            record = new AkronRoomStatRecord {
                AreaSid = level.Session.Area.GetSID(),
                Room = roomName
            };
            AkronModule.SaveData.RoomStats[key] = record;
        }

        record.AreaSid = level.Session.Area.GetSID();
        record.Room = roomName;
        return record;
    }

    private static string BuildKey(string mapSid, string roomName) {
        return mapSid + "|" + roomName;
    }

    private static string FormatTicks(long ticks) {
        return TimeSpan.FromTicks(Math.Max(0, ticks)).ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static string EscapeTsv(string value) {
        return (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    private static string SanitizeFilename(string value) {
        string sanitized = string.Join("_", (value ?? "map").Split(Path.GetInvalidFileNameChars()));
        return string.IsNullOrWhiteSpace(sanitized) ? "map" : sanitized;
    }

    public static bool ShouldFreezeRoomStatTimers(AkronRoomStatTimerFreezeMode mode, bool paused, bool inactive) {
        return ShouldFreezeRoomStatTimers(mode, paused, inactive, false);
    }

    public static bool ShouldFreezeRoomStatTimers(AkronRoomStatTimerFreezeMode mode, bool paused, bool inactive, bool cutscene) {
        return mode switch {
            AkronRoomStatTimerFreezeMode.Paused => paused,
            AkronRoomStatTimerFreezeMode.Inactive => inactive,
            AkronRoomStatTimerFreezeMode.Cutscene => cutscene,
            AkronRoomStatTimerFreezeMode.PausedOrInactive => paused || inactive,
            AkronRoomStatTimerFreezeMode.PausedInactiveOrCutscene => paused || inactive || cutscene,
            _ => false
        };
    }

    private static void ResetRoomStatTracker(long now) {
        AkronModule.Session.RoomStatAliveStartedAt = now;
        AkronModule.Session.RoomStatStrawberries = 0;
        AkronModule.Session.RoomStatFrozenStartedAt = -1;
        AkronModule.Session.RoomStatFrozenDuration = 0;
        AkronModule.Session.RoomStatAliveFrozenStartedAt = -1;
        AkronModule.Session.RoomStatAliveFrozenDuration = 0;
    }

    private static void UpdateRoomStatFreeze(Level level) {
        long now = level.Session.Time;
        bool frozen = IsRoomStatFreezeActive(level);
        (AkronModule.Session.RoomStatFrozenStartedAt, AkronModule.Session.RoomStatFrozenDuration) =
            UpdateFreezeState(now, frozen, AkronModule.Session.RoomStatFrozenStartedAt, AkronModule.Session.RoomStatFrozenDuration);
        (AkronModule.Session.RoomStatAliveFrozenStartedAt, AkronModule.Session.RoomStatAliveFrozenDuration) =
            UpdateFreezeState(now, frozen, AkronModule.Session.RoomStatAliveFrozenStartedAt, AkronModule.Session.RoomStatAliveFrozenDuration);
    }

    private static bool IsRoomStatFreezeActive(Level level) {
        bool inactive = Engine.Instance != null && !Engine.Instance.IsActive;
        bool paused = level.Paused || level.wasPaused;
        bool cutscene = level.InCutscene || level.SkippingCutscene;
        return ShouldFreezeRoomStatTimers(AkronModule.Settings.RoomStatTimerFreezeMode, paused, inactive, cutscene);
    }

    private static (long StartedAt, long Duration) UpdateFreezeState(long now, bool frozen, long startedAt, long duration) {
        if (frozen) {
            if (startedAt < 0) {
                startedAt = now;
            }

            return (startedAt, duration);
        }

        if (startedAt >= 0) {
            duration += Math.Max(0, now - startedAt);
            startedAt = -1;
        }

        return (startedAt, duration);
    }

    private static long GetFrozenDuration(long now, long startedAt, long duration) {
        if (startedAt >= 0) {
            return duration + Math.Max(0, now - startedAt);
        }

        return duration;
    }
}
