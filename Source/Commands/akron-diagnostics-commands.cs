using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_diagnostics", "diagnostics: open|consent|send|cancel|status|copy. Send only works while the consent page is open.")]
    public static void Diagnostics(string action = "status") {
        string normalized = NormalizeToken(action);
        if (normalized == "status" || normalized.Length == 0) {
            Log("diagnostics: " + AkronDiagnosticsMenu.DescribeState());
            return;
        }
        if (!AkronDiagnosticsMenu.Execute(normalized)) {
            Log("diagnostics: action unavailable. Use open, read consent, then send. Use status to inspect the result.");
            return;
        }
        Log("diagnostics: " + AkronDiagnosticsMenu.DescribeState());
    }
}
