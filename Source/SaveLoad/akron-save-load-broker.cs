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

    private static bool ShouldBrokerSavestatesInsteadOfNative() {
#if NET8_0_OR_GREATER
        // Akron currently ships and is live-tested through the core runtime build.
        // Native level restoration is still unstable there, so StartPos restores use the
        // established Speedrun Tool broker path instead of pretending the native
        // path is production-ready.
        return true;
#else
        return false;
#endif
    }

    private static bool IsRisky(Level level, int slot, out string reason) {
        if (!CanAccessNativeState(level, out reason)) {
            return true;
        }

        if (AkronMapOverrides.ShouldForceBroker(level)) {
            reason = "Current-map override forces brokered StartPos restores.";
            return true;
        }

        if (HasRiskyScriptedEntities(level)) {
            reason = "Risky scripted content was detected. Akron falls back to broker or block on Lua-like runtime scripts.";
            return true;
        }

        foreach (AkronSaveLoadRiskHandler handler in RiskHandlers) {
            if (handler(level, slot, out reason)) {
                return true;
            }
        }

        // Everest-safe stays conservative through explicit risk detection and per-map
        // compatibility overrides. It should not blanket-block every non-vanilla map.
        reason = string.Empty;
        return false;
    }

    private static bool HasRiskyScriptedEntities(Level level) {
        foreach (Entity entity in level.Entities) {
            string fullName = entity.GetType().FullName ?? string.Empty;
            if (fullName.IndexOf("LuaCutscene", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullName.IndexOf("Lua", StringComparison.OrdinalIgnoreCase) >= 0 && fullName.IndexOf("Cutscene", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullName.IndexOf("Lua", StringComparison.OrdinalIgnoreCase) >= 0 && fullName.IndexOf("Trigger", StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
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

    private static bool TryUseUnsafeNativeOverride(Level level) {
        if (!IsUnsafeNativeOverrideEnabled(level) || !CanUseUnsafeNativeOverride(level)) {
            return false;
        }

        AkronPolicy.RecordFeatureUse(AkronFeatureKind.UnsafeNativeSavestateOverride);
        return true;
    }

    private static bool IsUnsafeNativeOverrideEnabled(Level level) {
        return AkronModule.Settings.UnsafeSavestateOverride || AkronMapOverrides.ShouldAllowUnsafeSavestates(level);
    }

    private static bool CanUseUnsafeNativeOverride(Level level) {
        if (!IsUnsafeNativeOverrideEnabled(level)) {
            return false;
        }

        AkronPolicyDecision policy = AkronPolicy.CanUse(AkronFeatureKind.UnsafeNativeSavestateOverride);
        return policy.Allowed;
    }
}
