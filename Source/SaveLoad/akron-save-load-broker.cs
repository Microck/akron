using System;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronSaveLoadService {
    private static bool CanAccessNativeState(Level level, out string reason, bool allowDeadPlayer = false) {
        Player player = level.Tracker.GetEntity<Player>();
        if (level.Paused) {
            reason = "Native StartPos restores are blocked while paused.";
            return false;
        }
        if (level.Transitioning || level.InCutscene || level.SkippingCutscene) {
            reason = "Native StartPos restores are blocked during transitions and cutscenes.";
            return false;
        }
        if (!allowDeadPlayer && player != null && player.Dead) {
            reason = "Native StartPos restores are blocked while the player is dead.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static AkronSaveLoadResult TryBrokerSave(int slot) {
        if (AkronSpeedrunToolBroker.Available) {
            AkronPolicy.RecordFeatureUse(AkronFeatureKind.BrokeredSavestates);
            return AkronSpeedrunToolBroker.Save(slot);
        }

        return AkronModule.Settings.SpeedrunToolBrokerWarnings
            ? AkronSaveLoadResult.BrokerUnavailable
            : AkronSaveLoadResult.Blocked;
    }

    private static AkronSaveLoadResult TryBrokerLoad(Level level, int slot) {
        if (AkronSpeedrunToolBroker.Available) {
            long currentSessionTime = level?.Session?.Time ?? 0L;
            int currentDeaths = level?.Session?.Deaths ?? 0;
            int currentDeathsInRoom = level?.Session?.DeathsInCurrentLevel ?? 0;
            float currentLevelTimeActive = level?.TimeActive ?? 0f;
            float currentLevelRawTimeActive = level?.RawTimeActive ?? 0f;
            long currentSaveDataTime = SaveData.Instance?.Time ?? 0L;
            int currentTotalDeaths = SaveData.Instance?.TotalDeaths ?? 0;
            AreaKey? currentAreaKey = level?.Session?.Area;
            long currentAreaTimePlayed = currentAreaKey.HasValue
                ? SaveData.Instance?.Areas_Safe[currentAreaKey.Value.ID].Modes[(int) currentAreaKey.Value.Mode].TimePlayed ?? 0L
                : 0L;
            int currentAreaDeaths = currentAreaKey.HasValue
                ? SaveData.Instance?.Areas_Safe[currentAreaKey.Value.ID].Modes[(int) currentAreaKey.Value.Mode].Deaths ?? 0
                : 0;

            AkronPolicy.RecordFeatureUse(AkronFeatureKind.BrokeredSavestates);
            AkronSaveLoadResult result = AkronSpeedrunToolBroker.Load(slot);
            if (result == AkronSaveLoadResult.Success && !AkronModule.Settings.SaveTimeAndDeaths) {
                RestoreBrokerTimeAndDeaths(
                    level,
                    currentSessionTime,
                    currentDeaths,
                    currentDeathsInRoom,
                    currentLevelTimeActive,
                    currentLevelRawTimeActive,
                    currentSaveDataTime,
                    currentTotalDeaths,
                    currentAreaKey,
                    currentAreaTimePlayed,
                    currentAreaDeaths
                );
            }
            return result;
        }

        return AkronModule.Settings.SpeedrunToolBrokerWarnings
            ? AkronSaveLoadResult.BrokerUnavailable
            : AkronSaveLoadResult.Blocked;
    }

    private static void RestoreBrokerTimeAndDeaths(
        Level fallbackLevel,
        long currentSessionTime,
        int currentDeaths,
        int currentDeathsInRoom,
        float currentLevelTimeActive,
        float currentLevelRawTimeActive,
        long currentSaveDataTime,
        int currentTotalDeaths,
        AreaKey? currentAreaKey,
        long currentAreaTimePlayed,
        int currentAreaDeaths
    ) {
        Level level = Engine.Scene as Level ?? fallbackLevel;
        if (level?.Session != null) {
            level.Session.Time = Math.Max(currentSessionTime, level.Session.Time);
            level.Session.Deaths = Math.Max(currentDeaths, level.Session.Deaths);
            level.Session.DeathsInCurrentLevel = Math.Max(currentDeathsInRoom, level.Session.DeathsInCurrentLevel);
            level.TimeActive = Math.Max(currentLevelTimeActive, level.TimeActive);
            level.RawTimeActive = Math.Max(currentLevelRawTimeActive, level.RawTimeActive);
        }

        if (SaveData.Instance == null) {
            return;
        }

        SaveData.Instance.Time = Math.Max(currentSaveDataTime, SaveData.Instance.Time);
        SaveData.Instance.TotalDeaths = Math.Max(currentTotalDeaths, SaveData.Instance.TotalDeaths);
        if (currentAreaKey.HasValue) {
            SaveData.Instance.Areas_Safe[currentAreaKey.Value.ID].Modes[(int) currentAreaKey.Value.Mode].TimePlayed =
                Math.Max(currentAreaTimePlayed, SaveData.Instance.Areas_Safe[currentAreaKey.Value.ID].Modes[(int) currentAreaKey.Value.Mode].TimePlayed);
            SaveData.Instance.Areas_Safe[currentAreaKey.Value.ID].Modes[(int) currentAreaKey.Value.Mode].Deaths =
                Math.Max(currentAreaDeaths, SaveData.Instance.Areas_Safe[currentAreaKey.Value.ID].Modes[(int) currentAreaKey.Value.Mode].Deaths);
        }
    }
}
