using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using Celeste.Mod;
using Monocle;

namespace Celeste.Mod.Akron;

// Per-phase overlay cost attribution stays at sample-window granularity. Do not
// add call counters or per-call instrumentation to the render path: measuring
// memoized hot paths would add the cost this telemetry is meant to detect.
internal enum AkronPerfBucket {
    OverlayInput,
    OverlayLayout,
    OverlayImGui,
    OverlayDraw
}

internal static class AkronPerformanceTelemetry {
    private const int Capacity = 240;
    private const int OverlayLogIntervalFrames = 120;

    // One JSONL sample record is emitted every PerfWindowFrames update frames.
    // Same cadence as the overlay log line so the two are directly comparable,
    // and short enough that a 30 second scenario still yields ~15 records.
    private const int PerfWindowFrames = 120;
    private const int FrameSampleCapacity = 1024;
    private const int JsonWriterBufferBytes = 64 * 1024;
    internal const string PerfDirectoryName = ".tmp-perf";

    // Upper bounds in milliseconds. The last bucket is the overflow bucket, so
    // the array is one longer than the bound list.
    private static readonly double[] HistogramBoundsMs = { 16.7, 20.0, 25.0, 33.0, 50.0, 100.0, 250.0 };

    private static readonly double TicksPerMillisecond = Stopwatch.Frequency / 1000.0;

    private static readonly double[] overlayRenderIntervals = new double[Capacity];
    private static int overlayRenderIndex;
    private static int overlayRenderCount;
    private static long lastRenderTimestamp;
    private static int overlayFramesSinceLog;
    private static bool wasOverlayVisible;
    private static bool measureHiddenBaselineAfterOverlay;
    private static double hiddenBaselineTotal;
    private static double hiddenBaselineWorst;
    private static int hiddenBaselineSamples;
    private static double overlayInputMsTotal;
    private static double overlayLayoutMsTotal;
    private static double overlayImGuiMsTotal;
    private static double overlayDrawMsTotal;
    private static int overlayCostSamples;
    private static readonly Dictionary<string, WindowCost> overlayWindowCosts = new Dictionary<string, WindowCost>(StringComparer.Ordinal);

    // Per-frame state. Everything here is preallocated: RecordUpdateFrame runs
    // on the game thread every update and must not allocate, format a string or
    // touch the filesystem.
    private static readonly long[] frameTicks = new long[FrameSampleCapacity];
    private static readonly long[] frameTicksScratch = new long[FrameSampleCapacity];
    private static int frameTicksIndex;
    private static int frameTicksCount;
    private static long lastUpdateTimestamp;
    private static readonly long[] frameHistogram = new long[HistogramBoundsMs.Length + 1];
    private static readonly long[] bucketTicks = new long[Enum.GetValues<AkronPerfBucket>().Length];
    private static readonly long[] bucketCalls = new long[Enum.GetValues<AkronPerfBucket>().Length];
    private static int windowFrames;
    private static int windowIndex;
    private static long windowWorstTicks;
    private static int gcBaselineGen0;
    private static int gcBaselineGen1;
    private static int gcBaselineGen2;
    private static long gcBaselineAllocatedBytes;

    // Allocation attributed to the game thread, against the process total above.
    // The StartPos snapshot persistence worker runs on a thread pool thread
    // (AkronStartPosPersistence.RunWorker), so process-total minus game-thread is
    // a direct measurement of how much allocation pressure comes from off the
    // game thread rather than an inference from timing.
    private static long gcBaselineGameThreadAllocatedBytes;

    // Per-frame GC attribution. GC.CollectionCount is a counter read with no
    // allocation, so sampling it every update is affordable, and it is the only
    // way to say that one specific slow frame contained a collection instead of
    // merely sharing a window with one. Only gen0 is read on a normal frame:
    // every collection of any generation also bumps the gen0 count, so gen1 and
    // gen2 are read only once that has moved.
    private static int frameGen0;
    private static int frameGen1;
    private static int frameGen2;
    private static bool hasFrameGcBaseline;

    // Frames over each threshold, split by whether a collection completed during
    // that frame. The unsplit totals are already in the histogram; this split is
    // what answers "how many frames over 16.7 ms are attributable to GC".
    private static int gcFrames;
    private static int gcFramesOver16;
    private static int gcFramesOver33;
    private static int gcFramesOver100;
    private static int gen2Frames;
    private static int gen2FramesOver100;

    // Individual slow frames with the collection counts that advanced during
    // each. Fixed capacity and no allocation; a window that overflowed it would
    // be pathological, and the first entries would already say so.
    private const int SpikeCapacity = 64;
    private const double SpikeThresholdMs = 33.0;
    private static readonly double[] spikeMs = new double[SpikeCapacity];
    private static readonly int[] spikeGen0 = new int[SpikeCapacity];
    private static readonly int[] spikeGen1 = new int[SpikeCapacity];
    private static readonly int[] spikeGen2 = new int[SpikeCapacity];
    private static readonly int[] spikeFrame = new int[SpikeCapacity];
    private static int spikeCount;

    // Per-collection facts that no polling API supplies. GCMemoryInfo describes
    // the most recent collection of a kind, so a window holding several
    // collections keeps only the last, and it never carries the trigger reason at
    // all. The runtime's own event source carries both, per collection, and
    // dispatches off the game thread. Only armed while recording.
    private const int GcEventCapacity = 512;
    private const int GcEventKindStart = 0;
    private const int GcEventKindPause = 1;
    private static readonly object gcEventSync = new object();
    private static readonly int[] gcEventKind = new int[GcEventCapacity];
    private static readonly long[] gcEventIndex = new long[GcEventCapacity];
    private static readonly int[] gcEventGeneration = new int[GcEventCapacity];
    private static readonly int[] gcEventReason = new int[GcEventCapacity];
    private static readonly int[] gcEventType = new int[GcEventCapacity];
    private static readonly double[] gcEventPauseMs = new double[GcEventCapacity];
    private static int gcEventCount;
    private static long gcEventDropped;
    private static long gcSuspendBeginTicks;
    private static AkronGcEventListener gcEventListener;
    private static string gcEventStatus = "off";
    private static bool gcEventsEnabled = true;

    // Player position at the previous window boundary, so every record can carry
    // how far the player was from where it was one window ago. This is evidence,
    // not a filter: it samples endpoints, and a frame-symmetric scenario is
    // usually back at its start position when a boundary lands, so a zero here
    // does not mean the player stood still. The report decides which windows are
    // gameplay windows from tasRunning.
    private static float lastPlayerX;
    private static float lastPlayerY;
    private static bool hasLastPlayerPosition;

