using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Celeste.Mod.Akron;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[CollectionDefinition("Performance", DisableParallelization = true)]
public sealed class PerformanceTestCollection {
}

[Collection("Performance")]
public sealed class PerformanceTests {
    private const int FeatureClassificationIterations = 20_000;
    private const int UiLabelClassificationIterations = 10_000;
    private const int ContributorScanIterations = 25_000;

    // Two of the guards below used to be wall-clock budgets in milliseconds,
    // and a wall-clock budget measures the machine rather than the code. On a
    // two-core box under load the same loops ran four to eight times slower
    // than on a quiet one, which failed the run without anything having
    // regressed. Both now compare the measured loop against a reference loop
    // of the same shape measured in the same process, so the machine cancels
    // out. Both guards ended up tighter than the budgets they replace: the
    // budgets allowed a seven to eighteen times regression before failing, and
    // the ratios below allow three to five.
    //
    // Each guard measures the two loops back to back several times and uses the
    // median ratio. That rejects one noisy sample without letting one unusually
    // favorable sample hide a consistent regression.
    private const int MeasurementRepetitions = 5;

    // Classifying a UI label is one dictionary probe over 237 entries. It
    // reads at about twice the reference today, and a scan over those entries
    // would read at about a hundred times it.
    private const double UiLabelClassificationBudgetRatio = 5;

    // The contributor scan is a fixed sequence of option checks that appends
    // to a list, so building its own answer is the floor of what it can cost.
    // The scan reads at about twice that floor; six leaves room for contention
    // while still catching per-option work that does not belong behind a
    // boolean and a lookup.
    private const double ContributorScanBudgetRatio = 6;

    [Fact]
    public void FeatureClassificationStaysConstantTimeForAllFeatures() {
        AkronFeatureKind[] features = Enum.GetValues<AkronFeatureKind>();

        for (int i = 0; i < 1_000; i++) {
            foreach (AkronFeatureKind feature in features) {
                AkronFeatureRegistry.Classify(feature);
            }
        }

        TimeSpan elapsed = Measure(() => {
            int checksum = 0;
            for (int i = 0; i < FeatureClassificationIterations; i++) {
                foreach (AkronFeatureKind feature in features) {
                    checksum += (int) AkronFeatureRegistry.Classify(feature);
                }
            }

            Assert.True(checksum > 0);
        });

        // This one keeps its wall-clock budget. Classifying every feature is an
        // array index, and the loop runs in about 10 ms against a 400 ms
        // budget even on a loaded two-core box, so there is no contention
        // reading that comes near it.
        Assert.True(
            elapsed < TimeSpan.FromMilliseconds(400),
            $"Classifying every feature {FeatureClassificationIterations} times took {elapsed.TotalMilliseconds:0.0}ms.");
    }

    [Fact]
    public void UiLabelClassificationStaysConstantTimeForOverlayRows() {
        string[] labels = {
            "Safe Mode",
            "Pause Buffering",
            "Death Stats",
            "Input History",
            "Stamina Bar",
            "Dash Number",
            "Reduced Visual Noise",
            "Fix Hitbox Pixels",
            "Show Hitboxes On Death",
            "Room Timer",
            "Extended Variants Master",
            "Submission Mode",
            "Proof Recorder Guard",
            "Lag Pauser",
            "Journal Snapshot / Compare",
            // Recorder rows are included in the lookup set, but this is only a
            // dictionary classification guard rather than a runtime recording budget.
            "Start Recording",
            "Stop Recording",
            "Build Clear Video"
        };

        // The reference: the same loop over the same labels against a plain
        // dictionary with the comparer the registry uses. That is what a
        // constant-time classification costs, so the registry has to stay
        // within a small multiple of it.
        Dictionary<string, int> referenceLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < labels.Length; i++) {
            referenceLookup[labels[i]] = i;
        }

        for (int i = 0; i < 1_000; i++) {
            foreach (string label in labels) {
                AkronFeatureRegistry.TryClassifyUiLabel(label, out _);
                referenceLookup.TryGetValue(label, out _);
            }
        }

