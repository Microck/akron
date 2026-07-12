using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoMod.ModInterop;

namespace Celeste.Mod.Akron;

public static class AkronInterop {
    private static readonly EverestModuleMetadata SpeedrunToolMetadata = new EverestModuleMetadata { Name = "SpeedrunTool" };
    private static readonly EverestModuleMetadata CelesteTasMetadata = new EverestModuleMetadata { Name = "CelesteTAS" };
    private static readonly EverestModuleMetadata ExtendedVariantModeMetadata = new EverestModuleMetadata { Name = "ExtendedVariantMode" };
    private static readonly EverestModuleMetadata CollabUtilsMetadata = new EverestModuleMetadata { Name = "CollabUtils2" };
    private static readonly EverestModuleMetadata MaddieHelpingHandMetadata = new EverestModuleMetadata { Name = "MaxHelpingHand" };
    private static readonly EverestModuleMetadata GravityHelperMetadata = new EverestModuleMetadata { Name = "GravityHelper" };
    private static readonly EverestModuleMetadata CommunalHelperMetadata = new EverestModuleMetadata { Name = "CommunalHelper" };
    private static readonly EverestModuleMetadata ExtendedCameraDynamicsMetadata = new EverestModuleMetadata { Name = "ExtendedCameraDynamics" };
    private static bool speedrunToolTabConflictMitigated;
    private static bool speedrunToolSaveLoadHooksRegistered;
    private static bool speedrunToolSaveLoadHookWarningLogged;
    private static bool extendedCameraDynamicsWarningLogged;
    private static object speedrunToolSaveLoadHookRegistration;
    private static MethodInfo speedrunToolSaveLoadUnregisterMethod;
    private static Type extendedCameraZoomHooksType;
    private static PropertyInfo extendedCameraAutomaticZoomingProperty;

    public static void Initialize() {
        typeof(RoomTimerImports).ModInterop();
        typeof(CelesteTasImports).ModInterop();
        typeof(ExtendedCameraDynamicsImports).ModInterop();
        EnsureSpeedrunToolSaveLoadHooksRegistered();
    }

    public static void UnregisterSpeedrunToolSaveLoadHooks() {
        try {
            if (speedrunToolSaveLoadHookRegistration != null && speedrunToolSaveLoadUnregisterMethod != null) {
                speedrunToolSaveLoadUnregisterMethod.Invoke(null, new[] { speedrunToolSaveLoadHookRegistration });
            }
        } catch (Exception exception) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to unregister Speedrun Tool save-load render suppression: " + exception.Message);
        }

