using System;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class StartPosPlacementCameraTests {
    private static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source", file));

    [Fact]
    public void PlacementPansAfterCancellationChecksAfterPanelBeforeWorldConversion() {
        string source = Read("Overlay/akron-overlay-placement.cs");
        int start = source.IndexOf("private bool UpdateStartPosPlacement()", StringComparison.Ordinal);
        int end = source.IndexOf("private bool UpdatePracticeAreaSelection", start, StringComparison.Ordinal);
        string method = source.Substring(start, end - start);
        int cancel = method.IndexOf("StartPos placement closed.", StringComparison.Ordinal);
        int pan = method.IndexOf("AkronRuntimeOptions.ApplyFreeCameraForStartPosPlacement(placementLevel);", StringComparison.Ordinal);
        int panel = method.IndexOf("if (IsMouseInsideStartPosPlacementPanel(mouse))", StringComparison.Ordinal);
        int world = method.IndexOf("AkronScreenProjection.MouseScreenToWorld", StringComparison.Ordinal);
        Assert.True(cancel >= 0 && panel > cancel && pan > panel && world > pan);
        Assert.Contains("SearchOwnsGameplayInputThisFrame = true;", method);
        Assert.Contains("Engine.Scene is Level placementLevel && AkronPolicy.CanUse(AkronFeatureKind.Freeze).Allowed)", method);
        Assert.Contains("!AkronImGuiRenderer.WantCaptureKeyboard && !AkronImGuiRenderer.WantTextInput", method);
        Assert.Contains("!AkronImGuiRenderer.WantCaptureMouse", method);
        Assert.Contains("if (!CanUseStartPosPlacementEditor())", method);
        Assert.DoesNotContain("ApplyEnabledRuntimeFeatures", method);
    }

    [Fact]
    public void PlacementCameraUsesOrdinaryPolicyClampingAndControlPathOnly() {
        string source = Read("Runtime/akron-runtime-options.cs");
        int start = source.IndexOf("internal static void ApplyFreeCameraForStartPosPlacement", StringComparison.Ordinal);
        int end = source.IndexOf("private static void ApplyFreeCamera(", start, StringComparison.Ordinal);
        string method = source.Substring(start, end - start);
        Assert.Contains("MoveFreeCamera(level);", method);
        Assert.DoesNotContain("LockFreeCameraPlayerControl", method);
        Assert.DoesNotContain("RestoreFreeCameraPlayerControl", method);
        Assert.Contains("AkronModule.IsFreeCameraEffectiveEnabled()", method);
        Assert.Contains("AkronModule.TryUseRuntime(AkronFeatureKind.FreeCamera)", method);
        Assert.DoesNotContain("ApplyAudio", method);
        Assert.DoesNotContain("level.Update", method);
        Assert.Contains("AkronModule.TryUseRuntime(AkronFeatureKind.FreeCamera)", source);
        Assert.Contains("level.Camera.Position = ClampCameraToRoom", source);
    }

    [Fact]
    public void PlacementStillRestoresPriorFreezeAndFreeCameraState() {
        string source = Read("Overlay/akron-overlay-placement.cs");
        int start = source.IndexOf("private void EndStartPosPlacement(", StringComparison.Ordinal);
        int end = source.IndexOf("private void EndAutoKillAreaSelection", start, StringComparison.Ordinal);
        string method = source.Substring(start, end - start);
        Assert.Contains("AkronModule.Settings.FreeCamera = startPosPlacementPreviousFreeCamera;", method);
        Assert.Contains("AkronModule.Settings.FreeCameraFreezeGameplay = startPosPlacementPreviousFreeCameraFreeze;", method);
        Assert.Contains("AkronModule.Session.FreezeGameplay = startPosPlacementPreviousFreeze;", method);
    }
}