    // Recording state. The writer is opened once per run and flushed only on a
    // window boundary, so the recorder never adds file I/O to a gameplay frame.
    private static StreamWriter recordWriter;
    private static string recordLabel = string.Empty;
    private static string recordPath = string.Empty;
    private static readonly StringBuilder json = new StringBuilder(2048);

    // The everest.yaml version is what actually distinguishes two Akron builds.
    // The assembly informational version is a constant 1.0.0 in this project, so
    // it is only a last resort. Resolved lazily because Everest metadata is not
    // attached yet when this class is first touched.
    private static string BuildVersion =>
        AkronModule.Instance?.Metadata?.VersionString ??
        typeof(AkronModule).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        "unknown";

    // Everest reports VersionString as "1.0.0" for any everest.yaml version it
    // cannot parse into a System.Version, and Akron's versions carry a
    // "-beta.NN" suffix, so the version string alone cannot tell two builds
    // apart. The assembly MVID changes with every compilation, so it is the
    // field that actually identifies which binary produced a record.
    private static string BuildId => typeof(AkronModule).Assembly.ManifestModule.ModuleVersionId.ToString("N").Substring(0, 12);

    public static bool IsRecording => recordWriter != null;

    // Read-only views used by the telemetry unit tests to assert window
    // rollover without reaching into private state through reflection.
    internal static int FrameSampleCount => frameTicksCount;

    internal static int CompletedWindowCount => windowIndex;

    internal static int WindowFrameCapacity => PerfWindowFrames;

    public static string RecordingPath => recordPath;

    public static void Reset() {
        overlayRenderIndex = 0;
        overlayRenderCount = 0;
        lastRenderTimestamp = 0;
        overlayFramesSinceLog = 0;
        wasOverlayVisible = false;
        measureHiddenBaselineAfterOverlay = false;
        hiddenBaselineTotal = 0.0;
        hiddenBaselineWorst = 0.0;
        hiddenBaselineSamples = 0;
        overlayInputMsTotal = 0.0;
        overlayLayoutMsTotal = 0.0;
        overlayImGuiMsTotal = 0.0;
        overlayDrawMsTotal = 0.0;
        overlayCostSamples = 0;
        overlayWindowCosts.Clear();
        Array.Clear(overlayRenderIntervals, 0, overlayRenderIntervals.Length);

        // A reset is a "start measuring here" marker, so the frame ring, the
        // window and the GC baseline all restart. An in-progress recording
        // deliberately survives: the harness resets first, then records.
        Array.Clear(frameTicks, 0, frameTicks.Length);
        Array.Clear(frameHistogram, 0, frameHistogram.Length);
        Array.Clear(bucketTicks, 0, bucketTicks.Length);
        Array.Clear(bucketCalls, 0, bucketCalls.Length);
        frameTicksIndex = 0;
        frameTicksCount = 0;
        lastUpdateTimestamp = 0;
        windowFrames = 0;
        windowIndex = 0;
        windowWorstTicks = 0;
        hasFrameGcBaseline = false;
        hasLastPlayerPosition = false;
        ClearWindowGcAttribution();
        ResetGcEvents();
        CaptureGcBaseline();
    }

    public static void RecordOverlayRenderCost(double inputMs, double layoutMs, double imguiMs, double drawMs) {
        overlayInputMsTotal += inputMs;
        overlayLayoutMsTotal += layoutMs;
        overlayImGuiMsTotal += imguiMs;
        overlayDrawMsTotal += drawMs;
        overlayCostSamples++;

        // Mirror the overlay phases into the generic bucket array so a JSONL
        // sample carries real per-subsystem attribution without every overlay
        // call site having to learn the scope API.
        AddBucketMilliseconds(AkronPerfBucket.OverlayInput, inputMs);
        AddBucketMilliseconds(AkronPerfBucket.OverlayLayout, layoutMs);
        AddBucketMilliseconds(AkronPerfBucket.OverlayImGui, imguiMs);
        AddBucketMilliseconds(AkronPerfBucket.OverlayDraw, drawMs);
    }

    public static void RecordOverlayWindowCost(string title, double milliseconds) {
        if (string.IsNullOrWhiteSpace(title)) {
            return;
        }

        if (!overlayWindowCosts.TryGetValue(title, out WindowCost cost)) {
            cost = new WindowCost();
            overlayWindowCosts[title] = cost;
        }

        cost.TotalMs += milliseconds;
        cost.Samples++;
    }

    public static void RecordRenderFrame(bool overlayVisible) {
        long timestamp = Stopwatch.GetTimestamp();
        double interval = lastRenderTimestamp == 0
            ? 0.0
            : (timestamp - lastRenderTimestamp) / (double) Stopwatch.Frequency;
        lastRenderTimestamp = timestamp;

        if (!overlayVisible) {
            if (wasOverlayVisible && overlayRenderCount > 0) {
                AkronLog.Info(nameof(AkronPerformanceTelemetry), "Overlay hidden. " + DescribeOverlayRenderCadence());
                measureHiddenBaselineAfterOverlay = true;
                hiddenBaselineTotal = 0.0;
                hiddenBaselineWorst = 0.0;
                hiddenBaselineSamples = 0;
            }

            RecordHiddenBaselineAfterOverlay(interval);
            overlayFramesSinceLog = 0;
            wasOverlayVisible = false;
            return;
        }

        wasOverlayVisible = true;
        if (interval > 0.0) {
            overlayRenderIntervals[overlayRenderIndex] = interval;
            overlayRenderIndex = (overlayRenderIndex + 1) % overlayRenderIntervals.Length;
            overlayRenderCount = Math.Min(overlayRenderCount + 1, overlayRenderIntervals.Length);
        }

        overlayFramesSinceLog++;
        if (overlayFramesSinceLog >= OverlayLogIntervalFrames) {
            overlayFramesSinceLog = 0;
            AkronLog.Info(nameof(AkronPerformanceTelemetry), "Overlay visible. " + DescribeOverlayRenderCadence());
        }
    }

    // Called once per Engine.Update. Hot path: no allocation, no formatting, no
    // I/O. The only work that can be expensive is the window flush, and that
    // happens once every PerfWindowFrames frames.
    public static void RecordUpdateFrame() {
        long timestamp = Stopwatch.GetTimestamp();
        if (lastUpdateTimestamp == 0) {
            lastUpdateTimestamp = timestamp;
            CaptureGcBaseline();
            return;
        }

        long deltaTicks = timestamp - lastUpdateTimestamp;
        lastUpdateTimestamp = timestamp;
        if (deltaTicks < 0) {
            return;
        }

        frameTicks[frameTicksIndex] = deltaTicks;
        frameTicksIndex = (frameTicksIndex + 1) % frameTicks.Length;
        frameTicksCount = Math.Min(frameTicksCount + 1, frameTicks.Length);
        if (deltaTicks > windowWorstTicks) {
            windowWorstTicks = deltaTicks;
        }

        double frameMs = deltaTicks / TicksPerMillisecond;
        frameHistogram[HistogramBucketIndex(frameMs)]++;
        RecordFrameGcAttribution(frameMs);

        windowFrames++;
        if (windowFrames >= PerfWindowFrames) {
            FlushWindow();
        }
    }

