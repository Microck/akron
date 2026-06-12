using System;
using System.Collections.Generic;
using Celeste;
using MonoMod.ModInterop;
using Monocle;

namespace Celeste.Mod.Akron;

[ModExportName("Megahack.SaveLoad")]
public static class AkronSaveLoadExports {
    public static object RegisterSaveLoadAction(
        Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState,
        Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState,
        Action clearState,
        Action<Level> beforeSaveState,
        Action<Level> beforeLoadState,
        Action preCloneEntities
    ) {
        return AkronSaveLoadService.RegisterSaveLoadAction(saveState, loadState, clearState, beforeSaveState, beforeLoadState, preCloneEntities);
    }

    public static object RegisterStaticTypes(Type type, params string[] memberNames) {
        return AkronSaveLoadService.RegisterStaticTypes(type, memberNames);
    }

    public static object RegisterRiskHandler(AkronSaveLoadRiskHandler handler) {
        AkronSaveLoadService.RegisterRiskHandler(handler);
        return handler;
    }

    public static void Unregister(object obj) {
        AkronSaveLoadService.Unregister(obj);
    }

    public static void IgnoreSaveState(Entity entity, bool based = false) {
        AkronSaveLoadService.IgnoreSaveState(entity, based);
    }

    public static void AddReturnSameObjectProcessor(Func<Type, bool> predicate) {
        AkronSaveLoadService.AddReturnSameObjectProcessor(predicate);
    }

    public static void RemoveReturnSameObjectProcessor(Func<Type, bool> predicate) {
        AkronSaveLoadService.RemoveReturnSameObjectProcessor(predicate);
    }

    public static void AddCustomDeepCloneProcessor(Func<object, object> processor) {
        AkronSaveLoadService.AddCustomDeepCloneProcessor(processor);
    }

    public static void RemoveCustomDeepCloneProcessor(Func<object, object> processor) {
        AkronSaveLoadService.RemoveCustomDeepCloneProcessor(processor);
    }

    public static object DeepClone(object from) {
        return AkronSaveLoadService.DeepClone(from);
    }

    public static string GetSlotName() {
        return AkronSaveLoadService.CurrentSlotName;
    }

    public static bool SaveState(int slot) {
        return AkronSaveLoadService.Save(Engine.Scene as Level, slot) == AkronSaveLoadResult.Success;
    }

    public static bool LoadState(int slot) {
        return AkronSaveLoadService.Load(Engine.Scene as Level, slot) == AkronSaveLoadResult.Success;
    }

    public static void ClearState(int slot) {
        AkronSaveLoadService.ClearSlot(slot);
    }

    public static bool IsSaved(int slot) {
        return AkronSaveLoadService.HasSlot(slot);
    }
}
