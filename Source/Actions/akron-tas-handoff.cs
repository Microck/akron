using System;
using System.Reflection;

namespace Celeste.Mod.Akron;

internal static class AkronTasHandoff {
    internal static bool TryReadEnabled(Type settingsType, out bool enabled) {
        enabled = false;
        try {
            PropertyInfo instanceProperty = settingsType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            PropertyInfo enabledProperty = settingsType?.GetProperty("Enabled", BindingFlags.Public | BindingFlags.Instance);
            if (instanceProperty?.GetMethod?.IsPublic != true || instanceProperty.GetMethod.IsStatic != true ||
                enabledProperty?.PropertyType != typeof(bool) || enabledProperty.GetMethod?.IsPublic != true ||
                enabledProperty.GetMethod.IsStatic || instanceProperty.GetIndexParameters().Length != 0 ||
                enabledProperty.GetIndexParameters().Length != 0) return false;
            object instance = instanceProperty.GetValue(null);
            if (instance == null || !settingsType.IsInstanceOfType(instance)) return false;
            enabled = (bool)enabledProperty.GetValue(instance);
            return true;
        } catch (Exception) {
            return false;
        }
    }

    // Provider startup is synchronous; an empty/rejected file leaves it inactive.
    // Release only this overlay's input/pause ownership after confirmed startup.
    internal static bool Start(Action start, Func<bool> isActive, Action releaseOverlay) {
        start();
        if (!isActive()) return false;
        releaseOverlay();
        return true;
    }
}
