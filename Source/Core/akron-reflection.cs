using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod;

namespace Celeste.Mod.Akron;

internal static class AkronReflection {
    private const BindingFlags AnyInstanceOrStatic =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly ConcurrentDictionary<(Type Type, string Name), FieldInfo> FieldCache = new ConcurrentDictionary<(Type Type, string Name), FieldInfo>();
    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo> PropertyCache = new ConcurrentDictionary<(Type Type, string Name), PropertyInfo>();
    private static readonly ConcurrentDictionary<(Type Type, string Name, string Signature), MethodInfo> MethodCache = new ConcurrentDictionary<(Type Type, string Name, string Signature), MethodInfo>();

    public static EverestModule GetModule(string metadataName) {
        return Everest.Modules.FirstOrDefault(module => string.Equals(module.Metadata?.Name, metadataName, StringComparison.OrdinalIgnoreCase));
    }

    public static Assembly GetAssembly(string metadataName) {
        return GetModule(metadataName)?.GetType().Assembly;
    }

    public static Type GetType(string metadataName, string fullName) {
        return GetAssembly(metadataName)?.GetType(fullName);
    }

    public static FieldInfo GetFieldInfo(this Type type, string name) {
        return FieldCache.GetOrAdd((type, name), static key => {
            Type current = key.Type;
            while (current != null) {
                FieldInfo field = current.GetField(key.Name, AnyInstanceOrStatic);
                if (field != null) {
                    return field;
                }
                current = current.BaseType;
            }
            return null;
        });
    }

    public static PropertyInfo GetPropertyInfo(this Type type, string name) {
        return PropertyCache.GetOrAdd((type, name), static key => {
            Type current = key.Type;
            while (current != null) {
                PropertyInfo property = current.GetProperty(key.Name, AnyInstanceOrStatic);
                if (property != null) {
                    return property;
                }
                current = current.BaseType;
            }
            return null;
        });
    }

    public static MethodInfo GetMethodInfo(this Type type, string name, params Type[] parameterTypes) {
        string signature = BuildMethodSignature(parameterTypes);
        return MethodCache.GetOrAdd((type, name, signature), static key => {
            Type current = key.Type;
            while (current != null) {
                MethodInfo[] methods = current.GetMethods(AnyInstanceOrStatic);
                MethodInfo method = methods.FirstOrDefault(candidate => {
                    if (candidate.Name != key.Name) {
                        return false;
                    }

                    if (key.Signature == "*") {
                        return true;
                    }

                    if (key.Signature.Length == 0) {
                        return candidate.GetParameters().Length == 0;
                    }

                    ParameterInfo[] parameters = candidate.GetParameters();
                    if (parameters.Length == 0) {
                        return false;
                    }

                    string candidateSignature = BuildMethodSignature(parameters.Select(parameter => parameter.ParameterType));
                    return string.Equals(candidateSignature, key.Signature, StringComparison.Ordinal);
                });
                if (method != null) {
                    return method;
                }
                current = current.BaseType;
            }
            return null;
        });
    }

    public static object GetFieldValue(this object instance, string name) {
        return instance?.GetType().GetFieldInfo(name)?.GetValue(instance);
    }

    public static T GetFieldValue<T>(this object instance, string name) {
        object value = GetFieldValue(instance, name);
        return value is T typed ? typed : default;
    }

    public static object GetFieldValue(this Type type, string name) {
        return type?.GetFieldInfo(name)?.GetValue(null);
    }

    public static T GetFieldValue<T>(this Type type, string name) {
        object value = GetFieldValue(type, name);
        return value is T typed ? typed : default;
    }

    public static void SetFieldValue(this object instance, string name, object value) {
        instance?.GetType().GetFieldInfo(name)?.SetValue(instance, value);
    }

    public static void SetFieldValue(this Type type, string name, object value) {
        type?.GetFieldInfo(name)?.SetValue(null, value);
    }

    public static object GetPropertyValue(this object instance, string name) {
        return instance?.GetType().GetPropertyInfo(name)?.GetValue(instance);
    }

    public static object GetPropertyValue(this Type type, string name) {
        return type?.GetPropertyInfo(name)?.GetValue(null);
    }

    public static void SetPropertyValue(this object instance, string name, object value) {
        instance?.GetType().GetPropertyInfo(name)?.SetValue(instance, value);
    }

    public static void SetPropertyValue(this Type type, string name, object value) {
        type?.GetPropertyInfo(name)?.SetValue(null, value);
    }

    public static object InvokeMethod(this object instance, string name, params object[] parameters) {
        return instance?.GetType().GetMethodInfo(name, parameters?.Select(parameter => parameter?.GetType()).ToArray())?.Invoke(instance, parameters);
    }

    public static object InvokeMethod(this Type type, string name, params object[] parameters) {
        return type?.GetMethodInfo(name, parameters?.Select(parameter => parameter?.GetType()).ToArray())?.Invoke(null, parameters);
    }

    public static bool HasCustomAttributeNamed(this MemberInfo member, string attributeTypeName) {
        return member?.GetCustomAttributesData().Any(attribute => string.Equals(attribute.AttributeType.Name, attributeTypeName, StringComparison.Ordinal)) == true;
    }

    private static string BuildMethodSignature(IEnumerable<Type> parameterTypes) {
        if (parameterTypes == null) {
            return "*";
        }

        return string.Join("|", parameterTypes.Select(type => type?.AssemblyQualifiedName ?? "<null>"));
    }
}