    // Hot path, called for every recorded frame. One counter read on a normal
    // frame; two more only on the frames where a collection actually landed.
    // Nothing here allocates or formats.
    private static void RecordFrameGcAttribution(double frameMs) {
        int gen0 = GC.CollectionCount(0);
        if (!hasFrameGcBaseline) {
            frameGen0 = gen0;
            frameGen1 = GC.CollectionCount(1);
            frameGen2 = GC.CollectionCount(2);
            hasFrameGcBaseline = true;
            return;
        }

        bool collected = gen0 != frameGen0;
        int gen0Delta = 0;
        int gen1Delta = 0;
        int gen2Delta = 0;
        if (collected) {
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            gen0Delta = gen0 - frameGen0;
            gen1Delta = gen1 - frameGen1;
            gen2Delta = gen2 - frameGen2;
            frameGen0 = gen0;
            frameGen1 = gen1;
            frameGen2 = gen2;

            gcFrames++;
            if (frameMs > HistogramBoundsMs[0]) {
                gcFramesOver16++;
            }

            if (frameMs > SpikeThresholdMs) {
                gcFramesOver33++;
            }

            if (frameMs > 100.0) {
                gcFramesOver100++;
            }

            if (gen2Delta > 0) {
                gen2Frames++;
                if (frameMs > 100.0) {
                    gen2FramesOver100++;
                }
            }
        }

        // Every slow frame is recorded, collection or not. A 230 ms frame that
        // advanced no collection counter is the evidence that would rule GC out,
        // so it has to be able to appear here.
        if (frameMs > SpikeThresholdMs && spikeCount < SpikeCapacity) {
            spikeMs[spikeCount] = frameMs;
            spikeGen0[spikeCount] = gen0Delta;
            spikeGen1[spikeCount] = gen1Delta;
            spikeGen2[spikeCount] = gen2Delta;
            spikeFrame[spikeCount] = windowFrames;
            spikeCount++;
        }
    }

    public static bool StartRecording(string label, out string path) {
        StopRecording();
        Reset();
        path = string.Empty;
        try {
            string sanitized = SanitizeLabel(label);
            string directory = Path.Combine(Everest.PathGame, "Saves", PerfDirectoryName);
            Directory.CreateDirectory(directory);
            string preferredPath = Path.Combine(directory,
                "akron-perf-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                "-" + sanitized + ".jsonl");

            recordWriter = OpenRecordWriter(preferredPath, out string candidatePath);
            recordLabel = sanitized;
            recordPath = candidatePath;
            if (gcEventsEnabled) {
                StartGcEventListener();
            }

            WriteHeaderRecord();
            path = candidatePath;
            return true;
        } catch (Exception exception) {
            try {
                recordWriter?.Dispose();
            } catch {
                // The open or first write is already the failure being reported.
            }

            recordWriter = null;
            recordLabel = string.Empty;
            recordPath = string.Empty;
            StopGcEventListener();
            AkronLog.Warn(nameof(AkronPerformanceTelemetry),
                "Could not start perf recording: " + exception.Message);
            return false;
        }
    }

    internal static StreamWriter OpenRecordWriter(string preferredPath, out string actualPath) {
        actualPath = preferredPath;
        try {
            return CreateRecordWriter(actualPath);
        } catch (IOException) when (File.Exists(actualPath)) {
            // The timestamp only has second precision. Keep the readable filename in the normal case, then
            // add a collision-resistant suffix rather than replacing a recording started in the same second.
            actualPath = Path.Combine(
                Path.GetDirectoryName(preferredPath),
                Path.GetFileNameWithoutExtension(preferredPath) + "-" + Guid.NewGuid().ToString("N") +
                Path.GetExtension(preferredPath));
            return CreateRecordWriter(actualPath);
        }
    }

    private static StreamWriter CreateRecordWriter(string path) {
        return new StreamWriter(
            new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, JsonWriterBufferBytes),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            JsonWriterBufferBytes);
    }

    public static void StopRecording() {
        // A recording owns whole windows from its start through its stop. Emit
        // the last short window before detaching the writer so short runs and
        // terminal stalls are not silently lost.
        if (recordWriter != null && windowFrames > 0) {
            FlushWindow();
        }

        StreamWriter writer = recordWriter;
        recordWriter = null;
        recordLabel = string.Empty;
        recordPath = string.Empty;

        Exception stopError = null;
        try {
            writer?.Dispose();
        } catch (Exception exception) {
            stopError = exception;
        }
        StopGcEventListener();
        if (stopError != null) {
            AkronLog.Warn(nameof(AkronPerformanceTelemetry),
                "Could not close the perf recording: " + stopError.Message);
        }
    }

    // A/A control. The runtime event subscription is the one part of this
    // recorder that costs something outside the game thread, so it has to be
    // possible to run the identical scenario without it and show the frame-time
    // picture is the same.
    public static bool GcEventsEnabled {
        get => gcEventsEnabled;
        set => gcEventsEnabled = value;
    }

    public static string DescribeOverlayRenderCadence() {
        if (overlayRenderCount == 0) {
            return "overlay-render-fps: unavailable";
        }

        double total = 0.0;
        double worst = 0.0;
        for (int i = 0; i < overlayRenderCount; i++) {
            double interval = overlayRenderIntervals[i];
            total += interval;
            worst = Math.Max(worst, interval);
        }

        double averageInterval = total / overlayRenderCount;
        double averageFps = averageInterval <= 0.0 ? 0.0 : 1.0 / averageInterval;
        double worstFps = worst <= 0.0 ? 0.0 : 1.0 / worst;
        return "overlay-render-fps-avg: " + averageFps.ToString("0.0", CultureInfo.InvariantCulture) +
               "; overlay-render-fps-worst: " + worstFps.ToString("0.0", CultureInfo.InvariantCulture) +
               "; overlay-render-samples: " + overlayRenderCount.ToString(CultureInfo.InvariantCulture) +
               DescribeOverlayCost();
    }

