using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ReplayWindowTimingTests
{
    private static readonly DateTime Origin = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AbsoluteSlotsHoldPreviousImageAndCompleteEndExclusively()
    {
        var timeline = new AkronReplayTimeline(10, 1000);
        using var output = new MemoryStream();
        timeline.WriteSample(output, new byte[] { 1 }, 0, Origin);
        timeline.WriteSample(output, new byte[] { 2 }, 250, Origin.AddMilliseconds(250));
        timeline.WriteSample(output, new byte[] { 3 }, 850, Origin.AddMilliseconds(850));
        timeline.CompleteThrough(output, 1000);
        Assert.Equal(10, timeline.FramesWritten);
        Assert.Equal(new byte[] { 1, 1, 2, 2, 2, 2, 2, 2, 3, 3 }, output.ToArray());
        Assert.Equal(0, timeline.CompleteThrough(output, 1000));
    }

    [Fact]
    public void JitterDoesNotAccumulateDeadlineDrift()
    {
        var timeline = new AkronReplayTimeline(30, 1000000);
        using var output = new MemoryStream();
        for (long tick = 0; tick < 5000000; tick += 34789)
            timeline.WriteSample(output, new byte[] { 7 }, tick, Origin.AddTicks(tick * 10));
        timeline.CompleteThrough(output, 5000000);
        Assert.Equal(150, timeline.FramesWritten);
    }

    [Fact]
    public void EarlySampleCannotReplacePreviousImageInPastSlot()
    {
        var timeline = new AkronReplayTimeline(10, 1000);
        using var output = new MemoryStream();
        timeline.WriteSample(output, new byte[] { 1 }, 0, Origin);
        Assert.Equal(0, timeline.WriteSample(output, new byte[] { 9 }, 50, Origin.AddMilliseconds(50)));
        timeline.CompleteThrough(output, 200);
        Assert.Equal(new byte[] { 1, 1 }, output.ToArray());
    }

    [Fact]
    public void InvalidGeometryAndExcessiveGapDoNotWritePartialCatchup()
    {
        var timeline = new AkronReplayTimeline(30, 1000);
        using var output = new MemoryStream();
        timeline.WriteSample(output, new byte[] { 1 }, 0, Origin);
        Assert.Throws<InvalidDataException>(() => timeline.WriteSample(output, new byte[] { 1, 2 }, 100, Origin.AddMilliseconds(100)));
        Assert.Equal(1, output.Length);
        Assert.Throws<IOException>(() => timeline.CompleteThrough(output, 10000));
        Assert.Equal(1, output.Length);
    }

    [Fact]
    public void ClockDiscontinuityFailsInsteadOfInventingWindowTime()
    {
        var timeline = new AkronReplayTimeline(30, 1000);
        using var output = new MemoryStream();
        timeline.WriteSample(output, new byte[] { 1 }, 100, Origin);
        Assert.Throws<InvalidDataException>(() => timeline.WriteSample(output, new byte[] { 2 }, 99, Origin));
        Assert.Throws<IOException>(() => timeline.WriteSample(output, new byte[] { 2 }, 200, Origin.AddHours(1)));
        Assert.Equal(1, output.Length);
    }

    [Theory]
    [InlineData(5, 10, 150)]
    [InlineData(7, 9, 60)]
    [InlineData(8.01, 9.01, 30)]
    public void RequestedWindowIsFrameBoundedInsteadOfWholeSegments(double start, double end, long frames)
    {
        var segments = Enumerable.Range(0, 5).Select(i => new AkronReplaySegment($"segment-{i:000000}.mkv", i * 2, i * 2 + 2)).ToList();
        var window = AkronReplayWindows.Plan(segments, Origin, 30, 300, Origin.AddSeconds(start), Origin.AddSeconds(end));
        Assert.Equal(frames, window.FrameCount);
        Assert.InRange((window.StartUtc - Origin.AddSeconds(start)).TotalSeconds, -0.000001, 1d / 30);
        Assert.InRange((window.EndUtc - Origin.AddSeconds(end)).TotalSeconds, -0.000001, 1d / 30);
    }

    [Theory]
    [InlineData(0.1, 1.0, 0.0, 1.0, true)]
    [InlineData(0.0, 0.9, 0.0, 1.0, true)]
    [InlineData(0.0, 1.0, 0.01, 0.91, false)]
    [InlineData(0.0, 1.0, -0.1, 1.0, true)]
    [InlineData(0.0, 1.0, 0.0, 1.1, true)]
    public void PartialStatusUsesRequestedFrameGrid(double availableStart, double availableEnd,
        double requestedStart, double requestedEnd, bool partial)
    {
        var window = AkronReplayWindows.Plan(new[] { new AkronReplaySegment("a", availableStart, availableEnd) },
            Origin, 10, (long)Math.Round(availableEnd * 10), Origin.AddSeconds(requestedStart), Origin.AddSeconds(requestedEnd));
        Assert.Equal(partial, window.IsPartial);
    }

    [Fact]
    public void MissingEarlyHistoryClampsActualBoundsButInteriorGapFails()
    {
        var segments = new[] { new AkronReplaySegment("a", 6, 8), new AkronReplaySegment("b", 8, 10) };
        var window = AkronReplayWindows.Plan(segments, Origin, 30, 300, Origin.AddSeconds(5), Origin.AddSeconds(10));
        Assert.Equal(Origin.AddSeconds(6), window.StartUtc);
        Assert.Equal(120, window.FrameCount);
        Assert.Throws<InvalidDataException>(() => AkronReplayWindows.Plan(new[] {
            new AkronReplaySegment("a", 0, 2), new AkronReplaySegment("b", 4, 6) }, Origin, 30, 180, Origin, Origin.AddSeconds(6)));
    }

    [Fact]
    public void MillisecondSegmentMetadataDoesNotLoseAvailableBoundaryFrames()
    {
        var endRounded = AkronReplayWindows.Plan(new[] { new AkronReplaySegment("a", 0, 11.233) },
            Origin, 30, 337, Origin.AddSeconds(187d / 30), Origin.AddSeconds(337d / 30));
        Assert.Equal(150, endRounded.FrameCount);
        var startRounded = AkronReplayWindows.Plan(new[] { new AkronReplaySegment("a", 4.267, 10) },
            Origin, 60, 600, Origin.AddSeconds(256d / 60), Origin.AddSeconds(556d / 60));
        Assert.Equal(300, startRounded.FrameCount);
        Assert.Equal(0, startRounded.InputFirstFrame);
    }

    [Fact]
    public void CsvUsesFinalizedTimingsAndConfinesFilesToReplayDirectory()
    {
        string folder = Path.Combine(Path.GetTempPath(), "akron-csv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "segment-000000.mkv"), new byte[] { 1 });
            File.WriteAllText(Path.Combine(folder, "segments.csv"), "\"elsewhere/segment-000000.mkv\",0,2.000\nsegment-000001.mkv,2,4\n");
            var segment = Assert.Single(AkronReplayWindows.ReadSegments(folder));
            Assert.Equal(Path.Combine(folder, "segment-000000.mkv"), segment.Path);
            Assert.Equal(2d, segment.End);
            File.WriteAllText(Path.Combine(folder, "segments.csv"), "segment-000000.mkv,NaN,2\n");
            Assert.Throws<InvalidDataException>(() => AkronReplayWindows.ReadSegments(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ByteCatchupBudgetIsCheckedBeforeWriting()
    {
        var timeline = new AkronReplayTimeline(30, 1000);
        using var output = new MemoryStream();
        byte[] frame = new byte[2 * 1024 * 1024];
        timeline.WriteSample(output, frame, 0, Origin);
        Assert.Throws<IOException>(() => timeline.CompleteThrough(output, 2300));
        Assert.Equal(frame.Length, output.Length);
    }

    [Fact]
    public void ExportUsesFinalizedHistoryAndPrivateStaging()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "../../../../Source/Recorder");
        string export = File.ReadAllText(Path.Combine(root, "akron-internal-recorder-replay-export.cs"));
        Assert.Contains("ReadFinalizedSegments", export);
        Assert.Contains("LeaseReplayBuffer", export);
        Assert.DoesNotContain("snapshot.Timeline.CompleteThrough", export);
        Assert.DoesNotContain("replayProcess = null", export);
        Assert.DoesNotContain("AkronInternalAudioRecorder.StopReplayBuffer()", export);
        Assert.Contains("PinnedReplaySegments", export);
        Assert.Contains("MainThreadHelper.Schedule", export);
        Assert.Contains("Path.Combine(snapshot.Directory,", export);
        Assert.Contains("File.Move(output, publishedPath, true)", export);
        Assert.Contains("durationSeconds=", export);
        Assert.Contains("requestedStartUtc=", export);
        Assert.DoesNotContain("StandardError.ReadToEnd", export);
        string capture = File.ReadAllText(Path.Combine(root, "akron-internal-recorder-ffmpeg.cs"));
        Assert.Contains("-segment_list", capture);
        Assert.Contains("-avoid_negative_ts", capture);
        string audio = File.ReadAllText(Path.Combine(root, "akron-internal-audio-recorder.cs"));
        Assert.Contains("if (publishedTracks) Stop(purpose);", audio);
    }

    [Theory]
    [InlineData(-2, -1, 4, 0)]
    [InlineData(-2, 2, 16, 8)]
    [InlineData(1, 4, 12, 4)]
    public void AudioUsesIntersectionAndPadsUnavailableHeadAndTail(double from, double to, int samples, int nonzero)
    {
        string source = Path.GetTempFileName();
        AkronRecordedAudio result = new(new List<AkronRecordedAudioTrack>());
        try
        {
            File.WriteAllBytes(source, Enumerable.Range(0, 8).SelectMany(_ => new byte[] { 1, 0 }).ToArray());
            var audio = new AkronRecordedAudio(new List<AkronRecordedAudioTrack> {
                new(source, "mix", 4, 1, 16, Origin, Origin.AddSeconds(2)) });
            result = AkronReplayAudioSlice.Slice(audio, Origin.AddSeconds(from), Origin.AddSeconds(to));
            var track = Assert.Single(result.Tracks);
            byte[] bytes = File.ReadAllBytes(track.Path);
            Assert.Equal(samples * 2, bytes.Length);
            Assert.Equal(nonzero, bytes.Count(value => value == 1));
            if (from < 0) Assert.All(bytes.Take(Math.Min(samples, (int)(-from * 4)) * 2), value => Assert.Equal(0, value));
        }
        finally { File.Delete(source); foreach (var track in result.Tracks) File.Delete(track.Path); }
    }
}
