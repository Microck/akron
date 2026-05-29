using System;
using System.Globalization;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_bypass", "control Bypass options: status|low-volume on/off|volume <music> <sfx>|instant-complete|unlock-a-sides|unlock-b-sides|unlock-c-sides|unlock-all-levels|unlock-golden-berries|unlock-paths|obtain-room-berries|obtain-chapter-berries|berry-options")]
    public static void Bypass(string action = "status", string value = "", string extra = "") {
        string normalized = NormalizeToken(action);
        switch (normalized) {
            case "":
            case "status":
                LogBypassStatus();
                return;
            case "lowvolume":
            case "allowlowvolume":
                if (NormalizeToken(value) == "status" || string.IsNullOrWhiteSpace(value)) {
                    Log("allow-low-volume: " + AkronModule.Settings.AllowLowVolume.ToString().ToLowerInvariant());
                    return;
                }

                if (!TryParseBoolean(value, out bool lowVolume)) {
                    Log("usage: akron_bypass low-volume on|off");
                    return;
                }

                AkronActions.SetAllowLowVolume(lowVolume);
                LogBypassStatus();
                return;
            case "volume":
            case "lowvolumelevels":
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float music) ||
                    !float.TryParse(extra, NumberStyles.Float, CultureInfo.InvariantCulture, out float sfx)) {
                    Log("usage: akron_bypass volume <music 0.0-10.0> <sfx 0.0-10.0>");
                    return;
                }

                AkronActions.SetLowVolumeMusic(music);
                AkronActions.SetLowVolumeSfx(sfx);
                LogBypassStatus();
                return;
            case "instantcomplete":
                Level level = RequireLevel();
                if (level != null) {
                    AkronActions.InstantComplete(level);
                    Log("instant-complete: requested");
                }
                return;
            case "unlockpaths":
            case "unlockgates":
                Log("unlock-paths: " + (AkronActions.UnlockPaths() ? "applied" : "blocked"));
                Log("unlock-system: " + AkronActions.DescribeUnlockState());
                return;
            case "unlockasides":
            case "unlocka":
                Log("unlock-a-sides: " + (AkronActions.UnlockASides() ? "applied" : "blocked"));
                Log("unlock-system: " + AkronActions.DescribeUnlockState());
                return;
            case "unlockbsides":
            case "unlockb":
                Log("unlock-b-sides: " + (AkronActions.UnlockBSides() ? "applied" : "blocked"));
                Log("unlock-system: " + AkronActions.DescribeUnlockState());
                return;
            case "unlockcsides":
            case "unlockc":
                Log("unlock-c-sides: " + (AkronActions.UnlockCSides() ? "applied" : "blocked"));
                Log("unlock-system: " + AkronActions.DescribeUnlockState());
                return;
            case "unlockgoldenberries":
            case "unlockgoldens":
                Log("unlock-golden-berries: " + (AkronActions.UnlockGoldenBerries() ? "applied" : "blocked"));
                Log("unlock-system: " + AkronActions.DescribeUnlockState());
                return;
            case "obtainroomberries":
            case "obtainroom":
                Level roomLevel = RequireLevel();
                if (roomLevel != null) {
                    Log("obtain-room-berries: " + AkronActions.ObtainRoomBerries(roomLevel).ToString(CultureInfo.InvariantCulture));
                }
                return;
            case "obtainchapterberries":
            case "obtainchapter":
                Level chapterLevel = RequireLevel();
                if (chapterLevel != null) {
                    Log("obtain-chapter-berries: " + AkronActions.ObtainChapterBerries(chapterLevel).ToString(CultureInfo.InvariantCulture));
                }
                return;
            case "berryoptions":
                ApplyBerryOptionCommand(value, extra);
                LogBypassStatus();
                return;
            case "unlockmainlevels":
            case "unlockalllevels":
            case "unlockall":
                Log("unlock-all-levels: " + (AkronActions.UnlockAllLevels() ? "applied" : "blocked"));
                Log("unlock-system: " + AkronActions.DescribeUnlockState());
                return;
            case "swiftclick":
                Log("swift-click: planned-xl-not-implemented");
                return;
            default:
                Log("unknown bypass action: " + action);
                Log("usage: akron_bypass status|low-volume on/off|volume <music> <sfx>|instant-complete|unlock-a-sides|unlock-b-sides|unlock-c-sides|unlock-all-levels|unlock-golden-berries|unlock-paths|obtain-room-berries|obtain-chapter-berries|berry-options <regular|golden|moon> on/off");
                return;
        }
    }

    private static void LogBypassStatus() {
        Log("allow-low-volume: " + AkronModule.Settings.AllowLowVolume.ToString().ToLowerInvariant());
        Log("low-volume-music: " + AkronModule.Settings.LowVolumeMusic.ToString("0.0", CultureInfo.InvariantCulture));
        Log("low-volume-sfx: " + AkronModule.Settings.LowVolumeSfx.ToString("0.0", CultureInfo.InvariantCulture));
        if (Settings.Instance != null) {
            Log("current-music-volume: " + Settings.Instance.MusicVolume.ToString(CultureInfo.InvariantCulture));
            Log("current-sfx-volume: " + Settings.Instance.SFXVolume.ToString(CultureInfo.InvariantCulture));
        }
        Log("runtime-music-volume: " + (Audio.MusicVolume * 10f).ToString("0.0", CultureInfo.InvariantCulture));
        Log("runtime-sfx-volume: " + (Audio.SfxVolume * 10f).ToString("0.0", CultureInfo.InvariantCulture));

        Log("unlock-system: " + AkronActions.DescribeUnlockState());
        Log("berry-obtain-options: " + AkronActions.DescribeBerryObtainOptions());
        Log("berry-obtain-regular: " + AkronModule.Settings.BerryObtainIncludeRegular.ToString().ToLowerInvariant());
        Log("berry-obtain-golden: " + AkronModule.Settings.BerryObtainIncludeGolden.ToString().ToLowerInvariant());
        Log("berry-obtain-moon: " + AkronModule.Settings.BerryObtainIncludeMoon.ToString().ToLowerInvariant());
        Log("swift-click: planned-xl-not-implemented");
    }

    private static void ApplyBerryOptionCommand(string target, string value) {
        string normalizedTarget = NormalizeToken(target);
        if (string.IsNullOrWhiteSpace(normalizedTarget) || string.Equals(normalizedTarget, "status", StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        if (!TryParseBoolean(value, out bool enabled)) {
            Log("usage: akron_bypass berry-options <regular|golden|moon> on|off");
            return;
        }

        switch (normalizedTarget) {
            case "regular":
            case "red":
            case "strawberries":
                AkronModule.Settings.BerryObtainIncludeRegular = enabled;
                return;
            case "golden":
            case "goldens":
            case "goldenberries":
                AkronModule.Settings.BerryObtainIncludeGolden = enabled;
                return;
            case "moon":
            case "moonberry":
                AkronModule.Settings.BerryObtainIncludeMoon = enabled;
                return;
            default:
                Log("unknown berry option: " + target);
                return;
        }
    }
}
