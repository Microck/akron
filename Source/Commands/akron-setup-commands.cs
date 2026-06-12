using System;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_setup", "show/import/export Akron .akr setup: export [scope] [name]|import [scope] <file-or-name>|import-latest [scope]; scopes: whole|startpos|keybinds|auto-kill|auto-deafen|recorder|audio|hud")]
    public static void Setup(string value = "", string part2 = "", string part3 = "", string part4 = "", string part5 = "", string part6 = "") {
        string action = NormalizeToken(value);
        string joined = JoinCommandText(part2, part3, part4, part5, part6);
        if (string.IsNullOrWhiteSpace(value)) {
            Log("setup-section: " + AkronSetupPacks.FormatSection(AkronModule.Settings.SetupPackSection));
            Log("setup-directory: " + AkronSetupPacks.GetSetupDirectory());
            return;
        }

        if (action == "export") {
            AkronSetupSection section = AkronSetupSection.Whole;
            string exportName = joined;
            if (AkronSetupPacks.TryParseSection(part2, out AkronSetupSection parsedSection) && !string.IsNullOrWhiteSpace(part2)) {
                section = parsedSection;
                exportName = JoinCommandText(part3, part4, part5, part6);
            }

            string path = AkronSetupPacks.ExportCurrent(exportName, section);
            Log("setup-export: " + path);
            Log("setup-section: " + AkronSetupPacks.FormatSection(section));
            return;
        }

        if (action == "import") {
            AkronSetupSection? section = null;
            string importPath = joined;
            if (AkronSetupPacks.TryParseSection(part2, out AkronSetupSection parsedSection) && !string.IsNullOrWhiteSpace(part2)) {
                section = parsedSection;
                importPath = JoinCommandText(part3, part4, part5, part6);
            }

            if (string.IsNullOrWhiteSpace(importPath)) {
                Log("usage: akron_setup import [scope] <file-or-name>");
                return;
            }

            Log("setup-imported: " + AkronSetupPacks.Import(importPath, section).ToString().ToLowerInvariant());
            if (section.HasValue) {
                Log("setup-section: " + AkronSetupPacks.FormatSection(section.Value));
            }
            return;
        }

        if (action == "importlatest") {
            AkronSetupSection? section = AkronSetupPacks.TryParseSection(part2, out AkronSetupSection parsedSection) && !string.IsNullOrWhiteSpace(part2)
                ? parsedSection
                : null;
            string path = AkronSetupPacks.ImportLatest(section);
            Log("setup-import-latest: " + path);
            if (section.HasValue) {
                Log("setup-section: " + AkronSetupPacks.FormatSection(section.Value));
            }
            return;
        }

        Log("unknown setup action: " + value);
    }

    [Command("akron_theme", "Akron overlay themes: status|next|copy-custom|export [name]|import <file-or-name>|import-latest")]
    public static void Theme(string action = "status", string value = "", string part2 = "", string part3 = "", string part4 = "", string part5 = "", string part6 = "") {
        string normalized = NormalizeToken(action);
        string joined = JoinCommandText(value, part2, part3, part4, part5, part6);
        switch (normalized) {
            case "":
            case "status":
                break;
            case "next":
                AkronModule.Settings.OverlayThemePreset = AkronOverlayThemes.NextPreset(AkronModule.Settings.OverlayThemePreset);
                break;
            case "copycustom":
                AkronOverlayThemes.CopyPresetToCustom();
                break;
            case "export":
                string exportedPath = AkronOverlayThemes.ExportCurrentTheme(joined);
                Log("theme-export: " + exportedPath);
                break;
            case "import":
                if (string.IsNullOrWhiteSpace(joined)) {
                    Log("usage: akron_theme import <file-or-name>");
                    return;
                }

                Log("theme-imported: " + AkronOverlayThemes.ImportTheme(joined).ToString().ToLowerInvariant());
                break;
            case "importlatest":
                string importedPath = AkronOverlayThemes.ImportLatestTheme();
                Log("theme-import-latest: " + importedPath);
                break;
            default:
                Log("unknown theme action: " + action);
                return;
        }

        Log("theme: " + AkronOverlayThemes.CurrentDisplayName());
        Log("theme-preset: " + AkronModule.Settings.OverlayThemePreset);
        Log("theme-directory: " + AkronOverlayThemes.GetThemeDirectory());
        Log("theme-window-color: " + FormatRgb(AkronOverlayThemes.CurrentDefinition().WindowColor));
        Log("theme-header-color: " + FormatRgb(AkronOverlayThemes.CurrentDefinition().HeaderColor));
    }

    // Console mirror for the Everest "Editable Flag Name" setting. The flag
    // inspector is a developer/map-inspection tool, not first-case player UI.
    [Command("akron_editable_flag", "show or set Akron editable flag name; use __empty__ to clear")]
    public static void EditableFlag(string value = "") {
        if (string.IsNullOrWhiteSpace(value)) {
            Log("editable-flag: " + AkronModule.Settings.EditableFlagName);
            return;
        }

        AkronModule.Settings.EditableFlagName = string.Equals(value, "__empty__", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : value;
        Log("editable-flag set: " + (string.IsNullOrWhiteSpace(AkronModule.Settings.EditableFlagName) ? "<empty>" : AkronModule.Settings.EditableFlagName));
    }

    // Console mirror for the Everest "TAS File Path" setting. The overlay can
    // show/play the configured file; TAS handoff remains developer-facing.
    [Command("akron_tas_file", "show or set Akron TAS file path; use __unset__ to clear")]
    public static void TasFile(string value = "", string part2 = "", string part3 = "", string part4 = "", string part5 = "", string part6 = "") {
        value = JoinCommandText(value, part2, part3, part4, part5, part6);
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "status", StringComparison.OrdinalIgnoreCase)) {
            Log("tas-file: " + (string.IsNullOrWhiteSpace(AkronModule.Settings.TasFilePath) ? "unset" : AkronModule.Settings.FormatPathForDisplay(AkronModule.Settings.TasFilePath)));
            return;
        }

        AkronModule.Settings.TasFilePath = string.Equals(value, "__unset__", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : value;
        Log("tas-file set: " + (string.IsNullOrWhiteSpace(AkronModule.Settings.TasFilePath) ? "unset" : AkronModule.Settings.FormatPathForDisplay(AkronModule.Settings.TasFilePath)));
    }

    [Command("akron_play_tas", "run Akron's configured TAS handoff action for automation")]
    public static void PlayTas(string _ = "") {
        AkronActions.LaunchTas();
        Log("tas-active: " + AkronInterop.IsTasActive().ToString().ToLowerInvariant());
        Log("tas-running: " + AkronInterop.IsTasRunning().ToString().ToLowerInvariant());
    }

    [Command("akron_broker_warnings", "show or set Akron broker warnings: on|off|status")]
    public static void BrokerWarnings(string action = "status") {
        switch ((action ?? string.Empty).Trim().ToLowerInvariant()) {
            case "":
            case "status":
                break;
            case "on":
                AkronModule.Settings.SpeedrunToolBrokerWarnings = true;
                break;
            case "off":
                AkronModule.Settings.SpeedrunToolBrokerWarnings = false;
                break;
            default:
                Log("unknown broker-warnings action: " + action);
                return;
        }

        Log("broker-warnings: " + AkronModule.Settings.SpeedrunToolBrokerWarnings.ToString().ToLowerInvariant());
    }

    // Command-only prompt automation. Users normally reach prompts by using the
    // relevant overlay action; this exists for tests and live verification.
    [Command("akron_broker_prompt", "show Akron broker prompt for save or load automation: save|load")]
    public static void BrokerPrompt(string action = "save") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        bool load;
        switch ((action ?? string.Empty).Trim().ToLowerInvariant()) {
            case "":
            case "save":
                load = false;
                break;
            case "load":
                load = true;
                break;
            default:
                Log("unknown broker prompt action: " + action);
                return;
        }

        AkronModule.PerformBrokerPromptForAutomation(level, load);
        Log("prompt: " + AkronPromptMenu.DescribeState());
    }

    [Command("akron_toggle_force_broker", "toggle Akron per-map force-broker override")]
    public static void ToggleForceBroker(string _ = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        AkronActions.ToggleForceBroker(level);
        Log("force-broker-override: " + AkronMapOverrides.ShouldForceBroker(level).ToString().ToLowerInvariant());
    }

    [Command("akron_toggle_unsafe_native_override", "toggle Akron unsafe native StartPos restore override for this map")]
    public static void ToggleUnsafeNativeOverride(string _ = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        AkronActions.ToggleUnsafeNativeOverride(level);
        Log("unsafe-native-override: " + AkronMapOverrides.ShouldAllowUnsafeSavestates(level).ToString().ToLowerInvariant());
    }

    [Command("akron_toggle_everest_safe_override", "toggle Akron Everest-safe override for this map")]
    public static void ToggleEverestSafeOverride(string _ = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        AkronActions.ToggleEverestSafeBypass(level);
        Log("everest-safe-override: " + AkronMapOverrides.ShouldDisableEverestSafeBlock(level).ToString().ToLowerInvariant());
    }

    [Command("akron_prompt_state", "show Akron prompt state for automation")]
    public static void PromptState(string _ = "") {
        Log("prompt: " + AkronPromptMenu.DescribeState());
    }

    [Command("akron_prompt_select", "select Akron prompt option by exact label or 1-based index")]
    public static void PromptSelect(string labelOrIndex = "", string part2 = "", string part3 = "", string part4 = "", string part5 = "", string part6 = "") {
        labelOrIndex = JoinCommandText(labelOrIndex, part2, part3, part4, part5, part6);
        if (!AkronPromptMenu.ExecuteOption(labelOrIndex)) {
            Log("prompt select failed: " + labelOrIndex);
            return;
        }

        Log("prompt: " + AkronPromptMenu.DescribeState());
    }
}