    public static string DescribeFrameCadence() {
        int count = SortFrameSampleScratch();
        if (count == 0) {
            return "frame-ms: unavailable; perf-recording: " + (IsRecording ? recordPath : "off");
        }

        CaptureStartPosContext(out int placed, out int warm, out int cold);
        return "frame-ms-p50: " + Format(PercentileMs(frameTicksScratch, count, 50.0)) +
               "; frame-ms-p95: " + Format(PercentileMs(frameTicksScratch, count, 95.0)) +
               "; frame-ms-p99: " + Format(PercentileMs(frameTicksScratch, count, 99.0)) +
               "; frame-ms-worst: " + Format(frameTicksScratch[count - 1] / TicksPerMillisecond) +
               "; frame-samples: " + count.ToString(CultureInfo.InvariantCulture) +
               "; startpos-placed: " + placed.ToString(CultureInfo.InvariantCulture) +
               "; startpos-warm: " + warm.ToString(CultureInfo.InvariantCulture) +
               "; startpos-cold: " + cold.ToString(CultureInfo.InvariantCulture) +
               "; perf-recording: " + (IsRecording ? recordPath : "off");
    }

    // One line of GC configuration for the console and the automation queue, so
    // the collector's actual settings can be read off a running game without
    // waiting for a JSONL record. Built on demand, never per frame.
    public static string DescribeGcState() {
        GCMemoryInfo background = GC.GetGCMemoryInfo(GCKind.Background);
        GCMemoryInfo blocking = GC.GetGCMemoryInfo(GCKind.FullBlocking);
        string concurrentSetting = "unknown";
        string heapCount = "unknown";
        try {
            // Key names come from the runtime's own GC config dump, not from the
            // DOTNET_* environment variable spelling: the runtime reports
            // "ConcurrentGC" and "HeapCount" for what a host sets as
            // DOTNET_gcConcurrent and DOTNET_GCHeapCount.
            IReadOnlyDictionary<string, object> variables = GC.GetConfigurationVariables();
            if (variables.TryGetValue("ConcurrentGC", out object concurrent)) {
                concurrentSetting = concurrent?.ToString() ?? "null";
            }

            if (variables.TryGetValue("HeapCount", out object heaps)) {
                heapCount = heaps?.ToString() ?? "null";
            }
        } catch (Exception exception) {
            concurrentSetting = exception.GetType().Name;
        }

        return "gc-server: " + (GCSettings.IsServerGC ? "true" : "false") +
               "; gc-concurrent-config: " + concurrentSetting +
               "; gc-heap-count: " + heapCount +
               "; gc-latency-mode: " + GCSettings.LatencyMode +
               "; gc-background-collections: " + background.Index.ToString(CultureInfo.InvariantCulture) +
               "; gc-blocking-collections: " + blocking.Index.ToString(CultureInfo.InvariantCulture) +
               "; gc-counts: " + GC.CollectionCount(0).ToString(CultureInfo.InvariantCulture) +
               "/" + GC.CollectionCount(1).ToString(CultureInfo.InvariantCulture) +
               "/" + GC.CollectionCount(2).ToString(CultureInfo.InvariantCulture) +
               "; gc-events: " + gcEventStatus;
    }

    // Nearest-rank percentile over an already sorted tick buffer, returned in
    // milliseconds. Kept pure and internal so the unit tests can exercise the
    // math without a running game.
    internal static double PercentileMs(long[] sortedTicks, int count, double percentile) {
        if (sortedTicks == null || count <= 0) {
            return 0.0;
        }

        int rank = (int) Math.Ceiling(percentile / 100.0 * count);
        int index = Math.Clamp(rank - 1, 0, count - 1);
        return sortedTicks[index] / TicksPerMillisecond;
    }

    internal static void SortSampleWindow(long[] buffer, int count) {
        if (buffer == null || count <= 1) {
            return;
        }

        Array.Sort(buffer, 0, Math.Min(count, buffer.Length));
    }

    // Maps a frame time to its histogram slot. Separate and internal so the
    // bucket boundaries are directly testable.
    internal static int HistogramBucketIndex(double milliseconds) {
        for (int i = 0; i < HistogramBoundsMs.Length; i++) {
            if (milliseconds <= HistogramBoundsMs[i]) {
                return i;
            }
        }

        return HistogramBoundsMs.Length;
    }

    internal static string SanitizeLabel(string label) {
        if (string.IsNullOrWhiteSpace(label)) {
            return "run";
        }

        StringBuilder builder = new StringBuilder(label.Length);
        foreach (char value in label.Trim()) {
            builder.Append(char.IsAsciiLetterOrDigit(value) || value == '.' || value == '_' || value == '-'
                ? value
                : '-');
        }

        return builder.Length == 0 ? "run" : builder.ToString();
    }

    // Counts placed StartPos slots and splits them warm versus cold. A slot is
    // warm when AkronSaveLoadService still holds its captured runtime state in
    // memory; a cold slot has to go to disk on every HasRuntimeState probe,
    // which is the cost this harness exists to measure. Window boundary only.
    internal static void CaptureStartPosContext(out int placed, out int warm, out int cold) {
        placed = 0;
        warm = 0;
        cold = 0;
        Dictionary<int, AkronStartPos> startPositions = AkronModule.Session?.StartPositions;
        if (startPositions == null) {
            return;
        }

        foreach (AkronStartPos startPos in startPositions.Values) {
            if (startPos == null) {
                continue;
            }

            placed++;
            if (!string.IsNullOrWhiteSpace(startPos.StateSlotName) &&
                AkronSaveLoadService.GetRuntimeStateForDebug(startPos.StateSlotName) != null) {
                warm++;
            } else {
                cold++;
            }
        }
    }

    // Distance the player covered since the previous window boundary. This is a
    // sampled lower bound, not a path length: an oscillating scenario can return
    // to the same spot. It exists to separate "moving" from "frozen", which is
    // all the report needs it for.
    private static double MeasurePlayerMovement(Level level) {
        Player player = level?.Tracker?.GetEntity<Player>();
        if (player == null) {
            hasLastPlayerPosition = false;
            return 0.0;
        }

        float x = player.Position.X;
        float y = player.Position.Y;
        double moved = hasLastPlayerPosition
            ? Math.Sqrt(((double) x - lastPlayerX) * ((double) x - lastPlayerX) +
                        ((double) y - lastPlayerY) * ((double) y - lastPlayerY))
            : 0.0;
        lastPlayerX = x;
        lastPlayerY = y;
        hasLastPlayerPosition = true;
        return moved;
    }

