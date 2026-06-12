using System.Globalization;
using System.Linq;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_evm", "control Extended Variant Mode: status|master on/off|reset|reset-vanilla|randomizer on/off|display on/off|list|set <variant> <value>|configure <variant> <value>|toggle <variant>")]
    public static void ExtendedVariants(string action = "status", string variant = "", string value = "") {
        string normalized = (action ?? string.Empty).Trim().ToLowerInvariant();
        if (!AkronExtendedVariants.Available) {
            Log("evm: missing");
            return;
        }

        switch (normalized) {
            case "":
            case "status":
                Log("evm: " + AkronExtendedVariants.StatusSummary);
                Log("evm-master: " + AkronExtendedVariants.MasterSwitch.ToString().ToLowerInvariant());
                Log("evm-randomizer: " + AkronExtendedVariants.RandomizerEnabled.ToString().ToLowerInvariant());
                Log("evm-display-enabled: " + AkronExtendedVariants.DisplayEnabledVariants.ToString().ToLowerInvariant());
                Log("evm-variant-count: " + AkronExtendedVariants.GetOptions().Count.ToString(CultureInfo.InvariantCulture));
                return;
            case "master":
                if (!TryParseBoolean(FirstNonEmpty(value, variant), out bool master)) {
                    Log("usage: akron_evm master on|off");
                    return;
                }

                if (master && !AkronModule.TryUse(AkronFeatureKind.ExtendedVariantMode)) {
                    Log("evm-master: blocked by current Akron policy");
                    return;
                }

                AkronExtendedVariants.MasterSwitch = master;
                Log("evm-master: " + AkronExtendedVariants.MasterSwitch.ToString().ToLowerInvariant());
                return;
            case "reset":
                AkronExtendedVariants.ResetExtended();
                Log("evm-reset: extended");
                return;
            case "reset-vanilla":
                AkronExtendedVariants.ResetVanilla();
                Log("evm-reset: vanilla");
                return;
            case "randomizer":
                if (!TryParseBoolean(FirstNonEmpty(value, variant), out bool randomizer)) {
                    Log("usage: akron_evm randomizer on|off");
                    return;
                }

                if (randomizer && !AkronModule.TryUse(AkronFeatureKind.ExtendedVariantMode)) {
                    Log("evm-randomizer: blocked by current Akron policy");
                    return;
                }

                AkronExtendedVariants.MasterSwitch = true;
                AkronExtendedVariants.RandomizerEnabled = randomizer;
                Log("evm-randomizer: " + AkronExtendedVariants.RandomizerEnabled.ToString().ToLowerInvariant());
                return;
            case "display":
                if (!TryParseBoolean(FirstNonEmpty(value, variant), out bool display)) {
                    Log("usage: akron_evm display on|off");
                    return;
                }

                AkronExtendedVariants.DisplayEnabledVariants = display;
                Log("evm-display-enabled: " + AkronExtendedVariants.DisplayEnabledVariants.ToString().ToLowerInvariant());
                return;
            case "list":
                foreach (AkronExtendedVariantOption option in AkronExtendedVariants.GetOptions()) {
                    Log("evm-option: " + option.Name + " = " + AkronExtendedVariants.DescribeConfiguredState(option) + " (current " + AkronExtendedVariants.FormatValue(option.CurrentValue) + ", default " + AkronExtendedVariants.FormatValue(option.DefaultValue) + ", " + option.TypeName + ")");
                }
                return;
            case "toggle":
                if (string.IsNullOrWhiteSpace(variant)) {
                    Log("usage: akron_evm toggle <variant>");
                    return;
                }

                if (!AkronModule.TryUse(AkronFeatureKind.ExtendedVariantMode)) {
                    Log("evm-toggle: blocked by current Akron policy");
                    return;
                }

                if (AkronExtendedVariants.TryToggleConfigured(variant, out string toggleMessage)) {
                    Log("evm-toggle: " + toggleMessage);
                } else {
                    Log("evm-toggle-failed: " + toggleMessage);
                }
                return;
            case "configure":
                if (string.IsNullOrWhiteSpace(variant) || string.IsNullOrWhiteSpace(value)) {
                    Log("usage: akron_evm configure <variant> <value>");
                    return;
                }

                if (!AkronModule.TryUse(AkronFeatureKind.ExtendedVariantMode)) {
                    Log("evm-configure: blocked by current Akron policy");
                    return;
                }

                if (AkronExtendedVariants.TrySetConfiguredFromText(variant, value, out string configureMessage)) {
                    Log("evm-configure: " + configureMessage);
                } else {
                    Log("evm-configure-failed: " + configureMessage);
                }
                return;
            case "set":
                if (string.IsNullOrWhiteSpace(variant) || string.IsNullOrWhiteSpace(value)) {
                    Log("usage: akron_evm set <variant> <value>");
                    return;
                }

                if (!AkronModule.TryUse(AkronFeatureKind.ExtendedVariantMode)) {
                    Log("evm-set: blocked by current Akron policy");
                    return;
                }

                if (AkronExtendedVariants.TrySetFromText(variant, value, out string message)) {
                    Log("evm-set: " + message);
                } else {
                    Log("evm-set-failed: " + message);
                }
                return;
            default:
                Log("unknown evm action: " + action);
                return;
        }
    }

    private static string FirstNonEmpty(params string[] parts) {
        return parts.FirstOrDefault(part => !string.IsNullOrWhiteSpace(part)) ?? string.Empty;
    }
}
