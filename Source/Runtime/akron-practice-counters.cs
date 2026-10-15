using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static class AkronPracticeCounters {
    public static void OnLevelBegin(Level level) {
        if (level?.Session == null || AkronModule.Session == null) {
            return;
        }

        AkronModule.Session.AkronDashCountAtLevelStart = GetDashCount(level.Session.Dashes, AkronModule.Session.AkronRetainedDashCount);
        AkronModule.Session.AkronJumpCountAtLevelStart = AkronModule.Session.AkronJumpCount;
    }

    private sealed class ReloadCounterContext {
        public Level Level;
        public Session NativeSession;
        public AkronModuleSession PracticeSession;
        public int NativeBefore;
        public int RetainedBefore;
        public bool Preserve;
        public ReloadCounterContext Parent;
    }
    private static ReloadCounterContext activeReload;

    internal static void OnLevelReload(On.Celeste.Level.orig_Reload orig, Level level) {
        var context = new ReloadCounterContext {
            Level = level, NativeSession = level?.Session, PracticeSession = AkronModule.Session,
            NativeBefore = level?.Session?.Dashes ?? 0,
            RetainedBefore = AkronModule.Session?.AkronRetainedDashCount ?? 0,
            Preserve = AkronModule.Settings.DashCountStatsDoNotResetOnDeath,
            Parent = activeReload
        };
        activeReload = context;
        try {
            orig(level);
            ApplyReloadRetention(context, level);
        } finally {
            activeReload = context.Parent;
        }
    }

    private static void ApplyReloadRetention(ReloadCounterContext context, Level level) {
        if (context == null || !ReferenceEquals(context.Level, level)
            || context.NativeSession == null || context.PracticeSession == null
            || !context.Preserve || !AkronModule.Settings.DashCountStatsDoNotResetOnDeath
            || !ReferenceEquals(context.NativeSession, level.Session)
            || !ReferenceEquals(context.PracticeSession, AkronModule.Session)) {
            return;
        }
        context.PracticeSession.AkronRetainedDashCount = RetainReloadedDashes(
            context.RetainedBefore, context.PracticeSession.AkronRetainedDashCount,
            context.NativeBefore, context.NativeSession.Dashes);
    }

    internal static int RetainReloadedDashes(int retainedBefore, int retainedNow, int nativeBefore, int nativeAfter) {
        // A nested reload may already have accounted for this rollback. Do not add it
        // a second time when the outer hook returns.
        long discarded = System.Math.Max(0L, (long) nativeBefore - nativeAfter);
        return (int) System.Math.Min(int.MaxValue,
            System.Math.Max(retainedNow, System.Math.Max(0L, retainedBefore) + discarded));
    }

    internal static int GetDashCount(int nativeCount, int retained) {
        return (int) System.Math.Clamp((long) nativeCount + retained, 0L, int.MaxValue);
    }

    public static void OnPlayerJump(Player player) {
        if (player == null || AkronModule.Session == null) {
            return;
        }

        AkronModule.Session.AkronJumpCount++;
    }

    public static void OnRespawn(Level level) {
        if (AkronModule.Session == null) {
            return;
        }

        // Player.Added can run inside Reload. Commit the native rollback before
        // the following respawn autosave observes the module session. The final
        // Reload reconciliation is idempotent and also covers reloads without Added.
        for (var context = activeReload; context != null; context = context.Parent) {
            ApplyReloadRetention(context, level);
        }
        if (!AkronModule.Settings.DashCountStatsDoNotResetOnDeath && level?.Session != null) {
            AkronModule.Session.AkronRetainedDashCount = 0;
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

        return Format("Dashes", AkronModule.Settings.DashCountStatsMode, GetDashCount(level.Session.Dashes, AkronModule.Session?.AkronRetainedDashCount ?? 0), AkronModule.Session?.AkronDashCountAtLevelStart ?? 0, SaveData.Instance?.TotalDashes ?? 0);
    }

    public static string FormatJumpCount() {
        // File mode reads Celeste's own per-file jump total, the same way Dash Stats reads
        // TotalDashes, so it persists and follows the open save file.
        return Format("Jumps", AkronModule.Settings.JumpCountMode, AkronModule.Session?.AkronJumpCount ?? 0, AkronModule.Session?.AkronJumpCountAtLevelStart ?? 0, SaveData.Instance?.TotalJumps ?? 0);
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
