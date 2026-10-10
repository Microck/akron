using System;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule
{
    private static bool errorReportingNoticeShown;

    private static void ConfigureErrorReporting()
    {
        try
        {
            AkronTelemetry.Configure(TryGetSettings()?.ErrorReportingEnabled == true,
                AkronTelemetry.ResolveDsn(), Instance?.Metadata?.VersionString);
        }
        catch (Exception)
        {
            // A broken telemetry configuration must not break the mod.
        }
    }

    private static void ShowErrorReportingNotice(Scene scene)
    {
        // Wait for a player-facing scene with fonts loaded, not the startup loader.
        if (errorReportingNoticeShown || !AkronTelemetry.IsEnabled || scene is not (Overworld or Level)) return;
        errorReportingNoticeShown = true;
        scene.Add(new AkronToast(
            "Automatic Error Reports are on. Akron stack details and version go to Sentry.\nTurn off: Akron > Interface > Automatic Error Reports.",
            forceVisible: true, durationSeconds: 8f));
    }

    internal static void SetErrorReportingEnabled(bool enabled)
    {
        AkronModuleSettings settings = TryGetSettings();
        if (settings == null || (enabled && !AkronTelemetry.IsConfigured)) return;
        settings.ErrorReportingEnabled = enabled;
        if (!enabled) AkronTelemetry.Stop(flush: false);
        if (!SaveAkronSettingsNow("error reporting consent"))
        {
            settings.ErrorReportingEnabled = false;
            AkronTelemetry.Stop(flush: false);
            Engine.Scene?.Add(new AkronToast("Error reports are off. Could not save this preference; check it after restarting."));
            return;
        }
        if (enabled) ConfigureErrorReporting();
    }
}
