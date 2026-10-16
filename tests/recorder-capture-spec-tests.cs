using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class RecorderCaptureSpecTests {
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    public void DiagnosticsRetainLastIdleSinkAndPreferActiveManual(bool manual, bool replay, bool lastReplay, bool expected) {
        Assert.Equal(expected, AkronInternalRecorder.ShouldUseReplayDiagnostics(manual, replay, lastReplay));
    }

    [Fact]
    public void ConcurrentCadencesKeepTheirLaunchRatesAndDeadlines() {
        var manual = new AkronCaptureCadence(30, 10);
        var replay = new AkronCaptureCadence(15, 10);
        Assert.True(manual.TrySchedule(10, false, out _));
        Assert.True(replay.TrySchedule(10, false, out _));
        Assert.True(manual.TrySchedule(10 + 1d / 30, true, out _));
        Assert.False(replay.TrySchedule(10 + 1d / 30, false, out _));
        // Starting/replacing another sink at a later clock offset has no effect on manual.
        replay = new AkronCaptureCadence(60, 10.04);
        Assert.True(replay.TrySchedule(10.04, false, out long missed));
        Assert.Equal(0, missed);
        Assert.False(manual.TrySchedule(10.04, true, out _));
        Assert.True(manual.TrySchedule(10 + 2d / 30, true, out _));
    }

    [Fact]
    public void NewCadenceDoesNotCountPreLaunchTimeAsDroppedFrames() {
        var cadence = new AkronCaptureCadence(30, 500);
        Assert.True(cadence.TrySchedule(500, false, out long missed));
        Assert.Equal(0, missed);
        Assert.False(cadence.TrySchedule(500.001, true, out missed));
        Assert.Equal(0, missed);
        Assert.True(cadence.TrySchedule(501, true, out missed));
        Assert.True(missed > 0);
    }

    [Fact]
    public void EncoderSpecsAreImmutableSnapshotsRatherThanLiveSettings() {
        Type type = typeof(AkronInternalRecorder).GetNestedType("RecorderSpec", BindingFlags.NonPublic)!;
        MethodInfo factory = type.GetMethod("FromSettings", BindingFlags.Public | BindingFlags.Static)!;
        var settings = new AkronModuleSettings { RecordingResolutionX = 320, RecordingResolutionY = 180, RecordingFramerate = 30 };
        object launched = factory.Invoke(null, new object[] { settings })!;
        settings.RecordingResolutionX = 640;
        settings.RecordingResolutionY = 180;
        settings.RecordingFramerate = 15;
        object next = factory.Invoke(null, new object[] { settings })!;
        Assert.Equal(320, type.GetProperty("Width")!.GetValue(launched));
        Assert.Equal(180, type.GetProperty("Height")!.GetValue(launched));
        Assert.Equal(30, type.GetProperty("Framerate")!.GetValue(launched));
        Assert.Equal(640, type.GetProperty("Width")!.GetValue(next));
        Assert.Equal(15, type.GetProperty("Framerate")!.GetValue(next));
    }

    [Fact]
    public void CaptureUsesIndependentSpecsAndWritesBeforeBufferReuse() {
        string root = Path.Combine(AppContext.BaseDirectory, "../../../../Source/Recorder");
        string manual = File.ReadAllText(Path.Combine(root, "akron-internal-recorder.cs"));
        string replay = File.ReadAllText(Path.Combine(root, "akron-internal-recorder-replay.cs"));
        Assert.Contains("recordingSpec = manualSpec;", manual);
        Assert.Contains("replayFrameSpec = replaySpec;", manual);
        Assert.Contains("recordingSpec.Width, recordingSpec.Height", manual);
        Assert.Contains("replayFrameSpec.Width, replayFrameSpec.Height", manual);
        int manualWrite = manual.IndexOf("recordingInput.Write(frame", StringComparison.Ordinal);
        int replayBuild = manual.IndexOf("byte[] replayFrame = BuildFrameBytes", StringComparison.Ordinal);
        Assert.True(manualWrite >= 0 && replayBuild > manualWrite);
        Assert.Contains("Viewport viewport = Engine.Viewport", manual);
        Assert.Contains("if (manualDue) capturedFrames++;", manual);
        Assert.Contains("manualCadence.TrySchedule", manual);
        Assert.Contains("replayCadence.TrySchedule(now, replayCapturedFrames > 0, out missedReplayFrames)", manual);
        Assert.Contains("replayDroppedFrames += missedReplayFrames", manual);
        Assert.Contains("replayCapturedFrames += activeReplayTimeline.WriteSample", manual);
        Assert.Contains("!replayWarningShown", manual);
        Assert.Contains("replayCapturedFrames = 0", replay);
        Assert.Contains("replayDroppedFrames = 0", replay);
        Assert.Contains("replaySpec.Equals(spec)", replay);
        Assert.DoesNotContain("activeSpec", manual + replay);
        Assert.DoesNotContain("capturedFrames =", replay);
        Assert.DoesNotContain("manualCadence =", replay);
        Assert.Contains("if (process != null) lastCaptureWasReplay = false;", manual);
        Assert.Contains("if (process != null && ffmpegProcess == null) lastCaptureWasReplay = true;", replay);
        Assert.Contains("if (ffmpegProcess == null) lastCaptureWasReplay = true;", replay);
    }
}