        speedrunToolSaveLoadHookRegistration = null;
        speedrunToolSaveLoadUnregisterMethod = null;
        speedrunToolSaveLoadHooksRegistered = false;
    }

    public static bool SpeedrunToolLoaded => IsModLoaded(SpeedrunToolMetadata, "SpeedrunTool");
    public static bool CelesteTasLoaded => IsModLoaded(CelesteTasMetadata, "CelesteTAS", "CelesteTAS-EverestInterop");
    public static bool ExtendedVariantModeLoaded => IsModLoaded(ExtendedVariantModeMetadata, "ExtendedVariantMode");
    public static bool CollabUtilsLoaded => IsModLoaded(CollabUtilsMetadata, "CollabUtils2");
    public static bool MaddieHelpingHandLoaded => IsModLoaded(MaddieHelpingHandMetadata, "MaxHelpingHand");
    public static bool GravityHelperLoaded => IsModLoaded(GravityHelperMetadata, "GravityHelper");
    public static bool CommunalHelperLoaded => IsModLoaded(CommunalHelperMetadata, "CommunalHelper");
    public static bool ExtendedCameraDynamicsLoaded => IsModLoaded(ExtendedCameraDynamicsMetadata, "ExCameraDynamics", "ExtendedCameraDynamics");
    public static bool MotionSmoothingLoaded => AkronMotionSmoothingInterop.Loaded;
    public static bool RoomTimerAvailable =>
        SpeedrunToolLoaded &&
        RoomTimerImports.RoomTimerIsCompleted != null &&
        RoomTimerImports.GetRoomTime != null;

    public static bool CelesteTasInteropAvailable =>
        CelesteTasLoaded &&
        CelesteTasImports.IsTasActive != null &&
        CelesteTasImports.IsTasRunning != null;

    public static bool ExtendedCameraDynamicsInteropAvailable =>
        ExtendedCameraDynamicsLoaded &&
        ExtendedCameraDynamicsImports.ExtendedCameraHooksEnabled != null &&
        ExtendedCameraDynamicsImports.Create_CameraFocus != null &&
        ExtendedCameraDynamicsImports.Level_ForceZoomToCameraFocus != null;

    public static bool IsTasActive() {
        return CelesteTasInteropAvailable && CelesteTasImports.IsTasActive();
    }

    public static bool IsTasRunning() {
        return CelesteTasInteropAvailable && CelesteTasImports.IsTasRunning();
    }

    public static long? TryGetSpeedrunToolRoomTime() {
        return RoomTimerAvailable ? RoomTimerImports.GetRoomTime() : null;
    }

    public static bool AreExtendedCameraHooksActive() {
        if (!ExtendedCameraDynamicsLoaded || ExtendedCameraDynamicsImports.ExtendedCameraHooksEnabled == null) {
            return false;
        }

        try {
            return ExtendedCameraDynamicsImports.ExtendedCameraHooksEnabled();
        } catch (Exception exception) {
            LogExtendedCameraDynamicsWarning("Failed to query Extended Camera Dynamics hook state: " + exception.Message);
            return false;
        }
    }

    public static bool TryForceExtendedCameraFocus(Level level, Vector2 worldCenter, float zoom) {
        if (level == null || !ExtendedCameraDynamicsInteropAvailable) {
            return false;
        }

        try {
            object focus = ExtendedCameraDynamicsImports.Create_CameraFocus(worldCenter, zoom);
            if (focus == null) {
                return false;
            }

            ExtendedCameraDynamicsImports.Level_ForceZoomToCameraFocus(level, focus);
            return true;
        } catch (Exception exception) {
            LogExtendedCameraDynamicsWarning("Failed to force Extended Camera Dynamics focus: " + exception.Message);
            return false;
        }
    }

    public static bool TryRestoreExtendedCameraAutomaticZooming() {
        if (!ExtendedCameraDynamicsLoaded) {
            return false;
        }

        try {
            extendedCameraZoomHooksType ??= FindType("ExCameraDynamics", "Celeste.Mod.ExCameraDynamics.Code.Hooks.CameraZoomHooks");
            extendedCameraAutomaticZoomingProperty ??= extendedCameraZoomHooksType?.GetProperty("AutomaticZooming", BindingFlags.Public | BindingFlags.Static);
            if (extendedCameraAutomaticZoomingProperty == null || !extendedCameraAutomaticZoomingProperty.CanWrite) {
                return false;
            }

            extendedCameraAutomaticZoomingProperty.SetValue(null, true);
            return true;
        } catch (Exception exception) {
            LogExtendedCameraDynamicsWarning("Failed to restore Extended Camera Dynamics automatic zooming: " + exception.Message);
            return false;
        }
    }

    public static void EnsureSpeedrunToolTabDoesNotStealAkronOverlayBinding() {
        if (speedrunToolTabConflictMitigated) {
            return;
        }

        if (!SpeedrunToolLoaded || AkronModule.Settings.ToggleOverlay?.Keys?.Contains(Keys.Tab) != true) {
            return;
        }

        try {
            Type hotkeyConfigUiType = FindType("SpeedrunTool", "Celeste.Mod.SpeedrunTool.Other.HotkeyConfigUi");
            Type hotkeyType = FindType("SpeedrunTool", "Celeste.Mod.SpeedrunTool.Other.Hotkey");
            if (hotkeyConfigUiType == null || hotkeyType == null) {
                return;
            }

            FieldInfo configsField = hotkeyConfigUiType.GetField("HotkeyConfigs", BindingFlags.Public | BindingFlags.Static);
            object configs = configsField?.GetValue(null);
            object toggleSaveLoadUi = Enum.Parse(hotkeyType, "ToggleSaveLoadUI");
            object hotkeyConfig = configs?.GetType().GetProperty("Item")?.GetValue(configs, new[] { toggleSaveLoadUi });
            if (hotkeyConfig == null) {
                return;
            }

            MethodInfo getKeysMethod = hotkeyConfig.GetType().GetMethod("GetKeys", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo setKeysMethod = hotkeyConfig.GetType().GetMethod("SetKeys", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo updateVirtualButtonMethod = hotkeyConfig.GetType().GetMethod("UpdateVirtualButton", BindingFlags.Public | BindingFlags.Instance);
            if (getKeysMethod == null || setKeysMethod == null || updateVirtualButtonMethod == null) {
                return;
            }

            List<Keys> keys = getKeysMethod.Invoke(hotkeyConfig, Array.Empty<object>()) as List<Keys>;
            if (keys == null || !keys.Contains(Keys.Tab)) {
                return;
            }

            keys = keys.Where(key => key != Keys.Tab).ToList();
            setKeysMethod.Invoke(hotkeyConfig, new object[] { keys });
            updateVirtualButtonMethod.Invoke(hotkeyConfig, Array.Empty<object>());
            speedrunToolTabConflictMitigated = true;
            Logger.Log(LogLevel.Info, nameof(AkronModule), "Removed Tab from Speedrun Tool ToggleSaveLoadUI because Akron owns the Tab overlay binding.");
        } catch (Exception exception) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to mitigate Speedrun Tool Tab conflict: " + exception.Message);
        }
    }

    public static void EnsureSpeedrunToolSaveLoadHooksRegistered() {
        if (speedrunToolSaveLoadHooksRegistered) {
            return;
        }

        try {
            Type saveLoadExportsType = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => string.Equals(assembly.GetName().Name, "SpeedrunTool", StringComparison.OrdinalIgnoreCase))
                .Select(assembly => assembly.GetType("Celeste.Mod.SpeedrunTool.ModInterop.SaveLoadInterop+SaveLoadExports"))
                .FirstOrDefault(type => type != null);
            MethodInfo registerMethod = saveLoadExportsType?.GetMethod("RegisterSaveLoadAction", BindingFlags.Public | BindingFlags.Static);
            speedrunToolSaveLoadUnregisterMethod = saveLoadExportsType?.GetMethod("Unregister", BindingFlags.Public | BindingFlags.Static);
            if (registerMethod == null) {
                return;
            }

            Action<Dictionary<Type, Dictionary<string, object>>, Level> afterLoadState = (_, _) => {
                AkronModule.SuppressAkronRenderSurfacesAfterStateTransition();
                AkronModule.SuppressLagPauserForSpeedrunToolLoadState();
            };
            Action clearState = () => {
                AkronModule.SuppressAkronRenderSurfacesAfterStateTransition();
                AkronModule.SuppressLagPauserForSpeedrunToolLoadState();
            };
            Action<Level> beforeLoadState = _ => {
                AkronModule.SuppressAkronRenderSurfacesAfterStateTransition();
                AkronModule.SuppressLagPauserForSpeedrunToolLoadState();
            };

            speedrunToolSaveLoadHookRegistration = registerMethod.Invoke(null, new object[] {
                null,
                afterLoadState,
                clearState,
                null,
                beforeLoadState,
                null
            });
            speedrunToolSaveLoadHooksRegistered = true;
        } catch (Exception exception) {
            if (!speedrunToolSaveLoadHookWarningLogged) {
                Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to register Speedrun Tool save-load render suppression: " + exception.Message);
                speedrunToolSaveLoadHookWarningLogged = true;
            }
        }
    }

    public static void TryShowStudioPopup(string id, string title, string text) {
        if (CelesteTasLoaded && CelesteTasImports.ShowStudioPopupMessage != null) {
            CelesteTasImports.ShowStudioPopupMessage(id, title, text);
        }
    }

    public static string DescribeLoadedMods() {
        List<string> items = new List<string>();
        items.Add("Speedrun Tool: " + (SpeedrunToolLoaded ? "loaded" : "missing"));
        items.Add("CelesteTAS: " + (CelesteTasLoaded ? "loaded" : "missing"));
        items.Add("Collab Utils 2: " + (CollabUtilsLoaded ? "loaded" : "missing"));
        items.Add("Maddie's Helping Hand: " + (MaddieHelpingHandLoaded ? "loaded" : "missing"));
        items.Add("Gravity Helper: " + (GravityHelperLoaded ? "loaded" : "missing"));
        items.Add("Communal Helper: " + (CommunalHelperLoaded ? "loaded" : "missing"));
        items.Add("Extended Variant Mode: " + (AkronExtendedVariants.Available ? "loaded" : "missing"));
        items.Add("Extended Camera Dynamics: " + (ExtendedCameraDynamicsLoaded ? "loaded" : "missing"));
        items.Add("Motion Smoothing: " + (MotionSmoothingLoaded ? "loaded" : "missing"));
        return string.Join(" | ", items);
    }

    private static void LogExtendedCameraDynamicsWarning(string message) {
        if (extendedCameraDynamicsWarningLogged) {
            return;
        }

        extendedCameraDynamicsWarningLogged = true;
        Logger.Log(LogLevel.Warn, nameof(AkronModule), message);
    }

    private static bool IsModLoaded(EverestModuleMetadata metadata, params string[] assemblyNames) {
        return Everest.Loader.DependencyLoaded(metadata) ||
            Everest.Modules.Any(module => string.Equals(module.Metadata?.Name, metadata.Name, StringComparison.OrdinalIgnoreCase)) ||
            assemblyNames.Any(IsAssemblyLoaded);
    }

    private static bool IsAssemblyLoaded(string assemblyName) {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Any(assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
    }

    private static Type FindType(string assemblyName, string typeName) {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
            .Select(assembly => assembly.GetType(typeName))
            .FirstOrDefault(type => type != null);
    }

    [ModImportName("SpeedrunTool.RoomTimer")]
    private static class RoomTimerImports {
        public static Func<bool> RoomTimerIsCompleted = null!;
        public static Func<long> GetRoomTime = null!;
    }

    [ModImportName("CelesteTAS")]
    private static class CelesteTasImports {
        public static Func<bool> IsTasActive = null!;
        public static Func<bool> IsTasRunning = null!;
        public static Func<bool> IsTasRecording = null!;
        public static Action<string, string, string> ShowStudioPopupMessage = null!;
    }

    [ModImportName("ExtendedCameraDynamics")]
    private static class ExtendedCameraDynamicsImports {
        public static Func<bool> ExtendedCameraHooksEnabled = null!;
        public static Func<Vector2, float, object> Create_CameraFocus = null!;
        public static Action<Level, object> Level_ForceZoomToCameraFocus = null!;
    }
}
