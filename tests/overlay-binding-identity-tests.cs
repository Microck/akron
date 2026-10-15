using System;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class OverlayBindingIdentityTests
{
    [Fact]
    public void OverlayDefaultResetPreservesExistingNativeBindingIdentity()
    {
        // ButtonBinding collection getters have invalid IL in stripped Everest
        // references. Native clear/rebind acceptance is recorded separately.
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/akron-module-settings-label-rows.cs"));
        int start = source.IndexOf("public static bool EnsureCurrentOverlayToggleDefault", StringComparison.Ordinal);
        int end = source.IndexOf("internal static bool ShouldUseDefaultOverlayToggleBinding", start, StringComparison.Ordinal);
        string reset = source[start..end];
        Assert.Contains("if (binding == null)", reset);
        Assert.Contains("settings.ToggleOverlay = CreateDefaultOverlayToggleBinding();", reset);
        string existing = reset[(reset.IndexOf("} else {", StringComparison.Ordinal) + 8)..];
        Assert.DoesNotContain("settings.ToggleOverlay =", existing);
        Assert.Contains("binding.Keys.Clear();", existing);
        Assert.Contains("binding.Keys.Add(Keys.Tab);", existing);
        Assert.Contains("binding.Buttons.Clear();", existing);
        Assert.Contains("binding.MouseButtons.Clear();", existing);
        Assert.DoesNotContain("binding.Button =", reset);
    }

}
