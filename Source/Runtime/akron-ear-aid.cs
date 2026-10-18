using System;
using System.Collections.Generic;
using FMOD.Studio;

namespace Celeste.Mod.Akron;

public static class AkronEarAid {
    private static readonly List<(EventInstance Instance, string Key, float OriginalVolume, bool Applied)> VolumeOverrides = new();

    private static volatile bool observeForQa;
    private static readonly object QaObservationLock = new();
    private static readonly Queue<(EventInstance Instance, string BeforeOverride, string AfterOverride)> QaObservedEvents = new();

    internal static int QaObservedEventCount {
        get { lock (QaObservationLock) { return QaObservedEvents.Count; } }
    }

    internal static void SetQaObservation(bool enabled) {
        lock (QaObservationLock) {
            observeForQa = enabled;
            if (enabled) {
                QaObservedEvents.Clear();
            }
        }
    }

    internal static string CaptureBeforeOverrideForQa(EventInstance instance) {
        return observeForQa && instance != null ? DescribeQaEvent(instance) : null;
    }

    internal static void ObserveCreatedEventForQa(EventInstance instance, string beforeOverride) {
        if (!observeForQa || instance == null || beforeOverride == null) {
            return;
        }
        string initialState = DescribeQaEvent(instance);
        lock (QaObservationLock) {
            if (!observeForQa) {
                return;
            }
            // Bounded managed references; no native ownership or playback mutation.
            while (QaObservedEvents.Count >= 128) {
                QaObservedEvents.Dequeue();
            }
            QaObservedEvents.Enqueue((instance, beforeOverride, initialState));
        }
    }

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
        new SoundDefinition("core-block", "Core Block", "coreblock", "bounceblock", "iceblock"),
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
        new SoundDefinition("move-block", "Move Block", "moveblock", "move_block", "arrowblock"),
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

        SoundDefinition sound = FindBestMatch(eventName);
        if (sound == null || !OverrideEnabled(sound.Key)) {
            return;
        }

        if (instance.getVolume(out float originalVolume, out _) != FMOD.RESULT.OK) {
            return;
        }

