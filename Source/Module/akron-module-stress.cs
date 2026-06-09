using System;
using System.Globalization;
using System.Reflection;
using Celeste.Mod;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
#if DEBUG
    private static bool stressMode;
    private static int stressFrame;
    private static int stressIteration;
    private static int stressSeed = 0xA0001;
    private static Random stressRng = new Random(stressSeed);
    private static readonly string StressBuildHash =
        typeof(AkronModule).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        typeof(AkronModule).Assembly.GetName().Version?.ToString() ??
        "unknown";

    internal static bool StressModeEnabled => stressMode;

    internal static string StressStatus() {
        return "stress: " + (stressMode ? "on" : "off") +
               ";seed=" + stressSeed.ToString(CultureInfo.InvariantCulture) +
               ";frame=" + stressFrame.ToString(CultureInfo.InvariantCulture) +
               ";iteration=" + stressIteration.ToString(CultureInfo.InvariantCulture) +
               ";managed-bytes=" + GC.GetTotalMemory(forceFullCollection: false).ToString(CultureInfo.InvariantCulture);
    }

    internal static void StartStressMode(int seed) {
        stressSeed = seed;
        stressRng = new Random(stressSeed);
        stressFrame = 0;
        stressIteration = 0;
        stressMode = true;
        Logger.Log(LogLevel.Info, "AkronStress", "Started stress mode seed=" + stressSeed.ToString(CultureInfo.InvariantCulture) + ", build=" + StressBuildHash);
    }

    internal static void StopStressMode() {
        if (!stressMode) {
            return;
        }

        Logger.Log(LogLevel.Info, "AkronStress", "Stopped stress mode seed=" + stressSeed.ToString(CultureInfo.InvariantCulture) + ", iteration=" + stressIteration.ToString(CultureInfo.InvariantCulture));
        stressMode = false;
    }

    private static void StressUpdate(Scene scene) {
        if (!stressMode || scene == null) {
            return;
        }

        EnsureOverlay(scene);
        stressFrame++;

        if (stressFrame % 15 == 0) {
            SetOverlayVisible(scene, !IsOverlayVisible);
        }

        if (Overlay != null) {
            Overlay.StressMutateUi(stressRng, stressIteration);
        }

        if (stressFrame % 120 == 0) {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        if (stressFrame % 300 == 0) {
            Logger.Log(LogLevel.Info, "AkronStress", StressStatus());
        }

        stressIteration++;
    }
#endif
}
