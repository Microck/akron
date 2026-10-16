using System;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class OverlayEnsureVisibleTests {
    [Fact]
    public void EnsuringAnAlreadyVisibleOverlayPreservesItsTransientSelection() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/akron-module-overlay-input.cs"));
        int start = source.IndexOf("public static AkronOverlay GetOverlay(", StringComparison.Ordinal);
        int end = source.IndexOf("private static void UpdateOverlayCursorState", start, StringComparison.Ordinal);
        string method = source.Substring(start, end - start);
        Assert.Contains("bool wasVisible = Overlay.Visible;", method);
        Assert.Contains("if (!wasVisible) {\n                Overlay.ResetTransientUiState();\n            }", method);
        Assert.Contains("Overlay.PrewarmLayout(scene as Level);", method);
        Assert.Contains("Overlay.Active = Overlay.Visible && Settings.ConsumeGameplayInputInMenu;", method);
        Assert.Contains("UpdateOverlayCursorState();", method);
        Assert.Equal(1, method.Split("Overlay.ResetTransientUiState();").Length - 1);
    }
    [Theory]
    [InlineData("public bool SelectAction(", "public bool OpenActionOptions(", "selectedTabIndex = tabIndex;")]
    [InlineData("private bool SelectActionOptionsPopup(", "public bool EndStartPosPlacementForLoad(", "selectedTabIndex = tab;")]
    public void UnfilteredFallbackClearsSearchBeforeAssigningItsIndex(string startSignature, string endSignature, string assignment) {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/AkronOverlay.cs"));
        int start = source.IndexOf(startSignature, StringComparison.Ordinal);
        int end = source.IndexOf(endSignature, start, StringComparison.Ordinal);
        string method = source.Substring(start, end - start);
        int clear = method.IndexOf("ClearSearchQuery();", StringComparison.Ordinal);
        int select = method.IndexOf(assignment, StringComparison.Ordinal);
        Assert.True(clear >= 0 && clear < select);
        if (startSignature.Contains("SelectAction(")) {
            Assert.True(clear > method.IndexOf("if (TryFindAction(", StringComparison.Ordinal));
            Assert.True(clear > method.IndexOf("if (filteredIndex >= 0)", StringComparison.Ordinal));
        }
    }

}
