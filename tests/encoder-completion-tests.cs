using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class EncoderCompletionTests {
    [Theory]
    [InlineData(0, true, "", true)]
    [InlineData(0, true, "ordinary warning", true)]
    [InlineData(7, true, "", false)]
    [InlineData(7, false, "bad codec", false)]
    [InlineData(0, false, "", false)]
    public void RequiresZeroExitAndNonemptyOutput(int code, bool output, string diagnostic, bool success) {
        bool disposed = false;
        string error = AkronEncoderCompletion.Finish(new MemoryStream(), _ => true, () => code,
            () => throw new Exception("Unexpected kill"), () => disposed = true, () => diagnostic, () => output);
        Assert.Equal(success, error.Length == 0);
        Assert.True(disposed);
        if (code != 0) Assert.Contains("code 7", error);
        if (!success && diagnostic.Length > 0) Assert.Contains(diagnostic, error);
    }

    [Fact]
    public void TimeoutKillsAndNeverPublishesEvenIfPartialFileExists() {
        List<string> operations = new();
        string error = AkronEncoderCompletion.Finish(new MemoryStream(), ms => { operations.Add("wait" + ms); return false; },
            () => throw new Exception("Must not read killed exit"), () => operations.Add("kill"),
            () => operations.Add("dispose"), () => "", () => true);
        Assert.Contains("did not exit normally", error);
        Assert.Equal(new[] { "wait3000", "kill", "wait1000", "dispose" }, operations);
    }

    [Fact]
    public void BrokenPipeCannotSkipDisposalWaitOrKill() {
        ThrowingStream stream = new();
        bool waited = false, killed = false, disposed = false;
        string error = AkronEncoderCompletion.Finish(stream, _ => { waited = true; return false; }, () => 0,
            () => killed = true, () => disposed = true, () => "encoder rejection", () => true, "capture failed");
        Assert.True(stream.DisposeAttempted && waited && killed && disposed);
        Assert.Contains("flush failed", error);
        Assert.Contains("close failed", error);
        Assert.Contains("capture failed", error);
        Assert.StartsWith("encoder rejection", error);
    }

    [Fact]
    public void UninspectableExitAndOutputFailClosed() {
        string error = AkronEncoderCompletion.Finish(null!, _ => true, () => throw new IOException("exit unavailable"),
            () => { }, () => { }, () => "", () => throw new IOException("file unavailable"));
        Assert.Contains("exit unavailable", error);
        Assert.Contains("file unavailable", error);
    }

    [Fact]
    public void WaitExceptionStillAttemptsKillAndDispose() {
        bool killed = false, disposed = false;
        string error = AkronEncoderCompletion.Finish(null!, _ => throw new IOException("wait failed"), () => 0,
            () => killed = true, () => disposed = true, () => "", () => true);
        Assert.True(killed && disposed);
        Assert.Contains("wait failed", error);
    }

    [Fact]
    public void FileValidationRejectsAbsentAndEmptyButAcceptsNonempty() {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv");
        try {
            Assert.False(AkronEncoderCompletion.HasNonemptyOutput(path));
            File.WriteAllBytes(path, Array.Empty<byte>());
            Assert.False(AkronEncoderCompletion.HasNonemptyOutput(path));
            File.WriteAllBytes(path, new byte[] { 1 });
            Assert.True(AkronEncoderCompletion.HasNonemptyOutput(path));
        } finally { File.Delete(path); }
    }

    [Fact]
    public void DiagnosticsAreProcessOwnedAndBounded() {
        AkronEncoderDiagnostics first = new(), second = new();
        for (int i = 0; i < 20; i++) first.Receive("line-" + i + new string('x', 600));
        first.Receive(null!); second.Receive("second"); second.Receive(null!);
        string value = first.ReadAfterExit();
        Assert.DoesNotContain("line-0", value);
        Assert.Contains("line-19", value);
        Assert.True(value.Length <= 8 * 512 + 7 * 3);
        Assert.Equal("second", second.ReadAfterExit());
    }

    [Fact]
    public void FailureGatePrecedesPostProcessingAndPublicationWithAudioCleanup() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Recorder/akron-internal-recorder.cs"));
        string stop = source.Substring(source.IndexOf("private static void Stop(string"));
        stop = stop.Substring(0, stop.IndexOf("public static void BuildCompletionVideo"));
        int gate = stop.IndexOf("if (!string.IsNullOrWhiteSpace(completionError))");
        Assert.True(gate > 0);
        Assert.True(gate < stop.IndexOf("TryRemuxToMp4"));
        Assert.True(gate < stop.IndexOf("TryMuxAudioIntoVideo"));
        Assert.True(gate < stop.IndexOf("WriteClipSidecar"));
        Assert.Contains("return;", stop.Substring(gate, stop.IndexOf("lastError = string.Empty", gate) - gate));
        Assert.Contains("finally {\n            DeleteRecordedAudioFiles(recordedAudio);", stop);
        Assert.Contains("Stop(captureError);", source);
        Assert.Contains("string captureError = ex.Message;\n            StopReplayBuffer();", source);
    }

    [Fact]
    public void FailureToastKeepsVisiblePrefixAndBoundsDiagnostic() {
        string full = "encoder\r\n" + new string('x', 4000);
        string toast = AkronEncoderCompletion.FailureToast(full);
        Assert.StartsWith("Recording failed: encoder", toast);
        Assert.True(toast.Length <= 82);
        Assert.EndsWith("...", toast);
        Assert.DoesNotContain("\n", toast);
        Assert.DoesNotContain("\r", toast);
        Assert.Equal("Recording failed.", AkronEncoderCompletion.FailureToast(null!));
    }

    [Fact]
    public void FreshReservationsNeverOverwriteExistingVideoOrOrphanedSidecar() {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try {
            string requested = Path.Combine(directory, "clip.mkv");
            File.WriteAllText(requested, "previous successful video");
            File.WriteAllText(requested + ".akrclip", "previous metadata");
            File.WriteAllText(Path.Combine(directory, "clip-1.mkv.akrclip"), "orphaned metadata");
            string first = AkronEncoderCompletion.ReserveOutputPath(requested);
            string second = AkronEncoderCompletion.ReserveOutputPath(requested);
            Assert.Equal(Path.Combine(directory, "clip-2.mkv"), first);
            Assert.Equal(Path.Combine(directory, "clip-3.mkv"), second);
            Assert.Equal(0, new FileInfo(first).Length);
            Assert.Equal("previous successful video", File.ReadAllText(requested));
            Assert.Equal("previous metadata", File.ReadAllText(requested + ".akrclip"));
            Assert.Equal(Path.Combine(directory, "fresh.mp4"), AkronEncoderCompletion.ReserveOutputPath(Path.Combine(directory, "fresh.mp4")));
        } finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ManualAndOptionalOutputsUseFreshReservationsBeforeEncoderStart() {
        string root = Path.Combine(AppContext.BaseDirectory, "../../../../Source/Recorder");
        string manual = File.ReadAllText(Path.Combine(root, "akron-internal-recorder.cs"));
        int reservation = manual.IndexOf("ReserveOutputPath(outputPath)");
        Assert.True(reservation >= 0);
        Assert.True(reservation < manual.IndexOf("Process.Start(startInfo)"));
        string optional = File.ReadAllText(Path.Combine(root, "akron-internal-recorder-ffmpeg.cs"));
        Assert.Contains("outputPath = AkronEncoderCompletion.ReserveOutputPath(outputPath);", optional);
        Assert.Equal(2, optional.Split("outputPath = AkronEncoderCompletion.ReserveOutputPath(outputPath);").Length - 1);
    }

    [Fact]
    public void ReplayWriteAndPruningHaveIndependentFailureBoundary() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Recorder/akron-internal-recorder.cs"));
        int start = source.IndexOf("if (replayDue) {");
        Assert.True(start >= 0);
        int end = source.IndexOf("capturedFrames++;", start);
        Assert.True(end > start);
        string replay = source.Substring(start, end - start);
        Assert.Contains("try {\n                    byte[] replayFrame = BuildFrameBytes", replay);
        Assert.Contains("activeReplayInput.Write(replayFrame, 0, replayFrame.Length);", replay);
        Assert.Contains("PruneReplaySegments(settings);", replay);
        Assert.Contains("catch (Exception replayError)", replay);
        Assert.Contains("StopReplayBuffer();", replay);
        Assert.Contains("catch (Exception cleanupError)", replay);
        Assert.DoesNotContain("Stop(captureError)", replay);
        Assert.DoesNotContain("Stop();", replay);
        Assert.Contains("lastError = replayMessage;", replay);
    }

    private sealed class ThrowingStream : MemoryStream {
        public bool DisposeAttempted { get; private set; }
        public override void Flush() => throw new IOException("flush failed");
        protected override void Dispose(bool disposing) {
            DisposeAttempted = true;
            base.Dispose(disposing);
            throw new IOException("close failed");
        }
    }
}
