using Celeste.Mod.Akron;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

// AkronLog is called from production helpers that headless tests exercise directly, so every entry point has
// to survive AkronModule.Instance being null. These tests pin that contract and the verbosity ladder that
// decides whether an event is dropped, aggregated into the 60-second summary, or written as its own line.
[Collection(AkronSharedStateCollection.Name)]
public sealed class LogTests {
    private const BindingFlags Internals = BindingFlags.Static | BindingFlags.NonPublic;

    private static readonly FieldInfo StreamWriterField = typeof(AkronLog).GetField("streamWriter", Internals)
        ?? throw new InvalidOperationException("AkronLog.streamWriter is unavailable.");

    private static readonly FieldInfo FileStreamField = typeof(AkronLog).GetField("fileStream", Internals)
        ?? throw new InvalidOperationException("AkronLog.fileStream is unavailable.");

    private static readonly FieldInfo PolicyCountsField = typeof(AkronLog).GetField("DiagnosticAllowedPolicyChecks", Internals)
        ?? throw new InvalidOperationException("AkronLog.DiagnosticAllowedPolicyChecks is unavailable.");

    private static readonly FieldInfo FeatureUseCountsField = typeof(AkronLog).GetField("DiagnosticFeatureUses", Internals)
        ?? throw new InvalidOperationException("AkronLog.DiagnosticFeatureUses is unavailable.");

    private static readonly FieldInfo PolicyWindowField = typeof(AkronLog).GetField("diagnosticPolicyWindowStartedUtc", Internals)
        ?? throw new InvalidOperationException("AkronLog.diagnosticPolicyWindowStartedUtc is unavailable.");

    private static readonly FieldInfo FeatureUseWindowField = typeof(AkronLog).GetField("diagnosticFeatureUseWindowStartedUtc", Internals)
        ?? throw new InvalidOperationException("AkronLog.diagnosticFeatureUseWindowStartedUtc is unavailable.");

    private static readonly MethodInfo RecordAllowedPolicyCheckMethod = typeof(AkronLog).GetMethod("RecordDiagnosticAllowedPolicyCheck", Internals)
        ?? throw new InvalidOperationException("AkronLog.RecordDiagnosticAllowedPolicyCheck is unavailable.");

    private static readonly MethodInfo RecordFeatureUseMethod = typeof(AkronLog).GetMethod("RecordDiagnosticFeatureUse", Internals)
        ?? throw new InvalidOperationException("AkronLog.RecordDiagnosticFeatureUse is unavailable.");

    private static Dictionary<AkronFeatureKind, long> PolicyCounts =>
        (Dictionary<AkronFeatureKind, long>) PolicyCountsField.GetValue(null)!;

    private static Dictionary<AkronFeatureKind, long> FeatureUseCounts =>
        (Dictionary<AkronFeatureKind, long>) FeatureUseCountsField.GetValue(null)!;

    private static void ResetAggregationState() {
        PolicyCounts.Clear();
        FeatureUseCounts.Clear();
        PolicyWindowField.SetValue(null, default(DateTime));
        FeatureUseWindowField.SetValue(null, default(DateTime));
    }

