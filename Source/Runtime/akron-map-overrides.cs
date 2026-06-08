using System.Collections.Generic;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static class AkronMapOverrides {
    public static string GetMapSid(Level level) {
        return level?.Session?.Area.GetSID() ?? string.Empty;
    }

    public static AkronMapOverride Get(Level level) {
        string mapSid = GetMapSid(level);
        if (string.IsNullOrWhiteSpace(mapSid)) {
            return null;
        }

        return AkronModule.Settings.MapOverrides.TryGetValue(mapSid, out AkronMapOverride mapOverride)
            ? mapOverride
            : null;
    }

    public static AkronMapOverride GetOrCreate(Level level) {
        string mapSid = GetMapSid(level);
        if (string.IsNullOrWhiteSpace(mapSid)) {
            return new AkronMapOverride();
        }

        if (!AkronModule.Settings.MapOverrides.TryGetValue(mapSid, out AkronMapOverride mapOverride)) {
            mapOverride = new AkronMapOverride();
            AkronModule.Settings.MapOverrides[mapSid] = mapOverride;
        }

        return mapOverride;
    }

    public static bool ShouldForceBroker(Level level) {
        return Get(level)?.AlwaysUseBroker ?? false;
    }

    public static bool ShouldAllowUnsafeSavestates(Level level) {
        return Get(level)?.AllowUnsafeSavestates ?? false;
    }

    public static bool ShouldDisableEverestSafeBlock(Level level) {
        return Get(level)?.DisableEverestSafeBlock ?? false;
    }

    public static string Describe(Level level) {
        AkronMapOverride mapOverride = Get(level);
        if (mapOverride == null) {
            return "No current-map overrides";
        }

        List<string> flags = new List<string>();
        if (mapOverride.AlwaysUseBroker) {
            flags.Add("Force broker");
        }
        if (mapOverride.AllowUnsafeSavestates) {
            flags.Add("Allow unsafe native");
        }
        if (mapOverride.DisableEverestSafeBlock) {
            flags.Add("Disable Everest-safe block");
        }

        return flags.Count == 0 ? "No current-map overrides" : string.Join(", ", flags);
    }

    public static void ToggleForceBroker(Level level) {
        AkronMapOverride mapOverride = GetOrCreate(level);
        mapOverride.AlwaysUseBroker = !mapOverride.AlwaysUseBroker;
    }

    public static void ToggleAllowUnsafe(Level level) {
        AkronMapOverride mapOverride = GetOrCreate(level);
        mapOverride.AllowUnsafeSavestates = !mapOverride.AllowUnsafeSavestates;
    }

    public static void ToggleEverestSafeBlock(Level level) {
        AkronMapOverride mapOverride = GetOrCreate(level);
        mapOverride.DisableEverestSafeBlock = !mapOverride.DisableEverestSafeBlock;
    }
}
