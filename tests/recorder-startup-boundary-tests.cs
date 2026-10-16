using System;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class RecorderStartupBoundaryTests {
    private static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Recorder", file));

    [Fact]
    public void ManualPreparationAndLaunchShareFailureBoundary() {
        string source = Read("akron-internal-recorder.cs");
        int a = source.IndexOf("public static void Start(Level level)", StringComparison.Ordinal);
        int b = source.IndexOf("public static void Stop(", a, StringComparison.Ordinal);
        string method = source.Substring(a, b - a);
        int guard = method.IndexOf("try {", StringComparison.Ordinal);
        int path = method.IndexOf("BuildOutputPath(", StringComparison.Ordinal);
        int mkdir = method.IndexOf("Directory.CreateDirectory(outputDirectory)", StringComparison.Ordinal);
        int reserve = method.IndexOf("AkronEncoderCompletion.ReserveOutputPath", StringComparison.Ordinal);
        int launch = method.IndexOf("Process.Start(startInfo)", StringComparison.Ordinal);
        int handler = method.LastIndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(guard >= 0 && path > guard && mkdir > path && reserve > mkdir && launch > reserve && handler > launch);
        Assert.Contains("lastError = ex.Message;", method.Substring(handler));
        Assert.Contains("AkronEncoderCompletion.FailureToast(lastError)", method.Substring(handler));
    }

    [Fact]
    public void ReplayPreparationFailureDisarmsAndGuardsCleanupWithoutStoppingManual() {
        string source = Read("akron-internal-recorder-replay.cs");
        int a = source.IndexOf("private static void EnsureReplayBuffer(", StringComparison.Ordinal);
        int b = source.IndexOf("private static AkronRecordedAudio StopReplayBuffer", a, StringComparison.Ordinal);
        string method = source.Substring(a, b - a);
        int guard = method.IndexOf("try {", StringComparison.Ordinal);
        int mkdir = method.IndexOf("Directory.CreateDirectory(directory)", StringComparison.Ordinal);
        int handler = method.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(guard >= 0 && mkdir > guard && handler > mkdir);
        string failure = method.Substring(handler);
        Assert.Contains("manualReplayBufferEnabled = false;", failure);
        Assert.Contains("DisarmReplayBufferAutoStart(settings);", failure);
        Assert.Contains("catch (Exception cleanupError)", failure);
        Assert.Contains("lastError = failure;", failure);
        Assert.Contains("StopReplayBuffer(false, out cleanupFailure);", failure);
        Assert.Contains("StopReplayBuffer(false, out replacementCleanupError);", method);
        Assert.DoesNotContain("Stop(false)", failure);
        Assert.Contains("throw new IOException(\"FFmpeg replay buffer did not start.\")", method);
    }
    [Fact]
    public void CompletionArmingReportsSuccessOnlyAfterSuccessfulImmediateStart() {
        string source = Read("akron-internal-recorder-replay.cs");
        int a = source.IndexOf("public static void ArmCompletionCapture(", StringComparison.Ordinal);
        int b = source.IndexOf("private static void DisarmReplayBufferAutoStart", a, StringComparison.Ordinal);
        string method = source.Substring(a, b - a);
        int attempt = method.IndexOf("EnsureReplayBuffer(scene, settings", StringComparison.Ordinal);
        int guard = method.IndexOf("if (!IsReplayBuffering) return;", StringComparison.Ordinal);
        int toast = method.IndexOf("Completion capture armed.", StringComparison.Ordinal);
        Assert.True(attempt >= 0 && guard > attempt && toast > guard);
        Assert.Contains("if (scene != null)", method);
    }

    [Theory]
    [InlineData("", "", "", "mkdir")]
    [InlineData("flush", "", "", "mkdir | Replay cleanup: flush")]
    [InlineData("flush", "wait", "audio", "mkdir | Replay cleanup: flush | Replay cleanup: wait | Replay cleanup: audio")]
    public void StartupFailureRetainsCapturedAndThrownCleanupDiagnostics(string prior, string cleanup, string thrown, string expected) {
        Assert.Equal(expected, AkronInternalRecorder.CombineReplayStartupErrors("mkdir", prior, cleanup, thrown));
    }

}
