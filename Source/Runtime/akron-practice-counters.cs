using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static class AkronPracticeCounters {
    private static int fileJumpCount;

    public static int FileJumpCount => fileJumpCount;

    public static void OnLevelBegin(Level level) {
        if (level?.Session == null || AkronModule.Session == null) {
            return;
        }

        AkronModule.Session.AkronDashCountAtLevelStart = level.Session.Dashes;
        AkronModule.Session.AkronJumpCountAtLevelStart = AkronModule.Session.AkronJumpCount;
    }

    public static void OnPlayerJump(Player player) {
        if (player == null || AkronModule.Session == null) {
            return;
        }

        AkronModule.Session.AkronJumpCount++;
        fileJumpCount++;
    }

    public static void OnRespawn(Level level) {
        if (AkronModule.Session == null) {
            return;
        }

        if (!AkronModule.Settings.DashCountStatsDoNotResetOnDeath && level?.Session != null) {
            AkronModule.Session.AkronDashCountAtLevelStart = level.Session.Dashes;
        }

        if (!AkronModule.Settings.JumpCountDoNotResetOnDeath) {
            AkronModule.Session.AkronJumpCount = AkronModule.Session.AkronJumpCountAtLevelStart;
        }
    }

    public static string FormatDashCount(Level level) {
        if (level?.Session == null) {
            return "Dashes: 0";
        }

        return Format("Dashes", AkronModule.Settings.DashCountStatsMode, level.Session.Dashes, AkronModule.Session?.AkronDashCountAtLevelStart ?? 0, SaveData.Instance?.TotalDashes ?? 0);
    }

    public static string FormatJumpCount() {
        return Format("Jumps", AkronModule.Settings.JumpCountMode, AkronModule.Session?.AkronJumpCount ?? 0, AkronModule.Session?.AkronJumpCountAtLevelStart ?? 0, fileJumpCount);
    }

    private static string Format(string label, AkronCounterDisplayMode mode, int session, int levelStart, int file) {
        int chapter = System.Math.Max(0, session - levelStart);
        return mode switch {
            AkronCounterDisplayMode.Chapter => label + ": " + chapter.ToString(System.Globalization.CultureInfo.InvariantCulture),
            AkronCounterDisplayMode.File => label + ": " + file.ToString(System.Globalization.CultureInfo.InvariantCulture),
            AkronCounterDisplayMode.Both => label + ": " + chapter.ToString(System.Globalization.CultureInfo.InvariantCulture) + " / " + file.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => label + ": " + session.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}
