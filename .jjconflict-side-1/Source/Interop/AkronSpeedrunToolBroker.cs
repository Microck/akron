using System;
using System.Linq;
using System.Reflection;
using Celeste;
using MonoMod.ModInterop;

namespace Celeste.Mod.Akron;

public static class AkronSpeedrunToolBroker {
    private static MethodInfo reflectedSaveStateTas;
    private static MethodInfo reflectedLoadStateTas;
    private static MethodInfo reflectedIsSaved;
    private static Type reflectedRoomTimerType;
    private static PropertyInfo reflectedSettingsInstance;
    private static PropertyInfo reflectedEnabledProperty;
    private static PropertyInfo reflectedRoomTimerTypeProperty;
    private static object reflectedRoomTimerOffValue;
    private static bool roomTimerReflectionWarningLogged;

    public static void Initialize() {
        EnsureImports();
        EnsureReflectionFallback();
    }

    // The shipped broker contract is "can Akron call the save/load entry points right now?"
    // Loader metadata checks are weaker than that and have already produced false negatives in
    // live testing even when Speedrun Tool was loaded and its callable entry points existed.
    public static bool Available => HasInteropImports() || HasReflectionFallback();

    public static AkronSaveLoadResult Save(int slot) {
        return Save(AkronSaveLoadService.GetSlotName(slot));
    }

    public static AkronSaveLoadResult Save(string slotName) {
        if (!Available) {
            return AkronSaveLoadResult.BrokerUnavailable;
        }

        bool saved = SpeedrunToolTasImports.SaveState != null
            ? SpeedrunToolTasImports.SaveState.Invoke(slotName)
            : (bool) reflectedSaveStateTas.Invoke(null, new object[] { slotName });
        return saved ? AkronSaveLoadResult.Success : AkronSaveLoadResult.Failed;
    }

    public static AkronSaveLoadResult Load(int slot) {
        return Load(AkronSaveLoadService.GetSlotName(slot));
    }

    public static AkronSaveLoadResult Load(string slotName) {
        if (!Available) {
            return AkronSaveLoadResult.BrokerUnavailable;
        }

        if (!IsSaved(slotName)) {
            return AkronSaveLoadResult.NoState;
        }

        bool loaded = SpeedrunToolTasImports.LoadState != null
            ? SpeedrunToolTasImports.LoadState.Invoke(slotName)
            : (bool) reflectedLoadStateTas.Invoke(null, new object[] { slotName });
        return loaded ? AkronSaveLoadResult.Success : AkronSaveLoadResult.Failed;
    }

    public static bool IsSaved(int slot) {
        return IsSaved(AkronSaveLoadService.GetSlotName(slot));
    }

    public static bool IsSaved(string slotName) {
        if (!Available) {
            return false;
        }

        if (SpeedrunToolTasImports.TasIsSaved != null) {
            return SpeedrunToolTasImports.TasIsSaved.Invoke(slotName);
        }

        return reflectedIsSaved != null &&
               (bool) reflectedIsSaved.Invoke(null, new object[] { slotName });
    }

    public static void Clear(string slotName) {
        if (!Available || string.IsNullOrWhiteSpace(slotName)) {
            return;
        }

        SpeedrunToolTasImports.ClearState?.Invoke(slotName);
    }

    internal static object SuppressRoomTimerHudForCapture() {
        if (!TryEnsureRoomTimerReflection()) {
            return null;
        }

        object settings = reflectedSettingsInstance?.GetValue(null);
        if (settings == null ||
            reflectedRoomTimerTypeProperty == null ||
            reflectedRoomTimerOffValue == null) {
            return null;
        }

        object previousEnabled = reflectedEnabledProperty?.GetValue(settings);
        object previous = reflectedRoomTimerTypeProperty.GetValue(settings);
        reflectedEnabledProperty?.SetValue(settings, false);
        if (!Equals(previous, reflectedRoomTimerOffValue)) {
            reflectedRoomTimerTypeProperty.SetValue(settings, reflectedRoomTimerOffValue);
        }

        return new SpeedrunToolRoomTimerState(settings, previousEnabled, previous);
    }

