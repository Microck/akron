using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Celeste.Mod;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed class AkronExtendedVariantOption {
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public object CurrentValue { get; set; }
    public object DefaultValue { get; set; }
    public bool IsDefault { get; set; }
    public bool IsMapDefined { get; set; }
    public string ConfiguredValueText { get; set; } = string.Empty;
}

public static class AkronExtendedVariants {
    private const string ModuleAssemblyName = "ExtendedVariantMode";
    private const string ModuleTypeName = "ExtendedVariants.Module.ExtendedVariantsModule";
    private const string UiEntriesTypeName = "ExtendedVariants.UI.ModOptionsEntries";
    private const string TriggerManagerTypeName = "ExtendedVariants.ExtendedVariantTriggerManager";

    private static Type moduleType;
    private static Type variantEnumType;
    private static Type uiEntriesType;
    private static Type triggerManagerType;
    private static MethodInfo getCurrentVariantValueMethod;
    private static MethodInfo getCurrentMapDefinedVariantValueMethod;
    private static MethodInfo setVariantValueMethod;
    private static MethodInfo resetExtendedVariantsMethod;
    private static MethodInfo resetVanillaVariantsMethod;
    private static FieldInfo variantHandlersField;
    private static FieldInfo instanceField;
    private static PropertyInfo settingsProperty;
    private static int failedResolveAssemblyCount = -1;
    private static Dictionary<string, VariantMetadata> optionMetadataByName;
    private static Dictionary<string, VariantMetadata> optionMetadataByLabel;
    private static IReadOnlyList<VariantMetadata> optionMetadata;

    static AkronExtendedVariants() {
        // A missing optional mod is the common path. Keep that failure cheap, but
        // retry if Everest loads another assembly later in the session.
        AppDomain.CurrentDomain.AssemblyLoad += (_, _) => {
            failedResolveAssemblyCount = -1;
        };
    }

    public static bool Available => ResolveTypes();

    public static string StatusSummary {
        get {
            if (!Available) {
                return "EVM missing";
            }

            int activeCount = GetChangedOptionCount();
            return (MasterSwitch ? "On" : "Off") + " / " + activeCount.ToString(CultureInfo.InvariantCulture) + " changed";
        }
    }

    public static bool MasterSwitch {
        get => Available && ReadSettingsProperty<bool>("MasterSwitch");
        set {
            if (!Available) {
                return;
            }

            object settings = GetSettings();
            PropertyInfo property = settings?.GetType().GetProperty("MasterSwitch", BindingFlags.Instance | BindingFlags.Public);
            property?.SetValue(settings, value, null);

            // EVM only loads the expensive hooks while its master switch is on.
            // Calling the same public hook toggles its own menu uses keeps Akron
            // from maintaining a second physics implementation.
            object instance = GetInstance();
            MethodInfo method = moduleType.GetMethod(value ? "HookStuff" : "UnhookStuff", BindingFlags.Instance | BindingFlags.Public);
            method?.Invoke(instance, Array.Empty<object>());
        }
    }

    public static bool RandomizerEnabled {
        get => Available && ReadSettingsProperty<bool>("ChangeVariantsRandomly");
        set => WriteSettingsProperty("ChangeVariantsRandomly", value);
    }

    public static bool RandomizerRerollMode {
        get => Available && ReadSettingsProperty<bool>("RerollMode");
        set => WriteSettingsProperty("RerollMode", value);
    }

    public static bool DisplayEnabledVariants {
        get => Available && ReadSettingsProperty<bool>("DisplayEnabledVariantsToScreen");
        set => WriteSettingsProperty("DisplayEnabledVariantsToScreen", value);
    }

    public static int RandomizerInterval {
        get => Available ? ReadSettingsProperty<int>("ChangeVariantsInterval") : 0;
        set => WriteSettingsProperty("ChangeVariantsInterval", Calc.Clamp(value, 0, 3600));
    }

    public static int RandomizerMaxEnabled {
        get => Available ? ReadSettingsProperty<int>("MaxEnabledVariants") : 0;
        set => WriteSettingsProperty("MaxEnabledVariants", Calc.Clamp(value, 0, Math.Max(0, OptionCount)));
    }

