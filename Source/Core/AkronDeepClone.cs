using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    [ThreadStatic] private static bool cloneEventInstancesAsDormant;
    [ThreadStatic] private static List<EventInstance> dormantEventInstances;

    private static readonly DynamicDataMapAccessor DynamicDataMap =
        DynamicDataMapAccessor.Create(typeof(DynamicData)) ?? DynamicDataMapAccessor.Empty;
    private static readonly ConcurrentDictionary<Type, DynamicDataMapAccessor[]> GenericDynamicDataMaps =
        new ConcurrentDictionary<Type, DynamicDataMapAccessor[]>();
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
        cloneEventInstancesAsDormant = false;
        dormantEventInstances = null;
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
        slot.PreClonedEventInstances = RunWithDormantEventClones(() => {
            slot.SavedLevel.Entities.DeepClone(state);
            AkronLevelRenderState.RendererListField?.GetValue(slot.SavedLevel)?.DeepClone(state);
            slot.SaveDataState?.DeepClone(state);
        });
        return state;
    }

    public static object Clone(object source) {
        EnsureSharedState();
        return source.DeepClone(sharedDeepCloneState);
    }

    public static object CloneDormant(object source, out List<EventInstance> capturedEventInstances) {
        object clone = null;
        capturedEventInstances = RunWithDormantEventClones(() => clone = Clone(source));
        return clone;
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

    public static List<EventInstance> CopyIntoDormant(object source, object target) {
        return RunWithDormantEventClones(() => CopyInto(source, target));
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

            if (source is EventInstance eventInstance) {
                EventInstance clone = AkronEventInstanceUtils.Clone(eventInstance, cloneEventInstancesAsDormant);
                if (cloneEventInstancesAsDormant && clone != null) {
                    dormantEventInstances?.Add(clone);
                }
                return clone;
            }

            if (source is WeakReference weakReference) {
                return new WeakReference(weakReference.Target.DeepClone(state), weakReference.TrackResurrection);
            }

            object custom = AkronSaveLoadService.TryCustomClone(source);
            return custom;
        }
    }

    private static List<EventInstance> RunWithDormantEventClones(Action cloneAction) {
        bool previousDormantMode = cloneEventInstancesAsDormant;
        List<EventInstance> previousEventInstances = dormantEventInstances;
        List<EventInstance> capturedEventInstances = new List<EventInstance>();
        cloneEventInstancesAsDormant = true;
        dormantEventInstances = capturedEventInstances;
        try {
            cloneAction();
            return capturedEventInstances;
        } catch {
            AkronEventInstanceUtils.ReleaseDormantEventInstances(capturedEventInstances);
            capturedEventInstances.Clear();
            throw;
        } finally {
            if (previousDormantMode) {
                previousEventInstances?.AddRange(capturedEventInstances);
            }
            cloneEventInstancesAsDormant = previousDormantMode;
            dormantEventInstances = previousEventInstances;
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
        if (ReferenceEquals(source, clone)) {
            return;
        }

        // MonoMod stores DynamicData and DynData<T> values outside the target object in
        // separate conditional-weak-table sidecars. DeepCloner cannot discover those
        // references by walking the object, so copy both maps with the same clone state.
        DynamicDataMap.CloneEntry(source, clone, state);
        foreach (DynamicDataMapAccessor map in GetGenericDynamicDataMaps(source.GetType())) {
            map.CloneEntry(source, clone, state);
        }
    }

    private static DynamicDataMapAccessor[] GetGenericDynamicDataMaps(Type targetType) {
        if (targetType.IsValueType) {
            return Array.Empty<DynamicDataMapAccessor>();
        }

        return GenericDynamicDataMaps.GetOrAdd(targetType, CreateGenericDynamicDataMaps);
    }

    private static DynamicDataMapAccessor[] CreateGenericDynamicDataMaps(Type targetType) {
        List<DynamicDataMapAccessor> maps = new List<DynamicDataMapAccessor>();
        HashSet<Type> mappedTypes = new HashSet<Type>();
        for (Type current = targetType; current != null; current = current.BaseType) {
            mappedTypes.Add(current);
        }
        foreach (Type interfaceType in targetType.GetInterfaces()) {
            mappedTypes.Add(interfaceType);
        }

        foreach (Type mappedType in mappedTypes) {
            DynamicDataMapAccessor map = DynamicDataMapAccessor.Create(
                typeof(DynData<>).MakeGenericType(mappedType));
            if (map != null) {
                maps.Add(map);
            }
        }
        return maps.ToArray();
    }

    private sealed class DynamicDataMapAccessor {
        private delegate bool TryGetValue(object key, out object value);

        public static readonly DynamicDataMapAccessor Empty = new DynamicDataMapAccessor(
            (object _, out object value) => {
                value = null;
                return false;
            },
            (_, _) => { });

        private readonly TryGetValue tryGetValue;
        private readonly Action<object, object> replace;

        private DynamicDataMapAccessor(TryGetValue tryGetValue, Action<object, object> replace) {
            this.tryGetValue = tryGetValue;
            this.replace = replace;
        }

        public static DynamicDataMapAccessor Create(Type sidecarType) {
            FieldInfo mapField = sidecarType.GetField(
                "_DataMap",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            object map = mapField?.GetValue(null);
            if (map == null) {
                return null;
            }

            Type mapType = map.GetType();
            Type[] mapArguments = mapType.IsGenericType ? mapType.GetGenericArguments() : Type.EmptyTypes;
            if (mapArguments.Length != 2 || mapArguments[0] != typeof(object)) {
                return null;
            }

            MethodInfo createTyped = typeof(DynamicDataMapAccessor).GetMethod(
                nameof(CreateTyped),
                BindingFlags.Static | BindingFlags.NonPublic);
            return (DynamicDataMapAccessor) createTyped
                ?.MakeGenericMethod(mapArguments[1])
                .Invoke(null, new[] { map });
        }

        private static DynamicDataMapAccessor CreateTyped<TValue>(object mapObject) where TValue : class {
            ConditionalWeakTable<object, TValue> typedMap = (ConditionalWeakTable<object, TValue>) mapObject;
            return new DynamicDataMapAccessor(
                (object key, out object value) => {
                    bool found = typedMap.TryGetValue(key, out TValue typedValue);
                    value = typedValue;
                    return found;
                },
                (key, value) => {
                    typedMap.Remove(key);
                    typedMap.Add(key, (TValue) value);
                });
        }

        public void CloneEntry(object source, object clone, DeepCloneState state) {
            if (!tryGetValue(source, out object sidecar)) {
                return;
            }

            replace(clone, sidecar.DeepClone(state));
        }
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
