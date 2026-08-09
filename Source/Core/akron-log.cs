using Celeste.Mod;
using Monocle;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Celeste.Mod.Akron;

// Skip writes nothing, Aggregate counts the event into the 60-second Diagnostic summary, Emit writes one line per event.
public enum AkronLoggingRecordMode {
    Skip,
    Aggregate,
    Emit
}

public static class AkronLog {
    private const string DirectoryName = "AkronLogs";
    private const string CurrentFileName = "akron-current.log";
    // Opening and closing the file per line costs about six blocking syscalls on the render thread.
    // The stream is held open and auto-flushes once per line, which is one write syscall.
    // We do not buffer across lines on purpose because this is a crash-diagnosis log, and the last lines before a crash matter.
    private static FileStream fileStream;
    private static StreamWriter streamWriter;
    private static readonly object FileLock = new object();
    private static readonly TimeSpan DiagnosticSummaryInterval = TimeSpan.FromSeconds(60);
    private static readonly object DiagnosticSummaryLock = new object();
    private static readonly Dictionary<AkronFeatureKind, long> DiagnosticAllowedPolicyChecks = new Dictionary<AkronFeatureKind, long>();
    private static readonly Dictionary<AkronFeatureKind, long> DiagnosticFeatureUses = new Dictionary<AkronFeatureKind, long>();
    private static DateTime diagnosticPolicyWindowStartedUtc;
    private static DateTime diagnosticFeatureUseWindowStartedUtc;

    public static void Normal(string source, string message) {
        Write(AkronLoggingLevel.Normal, source, message, mirrorLogLevel: null);
    }

    public static void Verbose(string source, string message) {
        Write(AkronLoggingLevel.Verbose, source, message, mirrorLogLevel: null);
    }

    public static void Trace(string source, string message) {
        Write(AkronLoggingLevel.Trace, source, message, mirrorLogLevel: null);
    }

    public static void Diagnostic(string source, string message) {
        Write(AkronLoggingLevel.Diagnostic, source, message, mirrorLogLevel: null);
    }

    public static void Info(string source, string message) {
        Write(AkronLoggingLevel.Normal, source, message, mirrorLogLevel: null);
    }

    public static void Warn(string source, string message) {
        Write(AkronLoggingLevel.Normal, source, message, LogLevel.Warn);
    }

    public static void Error(string source, string message) {
        Write(AkronLoggingLevel.Normal, source, message, LogLevel.Error);
    }

    public static string GetLogDirectory() {
        return Path.Combine(Everest.PathGame, "Saves", DirectoryName);
    }

    public static string GetCurrentLogPath() {
        return Path.Combine(GetLogDirectory(), CurrentFileName);
    }

    // AkronLog is reachable outside module lifetime: before Load, after Unload, and from headless tests that
    // exercise production helpers. AkronModule.Settings throws there because AkronModule.Instance is null, so
    // every entry point resolves settings through this and treats null as "logging is off".
    //
    // Null settings mean there is no destination we own. There is no configured level, no size cap, no
    // retention count, and no meaningful game path to write into, so any file we created would be guesswork:
    // a unit test run must not silently drop log files into the install directory, and a call before module
    // load must not crash the caller. So with no settings AkronLog is a no-op sink for both the log file and
    // the Everest mirror. This is one codepath, not a test-only branch: it is also the honest behavior for
    // logging that happens before the module loads or after it unloads.
    private static AkronModuleSettings ResolveSettings() {
        return AkronModule.TryGetSettings();
    }

    public static string DescribeSettings() {
        AkronModuleSettings settings = ResolveSettings();
        if (settings == null) {
            return "Unavailable (no module settings loaded)";
        }

        return (settings.Logging ? "On" : "Off") +
               " / " + FormatLevel(settings.LoggingLevel) +
               " / " + settings.LoggingMaxFileSizeMb.ToString(CultureInfo.InvariantCulture) + " MB x " +
               settings.LoggingRetainedFiles.ToString(CultureInfo.InvariantCulture);
    }