    public static int OptionCount => EnsureOptionMetadata() ? optionMetadata.Count : 0;

    public static IReadOnlyList<AkronExtendedVariantOption> GetOptions() {
        if (!EnsureOptionMetadata()) {
            return Array.Empty<AkronExtendedVariantOption>();
        }

        return optionMetadata.Select(BuildOption).ToArray();
    }

    public static IReadOnlyList<AkronExtendedVariantOption> GetOptionDefinitions() {
        if (!EnsureOptionMetadata()) {
            return Array.Empty<AkronExtendedVariantOption>();
        }

        return optionMetadata.Select(BuildOptionDefinition).ToArray();
    }

    public static AkronExtendedVariantOption GetOption(string name) {
        if (!EnsureOptionMetadata() || string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        return optionMetadataByName.TryGetValue(name, out VariantMetadata metadata)
            ? BuildOption(metadata)
            : null;
    }

    public static AkronExtendedVariantOption GetOptionByLabel(string label) {
        if (!EnsureOptionMetadata() || string.IsNullOrWhiteSpace(label)) {
            return null;
        }

        return optionMetadataByLabel.TryGetValue(label, out VariantMetadata metadata)
            ? BuildOption(metadata)
            : null;
    }

    public static bool TryToggleBoolean(string name, out string message) {
        AkronExtendedVariantOption option = GetOption(name);
        if (option?.CurrentValue is bool current) {
            SetVariantValue(option.Name, !current);
            message = option.Label + ": " + (!current ? "on" : "off");
            return true;
        }

        message = "Variant is not a boolean toggle: " + name;
        return false;
    }

    public static bool TryToggleConfigured(string name, out string message) {
        AkronExtendedVariantOption option = GetOption(name);
        if (option == null) {
            message = "Unknown variant: " + name;
            return false;
        }

        if (option.CurrentValue is bool) {
            return TryToggleBoolean(name, out message);
        }

        if (!option.IsDefault) {
            StoreConfiguredValue(option.Name, SerializeValue(option.CurrentValue));
            SetVariantValue(option.Name, option.DefaultValue);
            message = option.Label + ": off | " + FormatValue(option.CurrentValue);
            return true;
        }

        if (!TryGetConfiguredValue(option, out object configured, out message)) {
            message = "Set " + option.Label + " in the triangle menu first.";
            return false;
        }

        if (ValuesMatch(configured, option.DefaultValue)) {
            message = option.Label + " configured value matches the default.";
            return false;
        }

        SetVariantValue(option.Name, configured);
        message = option.Label + ": on | " + FormatValue(configured);
        return true;
    }

    public static bool TryAdjustNumber(string name, float delta, out string message) {
        AkronExtendedVariantOption option = GetOption(name);
        if (option == null) {
            message = "Unknown variant: " + name;
            return false;
        }

        if (option.CurrentValue is int currentInt) {
            int next = Calc.Clamp(currentInt + (int) Math.Round(delta), -100000, 100000);
            SetVariantValue(option.Name, next);
            message = option.Label + ": " + next.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (option.CurrentValue is float currentFloat) {
            float next = currentFloat + delta;
            if (float.IsNaN(next) || float.IsInfinity(next)) {
                next = Convert.ToSingle(option.DefaultValue, CultureInfo.InvariantCulture);
            }

            SetVariantValue(option.Name, next);
            message = option.Label + ": " + FormatValue(next);
            return true;
        }

        message = "Variant is not numeric: " + option.Label;
        return false;
    }

    public static bool TrySetFromText(string name, string rawValue, out string message) {
        AkronExtendedVariantOption option = GetOption(name);
        if (option == null) {
            message = "Unknown variant: " + name;
            return false;
        }

        if (TryConvertText(option, rawValue, out object converted, out message)) {
            SetVariantValue(option.Name, converted);
            message = option.Label + ": " + FormatValue(converted);
            return true;
        }

        return false;
    }

    public static bool TrySetValue(string name, object value, out string message) {
        AkronExtendedVariantOption option = GetOption(name);
        if (option == null) {
            message = "Unknown variant: " + name;
            return false;
        }

        SetVariantValue(option.Name, value);
        message = option.Label + ": " + FormatValue(value);
        return true;
    }

    public static bool TrySetConfiguredValue(string name, object value, out string message) {
        AkronExtendedVariantOption option = GetOption(name);
        if (option == null) {
            message = "Unknown variant: " + name;
            return false;
        }

        StoreConfiguredValue(option.Name, SerializeValue(value));
        if (!option.IsDefault) {
            SetVariantValue(option.Name, value);
            message = option.Label + ": on | " + FormatValue(value);
        } else {
            message = option.Label + ": configured | " + FormatValue(value);
        }

        return true;
    }

    public static bool TrySetConfiguredFromText(string name, string rawValue, out string message) {
        AkronExtendedVariantOption option = GetOption(name);
        if (option == null) {
            message = "Unknown variant: " + name;
            return false;
        }

        if (!TryConvertText(option, rawValue, out object converted, out message)) {
            return false;
        }

        StoreConfiguredValue(option.Name, SerializeConfiguredText(converted, rawValue));
        if (!option.IsDefault) {
            SetVariantValue(option.Name, converted);
            message = option.Label + ": on | " + FormatValue(converted);
        } else {
            message = option.Label + ": configured | " + FormatValue(converted);
        }

        return true;
    }

    public static bool TryResetConfigured(string name, out string message) {
        AkronExtendedVariantOption option = GetOption(name);
        if (option == null) {
            message = "Unknown variant: " + name;
            return false;
        }

        RemoveConfiguredValue(option.Name);
        SetVariantValue(option.Name, option.DefaultValue);
        message = option.Label + ": reset";
        return true;
    }

    public static object GetConfiguredOrCurrentValue(AkronExtendedVariantOption option) {
        if (option == null) {
            return null;
        }

        return TryGetConfiguredValue(option, out object configured, out _) ? configured : option.CurrentValue;
    }

    public static string DescribeConfiguredState(AkronExtendedVariantOption option) {
        if (option == null) {
            return string.Empty;
        }

        if (option.CurrentValue is bool) {
            return FormatValue(option.CurrentValue);
        }

        object displayed = GetConfiguredOrCurrentValue(option);
        string state = option.IsDefault ? "Off" : "On";
        return state + " | " + FormatValue(displayed);
    }

    public static void ResetAll() {
        ResetVanilla();
        ResetExtended();
    }

    public static bool IsUserControlledVariantActive(string name) {
        AkronExtendedVariantOption option = GetOption(name);
        return AkronPolicy.ShouldFlagExtendedVariantOption(option);
    }

    public static void RecordVariantCheatUseIfUserControlled(string name) {
        if (IsUserControlledVariantActive(name)) {
            AkronPolicy.RecordCheatUse("An Extended Variant Mode option changed active gameplay rules.");
        }
    }

    public static void RecordRandomizerCheatUseIfEnabled() {
        if (RandomizerEnabled) {
            AkronPolicy.RecordCheatUse("Extended Variant Mode randomizer changed active gameplay rules.");
        }
    }

    public static void ResetExtended() {
        if (Available) {
            resetExtendedVariantsMethod?.Invoke(GetInstance(), Array.Empty<object>());
        }
    }

    public static void ResetVanilla() {
        if (Available) {
            resetVanillaVariantsMethod?.Invoke(GetInstance(), Array.Empty<object>());
        }
    }

    public static string FormatValue(object value) {
        if (value == null) {
            return "null";
        }

        if (value is bool boolean) {
            return boolean ? "On" : "Off";
        }

        if (value is float floating) {
            return floating.ToString("0.###", CultureInfo.InvariantCulture);
        }

        if (value is double doubleValue) {
            return doubleValue.ToString("0.###", CultureInfo.InvariantCulture);
        }

        if (value is int integer) {
            return integer.ToString(CultureInfo.InvariantCulture);
        }

        if (IsDashDirectionMatrix(value)) {
            return FormatDashDirectionMatrix(value);
        }

        return value.ToString();
    }

    private static void SetVariantValue(string name, object value) {
        if (!Available) {
            return;
        }

        MasterSwitch = true;
        object variant = ParseVariant(name);
        setVariantValueMethod?.Invoke(null, new[] { variant, value });
    }

    private static bool TryConvertText(AkronExtendedVariantOption option, string rawValue, out object converted, out string message) {
        converted = null;
        message = string.Empty;
        string value = (rawValue ?? string.Empty).Trim();

        if (option.CurrentValue is bool) {
            if (TryParseBoolean(value, out bool boolean)) {
                converted = boolean;
                return true;
            }

            message = "Use on/off, true/false, or 1/0 for " + option.Label;
            return false;
        }

        if (option.CurrentValue is int) {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer)) {
                converted = integer;
                return true;
            }

            message = "Use an integer for " + option.Label;
            return false;
        }

        if (option.CurrentValue is float) {
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floating)) {
                converted = floating;
                return true;
            }

