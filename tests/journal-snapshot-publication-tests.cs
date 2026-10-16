using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class JournalSnapshotPublicationTests : IDisposable {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "akron-journal-test-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime timestamp = new(2026, 10, 16, 18, 54, 41, DateTimeKind.Utc);

    public JournalSnapshotPublicationTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void SameSecondSnapshotsPreserveEveryPayload() {
        string first = AkronActions.WriteUniqueJournalSnapshot(directory, "first", timestamp);
        string second = AkronActions.WriteUniqueJournalSnapshot(directory, "second", timestamp);
        string third = AkronActions.WriteUniqueJournalSnapshot(directory, "third", timestamp);
        Assert.Equal("akron-journal-20261016-185441.json", Path.GetFileName(first));
        Assert.Equal("akron-journal-20261016-185441-1.json", Path.GetFileName(second));
        Assert.Equal("akron-journal-20261016-185441-2.json", Path.GetFileName(third));
        Assert.Equal("first", File.ReadAllText(first));
        Assert.Equal("second", File.ReadAllText(second));
        Assert.Equal("third", File.ReadAllText(third));
    }

    [Fact]
    public void ExistingBaseAndSuffixAreNotOverwritten() {
        File.WriteAllText(Path.Combine(directory, "akron-journal-20261016-185441.json"), "old");
        File.WriteAllText(Path.Combine(directory, "akron-journal-20261016-185441-1.json"), "old suffix");
        string result = AkronActions.WriteUniqueJournalSnapshot(directory, "new", timestamp);
        Assert.EndsWith("-2.json", result);
        Assert.Equal(new[] { "new", "old", "old suffix" }, Directory.GetFiles(directory).Select(File.ReadAllText).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void DifferentSecondsKeepExistingNaming() {
        string first = AkronActions.WriteUniqueJournalSnapshot(directory, "first", timestamp);
        string second = AkronActions.WriteUniqueJournalSnapshot(directory, "second", timestamp.AddSeconds(1));
        Assert.EndsWith("185441.json", first);
        Assert.EndsWith("185442.json", second);
        Assert.Equal(2, Directory.GetFiles(directory, "akron-journal-*.json").Length);
    }

    [Fact]
    public void ConcurrentReservationsKeepAllSnapshots() {
        Parallel.For(0, 20, i => AkronActions.WriteUniqueJournalSnapshot(directory, i.ToString(), timestamp));
        string[] payloads = Directory.GetFiles(directory, "akron-journal-*.json").Select(File.ReadAllText).ToArray();
        Assert.Equal(20, payloads.Length);
        Assert.Equal(Enumerable.Range(0, 20).OrderBy(i => i), payloads.Select(int.Parse).OrderBy(i => i));
    }

    [Fact]
    public void MissingDirectoryErrorIsNotRetriedAsCollision() {
        Assert.Throws<DirectoryNotFoundException>(() => AkronActions.WriteUniqueJournalSnapshot(Path.Combine(directory, "missing"), "new", timestamp));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void SummaryRemainsUtf8() {
        string summary = "{\"room\":\"雪 montaña\"}";
        string path = AkronActions.WriteUniqueJournalSnapshot(directory, summary, timestamp);
        Assert.Equal(summary, File.ReadAllText(path));
    }

    [Fact]
    public void ActualActionSelectsPreviousBeforeReservingNewSnapshot() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Actions/akron-actions.cs"));
        string method = source.Split("public static void WriteJournalSnapshotCompare(Level level)")[1].Split("internal static string WriteUniqueJournalSnapshot")[0];
        Assert.True(method.IndexOf("FindLatestJournalSnapshot(directory)", StringComparison.Ordinal) < method.IndexOf("WriteUniqueJournalSnapshot(directory, summary, DateTime.UtcNow)", StringComparison.Ordinal));
        Assert.DoesNotContain("File.WriteAllText", method);
        Assert.Contains("LastJournalSnapshotPath = path", method);
    }
}
