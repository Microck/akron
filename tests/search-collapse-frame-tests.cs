using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class SearchCollapseFrameTests
{
    [Theory]
    [InlineData("camera", "", true, false)]
    [InlineData("advance", "   ", true, false)]
    [InlineData("", "camera", false, true)]
    [InlineData("camera", "advance", true, true)]
    public void SearchEditsDoNotChangeCollapseModeUntilNextDraw(
        string initial, string edited, bool duringFrame, bool nextFrame)
    {
        AkronOverlay overlay = (AkronOverlay)RuntimeHelpers.GetUninitializedObject(typeof(AkronOverlay));
        overlay.SetSearchQuery(initial);
        overlay.BeginSearchCollapseFrame();
        overlay.ApplyImGuiSearchEdit("##akron_action_search", edited);
        Assert.Equal(duringFrame, ReadFrameMode(overlay));
        overlay.BeginSearchCollapseFrame();
        Assert.Equal(nextFrame, ReadFrameMode(overlay));
    }

    [Fact]
    public void FixedWindowsUseFrameSnapshotInsteadOfEditedQuery()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "../../../../Source/Overlay/akron-overlay-imgui-renderer.cs"));
        int start = source.IndexOf("private bool BeginFixedWindow(", StringComparison.Ordinal);
        int end = source.IndexOf("internal static float CurrentOverlayScale()", start, StringComparison.Ordinal);
        string window = source[start..end];
        Assert.Contains("bool searching = searchActiveForImGuiDraw;", window);
        Assert.DoesNotContain("IsNullOrWhiteSpace(searchQuery)", window);
        Assert.Contains("BeginSearchCollapseFrame();", source[..start]);
        Assert.Contains("searchExpandedWindowsLastFrame && !searchActiveForImGuiDraw", source);
    }

    private static bool ReadFrameMode(AkronOverlay overlay) =>
        (bool)typeof(AkronOverlay).GetField("searchActiveForImGuiDraw", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(overlay)!;
}