            message = "Use a decimal number for " + option.Label;
            return false;
        }

        if (option.CurrentValue is string) {
            converted = value;
            return true;
        }

        Type currentType = option.CurrentValue?.GetType();
        if (currentType?.IsEnum == true) {
            try {
                converted = Enum.Parse(currentType, value, true);
                return true;
            } catch {
                message = "Use one of: " + string.Join(", ", Enum.GetNames(currentType));
                return false;
            }
        }

        if (IsDashDirectionMatrix(option.CurrentValue)) {
            converted = BuildDashDirectionPreset(value);
            if (converted != null) {
                return true;
            }

            message = "Use dash-direction preset: all, cardinal, diagonal, no-up, no-down, horizontal, vertical, or none.";
            return false;
        }

        message = "Unsupported value type for " + option.Label + ": " + option.TypeName;
        return false;
    }

    private static bool TryParseBoolean(string value, out bool parsed) {
        switch (value.ToLowerInvariant()) {
            case "on":
            case "true":
            case "1":
            case "yes":
                parsed = true;
                return true;
            case "off":
            case "false":
            case "0":
            case "no":
                parsed = false;
                return true;
            default:
                parsed = false;
                return false;
        }
    }

    private static object BuildDashDirectionPreset(string preset) {
        string normalized = (preset ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.StartsWith("matrix:", StringComparison.OrdinalIgnoreCase) && normalized.Length == 16) {
            bool[][] parsed = new[] {
                new[] { false, false, false },
                new[] { false, false, false },
                new[] { false, false, false }
            };
            for (int i = 0; i < 9; i++) {
                char value = normalized["matrix:".Length + i];
                if (value != '0' && value != '1') {
                    return null;
                }

                parsed[i / 3][i % 3] = value == '1';
            }

            return parsed;
        }

        bool[][] matrix = new[] {
            new[] { false, false, false },
            new[] { false, false, false },
            new[] { false, false, false }
        };

        void Allow(int x, int y) {
            if (x != 1 || y != 1) {
                matrix[x][y] = true;
            }
        }

        switch (normalized) {
            case "all":
                for (int x = 0; x < 3; x++) {
                    for (int y = 0; y < 3; y++) {
                        Allow(x, y);
                    }
                }
                return matrix;
            case "cardinal":
                Allow(1, 0);
                Allow(0, 1);
                Allow(2, 1);
                Allow(1, 2);
                return matrix;
            case "diagonal":
                Allow(0, 0);
                Allow(2, 0);
                Allow(0, 2);
                Allow(2, 2);
                return matrix;
            case "no-up":
                for (int x = 0; x < 3; x++) {
                    for (int y = 1; y < 3; y++) {
                        Allow(x, y);
                    }
                }
                return matrix;
            case "no-down":
                for (int x = 0; x < 3; x++) {
                    for (int y = 0; y < 2; y++) {
                        Allow(x, y);
                    }
                }
                return matrix;
            case "horizontal":
                Allow(0, 1);
                Allow(2, 1);
                return matrix;
            case "vertical":
                Allow(1, 0);
                Allow(1, 2);
                return matrix;
            case "none":
                return matrix;
            default:
                return null;
        }
    }

    private static string FormatDashDirectionMatrix(object value) {
        bool[][] matrix = (bool[][]) value;
        int allowed = 0;
        for (int x = 0; x < 3; x++) {
            for (int y = 0; y < 3; y++) {
                if ((x != 1 || y != 1) && matrix[x][y]) {
                    allowed++;
                }
            }
        }

        return allowed.ToString(CultureInfo.InvariantCulture) + "/8 directions";
    }

    private static bool IsDashDirectionMatrix(object value) {
        return value is bool[][] matrix && matrix.Length == 3 && matrix.All(row => row?.Length == 3);
    }

    private static bool ValuesMatch(object current, object defaultValue) {
        if (IsDashDirectionMatrix(current) && IsDashDirectionMatrix(defaultValue)) {
            bool[][] currentMatrix = (bool[][]) current;
            bool[][] defaultMatrix = (bool[][]) defaultValue;
            for (int x = 0; x < 3; x++) {
                for (int y = 0; y < 3; y++) {
                    if (currentMatrix[x][y] != defaultMatrix[x][y]) {
                        return false;
                    }
                }
            }

            return true;
        }

        return Equals(current, defaultValue);
    }

    private static AkronExtendedVariantOption BuildOption(VariantMetadata metadata) {
        object current = GetCurrentValue(metadata.Variant);
        object mapDefined = GetCurrentMapDefinedValue(metadata.Variant, metadata.DefaultValue);
        return new AkronExtendedVariantOption {
            Name = metadata.Name,
            Label = metadata.Label,
            TypeName = metadata.TypeName,
            CurrentValue = current,
            DefaultValue = metadata.DefaultValue,
            IsDefault = ValuesMatch(current, metadata.DefaultValue),
            IsMapDefined = !ValuesMatch(mapDefined, metadata.DefaultValue) && ValuesMatch(current, mapDefined),
            ConfiguredValueText = GetConfiguredValueText(metadata.Name)
        };
    }

    private static AkronExtendedVariantOption BuildOptionDefinition(VariantMetadata metadata) {
        return new AkronExtendedVariantOption {
            Name = metadata.Name,
            Label = metadata.Label,
            TypeName = metadata.TypeName,
            CurrentValue = metadata.DefaultValue,
            DefaultValue = metadata.DefaultValue,
            IsDefault = true,
            ConfiguredValueText = GetConfiguredValueText(metadata.Name)
        };
    }

    private static bool TryGetConfiguredValue(AkronExtendedVariantOption option, out object configured, out string message) {
        configured = null;
        message = string.Empty;
        string configuredText = GetConfiguredValueText(option.Name);
        if (string.IsNullOrWhiteSpace(configuredText)) {
            message = "No configured value for " + option.Label;
            return false;
        }

        return TryConvertText(option, configuredText, out configured, out message);
    }

    private static string GetConfiguredValueText(string name) {
        Dictionary<string, string> values = AkronModule.Settings.ExtendedVariantConfiguredValues;
        return values != null && values.TryGetValue(name, out string configured) ? configured : string.Empty;
    }

    private static void StoreConfiguredValue(string name, string value) {
        AkronModule.Settings.ExtendedVariantConfiguredValues ??= new Dictionary<string, string>();
        AkronModule.Settings.ExtendedVariantConfiguredValues[name] = value ?? string.Empty;
    }

    private static void RemoveConfiguredValue(string name) {
        AkronModule.Settings.ExtendedVariantConfiguredValues?.Remove(name);
    }

    private static string SerializeConfiguredText(object value, string rawValue) {
        if (IsDashDirectionMatrix(value)) {
            string preset = (rawValue ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(preset) && !preset.StartsWith("matrix:", StringComparison.OrdinalIgnoreCase)) {
                return preset;
            }
        }

        return SerializeValue(value);
    }

    private static string SerializeValue(object value) {
        if (value == null) {
            return string.Empty;
        }

        if (value is float floating) {
            return floating.ToString("0.###", CultureInfo.InvariantCulture);
        }

        if (value is double doubleValue) {
            return doubleValue.ToString("0.###", CultureInfo.InvariantCulture);
        }

        if (value is int integer) {
            return integer.ToString(CultureInfo.InvariantCulture);
        }

        if (IsDashDirectionMatrix(value)) {
            bool[][] matrix = (bool[][]) value;
            char[] chars = new char[9];
            for (int x = 0; x < 3; x++) {
                for (int y = 0; y < 3; y++) {
                    chars[x * 3 + y] = matrix[x][y] ? '1' : '0';
                }
            }

            return "matrix:" + new string(chars);
        }

        return value.ToString();
    }

    private static object GetCurrentValue(object variant) {
        object triggerManager = GetTriggerManager();
        return getCurrentVariantValueMethod?.Invoke(triggerManager, new[] { variant });
    }

    private static object GetCurrentMapDefinedValue(object variant, object defaultValue) {
        object triggerManager = GetTriggerManager();
        return getCurrentMapDefinedVariantValueMethod?.Invoke(triggerManager, new[] { variant }) ?? defaultValue;
    }

    private static bool HasHandler(object variant) {
        return GetHandler(variant) != null;
    }

    private static object GetHandler(object variant) {
        object instance = GetInstance();
        if (variantHandlersField?.GetValue(instance) is IDictionary dictionary && dictionary.Contains(variant)) {
            return dictionary[variant];
        }

        return null;
    }

    private static object ParseVariant(string name) {
        return Enum.Parse(variantEnumType, name, true);
    }

    private static object GetTriggerManager() {
        object instance = GetInstance();
        FieldInfo field = moduleType.GetField("TriggerManager", BindingFlags.Instance | BindingFlags.Public);
        return field?.GetValue(instance);
    }

    private static object GetSettings() {
        return settingsProperty?.GetValue(null, null);
    }

    private static object GetInstance() {
        return instanceField?.GetValue(null);
    }

    private static T ReadSettingsProperty<T>(string name) {
        object settings = GetSettings();
        object value = settings?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(settings, null);
        return value is T typed ? typed : default;
    }

    private static void WriteSettingsProperty(string name, object value) {
        if (!Available) {
            return;
        }

        object settings = GetSettings();
        PropertyInfo property = settings?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        property?.SetValue(settings, value, null);
    }

    private static bool ResolveTypes() {
        if (moduleType != null &&
            variantEnumType != null &&
            uiEntriesType != null &&
            triggerManagerType != null &&
            instanceField != null &&
            settingsProperty != null &&
            variantHandlersField != null &&
            getCurrentVariantValueMethod != null &&
            setVariantValueMethod != null) {
            return true;
        }

        if (failedResolveAssemblyCount >= 0) {
            return false;
        }

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        Assembly assembly = assemblies
            .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, ModuleAssemblyName, StringComparison.OrdinalIgnoreCase));
        if (assembly == null) {
            failedResolveAssemblyCount = assemblies.Length;
            return false;
        }

        moduleType = assembly.GetType(ModuleTypeName);
        variantEnumType = moduleType?.GetNestedType("Variant", BindingFlags.Public);
        uiEntriesType = assembly.GetType(UiEntriesTypeName);
        triggerManagerType = assembly.GetType(TriggerManagerTypeName);
        if (moduleType == null || variantEnumType == null || uiEntriesType == null || triggerManagerType == null) {
            return false;
        }

        instanceField = moduleType.GetField("Instance", BindingFlags.Static | BindingFlags.Public);
        settingsProperty = moduleType.GetProperty("Settings", BindingFlags.Static | BindingFlags.Public);
        variantHandlersField = moduleType.GetField("VariantHandlers", BindingFlags.Instance | BindingFlags.Public);
        getCurrentVariantValueMethod = triggerManagerType.GetMethod("GetCurrentVariantValue", BindingFlags.Instance | BindingFlags.Public);
        getCurrentMapDefinedVariantValueMethod = triggerManagerType.GetMethod("GetCurrentMapDefinedVariantValue", BindingFlags.Instance | BindingFlags.Public);
        setVariantValueMethod = uiEntriesType.GetMethod("SetVariantValue", BindingFlags.Static | BindingFlags.Public);
        resetExtendedVariantsMethod = moduleType.GetMethod("ResetExtendedVariantsToDefaultSettings", BindingFlags.Instance | BindingFlags.Public);
        resetVanillaVariantsMethod = moduleType.GetMethod("ResetVanillaVariantsToDefaultSettings", BindingFlags.Instance | BindingFlags.Public);
        bool resolved = GetInstance() != null &&
               GetSettings() != null &&
               variantHandlersField != null &&
               getCurrentVariantValueMethod != null &&
               setVariantValueMethod != null;
        if (!resolved) {
            failedResolveAssemblyCount = assemblies.Length;
        }

        return resolved;
    }

    private static bool EnsureOptionMetadata() {
        if (!Available) {
            return false;
        }

        if (optionMetadata != null) {
            return true;
        }

        object[] variants = Enum.GetValues(variantEnumType).Cast<object>().ToArray();
        List<VariantMetadata> metadata = new List<VariantMetadata>(variants.Length);
        foreach (object variant in variants) {
            object handler = GetHandler(variant);
            if (handler == null) {
                continue;
            }

            MethodInfo defaultMethod = handler.GetType().GetMethod("GetDefaultVariantValue", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo typeMethod = handler.GetType().GetMethod("GetVariantType", BindingFlags.Instance | BindingFlags.Public);
            object defaultValue = defaultMethod?.Invoke(handler, Array.Empty<object>());
            Type declaredType = typeMethod?.Invoke(handler, Array.Empty<object>()) as Type;
            string name = variant.ToString();
            metadata.Add(new VariantMetadata(
                variant,
                name,
                FormatVariantLabel(name),
                (declaredType ?? defaultValue?.GetType())?.Name ?? "unknown",
                defaultValue));
        }

        optionMetadata = metadata
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        optionMetadataByName = optionMetadata.ToDictionary(option => option.Name, StringComparer.OrdinalIgnoreCase);
        optionMetadataByLabel = optionMetadata.ToDictionary(option => option.Label, StringComparer.OrdinalIgnoreCase);
        return true;
    }

    private static int GetChangedOptionCount() {
        object settings = GetSettings();
        object enabled = settings?.GetType().GetProperty("EnabledVariants", BindingFlags.Instance | BindingFlags.Public)?.GetValue(settings, null);
        return enabled is IDictionary dictionary ? dictionary.Count : GetOptions().Count(option => !option.IsDefault);
    }

    private static string FormatVariantLabel(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return string.Empty;
        }

        List<char> chars = new List<char>(name.Length + 8);
        for (int i = 0; i < name.Length; i++) {
            char current = name[i];
            if (i > 0 && char.IsUpper(current) && !char.IsUpper(name[i - 1])) {
                chars.Add(' ');
            }

            chars.Add(current);
        }

        string label = new string(chars.ToArray())
            .Replace("X", " X")
            .Replace("Y", " Y")
            .Replace("  ", " ")
            .Trim();
        return ShortenVariantLabel(label);
    }

    private static string ShortenVariantLabel(string label) {
        return label
            .Replace("Preserve Extra Dashes Underwater", "Keep Extra Dashes Underwater")
            .Replace("Preserve", "Keep")
            .Replace("Disable Auto Jump", "No Auto Jump")
            .Replace("Background", "BG")
            .Replace("Foreground", "FG")
            .Trim();
    }

    private sealed class VariantMetadata {
        public VariantMetadata(object variant, string name, string label, string typeName, object defaultValue) {
            Variant = variant;
            Name = name;
            Label = label;
            TypeName = typeName;
            DefaultValue = defaultValue;
        }

        public object Variant { get; }
        public string Name { get; }
        public string Label { get; }
        public string TypeName { get; }
        public object DefaultValue { get; }
    }
}