    public static string FormatLevel(AkronLoggingLevel level) {
        return AkronModuleSettings.NormalizeLoggingLevel(level) switch {
            AkronLoggingLevel.Normal => "Normal",
            AkronLoggingLevel.Verbose => "Verbose",
            AkronLoggingLevel.Diagnostic => "Diagnostic",
            _ => "Trace"
        };
    }

    // A policy check fires once per rendered frame per feature, so it only becomes a per-event line at
    // Trace. At Diagnostic it aggregates instead, which is why Diagnostic sits below Verbose in the ladder.
    internal static AkronLoggingRecordMode ResolvePolicyCheckRecordMode(bool loggingEnabled, AkronLoggingLevel configured) {
        configured = AkronModuleSettings.NormalizeLoggingLevel(configured);
        if (!loggingEnabled || configured < AkronLoggingLevel.Diagnostic) {
            return AkronLoggingRecordMode.Skip;
        }

        if (configured >= AkronLoggingLevel.Trace) {
            return AkronLoggingRecordMode.Emit;
        }

        return AkronLoggingRecordMode.Aggregate;
    }

    // A feature use becomes a per-event line one step earlier, at Verbose, because it is cheaper than a
    // policy check. It still aggregates at Diagnostic.
    internal static AkronLoggingRecordMode ResolveFeatureUseRecordMode(bool loggingEnabled, AkronLoggingLevel configured) {
        configured = AkronModuleSettings.NormalizeLoggingLevel(configured);
        if (!loggingEnabled || configured < AkronLoggingLevel.Diagnostic) {
            return AkronLoggingRecordMode.Skip;
        }

        if (configured >= AkronLoggingLevel.Verbose) {
            return AkronLoggingRecordMode.Emit;
        }

        return AkronLoggingRecordMode.Aggregate;
    }

    public static void LogSettingsChanged(string detail) {
        FlushDiagnosticSummaries();
        Normal(nameof(AkronLog), "logging settings changed: " + detail + "; " + DescribeSettings());
        AkronModuleSettings settings = ResolveSettings();
        // Do not keep the file handle open while logging is disabled, or while no settings authorize a file.
        if (settings == null || !settings.Logging) {
            CloseLogFile();
        }
    }

    // The one place the configured log level is allowed to move. The overlay radio button and the
    // akron_log_level console command both call this and do nothing else, so neither can drift from the
    // other: a change here reaches both, and there is no second way to set the level.
    //
    // The order is load-bearing. The pending Diagnostic summaries are flushed while the old level is still
    // in force, so counts collected under it are attributed to it rather than to the level being switched
    // to. Then the level moves, then the change is written to the log, then the settings file is rewritten
    // immediately: without that last step the chosen level only reaches disk on a clean overlay close, so a
    // crash or a kill silently reverts it, which is the defect this exists to prevent.
    //
    // Returns whether the new level reached disk. With no settings there is nothing to change and nothing
    // to write, matching how every other AkronLog entry point treats a null settings object.
    public static bool ApplyLoggingLevel(AkronLoggingLevel level, string reason) {
        AkronModuleSettings settings = ResolveSettings();
        if (settings == null) {
            return false;
        }

        FlushDiagnosticSummaries();
        settings.LoggingLevel = level;
        LogSettingsChanged("level=" + FormatLevel(level));
        return AkronModule.SaveAkronSettingsNow(reason);
    }

    public static void RecordPolicyCheck(AkronFeatureKind feature, AkronPolicyDecision decision) {
        // Runs once per rendered frame per feature, so the settings lookup is one property read and the
        // early return allocates nothing.
        AkronModuleSettings settings = ResolveSettings();
        if (settings == null) {
            return;
        }

        switch (ResolvePolicyCheckRecordMode(settings.Logging, settings.LoggingLevel)) {
            case AkronLoggingRecordMode.Skip:
                return;
            case AkronLoggingRecordMode.Emit:
                Trace(nameof(AkronModule), FormatPolicyCheckMessage(feature, decision));
                return;
            case AkronLoggingRecordMode.Aggregate:
                if (!decision.Allowed) {
                    Diagnostic(nameof(AkronModule), "policy check denied: " + feature + "; message=" + decision.Message);
                    return;
                }

                RecordDiagnosticAllowedPolicyCheck(feature);
                return;
        }
    }

