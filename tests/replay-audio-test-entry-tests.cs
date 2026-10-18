using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ReplayAudioTestEntryTests
{
    [Theory]
    [InlineData(-2, -1, 20, 0, 0, 0)]
    [InlineData(-2, 1, 40, 0, 20, 0)]
    [InlineData(1, 3, 0, 20, 40, 0)]
    [InlineData(4, 6, 0, 80, 20, 20)]
    [InlineData(5, 6, 0, 0, 0, 20)]
    [InlineData(1, 1, 0, 0, 0, 0)]
    [InlineData(2, 1, 0, 0, 0, 0)]
    public void TestEntryUsesProductionIntersectionAndSilenceContract(
        double from, double to, int leading, int sourceOffset, int copied, int trailing)
    {
        Verify(from, to, 1, leading, sourceOffset, copied, trailing);
    }

    [Fact]
    public void StereoFractionalWindowKeepsCompleteFrames()
    {
        // 10 Hz stereo: floor(-0.15 * 10) = -2; ceil(0.4 * 10) = 4 frames.
        Verify(-0.15, 0.25, 2, 8, 0, 8, 0);
    }

    private static void Verify(double from, double to, int channels,
        int leading, int sourceOffset, int copied, int trailing)
    {
        string source = Path.GetTempFileName();
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var input = Enumerable.Range(0, 100).Select(value => (byte)value).ToArray();
        AkronRecordedAudio result = new(Array.Empty<AkronRecordedAudioTrack>());
        try
        {
            File.WriteAllBytes(source, input);
            var audio = new AkronRecordedAudio(new[] {
                new AkronRecordedAudioTrack(source, "ramp", 10, channels, input.Length,
                    origin, origin.AddSeconds(5d / channels)) });
            result = AkronInternalRecorder.SliceRecordedAudioForTesting(
                audio, origin.AddSeconds(from), origin.AddSeconds(to));
            if (to <= from)
            {
                Assert.Empty(result.Tracks);
                return;
            }
            var track = Assert.Single(result.Tracks);
            var expected = new byte[leading].Concat(input.Skip(sourceOffset).Take(copied))
                .Concat(new byte[trailing]).ToArray();
            Assert.Equal(expected, File.ReadAllBytes(track.Path));
            Assert.Equal(expected.Length, track.Bytes);
            Assert.Equal(origin.AddSeconds(from), track.StartedUtc);
            Assert.Equal(origin.AddSeconds(to), track.EndedUtc);
            Assert.Equal(channels, track.Channels);
            Assert.Equal(10, track.SampleRate);
            Assert.Equal(0, track.Bytes % (channels * 2));
            Assert.Equal(input, File.ReadAllBytes(source));
        }
        finally
        {
            File.Delete(source);
            foreach (var track in result.Tracks) File.Delete(track.Path);
        }
    }
}
