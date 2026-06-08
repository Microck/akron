using System;
using System.Collections.Generic;
using FMOD.Studio;

namespace Celeste.Mod.Akron;

public static class AkronEarAid {
    public sealed class SoundDefinition {
        public SoundDefinition(string key, string label, params string[] eventFragments) {
            Key = key;
            Label = label;
            EventFragments = eventFragments ?? Array.Empty<string>();
        }

        public string Key { get; }
        public string Label { get; }
        public string[] EventFragments { get; }
    }

    public static readonly SoundDefinition[] Sounds = {
        new SoundDefinition("bird-squawk", "Bird Squawk", "bird", "squawk"),
        new SoundDefinition("broken-window", "Broken Window", "broken_window", "window"),
        new SoundDefinition("conveyor", "Conveyor", "conveyor"),
        new SoundDefinition("core-block", "Core Block", "core", "coreblock"),
        new SoundDefinition("death", "Death", "death"),
        new SoundDefinition("respawn", "Respawn", "respawn"),
        new SoundDefinition("golden-death", "Golden Death", "golden", "goldberry"),
        new SoundDefinition("dialogue", "Dialogue", "dialogue", "textbox"),
        new SoundDefinition("dream-block", "Dream Block", "dreamblock", "dream_block"),
        new SoundDefinition("drum-swap-block", "Drum Swap Block", "swapblock", "swap_block"),
        new SoundDefinition("fireball", "Fireball", "fireball"),
        new SoundDefinition("heart-collect", "Heart Collect", "heart", "crystalheart"),
        new SoundDefinition("item-crystal-death", "Item Crystal Death", "crystal", "return"),
        new SoundDefinition("kevin-block", "Kevin Block", "crushblock", "kevin"),
        new SoundDefinition("lava-barrier", "Lava Barrier", "lava"),
        new SoundDefinition("lightning-ambience", "Lightning Ambience", "lightning", "ambience"),
        new SoundDefinition("lightning-strike", "Lightning Strike", "lightning_strike", "strike"),
        new SoundDefinition("move-block", "Move Block", "moveblock", "move_block"),
        new SoundDefinition("oshiro-boss", "Oshiro Boss", "oshiro"),
        new SoundDefinition("pico8-flag", "Pico-8 Flag", "pico8", "flag"),
        new SoundDefinition("seeker", "Seeker", "seeker"),
        new SoundDefinition("spring", "Spring", "spring"),
        new SoundDefinition("touch-switch-complete", "Touch Switch Complete", "touchswitch", "touch_switch"),
        new SoundDefinition("farewell-wind", "Farewell Wind", "farewell", "wind"),
        new SoundDefinition("ridge-wind", "Ridge Wind", "ridge", "wind"),
        new SoundDefinition("zip-mover", "Zip Mover", "zipmover", "zip_mover")
    };

    public static Dictionary<string, int> CreateDefaultVolumes() {
        Dictionary<string, int> volumes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (SoundDefinition sound in Sounds) {
            volumes[sound.Key] = 100;
        }

        return volumes;
    }

    public static Dictionary<string, bool> CreateDefaultOverrideToggles() {
        Dictionary<string, bool> enabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (SoundDefinition sound in Sounds) {
            enabled[sound.Key] = false;
        }

        return enabled;
    }

    public static bool OverrideEnabled(string key) {
        Dictionary<string, bool> enabled = AkronModule.Settings?.SoundVolumeOverrides;
        return enabled != null && enabled.TryGetValue(key, out bool value) && value;
    }

    public static void SetOverrideEnabled(string key, bool value) {
        AkronModule.Settings.SoundVolumeOverrides ??= CreateDefaultOverrideToggles();
        AkronModule.Settings.SoundVolumeOverrides[key] = value;
    }

    public static int VolumeFor(string key) {
        Dictionary<string, int> volumes = AkronModule.Settings?.SoundVolumes;
        if (volumes == null || !volumes.TryGetValue(key, out int volume)) {
            return 100;
        }

        return AkronModuleSettings.ClampSoundVolumePercent(volume);
    }

    public static void SetVolume(string key, int volume) {
        AkronModule.Settings.SoundVolumes ??= CreateDefaultVolumes();
        AkronModule.Settings.SoundVolumes[key] = AkronModuleSettings.ClampSoundVolumePercent(volume);
    }

    public static void ApplyVolume(EventDescription description, EventInstance instance) {
        if (description.Equals(default(EventDescription)) || instance.Equals(default(EventInstance))) {
            return;
        }

        string eventName;
        try {
            eventName = Audio.GetEventName(description) ?? string.Empty;
        } catch {
            return;
        }

        foreach (SoundDefinition sound in Sounds) {
            if (!Matches(eventName, sound)) {
                continue;
            }

            if (OverrideEnabled(sound.Key)) {
                int volume = VolumeFor(sound.Key);
                instance.setVolume(volume / 100f);
            }
            return;
        }
    }

    private static bool Matches(string eventName, SoundDefinition sound) {
        foreach (string fragment in sound.EventFragments) {
            if (!string.IsNullOrWhiteSpace(fragment) && eventName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }
}
