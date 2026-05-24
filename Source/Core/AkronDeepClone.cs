using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using FMOD.Studio;
using Force.DeepCloner;
using Force.DeepCloner.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using MonoMod.Cil;
using MonoMod.Utils;

namespace Celeste.Mod.Akron;

internal static class AkronDeepClone {
    [ThreadStatic] private static Stack<Component> hashSetComponents;
    [ThreadStatic] private static Stack<object> hashSetObjects;
    [ThreadStatic] private static Dictionary<object, object> dictionaryBackup;

    private static DeepCloneState sharedDeepCloneState = new DeepCloneState();
    private static bool configured;

    public static void Initialize() {
        if (configured) {
            return;
        }

        configured = true;
        DeepCloner.SetKnownTypesProcessor(ShouldUseOriginalObject);
        DeepCloner.SetPreCloneProcessor(CloneSpecialRuntimeObject);
        DeepCloner.SetPostCloneProcessor(RepairClonedCollection);
    }

    public static void Reset() {
        DeepCloner.ClearKnownTypesProcessor();
        DeepCloner.ClearPreCloneProcessor();
        DeepCloner.ClearPostCloneProcessor();
        sharedDeepCloneState = new DeepCloneState();
        configured = false;
    }

    public static void ClearSharedState() {
        sharedDeepCloneState = null;
    }

    public static void SetSharedState(DeepCloneState state) {
        sharedDeepCloneState = state;
    }

    public static DeepCloneState CreateSharedEntityState(AkronSaveLoadSlot slot) {
        if (slot?.SavedLevel == null) {
            return null;
        }

        DeepCloneState state = new DeepCloneState();
        slot.SavedLevel.Entities.DeepClone(state);
        AkronLevelRenderState.RendererListField?.GetValue(slot.SavedLevel)?.DeepClone(state);
        slot.SaveDataState?.DeepClone(state);
        return state;
    }

    public static object Clone(object source) {
        EnsureSharedState();
        return source.DeepClone(sharedDeepCloneState);
    }

    public static void CopyInto(object source, object target) {
        if (source == null || target == null) {
            return;
        }

        if (source.GetType() != target.GetType()) {
            throw new InvalidOperationException("AkronDeepClone.CopyInto requires matching source and target types.");
        }

        EnsureSharedState();
        source.DeepCloneTo(target, sharedDeepCloneState);
    }

    private static void EnsureSharedState() {
        sharedDeepCloneState ??= new DeepCloneState();
    }

    private static bool? ShouldUseOriginalObject(Type type) {
        if (type.FullName == "Celeste.Celeste" ||
            type == typeof(Settings) ||
            type == typeof(Type) ||
            type.IsSubclassOf(typeof(ModAsset)) ||
            type.IsSubclassOf(typeof(EverestModule)) ||
            type.IsSubclassOf(typeof(EverestModuleSettings)) ||
            type == typeof(EverestModuleMetadata) ||
            type == typeof(GraphicsDevice) ||
            type == typeof(GraphicsDeviceManager) ||
            type == typeof(Monocle.Commands) ||
            type == typeof(BitTag) ||
            type == typeof(Atlas) ||
            type.IsSubclassOf(typeof(GraphicsResource)) ||
            typeof(MTexture).IsAssignableFrom(type) ||
            string.Equals(type.Name, "ILHook", StringComparison.Ordinal) ||
            type.GetInterfaces().Any(candidate => candidate.FullName?.IndexOf("Detour", StringComparison.OrdinalIgnoreCase) >= 0) ||
            typeof(MemberInfo).IsAssignableFrom(type) ||
            typeof(Assembly).IsAssignableFrom(type)) {
            return true;
        }

        return AkronSaveLoadService.ShouldReturnSameObject(type) ? true : null;
    }

    private static object CloneSpecialRuntimeObject(object source, DeepCloneState state) {
        if (source == null) {
            return null;
        }

        lock (source) {
            if (source is VirtualAsset virtualAsset) {
                AkronVirtualAssetReloadTracker.Add(virtualAsset);
                return virtualAsset;
            }

            if (source is Scene) {
                if (source is Level && Engine.Scene is Level liveLevel) {
                    return liveLevel;
                }

                return source;
            }

            if (source is EventInstance eventInstance && AkronEventInstanceUtils.IsManualCloneNeeded(eventInstance)) {
                return AkronEventInstanceUtils.Clone(eventInstance);
            }

            if (source is WeakReference weakReference) {
                return new WeakReference(weakReference.Target.DeepClone(state), weakReference.TrackResurrection);
            }

            object custom = AkronSaveLoadService.TryCustomClone(source);
            return custom;
        }
    }

    private static object RepairClonedCollection(object source, object clone, DeepCloneState state) {
        if (clone == null) {
            return null;
        }

        lock (source) {
            Type type = clone.GetType();
            if (clone is HashSet<Component> componentSet) {
                hashSetComponents ??= new Stack<Component>();
                foreach (Component component in componentSet) {
                    if (component != null) {
                        hashSetComponents.Push(component);
                    }
                }

                componentSet.Clear();
                while (hashSetComponents.Count > 0) {
                    componentSet.Add(hashSetComponents.Pop());
                }
            } else if (IsHashSet(type) && clone is IEnumerable enumerable) {
                hashSetObjects ??= new Stack<object>();
                foreach (object item in enumerable) {
                    if (item != null) {
                        hashSetObjects.Push(item);
                    }
                }

                clone.GetType().GetMethod("Clear")?.Invoke(clone, null);
                MethodInfo add = clone.GetType().GetMethod("Add");
                while (hashSetObjects.Count > 0) {
                    add?.Invoke(clone, new[] { hashSetObjects.Pop() });
                }
            } else if (clone is IDictionary dictionary && dictionary.Count > 0 && IsComplexDictionaryKey(type)) {
                dictionaryBackup ??= new Dictionary<object, object>();
                foreach (DictionaryEntry entry in dictionary) {
                    dictionaryBackup[entry.Key] = entry.Value;
                }

                dictionary.Clear();
                foreach (KeyValuePair<object, object> entry in dictionaryBackup) {
                    dictionary[entry.Key] = entry.Value;
                }
                dictionaryBackup.Clear();
            }

            CloneDynamicDataIfPresent(source, clone, state);
        }

        return clone;
    }

    private static void CloneDynamicDataIfPresent(object source, object clone, DeepCloneState state) {
        // Speedrun Tool also preserves MonoMod DynamicData sidecars. The public
        // shape of DynamicData differs between MonoMod builds, so Akron keeps this
        // reflective and skips the sidecar when the runtime does not expose it.
        FieldInfo dataMapField = typeof(DynamicData).GetField("_DataMap", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (dataMapField?.GetValue(null) is not IDictionary dataMap || !dataMap.Contains(source)) {
            return;
        }

        object value = dataMap[source];
        object clonedValue = value.DeepClone(state);
        dataMap[clone] = clonedValue;
    }

    private static bool IsHashSet(Type type) {
        while (type != null) {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>)) {
                return true;
            }
            type = type.BaseType;
        }

        return false;
    }

    private static bool IsComplexDictionaryKey(Type type) {
        Type current = type;
        while (current != null) {
            if (current.IsGenericType && current.GetGenericArguments().Length == 2) {
                Type keyType = current.GetGenericArguments()[0];
                return !keyType.IsPrimitive && !keyType.IsEnum && keyType != typeof(string);
            }
            current = current.BaseType;
        }

        return false;
    }
}
