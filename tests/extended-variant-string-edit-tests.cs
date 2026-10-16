using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ExtendedVariantStringEditTests {
    [Theory]
    [InlineData("cold")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" cold ")]
    public void EditorReadsExactConfiguredTextWhileProviderStaysUnchanged(string configured) {
        var option = new AkronExtendedVariantOption { Name = "ColorGrading", CurrentValue = "default" };
        var values = new Dictionary<string, string> { [option.Name] = configured };
        for (int frame = 0; frame < 25; frame++) {
            Assert.Equal(configured, AkronExtendedVariants.GetStringEditorValue(option, values));
            Assert.Equal("default", option.CurrentValue);
        }
        values.Remove(option.Name);
        Assert.Equal("default", AkronExtendedVariants.GetStringEditorValue(option, values));
    }

    [Fact]
    public void MissingConfigurationUsesCurrentValueWithoutCrossVariantLeakage() {
        var option = new AkronExtendedVariantOption { Name = "ColorGrading", CurrentValue = "cold" };
        Assert.Equal("cold", AkronExtendedVariants.GetStringEditorValue(option, null));
        Assert.Equal("cold", AkronExtendedVariants.GetStringEditorValue(option, new Dictionary<string, string> { ["Other"] = "hot" }));
        Assert.Equal(string.Empty, AkronExtendedVariants.GetStringEditorValue(null, null));
    }

    [Fact]
    public void LatestStoredTextAndNullFallbackAreReadEachTime() {
        var option = new AkronExtendedVariantOption { Name = "ColorGrading", CurrentValue = "current" };
        var values = new Dictionary<string, string> { [option.Name] = "cold" };
        Assert.Equal("cold", AkronExtendedVariants.GetStringEditorValue(option, values));
        values[option.Name] = "hot";
        Assert.Equal("hot", AkronExtendedVariants.GetStringEditorValue(option, values));
        values[option.Name] = null!;
        Assert.Equal(string.Empty, AkronExtendedVariants.GetStringEditorValue(option, values));
        option.Name = null!;
        Assert.Equal("current", AkronExtendedVariants.GetStringEditorValue(option, values));
    }

    [Fact]
    public void StringPopupUsesStoredModelAndExistingConfiguredSetter() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-extended-variant-popups.cs"));
        int start = source.IndexOf("if (option.CurrentValue is string)", StringComparison.Ordinal);
        int end = source.IndexOf("if (option.CurrentValue is bool[][])", start, StringComparison.Ordinal);
        string body = source.Substring(start, end - start);
        Assert.Contains("GetStringEditorValue(option, AkronModule.Settings.ExtendedVariantConfiguredValues)", body);
        Assert.Contains("SetExtendedVariantConfiguredValue(option, value);", body);
        Assert.Contains("DrawExtendedVariantResetButton(option, popupId);", body);
    }
}
