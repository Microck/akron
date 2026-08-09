using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Celeste.Mod.Akron;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

// The capturing half of the two-process round trip below.
//
// A test assembly built with Microsoft.NET.Test.Sdk is an executable whose
// generated entry point does nothing, and GenerateProgramFile is off in the
// csproj so this one takes its place. The test host never calls it - vstest
// loads the assembly and drives xunit directly - so it runs only when
// AkronStartPosHashIndexTests starts `dotnet exec` on this same dll.
public static class Program {
    public const string CaptureHashDocumentCommand = "capture-hash-document";

    public static int Main(string[] args) {
        if (args.Length != 2 || !string.Equals(args[0], CaptureHashDocumentCommand, StringComparison.Ordinal)) {
            Console.Error.WriteLine("usage: " + CaptureHashDocumentCommand + " <document-path>");
            return 2;
        }
        return AkronStartPosHashIndexTests.WriteCapturedHashDocument(args[1]);
    }
}

public class AkronStartPosHashIndexTests {
    // A mod comparer that hashes a string itself. string.GetHashCode is
    // randomized per process, so this breaks a restored set exactly the way a
    // culture-aware comparer does with no CompareInfo anywhere in the path.
    private sealed class SelfHashingOrdinalComparer : IEqualityComparer<string> {
        public bool Equals(string? left, string? right) {
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        public int GetHashCode(string value) {
            return value.GetHashCode();
        }
    }

    private sealed class HashIndexSession : EverestModuleSession {
        public HashSet<string> VisitedRooms = null!;
        public Dictionary<string, int> BerriesByRoom = null!;
        public ConcurrentDictionary<string, int> DeathsByRoom = null!;
        public HashSet<string> ModHashedRooms = null!;
    }