        List<(double Ratio, double ClassificationMs, double ReferenceMs)> samples =
            new List<(double Ratio, double ClassificationMs, double ReferenceMs)>();
        for (int repetition = 0; repetition < MeasurementRepetitions; repetition++) {
            // Back to back, so both loops meet the same machine and the ratio
            // is of one moment rather than of two.
            TimeSpan classificationSample = Measure(() => {
                int classified = 0;
                for (int i = 0; i < UiLabelClassificationIterations; i++) {
                    foreach (string label in labels) {
                        if (AkronFeatureRegistry.TryClassifyUiLabel(label, out AkronStatus status)) {
                            classified += (int) status + 1;
                        }
                    }
                }

                Assert.True(classified > labels.Length);
            });
            TimeSpan referenceSample = Measure(() => {
                int found = 0;
                for (int i = 0; i < UiLabelClassificationIterations; i++) {
                    foreach (string label in labels) {
                        if (referenceLookup.TryGetValue(label, out int index)) {
                            found += index + 1;
                        }
                    }
                }

                Assert.True(found > labels.Length);
            });

            samples.Add((
                classificationSample.TotalMilliseconds / referenceSample.TotalMilliseconds,
                classificationSample.TotalMilliseconds,
                referenceSample.TotalMilliseconds));
        }

        (double ratio, double classificationMs, double referenceMs) =
            samples.OrderBy(sample => sample.Ratio).ElementAt(samples.Count / 2);

