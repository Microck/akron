using System;
using System.Collections.Generic;
using System.Reflection;

namespace Celeste.Mod.Akron;

internal static class AkronVanillaVariantRouting {
    internal const string BaseTypeName = "ExtendedVariants.Variants.Vanilla.AbstractVanillaVariant";
    private static readonly Dictionary<string, string> Fields = new(StringComparer.Ordinal) {
        ["AirDashes"] = "DashMode", ["VanillaGameSpeed"] = "GameSpeed",
        ["DashAssist"] = "DashAssist", ["Hiccups"] = "Hiccups",
        ["InfiniteStamina"] = "InfiniteStamina", ["Invincible"] = "Invincible",
        ["InvisibleMotion"] = "InvisibleMotion", ["LowFriction"] = "LowFriction",
        ["MirrorMode"] = "MirrorMode", ["NoGrabbing"] = "NoGrabbing",
        ["PlayAsBadeline"] = "PlayAsBadeline", ["SuperDashing"] = "SuperDashing",
        ["ThreeSixtyDashing"] = "ThreeSixtyDashing"
    };
    internal static bool IsKnownVanilla(string name) => name != null && Fields.ContainsKey(name);
    internal static bool HasVanillaBase(Type handler) {
        for (Type current = handler; current != null; current = current.BaseType)
            if (current.FullName == BaseTypeName) return true;
        return false;
    }
    internal static FieldInfo AssistField(string name) =>
        name != null && Fields.TryGetValue(name, out string field)
            ? typeof(global::Celeste.Assists).GetField(field, BindingFlags.Public | BindingFlags.Instance)
            : null;
    internal static bool TryResolveAccess(Type handler, Type baseType, Type uiType, Type variantType,
        string name, bool hasSave, out FieldInfo assist, out MethodInfo setter, out string error) {
        assist = AssistField(name); setter = null; error = string.Empty;
        if (variantType != null) setter = uiType?.GetMethod("SetVariantValue",
            BindingFlags.Static | BindingFlags.Public, null, new[] { variantType, typeof(object) }, null);
        if (!HasVanillaBase(handler) || baseType == null || handler == null || !baseType.IsAssignableFrom(handler) ||
            assist == null || setter?.ReturnType != typeof(void)) {
            error = name + ": vanilla assist integration unavailable."; return false;
        }
        if (!hasSave) { error = name + ": vanilla assists require an active save."; return false; }
        return true;
    }
    internal static bool IsMapApplied(object map, object defaultValue,
        bool inLevel, bool hooksActive, bool hasSession, bool userOverride) =>
        inLevel && hooksActive && hasSession && !userOverride && map != null && !Equals(map, defaultValue);
    internal static object EffectiveValue(object player, object map, object defaultValue,
        bool inLevel, bool hooksActive, bool hasSession, bool userOverride) =>
        IsMapApplied(map, defaultValue, inLevel, hooksActive, hasSession, userOverride) ? map : player;
}
