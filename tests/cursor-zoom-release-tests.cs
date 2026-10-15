using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class CursorZoomReleaseTests {
    // CI uses reference-only FNA: constructors/getters have stripped method bodies.
    // Seed/read its retained value storage, keeping these lease tests executable in
    // both CI and native builds without weakening the distinct viewport assertions.
    private static readonly FieldInfo ViewportStorage = typeof(Viewport).GetField(
        "viewport", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static Viewport FixtureViewport(int width, int height) {
        object value = default(Viewport);
        object storage = Activator.CreateInstance(ViewportStorage.FieldType)!;
        ViewportStorage.FieldType.GetField("w", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(storage, width);
        ViewportStorage.FieldType.GetField("h", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(storage, height);
        ViewportStorage.SetValue(value, storage);
        return (Viewport)value;
    }
    private static int FixtureWidth(Viewport value) => (int)ViewportStorage.FieldType.GetField(
        "w", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(ViewportStorage.GetValue(value))!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoreOriginalViewportBeforeProviderNativeReset(bool activeProvider) {
        var lease = new AkronCameraZoomLease();
        var scene = new object();
        var original = FixtureViewport(320, 180);
        var expanded = FixtureViewport(640, 360);
        var current = expanded;
        var order = new List<string>();
        lease.Capture(scene, original);
        lease.Capture(scene, expanded); // subsequent frames must not replace the baseline
        Assert.True(lease.TryRelease(scene, value => { current = value; order.Add("viewport"); },
            () => { Assert.Equal(320, FixtureWidth(current)); order.Add("reset"); if (activeProvider) current = FixtureViewport(480, 270); },
            () => { order.Add("automatic"); return true; }));
        Assert.Equal(new[] { "viewport", "reset", "automatic" }, order);
        Assert.Equal(activeProvider ? 480 : 320, FixtureWidth(current));
        Assert.False(lease.HasOwner);
        Assert.True(lease.TryRelease(scene, _ => throw new Exception(), () => throw new Exception(), () => throw new Exception()));
    }

    [Fact]
    public void DifferentSceneNeverReceivesOldCameraState() {
        var lease = new AkronCameraZoomLease();
        lease.Capture(new object(), FixtureViewport(320, 180));
        int automatic = 0;
        Assert.True(lease.TryRelease(new object(), _ => throw new Exception(), () => throw new Exception(), () => { automatic++; return true; }));
        Assert.Equal(1, automatic);
        Assert.False(lease.HasOwner);
    }

    [Fact]
    public void FailedAutomaticRestoreKeepsOwnershipWithoutRepeatingCameraReset() {
        var lease = new AkronCameraZoomLease(); var scene = new object(); int resets = 0;
        lease.Capture(scene, FixtureViewport(320, 180));
        Assert.False(lease.TryRelease(scene, _ => { }, () => resets++, () => false));
        Assert.True(lease.HasOwner);
        Assert.True(lease.TryRelease(scene, _ => throw new Exception(), () => throw new Exception(), () => true));
        Assert.Equal(1, resets);
    }

    [Fact]
    public void FailedResetKeepsOriginalViewportForRetry() {
        var lease = new AkronCameraZoomLease(); var scene = new object(); int width = 0;
        lease.Capture(scene, FixtureViewport(320, 180));
        Assert.Throws<InvalidOperationException>(() => lease.TryRelease(scene, v => width = FixtureWidth(v),
            () => throw new InvalidOperationException(), () => true));
        Assert.True(lease.HasOwner);
        Assert.True(lease.TryRelease(scene, v => width = FixtureWidth(v), () => { }, () => true));
        Assert.Equal(320, width);
    }

    [Fact]
    public void NewForceAfterPartialReleaseNeedsAnotherResetButRetainsBaseline() {
        var lease = new AkronCameraZoomLease(); var scene = new object(); int resets = 0;
        lease.Capture(scene, FixtureViewport(320, 180));
        Assert.False(lease.TryRelease(scene, _ => { }, () => resets++, () => false));
        lease.Capture(scene, FixtureViewport(640, 360));
        Assert.True(lease.TryRelease(scene, v => Assert.Equal(320, FixtureWidth(v)), () => resets++, () => true));
        Assert.Equal(2, resets);
    }

    [Fact]
    public void CaptureRejectsUnreleasedDifferentOwner() {
        var lease = new AkronCameraZoomLease(); lease.Capture(new object(), default);
        Assert.Throws<InvalidOperationException>(() => lease.Capture(new object(), default));
    }
    [Fact]
    public void NativeHandoffRetainsLeaseAndSuppressesEveryFrameWithoutRepeatedReset() {
        var lease = new AkronCameraZoomLease(); var scene = new object(); int resets = 0, suppressions = 0;
        lease.Capture(scene, FixtureViewport(320, 180));
        for (int i = 0; i < 3; i++) {
            Assert.True(lease.TryPrepareNative(scene, v => Assert.Equal(320, FixtureWidth(v)), () => resets++, () => { suppressions++; return true; }));
        }
        Assert.True(lease.IsOwner(scene)); Assert.Equal(1, resets); Assert.Equal(3, suppressions);
        Assert.True(lease.TryRelease(scene, v => Assert.Equal(320, FixtureWidth(v)), () => resets++, () => true));
        Assert.Equal(2, resets);
    }

    [Fact]
    public void NativeToForeignToNativeRetainsOriginalViewport() {
        var lease = new AkronCameraZoomLease(); var scene = new object(); int resets = 0;
        lease.Capture(scene, FixtureViewport(320, 180));
        Assert.True(lease.TryPrepareNative(scene, _ => { }, () => resets++, () => true));
        lease.Capture(scene, FixtureViewport(640, 360));
        Assert.True(lease.TryPrepareNative(scene, v => Assert.Equal(320, FixtureWidth(v)), () => resets++, () => true));
        Assert.Equal(2, resets);
        Assert.True(lease.TryRelease(scene, v => Assert.Equal(320, FixtureWidth(v)), () => { }, () => true));
    }

    [Fact]
    public void FailedSuppressionRetainsCleanupAndDoesNotPrepareNative() {
        var lease = new AkronCameraZoomLease(); var scene = new object(); int resets = 0;
        lease.Capture(scene, FixtureViewport(320, 180));
        Assert.False(lease.TryPrepareNative(scene, _ => { }, () => resets++, () => false));
        Assert.True(lease.HasOwner);
        Assert.True(lease.TryRelease(scene, _ => { }, () => resets++, () => true));
        Assert.Equal(2, resets);
    }

    [Fact]
    public void NativePreparationRejectsAnotherSceneWithoutCallbacks() {
        var lease = new AkronCameraZoomLease(); lease.Capture(new object(), default);
        Assert.False(lease.TryPrepareNative(new object(), _ => throw new Exception(), () => throw new Exception(), () => throw new Exception()));
        Assert.True(lease.HasOwner);
    }

    [Fact]
    public void NativeWiringSuppressesAutomaticModeBeforeWritingZoom() {
        string source = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory,
            "../../../../Source/Module/akron-module-visual-runtime.cs"));
        int start = source.IndexOf("internal static void ApplyCursorZoomFrame", StringComparison.Ordinal);
        int prepare = source.IndexOf("cursorZoomLease.TryPrepareNative", start, StringComparison.Ordinal);
        int suppress = source.IndexOf("TrySetExtendedCameraAutomaticZooming(false)", prepare, StringComparison.Ordinal);
        int write = source.IndexOf("level.Zoom = zoom;", start, StringComparison.Ordinal);
        Assert.True(prepare > start && suppress > prepare && write > suppress);
        Assert.Contains("cursorZoomLease.HasOwner || AkronInterop.AreExtendedCameraHooksActive()", source);
        Assert.Contains("cursorZoomOwnedByExtendedCamera = cursorZoomLease.HasOwner;", source);
    }

    [Fact]
    public void FailedPreparationAfterPendingReleaseInvalidatesCompletedReset() {
        var lease = new AkronCameraZoomLease(); var scene = new object(); int cleanupResets = 0;
        lease.Capture(scene, FixtureViewport(320, 180));
        Assert.False(lease.TryRelease(scene, _ => { }, () => cleanupResets++, () => false));
        Assert.Throws<InvalidOperationException>(() => lease.TryPrepareNative(scene, _ => { },
            () => throw new InvalidOperationException(), () => true));
        Assert.True(lease.TryRelease(scene, _ => { }, () => cleanupResets++, () => true));
        Assert.Equal(2, cleanupResets);
    }

}