    private static void RecordHiddenBaselineAfterOverlay(double interval) {
        if (!measureHiddenBaselineAfterOverlay || interval <= 0.0) {
            return;
        }

        hiddenBaselineTotal += interval;
        hiddenBaselineWorst = Math.Max(hiddenBaselineWorst, interval);
        hiddenBaselineSamples++;
        if (hiddenBaselineSamples < 120) {
            return;
        }

        double averageInterval = hiddenBaselineTotal / hiddenBaselineSamples;
        double averageFps = averageInterval <= 0.0 ? 0.0 : 1.0 / averageInterval;
        double worstFps = hiddenBaselineWorst <= 0.0 ? 0.0 : 1.0 / hiddenBaselineWorst;
        AkronLog.Info(nameof(AkronPerformanceTelemetry),
            "Overlay hidden baseline. render-fps-avg: " + averageFps.ToString("0.0", CultureInfo.InvariantCulture) +
            "; render-fps-worst: " + worstFps.ToString("0.0", CultureInfo.InvariantCulture) +
            "; render-samples: " + hiddenBaselineSamples.ToString(CultureInfo.InvariantCulture));
        measureHiddenBaselineAfterOverlay = false;
    }

    private static string DescribeOverlayCost() {
        if (overlayCostSamples == 0) {
            return string.Empty;
        }

        double divisor = overlayCostSamples;
        return "; overlay-cost-input-ms: " + (overlayInputMsTotal / divisor).ToString("0.00", CultureInfo.InvariantCulture) +
               "; overlay-cost-layout-ms: " + (overlayLayoutMsTotal / divisor).ToString("0.00", CultureInfo.InvariantCulture) +
               "; overlay-cost-imgui-ms: " + (overlayImGuiMsTotal / divisor).ToString("0.00", CultureInfo.InvariantCulture) +
               "; overlay-cost-draw-ms: " + (overlayDrawMsTotal / divisor).ToString("0.00", CultureInfo.InvariantCulture) +
               DescribeWindowCosts();
    }

    private static string DescribeWindowCosts() {
        if (overlayWindowCosts.Count == 0) {
            return string.Empty;
        }

        string joined = string.Join(", ",
            overlayWindowCosts
                .Where(pair => pair.Value.Samples > 0)
                .OrderByDescending(pair => pair.Value.TotalMs / pair.Value.Samples)
                .Take(4)
                .Select(pair => pair.Key + "=" + (pair.Value.TotalMs / pair.Value.Samples).ToString("0.00", CultureInfo.InvariantCulture)));
        return string.IsNullOrEmpty(joined) ? string.Empty : "; overlay-window-ms: " + joined;
    }

    private static void AddBucketMilliseconds(AkronPerfBucket bucket, double milliseconds) {
        if (milliseconds <= 0.0) {
            return;
        }

        bucketTicks[(int) bucket] += (long) (milliseconds * TicksPerMillisecond);
        bucketCalls[(int) bucket]++;
    }

    private static void CaptureGcBaseline() {
        gcBaselineGen0 = GC.CollectionCount(0);
        gcBaselineGen1 = GC.CollectionCount(1);
        gcBaselineGen2 = GC.CollectionCount(2);
        gcBaselineAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
        gcBaselineGameThreadAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
    }

    private static void ClearWindowGcAttribution() {
        gcFrames = 0;
        gcFramesOver16 = 0;
        gcFramesOver33 = 0;
        gcFramesOver100 = 0;
        gen2Frames = 0;
        gen2FramesOver100 = 0;
        spikeCount = 0;
    }

    // Copies the collected frame samples into the scratch array and sorts it.
    // Returns the sample count. The scratch array is preallocated, so this costs
    // one memcpy and one sort, paid at a window boundary or on a status query,
    // never per frame.
    private static int SortFrameSampleScratch() {
        int count = frameTicksCount;
        if (count <= 0) {
            return 0;
        }

        Array.Copy(frameTicks, 0, frameTicksScratch, 0, count);
        SortSampleWindow(frameTicksScratch, count);
        return count;
    }

    private static void FlushWindow() {
        int frames = windowFrames;
        windowFrames = 0;
        windowIndex++;

        int gen0 = GC.CollectionCount(0) - gcBaselineGen0;
        int gen1 = GC.CollectionCount(1) - gcBaselineGen1;
        int gen2 = GC.CollectionCount(2) - gcBaselineGen2;
        long allocated = GC.GetTotalAllocatedBytes(precise: false) - gcBaselineAllocatedBytes;
        long gameThreadAllocated = GC.GetAllocatedBytesForCurrentThread() - gcBaselineGameThreadAllocatedBytes;
        long totalMemory = GC.GetTotalMemory(forceFullCollection: false);

        if (recordWriter != null) {
            WriteSampleRecord(frames, gen0, gen1, gen2, allocated, gameThreadAllocated, totalMemory);
        } else {
            DrainGcEvents();
        }

        // Everything a record reports is scoped to its own window, including the
        // percentiles, so the frame samples reset here too. That keeps "frames"
        // and the frameMs block describing the same set of frames.
        Array.Clear(bucketTicks, 0, bucketTicks.Length);
        Array.Clear(bucketCalls, 0, bucketCalls.Length);
        Array.Clear(frameHistogram, 0, frameHistogram.Length);
        ClearWindowGcAttribution();
        frameTicksIndex = 0;
        frameTicksCount = 0;
        windowWorstTicks = 0;
        CaptureGcBaseline();
    }

