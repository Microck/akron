using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class SearchLayoutHeightTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StackedHeightMatchesEffectiveCollapseWithoutChangingPreference(bool savedCollapsed, bool searching)
    {
        AkronOverlay overlay = NewOverlay(savedCollapsed);
        float height = Height(overlay, searching);
        Assert.Equal(savedCollapsed && !searching ? HeaderHeight() : 134f, height);
        Assert.Equal(savedCollapsed, Collapsed(overlay).Contains("Creator"));
    }

    [Theory]
    [InlineData("next", "", true, false)]
    [InlineData("", "next", false, true)]
    [InlineData("next", "   ", true, false)]
    public void MidDrawSearchEditKeepsStackingInCurrentFrameMode(string initial, string edited, bool current, bool next)
    {
        AkronOverlay overlay = NewOverlay(true);
        overlay.SetSearchQuery(initial);
        overlay.BeginSearchCollapseFrame();
        overlay.ApplyImGuiSearchEdit("##akron_action_search", edited);
        Assert.Equal(current ? 134f : HeaderHeight(), Height(overlay, FrameMode(overlay)));
        overlay.BeginSearchCollapseFrame();
        Assert.Equal(next ? 134f : HeaderHeight(), Height(overlay, FrameMode(overlay)));
        Assert.Contains("Creator", Collapsed(overlay));
    }

    [Fact]
    public void DrawAndFallbackPassTheirOwnSnapshotThroughPlacementAndStacking()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay");
        string draw = File.ReadAllText(Path.Combine(root, "akron-overlay-imgui-renderer.cs"));
        string layout = File.ReadAllText(Path.Combine(root, "akron-overlay-layout.cs"));
        Assert.Contains("menuTop, displayHeight, searchActiveForImGuiDraw)", draw);
        Assert.Contains("GetStackedActionSectionHeight(tabName, height, searchActiveForImGuiDraw)", draw);
        Assert.Contains("if (searchActiveForImGuiDraw && entries.Count == 0)", draw);
        Assert.Contains("bool searching = !string.IsNullOrWhiteSpace(searchQuery);", layout);
        Assert.Contains("menuTop, ScreenHeight, searching)", layout);
        Assert.Contains("CreateSectionLayout(tabName, x, y, menuColumnWidth, height, searching)", layout);
        Assert.DoesNotContain("searchActiveForImGuiDraw", layout);
        Assert.Contains("bool collapsed = IsSectionCollapsedForLayout(title, searching);", layout);
    }

    private static AkronOverlay NewOverlay(bool collapsed)
    {
        AkronOverlay overlay = (AkronOverlay)RuntimeHelpers.GetUninitializedObject(typeof(AkronOverlay));
        typeof(AkronOverlay).GetField("collapsedWindowTitles", PrivateInstance)!.SetValue(overlay,
            new HashSet<string>(collapsed ? new[] { "Creator" } : Array.Empty<string>(), StringComparer.OrdinalIgnoreCase));
        return overlay;
    }
    private static HashSet<string> Collapsed(AkronOverlay overlay) =>
        (HashSet<string>)typeof(AkronOverlay).GetField("collapsedWindowTitles", PrivateInstance)!.GetValue(overlay)!;
    private static bool FrameMode(AkronOverlay overlay) =>
        (bool)typeof(AkronOverlay).GetField("searchActiveForImGuiDraw", PrivateInstance)!.GetValue(overlay)!;
    private static float Height(AkronOverlay overlay, bool searching) =>
        (float)typeof(AkronOverlay).GetMethod("GetStackedActionSectionHeight", PrivateInstance)!.Invoke(overlay, new object[] { "Creator", 134f, searching })!;
    private static float HeaderHeight() =>
        (float)typeof(AkronOverlay).GetField("HeaderHeight", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
}
