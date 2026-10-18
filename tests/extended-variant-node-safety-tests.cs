using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ExtendedVariantNodeSafetyTests {
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(6)]
    [InlineData(100000)]
    [InlineData(int.MaxValue)]
    public void UnsafeNodeCountsAreRejected(int count) {
        Assert.False(AkronExtendedVariants.TryValidateGameplayValue("BadelineBossNodeCount", count, out string message));
        Assert.Contains("from 1 to 5", message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void ValidNodeCountsRemainAvailable(int count) =>
        Assert.True(AkronExtendedVariants.TryValidateGameplayValue("badelinebossnodecount", count, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData(0f)]
    public void WrongTypedNodeValuesCannotReachProvider(object value) =>
        Assert.False(AkronExtendedVariants.TryValidateGameplayValue("BadelineBossNodeCount", value, out _));

    [Theory]
    [InlineData("BadelineBossCount", 0)]
    [InlineData("BadelineAttackPattern", 0)]
    [InlineData("JumpCount", -1)]
    public void OtherVariantsKeepTheirExistingSemantics(string name, int value) {
        Assert.True(AkronExtendedVariants.TryValidateGameplayValue(name, value, out _));
        Assert.Equal(-100000, AkronExtendedVariants.GetIntegerMinimum(name));
        Assert.Equal(100000, AkronExtendedVariants.GetIntegerMaximum(name));
    }

    [Fact]
    public void PopupUsesNamedMinimum() {
        Assert.Equal(1, AkronExtendedVariants.GetIntegerMinimum("BadelineBossNodeCount"));
        Assert.Equal(5, AkronExtendedVariants.GetIntegerMaximum("BadelineBossNodeCount"));
        Assert.Contains("AkronExtendedVariants.GetIntegerMaximum(option.Name)", Read("Overlay/akron-overlay-extended-variant-popups.cs"));
        Assert.Contains("AkronExtendedVariants.GetIntegerMinimum(option.Name)", Read("Overlay/akron-overlay-extended-variant-popups.cs"));
    }

    [Fact]
    public void ProviderApplicationRejectsBeforeAnyExternalStateMutation() {
        var method = typeof(AkronExtendedVariants).GetMethod("TryApplyVariantValue", BindingFlags.NonPublic | BindingFlags.Static);
        object[] args = { "BadelineBossNodeCount", 0, null };
        Assert.False((bool)method.Invoke(null, args)); // no initialized module/provider needed
        Assert.Contains("from 1 to 5", (string)args[2]);
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronModule).Assembly.Location);
        var compiled = assembly.MainModule.GetType(typeof(AkronExtendedVariants).FullName).Methods.Single(m => m.Name == "TryApplyVariantValue");
        var calls = compiled.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        Assert.Equal("TryValidateGameplayValue", calls[0].Name);
    }

    [Fact]
    public void ConfigurationAndPersistedTextAreValidatedBeforeStorageOrActivation() {
        string source = Read("Interop/akron-extended-variants.cs");
        string typed = source.Split("public static bool TrySetConfiguredValue(string name, object value, bool?")[1]
            .Split("internal static string FormatAppliedConfiguredValueMessage")[0];
        Assert.True(typed.IndexOf("TryValidateGameplayValue", StringComparison.Ordinal) < typed.IndexOf("StoreConfiguredValue", StringComparison.Ordinal));
        string text = source.Split("private static bool TryConvertText(")[1].Split("if (option.CurrentValue is float)")[0];
        Assert.Contains("return TryValidateGameplayValue(option.Name, converted, out message);", text);
        string stored = source.Split("private static bool TryGetConfiguredValue(")[1].Split("private static string GetConfiguredValueText")[0];
        Assert.Contains("return TryConvertText(option, configuredText, out configured, out message);", stored);
    }

    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source", path));
}
