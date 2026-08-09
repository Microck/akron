using System;
using System.Text;
using Celeste.Mod.Akron;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

// Covers the pure parts of AkronPerformanceTelemetry: percentile math, the
// frame-time histogram boundaries, sample-window rollover, and the JSON
// escaping the JSONL recorder uses. Anything that needs a live Engine or an
// Everest game path is exercised by the in-game harness in scripts/akron-perf
// instead, because faking those would only test the fake.
[Collection("Performance")]
public sealed class PerformanceTelemetryTests {
    // The telemetry stores raw Stopwatch ticks and reports milliseconds, so the
    // tests build their inputs in ticks from a millisecond intent.
    private static long Ticks(double milliseconds) {
        return (long) Math.Round(milliseconds * (System.Diagnostics.Stopwatch.Frequency / 1000.0));
    }

    private static long[] SortedTicks(params double[] milliseconds) {
        long[] values = new long[milliseconds.Length];
        for (int i = 0; i < milliseconds.Length; i++) {
            values[i] = Ticks(milliseconds[i]);
        }

        AkronPerformanceTelemetry.SortSampleWindow(values, values.Length);
        return values;
    }

    [Fact]
    public void PercentileUsesNearestRankOverASortedWindow() {
        long[] samples = SortedTicks(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

        // Nearest rank: ceil(p/100 * 10), then a 1-based rank into a 0-based index.
        Assert.Equal(5.0, AkronPerformanceTelemetry.PercentileMs(samples, samples.Length, 50.0), 3);
        Assert.Equal(9.0, AkronPerformanceTelemetry.PercentileMs(samples, samples.Length, 90.0), 3);
        Assert.Equal(10.0, AkronPerformanceTelemetry.PercentileMs(samples, samples.Length, 95.0), 3);
        Assert.Equal(10.0, AkronPerformanceTelemetry.PercentileMs(samples, samples.Length, 99.0), 3);
        Assert.Equal(1.0, AkronPerformanceTelemetry.PercentileMs(samples, samples.Length, 0.0), 3);
    }

    [Fact]
    public void PercentileSeparatesTheBodyFromTheSpikes() {
        // 98 well-behaved frames and two 100 ms stalls. The average would sit at
        // 17.7 ms and hide both; p99 has to surface them. This is exactly the
        // spike distribution the maintainer reported and avg/worst cannot show.
        double[] milliseconds = new double[100];
        for (int i = 0; i < 98; i++) {
            milliseconds[i] = 16.0;
        }

        milliseconds[98] = 100.0;
        milliseconds[99] = 100.0;
        long[] samples = SortedTicks(milliseconds);

        Assert.Equal(16.0, AkronPerformanceTelemetry.PercentileMs(samples, samples.Length, 50.0), 3);
        Assert.Equal(16.0, AkronPerformanceTelemetry.PercentileMs(samples, samples.Length, 95.0), 3);
        Assert.Equal(100.0, AkronPerformanceTelemetry.PercentileMs(samples, samples.Length, 99.0), 3);
    }

    [Fact]
    public void PercentileIgnoresBufferEntriesBeyondTheSampleCount() {
        // The scratch buffer is preallocated and reused, so trailing entries from
        // an earlier, longer window must never leak into the result.
        long[] buffer = new long[8];
        buffer[0] = Ticks(10);
        buffer[1] = Ticks(20);
        buffer[2] = Ticks(30);
        for (int i = 3; i < buffer.Length; i++) {
            buffer[i] = Ticks(999);
        }

        Assert.Equal(30.0, AkronPerformanceTelemetry.PercentileMs(buffer, 3, 99.0), 3);
        Assert.Equal(20.0, AkronPerformanceTelemetry.PercentileMs(buffer, 3, 50.0), 3);
    }

    [Fact]
    public void PercentileReturnsZeroForAnEmptyWindow() {
        Assert.Equal(0.0, AkronPerformanceTelemetry.PercentileMs(new long[4], 0, 99.0));
        Assert.Equal(0.0, AkronPerformanceTelemetry.PercentileMs(null, 4, 99.0));
    }

    [Theory]
    // Bounds are 16.7 / 20 / 25 / 33 / 50 / 100 / 250 with an overflow slot.
    [InlineData(0.5, 0)]
    [InlineData(16.7, 0)]
    [InlineData(16.71, 1)]
    [InlineData(20.0, 1)]
    [InlineData(24.9, 2)]
    [InlineData(33.0, 3)]
    [InlineData(49.9, 4)]
    [InlineData(100.0, 5)]
    [InlineData(250.0, 6)]
    [InlineData(250.1, 7)]
    [InlineData(5000.0, 7)]
    public void HistogramBucketBoundsAreInclusiveOnTheUpperEdge(double milliseconds, int expectedIndex) {
        Assert.Equal(expectedIndex, AkronPerformanceTelemetry.HistogramBucketIndex(milliseconds));
    }

    [Fact]
    public void SortSampleWindowLeavesEntriesPastTheCountAlone() {
        long[] buffer = { 5, 3, 1, 900, 800 };
        AkronPerformanceTelemetry.SortSampleWindow(buffer, 3);

        Assert.Equal(new long[] { 1, 3, 5, 900, 800 }, buffer);
    }

    [Fact]
    public void SortSampleWindowIsANoOpForZeroOrOneSample() {
        long[] buffer = { 7, 2 };
        AkronPerformanceTelemetry.SortSampleWindow(buffer, 1);
        AkronPerformanceTelemetry.SortSampleWindow(buffer, 0);
        AkronPerformanceTelemetry.SortSampleWindow(null, 5);

        Assert.Equal(new long[] { 7, 2 }, buffer);
    }

    [Fact]
    public void WindowRollsOverAtTheFrameCapacityAndClearsItsSamples() {
        AkronPerformanceTelemetry.Reset();
        Assert.Equal(0, AkronPerformanceTelemetry.CompletedWindowCount);

        int capacity = AkronPerformanceTelemetry.WindowFrameCapacity;

        // The very first call only seeds the previous timestamp, so a full window
        // needs capacity + 1 calls before the first rollover.
        for (int i = 0; i < capacity; i++) {
            AkronPerformanceTelemetry.RecordUpdateFrame();
        }

        Assert.Equal(0, AkronPerformanceTelemetry.CompletedWindowCount);
        Assert.Equal(capacity - 1, AkronPerformanceTelemetry.FrameSampleCount);

        AkronPerformanceTelemetry.RecordUpdateFrame();

        Assert.Equal(1, AkronPerformanceTelemetry.CompletedWindowCount);
        Assert.Equal(0, AkronPerformanceTelemetry.FrameSampleCount);

        for (int i = 0; i < capacity; i++) {
            AkronPerformanceTelemetry.RecordUpdateFrame();
        }

        Assert.Equal(2, AkronPerformanceTelemetry.CompletedWindowCount);

        AkronPerformanceTelemetry.Reset();
        Assert.Equal(0, AkronPerformanceTelemetry.CompletedWindowCount);
        Assert.Equal(0, AkronPerformanceTelemetry.FrameSampleCount);
    }

    [Fact]
    public void RecordingIsOffUntilItIsStarted() {
        AkronPerformanceTelemetry.Reset();

        Assert.False(AkronPerformanceTelemetry.IsRecording);
        Assert.Contains("perf-recording: off", AkronPerformanceTelemetry.DescribeFrameCadence());
    }

    [Theory]
    [InlineData("baseline-n9-cold", "baseline-n9-cold")]
    [InlineData("  spaced label  ", "spaced-label")]
    [InlineData("../../etc/passwd", "..-..-etc-passwd")]
    [InlineData("run.1_2-3", "run.1_2-3")]
    [InlineData("", "run")]
    [InlineData("   ", "run")]
    [InlineData("!!!", "---")]
    public void RecordLabelsAreSanitizedIntoSafeFileNames(string input, string expected) {
        Assert.Equal(expected, AkronPerformanceTelemetry.SanitizeLabel(input));
    }

    [Fact]
    public void JsonStringsEscapeQuotesBackslashesAndControlCharacters() {
        StringBuilder builder = new StringBuilder();
        AkronPerformanceTelemetry.AppendJsonString(builder, "a\"b\\c\nd\te");

        Assert.Equal("\"a\\\"b\\\\c\\u000ad\\u0009e\"", builder.ToString());
    }

    [Fact]
    public void JsonStringsHandleNullAndEmptyValues() {
        StringBuilder builder = new StringBuilder();
        AkronPerformanceTelemetry.AppendJsonString(builder, null);
        AkronPerformanceTelemetry.AppendJsonString(builder, string.Empty);

        Assert.Equal("\"\"\"\"", builder.ToString());
    }

    [Fact]
    public void JsonStringsPassNonAsciiThrough() {
        // Akron map SIDs can contain any character a mod author picked. Only the
        // three JSON-significant classes are escaped; the rest travels as UTF-8.
        StringBuilder builder = new StringBuilder();
        AkronPerformanceTelemetry.AppendJsonString(builder, "Glyph/1-Forsaken");

        Assert.Equal("\"Glyph/1-Forsaken\"", builder.ToString());
    }

    [Fact]
    public void GcStateDescribesTheCollectorTheProcessActuallyGot() {
        // The GC facts this reports are read from the running collector, not
        // assumed from the target framework, because a host can turn background
        // GC off through runtimeconfig.json or DOTNET_gcConcurrent and the
        // meaning of a long pause changes completely when it does. This test
        // pins that every one of those fields resolves on a plain .NET 8 process
        // rather than throwing or reporting "unknown", so a field that reads
        // "unknown" in a real recording is a fact about the game host and not a
        // broken probe.
        string state = AkronPerformanceTelemetry.DescribeGcState();

        Assert.Contains("gc-server: ", state);
        Assert.Contains("gc-latency-mode: ", state);
        Assert.Contains("gc-background-collections: ", state);
        Assert.Contains("gc-blocking-collections: ", state);
        Assert.DoesNotContain("gc-concurrent-config: unknown", state);
        Assert.DoesNotContain("gc-heap-count: unknown", state);
    }

    [Fact]
    public void GcEventSubscriptionCanBeTurnedOffForAnAaRun() {
        // The runtime event subscription is the only part of the recorder that
        // costs anything outside the game thread, so a measurement has to be
        // able to run without it and show the same frame-time picture.
        bool original = AkronPerformanceTelemetry.GcEventsEnabled;
        try {
            AkronPerformanceTelemetry.GcEventsEnabled = false;
            Assert.False(AkronPerformanceTelemetry.GcEventsEnabled);
            AkronPerformanceTelemetry.GcEventsEnabled = true;
            Assert.True(AkronPerformanceTelemetry.GcEventsEnabled);
        } finally {
            AkronPerformanceTelemetry.GcEventsEnabled = original;
        }
    }
}
