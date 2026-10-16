using System;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class SectionNavigationTests
{
    [Theory]
    [InlineData(400f, 540f, 38f, 0f)]
    [InlineData(540f, 540f, 38f, 0f)]
    [InlineData(600f, 540f, 38f, 98f)]
    [InlineData(1200f, 360f, 38f, 878f)]
    [InlineData(10f, 0f, 38f, 0f)]
    public void OverflowReservesReachableNavigationStrip(float bottom, float height, float bar, float expected)
    {
        Assert.Equal(expected, AkronOverlay.CalculateSectionScrollMaximum(bottom, height, bar));
    }

    [Theory]
    [InlineData(-10f, 90f, 0f)]
    [InlineData(45f, 90f, 45f)]
    [InlineData(200f, 90f, 90f)]
    [InlineData(200f, 0f, 0f)]
    [InlineData(float.NaN, 90f, 0f)]
    public void ResizeAndCollapseClampScroll(float offset, float maximum, float expected)
    {
        Assert.Equal(expected, AkronOverlay.ClampSectionScroll(offset, maximum));
    }

    [Fact]
    public void LastCollapsedSectionRemainsReachableAtBottom()
    {
        float header = AkronOverlay.GetSectionMinimumVisibleHeight(true, 24f);
        float maximum = AkronOverlay.CalculateSectionScrollMaximum(900f, 360f, 38f);
        float sectionTop = 900f - header - 4f;
        Assert.True(sectionTop - maximum + header <= 360f - 38f);
        Assert.True(AkronOverlay.GetSectionMinimumVisibleHeight(false, 24f) > header);
    }

    [Fact]
    public void TranslationKeepsPlannedCapacityAndExcludesNavigationStrip()
    {
        string source = Read("akron-overlay-imgui-renderer.cs");
        Assert.Contains("windows.Add((tabName, x, y, height, needsScrolling, entries, tabIndex))", source);
        Assert.Contains("window.Y - sectionScrollOffset", source);
        Assert.Contains("Math.Min(window.Height, contentViewportHeight - translatedY)", source);
        Assert.Contains("window.Scroll || renderedHeight < window.Height", source);
        Assert.Contains("pendingImGuiOptionsPopupEntry == null", source);
        Assert.Contains("if (hadScrollContext)", source);
        Assert.Contains("ResetSectionNavigation();", Read("AkronOverlay.cs"));
        Assert.DoesNotContain("CalculateVisibleActionRows(y, displayHeight)", source);
    }

    [Fact]
    public void OuterNavigationDoesNotCompeteWithWheelOrModalOwnership()
    {
        string source = Read("akron-overlay-section-navigation.cs");
        Assert.Contains("IsBackgroundActionRowInputBlocked()", source);
        Assert.Contains("HasActiveBindingCapture", source);
        Assert.Contains("ImGui.BeginDisabled(blocked)", source);
        Assert.DoesNotContain("MouseWheel", source);
        Assert.DoesNotContain("actionScrollIndex", source);
        Assert.DoesNotContain("sectionScrollOffset", Read("akron-overlay-layout.cs"));
    }

    private static string Read(string filename) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "../../../../Source/Overlay", filename));
}
