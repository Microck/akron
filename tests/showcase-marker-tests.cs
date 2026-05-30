using System;
using System.IO;
using System.Linq;
using Celeste.Mod.Akron;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public class ShowcaseMarkerTests {
    [Fact]
    public void FormatOffsetUsesSyncAnchor() {
        DateTime sync = new DateTime(2026, 5, 30, 12, 0, 0, DateTimeKind.Utc);
        DateTime timestamp = sync.AddSeconds(83).AddMilliseconds(45);

        Assert.Equal("1:23.045", AkronShowcaseMarkers.FormatOffsetForTesting(timestamp, sync));
    }

    [Fact]
    public void ToggleMarkersWriteClosedIntervals() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-showcase-marker-tests-" + Guid.NewGuid().ToString("N"));
        DateTime now = new DateTime(2026, 5, 30, 12, 0, 0, DateTimeKind.Utc);
        try {
            AkronShowcaseMarkers.ResetForTesting(directory, () => now, true);
            AkronShowcaseMarkers.MarkSync("OBS_START");
            now = now.AddSeconds(2);
            AkronShowcaseMarkers.MarkTopLevelToggle("Show Hitboxes", true, AkronFeatureKind.HitboxViewer, "imgui");
            now = now.AddSeconds(5).AddMilliseconds(250);
            AkronShowcaseMarkers.MarkTopLevelToggle("Show Hitboxes", false, AkronFeatureKind.HitboxViewer, "imgui");

            string timeline = Directory.GetFiles(directory, "*-timeline.tsv").Single();
            string[] lines = File.ReadAllLines(timeline);

            Assert.Contains(lines, line => line.Contains("\tfeature_on\tShow Hitboxes\ttrue\tHitboxViewer\timgui\t"));
            Assert.Contains(lines, line => line.Contains("\tfeature_off\tShow Hitboxes\tfalse\tHitboxViewer\timgui\t"));
            Assert.Contains(lines, line => line.EndsWith("\tinterval\tShow Hitboxes\tclosed\tHitboxViewer\timgui\t0:05.250", StringComparison.Ordinal));
        } finally {
            AkronShowcaseMarkers.ResetForTesting(null, null, null);
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void PopupDetailsUseSeparateFile() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-showcase-marker-tests-" + Guid.NewGuid().ToString("N"));
        DateTime now = new DateTime(2026, 5, 30, 12, 0, 0, DateTimeKind.Utc);
        try {
            AkronShowcaseMarkers.ResetForTesting(directory, () => now, true);
            AkronShowcaseMarkers.MarkSync("OBS_START");
            now = now.AddSeconds(3);
            AkronShowcaseMarkers.MarkPopupDetail("Show Hitboxes", "Hazards", "checkbox", "false");

            string details = Directory.GetFiles(directory, "*-details.tsv").Single();
            string[] lines = File.ReadAllLines(details);

            Assert.Contains(lines, line => line.Contains("\tcheckbox\tShow Hitboxes / Hazards\tfalse\t"));
        } finally {
            AkronShowcaseMarkers.ResetForTesting(null, null, null);
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
