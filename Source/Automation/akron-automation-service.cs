using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static class AkronAutomationService {
    private const string EnabledEnvironmentVariable = "AKRON_AUTOMATION_ENABLED";
    private const string TokenEnvironmentVariable = "AKRON_AUTOMATION_SESSION_TOKEN";
    private const int MinimumSessionTokenCharacters = 32;
    private const int MaxCommandFileBytes = 64 * 1024;
    private const int MaxCommandLines = 128;
    private const int MaxCommandsPerRun = 64;
    private const int MaxCommandLineCharacters = 2048;
    private const int MaxOutputLines = 512;
    private const int MaxOutputLineCharacters = 4096;
    private static readonly List<string> RunOutput = new List<string>();
    private static readonly Queue<string> PendingCommands = new Queue<string>();
    private static readonly HashSet<string> JoinedTextArgumentCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "akron_overlay_select",
        "akron_overlay_exec",
        "akron_overlay_options",
        "akron_overlay_search",
        "akron_overlay_collapse",
        "akron_prompt_select",
        "akron_proof",
        "akron_showcase_mark",
        "akron_debug_snapshot",
        "akron_editable_flag",
        "akron_tas_file"
    };
    private static readonly HashSet<string> AllowedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "akron_air_jumps",
        "akron_auto_deafen",
        "akron_auto_kill",
        "akron_autosave",
        "akron_backboost",
        "akron_broker_prompt",
        "akron_broker_warnings",
        "akron_bypass",
        "akron_camera_offset",
        "akron_community_packs",
        "akron_control_display",
        "akron_core_mode",
        "akron_cursor_zoom",
        "akron_dash_bar",
        "akron_dash_count",
        "akron_dash_redirect",
        "akron_death_particles",
        "akron_debug_snapshot",
        "akron_deload_spinners",
        "akron_editable_flag",
        "akron_evm",
        "akron_feature",
        "akron_frame_bypass",
        "akron_freeze",
        "akron_golden_start_helper",
        "akron_grab_mode",
        "akron_ground_refills",
        "akron_hazard_accuracy",
        "akron_hitbox_filter",
        "akron_hitbox_style",
        "akron_hitboxes",
        "akron_hud_cheat_indicator",
        "akron_hud_labels",
        "akron_input_history",
        "akron_input_state",
        "akron_inspector",
        "akron_internal_recorder",
        "akron_invincibility",
        "akron_ips",
        "akron_journal_snapshot",
        "akron_load",
        "akron_madeline_colors",
        "akron_map_capture",
        "akron_megahack_public",
        "akron_menu_input",
        "akron_menu_pause",
        "akron_neutral_drop",
        "akron_no_freeze_frames",
        "akron_noclip",
        "akron_overlay",
        "akron_overlay_collapse",
        "akron_overlay_exec",
        "akron_overlay_move",
        "akron_overlay_options",
        "akron_overlay_search",
        "akron_overlay_select",
        "akron_overlay_select_area",
        "akron_overlay_state",
        "akron_perf",
        "akron_play_tas",
        "akron_player_state",
        "akron_position",
        "akron_prompt_select",
        "akron_prompt_state",
        "akron_proof",
        "akron_qa_air_jump_policy",
        "akron_qa_area_complete",
        "akron_qa_area_stats",
        "akron_qa_audio_state",
        "akron_qa_backup",
        "akron_qa_click_teleport",
        "akron_qa_cursor_tools_state",
        "akron_qa_cursor_zoom_frame",
        "akron_qa_cutscene_state",
        "akron_qa_dash_redirect",
        "akron_qa_death_visual_state",
        "akron_qa_enter_debug_map",
        "akron_qa_enter_level",
        "akron_qa_enter_map",
        "akron_qa_fast_lookout",
        "akron_qa_find_map_entities",
        "akron_qa_freeze_frame",
        "akron_qa_inspector_controls",
        "akron_qa_inspector_cursor_state",
        "akron_qa_inspector_pin_world",
        "akron_qa_inspector_probe_screen",
        "akron_qa_inspector_scan_targets",
        "akron_qa_inspector_stack_state",
        "akron_qa_invincibility_hazard",
        "akron_qa_label_number",
        "akron_qa_label_row_order",
        "akron_qa_list_maps",
        "akron_qa_pause",
        "akron_qa_pause_event",
        "akron_qa_pause_state",
        "akron_qa_player_state",
        "akron_qa_probe",
        "akron_qa_reenter_level",
        "akron_qa_refill_clarity_dash_crystal",
        "akron_qa_refill_clarity_probe",
        "akron_qa_refill_clarity_state",
        "akron_qa_refill_clarity_style",
        "akron_qa_save_area_stats",
        "akron_qa_save_settings",
        "akron_qa_screenshake",
        "akron_qa_screenshake_state",
        "akron_qa_session_probe",
        "akron_qa_session_state",
        "akron_qa_sleep",
        "akron_qa_sound_sources",
        "akron_qa_startpos_action",
        "akron_qa_startpos_death_candidate",
        "akron_qa_startpos_edge_capture",
        "akron_qa_startpos_load_probe",
        "akron_qa_stress",
        "akron_qa_toast_label",
        "akron_qa_unlock_state",
        "akron_qa_warp_room",
        "akron_showcase_mark",
        "akron_resource_hud",
        "akron_room_capture",
        "akron_room_stats_export",
        "akron_save",
        "akron_save_time_deaths",
        "akron_set_inventory",
        "akron_setup",
        "akron_skip_cutscene",
        "akron_slot",
        "akron_sound_volume",
        "akron_speed_number",
        "akron_startpos",
        "akron_status",
        "akron_step_frame",
        "akron_step_repeat",
        "akron_tas_file",
        "akron_theme",
        "akron_timescale",
        "akron_toggle_everest_safe_override",
        "akron_toggle_force_broker",
        "akron_toggle_unsafe_native_override",
        "akron_visual_noise",
        "akron_visual_tuning"
    };
    private static string currentCommandText = string.Empty;
    private static bool hasActiveRun;
    private static bool isProcessing;
    private static bool automationDirectoryReady;
    private static ulong nextIdlePollFrame;

    private static string AutomationDirectory => Path.Combine(Everest.PathGame, "Saves", "AkronAutomation");
    private static string CommandPath => Path.Combine(AutomationDirectory, "command.txt");
    private static string LastCommandPath => Path.Combine(AutomationDirectory, "last-command.txt");
    private static string LastResultPath => Path.Combine(AutomationDirectory, "last-result.txt");

    // This queue is intentionally simple. Remote automation writes one command file,
    // Akron executes it on the game thread, and Akron writes a plain-text result file.
    // That keeps the live validation path deterministic even when input injection and
    // DebugRC console responses are unreliable on the host.
    public static void ProcessPendingCommands(Scene scene) {
        if (scene == null || isProcessing || !TryGetSessionToken(out string sessionToken)) {
            return;
        }

        if (!hasActiveRun) {
            if (Engine.FrameCounter < nextIdlePollFrame) {
                return;
            }

            nextIdlePollFrame = Engine.FrameCounter + 15UL;
            EnsureAutomationDirectory();
            if (!File.Exists(CommandPath)) {
                return;
            }

            PendingCommands.Clear();
            RunOutput.Clear();

            string commandFile;
            try {
                commandFile = ReadCommandFileCapped(CommandPath);
            } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException) {
                AppendOutput("rejected: " + exception.Message);
                WriteResult(status: "rejected");
                return;
            } finally {
                try {
                    File.Delete(CommandPath);
                } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) {
                    Logger.Log(LogLevel.Warn, nameof(AkronAutomationService), "Could not delete automation command file: " + exception.Message);
                }
            }
            if (!TryParseCommandFile(commandFile, sessionToken, out List<string> commands, out string parseError)) {
                AppendOutput("rejected: " + parseError);
                WriteResult(status: "rejected");
                return;
            }

            currentCommandText = string.Join(Environment.NewLine, commands);
            WriteOwnerOnlyText(LastCommandPath, currentCommandText + Environment.NewLine);

            AppendOutput("timestamp: " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            AppendOutput("scene: " + scene.GetType().Name);

            foreach (string command in commands) {
                PendingCommands.Enqueue(command);
            }

            hasActiveRun = true;
            if (PendingCommands.Count == 0) {
                AppendOutput("no commands found");
                FinalizeRun();
                return;
            }

            WriteResult(status: "pending");
        }

        isProcessing = true;
        try {
            if (PendingCommands.Count == 0) {
                FinalizeRun();
                return;
            }

            string commandLine = PendingCommands.Dequeue();
            AppendOutput("> " + commandLine);
            try {
                ExecuteCommand(commandLine);
            } catch (Exception exception) {
                AppendOutput("! " + exception.GetType().Name + ": " + exception.Message);
            }
        } finally {
            isProcessing = false;
        }

        if (PendingCommands.Count == 0) {
            FinalizeRun();
            return;
        }

        WriteResult(status: "pending");
    }

    public static void RecordOutput(string line) {
        if (!hasActiveRun || string.IsNullOrWhiteSpace(line)) {
            return;
        }

        AppendOutput(line);
    }

    private static void ExecuteCommand(string commandLine) {
        string[] tokens = Tokenize(commandLine);
        if (tokens.Length == 0) {
            return;
        }

        string command = tokens[0];
        if (!IsAllowedCommand(command)) {
            throw new InvalidDataException("Automation command is not allowlisted.");
        }
        string[] args = tokens.Skip(1).ToArray();
        if (args.Length > 1 && JoinedTextArgumentCommands.Contains(command)) {
            args = new[] { string.Join(" ", args) };
        }

        Engine.Commands.ExecuteCommand(command, args);
    }

    private static string[] Tokenize(string input) {
        List<string> tokens = new List<string>();
        StringBuilder current = new StringBuilder();
        bool insideQuotes = false;

        foreach (char character in input) {
            if (character == '"') {
                insideQuotes = !insideQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !insideQuotes) {
                FlushToken(tokens, current);
                continue;
            }

            current.Append(character);
        }

        FlushToken(tokens, current);
        return tokens.ToArray();
    }

    private static void FlushToken(List<string> tokens, StringBuilder current) {
        if (current.Length == 0) {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
    }

    private static void AppendOutput(string line) {
        string safeLine = (line ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        if (safeLine.Length > MaxOutputLineCharacters) {
            safeLine = safeLine.Substring(0, MaxOutputLineCharacters);
        }
        if (RunOutput.Count < MaxOutputLines) {
            RunOutput.Add(safeLine);
        }
        Logger.Log(LogLevel.Info, nameof(AkronAutomationService), safeLine);
    }

    private static void FinalizeRun() {
        WriteResult(status: "complete");
        PendingCommands.Clear();
        currentCommandText = string.Empty;
        hasActiveRun = false;
    }

    private static void WriteResult(string status) {
        List<string> lines = new List<string> {
            "status: " + status
        };
        lines.AddRange(RunOutput);
        WriteOwnerOnlyText(LastResultPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private static void EnsureAutomationDirectory() {
        if (automationDirectoryReady && Directory.Exists(AutomationDirectory)) {
            return;
        }

        if (OperatingSystem.IsWindows()) {
            Directory.CreateDirectory(AutomationDirectory);
        } else {
            Directory.CreateDirectory(AutomationDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(AutomationDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        automationDirectoryReady = true;
    }

    private static bool TryGetSessionToken(out string token) {
        string enabled = Environment.GetEnvironmentVariable(EnabledEnvironmentVariable);
        token = Environment.GetEnvironmentVariable(TokenEnvironmentVariable) ?? string.Empty;
        return IsEnabledForTesting(enabled, token);
    }

    internal static bool IsEnabledForTesting(string enabled, string token) {
        return string.Equals(enabled, "1", StringComparison.Ordinal) &&
               !string.IsNullOrWhiteSpace(token) && token.Length >= MinimumSessionTokenCharacters;
    }

    internal static bool TryParseCommandFileForTesting(string content, string expectedToken, out IReadOnlyList<string> commands, out string error) {
        bool parsed = TryParseCommandFile(content, expectedToken, out List<string> parsedCommands, out error);
        commands = parsedCommands;
        return parsed;
    }

    private static bool TryParseCommandFile(string content, string expectedToken, out List<string> commands, out string error) {
        commands = new List<string>();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(content) || Encoding.UTF8.GetByteCount(content) > MaxCommandFileBytes) {
            error = "Command file is empty or too large.";
            return false;
        }

        string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (lines.Length > MaxCommandLines) {
            error = "Command file has too many lines.";
            return false;
        }

        int firstLine = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#", StringComparison.Ordinal));
        if (firstLine < 0 || !lines[firstLine].Trim().StartsWith("token: ", StringComparison.Ordinal)) {
            error = "Command file is missing its session token.";
            return false;
        }

        string suppliedToken = lines[firstLine].Trim().Substring("token: ".Length);
        if (!FixedTimeTokenEquals(suppliedToken, expectedToken)) {
            error = "Command file session token is invalid.";
            return false;
        }

        for (int index = firstLine + 1; index < lines.Length; index++) {
            string commandLine = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(commandLine) || commandLine.StartsWith("#", StringComparison.Ordinal)) {
                continue;
            }
            if (commandLine.Length > MaxCommandLineCharacters || commands.Count >= MaxCommandsPerRun) {
                error = "Command file exceeds command limits.";
                return false;
            }
            string command = Tokenize(commandLine).FirstOrDefault() ?? string.Empty;
            if (!IsAllowedCommand(command)) {
                error = "Automation command is not allowlisted: " + command;
                return false;
            }
            commands.Add(commandLine);
        }

        return true;
    }

    private static bool IsAllowedCommand(string command) {
        return AllowedCommands.Contains(command);
    }

    private static bool FixedTimeTokenEquals(string supplied, string expected) {
        byte[] suppliedBytes = Encoding.UTF8.GetBytes(supplied ?? string.Empty);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected ?? string.Empty);
        return suppliedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }

    private static string ReadCommandFileCapped(string path) {
        FileInfo file = new FileInfo(path);
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length > MaxCommandFileBytes) {
            throw new InvalidDataException("Automation command file is unsafe or too large.");
        }
        using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        using MemoryStream buffer = new MemoryStream((int)Math.Min(file.Length, MaxCommandFileBytes));
        byte[] chunk = new byte[4096];
        int total = 0;
        while (true) {
            int read = stream.Read(chunk, 0, Math.Min(chunk.Length, MaxCommandFileBytes + 1 - total));
            if (read == 0) {
                return Encoding.UTF8.GetString(buffer.ToArray());
            }
            total += read;
            if (total > MaxCommandFileBytes) {
                throw new InvalidDataException("Automation command file is too large.");
            }
            buffer.Write(chunk, 0, read);
        }
    }

    private static void WriteOwnerOnlyText(string path, string content) {
        File.WriteAllText(path, content ?? string.Empty);
        if (!OperatingSystem.IsWindows()) {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
