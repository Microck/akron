using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ReplayActiveSegmentPruningTests : IDisposable {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "akron-prune-segment-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime cutoff = new(2026, 10, 16, 20, 31, 0, DateTimeKind.Utc);
    public ReplayActiveSegmentPruningTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private FileInfo Segment(string name, int seconds) {
        string path = Path.Combine(directory, name + ".mkv");
        File.WriteAllText(path, string.Empty);
        File.SetLastWriteTimeUtc(path, cutoff.AddSeconds(seconds));
        return new FileInfo(path);
    }

    [Fact]
    public void SoleStaleOpenSegmentIsPreserved() {
        FileInfo active = Segment("segment-000000", -100);
        Assert.Empty(AkronInternalRecorder.SelectReplaySegmentsToPrune(new[] { active }, cutoff));
        Assert.True(active.Exists);
    }

    [Fact]
    public void OldClosedSegmentsPruneButNewestAndRecentSegmentsStay() {
        FileInfo old = Segment("segment-000001", -10);
        FileInfo recent = Segment("segment-000002", 1);
        FileInfo active = Segment("segment-000003", -100);
        Assert.Equal(new[] { old.FullName }, AkronInternalRecorder.SelectReplaySegmentsToPrune(new[] { active, old, recent }, cutoff).Select(f => f.FullName));
    }

    [Fact]
    public void ActiveProtectionAdvancesAfterNewSegmentAppears() {
        FileInfo first = Segment("segment-000000", -20);
        Assert.Empty(AkronInternalRecorder.SelectReplaySegmentsToPrune(new[] { first }, cutoff));
        FileInfo next = Segment("segment-000001", -10);
        Assert.Equal(first.FullName, Assert.Single(AkronInternalRecorder.SelectReplaySegmentsToPrune(new[] { first, next }, cutoff)).FullName);
    }

    [Fact]
    public void SequenceIsNumericEvenBeyondSixDigitWidth() {
        FileInfo old = Segment("segment-999999", -10);
        FileInfo active = Segment("segment-1000000", -100);
        Assert.Equal(old.FullName, Assert.Single(AkronInternalRecorder.SelectReplaySegmentsToPrune(new[] { active, old }, cutoff)).FullName);
    }

    [Fact]
    public void CutoffBoundaryAndUnknownNamesAreConservative() {
        FileInfo boundary = Segment("segment-000000", 0);
        FileInfo active = Segment("segment-000001", -100);
        FileInfo unknown = Segment("segment-unknown", -100);
        Assert.Empty(AkronInternalRecorder.SelectReplaySegmentsToPrune(new[] { boundary, active, unknown }, cutoff));
        Assert.Empty(AkronInternalRecorder.SelectReplaySegmentsToPrune(Array.Empty<FileInfo>(), cutoff));
    }

    [Fact]
    public void ProductionPruningUsesProtectedSelectorAfterPendingWindowRetention() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Recorder/akron-internal-recorder-replay.cs"));
        string prune = source.Split("private static void PruneReplaySegments")[1].Split("internal static FileInfo[] SelectReplaySegmentsToPrune")[0];
        Assert.Contains("foreach (PendingClipSave pending in PendingClipSaves)", prune);
        Assert.Contains("SelectReplaySegmentsToPrune(", prune);
        Assert.Contains("TryDeleteFile(file.FullName)", prune);
    }
}
