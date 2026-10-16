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
}
