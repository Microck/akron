using System.Collections.Generic;
using System.Text;
using FMOD;

namespace Celeste.Mod.Akron;

public static class AkronAudioSplitter {
    public static string Describe() {
        return AkronModule.Settings.AudioSplitter
            ? "Configured"
            : "Off";
    }

    public static IReadOnlyList<string> ListDevices() {
        List<string> devices = new List<string> { "Default" };
        try {
            Audio.System.getLowLevelSystem(out FMOD.System lowLevel);
            lowLevel.getNumDrivers(out int driverCount);
            for (int i = 0; i < driverCount; i++) {
                StringBuilder builder = new StringBuilder(256);
                lowLevel.getDriverInfo(i, builder, 256, out _, out _, out _, out _);
                string name = builder.ToString();
                devices.Add(string.IsNullOrWhiteSpace(name) ? "Device " + i.ToString(System.Globalization.CultureInfo.InvariantCulture) : name);
            }
        } catch {
        }

        return devices;
    }

    public static string Status() {
        IReadOnlyList<string> devices = ListDevices();
        return "main=" + AkronModule.Settings.AudioSplitterMainDevice +
               " music=" + AkronModule.Settings.AudioSplitterMusicDevice +
               " sfx=" + AkronModule.Settings.AudioSplitterSfxDevice +
               " devices=" + devices.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
