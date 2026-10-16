using System;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class NoclipSpeedFeedbackTests {
    [Theory]
    [InlineData(false, 24, 24)]
    [InlineData(false, 2160, 900)]
    [InlineData(true, 9, 10)]
    [InlineData(true, 810, 450)]
    public void RepeatedClampedProposalChangesOnlyOnce(bool grab, int proposal, int expected) {
        var settings = new AkronModuleSettings { NoclipSpeed = 240, NoclipFloatSpeed = 90 };
        Assert.True(AkronOverlay.TrySetNoclipSpeedValue(settings, proposal, grab));
        Assert.Equal(expected, grab ? settings.NoclipFloatSpeed : settings.NoclipSpeed);
        for (int frame = 0; frame < 120; frame++)
            Assert.False(AkronOverlay.TrySetNoclipSpeedValue(settings, proposal, grab));
        Assert.True(AkronOverlay.TrySetNoclipSpeedValue(settings, grab ? 90 : 240, grab));
        Assert.False(AkronOverlay.TrySetNoclipSpeedValue(settings, grab ? 90 : 240, grab));
        Assert.Equal(240, settings.NoclipSpeed);
        Assert.Equal(90, settings.NoclipFloatSpeed);
    }

    [Theory]
    [InlineData("AdjustNoclipSpeed", false)]
    [InlineData("AdjustNoclipFloatSpeed", true)]
    [InlineData("SetNoclipSpeedMultiplier", false)]
    [InlineData("SetNoclipGrabSpeedMultiplier", true)]
    public void EveryFeedbackRouteRequiresChangedValue(string method, bool grab) {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-entries.cs"));
        int start = source.IndexOf("private static void " + method + "(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        string body = source.Substring(start, end - start);
        int guard = body.IndexOf("if (!TrySetNoclipSpeedValue", StringComparison.Ordinal);
        Assert.True(guard >= 0 && guard < body.IndexOf("new AkronToast", StringComparison.Ordinal));
        Assert.Contains(grab ? ", true)) return;" : ", false)) return;", body);
    }
}
