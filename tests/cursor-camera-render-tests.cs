using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class CursorCameraRenderTests {
    private static Vector2 V(float x, float y) { Vector2 v = default; v.X = x; v.Y = y; return v; }
    [Fact]
    public void CursorTravelMatchesNativeFocusWithoutExtraZoomFactor() {
        Vector2 offset = AkronCursorCameraRenderScope.Offset(V(213.33333f, 60f), 1.5f);
        Assert.Equal(53.33333f, offset.X, 3);
        Assert.Equal(-30f, offset.Y);
        Vector2 screen = AkronCursorCameraRenderScope.Project(V(0, 0), offset, 1.5f);
        Assert.Equal(-80f, screen.X, 3);
        Assert.Equal(45f, screen.Y);
    }
    [Theory]
    [InlineData(1f)] [InlineData(.5f)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void NoOffsetAtUnityZoomOutOrInvalidScale(float zoom) {
        Vector2 result = AkronCursorCameraRenderScope.Offset(V(240, 30), zoom);
        Assert.Equal(0f, result.X); Assert.Equal(0f, result.Y);
    }
    [Fact]
    public void CenterPreservesAuthoredCameraAndProjectionRoundTrips() {
        Vector2 offset = AkronCursorCameraRenderScope.Offset(V(160, 90), 1.5f);
        Assert.Equal(0f, offset.X); Assert.Equal(0f, offset.Y);
        Vector2 camera = V(100.25f, -19.5f), world = V(250, 37);
        Vector2 result = AkronCursorCameraRenderScope.Unproject(
            AkronCursorCameraRenderScope.Project(world, camera, 1.5f), camera, 1.5f);
        Assert.Equal(world.X, result.X, 3); Assert.Equal(world.Y, result.Y, 3);
    }
    [Fact]
    public void RepeatedRenderScopesRestoreWithoutAccumulation() {
        int camera = 10;
        for (int i = 0; i < 10; i++) {
            int original = camera;
            using (var scope = new AkronCursorCameraRenderScope(() => camera += 20, () => camera = original)) {
                Assert.Equal(30, camera);
            }
            Assert.Equal(10, camera);
        }
    }
    [Fact]
    public void FailureRestoresAndEarlyDisposeDoesNotOverwriteLaterUiActions() {
        int camera = 10;
        Assert.Throws<InvalidOperationException>((Action)(() => {
            using var scope = new AkronCursorCameraRenderScope(() => camera = 30, () => camera = 10);
            throw new InvalidOperationException();
        }));
        Assert.Equal(10, camera);
        var early = new AkronCursorCameraRenderScope(() => camera = 30, () => camera = 10);
        early.Dispose(); camera = 99; early.Dispose(); Assert.Equal(99, camera);
        Assert.Throws<InvalidOperationException>(() => new AkronCursorCameraRenderScope(
            () => { camera = 40; throw new InvalidOperationException(); }, () => camera = 99));
        Assert.Equal(99, camera);
    }
    [Fact]
    public void ProductionScopeExcludesScannerAndRestoresBeforeInteractiveUi() {
        string Read(string path) {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, path))) dir = dir.Parent;
            Assert.NotNull(dir); return File.ReadAllText(Path.Combine(dir!.FullName, path));
        }
        string runtime = Read("Source/Module/akron-module-visual-runtime.cs");
        Assert.Contains("AkronScreenshotScanner.IsScanning || !cursorZoomApplied", runtime);
        Assert.Contains("cursorZoomRenderCamera != null ||", runtime);
        Assert.Contains("Camera camera = level.Camera;", runtime);
        Assert.Contains("finally { cursorZoomRenderCamera = null; }", runtime);
        string module = Read("Source/Module/AkronModule.cs");
        int start = module.IndexOf("private static void EngineOnRenderCore", StringComparison.Ordinal);
        string body = module.Substring(start, module.IndexOf("private static ScreenWipe LevelOnCompleteArea", start, StringComparison.Ordinal) - start);
        Assert.True(body.IndexOf("RunAfterEngineUpdateActions();", StringComparison.Ordinal) < body.IndexOf("using IDisposable cursorRenderScope", StringComparison.Ordinal));
        Assert.True(body.IndexOf("cursorRenderScope?.Dispose();", StringComparison.Ordinal) < body.IndexOf("Overlay.RenderImGui()", StringComparison.Ordinal));
        Assert.Contains("BeginExtendedCursorRender(level)", Read("Source/Tools/akron-capture.cs"));
        Assert.Contains("TryGetExtendedCursorProjection", Read("Source/Tools/akron-screen-projection.cs"));
    }
}