        bool applied = AkronModule.TryUseRuntime(AkronFeatureKind.SoundVolumeOverride);
        VolumeOverrides.Add((instance, sound.Key, originalVolume, applied));
        if (applied) {
            instance.setVolume(VolumeFor(sound.Key) / 100f);
        }
    }

    internal static void ReconcileVolumeOverrides(bool restore = false) {
        bool allowed = !restore && AkronPolicy.CanUse(AkronFeatureKind.SoundVolumeOverride).Allowed;
        for (int index = VolumeOverrides.Count - 1; index >= 0; index--) {
            var entry = VolumeOverrides[index];
            if (!entry.Instance.isValid()) {
                VolumeOverrides.RemoveAt(index);
                continue;
            }

            bool apply = allowed && OverrideEnabled(entry.Key);
            if (apply != entry.Applied) {
                entry.Instance.setVolume(apply ? VolumeFor(entry.Key) / 100f : entry.OriginalVolume);
                VolumeOverrides[index] = (entry.Instance, entry.Key, entry.OriginalVolume, apply);
            }
        }
    }

    internal static IEnumerable<string> DescribeLiveOverridesForQa() {
        (EventInstance Instance, string BeforeOverride, string AfterOverride)[] observed;
        lock (QaObservationLock) {
            observed = QaObservedEvents.ToArray();
        }
        if (observed.Length > 0) {
            foreach (var entry in observed) {
                yield return "before-override={" + entry.BeforeOverride + "}";
                yield return "after-override={" + entry.AfterOverride + "}";
                yield return "current={" + DescribeQaEvent(entry.Instance) + "}";
            }
        } else {
            foreach (var entry in VolumeOverrides.ToArray()) {
                yield return DescribeQaEvent(entry.Instance);
            }
        }
    }

    private static string DescribeQaEvent(EventInstance instance) {
        try {
            if (instance == null || !instance.isValid()) {
                return "released-or-invalid";
            }
            string name = "unknown";
            string nameError = "";
            if (instance.getDescription(out EventDescription description) == FMOD.RESULT.OK) {
                try {
                    name = Audio.GetEventName(description) ?? name;
                } catch (Exception error) {
                    nameError = error.GetType().Name;
                }
            }
            SoundDefinition definition = FindBestMatch(name);
            FMOD.RESULT result = instance.getVolume(out float volume, out float finalVolume);
            instance.getPlaybackState(out PLAYBACK_STATE playback);
            return FormattableString.Invariant($"key={definition?.Key ?? "unmatched"};event={name};name-error={nameError};volume={volume};final={finalVolume};result={result};playback={playback}");
        } catch (Exception error) {
            return "error=" + error.GetType().Name;
        }
    }

    public static float VolumeForEventNameForTesting(string eventName) {
        SoundDefinition sound = FindBestMatch(eventName ?? string.Empty);
        if (sound != null) {
            return OverrideEnabled(sound.Key) ? VolumeFor(sound.Key) / 100f : 1f;
        }

        return 1f;
    }

    internal static SoundDefinition FindBestMatch(string eventName) {
        if (string.IsNullOrEmpty(eventName)) {
            return null;
        }

        // Ear Aid controls effects and ambience, never the soundtrack. Chapter and
        // directory names are not sound identities ("bridge" even contains "ridge").
        if (eventName.StartsWith("event:/music/", StringComparison.OrdinalIgnoreCase) ||
            eventName.StartsWith("event:/new_content/music/", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        string exactKey = eventName.ToLowerInvariant() switch {
            "event:/char/madeline/revive" => "respawn",
            "event:/new_content/char/madeline/death_golden" => "golden-death",
            "event:/env/amb/04_main" => "ridge-wind",
            "event:/new_content/env/10_voidspiral" => "farewell-wind",
            _ => null
        };
        if (exactKey != null) {
            return Array.Find(Sounds, sound => sound.Key == exactKey);
        }

        if (eventName.StartsWith("event:/char/dialogue/", StringComparison.OrdinalIgnoreCase)) {
            return Array.Find(Sounds, sound => sound.Key == "dialogue");
        }
        if (eventName.StartsWith("event:/char/oshiro/boss_", StringComparison.OrdinalIgnoreCase)) {
            return Array.Find(Sounds, sound => sound.Key == "oshiro-boss");
        }

        string name = eventName.Substring(eventName.LastIndexOf('/') + 1);
        // An entity death is still that entity's sound, not the player's death.
        if (name.StartsWith("seeker_", StringComparison.OrdinalIgnoreCase)) {
            return Array.Find(Sounds, sound => sound.Key == "seeker");
        }

        SoundDefinition best = null;
        int bestScore = 0;
        foreach (SoundDefinition sound in Sounds) {
            int score = MatchScore(name, sound);
            if (score > bestScore) {
                best = sound;
                bestScore = score;
            }
        }

        return best;
    }

    private static int MatchScore(string eventName, SoundDefinition sound) {
        // Wind overrides need both the region and wind in a mod event's own name.
        // A chapter folder or an unrelated event containing "wind" is not enough.
        if (sound.Key == "farewell-wind" || sound.Key == "ridge-wind") {
            string region = sound.Key == "ridge-wind" ? "ridge" : "farewell";
            return HasToken(eventName, region) && HasToken(eventName, "wind") ? 2 : 0;
        }

        int score = 0;
        foreach (string fragment in sound.EventFragments) {
            if (!string.IsNullOrWhiteSpace(fragment) && eventName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) {
                score++;
            }
        }

        return score;
    }

    private static bool HasToken(string name, string token) {
        int start = 0;
        while (start < name.Length) {
            int index = name.IndexOf(token, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) {
                return false;
            }
            int end = index + token.Length;
            if ((index == 0 || !char.IsLetterOrDigit(name[index - 1])) &&
                (end == name.Length || !char.IsLetterOrDigit(name[end]))) {
                return true;
            }
            start = index + 1;
        }
        return false;
    }
}
