using System;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ExtendedVariantPopupIdentityTests
{
    [Theory]
    [InlineData("DashCount")]
    [InlineData("JumpCount")]
    [InlineData("Gravity")]
    public void ProviderNamesRoundTripThroughDistinctPopupKeys(string name)
    {
        string key = AkronOverlay.ExtendedVariantPopupKey(name);
        Assert.NotEqual(name, key);
        Assert.NotEqual("Dash Count", key);
        Assert.True(AkronOverlay.TryGetExtendedVariantPopupName(key, out string recovered));
        Assert.Equal(name, recovered);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Dash Count")]
    [InlineData("Extended Variants Randomizer")]
    [InlineData("ExtendedVariant:")]
    [InlineData("extendedvariant:DashCount")]
    public void OrdinaryLabelsAndInvalidKeysDoNotRouteToProvider(string? key)
    {
        Assert.False(AkronOverlay.TryGetExtendedVariantPopupName(key, out string name));
        Assert.Equal(string.Empty, name);
    }

    [Fact]
    public void ProviderPopupDispatchPrecedesLabelBasedCoreDispatch()
    {
        string source = Read("akron-overlay-popup-controls.cs");
        int provider = source.IndexOf("TryGetExtendedVariantPopupName(entry.OptionsPopupKey", StringComparison.Ordinal);
        int core = source.IndexOf("string.Equals(entry.Label, \"Dash Count\"", StringComparison.Ordinal);
        Assert.True(provider >= 0 && core > provider);
        Assert.Contains("entry.FeatureKind == AkronFeatureKind.ExtendedVariantMode", source);
        Assert.Contains("DrawExtendedVariantOptionPopupControls(AkronExtendedVariants.GetOption(variantName), popupId)", source);
    }

    [Fact]
    public void ProviderRowCapturesNameInsteadOfApplyingLabelBasedCoreDelta()
    {
        string source = Read("akron-overlay-interop-entries.cs");
        Assert.Contains("() => ToggleExtendedVariantOption(option.Name)", source);
        Assert.Contains("optionsPopupKey: ExtendedVariantPopupKey(option.Name)", source);
        Assert.DoesNotContain("ApplyOptionsPopupDelta(label, 1)", source);
        string entries = Read("akron-overlay-entries.cs");
        Assert.Contains("AkronExtendedVariants.TryToggleConfigured(name, out string message)", entries);
    }

    [Fact]
    public void PopupCapabilityAndFallbackSelectionUseStableIdentity()
    {
        Assert.Contains("AkronOverlay.HasOptionsPopup(OptionsPopupKey)", Read("akron-overlay-models.cs"));
        string fallback = Read("akron-overlay-options-popup.cs");
        Assert.Contains("string.Equals(action.Entry.OptionsPopupKey, openOptionsLabel", fallback);
        Assert.DoesNotContain("string.Equals(action.Entry.Label, openOptionsLabel", fallback);
        Assert.Contains("TryGetExtendedVariantPopupName(label, out string variantName)", fallback);
        Assert.Contains("IsOptionsPopupOpen(action.Entry.OptionsPopupKey)", Read("akron-overlay-sprite-renderer.cs"));
        Assert.DoesNotContain("string.Equals(action.Entry.Label, popupKey", Read("akron-overlay-imgui-popups.cs"));
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "../../../../Source/Overlay", file));
}
