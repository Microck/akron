using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod;

namespace Celeste.Mod.Akron;

// Motion Smoothing is a runtime-detected external integration. This bridge keeps
// Akron free of compile-time references while still letting Akron's UI drive the
// loaded mod.
public static class AkronMotionSmoothingInterop {
    private static Type moduleType;
    private static Type settingsType;
    private static PropertyInfo settingsProperty;
    private static PropertyInfo instanceProperty;
    private static MethodInfo applySettingsMethod;
    private static bool warnedApplyFailure;
    private static bool modulesProbeFailed;
    private static bool? loaded;
    private static readonly Dictionary<string, (PropertyInfo Property, object Original, object Applied)> overrides = new();

    public static bool Loaded => loaded ??= IsModLoaded();

    public static void RefreshLoadedState() {
        loaded = IsModLoaded();
    }

    public static void ApplyAkronSettings() {
        try {
            object settings = GetSettings();
            if (settings == null) {
                return;
            }

            AkronFrameBypassRates rates = AkronRuntimeOptions.ResolveCurrentFrameBypassRates();
            bool fpsAllowed = AkronPolicy.CanUse(AkronFeatureKind.FpsBypass).Allowed;
            bool tpsAllowed = AkronPolicy.CanUse(AkronFeatureKind.TpsBypass).Allowed;
            bool ownsEnabled = fpsAllowed && AkronModule.Settings.FpsBypass || tpsAllowed && AkronModule.Settings.TpsBypass;
            Set(settings, "Enabled", rates.Active, fpsAllowed && tpsAllowed || ownsEnabled);
            Set(settings, "FrameRate", rates.DrawRate, fpsAllowed);
            SetEnum(settings, "UnlockCameraStrategy", AkronModule.Settings.FrameBypassCameraSmoothing switch {
                AkronCameraSmoothingMode.Fancy => "Hires",
                AkronCameraSmoothingMode.Fast => "Unlock",
                _ => "Off"
            }, fpsAllowed);
            Set(settings, "RenderMadelineWithSubpixels", AkronModule.Settings.FrameBypassSubpixelMadeline, fpsAllowed);
            Set(settings, "RenderBackgroundHires", AkronModule.Settings.FrameBypassSmoothBackground, fpsAllowed);
            Set(settings, "RenderForegroundHires", AkronModule.Settings.FrameBypassSmoothForeground, fpsAllowed);
            Set(settings, "HideStretchedEdges", AkronModule.Settings.FrameBypassHideStretchedEdges, fpsAllowed);
            SetEnum(settings, "ObjectSmoothing", AkronModule.Settings.FrameBypassObjectSmoothing.ToString(), fpsAllowed);
            SetEnum(settings, "FramerateIncreaseMethod", AkronModule.Settings.FrameBypassMethod.ToString(), fpsAllowed);
            Set(settings, "TasMode", AkronModule.Settings.FrameBypassTasMode, fpsAllowed);
            Set(settings, "SillyMode", AkronModule.Settings.FrameBypassSillyMode, fpsAllowed);
            Set(settings, "GameSpeed", (double) rates.UpdateRate, tpsAllowed);
            Set(settings, "GameSpeedInLevelOnly", true, tpsAllowed);
            ApplySettings();
        } catch (Exception exception) {
            if (warnedApplyFailure) {
                return;
            }

            warnedApplyFailure = true;
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to apply Akron Motion Smoothing settings: " + exception.Message);
        }
    }

    public static void RestoreOriginalSettings() {
        if (overrides.Count == 0) {
            return;
        }
        object settings = GetSettings();
        if (settings == null) {
            return;
        }
        foreach (var saved in overrides.Values) {
            if (Equals(saved.Property.GetValue(settings), saved.Applied)) {
                saved.Property.SetValue(settings, saved.Original);
            }
        }
        overrides.Clear();
        ApplySettings();
    }

    private static void Set(object settings, string propertyName, object value, bool allowed) {
        settingsType ??= settings.GetType();
        PropertyInfo property = settingsType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null || !property.CanRead || !property.CanWrite) {
            return;
        }
        if (!allowed) {
            if (overrides.Remove(propertyName, out var saved) && Equals(property.GetValue(settings), saved.Applied)) {
                property.SetValue(settings, saved.Original);
            }
            return;
        }
        object original = overrides.TryGetValue(propertyName, out var previous) ? previous.Original : property.GetValue(settings);
        overrides[propertyName] = (property, original, value);
        property.SetValue(settings, value);
    }

    private static void SetEnum(object settings, string propertyName, string valueName, bool allowed) {
        settingsType ??= settings.GetType();
        PropertyInfo property = settingsType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property != null) {
            Set(settings, propertyName, allowed ? Enum.Parse(property.PropertyType, valueName) : null, allowed);
        }
    }

    private static object GetSettings() {
        if (!Loaded) {
            return null;
        }

        moduleType ??= FindType("Celeste.Mod.MotionSmoothing.MotionSmoothingModule");
        settingsProperty ??= moduleType?.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static);
        object settings = settingsProperty?.GetValue(null);
        settingsType ??= settings?.GetType();
        return settings;
    }

    private static void ApplySettings() {
        moduleType ??= FindType("Celeste.Mod.MotionSmoothing.MotionSmoothingModule");
        if (moduleType == null) {
            return;
        }

        instanceProperty ??= moduleType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        object instance = instanceProperty?.GetValue(null) ?? FindLoadedModuleInstance();
        applySettingsMethod ??= moduleType.GetMethod(
            "ApplySettings",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static,
            null,
            Type.EmptyTypes,
            null);
        if (applySettingsMethod == null) {
            return;
        }

        applySettingsMethod.Invoke(applySettingsMethod.IsStatic ? null : instance, Array.Empty<object>());
    }

    private static object FindLoadedModuleInstance() {
        if (moduleType == null) {
            return null;
        }

        try {
            return Everest.Modules.FirstOrDefault(module => moduleType.IsInstanceOfType(module));
        } catch (InvalidProgramException) {
            modulesProbeFailed = true;
            return null;
        }
    }


    private static bool IsModLoaded() {
        return AppDomain.CurrentDomain.GetAssemblies().Any(assembly => string.Equals(assembly.GetName().Name, "MotionSmoothing", StringComparison.OrdinalIgnoreCase)) ||
               ModulesContainMotionSmoothing();
    }

    private static bool ModulesContainMotionSmoothing() {
        if (modulesProbeFailed) {
            return false;
        }

        try {
            return Everest.Modules.Any(module => string.Equals(module.Metadata?.Name, "MotionSmoothing", StringComparison.OrdinalIgnoreCase));
        } catch (InvalidProgramException) {
            modulesProbeFailed = true;
            return false;
        }
    }

    private static Type FindType(string typeName) {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => string.Equals(assembly.GetName().Name, "MotionSmoothing", StringComparison.OrdinalIgnoreCase))
            .Select(assembly => assembly.GetType(typeName) ?? FindModuleTypeByName(assembly))
            .FirstOrDefault(type => type != null);
    }

    private static Type FindModuleTypeByName(Assembly assembly) {
        try {
            return assembly.GetTypes().FirstOrDefault(type => type.Name == "MotionSmoothingModule");
        } catch (ReflectionTypeLoadException exception) {
            return exception.Types?.FirstOrDefault(type => type?.Name == "MotionSmoothingModule");
        }
    }
}