    internal static void RestoreRoomTimerHudAfterCapture(object state) {
        if (state is not SpeedrunToolRoomTimerState roomTimerState ||
            reflectedRoomTimerTypeProperty == null ||
            roomTimerState.Settings == null) {
            return;
        }

        reflectedRoomTimerTypeProperty.SetValue(roomTimerState.Settings, roomTimerState.PreviousRoomTimerType);
        reflectedEnabledProperty?.SetValue(roomTimerState.Settings, roomTimerState.PreviousEnabled);
    }

    private static bool EnsureImports() {
        typeof(SpeedrunToolTasImports).ModInterop();
        return true;
    }

    private static bool HasInteropImports() {
        EnsureImports();
        return SpeedrunToolTasImports.SaveState != null && SpeedrunToolTasImports.LoadState != null;
    }

    private static bool HasReflectionFallback() {
        EnsureReflectionFallback();
        return reflectedSaveStateTas != null && reflectedLoadStateTas != null && reflectedIsSaved != null;
    }

    private static void EnsureReflectionFallback() {
        if (reflectedSaveStateTas != null && reflectedLoadStateTas != null && reflectedIsSaved != null) {
            return;
        }

        Type saveSlotsManagerType = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => string.Equals(assembly.GetName().Name, "SpeedrunTool", StringComparison.OrdinalIgnoreCase))
            .Select(assembly => assembly.GetType("Celeste.Mod.SpeedrunTool.SaveLoad.SaveSlotsManager"))
            .FirstOrDefault(type => type != null);
        if (saveSlotsManagerType == null) {
            return;
        }

        reflectedSaveStateTas = saveSlotsManagerType.GetMethod("SaveStateTas", BindingFlags.Public | BindingFlags.Static);
        reflectedLoadStateTas = saveSlotsManagerType.GetMethod("LoadStateTas", BindingFlags.Public | BindingFlags.Static);
        reflectedIsSaved = saveSlotsManagerType.GetMethod("IsSaved", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
    }

    private static bool TryEnsureRoomTimerReflection() {
        if (reflectedSettingsInstance != null &&
            reflectedRoomTimerTypeProperty != null &&
            reflectedRoomTimerOffValue != null) {
            return true;
        }

        try {
            Assembly speedrunToolAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetType("Celeste.Mod.SpeedrunTool.SpeedrunToolSettings") != null);
            Type settingsType = speedrunToolAssembly?.GetType("Celeste.Mod.SpeedrunTool.SpeedrunToolSettings");
            reflectedRoomTimerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Celeste.Mod.SpeedrunTool.RoomTimer.RoomTimerType"))
                .FirstOrDefault(type => type != null);
            if (settingsType == null || reflectedRoomTimerType == null) {
                return false;
            }

            reflectedSettingsInstance = settingsType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            reflectedEnabledProperty = settingsType.GetProperty("Enabled", BindingFlags.Public | BindingFlags.Instance);
            reflectedRoomTimerTypeProperty = settingsType.GetProperty("RoomTimerType", BindingFlags.Public | BindingFlags.Instance);
            reflectedRoomTimerOffValue = Enum.Parse(reflectedRoomTimerType, "Off");
            return reflectedSettingsInstance != null &&
                   reflectedRoomTimerTypeProperty != null &&
                   reflectedRoomTimerOffValue != null;
        } catch (Exception exception) {
            if (!roomTimerReflectionWarningLogged) {
                Logger.Log(LogLevel.Warn, nameof(AkronSpeedrunToolBroker), "Failed to prepare Speedrun Tool room timer suppression for capture: " + exception);
                roomTimerReflectionWarningLogged = true;
            }

            reflectedSettingsInstance = null;
            reflectedEnabledProperty = null;
            reflectedRoomTimerTypeProperty = null;
            reflectedRoomTimerOffValue = null;
            return false;
        }
    }

    private sealed class SpeedrunToolRoomTimerState {
        public SpeedrunToolRoomTimerState(object settings, object previousEnabled, object previousRoomTimerType) {
            Settings = settings;
            PreviousEnabled = previousEnabled;
            PreviousRoomTimerType = previousRoomTimerType;
        }

        public object Settings { get; }
        public object PreviousEnabled { get; }
        public object PreviousRoomTimerType { get; }
    }

    [ModImportName("SpeedrunTool.TasAction")]
    private static class SpeedrunToolTasImports {
        public static Func<string, bool> SaveState = null!;
        public static Func<string, bool> LoadState = null!;
        public static Action<string> ClearState = null!;
        public static Func<string, bool> TasIsSaved = null!;
    }
}
