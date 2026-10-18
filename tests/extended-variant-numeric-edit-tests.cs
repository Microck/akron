using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ExtendedVariantNumericEditTests {
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GestureKeepsOriginalIntentAcrossIntermediateDefaults(bool initiallyActive) {
        var edit = new AkronNumericEditIntent();
        edit.Begin("zoom:float", initiallyActive);
        foreach (float value in new[] { 1f, 1.5f, 0.5f, 1f }) {
            Assert.Equal(initiallyActive, edit.Get("zoom:float"));
        }
        edit.End("zoom:float");
        Assert.Null(edit.Get("zoom:float"));
    }

    [Fact]
    public void NewActivationAndOtherFieldsCannotInheritPriorIntent() {
        var edit = new AkronNumericEditIntent();
        edit.Begin("zoom:float", true);
        Assert.Null(edit.Get("jumps:int"));
        edit.Begin("zoom:float", false);
        Assert.Equal(false, edit.Get("zoom:float"));
        edit.Begin("jumps:int", true);
        edit.End("zoom:float");
        Assert.Equal(true, edit.Get("jumps:int"));
        Assert.Null(edit.Get("zoom:float"));
    }

    [Fact]
    public void OriginalPublicInteropSignatureRemainsAvailable() {
        Assert.NotNull(typeof(AkronExtendedVariants).GetMethod("TrySetConfiguredValue",
            BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(string), typeof(object), typeof(string).MakeByRefType() }, null));
    }

    [Theory]
    [InlineData("DrawFloatValueRow", "InputFloat")]
    [InlineData("DrawIntStepperRow", "InputInt")]
    public void NativeTextActivationPrecedesMutationAndDeactivationFollowsIt(string method, string input) {
        string source = Read("Overlay/akron-overlay-popup-controls.cs");
        int start = source.IndexOf("private void " + method + "(", StringComparison.Ordinal);
        int next = source.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
        string body = source.Substring(start, next - start);
        int draw = body.IndexOf("bool textChanged = ImGui." + input, StringComparison.Ordinal);
        int activate = body.IndexOf("if (ImGui.IsItemActivated()) onTextActivated?.Invoke();", StringComparison.Ordinal);
        int setter = body.IndexOf("(textSetter ?? setter)(next);", StringComparison.Ordinal);
        int deactivate = body.IndexOf("if (ImGui.IsItemDeactivated()) onTextDeactivated?.Invoke();", StringComparison.Ordinal);
        Assert.True(draw >= 0 && activate > draw && setter > activate && deactivate > setter);
        Assert.Equal(2, body.Split("setter(next);").Length - 1); // +/- retain ordinary semantics
    }

    [Fact]
    public void BothNumericVariantsUseKeyedIntentAndKeepPolicyChecks() {
        string popup = Read("Overlay/akron-overlay-extended-variant-popups.cs");
        foreach (string kind in new[] { "int", "float" }) {
            string key = "popupId + \":\" + option.Name + \":" + kind + "\"";
            Assert.Contains("extendedVariantNumericEdit.Begin(" + key + ", !option.IsDefault)", popup);
            Assert.Contains("extendedVariantNumericEdit.Get(" + key + ")", popup);
            Assert.Contains("extendedVariantNumericEdit.End(" + key + ")", popup);
        }
        string interop = Read("Interop/akron-extended-variants.cs");
        int start = interop.IndexOf("public static bool TrySetConfiguredValue(string name, object value, bool?", StringComparison.Ordinal);
        int end = interop.IndexOf("public static bool TrySetConfiguredFromText", start, StringComparison.Ordinal);
        string body = interop.Substring(start, end - start);
        Assert.Contains("if (!CanChange(out message)) return false;", body);
        Assert.Contains("GetOption(name)", body);
        Assert.Contains("StoreConfiguredValue(option.Name, SerializeValue(value))", body);
        Assert.Contains("if (applyCurrent ?? !option.IsDefault)", body);
        Assert.Contains("SetVariantValue(option.Name, value)", body);
    }

    [Theory]
    [InlineData(1f, 1f, "Zoom: off | 1")]
    [InlineData(1.5f, 1f, "Zoom: on | 1.5")]
    public void AppliedMessageReflectsActualDefaultState(float value, float defaultValue, string expected) {
        Assert.Equal(expected, AkronExtendedVariants.FormatAppliedConfiguredValueMessage("Zoom", value, defaultValue));
    }

    [Fact]
    public void IntegerDefaultMessageIsOff() {
        Assert.Equal("Jumps: off | 1", AkronExtendedVariants.FormatAppliedConfiguredValueMessage("Jumps", 1, 1));
        Assert.Equal("Jumps: on | 3", AkronExtendedVariants.FormatAppliedConfiguredValueMessage("Jumps", 3, 1));
    }

    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "../../../../Source", path));
}