    [Fact]
    public void EveryEntryPointIsSafeWithoutModuleSettings() {
        Assert.Null(AkronModule.Instance);
        ResetAggregationState();

        AkronLog.Normal(nameof(LogTests), "normal");
        AkronLog.Info(nameof(LogTests), "info");
        AkronLog.Diagnostic(nameof(LogTests), "diagnostic");
        AkronLog.Verbose(nameof(LogTests), "verbose");
        AkronLog.Trace(nameof(LogTests), "trace");
        AkronLog.Warn(nameof(LogTests), "warn");
        AkronLog.Error(nameof(LogTests), "error");
        AkronLog.Normal(null, null);

        AkronLog.RecordPolicyCheck(AkronFeatureKind.RoomTimer, new AkronPolicyDecision(true, "allowed"));
        AkronLog.RecordPolicyCheck(AkronFeatureKind.RoomTimer, new AkronPolicyDecision(false, "denied"));
        AkronLog.RecordFeatureUse(AkronFeatureKind.RoomTimer);
        AkronLog.FlushDiagnosticSummaries();
        AkronLog.LogSettingsChanged("test");
        // There is no settings object to move the level on and none to write, so this reports that nothing
        // was saved rather than throwing on AkronModule.Settings the way the old inline radio-button body would.
        Assert.False(AkronLog.ApplyLoggingLevel(AkronLoggingLevel.Trace, nameof(LogTests)));
        AkronLog.CloseLogFile();

        Assert.Equal("Unavailable (no module settings loaded)", AkronLog.DescribeSettings());
        Assert.Equal("Diagnostic", AkronLog.FormatLevel(AkronLoggingLevel.Diagnostic));

        // Nothing may reach the file system: a unit test run must not open or rotate log files, and there are
        // no settings to tell us the level, size cap, or retention count that would authorize one.
        Assert.Null(StreamWriterField.GetValue(null));
        Assert.Empty(PolicyCounts);
        Assert.Empty(FeatureUseCounts);
    }

    [Fact]
    public void DiagnosticSuppressesVerboseAndTrace() {
        Assert.True(AkronLog.ShouldWrite(AkronLoggingLevel.Normal, AkronLoggingLevel.Diagnostic));
        Assert.True(AkronLog.ShouldWrite(AkronLoggingLevel.Diagnostic, AkronLoggingLevel.Diagnostic));
        Assert.False(AkronLog.ShouldWrite(AkronLoggingLevel.Verbose, AkronLoggingLevel.Diagnostic));
        Assert.False(AkronLog.ShouldWrite(AkronLoggingLevel.Trace, AkronLoggingLevel.Diagnostic));
    }

    [Fact]
    public void VerboseSuppressesTrace() {
        Assert.True(AkronLog.ShouldWrite(AkronLoggingLevel.Verbose, AkronLoggingLevel.Verbose));
        Assert.True(AkronLog.ShouldWrite(AkronLoggingLevel.Diagnostic, AkronLoggingLevel.Verbose));
        Assert.False(AkronLog.ShouldWrite(AkronLoggingLevel.Trace, AkronLoggingLevel.Verbose));
    }

    // The aggregation gates read "configured < Diagnostic" so that Diagnostic aggregates instead of skipping.
    // A regression there is silent: policy checks and feature uses would simply stop being counted.
    [Fact]
    public void DiagnosticAggregatesPolicyChecksAndFeatureUses() {
        Assert.Equal(
            AkronLoggingRecordMode.Aggregate,
            AkronLog.ResolvePolicyCheckRecordMode(loggingEnabled: true, AkronLoggingLevel.Diagnostic));
        Assert.Equal(
            AkronLoggingRecordMode.Aggregate,
            AkronLog.ResolveFeatureUseRecordMode(loggingEnabled: true, AkronLoggingLevel.Diagnostic));
        Assert.Equal(
            AkronLoggingRecordMode.Skip,
            AkronLog.ResolvePolicyCheckRecordMode(loggingEnabled: true, AkronLoggingLevel.Normal));
        Assert.Equal(
            AkronLoggingRecordMode.Skip,
            AkronLog.ResolveFeatureUseRecordMode(loggingEnabled: true, AkronLoggingLevel.Normal));
    }

