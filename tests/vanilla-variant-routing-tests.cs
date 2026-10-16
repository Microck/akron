using System;
using System.IO;
using Xunit;

namespace ExtendedVariants.Variants.Vanilla {
    public abstract class AbstractVanillaVariant { }
}
namespace Celeste.Mod.Akron.Tests {
public sealed class VanillaVariantRoutingTests {
    public sealed class VanillaHandler : ExtendedVariants.Variants.Vanilla.AbstractVanillaVariant { }
    public sealed class ExtendedHandler { }
    public enum Variant { NoGrabbing }
    public static class ValidUi { public static void SetVariantValue(Variant variant, object value) { } }
    public static class WrongReturnUi { public static bool SetVariantValue(Variant variant, object value) => true; }
    public static class WrongArgumentUi { public static void SetVariantValue(Variant variant, bool value) { } }

    [Theory]
    [InlineData("AirDashes", "DashMode", typeof(global::Celeste.Assists.DashModes))]
    [InlineData("VanillaGameSpeed", "GameSpeed", typeof(int))]
    [InlineData("DashAssist", "DashAssist", typeof(bool))]
    [InlineData("Hiccups", "Hiccups", typeof(bool))]
    [InlineData("InfiniteStamina", "InfiniteStamina", typeof(bool))]
    [InlineData("Invincible", "Invincible", typeof(bool))]
    [InlineData("InvisibleMotion", "InvisibleMotion", typeof(bool))]
    [InlineData("LowFriction", "LowFriction", typeof(bool))]
    [InlineData("MirrorMode", "MirrorMode", typeof(bool))]
    [InlineData("NoGrabbing", "NoGrabbing", typeof(bool))]
    [InlineData("PlayAsBadeline", "PlayAsBadeline", typeof(bool))]
    [InlineData("SuperDashing", "SuperDashing", typeof(bool))]
    [InlineData("ThreeSixtyDashing", "ThreeSixtyDashing", typeof(bool))]
    public void EveryVanillaHandlerMapsToItsCanonicalTypedField(string name, string field, Type type) {
        Assert.True(AkronVanillaVariantRouting.IsKnownVanilla(name));
        var resolved = AkronVanillaVariantRouting.AssistField(name);
        Assert.NotNull(resolved); Assert.Equal(field, resolved.Name); Assert.Equal(type, resolved.FieldType);
    }

    [Theory]
    [InlineData("TrueNoGrabbing")]
    [InlineData("GameSpeed")]
    [InlineData("ZoomLevel")]
    public void ExtendedIdentifiersNeverBecomeCanonicalAssists(string name) {
        Assert.False(AkronVanillaVariantRouting.IsKnownVanilla(name));
        Assert.Null(AkronVanillaVariantRouting.AssistField(name));
        Assert.False(AkronVanillaVariantRouting.HasVanillaBase(typeof(ExtendedHandler)));
    }

    [Fact]
    public void ActualBaseChainIsRequiredAndValidatedContractResolves() {
        Assert.True(AkronVanillaVariantRouting.HasVanillaBase(typeof(VanillaHandler)));
        Assert.True(Resolve(typeof(VanillaHandler), typeof(ExtendedVariants.Variants.Vanilla.AbstractVanillaVariant), typeof(ValidUi), typeof(Variant), "NoGrabbing", true));
        Assert.False(Resolve(typeof(ExtendedHandler), typeof(ExtendedVariants.Variants.Vanilla.AbstractVanillaVariant), typeof(ValidUi), typeof(Variant), "NoGrabbing", true));
    }
    [Fact]
    public void MissingPrerequisitesFailLocallyAndNullSaveCanRecover() {
        Type handler = typeof(VanillaHandler), parent = handler.BaseType!, ui = typeof(ValidUi), variant = typeof(Variant);
        Assert.False(Resolve(null, parent, ui, variant, "NoGrabbing", true));
        Assert.False(Resolve(handler, null, ui, variant, "NoGrabbing", true));
        Assert.False(Resolve(handler, typeof(string), ui, variant, "NoGrabbing", true));
        Assert.False(Resolve(handler, parent, null, variant, "NoGrabbing", true));
        Assert.False(Resolve(handler, parent, ui, null, "NoGrabbing", true));
        Assert.False(Resolve(handler, parent, ui, variant, "UnknownVanilla", true));
        Assert.False(Resolve(handler, parent, typeof(WrongReturnUi), variant, "NoGrabbing", true));
        Assert.False(Resolve(handler, parent, typeof(WrongArgumentUi), variant, "NoGrabbing", true));
        Assert.False(Resolve(handler, parent, ui, variant, "NoGrabbing", false));
        Assert.True(Resolve(handler, parent, ui, variant, "NoGrabbing", true));
    }
    private static bool Resolve(Type? handler, Type? parent, Type? ui, Type? variant, string name, bool save) {
        bool result = AkronVanillaVariantRouting.TryResolveAccess(handler, parent, ui, variant, name, save, out var field, out var setter, out var error);
        if (result) { Assert.NotNull(field); Assert.NotNull(setter); Assert.Empty(error); }
        else Assert.NotEmpty(error);
        return result;
    }