    private static HashIndexSession BuildSession(bool populated) {
        // A culture-aware comparer hashes through a seed this process picked at
        // start-up, so the numbers a capture writes are meaningless in the
        // process that reloads the slot.
        StringComparer collation =
            StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), ignoreCase: false);
        HashIndexSession session = new HashIndexSession {
            VisitedRooms = new HashSet<string>(collation),
            BerriesByRoom = new Dictionary<string, int>(collation),
            DeathsByRoom = new ConcurrentDictionary<string, int>(collation),
            ModHashedRooms = new HashSet<string>(new SelfHashingOrdinalComparer())
        };
        if (populated) {
            session.VisitedRooms.Add("summit-a");
            session.VisitedRooms.Add("summit-b");
            session.BerriesByRoom["summit-a"] = 1;
            session.BerriesByRoom["summit-b"] = 2;
            session.DeathsByRoom["summit-a"] = 3;
            session.DeathsByRoom["summit-b"] = 4;
            session.ModHashedRooms.Add("summit-a");
            session.ModHashedRooms.Add("summit-b");
        }
        return session;
    }

    private static AkronPersistentRuntimeState BuildState(bool populated) {
        AkronPersistentRuntimeState state = new AkronPersistentRuntimeState();
        state.ModuleSessions["helper"] = BuildSession(populated);
        return state;
    }

    private static AkronReconstructionGraph CreateStartPosGraph() {
        return new AkronReconstructionGraph(
            AkronStartPosReconstruction.IsLiveResourceType,
            AkronStartPosReconstruction.GetLiveResourceKey,
            null,
            AkronStartPosReconstruction.ResolveDetachedLiveResource,
            areEquivalentLiveResources: AkronStartPosReconstruction.AreEquivalentLiveResources);
    }

    internal static int WriteCapturedHashDocument(string path) {
        AkronReconstructionGraph graph = CreateStartPosGraph();
        AkronReconstructionCapture capture = graph.Capture(BuildState(true), BuildState(false));
        if (!capture.Success) {
            Console.Error.WriteLine(capture.Error);
            return 1;
        }
        File.WriteAllText(path, graph.Serialize(capture.Document));
        return 0;
    }

    private static string CaptureHashDocumentInAnotherProcess() {
        string path = Path.Combine(
            Path.GetTempPath(),
            "akron-hash-index-" + Guid.NewGuid().ToString("N") + ".json");
        ProcessStartInfo start = new ProcessStartInfo {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                       ?? Environment.ProcessPath
                       ?? "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Akron.Tests.dll"));
        start.ArgumentList.Add(Program.CaptureHashDocumentCommand);
        start.ArgumentList.Add(path);

        using Process child = Process.Start(start) ?? throw new InvalidOperationException("no child process");
        string output = child.StandardOutput.ReadToEnd();
        string error = child.StandardError.ReadToEnd();
        Assert.True(child.WaitForExit(120000), "the capturing process did not finish");
        Assert.True(child.ExitCode == 0, "capture process failed: " + error + output);
        try {
            return File.ReadAllText(path);
        } finally {
            File.Delete(path);
        }
    }

    private static object GetField(object owner, string name) {
        return owner.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(owner)!;
    }

    // The one test that cannot be faked by doing both halves here. A hash
    // container stores one hash code per entry and looks entries up by that
    // number, and every hash function in this session is seeded by the process:
    // the culture-aware comparer and the mod comparer both hash a string through
    // a seed picked at start-up. So the document below carries the capturing
    // process's numbers into this one, which is exactly what a chapter change
    // does to a StartPos slot.
    //
    // Before the index was re-derived, all four containers came back holding
    // every entry, reporting the right Count, and answering false to Contains
    // for their own items - a restore that reported success and was wrong. A
    // version of this test that captured and restored in one process passed
    // without the fix, because the seed matched.
    [Fact]
    public void HashContainersCapturedInAnotherProcessFindTheirOwnEntriesAfterARestore() {
        string serialized = CaptureHashDocumentInAnotherProcess();
        AkronReconstructionGraph graph = CreateStartPosGraph();
        AkronReconstructionDocument document = graph.Deserialize(serialized);
        AkronPersistentRuntimeState fresh = BuildState(false);

        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.ErrorPath + ": " + restore.Error);
        AkronReconstructionVerification verification =
            graph.Verify(document, restore, Array.Empty<string>());
        Assert.True(verification.Success, verification.ErrorPath + ": " + verification.Error);

        HashIndexSession restored = Assert.IsType<HashIndexSession>(fresh.ModuleSessions["helper"]);
        Assert.Equal(2, restored.VisitedRooms.Count);
        Assert.True(restored.VisitedRooms.Contains("summit-a"), "culture-aware HashSet lost summit-a");
        Assert.True(restored.VisitedRooms.Contains("summit-b"), "culture-aware HashSet lost summit-b");
        Assert.False(restored.VisitedRooms.Contains("summit-c"));
        Assert.True(restored.BerriesByRoom.ContainsKey("summit-a"), "culture-aware Dictionary lost summit-a");
        Assert.Equal(2, restored.BerriesByRoom["summit-b"]);
        Assert.True(restored.DeathsByRoom.ContainsKey("summit-a"), "ConcurrentDictionary lost summit-a");
        Assert.Equal(4, restored.DeathsByRoom["summit-b"]);
        Assert.True(restored.ModHashedRooms.Contains("summit-a"), "mod-comparer HashSet lost summit-a");
        Assert.True(restored.ModHashedRooms.Contains("summit-b"), "mod-comparer HashSet lost summit-b");
    }

    // Re-deriving the index is allowed to move the index and nothing else. A
    // container the saved frame had removed something from keeps its free slot,
    // its _count and its entry order, so the restored container enumerates in
    // the same order the saved one did and a later add still lands in the same
    // place. Clearing the container and adding its contents back would compact
    // all three away, which is why that is not how this works.
    [Fact]
    public void RebuildingTheIndexLeavesRemovedSlotsAndEntryOrderWhereTheDocumentPutThem() {
        StringComparer collation =
            StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), ignoreCase: false);
        HashSet<string> saved = new HashSet<string>(collation) { "summit-a", "summit-b", "summit-c" };
        saved.Remove("summit-b");
        AkronPersistentRuntimeState savedState = new AkronPersistentRuntimeState();
        savedState.ModuleSessions["helper"] = new HashIndexSession {
            VisitedRooms = saved,
            BerriesByRoom = new Dictionary<string, int>(collation),
            DeathsByRoom = new ConcurrentDictionary<string, int>(collation),
            ModHashedRooms = new HashSet<string>(new SelfHashingOrdinalComparer())
        };
        string[] savedOrder = saved.ToArray();
        int savedCount = (int) GetField(saved, "_count");
        int savedFreeList = (int) GetField(saved, "_freeList");
        int savedFreeCount = (int) GetField(saved, "_freeCount");
        AkronReconstructionGraph graph = CreateStartPosGraph();

        AkronReconstructionCapture capture = graph.Capture(savedState, BuildState(false));

        Assert.True(capture.Success, capture.Error);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        AkronPersistentRuntimeState fresh = BuildState(false);

        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.ErrorPath + ": " + restore.Error);
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
        HashSet<string> restored =
            Assert.IsType<HashIndexSession>(fresh.ModuleSessions["helper"]).VisitedRooms;
        Assert.Equal(savedOrder, restored.ToArray());
        Assert.Equal(savedCount, (int) GetField(restored, "_count"));
        Assert.Equal(savedFreeList, (int) GetField(restored, "_freeList"));
        Assert.Equal(savedFreeCount, (int) GetField(restored, "_freeCount"));
    }

    // The verification exclusion has to stay the size it is. Everything the
    // rebuild writes is derived from the keys, the comparer and the bucket
    // count; everything that decides what the container holds keeps being
    // compared against the document.
    [Fact]
    public void OnlyTheFieldsTheRebuildWritesAreExcludedFromVerification() {
        Type setEntry = typeof(HashSet<string>).GetNestedType("Entry", BindingFlags.NonPublic)!
            .MakeGenericType(typeof(string));
        Type mapEntry = typeof(Dictionary<string, int>).GetNestedType("Entry", BindingFlags.NonPublic)!
            .MakeGenericType(typeof(string), typeof(int));
        Type tables = typeof(ConcurrentDictionary<string, int>).GetNestedType("Tables", BindingFlags.NonPublic)!
            .MakeGenericType(typeof(string), typeof(int));
        Type node = typeof(ConcurrentDictionary<string, int>).GetNestedType("Node", BindingFlags.NonPublic)!
            .MakeGenericType(typeof(string), typeof(int));

        // The two arrays whose contents move. The field that points at each and
        // the array's length are still compared; only the positions inside are
        // skipped, which is why these are a separate predicate.
        Assert.True(AkronHashIndex.IsDerivedIndexArrayField(typeof(HashSet<string>), "_buckets"));
        Assert.True(AkronHashIndex.IsDerivedIndexArrayField(typeof(Dictionary<string, int>), "_buckets"));
        Assert.True(AkronHashIndex.IsDerivedIndexArrayField(tables, "_buckets"));
        Assert.True(AkronHashIndex.IsDerivedIndexArrayField(tables, "_countPerLock"));
        Assert.False(AkronHashIndex.IsDerivedIndexField(typeof(HashSet<string>), "_buckets"));
        Assert.False(AkronHashIndex.IsDerivedIndexField(typeof(Dictionary<string, int>), "_buckets"));
        Assert.False(AkronHashIndex.IsDerivedIndexArrayField(tables, "_locks"));
        Assert.False(AkronHashIndex.IsDerivedIndexArrayField(typeof(List<string>), "_buckets"));

        Assert.True(AkronHashIndex.IsDerivedIndexField(setEntry, "HashCode"));
        Assert.True(AkronHashIndex.IsDerivedIndexField(setEntry, "Next"));
        Assert.True(AkronHashIndex.IsDerivedIndexField(mapEntry, "hashCode"));
        Assert.True(AkronHashIndex.IsDerivedIndexField(mapEntry, "next"));
        Assert.True(AkronHashIndex.IsDerivedIndexField(node, "_hashcode"));
        Assert.True(AkronHashIndex.IsDerivedIndexField(node, "_next"));

        // What decides the contents is still verified.
        foreach (string kept in new[] { "_entries", "_count", "_freeList", "_freeCount", "_comparer", "_version" }) {
            Assert.False(AkronHashIndex.IsDerivedIndexField(typeof(HashSet<string>), kept), kept);
            Assert.False(AkronHashIndex.IsDerivedIndexField(typeof(Dictionary<string, int>), kept), kept);
        }
        Assert.False(AkronHashIndex.IsDerivedIndexField(setEntry, "Value"));
        Assert.False(AkronHashIndex.IsDerivedIndexField(mapEntry, "key"));
        Assert.False(AkronHashIndex.IsDerivedIndexField(mapEntry, "value"));
        Assert.False(AkronHashIndex.IsDerivedIndexField(node, "_key"));
        Assert.False(AkronHashIndex.IsDerivedIndexField(node, "_value"));
        Assert.False(AkronHashIndex.IsDerivedIndexField(tables, "_locks"));
        Assert.False(AkronHashIndex.IsDerivedIndexField(tables, "_comparer"));

        // A free slot keeps the hash and the chain link the document holds,
        // because the rebuild steps over it and the free chain runs through
        // next. Verification uses this to keep comparing those slots.
        HashSet<string> holed = new HashSet<string> { "a", "b", "c" };
        holed.Remove("b");
        Array entries = (Array) GetField(holed, "_entries");
        Assert.True(AkronHashIndex.IsLiveHashEntry(entries.GetValue(0)!));
        Assert.False(AkronHashIndex.IsLiveHashEntry(entries.GetValue(1)!));
        Assert.True(AkronHashIndex.IsLiveHashEntry(entries.GetValue(2)!));
        Assert.True(AkronHashIndex.IsHashEntryType(setEntry));
        Assert.True(AkronHashIndex.IsHashEntryType(mapEntry));
        Assert.False(AkronHashIndex.IsHashEntryType(node));
        Assert.False(AkronHashIndex.IsHashEntryType(typeof(HashSet<string>)));

        // The names are generic enough to catch an unrelated type by accident.
        Assert.False(AkronHashIndex.IsDerivedIndexArrayField(typeof(SortedList<string, int>), "_buckets"));
        Assert.False(AkronHashIndex.IsDerivedIndexArrayField(typeof(System.Collections.Hashtable), "_buckets"));
        Assert.False(AkronHashIndex.IsDerivedIndexField(typeof(AkronStartPosHashIndexTests), "next"));
    }
}