    [Fact]
    public void AggregatedPolicyChecksAccumulateAndFlushAfterTheWindow() {
        ResetAggregationState();

        RecordAllowedPolicyCheckMethod.Invoke(null, new object[] { AkronFeatureKind.RoomTimer });
        RecordAllowedPolicyCheckMethod.Invoke(null, new object[] { AkronFeatureKind.RoomTimer });
        RecordAllowedPolicyCheckMethod.Invoke(null, new object[] { AkronFeatureKind.DeathStats });

        Assert.Equal(2L, PolicyCounts[AkronFeatureKind.RoomTimer]);
        Assert.Equal(1L, PolicyCounts[AkronFeatureKind.DeathStats]);
        Assert.NotEqual(default, (DateTime) PolicyWindowField.GetValue(null)!);

        // Backdate the window past the 60-second summary interval so the next event drains it.
        PolicyWindowField.SetValue(null, DateTime.UtcNow - TimeSpan.FromSeconds(61));
        RecordAllowedPolicyCheckMethod.Invoke(null, new object[] { AkronFeatureKind.RoomTimer });

        Assert.Empty(PolicyCounts);
        Assert.Equal(default, (DateTime) PolicyWindowField.GetValue(null)!);
        ResetAggregationState();
    }

    [Fact]
    public void AggregatedFeatureUsesAccumulateAndFlushAfterTheWindow() {
        ResetAggregationState();

        RecordFeatureUseMethod.Invoke(null, new object[] { AkronFeatureKind.RoomTimer });
        RecordFeatureUseMethod.Invoke(null, new object[] { AkronFeatureKind.RoomTimer });

        Assert.Equal(2L, FeatureUseCounts[AkronFeatureKind.RoomTimer]);
        Assert.NotEqual(default, (DateTime) FeatureUseWindowField.GetValue(null)!);

        FeatureUseWindowField.SetValue(null, DateTime.UtcNow - TimeSpan.FromSeconds(61));
        RecordFeatureUseMethod.Invoke(null, new object[] { AkronFeatureKind.RoomTimer });

        Assert.Empty(FeatureUseCounts);
        Assert.Equal(default, (DateTime) FeatureUseWindowField.GetValue(null)!);
        ResetAggregationState();
    }

    // The Aggregate branch of the per-frame entry points has to feed the summary counters rather than emit a
    // line, and the summary text has to name the counted features so a Diagnostic log stays readable.
    [Fact]
    public void DiagnosticSummaryTextNamesCountedFeatures() {
        Dictionary<AkronFeatureKind, long> counts = new Dictionary<AkronFeatureKind, long> {
            [AkronFeatureKind.RoomTimer] = 12,
            [AkronFeatureKind.DeathStats] = 3
        };

        string policySummary = AkronLog.FormatDiagnosticPolicySummary(counts, TimeSpan.FromSeconds(60));
        Assert.Contains("policy checks allowed:", policySummary);
        Assert.Contains("RoomTimer=12", policySummary);
        Assert.Contains("DeathStats=3", policySummary);
        Assert.Contains("window-seconds=60", policySummary);

        string featureUseSummary = AkronLog.FormatDiagnosticFeatureUseSummary(counts, TimeSpan.FromSeconds(60));
        Assert.Contains("feature uses recorded:", featureUseSummary);
        Assert.Contains("RoomTimer=12", featureUseSummary);
    }

    [Fact]
    public void RecordPolicyCheckAggregatesAllowedChecksAndEmitsDenialsAtDiagnostic() {
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../Source/Core/akron-log.cs"));
        int aggregateBranch = source.IndexOf("case AkronLoggingRecordMode.Aggregate:", StringComparison.Ordinal);
        Assert.True(aggregateBranch > 0);

        // An allowed check must never become a per-frame line at Diagnostic; a denial is rare and is written.
        int denial = source.IndexOf("policy check denied: ", aggregateBranch, StringComparison.Ordinal);
        int aggregation = source.IndexOf("RecordDiagnosticAllowedPolicyCheck(feature);", aggregateBranch, StringComparison.Ordinal);
        Assert.True(denial > 0);
        Assert.True(aggregation > denial);
    }