    private static void WriteHeaderRecord() {
        json.Clear();
        json.Append("{\"type\":\"header\",\"utc\":");
        AppendString(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        json.Append(",\"build\":");
        AppendString(BuildVersion);
        json.Append(",\"buildId\":");
        AppendString(BuildId);
        json.Append(",\"label\":");
        AppendString(recordLabel);
        json.Append(",\"windowFrames\":").Append(PerfWindowFrames.ToString(CultureInfo.InvariantCulture));
        json.Append(",\"histogramBoundsMs\":[");
        for (int i = 0; i < HistogramBoundsMs.Length; i++) {
            if (i > 0) {
                json.Append(',');
            }

            json.Append(HistogramBoundsMs[i].ToString("0.###", CultureInfo.InvariantCulture));
        }

        json.Append(']');
        AppendGcConfiguration();
        json.Append('}');
        recordWriter.WriteLine(json.ToString());
        recordWriter.Flush();
    }

    // Written once per run. Whether background GC is on decides what a 230 ms
    // pause even means, and it cannot be assumed from the runtime version: the
    // host can turn it off through runtimeconfig.json or DOTNET_gcConcurrent.
    // GC.GetConfigurationVariables reports what the running collector actually
    // resolved, which is the only answer that counts.
    private static void AppendGcConfiguration() {
        json.Append(",\"gcConfig\":{\"serverGC\":").Append(Bool(GCSettings.IsServerGC));
        json.Append(",\"latencyMode\":");
        AppendString(GCSettings.LatencyMode.ToString());
        json.Append(",\"processorCount\":").Append(Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
        json.Append(",\"is64Bit\":").Append(Bool(Environment.Is64BitProcess));
        json.Append(",\"framework\":");
        AppendString(RuntimeInformation.FrameworkDescription);
        json.Append(",\"gcEvents\":");
        AppendString(gcEventStatus);
        IReadOnlyDictionary<string, object> variables = null;
        string variablesError = null;
        try {
            variables = GC.GetConfigurationVariables();
        } catch (Exception exception) {
            variablesError = exception.GetType().Name + ": " + exception.Message;
        }

        json.Append(",\"variables\":{");
        if (variables == null) {
            json.Append("\"error\":");
            AppendString(variablesError ?? "unavailable");
        } else {
            bool first = true;
            foreach (KeyValuePair<string, object> entry in variables) {
                if (!first) {
                    json.Append(',');
                }

                first = false;
                AppendString(entry.Key);
                json.Append(':');
                switch (entry.Value) {
                    case bool flag:
                        json.Append(Bool(flag));
                        break;
                    case long number:
                        json.Append(number.ToString(CultureInfo.InvariantCulture));
                        break;
                    case int number:
                        json.Append(number.ToString(CultureInfo.InvariantCulture));
                        break;
                    default:
                        AppendString(entry.Value?.ToString() ?? string.Empty);
                        break;
                }
            }
        }

        json.Append("}}");
    }

    private static void WriteSampleRecord(int frames, int gen0, int gen1, int gen2, long allocated, long gameThreadAllocated, long totalMemory) {
        // A perf recorder must never take the game down with it. This is the
        // only guarded region in the file: a failed write disarms recording
        // rather than repeating the failure every window.
        try {
            int sampleCount = SortFrameSampleScratch();
            Level level = Engine.Scene as Level;
            CaptureStartPosContext(out int placed, out int warm, out int cold);

            json.Clear();
            json.Append("{\"type\":\"sample\",\"utc\":");
            AppendString(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            json.Append(",\"build\":");
            AppendString(BuildVersion);
            json.Append(",\"buildId\":");
            AppendString(BuildId);
            json.Append(",\"label\":");
            AppendString(recordLabel);
            json.Append(",\"window\":").Append(windowIndex.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"frames\":").Append(frames.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"map\":");
            AppendString(level?.Session?.Area.GetSID() ?? string.Empty);
            json.Append(",\"room\":");
            AppendString(level?.Session?.Level ?? string.Empty);
            json.Append(",\"rooms\":").Append((level?.Session?.MapData?.Levels?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
            json.Append(",\"startposPlaced\":").Append(placed.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"startposWarm\":").Append(warm.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"startposCold\":").Append(cold.ToString(CultureInfo.InvariantCulture));

            // Movement and playback evidence. Both are read once per window, not
            // per frame. tasRunning lets the report keep only the windows where
            // scripted playback was actually driving the player, which is the
            // only reason two runs are comparable at all.
            json.Append(",\"tasRunning\":").Append(Bool(AkronInterop.IsTasRunning()));
            json.Append(",\"playerMovedPx\":").Append(Format(MeasurePlayerMovement(level)));

            double totalMs = 0.0;
            for (int i = 0; i < sampleCount; i++) {
                totalMs += frameTicksScratch[i] / TicksPerMillisecond;
            }

            json.Append(",\"frameMs\":{\"avg\":").Append(Format(sampleCount == 0 ? 0.0 : totalMs / sampleCount));
            json.Append(",\"p50\":").Append(Format(PercentileMs(frameTicksScratch, sampleCount, 50.0)));
            json.Append(",\"p95\":").Append(Format(PercentileMs(frameTicksScratch, sampleCount, 95.0)));
            json.Append(",\"p99\":").Append(Format(PercentileMs(frameTicksScratch, sampleCount, 99.0)));
            json.Append(",\"p999\":").Append(Format(PercentileMs(frameTicksScratch, sampleCount, 99.9)));
            json.Append(",\"worst\":").Append(Format(windowWorstTicks / TicksPerMillisecond));
            json.Append('}');

            json.Append(",\"histogram\":[");
            for (int i = 0; i < frameHistogram.Length; i++) {
                if (i > 0) {
                    json.Append(',');
                }

                json.Append(frameHistogram[i].ToString(CultureInfo.InvariantCulture));
            }

            json.Append(']');

            json.Append(",\"gc\":{\"gen0\":").Append(gen0.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"gen1\":").Append(gen1.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"gen2\":").Append(gen2.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"allocatedBytes\":").Append(allocated.ToString(CultureInfo.InvariantCulture));
            // Same window, split by allocating thread. The persistence worker is
            // a thread pool task, so allocatedBytes - gameThreadAllocatedBytes is
            // the off-thread share, measured rather than inferred.
            json.Append(",\"gameThreadAllocatedBytes\":").Append(gameThreadAllocated.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"totalMemoryBytes\":").Append(totalMemory.ToString(CultureInfo.InvariantCulture));
            json.Append('}');

            AppendGcAttribution();
            AppendGcMemoryInfo();
            AppendGcEvents();

            json.Append(",\"buckets\":{");
            bool first = true;
            for (int i = 0; i < bucketCalls.Length; i++) {
                if (bucketCalls[i] <= 0) {
                    continue;
                }

                if (!first) {
                    json.Append(',');
                }

                first = false;
                AppendString(CamelCase(((AkronPerfBucket) i).ToString()));
                json.Append(":{\"ms\":").Append(Format(bucketTicks[i] / TicksPerMillisecond));
                json.Append(",\"calls\":").Append(bucketCalls[i].ToString(CultureInfo.InvariantCulture));
                json.Append('}');
            }

            json.Append('}');

            json.Append(",\"settings\":{\"startPosShowLabel\":").Append(Bool(AkronModule.Settings.StartPosShowLabel));
            json.Append(",\"labelObstruction\":").Append(Bool(AkronModule.Settings.CustomHudLabelObstructionEnabled));
            json.Append(",\"mousePlacement\":").Append(Bool(AkronModule.Settings.StartPosMousePlacement));
            json.Append(",\"loggingLevel\":");
            AppendString(AkronModule.Settings.LoggingLevel.ToString());
            json.Append(",\"overlayVisible\":").Append(Bool(AkronModule.IsOverlayVisible));
            json.Append("}}");

            recordWriter.WriteLine(json.ToString());
            recordWriter.Flush();
        } catch (Exception exception) {
            string failedPath = recordPath;
            StopRecording();
            AkronLog.Warn(nameof(AkronPerformanceTelemetry),
                "Stopped perf recording at " + failedPath + " after a write failure: " + exception.Message);
        }
    }

    // Frame counts split by whether a collection landed inside the frame, plus
    // every slow frame with the generation counters that moved during it. This
    // is the direct answer to "is this 230 ms frame a GC pause": a spike with
    // gen2=1 is one, a spike with all zeros is not.
    private static void AppendGcAttribution() {
        json.Append(",\"gcFrames\":{\"any\":").Append(gcFrames.ToString(CultureInfo.InvariantCulture));
        json.Append(",\"over16\":").Append(gcFramesOver16.ToString(CultureInfo.InvariantCulture));
        json.Append(",\"over33\":").Append(gcFramesOver33.ToString(CultureInfo.InvariantCulture));
        json.Append(",\"over100\":").Append(gcFramesOver100.ToString(CultureInfo.InvariantCulture));
        json.Append(",\"gen2\":").Append(gen2Frames.ToString(CultureInfo.InvariantCulture));
        json.Append(",\"gen2Over100\":").Append(gen2FramesOver100.ToString(CultureInfo.InvariantCulture));
        json.Append('}');

        json.Append(",\"spikes\":[");
        for (int i = 0; i < spikeCount; i++) {
            if (i > 0) {
                json.Append(',');
            }

            json.Append("{\"frame\":").Append(spikeFrame[i].ToString(CultureInfo.InvariantCulture));
            json.Append(",\"ms\":").Append(Format(spikeMs[i]));
            json.Append(",\"gen0\":").Append(spikeGen0[i].ToString(CultureInfo.InvariantCulture));
            json.Append(",\"gen1\":").Append(spikeGen1[i].ToString(CultureInfo.InvariantCulture));
            json.Append(",\"gen2\":").Append(spikeGen2[i].ToString(CultureInfo.InvariantCulture));
            json.Append('}');
        }

        json.Append(']');
    }

    // The runtime's own view of the last collection of each kind. Blocking and
    // Background are asked for separately because that pair is what separates a
    // blocking gen2 from the stop-the-world phases of a background gen2, and
    // those two have different fixes. An Index of 0 for a kind means the process
    // has never done a collection of that kind, which is itself an answer.
    private static void AppendGcMemoryInfo() {
        json.Append(",\"gcInfo\":{");
        AppendGcMemoryInfoEntry("last", GC.GetGCMemoryInfo(), first: true);
        AppendGcMemoryInfoEntry("ephemeral", GC.GetGCMemoryInfo(GCKind.Ephemeral), first: false);
        AppendGcMemoryInfoEntry("blocking", GC.GetGCMemoryInfo(GCKind.FullBlocking), first: false);
        AppendGcMemoryInfoEntry("background", GC.GetGCMemoryInfo(GCKind.Background), first: false);
        json.Append('}');
    }

    private static void AppendGcMemoryInfoEntry(string name, GCMemoryInfo info, bool first) {
        if (!first) {
            json.Append(',');
        }

        AppendString(name);
        json.Append(":{\"index\":").Append(info.Index.ToString(CultureInfo.InvariantCulture));
        json.Append(",\"generation\":").Append(info.Generation.ToString(CultureInfo.InvariantCulture));
        json.Append(",\"concurrent\":").Append(Bool(info.Concurrent));
        json.Append(",\"compacted\":").Append(Bool(info.Compacted));
        json.Append(",\"pauseMs\":[");
        ReadOnlySpan<TimeSpan> pauses = info.PauseDurations;
        for (int i = 0; i < pauses.Length; i++) {
            if (i > 0) {
                json.Append(',');
            }

            json.Append(Format(pauses[i].TotalMilliseconds));
        }

        json.Append(']');
        json.Append(",\"pausePercent\":").Append(Format(info.PauseTimePercentage));
        json.Append(",\"heapBytes\":").Append(info.HeapSizeBytes.ToString(CultureInfo.InvariantCulture));
        json.Append(",\"fragmentedBytes\":").Append(info.FragmentedBytes.ToString(CultureInfo.InvariantCulture));
        json.Append(",\"promotedBytes\":").Append(info.PromotedBytes.ToString(CultureInfo.InvariantCulture));
        json.Append(",\"memoryLoadBytes\":").Append(info.MemoryLoadBytes.ToString(CultureInfo.InvariantCulture));
        json.Append(",\"pinnedObjects\":").Append(info.PinnedObjectsCount.ToString(CultureInfo.InvariantCulture));

        // Per-generation sizes. Index 3 is the large object heap and index 4 the
        // pinned object heap, which is where a 42-231 MB serialized document
        // lands: anything at or above 85000 bytes is allocated straight onto the
        // LOH and is only reclaimed by a gen2.
        json.Append(",\"generations\":[");
        ReadOnlySpan<GCGenerationInfo> generations = info.GenerationInfo;
        for (int i = 0; i < generations.Length; i++) {
            if (i > 0) {
                json.Append(',');
            }

            json.Append("{\"sizeBefore\":").Append(generations[i].SizeBeforeBytes.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"sizeAfter\":").Append(generations[i].SizeAfterBytes.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"fragBefore\":").Append(generations[i].FragmentationBeforeBytes.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"fragAfter\":").Append(generations[i].FragmentationAfterBytes.ToString(CultureInfo.InvariantCulture));
            json.Append('}');
        }

        json.Append("]}");
    }

    private static void AppendGcEvents() {
        json.Append(",\"gcEvents\":[");
        long droppedEvents;
        lock (gcEventSync) {
            for (int i = 0; i < gcEventCount; i++) {
                if (i > 0) {
                    json.Append(',');
                }

                if (gcEventKind[i] == GcEventKindStart) {
                    json.Append("{\"k\":\"start\",\"index\":").Append(gcEventIndex[i].ToString(CultureInfo.InvariantCulture));
                    json.Append(",\"gen\":").Append(gcEventGeneration[i].ToString(CultureInfo.InvariantCulture));
                    json.Append(",\"reason\":").Append(gcEventReason[i].ToString(CultureInfo.InvariantCulture));
                    json.Append(",\"type\":").Append(gcEventType[i].ToString(CultureInfo.InvariantCulture));
                    json.Append('}');
                } else {
                    json.Append("{\"k\":\"pause\",\"ms\":").Append(Format(gcEventPauseMs[i])).Append('}');
                }
            }

            gcEventCount = 0;
            droppedEvents = gcEventDropped;
        }

        json.Append(']');
        json.Append(",\"gcEventsDropped\":").Append(droppedEvents.ToString(CultureInfo.InvariantCulture));
    }

    private static void ResetGcEvents() {
        lock (gcEventSync) {
            gcEventCount = 0;
            gcEventDropped = 0;
            gcSuspendBeginTicks = 0;
        }
    }

    private static void DrainGcEvents() {
        lock (gcEventSync) {
            gcEventCount = 0;
        }
    }

    // Called from the EventPipe dispatch thread, never from the game thread.
    private static void PushGcEvent(int kind, long index, int generation, int reason, int type, double pauseMs) {
        lock (gcEventSync) {
            PushGcEventLocked(kind, index, generation, reason, type, pauseMs);
        }
    }

    private static void PushGcEventLocked(
        int kind,
        long index,
        int generation,
        int reason,
        int type,
        double pauseMs
    ) {
        if (gcEventCount >= GcEventCapacity) {
            gcEventDropped++;
            return;
        }

        gcEventKind[gcEventCount] = kind;
        gcEventIndex[gcEventCount] = index;
        gcEventGeneration[gcEventCount] = generation;
        gcEventReason[gcEventCount] = reason;
        gcEventType[gcEventCount] = type;
        gcEventPauseMs[gcEventCount] = pauseMs;
        gcEventCount++;
    }

    private static void StartGcEventListener() {
        if (gcEventListener != null) {
            return;
        }

        try {
            ResetGcEvents();
            gcEventListener = new AkronGcEventListener();
            gcEventListener.ArmExistingSources();
            gcEventStatus = gcEventListener.Armed ? "enabled" : "runtime-source-not-found";
        } catch (Exception exception) {
            gcEventListener = null;
            gcEventStatus = "error: " + exception.GetType().Name + ": " + exception.Message;
        }
    }

    private static void StopGcEventListener() {
        AkronGcEventListener listener = gcEventListener;
        gcEventListener = null;
        gcEventStatus = "off";
        try {
            listener?.Dispose();
        } catch (Exception exception) {
            AkronLog.Warn(nameof(AkronPerformanceTelemetry),
                "Could not stop the GC event listener: " + exception.Message);
        }
    }

    private static void AppendString(string value) {
        AppendJsonString(json, value);
    }

    // Minimal JSON string escaping. Internal so the serialization tests can
    // exercise the exact code the recorder uses.
    internal static void AppendJsonString(StringBuilder builder, string value) {
        builder.Append('"');
        foreach (char character in value ?? string.Empty) {
            switch (character) {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                default:
                    if (character < 0x20) {
                        builder.Append("\\u").Append(((int) character).ToString("x4", CultureInfo.InvariantCulture));
                    } else {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private static string CamelCase(string name) {
        return string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static string Format(double milliseconds) {
        return milliseconds.ToString("0.000", CultureInfo.InvariantCulture);
    }

    private static string Bool(bool value) {
        return value ? "true" : "false";
    }

    private sealed class WindowCost {
        public double TotalMs;
        public int Samples;
    }

    // In-process subscriber to the CoreCLR runtime event source. This is the only
    // way to get the trigger reason and the blocking/background/foreground type
    // of each collection: GCMemoryInfo carries neither, and an out-of-process
    // dotnet-trace session is not reachable for a game hosted under Wine.
    // Armed only while a perf recording is open, so a normal play session pays
    // nothing for it.
    private sealed class AkronGcEventListener : EventListener {
        private const string RuntimeSourceName = "Microsoft-Windows-DotNETRuntime";

        // Keyword 0x1 is GCKeyword. Informational deliberately, not Verbose:
        // GCAllocationTick sits on the same keyword at Verbose and fires every
        // ~100 KB allocated, which on a process allocating hundreds of MB per
        // second would be a tax large enough to change what is being measured.
        private const EventKeywords GcKeyword = (EventKeywords) 0x1;
        private const int GcStartEventId = 1;
        private const int GcRestartEEEndEventId = 3;
        private const int GcSuspendEEBeginEventId = 9;

        internal bool Armed { get; private set; }

        // OnEventSourceCreated fires from the base constructor for sources that
        // already exist, so the runtime source is usually caught there. It is not
        // guaranteed to exist yet at that point, so this second pass runs after
        // construction. Enabling twice is harmless.
        internal void ArmExistingSources() {
            foreach (EventSource source in EventSource.GetSources()) {
                if (string.Equals(source.Name, RuntimeSourceName, StringComparison.Ordinal)) {
                    EnableEvents(source, EventLevel.Informational, GcKeyword);
                    Armed = true;
                }
            }
        }

        protected override void OnEventSourceCreated(EventSource eventSource) {
            if (!string.Equals(eventSource.Name, RuntimeSourceName, StringComparison.Ordinal)) {
                return;
            }

            EnableEvents(eventSource, EventLevel.Informational, GcKeyword);
            Armed = true;
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData) {
            switch (eventData.EventId) {
                case GcSuspendEEBeginEventId:
                    lock (gcEventSync) {
                        gcSuspendBeginTicks = eventData.TimeStamp.Ticks;
                    }
                    break;
                case GcRestartEEEndEventId: {
                    // Suspend-begin to restart-end is the stop-the-world window,
                    // which is what a player feels. A background gen2 produces
                    // two short ones; a blocking gen2 produces one long one.
                    lock (gcEventSync) {
                        long begin = gcSuspendBeginTicks;
                        gcSuspendBeginTicks = 0;
                        if (begin > 0) {
                            double pauseMs =
                                (eventData.TimeStamp.Ticks - begin) / (double) TimeSpan.TicksPerMillisecond;
                            PushGcEventLocked(GcEventKindPause, 0, 0, 0, 0, pauseMs);
                        }
                    }

                    break;
                }

                case GcStartEventId:
                    PushGcEvent(
                        GcEventKindStart,
                        ReadPayload(eventData, "Count"),
                        (int) ReadPayload(eventData, "Depth"),
                        (int) ReadPayload(eventData, "Reason"),
                        (int) ReadPayload(eventData, "Type"),
                        0.0);
                    break;
            }
        }

        // Payloads are read by name rather than by position so a future event
        // version that adds a field cannot silently shift the reason into the
        // type column. Returns -1 when a field is absent, which the report shows
        // as an unknown rather than as reason 0 (AllocSmall).
        private static long ReadPayload(EventWrittenEventArgs eventData, string name) {
            IReadOnlyList<string> names = eventData.PayloadNames;
            IReadOnlyList<object> values = eventData.Payload;
            if (names == null || values == null) {
                return -1;
            }

            int count = Math.Min(names.Count, values.Count);
            for (int i = 0; i < count; i++) {
                if (!string.Equals(names[i], name, StringComparison.Ordinal)) {
                    continue;
                }

                try {
                    return Convert.ToInt64(values[i], CultureInfo.InvariantCulture);
                } catch (Exception) {
                    return -1;
                }
            }

            return -1;
        }
    }
}