    public static void RecordFeatureUse(AkronFeatureKind feature) {
        AkronModuleSettings settings = ResolveSettings();
        if (settings == null) {
            return;
        }

        switch (ResolveFeatureUseRecordMode(settings.Logging, settings.LoggingLevel)) {
            case AkronLoggingRecordMode.Skip:
                return;
            case AkronLoggingRecordMode.Aggregate:
                RecordDiagnosticFeatureUse(feature);
                return;
            case AkronLoggingRecordMode.Emit:
                Verbose(nameof(AkronModule), "feature use recorded: " + feature);
                return;
        }
    }

    public static void FlushDiagnosticSummaries() {
        // Draining the counters is destructive, so with no settings there is nothing to drain into and the
        // pending counts are kept instead of being thrown away against a sink that cannot write them.
        if (ResolveSettings() == null) {
            return;
        }

        DateTime now = DateTime.UtcNow;
        string policyMessage = TakeDiagnosticPolicySummary(now);
        if (!string.IsNullOrEmpty(policyMessage)) {
            Diagnostic(nameof(AkronModule), policyMessage);
        }

        string featureUseMessage = TakeDiagnosticFeatureUseSummary(now);
        if (!string.IsNullOrEmpty(featureUseMessage)) {
            Diagnostic(nameof(AkronModule), featureUseMessage);
        }
    }

    internal static string FormatDiagnosticPolicySummary(IReadOnlyDictionary<AkronFeatureKind, long> counts, TimeSpan window) {
        return FormatDiagnosticFeatureCounts("policy checks allowed", counts, window);
    }

    internal static string FormatDiagnosticFeatureUseSummary(IReadOnlyDictionary<AkronFeatureKind, long> counts, TimeSpan window) {
        return FormatDiagnosticFeatureCounts("feature uses recorded", counts, window);
    }

