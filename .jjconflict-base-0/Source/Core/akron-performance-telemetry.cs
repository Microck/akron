using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Monocle;

namespace Celeste.Mod.Akron;

internal static class AkronPerformanceTelemetry {
    private const int Capacity = 240;
    private const int OverlayLogIntervalFrames = 120;

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
    }

    public static void RecordOverlayRenderCost(double inputMs, double layoutMs, double imguiMs, double drawMs) {
        overlayInputMsTotal += inputMs;
        overlayLayoutMsTotal += layoutMs;
        overlayImGuiMsTotal += imguiMs;
        overlayDrawMsTotal += drawMs;
        overlayCostSamples++;
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
                Logger.Log(LogLevel.Info, nameof(AkronPerformanceTelemetry), "Overlay hidden. " + DescribeOverlayRenderCadence());
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
            Logger.Log(LogLevel.Info, nameof(AkronPerformanceTelemetry), "Overlay visible. " + DescribeOverlayRenderCadence());
        }
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
        Logger.Log(LogLevel.Info, nameof(AkronPerformanceTelemetry),
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

    private sealed class WindowCost {
        public double TotalMs;
        public int Samples;
    }
}
