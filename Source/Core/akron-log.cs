using Celeste.Mod;
using Monocle;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Celeste.Mod.Akron;

public static class AkronLog {
    private const string DirectoryName = "AkronLogs";
    private const string CurrentFileName = "akron-current.log";
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

    public static string DescribeSettings() {
        AkronModuleSettings settings = AkronModule.Settings;
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

    public static void LogSettingsChanged(string detail) {
        FlushDiagnosticSummaries();
        Normal(nameof(AkronLog), "logging settings changed: " + detail + "; " + DescribeSettings());
    }

    public static void RecordPolicyCheck(AkronFeatureKind feature, AkronPolicyDecision decision) {
        AkronLoggingLevel configured = AkronModuleSettings.NormalizeLoggingLevel(AkronModule.Settings.LoggingLevel);
        if (!AkronModule.Settings.Logging || configured < AkronLoggingLevel.Diagnostic) {
            return;
        }

        if (configured >= AkronLoggingLevel.Trace) {
            Trace(nameof(AkronModule), FormatPolicyCheckMessage(feature, decision));
            return;
        }

        if (!decision.Allowed) {
            Diagnostic(nameof(AkronModule), "policy check denied: " + feature + "; message=" + decision.Message);
            return;
        }

        RecordDiagnosticAllowedPolicyCheck(feature);
    }

    public static void RecordFeatureUse(AkronFeatureKind feature) {
        AkronLoggingLevel configured = AkronModuleSettings.NormalizeLoggingLevel(AkronModule.Settings.LoggingLevel);
        if (!AkronModule.Settings.Logging || configured < AkronLoggingLevel.Verbose) {
            return;
        }

        if (configured == AkronLoggingLevel.Diagnostic) {
            RecordDiagnosticFeatureUse(feature);
            return;
        }

        Verbose(nameof(AkronModule), "feature use recorded: " + feature);
    }

    public static void FlushDiagnosticSummaries() {
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
        AkronModuleSettings settings = AkronModule.Settings;
        string safeSource = string.IsNullOrWhiteSpace(source) ? nameof(AkronLog) : source.Trim();
        string safeMessage = RedactForStreamerMode(message ?? string.Empty, settings.StreamerMode);

        if (settings.Logging && ShouldWrite(level, settings.LoggingLevel)) {
            WriteFileLine(level, safeSource, safeMessage, settings);
        }

        if (mirrorLogLevel.HasValue && settings.LoggingMirrorWarningsToEverest) {
            Logger.Log(mirrorLogLevel.Value, safeSource, safeMessage);
        }
    }

    private static bool ShouldWrite(AkronLoggingLevel level, AkronLoggingLevel configured) {
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
        try {
            string directory = GetLogDirectory();
            Directory.CreateDirectory(directory);
            string current = Path.Combine(directory, CurrentFileName);
            RotateIfNeeded(current, settings);
            string line = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                          " " + FormatLevel(level).ToUpperInvariant() +
                          " " + source +
                          " " + message +
                          Environment.NewLine;
            File.AppendAllText(current, line);
        } catch (Exception exception) {
            if (settings.LoggingMirrorWarningsToEverest) {
                Logger.Log(LogLevel.Warn, nameof(AkronLog), "Failed to write Akron log file: " + exception.Message);
            }
        }
    }

    private static void RotateIfNeeded(string current, AkronModuleSettings settings) {
        if (!File.Exists(current)) {
            return;
        }

        long maxBytes = Math.Max(1, AkronModuleSettings.ClampLoggingMaxFileSizeMb(settings.LoggingMaxFileSizeMb)) * 1024L * 1024L;
        FileInfo info = new FileInfo(current);
        if (info.Length < maxBytes) {
            return;
        }

        int retainedFiles = AkronModuleSettings.ClampLoggingRetainedFiles(settings.LoggingRetainedFiles);
        string directory = Path.GetDirectoryName(current) ?? GetLogDirectory();
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
