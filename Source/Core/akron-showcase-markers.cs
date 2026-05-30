using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Celeste.Mod;

namespace Celeste.Mod.Akron;

public static class AkronShowcaseMarkers {
    private const string EnabledEnvironmentVariable = "AKRON_SHOWCASE_MARKERS";
    private const string LaunchFlag = "--akron-showcase-markers";
    private const string DirectoryName = "AkronShowcaseMarkers";
    private static readonly object Sync = new object();
    private static readonly Dictionary<string, DateTime> OpenIntervals = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

    private static bool initialized;
    private static bool? enabledOverride;
    private static Func<DateTime> utcNow = () => DateTime.UtcNow;
    private static string outputDirectoryOverride;
    private static string sessionId = string.Empty;
    private static string timelinePath = string.Empty;
    private static string detailsPath = string.Empty;
    private static DateTime? syncUtc;
    private static string syncLabel = string.Empty;

    public static bool Enabled {
        get {
            if (enabledOverride.HasValue) {
                return enabledOverride.Value;
            }

            return IsTruthy(Environment.GetEnvironmentVariable(EnabledEnvironmentVariable)) ||
                   Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, LaunchFlag, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static string DescribeStatus() {
        if (!Enabled) {
            return "disabled";
        }

        EnsureInitialized();
        string syncStatus = syncUtc.HasValue
            ? "sync=" + syncLabel + " " + syncUtc.Value.ToString("O", CultureInfo.InvariantCulture)
            : "sync=unset";
        return syncStatus + " timeline=" + timelinePath + " details=" + detailsPath;
    }

    public static void MarkSync(string label) {
        if (!Enabled) {
            return;
        }

        DateTime now = utcNow();
        string normalizedLabel = string.IsNullOrWhiteSpace(label) ? "SYNC" : label.Trim();
        lock (Sync) {
            EnsureInitializedLocked();
            syncUtc = now;
            syncLabel = normalizedLabel;
            AppendTimelineLocked(now, "sync", normalizedLabel, string.Empty, string.Empty, string.Empty, string.Empty);
            AppendDetailLocked(now, "sync", normalizedLabel, string.Empty, string.Empty);
        }
    }

    public static void MarkNote(string label) {
        if (!Enabled) {
            return;
        }

        DateTime now = utcNow();
        string normalizedLabel = string.IsNullOrWhiteSpace(label) ? "note" : label.Trim();
        lock (Sync) {
            EnsureInitializedLocked();
            AppendTimelineLocked(now, "note", normalizedLabel, string.Empty, string.Empty, string.Empty, string.Empty);
            AppendDetailLocked(now, "note", normalizedLabel, string.Empty, string.Empty);
        }
    }

    public static void MarkTopLevelToggle(string label, bool enabled, AkronFeatureKind? featureKind, string source) {
        if (!Enabled || string.IsNullOrWhiteSpace(label)) {
            return;
        }

        DateTime now = utcNow();
        string normalizedLabel = label.Trim();
        string feature = featureKind.HasValue ? featureKind.Value.ToString() : string.Empty;
        lock (Sync) {
            EnsureInitializedLocked();
            AppendTimelineLocked(now, enabled ? "feature_on" : "feature_off", normalizedLabel, enabled.ToString().ToLowerInvariant(), feature, source, string.Empty);

            if (enabled) {
                OpenIntervals[normalizedLabel] = now;
                return;
            }

            if (!OpenIntervals.TryGetValue(normalizedLabel, out DateTime startedUtc)) {
                return;
            }

            OpenIntervals.Remove(normalizedLabel);
            AppendTimelineLocked(now, "interval", normalizedLabel, "closed", feature, source, FormatDuration(now - startedUtc));
        }
    }

    public static void MarkPopupDetail(string parentLabel, string suboptionLabel, string kind, string value) {
        if (!Enabled) {
            return;
        }

        DateTime now = utcNow();
        string parent = string.IsNullOrWhiteSpace(parentLabel) ? "popup" : parentLabel.Trim();
        string option = string.IsNullOrWhiteSpace(suboptionLabel) ? "option" : suboptionLabel.Trim();
        lock (Sync) {
            EnsureInitializedLocked();
            AppendDetailLocked(now, kind, parent + " / " + option, value ?? string.Empty, string.Empty);
        }
    }

    internal static void ResetForTesting(string outputDirectory, Func<DateTime> utcNowProvider, bool? enabled) {
        lock (Sync) {
            initialized = false;
            enabledOverride = enabled;
            utcNow = utcNowProvider ?? (() => DateTime.UtcNow);
            outputDirectoryOverride = outputDirectory;
            sessionId = string.Empty;
            timelinePath = string.Empty;
            detailsPath = string.Empty;
            syncUtc = null;
            syncLabel = string.Empty;
            OpenIntervals.Clear();
        }
    }

    internal static string FormatOffsetForTesting(DateTime timestampUtc, DateTime? sync) {
        return FormatOffset(timestampUtc, sync);
    }

    private static void EnsureInitialized() {
        lock (Sync) {
            EnsureInitializedLocked();
        }
    }

    private static void EnsureInitializedLocked() {
        if (initialized) {
            return;
        }

        DateTime now = utcNow();
        sessionId = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string directory = ResolveOutputDirectory();
        Directory.CreateDirectory(directory);
        timelinePath = Path.Combine(directory, "showcase-" + sessionId + "-timeline.tsv");
        detailsPath = Path.Combine(directory, "showcase-" + sessionId + "-details.tsv");
        File.WriteAllText(timelinePath, "offset\tutc\tevent\tlabel\tstate\tfeature\tsource\tduration\n");
        File.WriteAllText(detailsPath, "offset\tutc\tkind\tlabel\tvalue\textra\n");
        initialized = true;
    }

    private static string ResolveOutputDirectory() {
        if (!string.IsNullOrWhiteSpace(outputDirectoryOverride)) {
            return outputDirectoryOverride;
        }

        return Path.Combine(Everest.PathGame, "Saves", DirectoryName);
    }

    private static void AppendTimelineLocked(DateTime timestampUtc, string eventName, string label, string state, string feature, string source, string duration) {
        string line = string.Join("\t", new[] {
            FormatOffset(timestampUtc, syncUtc),
            timestampUtc.ToString("O", CultureInfo.InvariantCulture),
            Escape(eventName),
            Escape(label),
            Escape(state),
            Escape(feature),
            Escape(source),
            Escape(duration)
        });
        File.AppendAllText(timelinePath, line + Environment.NewLine);
    }

    private static void AppendDetailLocked(DateTime timestampUtc, string kind, string label, string value, string extra) {
        string line = string.Join("\t", new[] {
            FormatOffset(timestampUtc, syncUtc),
            timestampUtc.ToString("O", CultureInfo.InvariantCulture),
            Escape(kind),
            Escape(label),
            Escape(value),
            Escape(extra)
        });
        File.AppendAllText(detailsPath, line + Environment.NewLine);
    }

    private static string FormatOffset(DateTime timestampUtc, DateTime? sync) {
        if (!sync.HasValue) {
            return "unsynced";
        }

        TimeSpan delta = timestampUtc - sync.Value;
        string sign = delta < TimeSpan.Zero ? "-" : string.Empty;
        delta = delta.Duration();
        return sign + ((int) delta.TotalMinutes).ToString(CultureInfo.InvariantCulture) + ":" +
               delta.Seconds.ToString("00", CultureInfo.InvariantCulture) + "." +
               delta.Milliseconds.ToString("000", CultureInfo.InvariantCulture);
    }

    private static string FormatDuration(TimeSpan duration) {
        duration = duration.Duration();
        return ((int) duration.TotalMinutes).ToString(CultureInfo.InvariantCulture) + ":" +
               duration.Seconds.ToString("00", CultureInfo.InvariantCulture) + "." +
               duration.Milliseconds.ToString("000", CultureInfo.InvariantCulture);
    }

    private static string Escape(string value) {
        return (value ?? string.Empty)
            .Replace("\t", " ")
            .Replace("\r", " ")
            .Replace("\n", " ");
    }

    private static bool IsTruthy(string value) {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant()) {
            case "1":
            case "true":
            case "on":
            case "yes":
                return true;
            default:
                return false;
        }
    }
}
