using System;
using System.IO;
using System.Linq;
using Xunit;
namespace Celeste.Mod.Akron.Tests;

public sealed class RecordingSourceIdentityTests {
    [Theory]
    [InlineData("area-clear")]
    [InlineData("checkpoint-clear")]
    [InlineData("room-clear")]
    public void DelayedMetadataAndFilenameKeepTheEventIdentity(string kind) {
        string directory = Path.Combine(Path.GetTempPath(), "akron-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var origin = new AkronInternalRecorder.RecordingIdentity("Celeste/0-Intro", "3", 2, 18);
            var laterScene = new AkronInternalRecorder.RecordingIdentity("Overworld", "Overworld", 9, 99);
            DateTime time = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
            string name = AkronInternalRecorder.ExpandFilenameIdentity("{chapter}-{room}-{death}-{attempt}", origin, time);
            Assert.Equal("Celeste-0-Intro-3-2-18", name);
            Assert.DoesNotContain(laterScene.Chapter, name);
            string path = Path.Combine(directory, name + ".mp4");
            File.WriteAllBytes(path, new byte[] { 1 });
            AkronInternalRecorder.WriteClipSidecar(path, kind, AkronRecordingContainerFormat.Mp4, time, time.AddSeconds(5), origin);
            var settings = new AkronModuleSettings { RecordingOutputFolder = directory };
            var clip = Assert.Single(AkronInternalRecorder.SelectCompletionClipsForTesting(settings, "Celeste-0-Intro"));
            Assert.Equal("3", clip.Room);
            Assert.Empty(AkronInternalRecorder.SelectCompletionClipsForTesting(settings, "Overworld"));
        } finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ManualMetadataUsesStartIdentityWithoutAStopScene() {
        string path = Path.Combine(Path.GetTempPath(), "akron-manual-" + Guid.NewGuid().ToString("N") + ".mkv");
        try {
            var origin = new AkronInternalRecorder.RecordingIdentity("Celeste/0-Intro", "2", 0, 1);
            AkronInternalRecorder.WriteClipSidecar(path, "manual-recording", AkronRecordingContainerFormat.Mkv, DateTime.UtcNow, DateTime.UtcNow, origin);
            string[] lines = File.ReadAllLines(path + ".akrclip");
            Assert.Contains("chapter=Celeste-0-Intro", lines);
            Assert.Contains("room=2", lines);
            Assert.DoesNotContain("chapter=recording", lines);
        } finally { File.Delete(path + ".akrclip"); }
    }
    [Fact]
    public void ProductionPathsCaptureBeforeQueueingAndNeverLookUpThePublicationScene() {
        string root = Path.Combine(AppContext.BaseDirectory, "../../../../Source/Recorder");
        string manual = File.ReadAllText(Path.Combine(root, "akron-internal-recorder.cs"));
        Assert.Contains("RecordingIdentity identity = CaptureRecordingIdentity(level);", manual);
        Assert.Contains("activeIdentity = identity;", manual);
        Assert.Contains("completedIdentity = activeIdentity;", manual);
        Assert.Contains("activeIdentity = null;", manual);
        Assert.Contains("startedUtc, endedUtc, completedIdentity);", manual);
        Assert.DoesNotContain("startedUtc, endedUtc, Engine.Scene);", manual);
        string replay = File.ReadAllText(Path.Combine(root, "akron-internal-recorder-replay.cs"));
        Assert.Equal(7, replay.Split("QueueClipSave(level,").Length - 1);
        Assert.Contains("eventUtc.AddSeconds(postRoll), CaptureRecordingIdentity(level)", replay);
        Assert.Contains("endUtc, endUtc, CaptureRecordingIdentity(scene)", replay);
        string export = File.ReadAllText(Path.Combine(root, "akron-internal-recorder-replay-export.cs"));
        Assert.Contains("BuildOutputPath(request.Identity,", export);
        Assert.Contains("SanitizeToken(request.Identity.Chapter)", export);
        Assert.Contains("SanitizeToken(request.Identity.Room)", export);
        Assert.DoesNotContain("(scene as Level)?.Session", export);
    }

    [Fact]
    public void SeparateQueuedOriginsAndRestartedRecordingHaveIndependentSnapshots() {
        var first = new AkronInternalRecorder.RecordingIdentity("First", "room-a", 1, 2);
        var second = new AkronInternalRecorder.RecordingIdentity("Second", "room-b", 3, 4);
        var copied = first with { Room = "later-room" };
        Assert.Equal("First-room-a-1-2", AkronInternalRecorder.ExpandFilenameIdentity("{chapter}-{room}-{death}-{attempt}", first, DateTime.UtcNow));
        Assert.Equal("Second-room-b-3-4", AkronInternalRecorder.ExpandFilenameIdentity("{chapter}-{room}-{death}-{attempt}", second, DateTime.UtcNow));
        Assert.Equal("later-room", copied.Room);
    }
}
