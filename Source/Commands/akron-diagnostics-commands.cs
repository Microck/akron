using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_diagnostics", "diagnostics: open|consent|send|confirm|cancel|status|copy. Confirm accepts a visible blank-report prompt.")]
    public static void Diagnostics(string action = "status") {
        string normalized = NormalizeToken(action);
        if (normalized == "status" || normalized.Length == 0) {
            Log("diagnostics: " + AkronDiagnosticsMenu.DescribeState());
            return;
        }
        if (!AkronDiagnosticsMenu.Execute(normalized)) {
            Log("diagnostics: action unavailable. Open the form, then send. A blank report also needs confirm after its prompt appears. Use status to inspect the result.");
            return;
        }
        Log("diagnostics: " + AkronDiagnosticsMenu.DescribeState());
    }
}