    // The log file is now held open for the whole session. Windows will not rename a file its own process
    // still holds, so rotation only works because the writer is closed first and reopened afterwards. Linux
    // renames an open file happily, so this ordering is invisible here and has to be pinned as a contract.
    [Fact]
    public void RotationClosesTheHeldWriterBeforeMovingTheFile() {
        string source = ReadLogSource();
        int sizeCheck = source.IndexOf("if (fileStream.Position >= GetMaxFileSizeBytes(settings)) {", StringComparison.Ordinal);
        Assert.True(sizeCheck > 0, "The rotation size check is unavailable.");

        int close = source.IndexOf("CloseWriterLocked();", sizeCheck, StringComparison.Ordinal);
        int rotate = source.IndexOf(
            "RotateLogFiles(GetLogDirectory(), AkronModuleSettings.ClampLoggingRetainedFiles(settings.LoggingRetainedFiles));",
            sizeCheck,
            StringComparison.Ordinal);
        int reopen = source.IndexOf("EnsureWriterLocked();", rotate, StringComparison.Ordinal);
        Assert.True(close > sizeCheck);
        Assert.True(rotate > close, "Rotation must run after the writer is closed, with a clamped retention count.");
        Assert.True(reopen > rotate);
    }

    // EnsureWriterLocked can throw between opening the stream and wrapping it in a writer. Whatever it left
    // behind has to be closed, or an orphaned write handle keeps akron-current.log open for the rest of the
    // session with nothing left pointing at it, which on Windows is a handle nothing can get past.
    [Fact]
    public void ClosingTheLogDisposesAStreamThatNeverGotAWriter() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-log-orphan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        FileStream? orphan = null;
        try {
            orphan = new FileStream(
                Path.Combine(directory, "akron-current.log"),
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            Assert.Null(StreamWriterField.GetValue(null));
            FileStreamField.SetValue(null, orphan);

            AkronLog.CloseLogFile();

            Assert.True(orphan.SafeFileHandle.IsClosed);
            Assert.Null(FileStreamField.GetValue(null));
            Assert.Null(StreamWriterField.GetValue(null));
        } finally {
            FileStreamField.SetValue(null, null);
            StreamWriterField.SetValue(null, null);
            orphan?.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    // The other holder of akron-current.log during rotation is a backup archiving Saves. That read shares
    // Delete, which on Windows is what lets the rename below happen while the read is in flight.
    [Fact]
    public void RotationMovesTheCurrentLogWhileABackupReadsIt() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-log-rotation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            string current = Path.Combine(directory, "akron-current.log");
            File.WriteAllText(current, "rotated line");

            using (FileStream backupRead = new FileStream(current, FileMode.Open, FileAccess.Read, AkronBackupActions.AppendOnlyBackupSourceShare)) {
                AkronLog.RotateLogFiles(directory, retainedFiles: 3);

                Assert.False(File.Exists(current));
                Assert.Equal("rotated line", File.ReadAllText(Path.Combine(directory, "akron-1.log")));

                // The reader keeps working on the file it opened, which is what makes the backup complete.
                using StreamReader reader = new StreamReader(backupRead, Encoding.UTF8);
                Assert.Equal("rotated line", reader.ReadToEnd());
            }
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RotationShiftsRetainedFilesAndDropsTheOldest() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-log-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            File.WriteAllText(Path.Combine(directory, "akron-current.log"), "newest");
            File.WriteAllText(Path.Combine(directory, "akron-1.log"), "older");
            File.WriteAllText(Path.Combine(directory, "akron-2.log"), "oldest");

            AkronLog.RotateLogFiles(directory, retainedFiles: 2);

            Assert.False(File.Exists(Path.Combine(directory, "akron-current.log")));
            Assert.Equal("newest", File.ReadAllText(Path.Combine(directory, "akron-1.log")));
            Assert.Equal("older", File.ReadAllText(Path.Combine(directory, "akron-2.log")));

            // The oldest is dropped rather than promoted, so nothing may exist past the retention count.
            // Shifting akron-2.log up instead of deleting it would leave an akron-3.log here.
            Assert.Equal(
                new[] { "akron-1.log", "akron-2.log" },
                Directory.EnumerateFiles(directory).Select(Path.GetFileName).OrderBy(name => name).ToArray());
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ReadLogSource() {
        return File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../Source/Core/akron-log.cs"));
    }
}
