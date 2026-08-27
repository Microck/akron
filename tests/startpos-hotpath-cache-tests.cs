using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

// The StartPos HUD label, the label obstruction pass and the overlay rows all re-derive
// the StartPos list every rendered frame. These tests pin the caches that keep a SHA-256,
// a File.Exists and a map-wide room-order rebuild off that path, and pin every point where
// those caches have to be invalidated.
[Collection(AkronSharedStateCollection.Name)]
public sealed class StartPosHotPathCacheTests {
    private static string ActionsSource =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Actions/akron-startpos-actions.cs"));

    private static string SaveLoadSource =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/SaveLoad/AkronSaveLoad.cs"));

    private static string PersistenceSource =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Actions/akron-startpos-persistence.cs"));

    private static string ReconstructionSource =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/SaveLoad/akron-reconstruction-graph.cs"));

    private static AkronReconstructionDocument MinimalDocument() {
        return new AkronReconstructionDocument {
            RootNodeId = 1,
            Nodes = new List<AkronReconstructionNode> {
                new AkronReconstructionNode {
                    Id = 1,
                    Kind = "object",
                    TypeName = typeof(object).AssemblyQualifiedName!
                }
            }
        };
    }

    [Fact]
    public void SnapshotPathIsMemoizedAndStillMatchesTheSha256Layout() {
        string slotName = "Akron StartPos hotpath " + Guid.NewGuid().ToString("N");

        string first = AkronStartPosReconstruction.GetSnapshotPath(slotName);
        string second = AkronStartPosReconstruction.GetSnapshotPath(slotName);

        // Reference equality proves the memo hit rather than a recomputed equal string.
        Assert.Same(first, second);

        string expectedDigest =
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(slotName))).ToLowerInvariant();
        Assert.Equal("v10-" + expectedDigest + ".json.gz", Path.GetFileName(first));
    }

    [Fact]
    public void SnapshotPathKeepsDirectoriesApartForTheSameSlotName() {
        string slotName = "Akron StartPos hotpath " + Guid.NewGuid().ToString("N");
        string stagingDirectory = Path.Combine(Path.GetTempPath(), "akron-hotpath-" + Guid.NewGuid().ToString("N"));

        string canonical = AkronStartPosReconstruction.GetSnapshotPath(slotName);
        string staged = AkronStartPosReconstruction.GetSnapshotPath(slotName, stagingDirectory);

        Assert.NotEqual(canonical, staged);
        Assert.Equal(Path.GetFileName(canonical), Path.GetFileName(staged));
        Assert.Equal(stagingDirectory, Path.GetDirectoryName(staged));
        // Asking for the canonical path again must not have been poisoned by the
        // directory-qualified lookup.
        Assert.Equal(canonical, AkronStartPosReconstruction.GetSnapshotPath(slotName));
    }

    [Fact]
    public void HasSnapshotTracksWritesAndDeletesWithoutStatingEveryCall() {
        string slotName = "Akron StartPos hotpath " + Guid.NewGuid().ToString("N");
        try {
            Assert.False(AkronStartPosReconstruction.HasSnapshot(slotName));
            long afterNegative = AkronStartPosReconstruction.SnapshotExistenceRevision;

            // A repeated miss must be answered from the cache, so nothing changes.
            Assert.False(AkronStartPosReconstruction.HasSnapshot(slotName));
            Assert.Equal(afterNegative, AkronStartPosReconstruction.SnapshotExistenceRevision);

            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                slotName, "Akron/HotPath", "room", 1, MinimalDocument(), out string saveError), saveError);
            Assert.NotEqual(afterNegative, AkronStartPosReconstruction.SnapshotExistenceRevision);
            Assert.True(AkronStartPosReconstruction.HasSnapshot(slotName));

            long afterWrite = AkronStartPosReconstruction.SnapshotExistenceRevision;
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
            Assert.NotEqual(afterWrite, AkronStartPosReconstruction.SnapshotExistenceRevision);
            Assert.False(AkronStartPosReconstruction.HasSnapshot(slotName));
        } finally {
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    [Fact]
    public void StagedInstallAndRollbackBothRefreshTheCachedExistenceAnswer() {
        string slotName = "Akron StartPos hotpath " + Guid.NewGuid().ToString("N");
        string stagingDirectory = Path.Combine(Path.GetTempPath(), "akron-hotpath-" + Guid.NewGuid().ToString("N"));
        try {
            Assert.False(AkronStartPosReconstruction.HasSnapshot(slotName));
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                slotName, "Akron/HotPath", "room", 1, MinimalDocument(), out string stagedError, stagingDirectory),
                stagedError);

            // The staged write lands in a temp directory, so the canonical answer is
            // still "missing" and must stay cached as such.
            Assert.False(AkronStartPosReconstruction.HasSnapshot(slotName));

            using (AkronStartPosReconstruction.PreparedSnapshotInstall prepared =
                   AkronStartPosReconstruction.PrepareSnapshotInstall(slotName, stagingDirectory)) {
                Assert.True(prepared.Install(out string installError), installError);
                Assert.True(AkronStartPosReconstruction.HasSnapshot(slotName));
            }

            // Dispose without Commit rolls the install back, so the slot must disappear
            // from the list again instead of staying cached as present.
            Assert.False(AkronStartPosReconstruction.HasSnapshot(slotName));
        } finally {
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
            if (Directory.Exists(stagingDirectory)) {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ResettingTheExistenceCacheRecoversFromAnOutOfBandFileChange() {
        string slotName = "Akron StartPos hotpath " + Guid.NewGuid().ToString("N");
        try {
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                slotName, "Akron/HotPath", "room", 1, MinimalDocument(), out string saveError), saveError);
            Assert.True(AkronStartPosReconstruction.HasSnapshot(slotName));

            // Stand in for a player deleting the file from the Saves folder while the
            // game runs. The cache is allowed to be stale until the next room load.
            File.Delete(AkronStartPosReconstruction.GetSnapshotPath(slotName));
            Assert.True(AkronStartPosReconstruction.HasSnapshot(slotName));

            long beforeReset = AkronStartPosReconstruction.SnapshotExistenceRevision;
            AkronStartPosReconstruction.ResetSnapshotExistenceCache();
            Assert.NotEqual(beforeReset, AkronStartPosReconstruction.SnapshotExistenceRevision);
            Assert.False(AkronStartPosReconstruction.HasSnapshot(slotName));
        } finally {
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    [Fact]
    public void ReplacingTheSessionCatalogInvalidatesTheCachedStartPosList() {
        AkronModuleSession session = new AkronModuleSession();
        long before = AkronActions.StartPosCatalogRevision;

        session.StartPositions = new Dictionary<int, AkronStartPos> {
            [1] = new AkronStartPos { Room = "room", AreaSid = "Akron/HotPath" }
        };

        Assert.NotEqual(before, AkronActions.StartPosCatalogRevision);
    }

    [Fact]
    public void MarkingTheCatalogChangedAdvancesTheRevisionEveryTime() {
        long first = AkronActions.StartPosCatalogRevision;
        AkronActions.MarkStartPosCatalogChanged();
        long second = AkronActions.StartPosCatalogRevision;
        AkronActions.MarkStartPosCatalogChanged();

        Assert.NotEqual(first, second);
        Assert.NotEqual(second, AkronActions.StartPosCatalogRevision);
    }

    [Fact]
    public void GetStartPositionsChecksEveryCacheKeyComponentBeforeReusingItsList() {
        string source = ActionsSource;
        int start = source.IndexOf(
            "public static IReadOnlyList<AkronStartPosEntry> GetStartPositions(Level level)",
            StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = source.IndexOf("public static string DescribeStartPosIndex", start, StringComparison.Ordinal);
        Assert.True(end > start);
        string body = source.Substring(start, end - start);

        Assert.Contains("ReferenceEquals(cachedStartPosListSession, AkronModule.Session)", body);
        Assert.Contains("string.Equals(cachedStartPosListAreaSid, areaSid, StringComparison.Ordinal)", body);
        Assert.Contains("cachedStartPosListCatalogRevision == startPosCatalogRevision", body);
        Assert.Contains("cachedStartPosListRuntimeRevision == runtimeRevision", body);
        Assert.Contains("cachedStartPosListSnapshotRevision == snapshotRevision", body);
        // The list is shared between callers now, so it must not be handed out mutable.
        Assert.Contains("new ReadOnlyCollection<AkronStartPosEntry>(entries)", body);
    }

    [Fact]
    public void DescribeStartPosIndexDoesNotCopyTheStartPosListAgain() {
        string source = ActionsSource;
        int start = source.IndexOf("public static string DescribeStartPosIndex", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = source.IndexOf("public static void ApplyStartPosConfiguration", start, StringComparison.Ordinal);
        Assert.True(end > start);
        string body = source.Substring(start, end - start);

        Assert.DoesNotContain(".ToList()", body);
        Assert.DoesNotContain("FindIndex", body);
    }

    [Fact]
    public void EveryInPlaceStartPosCatalogMutationMarksTheCatalogChanged() {
        string source = ActionsSource;

        int publish = source.IndexOf(
            "AkronModule.Session.StartPositions[normalizedSlot] = startPos;",
            StringComparison.Ordinal);
        Assert.True(publish >= 0);
        int publishMark = source.IndexOf("MarkStartPosCatalogChanged();", publish, StringComparison.Ordinal);
        Assert.True(publishMark >= 0 && publishMark - publish < 120);

        int clear = source.IndexOf(
            "AkronModule.Session.StartPositions.Remove(clampedSlot);",
            StringComparison.Ordinal);
        Assert.True(clear >= 0);
        int clearMark = source.IndexOf("MarkStartPosCatalogChanged();", clear, StringComparison.Ordinal);
        Assert.True(clearMark >= 0 && clearMark - clear < 120);
    }

    [Fact]
    public void LoadingStartPositionsForALevelRestatsTheSnapshotDirectory() {
        string source = ActionsSource;
        int start = source.IndexOf(
            "internal static void LoadStartPositionsForLevel(Level level)",
            StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = source.IndexOf("internal static IEnumerable<KeyValuePair<int, AkronStartPos>>", start, StringComparison.Ordinal);
        Assert.True(end > start);

        Assert.Contains(
            "AkronStartPosReconstruction.ResetSnapshotExistenceCache();",
            source.Substring(start, end - start));
    }

    [Fact]
    public void EveryRuntimeSlotMutationAdvancesTheRuntimeStateRevision() {
        string source = SaveLoadSource;
        string[] mutations = {
            "RuntimeSlots.Clear();",
            "RuntimeSlots[slotName] = owner;"
        };
        foreach (string mutation in mutations) {
            int index = source.IndexOf(mutation, StringComparison.Ordinal);
            Assert.True(index >= 0, mutation);
            int mark = source.IndexOf("MarkRuntimeSlotsChanged();", index, StringComparison.Ordinal);
            Assert.True(mark >= 0 && mark - index < 120, mutation);
        }

        // Every removal must bump before its own method ends. Bounding the search by the
        // method's closing brace rather than by a character count keeps the check exact
        // when a removal carries an explanatory comment.
        int removals = 0;
        int cursor = 0;
        while (true) {
            int index = source.IndexOf("RuntimeSlots.Remove(", cursor, StringComparison.Ordinal);
            if (index < 0) {
                break;
            }
            removals++;
            int methodEnd = source.IndexOf("\n    }", index, StringComparison.Ordinal);
            int mark = source.IndexOf("MarkRuntimeSlotsChanged();", index, StringComparison.Ordinal);
            Assert.True(methodEnd > index, "RuntimeSlots.Remove at " + index);
            Assert.True(mark >= 0 && mark < methodEnd, "RuntimeSlots.Remove at " + index);
            cursor = index + 1;
        }
        // Four canonical-slot removals plus the three that park, restore and release the
        // previous state of a slot while a Set is writing its restart copy.
        Assert.Equal(7, removals);
    }

    [Fact]
    public void HasRuntimeStateStillShortCircuitsOnTheWarmSlotBeforeTouchingDisk() {
        string source = SaveLoadSource;
        string body = SliceMember(source, "public static bool HasRuntimeState(string slotName)");

        int warm = body.IndexOf("RuntimeSlots.ContainsKey(normalizedSlotName)", StringComparison.Ordinal);
        int snapshot = body.IndexOf("AkronStartPosReconstruction.HasSnapshot(normalizedSlotName)", StringComparison.Ordinal);
        Assert.True(warm >= 0 && snapshot > warm);
    }

    // --- Map-wide snapshot prewarm -------------------------------------------------
    //
    // A cold StartPos load spends most of its time turning the snapshot file into a
    // document, which is pure data and needs no live scene. These tests pin that a
    // prewarmed document is byte-identical to the one a cold read produces, that it is
    // served at most once, and that every way the file can change refuses it.

    private sealed class PrewarmTestNode {
        public string Name = string.Empty;
        public int Value;
    }

    private sealed class PrewarmTestRoot {
        public PrewarmTestNode Primary = null!;
        public int Counter;
    }

    private static AkronReconstructionDocument RichDocument() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(_ => false);
        AkronReconstructionCapture capture = graph.Capture(
            new PrewarmTestRoot { Counter = 17, Primary = new PrewarmTestNode { Name = "saved", Value = 4 } },
            new PrewarmTestRoot { Primary = new PrewarmTestNode { Name = "fresh" } });
        Assert.True(capture.Success, capture.Error);
        return capture.Document;
    }

    private static string WriteSnapshot(string slotName) {
        Assert.True(AkronStartPosReconstruction.SaveSnapshot(
            slotName, "Akron/Prewarm", "room-a", 2, RichDocument(), out string saveError), saveError);
        return AkronStartPosReconstruction.GetSnapshotPath(slotName);
    }

    [Fact]
    public void APrewarmedSnapshotServesTheSameDocumentAColdReadWouldHave() {
        string slotName = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        try {
            WriteSnapshot(slotName);

            // Read the snapshot without prewarming to establish the reference document.
            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                slotName, out AkronReconstructionDocument coldDocument, out string coldError), coldError);

            AkronStartPosReconstruction.PrewarmSnapshot(slotName, () => false);
            Assert.Equal(1, AkronStartPosReconstruction.PrewarmedSnapshotCount);

            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                slotName, out AkronReconstructionDocument prewarmedDocument, out string prewarmedError), prewarmedError);
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotCount);
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotBytes);

            // Serializing both back is the strongest available equality: it covers every
            // node, field, value, parent edge and header the restore passes will read.
            Assert.Equal(
                AkronStartPosReconstruction.Serialize(coldDocument),
                AkronStartPosReconstruction.Serialize(prewarmedDocument));
            Assert.Equal(slotName, prewarmedDocument.SlotName);
            Assert.Equal("Akron/Prewarm", prewarmedDocument.MapSid);
            Assert.Equal("room-a", prewarmedDocument.Room);
            Assert.Equal(2, prewarmedDocument.FileSlot);
        } finally {
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    [Fact]
    public void APrewarmedSnapshotIsServedOnceAndTheSlotThenReadsDiskAgain() {
        string slotName = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        try {
            string path = WriteSnapshot(slotName);
            AkronStartPosReconstruction.PrewarmSnapshot(slotName, () => false);
            Assert.Equal(1, AkronStartPosReconstruction.PrewarmedSnapshotCount);

            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(slotName, out _, out string firstError), firstError);
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotCount);

            // With the cache emptied by the first load, the second has to reach the file.
            File.Delete(path);
            Assert.False(AkronStartPosReconstruction.TryLoadSnapshot(slotName, out _, out string secondError));
            Assert.Equal("snapshot file is missing", secondError);
        } finally {
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    [Fact]
    public void WritingOrDeletingASnapshotDropsItsPrewarmedDocument() {
        string slotName = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        try {
            WriteSnapshot(slotName);
            AkronStartPosReconstruction.PrewarmSnapshot(slotName, () => false);
            Assert.Equal(1, AkronStartPosReconstruction.PrewarmedSnapshotCount);

            // Setting over the slot rewrites the file through SaveSnapshot, which is one
            // of the writers that already invalidates the cached existence answer.
            WriteSnapshot(slotName);
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotCount);

            AkronStartPosReconstruction.PrewarmSnapshot(slotName, () => false);
            Assert.Equal(1, AkronStartPosReconstruction.PrewarmedSnapshotCount);

            AkronStartPosReconstruction.DeleteSnapshot(slotName);
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotCount);
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotBytes);
        } finally {
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    [Fact]
    public void APrewarmedSnapshotIsRefusedAfterTheFileChangesOutOfBand() {
        string slotName = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        string otherSlotName = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        try {
            string path = WriteSnapshot(slotName);
            AkronStartPosReconstruction.PrewarmSnapshot(slotName, () => false);
            Assert.Equal(1, AkronStartPosReconstruction.PrewarmedSnapshotCount);

            // Stand in for a setup-pack import or the player replacing the file: a raw
            // copy over the path, with none of Akron's writers and no invalidation.
            string otherPath = WriteSnapshot(otherSlotName);
            File.Copy(otherPath, path, overwrite: true);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));

            // The stamp no longer matches, so the stale document is dropped rather than
            // handed to a restore, and the load reads what is actually on disk.
            Assert.False(AkronStartPosReconstruction.TryLoadSnapshot(slotName, out _, out string error));
            Assert.Equal("snapshot slot identity differs", error);
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotCount);
        } finally {
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
            AkronStartPosReconstruction.DeleteSnapshot(otherSlotName);
        }
    }

    [Fact]
    public void ACancelledPrewarmStoresNothing() {
        string slotName = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        try {
            WriteSnapshot(slotName);

            AkronStartPosReconstruction.PrewarmSnapshot(slotName, () => true);

            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotCount);
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotBytes);
            // The slot still loads normally; a cancelled prewarm is not a load failure.
            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(slotName, out _, out string error), error);

            string body = SliceMember(
                ReconstructionSource,
                "internal static AkronPrewarmOutcome PrewarmSnapshot(string slotName, Func<bool> isCancelled)");
            int publish = body.IndexOf("PrewarmedSnapshots[path] = new PrewarmedSnapshot", StringComparison.Ordinal);
            int finalCancellation = body.LastIndexOf("if (isCancelled())", publish, StringComparison.Ordinal);
            int finalLock = body.LastIndexOf("lock (PrewarmedSnapshotsLock)", publish, StringComparison.Ordinal);
            Assert.True(finalLock >= 0 && finalCancellation > finalLock && publish > finalCancellation);
        } finally {
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    [Fact]
    public void ThePrewarmWorkerReadsItsQueueAndStopsWhenCancelled() {
        string slotName = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        try {
            WriteSnapshot(slotName);

            AkronStartPosPersistence.PrewarmSnapshots(new[] { slotName });
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (AkronStartPosReconstruction.PrewarmedSnapshotCount == 0 && DateTime.UtcNow < deadline) {
                Thread.Sleep(10);
            }
            Assert.Equal(1, AkronStartPosReconstruction.PrewarmedSnapshotCount);

            AkronStartPosPersistence.CancelPrewarm();
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            AkronStartPosPersistence.PrewarmSnapshots(new[] { slotName });
            AkronStartPosPersistence.CancelPrewarm();
            // A cancelled queue can be refilled; the worker must not be stranded by the
            // cancellation that emptied it.
            AkronStartPosPersistence.PrewarmSnapshots(new[] { slotName });
            deadline = DateTime.UtcNow.AddSeconds(10);
            while (AkronStartPosReconstruction.PrewarmedSnapshotCount == 0 && DateTime.UtcNow < deadline) {
                Thread.Sleep(10);
            }
            Assert.Equal(1, AkronStartPosReconstruction.PrewarmedSnapshotCount);
        } finally {
            AkronStartPosPersistence.CancelPrewarm();
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    [Fact]
    public void ThePrewarmWorkerMakesNoProgressWhileThePlayerIsInControl() {
        string slotName = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        bool previousActive = AkronSnapshotPacing.GameplayActive;
        bool previousForcedOpen = AkronSnapshotPacing.ForcedOpen;
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        try {
            WriteSnapshot(slotName);
            AkronSnapshotPacing.ForcedOpen = false;
            AkronSnapshotPacing.GameplayActive = true;

            AkronStartPosPersistence.PrewarmSnapshots(new[] { slotName });
            // The read parks at its first buffer fill, so nothing is ever stored while
            // the player is in a level. This is the property that keeps the allocation
            // the deferred-collection work removed from getting back into gameplay.
            Thread.Sleep(300);
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotCount);
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotBytes);

            AkronSnapshotPacing.GameplayActive = false;
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (AkronStartPosReconstruction.PrewarmedSnapshotCount == 0 && DateTime.UtcNow < deadline) {
                Thread.Sleep(10);
            }
            Assert.Equal(1, AkronStartPosReconstruction.PrewarmedSnapshotCount);
        } finally {
            AkronSnapshotPacing.GameplayActive = previousActive;
            AkronSnapshotPacing.ForcedOpen = previousForcedOpen;
            AkronStartPosPersistence.CancelPrewarm();
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    [Fact]
    public void AParkedPrewarmLetsGoOfItsReadWhenTheQueueIsReplaced() {
        string slotName = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        bool previousActive = AkronSnapshotPacing.GameplayActive;
        bool previousForcedOpen = AkronSnapshotPacing.ForcedOpen;
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        try {
            WriteSnapshot(slotName);
            AkronSnapshotPacing.ForcedOpen = false;
            AkronSnapshotPacing.GameplayActive = true;
            AkronStartPosPersistence.PrewarmSnapshots(new[] { slotName });
            Thread.Sleep(200);

            // A parked read holds the snapshot file open and a half-built document in
            // memory. Cancelling has to be noticed while parked, not at the next gate
            // opening, or a Set could sit behind a speculative read for the rest of the
            // session on any platform that enforces file sharing.
            AkronStartPosPersistence.CancelPrewarm();
            AkronSnapshotPacing.GameplayActive = false;
            Thread.Sleep(300);
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotCount);

            // And the worker is not stranded by that cancellation.
            AkronStartPosPersistence.PrewarmSnapshots(new[] { slotName });
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (AkronStartPosReconstruction.PrewarmedSnapshotCount == 0 && DateTime.UtcNow < deadline) {
                Thread.Sleep(10);
            }
            Assert.Equal(1, AkronStartPosReconstruction.PrewarmedSnapshotCount);
        } finally {
            AkronSnapshotPacing.GameplayActive = previousActive;
            AkronSnapshotPacing.ForcedOpen = previousForcedOpen;
            AkronStartPosPersistence.CancelPrewarm();
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    [Fact]
    public void EveryQueuedSlotIsWarmedInsteadOfStoppingAtTheBudget() {
        string[] slotNames = new string[5];
        for (int index = 0; index < slotNames.Length; index++) {
            slotNames[index] = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        }
        bool previousActive = AkronSnapshotPacing.GameplayActive;
        bool previousForcedOpen = AkronSnapshotPacing.ForcedOpen;
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        try {
            foreach (string slotName in slotNames) {
                WriteSnapshot(slotName);
            }
            AkronSnapshotPacing.ForcedOpen = false;
            AkronSnapshotPacing.GameplayActive = false;

            AkronStartPosPersistence.PrewarmSnapshots(slotNames);
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (AkronStartPosReconstruction.PrewarmedSnapshotCount < slotNames.Length &&
                   DateTime.UtcNow < deadline) {
                Thread.Sleep(10);
            }

            // The budget used to stop at 96 MiB of decompressed JSON, which on a real
            // install was less than one modded snapshot. A queue of placed slots now
            // warms all the way through instead of stopping partway.
            Assert.Equal(slotNames.Length, AkronStartPosReconstruction.PrewarmedSnapshotCount);
            Assert.Equal(0, AkronStartPosPersistence.PrewarmQueueLength);
            Assert.True(AkronStartPosReconstruction.PrewarmedSnapshotBytes > 0);

            // Each one still serves the document a cold read would have produced, and
            // is removed on the way out so a second load of the same slot reads disk.
            foreach (string slotName in slotNames) {
                Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                    slotName, out AkronReconstructionDocument document, out string error), error);
                Assert.Equal(slotName, document.SlotName);
            }
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotCount);
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotBytes);
        } finally {
            AkronSnapshotPacing.GameplayActive = previousActive;
            AkronSnapshotPacing.ForcedOpen = previousForcedOpen;
            AkronStartPosPersistence.CancelPrewarm();
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            foreach (string slotName in slotNames) {
                AkronStartPosReconstruction.DeleteSnapshot(slotName);
            }
        }
    }

    [Fact]
    public void PrewarmingIsBoundedByAFiniteMemoryBudget() {
        string source = ReconstructionSource;
        Assert.True(AkronStartPosReconstruction.MaxPrewarmedSnapshotBytes > 0);

        // The budget has to clear the largest snapshot a real install produces, or the
        // cache is dead for the maps it was built for. Measured off the Linux test box:
        // 17 real snapshots, 42-85 MB decompressed for vanilla maps and 150-231 MB for
        // modded ones. At the original 96 MiB budget, 8 of the 17 - every modded one -
        // could not be prewarmed at all, so the feature did nothing on exactly the maps
        // with the slowest loads. Two of them have to fit, or a modded map warms one
        // slot and stops.
        const long largestMeasuredSnapshotBytes = 231081666L;
        Assert.True(
            AkronStartPosReconstruction.MaxPrewarmedSnapshotBytes >= 2L * largestMeasuredSnapshotBytes,
            "The prewarm budget must hold two of the largest snapshots measured on a real install.");

        // And the ceiling it implies has to be one a player's machine can pay. A
        // prewarmed slot costs 3.8x its decompressed bytes in process RSS, measured in
        // game on a modded map (249 MiB of RSS for a 64.9 MiB snapshot, n=2 per side).
        // Celeste with mods already sits at about 1 GiB, so a full cache has to stay
        // near 2 GiB for a 4 GB machine to survive it. This is the assertion that a
        // future raise has to argue with: the budget is not the cost.
        const long measuredRssPerDecompressedByteTimesTen = 38L;
        const long rssCeilingBytes = 2L * 1024L * 1024L * 1024L;
        Assert.True(
            AkronStartPosReconstruction.MaxPrewarmedSnapshotBytes *
            measuredRssPerDecompressedByteTimesTen / 10L <= rssCeilingBytes,
            "A full prewarm cache must stay under 2 GiB of resident memory at the measured 3.8x.");

        string body = SliceMember(source, "internal static AkronPrewarmOutcome PrewarmSnapshot(string slotName, Func<bool> isCancelled)");

        // The budget is answered from the size the snapshot expands to, before anything
        // is decompressed, and the store is still guarded under the lock because the
        // cache can grow while this read runs.
        Assert.Contains("remainingBudget = MaxPrewarmedSnapshotBytes - prewarmedSnapshotBytes;", body);
        Assert.Contains("expandedBytes > remainingBudget", body);
        Assert.Contains("prewarmedSnapshotBytes + decompressedBytes > MaxPrewarmedSnapshotBytes", body);
    }

    [Fact]
    public void APrewarmNeverAcceptsASnapshotAColdReadWouldRefuse() {
        // The budget is larger than a whole legal snapshot, so without this bound an
        // oversized snapshot would be prewarmed successfully and then fail when read
        // from disk. Whether a slot loads at all would then depend on whether the worker
        // reached it first, which is the opposite of exact-state restore.
        Assert.True(
            AkronStartPosReconstruction.MaxPrewarmedSnapshotBytes >
            AkronStartPosReconstruction.MaxDecompressedSnapshotBytes,
            "This bound is only load-bearing while the budget exceeds one whole snapshot.");

        string body = SliceMember(
            ReconstructionSource,
            "internal static AkronPrewarmOutcome PrewarmSnapshot(string slotName, Func<bool> isCancelled)");

        // The prewarm read is bounded by the cold-read limit itself, not by whatever is
        // left of the cache budget. Bounding it by the remaining budget is what used to
        // turn "this does not fit" into a read that threw.
        Assert.Contains("out decompressedBytes, MaxDecompressedSnapshotBytes)", body);

        // And the cold-read limit is answered before the budget, so a file no load could
        // use is never reported as a full cache.
        int coldReadLimit = body.IndexOf("expandedBytes > MaxDecompressedSnapshotBytes", StringComparison.Ordinal);
        int budget = body.IndexOf("expandedBytes > remainingBudget", StringComparison.Ordinal);
        Assert.True(coldReadLimit >= 0 && coldReadLimit < budget);
    }

    [Fact]
    public void AFullPrewarmCacheReportsBudgetExhaustionRatherThanAFailedRead() {
        // The remaining budget used to bound the read itself, so a snapshot that did not
        // fit threw its way out of the bounded stream and was counted as one the worker
        // could not read - wording that blames the file for a full cache, and the clause
        // that names the budget was only reachable when the budget was exactly zero.
        string slotName = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        try {
            string path = WriteSnapshot(slotName);
            long expandedBytes = ReadGzipExpandedSize(path);
            Assert.True(expandedBytes > 1);

            // One byte less room than this slot needs. That is the realistic shape - a
            // nearly full cache and a snapshot that no longer fits - and the outcome has
            // to name the budget rather than the file.
            AkronStartPosReconstruction.HoldPrewarmedSnapshotBytesForTests(
                AkronStartPosReconstruction.MaxPrewarmedSnapshotBytes - expandedBytes + 1);
            Assert.Equal(
                AkronPrewarmOutcome.BudgetFull,
                AkronStartPosReconstruction.PrewarmSnapshot(slotName, () => false));
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotCount);

            // Exactly enough room and the same file on the same disk is read and kept, so
            // the refusal above is the budget and nothing else.
            AkronStartPosReconstruction.HoldPrewarmedSnapshotBytesForTests(
                AkronStartPosReconstruction.MaxPrewarmedSnapshotBytes - expandedBytes);
            Assert.Equal(
                AkronPrewarmOutcome.Stored,
                AkronStartPosReconstruction.PrewarmSnapshot(slotName, () => false));
            Assert.Equal(1, AkronStartPosReconstruction.PrewarmedSnapshotCount);
        } finally {
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    [Fact]
    public void ASnapshotTooLargeForAColdReadIsNotReportedAsAFullCache() {
        // Answering the budget from the expanded size introduced a way to get this wrong
        // in the other direction: a file whose size no cold read would accept, including
        // a truncated one whose last four bytes decode to a large number, would be called
        // a budget refusal as soon as the cache had less room than that. It cannot be -
        // that file will never load, however empty the cache is.
        string oversizedSlotName = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        try {
            WriteGzipFileWithExpandedSizeTrailer(
                AkronStartPosReconstruction.GetSnapshotPath(oversizedSlotName),
                AkronStartPosReconstruction.MaxDecompressedSnapshotBytes + 1);

            // Empty cache: no budget pressure at all, and it is still refused.
            Assert.Equal(
                AkronPrewarmOutcome.NotStored,
                AkronStartPosReconstruction.PrewarmSnapshot(oversizedSlotName, () => false));

            // And with barely any room left it is refused for the same reason, not as a
            // full cache.
            AkronStartPosReconstruction.HoldPrewarmedSnapshotBytesForTests(
                AkronStartPosReconstruction.MaxPrewarmedSnapshotBytes - 1);
            Assert.Equal(
                AkronPrewarmOutcome.NotStored,
                AkronStartPosReconstruction.PrewarmSnapshot(oversizedSlotName, () => false));
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotCount);
        } finally {
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            AkronStartPosReconstruction.DeleteSnapshot(oversizedSlotName);
        }
    }

    // The expanded size of a snapshot, measured by decompressing the whole file, so the
    // budget tests measure the file rather than trusting the trailer the code reads.
    private static long ReadGzipExpandedSize(string path) {
        using FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using GZipStream compressed = new GZipStream(file, CompressionMode.Decompress);
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = compressed.Read(buffer, 0, buffer.Length)) > 0) {
            total += read;
        }
        return total;
    }

    // Prewarm reads the gzip ISIZE trailer before it decompresses anything. A valid
    // empty member with an oversized trailer exercises that boundary without writing
    // and compressing hundreds of megabytes in a unit test.
    private static void WriteGzipFileWithExpandedSizeTrailer(string path, long expandedBytes) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] gzip = Convert.FromHexString("1f8b080000000000000303000000000000000000");
        uint isize = checked((uint) expandedBytes);
        for (int index = 0; index < sizeof(uint); index++) {
            gzip[gzip.Length - sizeof(uint) + index] = (byte) (isize >> (index * 8));
        }
        File.WriteAllBytes(path, gzip);
    }

    [Fact]
    public void APrewarmedLoadReportsItselfAsPrewarmed() {
        string slotName = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        try {
            WriteSnapshot(slotName);

            // A cold read must not be reported as a cache hit, or the counter cannot
            // tell the two apart and the log line built on it says nothing.
            long beforeCold = AkronStartPosReconstruction.PrewarmedSnapshotHits;
            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(slotName, out _, out string coldError), coldError);
            Assert.Equal(beforeCold, AkronStartPosReconstruction.PrewarmedSnapshotHits);

            long beforeStore = AkronStartPosReconstruction.PrewarmedSnapshotStores;
            Assert.Equal(
                AkronPrewarmOutcome.Stored,
                AkronStartPosReconstruction.PrewarmSnapshot(slotName, () => false));
            Assert.Equal(beforeStore + 1, AkronStartPosReconstruction.PrewarmedSnapshotStores);

            long beforeWarm = AkronStartPosReconstruction.PrewarmedSnapshotHits;
            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(slotName, out _, out string warmError), warmError);
            Assert.Equal(beforeWarm + 1, AkronStartPosReconstruction.PrewarmedSnapshotHits);

            // A stale entry that the file stamp refuses is not a hit either: the load
            // read the file, and reporting it as served from the cache would hide the
            // one invalidation path that has no writer behind it.
            AkronStartPosReconstruction.PrewarmSnapshot(slotName, () => false);
            File.SetLastWriteTimeUtc(
                AkronStartPosReconstruction.GetSnapshotPath(slotName), DateTime.UtcNow.AddMinutes(5));
            long beforeStale = AkronStartPosReconstruction.PrewarmedSnapshotHits;
            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(slotName, out _, out string staleError), staleError);
            Assert.Equal(beforeStale, AkronStartPosReconstruction.PrewarmedSnapshotHits);
        } finally {
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    [Fact]
    public void ASlotAlreadyInTheCacheIsNotReportedAsAFailedPrewarm() {
        string slotName = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        try {
            WriteSnapshot(slotName);

            Assert.Equal(
                AkronPrewarmOutcome.Stored,
                AkronStartPosReconstruction.PrewarmSnapshot(slotName, () => false));

            // Every load re-queues the map's other slots, so on a warm map every slot
            // in the queue is already held. A bool return could not tell that apart
            // from a read that failed, and the summary line said "warmed 0 of 3" for a
            // cache that was working perfectly.
            Assert.Equal(
                AkronPrewarmOutcome.AlreadyCached,
                AkronStartPosReconstruction.PrewarmSnapshot(slotName, () => false));
            Assert.Equal(1, AkronStartPosReconstruction.PrewarmedSnapshotCount);
        } finally {
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    [Fact]
    public void ThePrewarmRunLineAccountsForEverySlotItQueued() {
        Assert.Equal(
            "StartPos prewarm warmed 4 of 4 slots for this map",
            AkronStartPosPersistence.DescribePrewarmRun(4, 4, 0, 0, 0, replaced: false));

        // The map was already warm. "warmed 0 of 3" on its own reads as a broken
        // feature and was observed in game on every load after the first.
        Assert.Equal(
            "StartPos prewarm warmed 0 of 3 slots for this map: 3 already cached",
            AkronStartPosPersistence.DescribePrewarmRun(3, 0, 3, 0, 0, replaced: false));

        // A queue left partly drained wrote nothing at all before, which is the case
        // the feature exists for: a map holds more slots than one non-gameplay window
        // can read, so the run normally ends by being replaced rather than by draining.
        Assert.Equal(
            "StartPos prewarm warmed 4 of 14 slots for this map: 10 not read before the queue was replaced",
            AkronStartPosPersistence.DescribePrewarmRun(14, 4, 0, 0, 0, replaced: true));

        // The budget stopping a modded map partway is the expected outcome there, not
        // a fault, and the line has to say which it was.
        Assert.Equal(
            "StartPos prewarm warmed 2 of 5 slots for this map: 3 did not fit the remaining budget",
            AkronStartPosPersistence.DescribePrewarmRun(5, 2, 0, 3, 0, replaced: false));

        Assert.Equal(
            "StartPos prewarm warmed 1 of 5 slots for this map: 1 already cached, " +
            "1 did not fit the remaining budget, 1 could not be read, 1 never read",
            AkronStartPosPersistence.DescribePrewarmRun(5, 1, 1, 1, 1, replaced: false));
    }

    [Fact]
    public void EveryPrewarmRunIsReportedWhetherItDrainsOrIsReplaced() {
        // The worker only reaches its report when the queue empties. A run that is
        // cancelled or replaced while slots are still queued ends in one of these two
        // instead, and before this both of them threw the progress away silently.
        string source = PersistenceSource;
        foreach (string member in new[] {
                     "public static void PrewarmSnapshots(IReadOnlyList<string> stateSlotNames)",
                     "public static void CancelPrewarm()"
                 }) {
            string body = SliceMember(source, member);
            int take = body.IndexOf("TakePrewarmRunSummaryLocked(replaced: true);", StringComparison.Ordinal);
            int report = body.IndexOf("ReportPrewarmRun(superseded);", StringComparison.Ordinal);
            Assert.True(take >= 0, member + " does not take the superseded run's counters");
            Assert.True(report > take, member + " does not report the run it replaced");
        }

        // Reported outside the lock. A file write on the game thread while the worker
        // waits on the same lock is a stall for a diagnostic.
        string cancel = SliceMember(source, "public static void CancelPrewarm()");
        int lockEnd = cancel.IndexOf("\n        }\n", StringComparison.Ordinal);
        Assert.True(lockEnd >= 0);
        Assert.True(
            cancel.IndexOf("ReportPrewarmRun(superseded);", StringComparison.Ordinal) > lockEnd,
            "the run summary is logged while the persistence lock is held");
    }

    [Fact]
    public void ASavestateLoadDoesNotRewindTheStartPosCatalog() {
        // A savestate restore replaces AkronModule._SaveData and _Session wholesale.
        // The persisted StartPos metadata lives in the first and the in-session
        // catalog in the second, and neither is gameplay state: both can hold slots
        // created after the savestate was taken. Measured in game, a slot set after
        // the savestate read startpos-set: false with its snapshot and metadata intact
        // on disk until the next process start, and the rewound persisted catalog is
        // what the next save file write would have persisted.
        string saveLoad = SaveLoadSource;
        string load = SliceMember(saveLoad, "public static AkronSaveLoadResult Load(Level level, int slot)");

        int captured = load.IndexOf(
            "AkronModule.Instance == null ? null : AkronModule.SaveData?.StartPositionsByMap",
            StringComparison.Ordinal);
        int core = load.IndexOf("return LoadCore(level, slot);", StringComparison.Ordinal);
        int restored = load.IndexOf(
            "AkronActions.RestoreStartPosCatalogAfterStateLoad(level, startPosCatalog);",
            StringComparison.Ordinal);
        Assert.True(captured >= 0, "the catalog is not captured before the restore");
        Assert.True(core > captured);
        Assert.True(restored > core, "the catalog is not put back after the restore");
        Assert.Contains("} finally {", load);

        // Both restore paths have to be inside that hold. The brokered path is the one
        // every shipped build takes - ShouldBrokerSavestatesInsteadOfNative returns
        // true on net8.0 - and SpeedrunTool assigns _Session and _SaveData itself, so
        // there is nowhere inside Akron's own native path to fix this.
        string core_ = SliceMember(saveLoad, "private static AkronSaveLoadResult LoadCore(Level level, int slot)");
        Assert.Contains("TryBrokerLoad(level, slot)", core_);
        Assert.Contains("RestoreNativeSlot(level, saveSlot)", core_);

        // And the in-session view is rebuilt from the metadata, because the session
        // object itself was replaced and the view has to land on the new one.
        string rebuild = SliceMember(
            ActionsSource,
            "internal static void RestoreStartPosCatalogAfterStateLoad(");
        int metadata = rebuild.IndexOf("RestoreStartPosCatalog(", StringComparison.Ordinal);
        int session = rebuild.IndexOf("LoadStartPositionsForLevel(level);", StringComparison.Ordinal);
        Assert.True(metadata >= 0 && session > metadata,
            "the session catalog must be rebuilt from the metadata that was put back first");
    }

    [Fact]
    public void ThePrewarmCacheIsVisibleInStatusAndInTheLoadTimingLine() {
        // An unobservable cache is a maintenance hazard: two verification passes could
        // only infer a hit from wall-clock timings, and a change that disabled the cache
        // would have left every test green.
        string status = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "../../../../Source/Commands/akron-startpos-commands.cs"));
        Assert.Contains("startpos-prewarm-slots: ", status);
        Assert.Contains("startpos-prewarm-bytes: ", status);
        Assert.Contains("startpos-prewarm-budget-bytes: ", status);
        Assert.Contains("startpos-prewarm-queued: ", status);
        Assert.Contains("startpos-prewarm-stored: ", status);
        Assert.Contains("startpos-prewarm-hits: ", status);

        string actions = ActionsSource;
        string body = SliceMember(actions, "private static void ReportStartPosRestoreTiming(");

        // Once per load, at Diagnostic, which is the default logging level.
        Assert.Contains("AkronLog.Diagnostic(nameof(AkronActions),", body);
        Assert.Contains("the snapshot came from the prewarm cache", body);
        Assert.Contains("the snapshot was read from disk", body);
        Assert.Contains("PrewarmedSnapshotHits > prewarmHitsBeforeLoad", body);
    }

    [Fact]
    public void ASnapshotRewrittenDuringItsPrewarmReadIsNotCached() {
        string slotName = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        try {
            WriteSnapshot(slotName);

            // A Set landing while the worker is mid-read has nothing to drop from the
            // cache, because the entry is not in it yet. Standing in for that here by
            // rewriting the slot from the cancellation callback, which the reader polls.
            bool rewritten = false;
            AkronStartPosReconstruction.PrewarmSnapshot(slotName, () => {
                if (!rewritten) {
                    rewritten = true;
                    WriteSnapshot(slotName);
                }
                return false;
            });

            Assert.True(rewritten);
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotCount);
            Assert.Equal(0, AkronStartPosReconstruction.PrewarmedSnapshotBytes);
        } finally {
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    [Fact]
    public void APrewarmReadThatRacesAWriteIsRejectedByTheWriteRevision() {
        string source = ReconstructionSource;
        string body = SliceMember(source, "internal static AkronPrewarmOutcome PrewarmSnapshot(string slotName, Func<bool> isCancelled)");

        int captured = body.IndexOf("long revisionBeforeRead = SnapshotWriteRevision;", StringComparison.Ordinal);
        int read = body.IndexOf("TryReadSnapshot(PrewarmGraph", StringComparison.Ordinal);
        int compared = body.IndexOf("revisionBeforeRead != SnapshotWriteRevision", StringComparison.Ordinal);

        Assert.True(captured >= 0 && captured < read, "The revision must be read before the file.");
        Assert.True(compared > read, "The revision must be re-checked after the read.");
    }

    [Fact]
    public void ARoomLoadDuringAPrewarmReadDoesNotThrowAwayTheResult() {
        string slotName = "Akron StartPos prewarm " + Guid.NewGuid().ToString("N");
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        try {
            WriteSnapshot(slotName);
            // Populate the existence cache the way the StartPos list does, so the flush
            // below has something to clear and therefore bumps its revision.
            Assert.True(AkronStartPosReconstruction.HasSnapshot(slotName));

            // Every room load flushes that cache so the next caller re-stats, and a
            // StartPos load reloads the room in the middle of the window the prewarm
            // worker now runs in. Nothing about the file changed, so the read has to
            // keep its result; comparing the existence revision instead discarded it.
            bool flushed = false;
            long revisionBefore = AkronStartPosReconstruction.SnapshotExistenceRevision;
            Assert.Equal(AkronPrewarmOutcome.Stored, AkronStartPosReconstruction.PrewarmSnapshot(slotName, () => {
                if (!flushed) {
                    flushed = true;
                    AkronStartPosReconstruction.ResetSnapshotExistenceCache();
                }
                return false;
            }));

            Assert.True(flushed);
            Assert.NotEqual(revisionBefore, AkronStartPosReconstruction.SnapshotExistenceRevision);
            Assert.Equal(1, AkronStartPosReconstruction.PrewarmedSnapshotCount);
        } finally {
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    [Fact]
    public void ASetupPackImportNamesEveryCanonicalSnapshotItTouches() {
        // The import moves files into the canonical directory with a raw File.Move, so
        // it is the one writer that does not funnel through InvalidateSnapshotExistence.
        // It leaned on the consume-time file stamp instead, which cannot tell a
        // replacement apart when the new file shares a length and a modification time.
        string source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "../../../../Source/Setups/akron-setup-packs.cs"));
        int start = source.IndexOf("private void InvalidateTouchedSnapshots()", StringComparison.Ordinal);
        Assert.True(start >= 0, "the import no longer names the paths it touched");
        int nextMember = source.IndexOf("private void BackUpDestination(", start, StringComparison.Ordinal);
        Assert.True(nextMember > start, "the import invalidation member boundary is unavailable");
        string body = source.Substring(start, nextMember - start);

        Assert.Contains("AkronStartPosReconstruction.InvalidateSnapshotExistence(installedPath);", body);
        Assert.Contains("AkronStartPosReconstruction.InvalidateSnapshotExistence(replacedPath);", body);
        Assert.Contains("AkronStartPosReconstruction.ResetSnapshotExistenceCache();", body);

        // The rollback has to name them before it empties the lists it names them from.
        int rollback = source.IndexOf("private void RollBack()", StringComparison.Ordinal);
        Assert.True(rollback >= 0);
        int invalidate = source.IndexOf("InvalidateTouchedSnapshots();", rollback, StringComparison.Ordinal);
        int cleared = source.IndexOf("installedPaths.Clear();", rollback, StringComparison.Ordinal);
        Assert.True(invalidate >= 0 && invalidate < cleared);
    }

    [Fact]
    public void EveryPrewarmedDocumentIsDroppedWhenItsPathIsInvalidated() {
        string source = ReconstructionSource;
        string body = SliceMember(source, "internal static void InvalidateSnapshotExistence(string path)");

        // Every Akron writer of the snapshot directory already funnels through this, so
        // the prewarm cache inherits the audited writer set rather than adding its own.
        Assert.Contains("DropPrewarmedSnapshot(path);", body);
    }

    [Fact]
    public void TheRequestedStartPosLoadQueuesNoPrewarmWorkOfItsOwn() {
        string source = ActionsSource;
        int start = source.IndexOf("private static bool RestoreStartPosUnderPacingGate(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = source.IndexOf("private static void ReportStartPosRestoreTiming(", start, StringComparison.Ordinal);
        Assert.True(end > start);
        string body = source.Substring(start, end - start);

        int cancel = body.IndexOf("AkronStartPosPersistence.CancelPrewarm();", StringComparison.Ordinal);
        int hold = body.IndexOf("AkronStartPosPersistence.HoldPacingGateOpen();", StringComparison.Ordinal);
        int finish = body.IndexOf("AkronStartPosPersistence.FinishPendingRestartCopy(", StringComparison.Ordinal);

        Assert.True(hold >= 0, "The load must hold the pacing gate open.");
        // Cancelling before the gate opens is what stops a read parked since an earlier
        // load from waking into competition with the restart copy this load blocks on.
        Assert.True(cancel >= 0 && cancel < hold, "Speculative reads must be abandoned before the gate opens.");
        Assert.True(finish > hold, "The restart copy must be finished inside the gate hold.");

        // Nothing speculative may be queued inside the window the load freezes the game
        // thread for. Measured in game: queueing the map's other slots in here took the
        // first load on Forsaken City from 5222.5 +- 21.9 ms (n=4) to 8220.1 +- 669.5 ms
        // (n=4), +57%, and the same build with nothing to queue landed at
        // 5183.3 +- 9.1 ms (n=3), so the whole regression was the speculative reads
        // competing with the load's own parse on a single workstation GC heap.
        Assert.DoesNotContain("PrewarmOtherStartPosSnapshots", body);
        Assert.DoesNotContain("AkronStartPosPersistence.PrewarmSnapshots(", body);

        // It is queued after the load instead, where the gate is closed again and the
        // worker parks until the player is out of control of the game.
        string core = SliceMember(source, "private static bool RestoreStartPosCore(");
        int gatedLoad = core.IndexOf("RestoreStartPosUnderPacingGate(level, startPos", StringComparison.Ordinal);
        int collect = core.IndexOf("AkronEngineGarbageCollection.CollectDeferred();", StringComparison.Ordinal);
        int prewarm = core.IndexOf("PrewarmOtherStartPosSnapshots(", StringComparison.Ordinal);
        Assert.True(gatedLoad >= 0);
        Assert.True(collect > gatedLoad, "The deferred collection is paid after the load.");
        Assert.True(prewarm > collect, "The prewarm queue must be filled after the load and its collection.");
        // Definition plus exactly one call site, so a second queueing point cannot
        // drift back into the load without this failing.
        Assert.Equal(2, CountOccurrences(source, "PrewarmOtherStartPosSnapshots("));

        // The warm/cold word has to be the path the load actually took, reported out of
        // LoadRuntimeState. Gating on a HasRuntimeStateInMemory check taken before the
        // call reads "warm" for a stale slot left by a chapter re-entry, which then
        // fails with SessionMismatch and rebuilds from the snapshot anyway. Measured on
        // the test box: 4602 ms of snapshot rebuild logged as a warm restore.
        Assert.Contains("out usedSnapshot);", body);
        Assert.DoesNotContain("HasRuntimeStateInMemory", body);
        Assert.Contains("(usedSnapshot ? \"cold\" : \"warm\")", source);
    }

    [Fact]
    public void PrewarmReadsAreStoppedWhileThePlayerIsInControl() {
        // The prewarm worker retains every byte it parses, so letting it run during play
        // would put the allocation the deferred-collection work just removed straight
        // back into frames the player is playing. It paces on the same gate the restart
        // copy uses: stopped in a level, running when the game is paused, in a menu, in
        // a StartPos input wait, or frozen inside a load.
        string persistence = PersistenceSource;
        int start = persistence.IndexOf("private static void RunPrewarmWorker()", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = persistence.IndexOf("private static void ReportPrewarmRun(", start, StringComparison.Ordinal);
        Assert.True(end > start);
        string worker = persistence.Substring(start, end - start);

        int begin = worker.IndexOf("AkronSnapshotPacing.BeginPacedWork(", StringComparison.Ordinal);
        int read = worker.IndexOf("AkronStartPosReconstruction.PrewarmSnapshot(", StringComparison.Ordinal);
        int endPaced = worker.IndexOf("AkronSnapshotPacing.EndPacedWork();", read, StringComparison.Ordinal);
        Assert.True(begin >= 0 && begin < read, "The prewarm read must run as paced work.");
        Assert.True(endPaced > read, "The paced scope must be closed after the read.");
        // A parked read holds the snapshot file open and a half-built document in
        // memory, so it has to let go the moment its queue is superseded rather than
        // waiting for the next gate opening to find out.
        Assert.Contains("AkronSnapshotPacing.BeginPacedWork(() => IsPrewarmCancelled(generation));", worker);

        // The pace point for a read is the bounded stream, which is where decompressed
        // bytes are counted, so it bounds allocation rather than compressed input.
        string reconstruction = ReconstructionSource;
        string streamBody = SliceMember(reconstruction, "private sealed class AkronBoundedReadStream");
        Assert.Equal(2, CountOccurrences(streamBody, "AkronSnapshotPacing.Pace();"));
    }

    [Fact]
    public void AParkedPrewarmDoesNotBlockAWriteOnWindows() {
        // Parking mid-read means the handle can be held for as long as the player keeps
        // playing. Windows refuses to rename or delete a file another handle holds open
        // unless that handle shares the right, so a narrower share mode would let a
        // speculative read stop a Set from installing its own snapshot. Linux discards
        // FileShare entirely, so the contract is what can be asserted here.
        string source = ReconstructionSource;
        string body = SliceMember(source, "internal static AkronPrewarmOutcome PrewarmSnapshot(string slotName, Func<bool> isCancelled)");

        Assert.Contains("FileShare.ReadWrite | FileShare.Delete", body);
        Assert.DoesNotContain("FileShare.Read)", body);
    }

    [Fact]
    public void WarmAndPendingSlotsAreNeverQueuedForPrewarm() {
        string source = ActionsSource;
        string body = SliceMember(source, "private static void PrewarmOtherStartPosSnapshots(");

        Assert.Contains("pair.Key != loadedSlot", body);
        // Session-aware on purpose. A slot still in memory from before a chapter
        // re-entry will be rebuilt from its snapshot, so it must not be skipped here.
        Assert.Contains("AkronSaveLoadService.WillRestoreFromRuntimeMemory(level, stateSlotName)", body);
        Assert.Contains("HasPendingStartPosState(stateSlotName)", body);
        Assert.Contains("!AkronStartPosReconstruction.HasSnapshot(stateSlotName)", body);
    }

    [Fact]
    public void ChangingMapOrSaveFileReleasesEveryPrewarmedDocument() {
        string source = ActionsSource;
        string body = SliceMember(source, "internal static void LoadStartPositionsForLevel(Level level)");

        Assert.Contains("prewarmedSnapshotScope", body);
        Assert.Contains("AkronStartPosReconstruction.ResetPrewarmedSnapshots();", body);
        Assert.Contains("AkronStartPosPersistence.CancelPrewarm();", body);
    }

    [Fact]
    public void StartPosLoadAndPersistDiagnosticsGoToTheAkronLog() {
        // Reporters attach AkronLogs/akron-current.log. Anything on the StartPos load or
        // persist path that only reached Celeste's log.txt was invisible in bug reports.
        Assert.DoesNotContain("Logger.Log(", ActionsSource);
        Assert.DoesNotContain("Logger.Log(", PersistenceSource);

        string body = SliceMember(ActionsSource, "private static void ReportStartPosRestoreTiming(");

        // The warm/cold selection is the one line that decides which pipeline ran. It is
        // once per load, so Diagnostic, which is written at the default logging level.
        Assert.Contains("AkronLog.Diagnostic(nameof(AkronActions),", body);
        Assert.Contains("(usedSnapshot ? \"cold\" : \"warm\")", body);
        Assert.Contains("AkronLog.Warn(nameof(AkronActions), message);", ActionsSource);
    }

    // Slices a member from its signature to the closing brace at type-member
    // indentation. Fixed-length windows silently stop asserting the moment a comment
    // grows, which is the failure mode these structural tests are most prone to.
    private static string SliceMember(string source, string signature) {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "member not found: " + signature);
        int end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        Assert.True(end > start, "member end not found: " + signature);
        return source.Substring(start, end - start);
    }

    private static int CountOccurrences(string source, string value) {
        int count = 0;
        int cursor = 0;
        while (true) {
            int index = source.IndexOf(value, cursor, StringComparison.Ordinal);
            if (index < 0) {
                return count;
            }
            count++;
            cursor = index + 1;
        }
    }
}