    [Fact]
    public void MapOverrideRequiresEveryProviderGateAndIgnoresDefaultMapValues() {
        for (int flags = 0; flags < 16; flags++) {
            bool level = (flags & 1) != 0, hooks = (flags & 2) != 0, session = (flags & 4) != 0, overridden = (flags & 8) != 0;
            Assert.Equal(level && hooks && session && !overridden,
                AkronVanillaVariantRouting.EffectiveValue(false, true, false, level, hooks, session, overridden));
            Assert.Equal(true, AkronVanillaVariantRouting.EffectiveValue(true, false, false, level, hooks, session, overridden));
        }
        Assert.Equal(12, AkronVanillaVariantRouting.EffectiveValue(12, 10, 10, true, true, true, false));
        Assert.Equal(7, AkronVanillaVariantRouting.EffectiveValue(12, 7, 10, true, true, true, false));
        Assert.Equal(12, AkronVanillaVariantRouting.EffectiveValue(12, null, 10, true, true, true, false));
    }

    [Fact]
    public void UnavailableReadDoesNotBecomeAnActiveCheat() {
        var option = new AkronExtendedVariantOption { Name = "NoGrabbing", IsDefault = false, UnavailableReason = "Unavailable" };
        Assert.False(AkronPolicy.ShouldFlagExtendedVariantOption(option));
        option.UnavailableReason = string.Empty;
        Assert.True(AkronPolicy.ShouldFlagExtendedVariantOption(option));
    }
    [Fact]
    public void MapProvenanceDoesNotDependOnEqualityWithPlayerPreference() {
        Assert.False(AkronVanillaVariantRouting.IsMapApplied(true, false, true, false, true, false));
        Assert.False(AkronVanillaVariantRouting.IsMapApplied(true, false, true, true, true, true));
        Assert.True(AkronVanillaVariantRouting.IsMapApplied(true, false, true, true, true, false));
        string source = Read("Interop/akron-extended-variants.cs");
        string build = source.Substring(source.IndexOf("private static AkronExtendedVariantOption BuildOption("));
        build = build.Substring(0, build.IndexOf("private static AkronExtendedVariantOption BuildOptionDefinition"));
        Assert.DoesNotContain("GetCurrentMapDefinedValue", build);
        Assert.Contains("IsMapDefined = mapApplied", build);
        Assert.Contains("ImGui.TextWrapped(option.UnavailableReason)", Read("Overlay/akron-overlay-extended-variant-popups.cs"));
    }

    [Fact]
    public void ProductionWritesPreflightBeforeMutationAndTrackPartialFailures() {
        string source = Read("Interop/akron-extended-variants.cs");
        int start = source.IndexOf("private static bool TryApplyVariantValue", StringComparison.Ordinal);
        int end = source.IndexOf("private static bool TryConvertText", start, StringComparison.Ordinal);
        string body = source.Substring(start, end-start);
        Assert.True(body.IndexOf("TryResolveWriteRoute", StringComparison.Ordinal) < body.IndexOf("BeginExternalChange", StringComparison.Ordinal));
        Assert.True(body.IndexOf("invalid vanilla assist value type", StringComparison.Ordinal) < body.IndexOf("SetMasterSwitch(true)", StringComparison.Ordinal));
        Assert.Contains("finally {\n            TrackExternalChange(before);", body);
        Assert.Contains("return false;", body);
        Assert.Contains("UnavailableReason = unavailable", source);
        Assert.Contains("if (!string.IsNullOrEmpty(option.UnavailableReason))", source);
        Assert.Contains("if (!TryApplyVariantValue(option.Name, option.DefaultValue, out message)) return false;\n        RemoveConfiguredValue(option.Name);", source);
        Assert.Contains("TryToggleBoolean(option.Name, out string message);", Read("Overlay/akron-overlay-interop-entries.cs"));
    }
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source", path));
}
}
