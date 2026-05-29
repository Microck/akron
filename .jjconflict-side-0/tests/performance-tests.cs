using System;
using System.Collections.Generic;
using System.Diagnostics;
using Celeste.Mod.Akron;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class PerformanceTests {
    private const int FeatureClassificationIterations = 20_000;
    private const int UiLabelClassificationIterations = 10_000;
    private const int ContributorScanIterations = 25_000;

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

        for (int i = 0; i < 1_000; i++) {
            foreach (string label in labels) {
                AkronFeatureRegistry.TryClassifyUiLabel(label, out _);
            }
        }

        TimeSpan elapsed = Measure(() => {
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

        Assert.True(
            elapsed < TimeSpan.FromMilliseconds(150),
            $"Classifying overlay labels {UiLabelClassificationIterations} times took {elapsed.TotalMilliseconds:0.0}ms.");
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
            PreventDownDashRedirectsEnabled = true,
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

        for (int i = 0; i < 1_000; i++) {
            AkronPolicy.GetActiveCheatContributors(settings, session);
        }

        TimeSpan elapsed = Measure(() => {
            int contributorCount = 0;
            for (int i = 0; i < ContributorScanIterations; i++) {
                IReadOnlyList<AkronActiveCheatContributor> contributors = AkronPolicy.GetActiveCheatContributors(settings, session);
                contributorCount += contributors.Count;
            }

            Assert.True(contributorCount > ContributorScanIterations);
        });

        Assert.True(
            elapsed < TimeSpan.FromMilliseconds(750),
            $"Scanning active cheat contributors {ContributorScanIterations} times took {elapsed.TotalMilliseconds:0.0}ms.");
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
