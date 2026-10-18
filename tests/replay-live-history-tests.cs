using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ReplayLiveHistoryTests
{
    [Fact]
    public void StalledFinalizationHasBoundedFailureDeadline()
    {
        Assert.False(AkronInternalRecorder.ReplayFinalizationExpired(0, 100000, 1000));
        Assert.False(AkronInternalRecorder.ReplayFinalizationExpired(1000, 60999, 1000));
        Assert.True(AkronInternalRecorder.ReplayFinalizationExpired(1000, 61000, 1000));
    }

    [Fact]
    public void YoungerPendingRequestsDoNotDelayFinalizedDueWindows()
    {
        var origin = DateTime.UnixEpoch;
        var ends = new[] { 8, 11, 14, 17, 20 };
        var ready = ends.Where(e => AkronInternalRecorder.IsReplayWindowFinalized(origin.AddSeconds(e), origin.AddSeconds(12))).ToArray();
        Assert.Equal(new[] { 8, 11 }, ready);
        Assert.False(AkronInternalRecorder.IsReplayWindowFinalized(origin.AddSeconds(14), origin.AddSeconds(12)));
    }

    [Fact]
    public void LiveManifestAdmitsOnlyCompleteRowsWhileWriterIsOpen()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "segment-000000.mkv"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(dir, "segment-000001.mkv"), new byte[] { 2 });
            using var writer = new FileStream(Path.Combine(dir, "segments.csv"), FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            var text = System.Text.Encoding.UTF8.GetBytes("segment-000000.mkv,0,2\nsegment-000001.mkv,2,");
            writer.Write(text); writer.Flush();
            var segments = AkronReplayWindows.ReadFinalizedSegments(dir);
            Assert.Equal(2, Assert.Single(segments).End);
            writer.Write(System.Text.Encoding.UTF8.GetBytes("4\n")); writer.Flush();
            Assert.Equal(2, AkronReplayWindows.ReadFinalizedSegments(dir).Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PcmLeaseStitchesChunksAndIgnoresBytesAppendedAfterWatermark()
    {
        string first = Path.GetTempFileName(), second = Path.GetTempFileName();
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        AkronRecordedAudio result = new(Array.Empty<AkronRecordedAudioTrack>());
        using var lease = new AkronReplayAudioLease();
        try
        {
            File.WriteAllBytes(first, Enumerable.Range(0, 8).Select(x => (byte)x).ToArray());
            using var writer = new FileStream(second, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete);
            writer.Write(Enumerable.Range(8, 8).Select(x => (byte)x).ToArray()); writer.Flush();
            lease.Tracks.Add(new(new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), "mix", 2, 2, 8, origin, 0));
            lease.Tracks.Add(new(new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), "mix", 2, 2, 8, origin, 2));
            writer.Write(new byte[] { 99, 99, 99, 99 }); writer.Flush();
            // Source path deletion while leased is compatible with Windows handle sharing.
            File.Delete(first);
            result = lease.CopyWindow(origin.AddSeconds(-0.5), origin.AddSeconds(2.5));
            var output = Assert.Single(result.Tracks);
            Assert.Equal(new byte[4].Concat(Enumerable.Range(0, 16).Select(x => (byte)x)).Concat(new byte[4]).ToArray(), File.ReadAllBytes(output.Path));
            Assert.Equal(24, output.Bytes);
            Assert.Equal(origin.AddSeconds(-0.5), output.StartedUtc);
            Assert.Equal(origin.AddSeconds(2.5), output.EndedUtc);
        }
        finally { lease.Dispose(); File.Delete(first); File.Delete(second); foreach (var t in result.Tracks) File.Delete(t.Path); }
    }

    [Fact]
    public void OverlappingPlansKeepTheirTriggerFramesOnOneContinuousTimeline()
    {
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var segments = Enumerable.Range(0, 20).Select(i => new AkronReplaySegment("segment" + i, i * 2, i * 2 + 2)).ToArray();
        foreach (int eventSecond in new[] { 15, 18, 21, 24 })
        {
            var closed = segments.Where(s => s.End <= eventSecond + 10).ToArray();
            var window = AkronReplayWindows.Plan(closed, origin, 30, 1200,
                origin.AddSeconds(eventSecond - 10), origin.AddSeconds(eventSecond + 8));
            Assert.Equal(540, window.FrameCount);
            Assert.False(window.IsPartial);
            Assert.True(window.FirstFrame <= eventSecond * 30 && window.EndFrame > eventSecond * 30);
        }
    }
}
