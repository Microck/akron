using System;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    private static void ConfigureErrorReporting() {
        try {
            AkronTelemetry.Configure(TryGetSettings()?.ErrorReportingEnabled == true,
                AkronTelemetry.ResolveDsn(), Instance?.Metadata?.VersionString);
        } catch (Exception) {
            // A broken telemetry configuration must not break the mod.
        }
    }

    internal static void SetErrorReportingEnabled(bool enabled) {
        AkronModuleSettings settings = TryGetSettings();
        if (settings == null || (enabled && !AkronTelemetry.IsConfigured)) return;
        settings.ErrorReportingEnabled = enabled;
        if (!enabled) AkronTelemetry.Stop(flush: false);
        if (!SaveAkronSettingsNow("error reporting consent")) {
            settings.ErrorReportingEnabled = false;
            AkronTelemetry.Stop(flush: false);
            Engine.Scene?.Add(new AkronToast("Error reports are off. Could not save this preference; check it after restarting."));
            return;
        }
        if (enabled) ConfigureErrorReporting();
    }
}
