using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

internal static class AkronSavestateRiskAnalyzer {
    private static readonly HashSet<string> SafeMaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "StrawberryJam2021/5-Grandmaster/maya"
    };

    private static readonly HashSet<Type> EarlyEntityTypes = new HashSet<Type>();
    private static readonly Dictionary<Type, Func<Level, Type, bool>> EarlySpecialChecks = new Dictionary<Type, Func<Level, Type, bool>>();
    private static readonly ConcurrentDictionary<Type, bool> TypeSafetyCache = new ConcurrentDictionary<Type, bool>();
    private static bool initialized;

    public static void Initialize() {
        if (initialized) {
            return;
        }

        initialized = true;
        RegisterLuaCutsceneChecks();
        RegisterBossesHelperChecks();
        AkronSaveLoadService.RegisterRiskHandler(CheckRisk);
    }

    private static void RegisterLuaCutsceneChecks() {
        Type luaCutsceneType = AkronReflection.GetType("LuaCutscenes", "Celeste.Mod.LuaCutscenes.LuaCutsceneEntity");
        if (luaCutsceneType != null) {
            EarlySpecialChecks[luaCutsceneType] = (level, type) => {
                if (level.InCutscene && level.GetFieldValue("onCutsceneSkip")?.GetType() == type) {
                    return true;
                }

                return level.Entities.Any(entity => type.IsInstanceOfType(entity) && !entity.GetFieldValue<bool>("Finished"));
            };
        }

        Type luaTalkerType = AkronReflection.GetType("LuaCutscenes", "Celeste.Mod.LuaCutscenes.LuaTalker");
        if (luaTalkerType != null) {
            EarlySpecialChecks[luaTalkerType] = (level, type) =>
                level.InCutscene && level.GetFieldValue("onCutsceneSkip")?.GetType() == type;
        }
    }

    private static void RegisterBossesHelperChecks() {
        Type bossControllerType = AkronReflection.GetType("BossesHelper", "Celeste.Mod.BossesHelper.Code.Entities.BossController");
        if (bossControllerType != null) {
            EarlyEntityTypes.Add(bossControllerType);
        }
    }

    private static bool CheckRisk(Level level, int slot, out string reason) {
        // Vanilla Celeste maps are the canonical native-savestate path. They do not
        // need the expensive unknown-entity structural scan used for custom content.
        if (level.Session.Area.GetSID().StartsWith("Celeste/", StringComparison.OrdinalIgnoreCase)) {
            reason = string.Empty;
            return false;
        }

        if (SafeMaps.Contains(level.Session.Area.SID)) {
            reason = string.Empty;
            return false;
        }

        foreach (KeyValuePair<Type, Func<Level, Type, bool>> pair in EarlySpecialChecks) {
            if (pair.Value(level, pair.Key)) {
                reason = "Native StartPos restores are blocked because [" + pair.Key.Name + "] may desync this map.";
                return true;
            }
        }

        foreach (Type type in EarlyEntityTypes) {
            if (level.Entities.Any(entity => type.IsInstanceOfType(entity))) {
                reason = "Native StartPos restores are blocked because [" + type.Name + "] may desync this map.";
                return true;
            }
        }

        foreach (Entity entity in level.Entities) {
            Type entityType = entity.GetType();
            if (!TypeSafetyCache.TryGetValue(entityType, out bool safe)) {
                safe = IsSafeType(entityType, new HashSet<Type>());
                TypeSafetyCache[entityType] = safe;
            }

            if (!safe) {
                reason = "Native StartPos restores are blocked because [" + entityType.Name + "] carries Lua-backed or otherwise unsafe runtime state.";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    private static bool IsSafeType(Type type, HashSet<Type> visiting) {
        if (type == null) {
            return true;
        }

        if (TypeSafetyCache.TryGetValue(type, out bool cached)) {
            return cached;
        }

        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Type) || type == typeof(IntPtr) || typeof(Delegate).IsAssignableFrom(type)) {
            return true;
        }

        string fullName = type.FullName ?? string.Empty;
        if (fullName.StartsWith("NLua.", StringComparison.Ordinal) ||
            fullName.StartsWith("KeraLua.", StringComparison.Ordinal) ||
            fullName.IndexOf("LuaCoroutine", StringComparison.OrdinalIgnoreCase) >= 0) {
            return false;
        }

        if (type.IsArray) {
            return IsSafeType(type.GetElementType(), visiting);
        }

        if (type.IsGenericType) {
            foreach (Type argument in type.GetGenericArguments()) {
                if (!IsSafeType(argument, visiting)) {
                    return false;
                }
            }
        }

        if (!visiting.Add(type)) {
            return true;
        }

        Type current = type;
        while (current != null) {
            foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
                if (!IsSafeType(field.FieldType, visiting)) {
                    visiting.Remove(type);
                    return false;
                }
            }
            current = current.BaseType;
        }

        visiting.Remove(type);
        return true;
    }
}