        Assert.True(
            ratio <= UiLabelClassificationBudgetRatio,
            $"Classifying overlay labels {UiLabelClassificationIterations} times took " +
            $"{classificationMs:0.0}ms against {referenceMs:0.0}ms for the same number of plain " +
            $"dictionary lookups, a median ratio of {ratio:0.00}.");
    }

    [Fact]
    public void ActiveCheatContributorScanStaysCheapWithManyEnabledOptions() {
        AkronModuleSettings settings = new AkronModuleSettings {
            AutoKill = true,
            CursorZoom = true,
            ClickTeleport = true,
            Noclip = true,
            NoclipAccuracy = true,
            FreeCamera = true,
            FpsBypass = true,
            TpsBypass = true,
            Invincibility = true,
            JumpHack = true,
            ResourceBars = true,
            StaminaBar = true,
            DashBar = true,
            DashNumber = true,
            SpeedNumber = true,
            SafeModeFreezeAttempts = true,
            SafeModeFreezeJumps = true,
            SafeModeFreezeBestRun = true,
            TransitionSpeedMultiplier = 0.5f,
            FreezeTimerWhilePaused = true,
            NoFreezeFrames = true,
            GroundRefillRules = true,
            DashRedirectEnabled = true,
            InfiniteDash = true,
            InfiniteStamina = true,
            DashCountOverride = true,
            DeloadSpinners = true,
            PauseCountdown = true,
            HitboxViewer = true,
            ShowTriggers = true,
            EntityInspector = true,
            ShowTrajectory = true
        };
        AkronModuleSession session = new AkronModuleSession {
            FreezeGameplay = true,
            TimescaleEnabled = true,
            TimescaleMultiplier = 0.5f
        };

        // The reference: build the answer the scan produces, the same number
        // of times, allocating what the scan allocates - the list, one
        // contributor per entry, and its disable command string. Every scan
        // pays that, so it is the floor the scan cannot go below, and anything
        // it spends per option beyond a boolean and a lookup shows up as a
        // multiple of the floor.
        IReadOnlyList<AkronActiveCheatContributor> answer = AkronPolicy.GetActiveCheatContributors(settings, session);
        Assert.NotEmpty(answer);

        for (int i = 0; i < 1_000; i++) {
            AkronPolicy.GetActiveCheatContributors(settings, session);
        }

        List<(double Ratio, double ScanMs, double ReferenceMs)> samples =
            new List<(double Ratio, double ScanMs, double ReferenceMs)>();
        for (int repetition = 0; repetition < MeasurementRepetitions; repetition++) {
            TimeSpan scanSample = Measure(() => {
                int contributorCount = 0;
                for (int i = 0; i < ContributorScanIterations; i++) {
                    IReadOnlyList<AkronActiveCheatContributor> contributors = AkronPolicy.GetActiveCheatContributors(settings, session);
                    contributorCount += contributors.Count;
                }

                Assert.True(contributorCount > ContributorScanIterations);
            });
            TimeSpan referenceSample = Measure(() => {
                int contributorCount = 0;
                for (int i = 0; i < ContributorScanIterations; i++) {
                    List<AkronActiveCheatContributor> materialized = new List<AkronActiveCheatContributor>();
                    for (int contributor = 0; contributor < answer.Count; contributor++) {
                        materialized.Add(new AkronActiveCheatContributor(
                            answer[contributor].Label,
                            "Turn off " + answer[contributor].Label,
                            answer[contributor].Feature));
                    }

                    contributorCount += materialized.Count;
                }

                Assert.True(contributorCount > ContributorScanIterations);
            });

            samples.Add((
                scanSample.TotalMilliseconds / referenceSample.TotalMilliseconds,
                scanSample.TotalMilliseconds,
                referenceSample.TotalMilliseconds));
        }

        (double ratio, double scanMs, double referenceMs) =
            samples.OrderBy(sample => sample.Ratio).ElementAt(samples.Count / 2);

        Assert.True(
            ratio <= ContributorScanBudgetRatio,
            $"Scanning active cheat contributors {ContributorScanIterations} times took " +
            $"{scanMs:0.0}ms against {referenceMs:0.0}ms for building the same answer list as many " +
            $"times, a median ratio of {ratio:0.00}.");
    }

    [Fact]
    public void PerfRecordingCollisionKeepsTheExistingFile() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-perf-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string preferredPath = Path.Combine(directory, "akron-perf-20260821-210000-run.jsonl");
        try {
            File.WriteAllText(preferredPath, "existing recording");

            using (StreamWriter writer = AkronPerformanceTelemetry.OpenRecordWriter(preferredPath, out string actualPath)) {
                writer.Write("new recording");
                writer.Flush();

                Assert.NotEqual(preferredPath, actualPath);
                Assert.StartsWith(
                    Path.Combine(directory, "akron-perf-20260821-210000-run-"),
                    actualPath,
                    StringComparison.Ordinal);
                Assert.EndsWith(".jsonl", actualPath, StringComparison.Ordinal);
                Assert.Equal("new recording", File.ReadAllText(actualPath));
            }

            Assert.Equal("existing recording", File.ReadAllText(preferredPath));
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RecordingWindowStartsCleanAndFlushesBeforeTheWriterIsDetached() {
        string source = File.ReadAllText(GetPerformanceTelemetrySourcePath());
        int start = source.IndexOf("public static bool StartRecording", StringComparison.Ordinal);
        int startEnd = source.IndexOf("internal static StreamWriter OpenRecordWriter", start, StringComparison.Ordinal);
        string startBody = source.Substring(start, startEnd - start);
        int stopPrevious = startBody.IndexOf("StopRecording();", StringComparison.Ordinal);
        int reset = startBody.IndexOf("Reset();", stopPrevious, StringComparison.Ordinal);
        int open = startBody.IndexOf("recordWriter = OpenRecordWriter", reset, StringComparison.Ordinal);

        int stop = source.IndexOf("public static void StopRecording", StringComparison.Ordinal);
        int stopEnd = source.IndexOf("public static bool GcEventsEnabled", stop, StringComparison.Ordinal);
        string stopBody = source.Substring(stop, stopEnd - stop);
        int partialWindow = stopBody.IndexOf("recordWriter != null && windowFrames > 0", StringComparison.Ordinal);
        int flush = stopBody.IndexOf("FlushWindow();", partialWindow, StringComparison.Ordinal);
        int detach = stopBody.IndexOf("StreamWriter writer = recordWriter;", flush, StringComparison.Ordinal);

        Assert.True(stopPrevious >= 0 && reset > stopPrevious && open > reset);
        Assert.True(partialWindow >= 0 && flush > partialWindow && detach > flush);
    }

    [Fact]
    public void StopRecordingContainsAWriterFailureAndDisarmsTheRecorder() {
        FieldInfo writerField = typeof(AkronPerformanceTelemetry).GetField(
            "recordWriter",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        StreamWriter failedWriter = new StreamWriter(new MemoryStream());
        failedWriter.Dispose();
        AkronPerformanceTelemetry.Reset();
        writerField.SetValue(null, failedWriter);
        try {
            Exception exception = Record.Exception(AkronPerformanceTelemetry.StopRecording);

            Assert.Null(exception);
            Assert.False(AkronPerformanceTelemetry.IsRecording);
        } finally {
            writerField.SetValue(null, null);
            AkronPerformanceTelemetry.StopRecording();
        }
    }

    private static string GetPerformanceTelemetrySourcePath() {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory)) {
            string candidate = Path.Combine(directory, "Source", "Core", "akron-performance-telemetry.cs");
            if (File.Exists(candidate)) {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException("Could not locate Source/Core/akron-performance-telemetry.cs.");
    }

    private static TimeSpan Measure(Action action) {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Stopwatch stopwatch = Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }
}
