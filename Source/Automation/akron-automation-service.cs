using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static class AkronAutomationService {
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
        if (scene == null || isProcessing) {
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

            currentCommandText = File.ReadAllText(CommandPath);
            File.WriteAllText(LastCommandPath, currentCommandText);
            File.Delete(CommandPath);

            PendingCommands.Clear();
            RunOutput.Clear();

            List<string> commands = currentCommandText
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
                .ToList();

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
        RunOutput.Add(line);
        Logger.Log(LogLevel.Info, nameof(AkronAutomationService), line);
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
        File.WriteAllText(LastResultPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private static void EnsureAutomationDirectory() {
        if (automationDirectoryReady && Directory.Exists(AutomationDirectory)) {
            return;
        }

        Directory.CreateDirectory(AutomationDirectory);
        automationDirectoryReady = true;
    }
}
