using System;
using System.Globalization;
using Celeste;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_freeze", "control Akron freeze: toggle|on|off|status")]
    public static void Freeze(string action = "toggle") {
        switch ((action ?? string.Empty).Trim().ToLowerInvariant()) {
            case "":
            case "toggle":
                AkronActions.ToggleFreeze();
                break;
            case "on":
                if (!AkronModule.Session.FreezeGameplay) {
                    AkronActions.ToggleFreeze();
                }
                break;
            case "off":
                if (AkronModule.Session.FreezeGameplay) {
                    AkronActions.ToggleFreeze();
                }
                break;
            case "status":
                break;
            default:
                Log("unknown freeze action: " + action);
                return;
        }

        Log("freeze: " + AkronModule.Session.FreezeGameplay.ToString().ToLowerInvariant());
    }

    [Command("akron_timescale", "set Akron timescale: reset or value between 0.1 and 2.0")]
    public static void Timescale(string value = "") {
        if (string.IsNullOrWhiteSpace(value)) {
            Log("timescale: " + AkronModule.Session.TimescaleMultiplier.ToString("0.0x", CultureInfo.InvariantCulture));
            return;
        }

        if (string.Equals(value, "reset", StringComparison.OrdinalIgnoreCase)) {
            AkronActions.AdjustTimescale(1f - AkronModule.Session.TimescaleMultiplier);
            Log("timescale: " + AkronModule.Session.TimescaleMultiplier.ToString("0.0x", CultureInfo.InvariantCulture));
            return;
        }

        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedValue)) {
            Log("invalid timescale: " + value);
            return;
        }

        AkronActions.AdjustTimescale(parsedValue - AkronModule.Session.TimescaleMultiplier);
        Log("timescale: " + AkronModule.Session.TimescaleMultiplier.ToString("0.0x", CultureInfo.InvariantCulture));
    }

    [Command("akron_golden_start_helper", "run the allowed golden start helper when the current room is the first room")]
    public static void GoldenStartHelper() {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        AkronActions.GiveGoldenFromStart(level);
        Log("golden-start-helper: " + AkronActions.DescribeGoldenStartHelper(level));
    }

    [Command("akron_journal_snapshot", "write and compare an Akron journal proof snapshot")]
    public static void JournalSnapshot() {
        Level level = Engine.Scene as Level;
        AkronActions.WriteJournalSnapshotCompare(level);
        string summary = AkronModule.Session?.LastJournalCompareSummary;
        Log("journal-snapshot: " + (string.IsNullOrWhiteSpace(summary) ? "unavailable" : summary));
    }

    [Command("akron_neutral_drop", "trigger Akron Neutral Drop shortcut")]
    public static void NeutralDrop() {
        AkronActions.NeutralDrop();
        Log("neutral-drop: requested");
    }

    [Command("akron_backboost", "trigger Akron Backboost shortcut")]
    public static void Backboost() {
        AkronActions.Backboost();
        Log("backboost: requested");
    }

    [Command("akron_prevent_down_dash_redirects", "control Prevent Down Dash Redirects: disabled|normal|diagonal|toggle|status")]
    public static void PreventDownDashRedirects(string action = "status") {
        SetPreventDownDashRedirects(action);
    }

    [Command("akron_step_repeat", "control frozen hold-repeat stepping: on|off|status|delay <frames>|interval <frames>|key <key>|reset-key")]
    public static void StepRepeat(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                AkronModule.Settings.StepHoldRepeat = true;
                break;
            case "off":
                AkronModule.Settings.StepHoldRepeat = false;
                break;
            case "toggle":
                AkronModule.Settings.StepHoldRepeat = !AkronModule.Settings.StepHoldRepeat;
                break;
            case "delay":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int delay)) {
                    Log("invalid step repeat delay: " + value);
                    return;
                }
                AkronModule.Settings.StepHoldDelayFrames = Calc.Clamp(delay, 1, 120);
                break;
            case "interval":
            case "speed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int interval)) {
                    Log("invalid step repeat interval: " + value);
                    return;
                }
                AkronModule.Settings.StepHoldIntervalFrames = Calc.Clamp(interval, 1, 60);
                break;
            case "key":
                if (!TryParseStepFrameKey(value, out Keys key)) {
                    Log("invalid step frame key: " + value);
                    return;
                }
                AkronModule.Settings.StepFrame = new ButtonBinding(0, key);
                break;
            case "resetkey":
                AkronModule.Settings.StepFrame = new ButtonBinding(0, Keys.F2);
                break;
            default:
                Log("unknown step-repeat action: " + action);
                return;
        }

        Log("step-hold-repeat: " + AkronModule.Settings.StepHoldRepeat.ToString().ToLowerInvariant());
        Log("step-repeat-delay: " + AkronModule.Settings.StepHoldDelayFrames.ToString(CultureInfo.InvariantCulture));
        Log("step-repeat-interval: " + AkronModule.Settings.StepHoldIntervalFrames.ToString(CultureInfo.InvariantCulture));
        Log("step-frame-binding: " + AkronModuleSettings.DescribeBinding(AkronModule.Settings.StepFrame));
    }

    [Command("akron_step_frame", "request one gameplay frame while Akron freeze is active")]
    public static void StepFrame(string _ = "") {
        if (!AkronModule.Session.FreezeGameplay) {
            Log("step-frame: ignored because freeze is off");
            return;
        }

        AkronModule.Session.StepFrameRequested = true;
        Log("step-frame: requested");
    }

    private static bool TryParseStepFrameKey(string value, out Keys key) {
        key = Keys.None;
        return !string.IsNullOrWhiteSpace(value) &&
               Enum.TryParse(value.Trim(), ignoreCase: true, out key) &&
               key != Keys.None;
    }

    [Command("akron_skip_cutscene", "request Akron cutscene/dialogue skip")]
    public static void SkipCutscene(string _ = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        bool wasInCutscene = level.InCutscene;
        bool wasSkippingCutscene = level.SkippingCutscene;
        AkronActions.SkipCutscene(level);
        Log("cutscene-before: in-cutscene=" + wasInCutscene.ToString().ToLowerInvariant() + ";skipping=" + wasSkippingCutscene.ToString().ToLowerInvariant());
        Log("cutscene-after: in-cutscene=" + level.InCutscene.ToString().ToLowerInvariant() + ";skipping=" + level.SkippingCutscene.ToString().ToLowerInvariant());
    }

    // Command-only coordinate probe for QA and controlled map debugging. Normal
    // player docs should prefer StartPos or room-warp UI flows.
    [Command("akron_position", "set player position inside the current room: x y")]
    public static void Position(string x = "", string y = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        Player player = level.Tracker.GetEntity<Player>();
        if (player == null) {
            Log("player not found");
            return;
        }

        if (!float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedX) ||
            !float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedY)) {
            Log("usage: akron_position <x> <y>");
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.RoomWarp)) {
            return;
        }

        Microsoft.Xna.Framework.Vector2 target = new Microsoft.Xna.Framework.Vector2(parsedX, parsedY);
        player.NaiveMove(target - player.Position);
        player.Speed = Microsoft.Xna.Framework.Vector2.Zero;
        Log("position: " + player.Position.X.ToString("0.##", CultureInfo.InvariantCulture) + ", " + player.Position.Y.ToString("0.##", CultureInfo.InvariantCulture));
    }

    [Command("akron_proof", "write an Akron proof sidecar JSON file")]
    public static void Proof(string eventName = "manual-proof-export") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        AkronActions.WriteProofSidecar(level, string.IsNullOrWhiteSpace(eventName) ? "manual-proof-export" : eventName);
        Log("proof exported");
    }

    // Command-only diagnostic artifact for bug reports and live verification.
    // Normal player-facing capture docs should prefer screenshot/recording UI.
    [Command("akron_debug_snapshot", "write an Akron debug snapshot JSON file: [tag]")]
    public static void DebugSnapshot(string tag = "manual") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        string path = AkronActions.WriteDebugSnapshot(level, string.IsNullOrWhiteSpace(tag) ? "manual" : tag);
        Log("debug-snapshot: " + path);
    }

    [Command("akron_room_stats_export", "export Akron room stats for the current map")]
    public static void RoomStatsExport() {
        Level level = Engine.Scene as Level;
        string path = level == null ? string.Empty : AkronPracticeStats.ExportRoomStats(level);
        Log("room-stats-export: " + (string.IsNullOrWhiteSpace(path) ? "unavailable" : path));
    }
}