    private static string FormatDiagnosticFeatureCounts(string label, IReadOnlyDictionary<AkronFeatureKind, long> counts, TimeSpan window) {
        StringBuilder builder = new StringBuilder(label);
        builder.Append(":");
        bool any = false;
        foreach (AkronFeatureKind feature in Enum.GetValues(typeof(AkronFeatureKind))) {
            if (!counts.TryGetValue(feature, out long count) || count <= 0) {
                continue;
            }

            builder.Append(any ? ", " : " ");
            builder.Append(feature);
            builder.Append("=");
            builder.Append(count.ToString(CultureInfo.InvariantCulture));
            any = true;
        }

        if (!any) {
            builder.Append(" none");
        }

        builder.Append("; window-seconds=");
        builder.Append(Math.Max(0, (int) Math.Round(window.TotalSeconds)).ToString(CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    private static void Write(AkronLoggingLevel level, string source, string message, LogLevel? mirrorLogLevel) {
        AkronModuleSettings settings = ResolveSettings();
        if (settings == null) {
            return;
        }

        bool writeToFile = settings.Logging && ShouldWrite(level, settings.LoggingLevel);
        bool mirrorToEverest = mirrorLogLevel.HasValue && settings.LoggingMirrorWarningsToEverest;
        if (!writeToFile && !mirrorToEverest) {
            return;
        }

        string safeSource = string.IsNullOrWhiteSpace(source) ? nameof(AkronLog) : source.Trim();
        string safeMessage = RedactForStreamerMode(message ?? string.Empty, settings.StreamerMode);

        if (writeToFile) {
            WriteFileLine(level, safeSource, safeMessage, settings);
        }

        if (mirrorToEverest) {
            Logger.Log(mirrorLogLevel.Value, safeSource, safeMessage);
        }
    }

    internal static bool ShouldWrite(AkronLoggingLevel level, AkronLoggingLevel configured) {
        return AkronModuleSettings.NormalizeLoggingLevel(level) <= AkronModuleSettings.NormalizeLoggingLevel(configured);
    }

    private static void RecordDiagnosticAllowedPolicyCheck(AkronFeatureKind feature) {
        string message = null;
        DateTime now = DateTime.UtcNow;

        lock (DiagnosticSummaryLock) {
            if (diagnosticPolicyWindowStartedUtc == default) {
                diagnosticPolicyWindowStartedUtc = now;
            }

            if (!DiagnosticAllowedPolicyChecks.TryGetValue(feature, out long count)) {
                count = 0;
            }
            DiagnosticAllowedPolicyChecks[feature] = count + 1;

            if (now - diagnosticPolicyWindowStartedUtc >= DiagnosticSummaryInterval) {
                message = TakeDiagnosticPolicySummaryLocked(now);
            }
        }

        if (!string.IsNullOrEmpty(message)) {
            Diagnostic(nameof(AkronModule), message);
        }
    }

    private static void RecordDiagnosticFeatureUse(AkronFeatureKind feature) {
        string message = null;
        DateTime now = DateTime.UtcNow;

        lock (DiagnosticSummaryLock) {
            if (diagnosticFeatureUseWindowStartedUtc == default) {
                diagnosticFeatureUseWindowStartedUtc = now;
            }

            if (!DiagnosticFeatureUses.TryGetValue(feature, out long count)) {
                count = 0;
            }
            DiagnosticFeatureUses[feature] = count + 1;

            if (now - diagnosticFeatureUseWindowStartedUtc >= DiagnosticSummaryInterval) {
                message = TakeDiagnosticFeatureUseSummaryLocked(now);
            }
        }

        if (!string.IsNullOrEmpty(message)) {
            Diagnostic(nameof(AkronModule), message);
        }
    }

    private static string TakeDiagnosticPolicySummary(DateTime now) {
        lock (DiagnosticSummaryLock) {
            return TakeDiagnosticPolicySummaryLocked(now);
        }
    }

    private static string TakeDiagnosticFeatureUseSummary(DateTime now) {
        lock (DiagnosticSummaryLock) {
            return TakeDiagnosticFeatureUseSummaryLocked(now);
        }
    }

    private static string TakeDiagnosticPolicySummaryLocked(DateTime now) {
        if (DiagnosticAllowedPolicyChecks.Count == 0) {
            diagnosticPolicyWindowStartedUtc = default;
            return null;
        }

        TimeSpan window = diagnosticPolicyWindowStartedUtc == default ? TimeSpan.Zero : now - diagnosticPolicyWindowStartedUtc;
        string message = FormatDiagnosticPolicySummary(DiagnosticAllowedPolicyChecks, window);
        DiagnosticAllowedPolicyChecks.Clear();
        diagnosticPolicyWindowStartedUtc = default;
        return message;
    }

    private static string TakeDiagnosticFeatureUseSummaryLocked(DateTime now) {
        if (DiagnosticFeatureUses.Count == 0) {
            diagnosticFeatureUseWindowStartedUtc = default;
            return null;
        }

        TimeSpan window = diagnosticFeatureUseWindowStartedUtc == default ? TimeSpan.Zero : now - diagnosticFeatureUseWindowStartedUtc;
        string message = FormatDiagnosticFeatureUseSummary(DiagnosticFeatureUses, window);
        DiagnosticFeatureUses.Clear();
        diagnosticFeatureUseWindowStartedUtc = default;
        return message;
    }

    private static string FormatPolicyCheckMessage(AkronFeatureKind feature, AkronPolicyDecision decision) {
        return "policy check: " + feature + "; allowed=" + decision.Allowed.ToString().ToLowerInvariant() + "; message=" + decision.Message;
    }

    private static void WriteFileLine(AkronLoggingLevel level, string source, string message, AkronModuleSettings settings) {
        lock (FileLock) {
            try {
                string line = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                              " " + FormatLevel(level).ToUpperInvariant() +
                              " " + source +
                              " " + message +
                              Environment.NewLine;
                EnsureWriterLocked();
                if (fileStream.Position >= GetMaxFileSizeBytes(settings)) {
                    // Windows will not rename or delete a file that we ourselves still hold open, so the
                    // writer is closed before rotation and reopened after it. Anything else reading the file
                    // at that moment, in practice a backup archiving Saves, opens it with FileShare.Delete
                    // (AkronBackupActions.BackupSourceShare), which is what lets the rename below go through
                    // while that read is in flight. Linux enforces neither rule, so neither is visible on the
                    // development or test machines.
                    CloseWriterLocked();
                    RotateLogFiles(GetLogDirectory(), AkronModuleSettings.ClampLoggingRetainedFiles(settings.LoggingRetainedFiles));
                    EnsureWriterLocked();
                }

                streamWriter.Write(line);
            } catch (Exception exception) {
                CloseWriterLocked();
                if (settings.LoggingMirrorWarningsToEverest) {
                    Logger.Log(LogLevel.Warn, nameof(AkronLog), "Failed to write Akron log file: " + exception.Message);
                }
            }
        }
    }

    private static void EnsureWriterLocked() {
        if (streamWriter != null) {
            return;
        }

        Directory.CreateDirectory(GetLogDirectory());
        // This is a Windows concern that Linux never shows, because Linux does not enforce share modes.
        // FileShare.ReadWrite lets anything else open the file while we hold it for writing, which is what
        // makes Akron's own backup able to archive its own log. FileShare.Delete lets the file be renamed or
        // deleted underneath us, which is what makes rotation and a restore possible. Windows denies both by
        // default for a file that is open for writing.
        fileStream = new FileStream(
            GetCurrentLogPath(),
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        streamWriter = new StreamWriter(fileStream, new UTF8Encoding(false)) {
            AutoFlush = true
        };
    }

    private static void CloseWriterLocked() {
        // Dispose whichever of the two we hold. EnsureWriterLocked can throw between opening the stream and
        // wrapping it in a writer, and an orphaned write handle on akron-current.log would hold the file for
        // the rest of the session with nothing left pointing at it.
        //
        // The fields are cleared before the dispose because disposing flushes, so it can fail for the same
        // reason the write that brought us here failed. This runs on the failure path of a log write and must
        // leave consistent state behind rather than throw into the caller's frame.
        IDisposable open = (IDisposable) streamWriter ?? fileStream;
        streamWriter = null;
        fileStream = null;
        try {
            open?.Dispose();
        } catch {
        }
    }

    internal static void CloseLogFile() {
        lock (FileLock) {
            CloseWriterLocked();
        }
    }

    private static long GetMaxFileSizeBytes(AkronModuleSettings settings) {
        return Math.Max(1, AkronModuleSettings.ClampLoggingMaxFileSizeMb(settings.LoggingMaxFileSizeMb)) * 1024L * 1024L;
    }

    // Takes the directory and the retention count rather than reading them back out of settings, so the
    // rotation contract can be exercised directly against a real directory with a real handle held on the
    // file being rotated. The caller must have closed our own writer first.
    internal static void RotateLogFiles(string directory, int retainedFiles) {
        string current = Path.Combine(directory, CurrentFileName);
        for (int index = retainedFiles; index >= 1; index--) {
            string source = Path.Combine(directory, "akron-" + index.ToString(CultureInfo.InvariantCulture) + ".log");
            string destination = Path.Combine(directory, "akron-" + (index + 1).ToString(CultureInfo.InvariantCulture) + ".log");
            if (index == retainedFiles && File.Exists(source)) {
                File.Delete(source);
                continue;
            }

            if (File.Exists(source)) {
                if (File.Exists(destination)) {
                    File.Delete(destination);
                }

                File.Move(source, destination);
            }
        }

        string first = Path.Combine(directory, "akron-1.log");
        if (File.Exists(first)) {
            File.Delete(first);
        }

        if (retainedFiles > 0) {
            File.Move(current, first);
        } else {
            File.Delete(current);
        }
    }

    private static string RedactForStreamerMode(string message, bool streamerMode) {
        if (!streamerMode || string.IsNullOrWhiteSpace(message)) {
            return message;
        }

        string gamePath = Everest.PathGame;
        if (string.IsNullOrWhiteSpace(gamePath)) {
            return message;
        }

        string trimmed = gamePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string redacted = AkronModuleSettings.FormatPathForDisplay(trimmed, streamerMode);
        return message
            .Replace(trimmed, redacted)
            .Replace(trimmed.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), redacted);
    }
}
