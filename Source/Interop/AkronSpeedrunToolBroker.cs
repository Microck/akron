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

    [ModImportName("SpeedrunTool.TasAction")]
    private static class SpeedrunToolTasImports {
        public static Func<string, bool> SaveState = null!;
        public static Func<string, bool> LoadState = null!;
        public static Action<string> ClearState = null!;
        public static Func<string, bool> TasIsSaved = null!;
    }
}
