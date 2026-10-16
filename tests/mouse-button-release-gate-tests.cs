using System;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class MouseButtonReleaseGateTests {
    [Fact]
    public void OrdinaryInputPassesThroughWithoutSuppression() {
        var gate = new AkronMouseButtonReleaseGate();
        Assert.False(gate.Filter(false));
        Assert.True(gate.Filter(true));
        Assert.False(gate.Filter(false));
    }

    [Fact]
    public void CancellationIsSuppressedUntilReleaseThenFreshPressWorks() {
        var gate = new AkronMouseButtonReleaseGate();
        gate.SuppressUntilRelease();
        for (int frame = 0; frame < 120; frame++) Assert.False(gate.Filter(true));
        Assert.False(gate.Filter(false));
        Assert.True(gate.Filter(true));
    }

    [Fact]
    public void ReleaseBeforeNextRenderDoesNotEatFreshPress() {
        var gate = new AkronMouseButtonReleaseGate();
        gate.SuppressUntilRelease();
        Assert.False(gate.Filter(false));
        Assert.True(gate.Filter(true));
        gate.SuppressUntilRelease();
        Assert.False(gate.Filter(true));
        Assert.False(gate.Filter(false));
        Assert.True(gate.Filter(true));
    }

    [Fact]
    public void ActivePlacementNeverSubmitsRightDownBeforeCancellationUpdate() {
        var gate = new AkronMouseButtonReleaseGate();
        // Render samples a down before the next game update cancels placement.
        gate.SuppressUntilRelease();
        Assert.False(gate.Filter(true));
        // The update then restores the overlay while the same button remains held.
        gate.SuppressUntilRelease();
        Assert.False(gate.Filter(true));
        Assert.False(gate.Filter(false));
        Assert.True(gate.Filter(true));
        string overlay = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/AkronOverlay.cs"));
        int cleanup = overlay.IndexOf("bool cleanupRendered = AkronImGuiRenderer.Render(", StringComparison.Ordinal);
        int render = overlay.IndexOf("AkronImGuiRenderer.Render(DrawStartPosPlacementEditor)", StringComparison.Ordinal);
        int arm = overlay.IndexOf("if (startPosPlacementActive) AkronImGuiRenderer.SuppressRightMouseUntilRelease();", StringComparison.Ordinal);
        Assert.True(arm >= 0 && cleanup > arm && render > cleanup);
    }

    [Fact]
    public void NativeCancellationArmsBeforeRestoreAndRendererFiltersRawRightButton() {
        string root = Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay");
        string placement = File.ReadAllText(Path.Combine(root, "akron-overlay-placement.cs"));
        int arm = placement.IndexOf("if (mouse.RightButton == ButtonState.Pressed) AkronImGuiRenderer.SuppressRightMouseUntilRelease();", StringComparison.Ordinal);
        Assert.True(arm >= 0);
        Assert.True(placement.IndexOf("EndStartPosPlacement(true);", arm, StringComparison.Ordinal) > arm);
        string renderer = File.ReadAllText(Path.Combine(root, "akron-imgui-renderer.cs"));
        Assert.Contains("rightMouseReleaseGate.Filter(mouse.RightButton == ButtonState.Pressed)", renderer);
        Assert.Contains("io.AddMouseButtonEvent(1, acceptsInput && rightDown);", renderer);
        Assert.Contains("io.AddMouseButtonEvent(0, acceptsInput && mouse.LeftButton == ButtonState.Pressed);", renderer);
    }
}
