using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AkronSharedStateCollection {
    public const string Name = "Akron shared process state";
}

[Collection(AkronSharedStateCollection.Name)]
public sealed class StartPosPersistenceTests {
    private static readonly FieldInfo RandomStackField = typeof(Calc).GetField(
        "randomStack",
        BindingFlags.Static | BindingFlags.NonPublic
    ) ?? throw new InvalidOperationException("Monocle.Calc.randomStack is unavailable.");
    private static readonly FieldInfo LevelWipeField = typeof(Level).GetField(
        nameof(Level.Wipe),
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    ) ?? throw new InvalidOperationException("Celeste.Level.Wipe field is unavailable.");
    private static readonly FieldInfo SceneRendererListBackingField = typeof(Scene).GetField(
        "<RendererList>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic
    ) ?? throw new InvalidOperationException("Monocle.Scene.RendererList backing field is unavailable.");

    [Fact]
    public void FreshRoomDrainFinishesEntitiesAddedDuringAwake() {
        List<Entity> entities = new List<Entity>();
        Queue<Entity> pending = new Queue<Entity>();
        pending.Enqueue((Entity) RuntimeHelpers.GetUninitializedObject(typeof(Entity)));
        int remainingChildren = 3;

        AkronSaveLoadService.DrainFreshRoomEntityListsCore(
            () => {
                if (pending.Count > 0) {
                    Entity entity = pending.Dequeue();
                    entities.Add(entity);
                    if (remainingChildren-- > 0) {
                        pending.Enqueue((Entity) RuntimeHelpers.GetUninitializedObject(typeof(Entity)));
                    }
                }
            },
            () => entities,
            () => pending.Count,
            _ => false);

        Assert.Equal(4, entities.Count);
        Assert.Empty(pending);
    }

    [Fact]
    public void FreshRoomDrainWaitsForComponentsQueuedDuringStartup() {
        List<Entity> entities = new List<Entity> {
            (Entity) RuntimeHelpers.GetUninitializedObject(typeof(Entity))
        };
        int componentDrainPasses = 0;

        AkronSaveLoadService.DrainFreshRoomEntityListsCore(
            () => { },
            () => entities,
            () => 0,
            _ => ++componentDrainPasses == 1);

        Assert.Equal(2, componentDrainPasses);
    }

    [Fact]
    public void AkronToastsAreExcludedFromRoomStateSnapshots() {
        string source = File.ReadAllText(GetSourcePath("Core", "AkronToast.cs"));

        Assert.Contains("AkronSaveLoadService.IgnoreSaveState(this);", source);
    }

    [Fact]
    public void RandomStateCanCaptureAnUninitializedGenerator() {
        AkronRandomState originalState = AkronRandomState.Capture();
        try {
            Calc.Random = null;

            AkronRandomState savedState = AkronRandomState.Capture();
            Calc.Random = new Random(12345);
            savedState.Restore();

            Assert.Null(Calc.Random);
        } finally {
            originalState.Restore();
        }
    }

    [Fact]
    public void RandomStateRestoresTheCurrentGeneratorAndNestedGeneratorStack() {
        AkronRandomState originalState = AkronRandomState.Capture();
        try {
            Calc.Random = new Random(12345);
            PushRandom(new Random(67890));
            AkronRandomState savedState = AkronRandomState.Capture();

            int[] expectedCurrent = Enumerable.Range(0, 8).Select(_ => Calc.Random.Next()).ToArray();
            PopRandom();
            int[] expectedParent = Enumerable.Range(0, 8).Select(_ => Calc.Random.Next()).ToArray();

            savedState.Restore();

            int[] actualCurrent = Enumerable.Range(0, 8).Select(_ => Calc.Random.Next()).ToArray();
            PopRandom();
            int[] actualParent = Enumerable.Range(0, 8).Select(_ => Calc.Random.Next()).ToArray();

            Assert.Equal(expectedCurrent, actualCurrent);
            Assert.Equal(expectedParent, actualParent);
        } finally {
            originalState.Restore();
        }
    }

    [Fact]
    public void StableRandomStateKeepsTheBaseGeneratorWithoutAnActiveTemporaryScope() {
        AkronRandomState originalState = AkronRandomState.Capture();
        try {
            Calc.Random = new Random(12345);
            PushRandom(new Random(67890));
            AkronRandomState savedState = AkronRandomState.CaptureStable();

            PopRandom();
            int[] expected = Enumerable.Range(0, 8).Select(_ => Calc.Random.Next()).ToArray();

            savedState.Restore();

            int[] actual = Enumerable.Range(0, 8).Select(_ => Calc.Random.Next()).ToArray();
            Assert.Equal(expected, actual);
            Assert.False(AkronRandomState.HasActiveScope);
        } finally {
            originalState.Restore();
        }
    }

    [Fact]
    public void StableRandomStateDoesNotDestroyAnActiveTemporaryScope() {
        AkronRandomState originalState = AkronRandomState.Capture();
        try {
            Calc.Random = new Random(12345);
            AkronRandomState savedState = AkronRandomState.CaptureStable();
            PushRandom(new Random(67890));
            Assert.True(AkronRandomState.HasActiveScope);

            savedState.Restore();

            Assert.True(AkronRandomState.HasActiveScope);
            Random expectedTemporaryRandom = new Random(67890);
            Assert.Equal(
                Enumerable.Range(0, 8).Select(_ => expectedTemporaryRandom.Next()).ToArray(),
                Enumerable.Range(0, 8).Select(_ => Calc.Random.Next()).ToArray());
            PopRandom();
            Assert.False(AkronRandomState.HasActiveScope);
            Random expectedBaseRandom = new Random(12345);
            Assert.Equal(
                Enumerable.Range(0, 8).Select(_ => expectedBaseRandom.Next()).ToArray(),
                Enumerable.Range(0, 8).Select(_ => Calc.Random.Next()).ToArray());
        } finally {
            originalState.Restore();
        }
    }

    private static void PushRandom(Random random) {
        Stack<Random> stack = GetRandomStack();
        stack.Push(Calc.Random);
        Calc.Random = random;
    }

    private static void PopRandom() {
        Stack<Random> stack = GetRandomStack();
        Calc.Random = stack.Pop();
    }

    private static Stack<Random> GetRandomStack() {
        if (RandomStackField.GetValue(null) is Stack<Random> stack) {
            return stack;
        }

        Stack<Random> initialized = new Stack<Random>();
        RandomStackField.SetValue(null, initialized);
        return initialized;
    }

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
    public void ReplacedWarmStateStaysAliveUntilItsDiskWorkerFinishes() {
        AkronSaveLoadSlot slot = new AkronSaveLoadSlot("slot", "room", "map", saveTimeAndDeaths: false);
        int releases = 0;
        AkronSaveLoadSlotOwner owner = new AkronSaveLoadSlotOwner(slot, _ => releases++);
        AkronSaveLoadSlotLease workerLease = owner.Retain();

        owner.ReleaseOwnership();
        Assert.Equal(0, releases);

        workerLease.Dispose();
        workerLease.Dispose();
        Assert.Equal(1, releases);
    }

    [Fact]
    public void ReleasingOneRuntimeSnapshotRetainsOtherTrackedVirtualAssets() {
        VirtualRenderTarget shared = (VirtualRenderTarget) RuntimeHelpers.GetUninitializedObject(typeof(VirtualRenderTarget));
        VirtualRenderTarget firstOnly = (VirtualRenderTarget) RuntimeHelpers.GetUninitializedObject(typeof(VirtualRenderTarget));
        VirtualRenderTarget secondOnly = (VirtualRenderTarget) RuntimeHelpers.GetUninitializedObject(typeof(VirtualRenderTarget));

        AkronVirtualAssetReloadTracker.Clear();
        try {
            int firstMarker = AkronVirtualAssetReloadTracker.Mark();
            AkronVirtualAssetReloadTracker.Add(shared);
            AkronVirtualAssetReloadTracker.Add(firstOnly);
            IReadOnlyList<AkronTrackedVirtualAssetRegistration> firstSnapshotAssets =
                AkronVirtualAssetReloadTracker.GetRegistrationsSince(firstMarker);

            int secondMarker = AkronVirtualAssetReloadTracker.Mark();
            AkronVirtualAssetReloadTracker.Add(shared);
            AkronVirtualAssetReloadTracker.Add(secondOnly);
            IReadOnlyList<AkronTrackedVirtualAssetRegistration> secondSnapshotAssets =
                AkronVirtualAssetReloadTracker.GetRegistrationsSince(secondMarker);

            AkronVirtualAssetReloadTracker.Remove(firstSnapshotAssets);

            Assert.Equal(2, AkronVirtualAssetReloadTracker.Count);
            Assert.Equal(
                new[] { shared, secondOnly },
                AkronVirtualAssetReloadTracker.GetRenderTargetsSince(0));

            AkronVirtualAssetReloadTracker.Remove(secondSnapshotAssets);
            Assert.Equal(0, AkronVirtualAssetReloadTracker.Count);
            Assert.Empty(AkronVirtualAssetReloadTracker.GetRenderTargetsSince(0));
        } finally {
            AkronVirtualAssetReloadTracker.Clear();
        }
    }

    [Fact]
    public void StaleSnapshotOwnershipCannotRemoveAssetsRegisteredAfterReload() {
        VirtualRenderTarget shared = (VirtualRenderTarget) RuntimeHelpers.GetUninitializedObject(typeof(VirtualRenderTarget));

        AkronVirtualAssetReloadTracker.Clear();
        try {
            int loadedSlotMarker = AkronVirtualAssetReloadTracker.Mark();
            AkronVirtualAssetReloadTracker.Add(shared);
            IReadOnlyList<AkronTrackedVirtualAssetRegistration> loadedSlotAssets =
                AkronVirtualAssetReloadTracker.GetRegistrationsSince(loadedSlotMarker);

            int otherSlotMarker = AkronVirtualAssetReloadTracker.Mark();
            AkronVirtualAssetReloadTracker.Add(shared);
            IReadOnlyList<AkronTrackedVirtualAssetRegistration> otherSlotAssets =
                AkronVirtualAssetReloadTracker.GetRegistrationsSince(otherSlotMarker);

            // A warm Load consumes and clears the old reload batch, then its
            // replacement pre-clone registers the same process-owned object.
            AkronVirtualAssetReloadTracker.Clear();
            int replacementMarker = AkronVirtualAssetReloadTracker.Mark();
            AkronVirtualAssetReloadTracker.Add(shared);
            IReadOnlyList<AkronTrackedVirtualAssetRegistration> replacementAssets =
                AkronVirtualAssetReloadTracker.GetRegistrationsSince(replacementMarker);

            AkronVirtualAssetReloadTracker.Remove(loadedSlotAssets);
            AkronVirtualAssetReloadTracker.Remove(otherSlotAssets);

            Assert.Equal(1, AkronVirtualAssetReloadTracker.Count);
            Assert.Equal(new[] { shared }, AkronVirtualAssetReloadTracker.GetRenderTargetsSince(0));

            AkronVirtualAssetReloadTracker.Remove(replacementAssets);
            Assert.Equal(0, AkronVirtualAssetReloadTracker.Count);
        } finally {
            AkronVirtualAssetReloadTracker.Clear();
        }
    }

    [Fact]
    public void PerSlotClearDoesNotResetOtherVirtualAssetRegistrations() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int clearAll = source.IndexOf("public static void ClearRuntimeState()", StringComparison.Ordinal);
        int clearAllEnd = source.IndexOf("public static object RegisterSaveLoadAction", clearAll, StringComparison.Ordinal);
        string clearAllPath = SourceSlice(source, clearAll, clearAllEnd - clearAll);
        int clearActions = source.IndexOf("private static void RunClearStateActions()", StringComparison.Ordinal);
        int clearActionsEnd = source.IndexOf("private static void SaveStaticMemberValues", clearActions, StringComparison.Ordinal);
        string clearActionsPath = SourceSlice(source, clearActions, clearActionsEnd - clearActions);

        Assert.Contains("runtimeSlot.ReleaseOwnership()", clearAllPath);
        Assert.Contains("AkronVirtualAssetReloadTracker.Clear()", clearAllPath);
        Assert.DoesNotContain("AkronVirtualAssetReloadTracker.Clear()", clearActionsPath);
    }

    [Fact]
    public void WarmLoadReplacesItsTrackedPreCloneAssets() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int runtimeRestore = source.IndexOf("public static AkronSaveLoadResult RestoreRuntimeState", StringComparison.Ordinal);
        int runtimeRestoreEnd = source.IndexOf("public static AkronSaveLoadResult LoadRuntimeState", runtimeRestore, StringComparison.Ordinal);
        string runtimeRestorePath = SourceSlice(source, runtimeRestore, runtimeRestoreEnd - runtimeRestore);
        int helper = source.IndexOf("private static void PrepareRuntimeSlotPreClone", StringComparison.Ordinal);
        int helperEnd = source.IndexOf("private static bool RestoreNativeSlot", helper, StringComparison.Ordinal);
        string helperPath = SourceSlice(source, helper, helperEnd - helper);

        Assert.Contains("PrepareRuntimeSlotPreClone(saveSlot)", runtimeRestorePath);
        Assert.Contains("AkronVirtualAssetReloadTracker.Mark()", helperPath);
        Assert.Contains("PrepareSlotPreClone(saveSlot)", helperPath);
        Assert.Contains(
            "saveSlot.TrackedVirtualAssetRegistrations =",
            helperPath);
        Assert.Contains("AkronVirtualAssetReloadTracker.GetRegistrationsSince(virtualAssetMarker)", helperPath);
        Assert.Contains("AkronVirtualAssetReloadTracker.DiscardSince(virtualAssetMarker)", helperPath);
    }

    [Fact]
    public void StartPosRestartUsesTheExactReconstructionSnapshot() {
        string source = File.ReadAllText(GetActionsSourcePath());
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());
        string persistenceSource = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));

        Assert.DoesNotContain("AkronPersistentStartPosSnapshots", source);
        Assert.Contains("PersistRuntimeStateSnapshot", persistenceSource);
        Assert.Contains("AkronStartPosReconstruction.Capture", saveLoadSource);
        Assert.Contains("AkronStartPosReconstruction.Restore", saveLoadSource);
        Assert.Contains("AkronStartPosReconstruction.HasSnapshot", saveLoadSource);
        Assert.DoesNotContain("AkronSaveLoadService.HydrateRuntimeState(", source);
        Assert.DoesNotContain("AkronStartPosReplayService", source);
        Assert.Contains("AkronSaveLoadService.HasRuntimeState(startPos.StateSlotName)", source);
    }

    [Fact]
    public void PersistentStartPosSnapshotsAreIsolatedByCelesteFileSlot() {
        string firstFile = AkronActions.GetStartPosStateSlotName("Tests/SameMap", 3, 0);
        string secondFile = AkronActions.GetStartPosStateSlotName("Tests/SameMap", 3, 1);

        Assert.NotEqual(firstFile, secondFile);
        Assert.NotEqual(
            AkronStartPosReconstruction.GetSnapshotPath(firstFile),
            AkronStartPosReconstruction.GetSnapshotPath(secondFile));
    }

    // Snapshot files are addressed by the slot's identity, never by the state they
    // hold, so two slots carrying the same room in the same frame own two files and
    // clearing one cannot take the other's away. W9 saw four snapshot files vanish
    // when it cleared its own slots and read that as slots sharing one file; what it
    // actually did was re-set the same (file slot, map, slot index) triples an earlier
    // pass had left orphaned on disk, which overwrote those files before deleting them.
    // Hashing the content instead would make that reading true, so this guards the
    // addressing rule end to end rather than only comparing two paths.
    [Fact]
    public void SnapshotFilesAreAddressedBySlotIdentityRatherThanByTheStateTheyHold() {
        string sharedMap = "Tests/SharedSnapshot" + Guid.NewGuid().ToString("N");
        string firstSlot = AkronActions.GetStartPosStateSlotName(sharedMap, 1, 0);
        string secondSlot = AkronActions.GetStartPosStateSlotName(sharedMap, 2, 0);
        string firstPath = AkronStartPosReconstruction.GetSnapshotPath(firstSlot);
        string secondPath = AkronStartPosReconstruction.GetSnapshotPath(secondSlot);

        Assert.NotEqual(firstPath, secondPath);
        try {
            // Same map, same room, same file slot, same document shape: two slots set on
            // one frame in one room, which is exactly the case W9 believed would share.
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                firstSlot, sharedMap, "same-room", 0, MinimalDocument(), out string firstError), firstError);
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                secondSlot, sharedMap, "same-room", 0, MinimalDocument(), out string secondError), secondError);
            Assert.True(File.Exists(firstPath));
            Assert.True(File.Exists(secondPath));

            // Clearing slot 1 is the operation that was suspected of destroying slot 2.
            AkronSaveLoadService.ClearRuntimeState(firstSlot);

            Assert.False(File.Exists(firstPath));
            Assert.True(File.Exists(secondPath));
            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                secondSlot, out AkronReconstructionDocument survivor, out string loadError), loadError);
            Assert.Equal("same-room", survivor.Room);
            Assert.Equal(secondSlot, survivor.SlotName);
        } finally {
            AkronStartPosReconstruction.DeleteSnapshot(firstSlot);
            AkronStartPosReconstruction.DeleteSnapshot(secondSlot);
        }
    }

    // The three tests below are the snapshot half of the v7 -> v8 format bump.
    //
    // Structural paths in a document are measured against a clean reload of the room,
    // and two changes altered what that reload produces: the trail clear in
    // TryLoadFreshRoom and the PlayerPlayback capture exclusion. A pre-bump document
    // therefore counts objects the current fresh room no longer contains, and the shift
    // can hand one entity another same-typed entity's saved state instead of refusing.
    // So old and new documents are not interchangeable and an old one must be stopped
    // before it reaches the reconstruction path at all.

    // Writes what an older Akron left on disk. The document shape did not change across
    // this bump - the fresh room it is measured against did - so the faithful way to
    // build one is to write a current document and stamp the older format on it.
    private static void WriteSnapshotWithFormat(string path, string format) {
        string json = AkronStartPosReconstruction.Serialize(MinimalDocument())
            .Replace(AkronReconstructionDocument.CurrentFormat, format, StringComparison.Ordinal);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using GZipStream compressed = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: false);
        using StreamWriter writer = new StreamWriter(compressed, new UTF8Encoding(false));
        writer.Write(json);
    }

    // The path an older Akron addressed this slot by. Derived from the current name so
    // it cannot drift from the real naming rule: the prefix is the format version and
    // the rest is the digest of the slot name, which this bump did not change.
    private static string SupersededSnapshotPath(string slotName, string? directory) {
        string currentPath = AkronStartPosReconstruction.GetSnapshotPath(slotName, directory);
        string currentFileName = Path.GetFileName(currentPath);
        return Path.Combine(
            Path.GetDirectoryName(currentPath)!,
            "v7-" + currentFileName.Substring(currentFileName.IndexOf('-') + 1));
    }

    [Fact]
    public void ASnapshotFromAnOlderAkronIsRefusedAndSaysWhatToDoAboutIt() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-format-" + Guid.NewGuid().ToString("N"));
        string slotName = "Akron StartPos format " + Guid.NewGuid().ToString("N");
        try {
            // Written at the name the current build reads, which is the case the file
            // name alone cannot stop: a restored backup, a hand-placed file, or a
            // snapshot inside a pack. The document header has to be the thing that
            // refuses, and it has to refuse before Restore sees the document.
            WriteSnapshotWithFormat(
                AkronStartPosReconstruction.GetSnapshotPath(slotName, directory),
                "akron-reconstruction-v7");

            bool loaded = AkronStartPosReconstruction.TryLoadSnapshot(
                slotName, out AkronReconstructionDocument document, out string error, directory);

            Assert.False(loaded);
            Assert.Null(document);
            Assert.Contains("akron-reconstruction-v7", error);
            Assert.Contains(AkronReconstructionDocument.CurrentFormat, error);
            Assert.Contains("set this StartPos again", error);
            Assert.Contains("fresh room this build no longer loads", error);
            // ReportStartPosLoadFailure cuts the toast at 180 characters, so the action
            // has to survive the cut rather than sit behind the two format names.
            Assert.True(
                error.IndexOf("set this StartPos again", StringComparison.Ordinal) <
                error.IndexOf(AkronReconstructionDocument.CurrentFormat, StringComparison.Ordinal));
        } finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ASlotWhoseSnapshotPredatesTheFormatBumpIsNotVisibleToTheCurrentBuild() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-format-" + Guid.NewGuid().ToString("N"));
        string slotName = "Akron StartPos superseded " + Guid.NewGuid().ToString("N");
        try {
            WriteSnapshotWithFormat(SupersededSnapshotPath(slotName, directory), "akron-reconstruction-v7");

            // The file is on disk under the previous format's name, and nothing this
            // build does can see it: HasSnapshot builds the current name, so the slot
            // is dropped from the list by BuildRuntimeStartPositions and the load path
            // is never reached. What the player is told about the move comes from the
            // catalog entry instead, which is DescribeMissingStartPos.
            Assert.False(AkronStartPosReconstruction.HasSnapshot(slotName, directory));
        } finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // The release note that tells a player to set their slots again names both contracts
    // in prose, and nothing else in the tree ties that prose to the constants. A move
    // that edits the constants and leaves the note alone has already happened once: the
    // sentence said the saved-state contract had moved to akron-reconstruction-v8 and
    // the pack contract to akron-setup-v5 while the build wrote v9 and v6, so the one
    // instruction a player is given named a version this build never writes.
    //
    // What holds that instruction current is the newest section that names a contract
    // at all: sections are ordered newest first, so the first mention a reader meets is
    // the one that must match the constants. Older sections name the contracts their
    // own release shipped, which is what a changelog is for, and a section about
    // something else entirely has no contract prose to drift.
    [Fact]
    public void TheNewestChangelogContractMentionNamesTheContractsThisBuildActuallyWrites() {
        string changelog = File.ReadAllText(GetRepositoryFilePath("CHANGELOG.md"));
        Assert.Contains("## Unreleased", changelog);
        AssertNewestMentionIsCurrent(changelog, "akron-reconstruction-v", AkronReconstructionDocument.CurrentFormat);
        AssertNewestMentionIsCurrent(changelog, "akron-setup-v", AkronSetupPacks.SetupPackFormat);
    }

    private static void AssertNewestMentionIsCurrent(string changelog, string contractPrefix, string currentFormat) {
        string[] sections = changelog.Split("\n## ", StringSplitOptions.None);
        string? newestMention = sections.FirstOrDefault(section =>
            section.Contains(contractPrefix, StringComparison.Ordinal));
        Assert.True(newestMention != null, "CHANGELOG.md never names " + contractPrefix + "*.");
        Assert.Contains(currentFormat, newestMention);
    }

    [Fact]
    public void ACurrentSnapshotStillRoundTripsAfterTheFormatBump() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-format-" + Guid.NewGuid().ToString("N"));
        string slotName = "Akron StartPos current " + Guid.NewGuid().ToString("N");
        try {
            Directory.CreateDirectory(directory);
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                slotName, "Tests/FormatBump", "room", 3, MinimalDocument(), out string saveError, directory), saveError);

            Assert.True(AkronStartPosReconstruction.HasSnapshot(slotName, directory));

            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                slotName, out AkronReconstructionDocument document, out string loadError, directory), loadError);
            Assert.Equal(AkronReconstructionDocument.CurrentFormat, document.Format);
            Assert.Equal("akron-reconstruction-v10", document.Format);
            Assert.Equal(slotName, document.SlotName);
            Assert.Equal("Tests/FormatBump", document.MapSid);
            Assert.Equal("room", document.Room);
            Assert.Equal(3, document.FileSlot);
        } finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ASlotTheFormatBumpLeftBehindIsNotReportedAsASlotThatWasNeverSaved() {
        // The slot list keeps only slots HasRuntimeState answers for, so every slot set
        // before the bump vanishes from it rather than failing to load. This sentence is
        // the one the player actually gets after an update, which makes it the one that
        // has to name the fix.
        //
        // A headless test has no loaded save file, so the catalog is empty and only the
        // never-set branch can run here. Leaving a superseded file on disk must not move
        // it: the file is swept, the catalog is not, and reading the file back would put
        // the message on evidence that is about to be deleted.
        const int slot = 9998;
        string stateSlotName = AkronActions.GetStartPosStateSlotName(string.Empty, slot);
        string supersededPath = SupersededSnapshotPath(stateSlotName, null);
        try {
            Assert.Equal(
                "No StartPos saved in slot 9998.",
                AkronActions.DescribeMissingStartPos(null, slot));

            WriteSnapshotWithFormat(supersededPath, "akron-reconstruction-v7");

            Assert.Equal(
                "No StartPos saved in slot 9998.",
                AkronActions.DescribeMissingStartPos(null, slot));
        } finally {
            if (File.Exists(supersededPath)) {
                File.Delete(supersededPath);
            }
        }

        // Finding the catalog needs a loaded save file; reading one does not. The
        // sentence is chosen from the catalog alone, so it is exercised against a real
        // catalog here and only the lookup is pinned in the source.
        string source = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-actions.cs"));
        int describe = source.IndexOf(
            "internal static string DescribeMissingStartPos(Level level, int slot)", StringComparison.Ordinal);
        int describeEnd = source.IndexOf(
            "internal static string DescribeMissingStartPos(", describe + 1, StringComparison.Ordinal);
        string describeMethod = SourceSlice(source, describe, describeEnd - describe);

        Assert.Contains(
            "DescribeMissingStartPos(slot, GetPersistedStartPositions(GetAreaSid(level)))",
            describeMethod);
        Assert.DoesNotContain("HasSupersededSnapshot", describeMethod);

        // And the direct load is what asks for it. A sentence no path reaches has
        // shipped here before, so both callers are pinned rather than assumed: this
        // one, and Previous and Next below.
        int load = source.IndexOf("public static void LoadStartPos(Level level)", StringComparison.Ordinal);
        int loadEnd = source.IndexOf("public static void LoadStartPosSlot(", load, StringComparison.Ordinal);

        Assert.Contains(
            "new AkronToast(DescribeMissingStartPos(level, slot))",
            SourceSlice(source, load, loadEnd - load));
    }

    // The catalog a save file would hold for one map, with each slot's state recorded
    // under the format named for it. A real AkronPersistedStartPos in a real dictionary,
    // which is what the message reads in game.
    private static Dictionary<int, AkronPersistedStartPos> CatalogWithFormats(
        params (int Slot, string SnapshotFormat)[] slots
    ) {
        Dictionary<int, AkronPersistedStartPos> catalog = new Dictionary<int, AkronPersistedStartPos>();
        foreach ((int slot, string snapshotFormat) in slots) {
            catalog[slot] = new AkronPersistedStartPos {
                AreaSid = "Tests/Catalog",
                Room = "room",
                SnapshotFormat = snapshotFormat
            };
        }
        return catalog;
    }

    // The format one move below the one this build writes, derived rather than written
    // down so this keeps meaning "the previous format" after the next move.
    private static string PreviousSnapshotFormat() {
        string current = AkronReconstructionDocument.CurrentFormat;
        int digits = current.Length;
        while (digits > 0 && current[digits - 1] >= '0' && current[digits - 1] <= '9') {
            digits--;
        }
        int version = int.Parse(current.Substring(digits), CultureInfo.InvariantCulture);
        Assert.True(version > 1, "the current saved-state format has no predecessor to name");
        return current.Substring(0, digits) + (version - 1).ToString(CultureInfo.InvariantCulture);
    }

    private static string NextSnapshotFormat() {
        string current = AkronReconstructionDocument.CurrentFormat;
        int digits = current.Length;
        while (digits > 0 && current[digits - 1] >= '0' && current[digits - 1] <= '9') {
            digits--;
        }
        return current.Substring(0, digits) +
               (int.Parse(current.Substring(digits), CultureInfo.InvariantCulture) + 1)
                   .ToString(CultureInfo.InvariantCulture);
    }

    [Fact]
    public void ASlotEmptiedByAFormatMoveNamesTheMoveAndASlotEmptiedAnyOtherWayDoesNot() {
        // The sentence a format move earns. It says what happened and what to do, and
        // the catalog is what still knows it once the sweep has taken the file.
        Assert.Equal(
            "StartPos 3 was saved by an older Akron that built rooms differently, so it cannot be loaded. Set it again.",
            AkronActions.DescribeMissingStartPos(3, CatalogWithFormats((3, PreviousSnapshotFormat()))));

        // Every slot set before the format was recorded at all. This is what an install
        // that upgrades into this build actually holds, and it is the whole population
        // the sentence exists for, so it has to reach the same answer.
        Assert.Equal(
            "StartPos 3 was saved by an older Akron that built rooms differently, so it cannot be loaded. Set it again.",
            AkronActions.DescribeMissingStartPos(3, CatalogWithFormats((3, string.Empty))));

        // The format has not moved under this slot, so whatever took its state was not
        // an update: a file deleted by hand, a backup restored over the folder, a write
        // that never landed. Claiming an update here would be a lie, which is the one
        // thing this message may not be.
        Assert.Equal(
            "StartPos 3 was set, but the state behind it is missing. Set it again.",
            AkronActions.DescribeMissingStartPos(
                3, CatalogWithFormats((3, AkronReconstructionDocument.CurrentFormat))));

        // A slot written by a newer build the player has downgraded away from is
        // unreadable here too, and the sweep leaves its file alone. Older is the wrong
        // word for it.
        Assert.Equal(
            "StartPos 3 was set, but the state behind it is missing. Set it again.",
            AkronActions.DescribeMissingStartPos(3, CatalogWithFormats((3, NextSnapshotFormat()))));

        // A slot the player never set keeps the plainer sentence, whatever the other
        // slots on the map did.
        Assert.Equal(
            "No StartPos saved in slot 4.",
            AkronActions.DescribeMissingStartPos(4, CatalogWithFormats((3, PreviousSnapshotFormat()))));
        Assert.Equal(
            "No StartPos saved in slot 3.",
            AkronActions.DescribeMissingStartPos(3, new Dictionary<int, AkronPersistedStartPos>()));
    }

    // The name a build older than this one addressed the same slot by. Same derivation
    // as SupersededSnapshotPath, with the version chosen by the caller so the sweep can
    // be shown to answer for every version below the current one rather than for v7
    // alone.
    private static string SnapshotPathWithVersion(string slotName, string? directory, string version) {
        string currentPath = AkronStartPosReconstruction.GetSnapshotPath(slotName, directory);
        string currentFileName = Path.GetFileName(currentPath);
        return Path.Combine(
            Path.GetDirectoryName(currentPath)!,
            version + currentFileName.Substring(currentFileName.IndexOf('-')));
    }

    // The version prefix one above the one this build writes, read out of a current
    // file name so the fixture follows the format instead of pinning a number that a
    // bump would turn into the current one.
    private static string NextSnapshotVersion(string currentFileName) {
        string prefix = currentFileName.Substring(0, currentFileName.IndexOf('-'));
        int digits = 0;
        while (digits < prefix.Length && (prefix[digits] < '0' || prefix[digits] > '9')) {
            digits++;
        }
        return prefix.Substring(0, digits) +
               (int.Parse(prefix.Substring(digits), CultureInfo.InvariantCulture) + 1)
                   .ToString(CultureInfo.InvariantCulture);
    }

    [Fact]
    public void SnapshotsFromAnOlderFormatAreSweptAndTheCurrentOneIsLeftLoadable() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-sweep-" + Guid.NewGuid().ToString("N"));
        string liveSlot = "Akron StartPos live " + Guid.NewGuid().ToString("N");
        string deadSlot = "Akron StartPos dead " + Guid.NewGuid().ToString("N");
        try {
            Directory.CreateDirectory(directory);
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                liveSlot, "Tests/Sweep", "room", 0, MinimalDocument(), out string saveError, directory), saveError);
            // Three versions below the current one, so the sweep is shown to answer for
            // the scheme rather than for the single version this bump moved off.
            string v5 = SnapshotPathWithVersion(deadSlot, directory, "v5");
            string v6 = SnapshotPathWithVersion(deadSlot, directory, "v6");
            string v7 = SnapshotPathWithVersion(liveSlot, directory, "v7");
            WriteSnapshotWithFormat(v5, "akron-reconstruction-v5");
            WriteSnapshotWithFormat(v6, "akron-reconstruction-v6");
            WriteSnapshotWithFormat(v7, "akron-reconstruction-v7");

            (int files, long bytes) = AkronStartPosPersistence.SweepSupersededSnapshots(directory);

            Assert.Equal(3, files);
            Assert.True(bytes > 0);
            Assert.False(File.Exists(v5));
            Assert.False(File.Exists(v6));
            // The superseded copy of a slot that also has a current one goes too, and
            // the current one it sits next to has to survive its removal.
            Assert.False(File.Exists(v7));
            Assert.True(AkronStartPosReconstruction.HasSnapshot(liveSlot, directory));
            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                liveSlot, out AkronReconstructionDocument document, out string loadError, directory), loadError);
            Assert.Equal(AkronReconstructionDocument.CurrentFormat, document.Format);

            // Idempotent: a second launch finds nothing left to do.
            Assert.Equal((0, 0L), AkronStartPosPersistence.SweepSupersededSnapshots(directory));
        } finally {
            AkronStartPosReconstruction.ResetSnapshotExistenceCache();
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void TheSweepRemovesNothingThatIsNotAnOlderSnapshotOfThisExactScheme() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-sweep-" + Guid.NewGuid().ToString("N"));
        string slotName = "Akron StartPos shapes " + Guid.NewGuid().ToString("N");
        try {
            Directory.CreateDirectory(directory);
            string currentPath = AkronStartPosReconstruction.GetSnapshotPath(slotName, directory);
            string currentFileName = Path.GetFileName(currentPath);
            string digestAndSuffix = currentFileName.Substring(currentFileName.IndexOf('-') + 1);
            int digestLength = digestAndSuffix.IndexOf('.');
            string suffix = digestAndSuffix.Substring(digestLength);

            // Every one of these is a near miss the sweep must not take.
            string[] keep = {
                // The name this build writes.
                currentPath,
                // The temporary file a write lands through, which a prefix-only test
                // would match and a copy in flight would then lose.
                currentPath + "." + Guid.NewGuid().ToString("N") + ".tmp",
                // A newer build's snapshot, seen by an older build after a downgrade.
                // Derived from the current version so it stays one above it.
                Path.Combine(directory, NextSnapshotVersion(currentFileName) + "-" + digestAndSuffix),
                // Renamed by hand: nothing Akron writes carries an upper-case version.
                Path.Combine(directory, "V7-" + digestAndSuffix),
                // Right length, wrong alphabet where the digest belongs.
                Path.Combine(directory, "v7-" + new string('z', digestLength) + suffix),
                // The scheme without a version number at all.
                Path.Combine(directory, "backup-" + digestAndSuffix),
                // Not this scheme.
                Path.Combine(directory, "notes.txt")
            };
            foreach (string path in keep) {
                File.WriteAllText(path, "keep");
            }
            // A staging directory under the snapshot folder. EnumerateFiles is top level
            // only, and an import in flight owns what is inside one.
            string staging = Path.Combine(directory, ".import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            string stagedOldFormat = Path.Combine(staging, "v7-" + digestAndSuffix);
            File.WriteAllText(stagedOldFormat, "keep");

            Assert.Equal((0, 0L), AkronStartPosPersistence.SweepSupersededSnapshots(directory));

            foreach (string path in keep) {
                Assert.True(File.Exists(path), path);
            }
            Assert.True(File.Exists(stagedOldFormat));
        } finally {
            AkronStartPosReconstruction.ResetSnapshotExistenceCache();
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void TheSweepIsANoOpWhenThereIsNoSnapshotFolderToRead() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-sweep-missing-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(directory));

        // A first launch has no snapshot folder. The sweep only ever reclaims disk, so
        // there is nothing here to report as a failure.
        Assert.Equal((0, 0L), AkronStartPosPersistence.SweepSupersededSnapshots(directory));
    }

    [Fact]
    public void TheSweepReadsTheCurrentFormatVersionFromTheWriterRatherThanFromALiteral() {
        string source = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        int sweep = source.IndexOf(
            "internal static (int Files, long Bytes) SweepSupersededSnapshots(", StringComparison.Ordinal);
        int sweepEnd = source.IndexOf("private static bool TryReadSnapshotNaming(", sweep, StringComparison.Ordinal);
        string sweepSource = SourceSlice(source, sweep, sweepEnd - sweep);

        // A version literal here would keep matching after the format moved off it,
        // which is a live snapshot deleted on the first launch of the next build. The
        // shape comes from GetSnapshotPath, so it moves when the writer moves. The
        // comments name the current version to explain that, so only the code is
        // asserted on.
        Assert.Contains("AkronStartPosReconstruction.GetSnapshotPath(string.Empty, directory)", sweepSource);
        string sweepCode = string.Join(
            "\n",
            sweepSource.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        // The version prefix and the extension are read out of the writer at run time,
        // so this fails on whatever the current version happens to be rather than on a
        // number this test would have to be edited to keep up with.
        string currentFileName = Path.GetFileName(AkronStartPosReconstruction.GetSnapshotPath("probe"));
        Assert.DoesNotContain(
            currentFileName.Substring(0, currentFileName.IndexOf('-') + 1), sweepCode);
        Assert.DoesNotContain(currentFileName.Substring(currentFileName.IndexOf('.')), sweepCode);
        Assert.DoesNotContain(AkronReconstructionDocument.CurrentFormat, sweepCode);

        // And it runs once per Start, off the game thread, inside a catch. Nothing awaits
        // that task, so a throw out of it would be an unobserved Task rather than anything
        // anyone sees, and the sweep only ever reclaims disk space.
        int start = source.IndexOf("public static void Start()", StringComparison.Ordinal);
        int startEnd = source.IndexOf(
            "internal static (int Files, long Bytes) SweepSupersededSnapshots(", start, StringComparison.Ordinal);
        string startBody = SourceSlice(source, start, startEnd - start);
        int task = startBody.IndexOf("Task.Run(static () => {", StringComparison.Ordinal);
        int swept = startBody.IndexOf("SweepSupersededSnapshots();", task + 1, StringComparison.Ordinal);
        int guard = startBody.IndexOf("} catch (Exception exception) {", swept, StringComparison.Ordinal);
        Assert.True(task >= 0, "The sweep does not run off the game thread.");
        Assert.True(swept > task);
        Assert.True(guard > swept, "The sweep is not guarded inside its own task.");
    }

    [Fact]
    public void PreviousAndNextStartPosSayWhenTheFormatBumpEmptiedTheChapter() {
        string source = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-actions.cs"));
        int shift = source.IndexOf("public static void ShiftStartPos(Level level, int delta)", StringComparison.Ordinal);
        int shiftEnd = source.IndexOf("public static IReadOnlyList<AkronStartPosEntry> GetStartPositions(", shift, StringComparison.Ordinal);
        string shiftMethod = SourceSlice(source, shift, shiftEnd - shift);

        // Previous and Next are load actions too, and they hit the emptied list rather
        // than a missing slot, so they need their own sentence. Which sentence is
        // exercised below; that Previous and Next are what ask for it is pinned here,
        // because reaching them needs a Level and a save file.
        Assert.Contains("DescribeEmptyStartPosList(level)", shiftMethod);

        // The map-level sentence is on the catalog for the same reason the slot-level
        // one is: the leftover file it used to read is swept once nothing can read it.
        int describeEmpty = source.IndexOf(
            "internal static string DescribeEmptyStartPosList(Level level)", StringComparison.Ordinal);
        int describeEmptyEnd = source.IndexOf(
            "internal static string DescribeEmptyStartPosList(IReadOnlyDictionary",
            describeEmpty,
            StringComparison.Ordinal);
        string describeEmptyMethod = SourceSlice(source, describeEmpty, describeEmptyEnd - describeEmpty);

        Assert.Contains(
            "DescribeEmptyStartPosList(GetPersistedStartPositions(GetAreaSid(level)))", describeEmptyMethod);
        Assert.DoesNotContain("HasSupersededSnapshot", describeEmptyMethod);

        // A map whose every slot predates the current format. A move takes the whole
        // map at once, so this is the shape the sentence is for.
        Assert.Equal(
            "This chapter's StartPos slots were saved by an older Akron that built rooms differently. Set them again.",
            AkronActions.DescribeEmptyStartPosList(
                CatalogWithFormats((1, PreviousSnapshotFormat()), (2, string.Empty))));

        // Nothing on the map lost its state to a move, so nothing here may say one
        // happened.
        Assert.Equal(
            "This chapter's StartPos slots were set, but the states behind them are missing. Set them again.",
            AkronActions.DescribeEmptyStartPosList(
                CatalogWithFormats(
                    (1, AkronReconstructionDocument.CurrentFormat),
                    (2, AkronReconstructionDocument.CurrentFormat))));

        // One slot on the map lost its state some other way. This sentence covers the
        // slots together, so naming a move would be false for that one.
        Assert.Equal(
            "This chapter's StartPos slots were set, but the states behind them are missing. Set them again.",
            AkronActions.DescribeEmptyStartPosList(
                CatalogWithFormats(
                    (1, PreviousSnapshotFormat()),
                    (2, AkronReconstructionDocument.CurrentFormat))));

        // A chapter the player never set a slot in keeps the plainer sentence.
        Assert.Equal(
            "No StartPos entries in this chapter.",
            AkronActions.DescribeEmptyStartPosList(new Dictionary<int, AkronPersistedStartPos>()));
    }

    [Fact]
    public void SettingAStartPosRecordsTheFormatItsStateWasWrittenUnder() {
        // The stamp is what the two sentences read, and it is only ever written here.
        // A slot set by this build must carry this build's format, or every slot the
        // player sets would be reported as one an update emptied.
        string source = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-actions.cs"));
        int convert = source.IndexOf(
            "private static AkronPersistedStartPos ToPersistedStartPos(AkronStartPos startPos)",
            StringComparison.Ordinal);
        int convertEnd = source.IndexOf("internal static bool SaveAkronStartPosData(", convert, StringComparison.Ordinal);
        string convertMethod = SourceSlice(source, convert, convertEnd - convert);

        Assert.Contains("SnapshotFormat = AkronReconstructionDocument.CurrentFormat", convertMethod);

        // And the format is read out of the writer rather than written down, here and
        // in the comparison, so a move needs no edit in either place.
        int compare = source.IndexOf(
            "private static bool WasSavedByAnOlderAkron(AkronPersistedStartPos entry)", StringComparison.Ordinal);
        int compareEnd = source.IndexOf("public static void LoadStartPos(", compare, StringComparison.Ordinal);
        string compareCode = string.Join(
            "\n",
            SourceSlice(source, compare, compareEnd - compare)
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.DoesNotContain(AkronReconstructionDocument.CurrentFormat, compareCode);
    }

    // The load path is not allowed to name a cause for a missing restart copy, because
    // it cannot know one. A slot emptied by a format move never reaches it: the slot
    // list keeps only slots HasRuntimeState answers for, and HasRuntimeState builds the
    // current file name, so the move drops the slot before a Load can be pressed and
    // the sentence the player gets is DescribeMissingStartPos out of the catalog. That
    // catalog entry records the format its state was written under and compares it as a
    // number, which is the only place in the tree entitled to say "older".
    //
    // A disk probe here could only answer "a file for this slot exists under some other
    // version", and after SweepSupersededSnapshots has run at Start the only such file
    // left is a NEWER one, so the sentence would tell a player who downgraded that their
    // copy is old. A Load cannot run without a game, so this is asserted in the source
    // the way this file already asserts the fresh-room reload's ordering.
    [Fact]
    public void ALoadThatCannotFindARestartCopyDoesNotGuessWhyItIsGone() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int load = source.IndexOf("public static AkronSaveLoadResult LoadRuntimeState(", StringComparison.Ordinal);
        int loadEnd = source.IndexOf("internal static bool HasRuntimeStateInMemory(", load, StringComparison.Ordinal);
        string loadMethod = SourceSlice(source, load, loadEnd - load);

        Assert.Contains(
            "LastPersistentSnapshotError = \"no restart copy of this StartPos exists on disk\";",
            loadMethod);
        Assert.DoesNotContain("written by an older Akron", loadMethod);
        // And nothing anywhere reads the snapshot folder to decide it, so the sentence
        // cannot come back by another name.
        Assert.DoesNotContain(
            "HasSupersededSnapshot",
            File.ReadAllText(GetSourcePath("SaveLoad", "akron-reconstruction-graph.cs")));
    }

    // Installs one warm StartPos slot of a stated size, with a real snapshot on disk so
    // it is droppable, and returns its state slot name. The caller clears it.
    private static string AddWarmStartPosSlotWithSnapshot(string mapSid, int slot, long bytes) {
        string stateSlotName = AkronActions.GetStartPosStateSlotName(mapSid, slot, 0);
        Assert.True(AkronStartPosReconstruction.SaveSnapshot(
            stateSlotName, mapSid, "room", 0, MinimalDocument(), out string error), error);
        AkronSaveLoadService.AddWarmStartPosSlotForTests(stateSlotName, mapSid, bytes);
        return stateSlotName;
    }

    [Fact]
    public void ARepeatedSetIsRefusedBeforeWarmSlotsAreEvicted() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int capture = source.IndexOf("private static void CaptureStartPos", StringComparison.Ordinal);
        int captureEnd = source.IndexOf("private static void TrimWarmStartPosSlotsAndReport", capture, StringComparison.Ordinal);
        string body = SourceSlice(source, capture, captureEnd - capture);

        int rollbackRefusal = body.IndexOf("StartPosRollbacks.ContainsKey(stateSlotName)", StringComparison.Ordinal);
        int refusalReturn = body.IndexOf("return;", rollbackRefusal, StringComparison.Ordinal);
        int prepareBudget = body.IndexOf("PrepareWarmStartPosCapture", StringComparison.Ordinal);
        int beginRollback = body.IndexOf("BeginStartPosRollback", StringComparison.Ordinal);

        Assert.True(
            rollbackRefusal >= 0 &&
            refusalReturn > rollbackRefusal &&
            refusalReturn < prepareBudget &&
            beginRollback > prepareBudget);
    }

    // The ceiling on warm StartPos clones has to be denominated in bytes, not in slots.
    // Four slots the size of a Heart of the Storm clone overrun the budget, and one of
    // them gives up its clone. The companion test below runs fifty slots of a vanilla
    // clone - more slots, less memory - through the same code and drops none. A
    // count-based ceiling cannot tell those two apart; this is the half it gets wrong.
    [Fact]
    public void WarmStartPosSlotsAreBoundedByBytesRatherThanBySlotCount() {
        string heavyMap = "Tests/HeavyWarm" + Guid.NewGuid().ToString("N");
        List<string> installed = new List<string>();
        try {
            long heavySlotBytes = AkronSaveLoadService.MaxWarmStartPosBytes / 3;
            for (int slot = 1; slot <= 4; slot++) {
                installed.Add(AddWarmStartPosSlotWithSnapshot(heavyMap, slot, heavySlotBytes));
            }
            Assert.True(AkronSaveLoadService.WarmStartPosBytes > AkronSaveLoadService.MaxWarmStartPosBytes);

            int dropped = AkronSaveLoadService.TrimWarmStartPosSlots(out long droppedBytes);

            Assert.Equal(1, dropped);
            Assert.Equal(heavySlotBytes, droppedBytes);
            Assert.True(AkronSaveLoadService.WarmStartPosBytes <= AkronSaveLoadService.MaxWarmStartPosBytes);
            // The coldest went and the rest stayed: eviction is least-recently-used, so
            // the slot the player set first is the one that gives up its clone.
            Assert.False(AkronSaveLoadService.HasRuntimeStateInMemory(installed[0]));
            Assert.True(AkronSaveLoadService.HasRuntimeStateInMemory(installed[1]));
            Assert.True(AkronSaveLoadService.HasRuntimeStateInMemory(installed[3]));
        } finally {
            foreach (string stateSlotName in installed) {
                AkronSaveLoadService.ClearRuntimeState(stateSlotName);
            }
        }
    }

    [Fact]
    public void WarmStartPosEvictionDoesNotClearLiveRegisteredState() {
        string mapSid = "Tests/WarmEvictionCallbacks" + Guid.NewGuid().ToString("N");
        List<string> stateSlotNames = new List<string>();
        int clearCallbacks = 0;
        object registration = AkronSaveLoadService.RegisterSaveLoadAction(
            null,
            null,
            () => clearCallbacks++,
            null,
            null,
            null);
        try {
            long heavySlotBytes = AkronSaveLoadService.MaxWarmStartPosBytes / 3;
            for (int slot = 1; slot <= 4; slot++) {
                stateSlotNames.Add(AddWarmStartPosSlotWithSnapshot(mapSid, slot, heavySlotBytes));
            }

            Assert.Equal(1, AkronSaveLoadService.TrimWarmStartPosSlots(out _));

            Assert.Equal(0, clearCallbacks);
            Assert.False(AkronSaveLoadService.HasRuntimeStateInMemory(stateSlotNames[0]));
        } finally {
            AkronSaveLoadService.Unregister(registration);
            foreach (string stateSlotName in stateSlotNames) {
                AkronSaveLoadService.ClearRuntimeState(stateSlotName);
            }
        }
    }

    [Fact]
    public void FiftyWarmStartPosSlotsOfAVanillaSizedCloneKeepAllOfTheirMemory() {
        string lightMap = "Tests/LightWarm" + Guid.NewGuid().ToString("N");
        List<string> installed = new List<string>();
        try {
            // 13 MB each is the measured vanilla Forsaken City figure, so fifty of them
            // is the full slot ceiling on the map the ceiling was chosen against.
            for (int slot = 1; slot <= 50; slot++) {
                installed.Add(AddWarmStartPosSlotWithSnapshot(lightMap, slot, 13L * 1024L * 1024L));
            }
            Assert.True(AkronSaveLoadService.WarmStartPosBytes <= AkronSaveLoadService.MaxWarmStartPosBytes);

            Assert.Equal(0, AkronSaveLoadService.TrimWarmStartPosSlots(out long droppedBytes));

            Assert.Equal(0L, droppedBytes);
            Assert.True(AkronSaveLoadService.PrepareWarmStartPosCapture(
                lightMap,
                out int droppedForNextCapture,
                out long bytesDroppedForNextCapture));
            Assert.Equal(0, droppedForNextCapture);
            Assert.Equal(0L, bytesDroppedForNextCapture);
            foreach (string stateSlotName in installed) {
                Assert.True(AkronSaveLoadService.HasRuntimeStateInMemory(stateSlotName));
            }
        } finally {
            foreach (string stateSlotName in installed) {
                AkronSaveLoadService.ClearRuntimeState(stateSlotName);
            }
        }
    }

    [Fact]
    public void AnOversizedWarmCloneBlocksAnotherCaptureOnlyWhileItRemainsResident() {
        string mapSid = "Tests/OversizedWarm" + Guid.NewGuid().ToString("N");
        string stateSlotName = AkronActions.GetStartPosStateSlotName(
            mapSid,
            1,
            0);
        try {
            AkronSaveLoadService.AddWarmStartPosSlotForTests(
                stateSlotName,
                mapSid,
                AkronSaveLoadService.MaxWarmStartPosBytes + 1);

            Assert.False(AkronSaveLoadService.PrepareWarmStartPosCapture(
                mapSid,
                out int droppedSlots,
                out long droppedBytes));
            Assert.Equal(0, droppedSlots);
            Assert.Equal(0, droppedBytes);

            AkronSaveLoadService.DiscardRuntimeStateMemory(stateSlotName);
            Assert.Equal(0, AkronSaveLoadService.WarmStartPosBytes);
            Assert.True(AkronSaveLoadService.PrepareWarmStartPosCapture(
                mapSid,
                out droppedSlots,
                out droppedBytes));
            Assert.Equal(0, droppedSlots);
            Assert.Equal(0, droppedBytes);
        } finally {
            AkronSaveLoadService.ClearRuntimeState(stateSlotName);
        }
    }

    // Dropping a warm clone is only safe once the slot's restart copy is on disk. A
    // clone with no snapshot behind it is the only copy of that state, so the budget
    // must refuse to spend it and the Set path must decline instead.
    [Fact]
    public void WarmStartPosSlotsWithNoRestartCopyAreNeverDroppedToFreeMemory() {
        string mapSid = "Tests/NoCopyWarm" + Guid.NewGuid().ToString("N");
        List<string> installed = new List<string>();
        try {
            for (int slot = 1; slot <= 4; slot++) {
                // No SaveSnapshot call: these clones exist only in memory, which is what
                // a slot looks like between being set and its restart copy landing.
                string stateSlotName = AkronActions.GetStartPosStateSlotName(mapSid, slot, 0);
                AkronSaveLoadService.AddWarmStartPosSlotForTests(
                    stateSlotName,
                    mapSid,
                    AkronSaveLoadService.MaxWarmStartPosBytes / 3);
                installed.Add(stateSlotName);
            }
            Assert.True(AkronSaveLoadService.WarmStartPosBytes > AkronSaveLoadService.MaxWarmStartPosBytes);

            Assert.Equal(0, AkronSaveLoadService.TrimWarmStartPosSlots(out long droppedBytes));
            Assert.Equal(0L, droppedBytes);
            foreach (string stateSlotName in installed) {
                Assert.True(AkronSaveLoadService.HasRuntimeStateInMemory(stateSlotName));
            }
            // Nothing can be dropped and the budget is full, so the next Set is refused
            // rather than cloning a room the process cannot pay for.
            Assert.False(AkronSaveLoadService.PrepareWarmStartPosCapture(mapSid, out _, out _));

            // A droppable slot that does not cover the overrun must not unblock it.
            // Answering "is anything droppable at all" instead would wave the next Set
            // through on the strength of this one kilobyte and then have no way to pay
            // for the clone it let in.
            installed.Add(AddWarmStartPosSlotWithSnapshot(mapSid, 5, 1024L));
            Assert.False(AkronSaveLoadService.PrepareWarmStartPosCapture(mapSid, out _, out _));

            // Two heavy clones reaching disk cover both the current overrun and the
            // largest observed clone reserved for the next capture. Preparation drops
            // them before the clone is allocated, along with the extra kilobyte.
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                installed[0], mapSid, "room", 0, MinimalDocument(), out string snapshotError), snapshotError);
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                installed[1], mapSid, "room", 0, MinimalDocument(), out snapshotError), snapshotError);
            Assert.True(AkronSaveLoadService.PrepareWarmStartPosCapture(
                mapSid,
                out int droppedForCapture,
                out long freedBytes));
            Assert.Equal(3, droppedForCapture);
            Assert.Equal((2 * (AkronSaveLoadService.MaxWarmStartPosBytes / 3)) + 1024L, freedBytes);
            Assert.False(AkronSaveLoadService.HasRuntimeStateInMemory(installed[0]));
            Assert.False(AkronSaveLoadService.HasRuntimeStateInMemory(installed[1]));
            Assert.False(AkronSaveLoadService.HasRuntimeStateInMemory(installed[4]));
            Assert.True(
                AkronSaveLoadService.WarmStartPosBytes +
                (AkronSaveLoadService.MaxWarmStartPosBytes / 3) <=
                AkronSaveLoadService.MaxWarmStartPosBytes);
        } finally {
            foreach (string stateSlotName in installed) {
                AkronSaveLoadService.ClearRuntimeState(stateSlotName);
            }
        }
    }

    // The ledger is recomputed from RuntimeSlots on every read instead of being kept in
    // step by each of the eight places that mutate it. A slot cleared behind the ledger's
    // back must stop counting against the budget; the alternative is a mod that refuses
    // to keep slots warm because of clones it dropped an hour ago.
    [Fact]
    public void WarmStartPosBytesStopCountingSlotsThatAreNoLongerInMemory() {
        string mapSid = "Tests/StaleWarm" + Guid.NewGuid().ToString("N");
        string first = AddWarmStartPosSlotWithSnapshot(mapSid, 1, 64L * 1024L * 1024L);
        string second = AddWarmStartPosSlotWithSnapshot(mapSid, 2, 32L * 1024L * 1024L);
        try {
            Assert.Equal(96L * 1024L * 1024L, AkronSaveLoadService.WarmStartPosBytes);

            AkronSaveLoadService.ClearRuntimeState(first);

            Assert.Equal(32L * 1024L * 1024L, AkronSaveLoadService.WarmStartPosBytes);
        } finally {
            AkronSaveLoadService.ClearRuntimeState(first);
            AkronSaveLoadService.ClearRuntimeState(second);
        }
    }

    // A Set parks the clone the slot already held rather than releasing it, so the
    // previous state survives a Set that fails. Parking renames the entry, and a rename
    // is the one move the reconcile cannot follow: the bytes are still resident under a
    // key it no longer recognises. If the cost did not travel with the clone, a parked
    // slot would read as free memory that is not free.
    [Fact]
    public void WarmStartPosCostFollowsACloneThroughAParkAndBack() {
        string mapSid = "Tests/ParkedWarm" + Guid.NewGuid().ToString("N");
        string stateSlotName = AddWarmStartPosSlotWithSnapshot(mapSid, 1, 64L * 1024L * 1024L);
        try {
            Assert.Equal(64L * 1024L * 1024L, AkronSaveLoadService.WarmStartPosBytes);

            string parkedName = AkronSaveLoadService.ParkRuntimeState(stateSlotName);
            Assert.False(string.IsNullOrWhiteSpace(parkedName));
            Assert.Equal(64L * 1024L * 1024L, AkronSaveLoadService.WarmStartPosBytes);

            AkronSaveLoadService.RestoreParkedRuntimeState(parkedName, stateSlotName);

            Assert.True(AkronSaveLoadService.HasRuntimeStateInMemory(stateSlotName));
            Assert.Equal(64L * 1024L * 1024L, AkronSaveLoadService.WarmStartPosBytes);
        } finally {
            AkronSaveLoadService.ClearRuntimeState(stateSlotName);
        }
    }

    [Fact]
    public void PendingStartPosEntriesAreIsolatedByCelesteFileSlot() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int publish = source.IndexOf("private static void PublishPendingStartPos", StringComparison.Ordinal);
        int publishEnd = source.IndexOf("private static void RemovePendingStartPos", publish, StringComparison.Ordinal);
        int load = source.IndexOf("internal static void LoadStartPositionsForLevel", StringComparison.Ordinal);
        int loadEnd = source.IndexOf("internal static IEnumerable<KeyValuePair<int, AkronStartPos>>", load, StringComparison.Ordinal);
        string publishPath = SourceSlice(source, publish, publishEnd - publish);
        string loadPath = SourceSlice(source, load, loadEnd - load);

        Assert.Contains("BuildPendingStartPosKey(fileSlot, areaSid, startPos.ProfileId)", publishPath);
        Assert.Contains("BuildPendingStartPosKey(GetCurrentFileSlot(), areaSid)", loadPath);
    }

    [Fact]
    public void BackgroundStartPosCompletionRequiresTheOriginatingSaveFileIncarnation() {
        string actionsSource = File.ReadAllText(GetActionsSourcePath());
        string persistenceSource = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        int completion = actionsSource.IndexOf("internal static void CompletePersistentStartPosCapture", StringComparison.Ordinal);
        int completionEnd = actionsSource.IndexOf("private static void ApplyPlacedStartPosBeforeCapture", completion, StringComparison.Ordinal);
        string completionPath = SourceSlice(actionsSource, completion, completionEnd - completion);

        Assert.Contains("IsOriginatingSaveFileActive(fileSlot, profileId)", completionPath);
        Assert.Contains("PersistStartPos(slot, startPos, fileSlot, out previousMetadataLost)", completionPath);
        Assert.Contains("FileSlot = fileSlot", persistenceSource);
        Assert.Contains("ProfileId = startPos.ProfileId", persistenceSource);
        Assert.Contains("completion.Job.FileSlot", persistenceSource);
        Assert.Contains("completion.Job.ProfileId", persistenceSource);
        // The job carries a stable persisted identity, not a save data object.
        // Object identity changes during an ordinary Akron savestate Load, while the
        // incarnation changes when Celeste starts a replacement profile in the slot.
        Assert.DoesNotContain("SaveData = saveData", persistenceSource);
        Assert.DoesNotContain("completion.Job.SaveData", persistenceSource);
    }

    [Fact]
    public void TheOriginatingSaveFileCheckComparesTheSlotAndIncarnationNotTheObject() {
        string actionsSource = File.ReadAllText(GetActionsSourcePath());
        int check = actionsSource.IndexOf(
            "private static bool IsOriginatingSaveFileActive(int fileSlot, string profileId)",
            StringComparison.Ordinal);
        int checkEnd = actionsSource.IndexOf("internal static void RefreshStartPositionsAfterSnapshotImport", check, StringComparison.Ordinal);
        string checkBody = SourceSlice(actionsSource, check, checkEnd - check);

        Assert.True(check >= 0);
        Assert.Contains("SaveData.Instance?.FileSlot == fileSlot", checkBody);
        Assert.Contains("AkronModule.SaveData.ProfileId, profileId", checkBody);
        // Object identity is not the question. Everest replaces the mod save data
        // object whenever it reloads a file, and a savestate Load installs a clone
        // of it for every module, neither of which means the player changed files.
        Assert.DoesNotContain("ReferenceEquals", checkBody);
    }

    [Fact]
    public void ProfileIdentitySurvivesSavestateCloningButChangesForANewProfile() {
        AkronModuleSaveData original = new AkronModuleSaveData();
        AkronModuleSaveData savestateClone = (AkronModuleSaveData) AkronSaveLoadService.DeepClone(original);
        AkronModuleSaveData replacementProfile = new AkronModuleSaveData();

        Assert.False(string.IsNullOrWhiteSpace(original.ProfileId));
        Assert.Equal(original.ProfileId, savestateClone.ProfileId);
        Assert.NotEqual(original.ProfileId, replacementProfile.ProfileId);
    }

    [Fact]
    public void PrewarmScopeIncludesTheProfileIncarnation() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int load = source.IndexOf("internal static void LoadStartPositionsForLevel", StringComparison.Ordinal);
        int loadEnd = source.IndexOf(
            "internal static IEnumerable<KeyValuePair<int, AkronStartPos>>",
            load,
            StringComparison.Ordinal);
        string loadPath = SourceSlice(source, load, loadEnd - load);

        int profileAwareKey = loadPath.IndexOf(
            "BuildPendingStartPosKey(GetCurrentFileSlot(), areaSid)",
            StringComparison.Ordinal);
        int scopeCheck = loadPath.IndexOf("prewarmedSnapshotScope", profileAwareKey, StringComparison.Ordinal);
        Assert.True(profileAwareKey >= 0 && scopeCheck > profileAwareKey);
    }

    [Fact]
    public void DeletingAProfileClearsOnlyItsOwnedStartPosSnapshots() {
        int fileSlot = 2;
        string mapSid = "Tests/ProfileDelete" + Guid.NewGuid().ToString("N");
        AkronModuleSaveData deletedProfile = new AkronModuleSaveData {
            StartPositionsByMap = new Dictionary<string, AkronPersistedStartPosMap> {
                [mapSid] = new AkronPersistedStartPosMap {
                    Slots = new Dictionary<int, AkronPersistedStartPos> {
                        [1] = new AkronPersistedStartPos(),
                        [2] = new AkronPersistedStartPos()
                    }
                }
            }
        };
        AkronModuleSaveData survivingProfile = new AkronModuleSaveData();
        string firstDeleted = AkronActions.GetStartPosStateSlotName(
            mapSid, 1, fileSlot, deletedProfile.ProfileId);
        string secondDeleted = AkronActions.GetStartPosStateSlotName(
            mapSid, 2, fileSlot, deletedProfile.ProfileId);
        string survivor = AkronActions.GetStartPosStateSlotName(
            mapSid, 1, fileSlot, survivingProfile.ProfileId);

        try {
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                firstDeleted, mapSid, "room", fileSlot, MinimalDocument(), out string firstError), firstError);
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                secondDeleted, mapSid, "room", fileSlot, MinimalDocument(), out string secondError), secondError);
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                survivor, mapSid, "room", fileSlot, MinimalDocument(), out string survivorError), survivorError);

            Assert.Equal(2, AkronActions.DeleteStartPosSnapshotsForProfile(fileSlot, deletedProfile));
            Assert.False(AkronStartPosReconstruction.HasSnapshot(firstDeleted));
            Assert.False(AkronStartPosReconstruction.HasSnapshot(secondDeleted));
            Assert.True(AkronStartPosReconstruction.HasSnapshot(survivor));
        } finally {
            AkronSaveLoadService.ClearRuntimeState(firstDeleted);
            AkronSaveLoadService.ClearRuntimeState(secondDeleted);
            AkronSaveLoadService.ClearRuntimeState(survivor);
        }
    }

    [Fact]
    public void ProfileSnapshotCleanupFollowsCelestesModSaveDeletion() {
        string source = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        int handler = source.IndexOf("private static bool SaveDataOnTryDeleteModSaveData", StringComparison.Ordinal);
        int handlerEnd = source.IndexOf("private static AkronModuleSaveData ReadProfileSaveData", handler, StringComparison.Ordinal);
        string deletionPath = SourceSlice(source, handler, handlerEnd - handler);

        Assert.Contains("On.Celeste.SaveData.TryDeleteModSaveData += SaveDataOnTryDeleteModSaveData;", source);
        Assert.Contains("On.Celeste.SaveData.TryDeleteModSaveData -= SaveDataOnTryDeleteModSaveData;", source);
        int readBefore = deletionPath.IndexOf("ReadProfileSaveData(fileSlot)", StringComparison.Ordinal);
        int delete = deletionPath.IndexOf("orig(fileSlot)", StringComparison.Ordinal);
        int readAfter = deletionPath.IndexOf("ReadSaveData(fileSlot) == null", StringComparison.Ordinal);
        int cleanup = deletionPath.IndexOf("DeleteStartPosSnapshotsForProfile", StringComparison.Ordinal);
        Assert.True(readBefore >= 0 && delete > readBefore && readAfter > delete && cleanup > readAfter);
    }

    [Fact]
    public void StartPosCaptureQueuesDiskWorkWithoutReloadingTheLiveRoom() {
        string source = File.ReadAllText(GetActionsSourcePath());

        int capture = source.IndexOf("private static void CaptureStartPos", StringComparison.Ordinal);
        int captureEnd = source.IndexOf("private static void ApplyPlacedStartPosBeforeCapture", capture, StringComparison.Ordinal);
        string capturePath = SourceSlice(source, capture, captureEnd - capture);

        Assert.Contains("AkronStartPosPersistence.Enqueue", capturePath);
        Assert.DoesNotContain("level.Reload()", capturePath);
        Assert.DoesNotContain("PersistRuntimeStateSnapshot", capturePath);
    }

    [Fact]
    public void PersistentRestoreDoesNotReplaceGlobalSaveData() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        string modelsSource = File.ReadAllText(GetSourcePath("SaveLoad", "akron-save-load-models.cs"));
        int persistStart = source.IndexOf("internal static AkronSaveLoadResult PersistRuntimeStateSnapshot", StringComparison.Ordinal);
        int persistEnd = source.IndexOf("public static AkronSaveLoadResult RestoreRuntimeState", persistStart, StringComparison.Ordinal);
        int restoreStart = source.IndexOf("private static AkronSaveLoadResult RestorePersistentRuntimeState", StringComparison.Ordinal);
        int restoreEnd = source.IndexOf("private static bool TryLoadFreshRoom", restoreStart, StringComparison.Ordinal);
        int persistentModelStart = modelsSource.IndexOf("internal sealed class AkronPersistentRuntimeState", StringComparison.Ordinal);
        string persistMethod = SourceSlice(source, persistStart, persistEnd - persistStart);
        string restoreMethod = SourceSlice(source, restoreStart, restoreEnd - restoreStart);
        string persistentModel = SourceTail(modelsSource, persistentModelStart);

        Assert.Contains("AkronPersistentRuntimeState.CaptureSaved", persistMethod);
        Assert.Equal(2, persistMethod.Split("AkronPersistentRuntimeState.CaptureSaved", StringSplitOptions.None).Length - 1);
        Assert.Contains("AkronPersistentRuntimeState.CaptureCurrent", restoreMethod);
        Assert.Contains("ApplyPersistentRuntimeState", restoreMethod);
        Assert.DoesNotContain("SaveDataState", persistentModel);
        Assert.DoesNotContain("ModuleSaveData", persistentModel);
        Assert.DoesNotContain("SaveData.Instance =", restoreMethod);
        Assert.DoesNotContain("module._SaveData =", restoreMethod);
    }

    [Fact]
    public void StartPosCapturePublishesTheWarmStateBeforeDiskWorkStarts() {
        string actionsSource = File.ReadAllText(GetActionsSourcePath());
        int capture = actionsSource.IndexOf("private static void CaptureStartPos", StringComparison.Ordinal);
        int publish = actionsSource.IndexOf("PublishPendingStartPos(fileSlot, slot, startPos);", capture, StringComparison.Ordinal);
        int enqueue = actionsSource.IndexOf("AkronStartPosPersistence.Enqueue", capture, StringComparison.Ordinal);
        int completion = actionsSource.IndexOf("completion?.Invoke(true);", capture, StringComparison.Ordinal);

        Assert.True(capture >= 0);
        Assert.True(publish > capture);
        Assert.True(enqueue > publish);
        Assert.True(completion > publish);
    }

    [Fact]
    public void SuccessfulStartPosCaptureRetainsItsWarmRuntimeStateAfterDiskCommit() {
        string actionsSource = File.ReadAllText(GetActionsSourcePath());
        int completion = actionsSource.IndexOf("internal static void CompletePersistentStartPosCapture", StringComparison.Ordinal);
        int completionEnd = actionsSource.IndexOf("private static void ApplyPlacedStartPosBeforeCapture", completion, StringComparison.Ordinal);
        string completionPath = SourceSlice(actionsSource, completion, completionEnd - completion);

        Assert.True(completion >= 0);
        Assert.Contains("installedSnapshot.Commit();", completionPath);
        Assert.DoesNotContain("DiscardRuntimeStateMemory(stateSlotName)", completionPath);
    }

    [Fact]
    public void FirstColdStartPosRestoreCachesTheNativeStateForLaterLoads() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int restore = source.IndexOf("private static AkronSaveLoadResult RestorePersistentRuntimeState(", StringComparison.Ordinal);
        int restoreEnd = source.IndexOf("private static AkronSaveLoadResult RestorePersistentRuntimeStateCore", restore, StringComparison.Ordinal);
        string restorePath = SourceSlice(source, restore, restoreEnd - restore);
        int cache = source.IndexOf("private static AkronSaveLoadResult CacheRestoredRuntimeState", restore, StringComparison.Ordinal);
        string cachePath = SourceSlice(source, cache, restoreEnd - cache);

        Assert.Contains("CacheRestoredRuntimeState", restorePath);
        Assert.DoesNotContain("restoreResult = cacheResult", restorePath);
        Assert.Contains("return AkronSaveLoadResult.Success;", restorePath);
        Assert.Contains("StoreRuntimeSlot", cachePath);
        int prepare = cachePath.IndexOf("PrepareWarmStartPosCapture", StringComparison.Ordinal);
        int capture = cachePath.IndexOf("CaptureRuntimeState", StringComparison.Ordinal);
        int recordCost = cachePath.IndexOf("RecordWarmStartPosCost", StringComparison.Ordinal);
        int trim = cachePath.IndexOf("TrimWarmStartPosSlots", StringComparison.Ordinal);
        Assert.True(prepare >= 0 && capture > prepare);
        Assert.True(recordCost > capture && trim > recordCost);
    }

    [Fact]
    public void StaleWarmStartPosFallsBackToItsPersistentSnapshot() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int load = source.IndexOf("public static AkronSaveLoadResult LoadRuntimeState", StringComparison.Ordinal);
        int loadEnd = source.IndexOf("internal static bool HasRuntimeStateInMemory", load, StringComparison.Ordinal);
        string loadPath = SourceSlice(source, load, loadEnd - load);

        int warmRestore = loadPath.IndexOf("RestoreRuntimeState(level, saveSlot.Slot", StringComparison.Ordinal);
        int mismatch = warmRestore >= 0
            ? loadPath.IndexOf("AkronSaveLoadResult.SessionMismatch", warmRestore, StringComparison.Ordinal)
            : -1;
        int discard = mismatch >= 0
            ? loadPath.IndexOf("DiscardRuntimeStateMemory(normalizedSlotName)", mismatch, StringComparison.Ordinal)
            : -1;
        int persistentRestore = discard >= 0
            ? loadPath.IndexOf("RestorePersistentRuntimeState(level, normalizedSlotName", discard, StringComparison.Ordinal)
            : -1;

        Assert.True(warmRestore >= 0);
        Assert.True(mismatch > warmRestore);
        Assert.True(discard > mismatch);
        Assert.True(persistentRestore > discard);
    }

    [Fact]
    public void UncommittedStartPosReplacementCannotLoadThePreviousDiskSnapshot() {
        string actionsSource = File.ReadAllText(GetActionsSourcePath());
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());
        int load = saveLoadSource.IndexOf("public static AkronSaveLoadResult LoadRuntimeState", StringComparison.Ordinal);
        int loadEnd = saveLoadSource.IndexOf("internal static bool HasRuntimeStateInMemory", load, StringComparison.Ordinal);
        string loadPath = SourceSlice(saveLoadSource, load, loadEnd - load);
        int discard = loadPath.IndexOf("DiscardRuntimeStateMemory(normalizedSlotName)", StringComparison.Ordinal);
        int pendingGuard = loadPath.IndexOf("AkronActions.HasPendingStartPosState(normalizedSlotName)", StringComparison.Ordinal);
        int persistentRestore = loadPath.IndexOf("RestorePersistentRuntimeState(level, normalizedSlotName", StringComparison.Ordinal);

        Assert.Contains("internal static bool HasPendingStartPosState", actionsSource);
        Assert.True(discard >= 0);
        Assert.True(pendingGuard > discard);
        Assert.True(persistentRestore > pendingGuard);
    }

    [Fact]
    public void StaleRuntimeSlotIsRejectedBeforeHelperLoadCallbacksRun() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int restore = source.IndexOf("public static AkronSaveLoadResult RestoreRuntimeState", StringComparison.Ordinal);
        int restoreEnd = source.IndexOf("public static AkronSaveLoadResult LoadRuntimeState", restore, StringComparison.Ordinal);
        string restorePath = SourceSlice(source, restore, restoreEnd - restore);
        int sessionCheck = restorePath.IndexOf("MatchesCurrentNativeSession(level, saveSlot)", StringComparison.Ordinal);
        int beforeLoad = restorePath.IndexOf("action.BeforeLoadState?.Invoke(level)", StringComparison.Ordinal);

        Assert.True(sessionCheck >= 0);
        Assert.True(beforeLoad > sessionCheck);
    }

    [Fact]
    public void ReplacingStartPosDataCancelsPendingDiskCaptures() {
        string actionsSource = File.ReadAllText(GetActionsSourcePath());
        int replace = actionsSource.IndexOf("internal static void ReplacePersistedStartPositionsForMap", StringComparison.Ordinal);
        int replaceEnd = actionsSource.IndexOf("private static void EnsureStartPositionsLoaded", replace, StringComparison.Ordinal);
        string replacePath = SourceSlice(actionsSource, replace, replaceEnd - replace);
        int validation = replacePath.IndexOf("// Validate the complete import", StringComparison.Ordinal);
        int cancel = replacePath.IndexOf("CancelStartPosPersistence(", validation, StringComparison.Ordinal);
        int removePending = replacePath.IndexOf("PendingStartPositionsByFileAndMap.Remove(pendingKey);", cancel, StringComparison.Ordinal);

        Assert.True(validation >= 0);
        Assert.True(cancel > validation);
        Assert.True(removePending > cancel);
    }

    [Fact]
    public void FailedFreshBaselineCompletesWaitingPersistenceJobs() {
        string persistenceSource = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        int capture = persistenceSource.IndexOf("private static void CaptureFreshBaseline", StringComparison.Ordinal);
        int captureEnd = persistenceSource.IndexOf("private static void StartWorkerLocked", capture, StringComparison.Ordinal);
        string capturePath = SourceSlice(persistenceSource, capture, captureEnd - capture);

        Assert.Contains("FailWaitingJobsForBaselineLocked", capturePath);
        Assert.Contains("fresh-room baseline", capturePath);
    }

    [Fact]
    public void SetNeverCapturesABaselineFromTheCurrentRuntimeState() {
        string persistenceSource = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        int enqueue = persistenceSource.IndexOf("public static long Enqueue", StringComparison.Ordinal);
        int enqueueEnd = persistenceSource.IndexOf("public static void Cancel", enqueue, StringComparison.Ordinal);
        string enqueuePath = SourceSlice(persistenceSource, enqueue, enqueueEnd - enqueue);

        Assert.DoesNotContain("NotifyLevelReady(currentLevel)", enqueuePath);
        Assert.Contains("PendingBaselineGenerations.ContainsKey(baselineKey)", enqueuePath);
        Assert.Contains("fresh-room baseline is unavailable", enqueuePath);
    }

    [Fact]
    public void WarmCrossRoomRestoreReusesTheStartPosFreshBaseline() {
        string persistenceSource = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());
        int restore = saveLoadSource.IndexOf("public static AkronSaveLoadResult RestoreRuntimeState", StringComparison.Ordinal);
        int restoreEnd = saveLoadSource.IndexOf("public static AkronSaveLoadResult LoadRuntimeState", restore, StringComparison.Ordinal);
        string restorePath = SourceSlice(saveLoadSource, restore, restoreEnd - restore);

        Assert.Contains("RuntimeFreshBaselines", persistenceSource);
        Assert.Contains("AttachRuntimeFreshBaselineLocked(job.StateSlotName", persistenceSource);
        Assert.Contains("UseRuntimeFreshBaseline(saveSlot.SlotName)", restorePath);
        Assert.Contains("RemoveRuntimeFreshBaseline(slotName)", saveLoadSource);
    }

    [Fact]
    public void ColdRestoreCapturesTheFreshBaselineBeforeReconstruction() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int restore = source.IndexOf("private static AkronSaveLoadResult RestorePersistentRuntimeStateCore", StringComparison.Ordinal);
        int restoreEnd = source.IndexOf("private static AkronSaveLoadResult RestorePersistentRuntimeStateAfterActionState", restore, StringComparison.Ordinal);
        string restorePath = SourceSlice(source, restore, restoreEnd - restore);

        int freshRoom = restorePath.IndexOf("TryLoadFreshRoom", StringComparison.Ordinal);
        int freshBaseline = restorePath.IndexOf("CaptureFreshRuntimeState", freshRoom, StringComparison.Ordinal);
        int reconstruction = restorePath.IndexOf("RestorePersistentRuntimeStateAfterActionState", freshBaseline, StringComparison.Ordinal);

        Assert.True(freshRoom >= 0);
        Assert.True(freshBaseline > freshRoom);
        Assert.True(reconstruction > freshBaseline);
        Assert.Contains("AttachRuntimeFreshBaseline(slotName, freshBaseline)", source);
    }

    [Fact]
    public void FreshBaselineCacheEvictsRoomsThatAreNoLongerCurrent() {
        string persistenceSource = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        int notify = persistenceSource.IndexOf("public static void NotifyLevelReady", StringComparison.Ordinal);
        int notifyEnd = persistenceSource.IndexOf("public static long Enqueue", notify, StringComparison.Ordinal);
        string notifyPath = SourceSlice(persistenceSource, notify, notifyEnd - notify);

        Assert.Contains("EvictOtherBaselinesLocked(expectedKey)", notifyPath);
        Assert.Contains("FailWaitingJobsExceptLocked(", notifyPath);
        Assert.Contains("expectedKey,", notifyPath);
    }

    // Every room load rebuilds the room, so the retained baseline is stale and
    // must be recaptured. The refresh is unconditional on purpose: StartPos
    // promises exact restoration, and no in-process check can see every input
    // room construction reads, so skipping a capture cannot be made sound.
    [Fact]
    public void RoomLoadsRefreshTheFreshBaselineUnconditionally() {
        string source = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        int notify = source.IndexOf("public static void NotifyLevelReady", StringComparison.Ordinal);
        int notifyEnd = source.IndexOf("public static long Enqueue", notify, StringComparison.Ordinal);
        string notifyPath = SourceSlice(source, notify, notifyEnd - notify);
        int loadHook = source.IndexOf("private static void LevelOnLoadLevel", StringComparison.Ordinal);
        int capture = source.IndexOf("private static void CaptureFreshBaseline", loadHook, StringComparison.Ordinal);
        string loadHookPath = SourceSlice(source, loadHook, capture - loadHook);

        Assert.Contains("bool refreshBaseline = false", notifyPath);
        Assert.Contains("if (refreshBaseline) {", notifyPath);
        // No freshness heuristic may creep back in front of the capture.
        Assert.DoesNotContain("sessionUnchanged", notifyPath);
        Assert.Contains("FreshBaselines.Remove(expectedKey", notifyPath);
        Assert.Contains("FailWaitingJobsForBaselineLocked(", notifyPath);
        Assert.Contains("the room reloaded before its fresh-room baseline was ready", notifyPath);
        Assert.Contains("PendingBaselineGenerations[expectedKey] = captureGeneration", notifyPath);
        Assert.Contains("IsPendingBaselineGenerationLocked", source);
        Assert.Contains("NotifyLevelReady(self, refreshBaseline: true);", loadHookPath);
    }

    [Fact]
    public void WarmRestoreDoesNotMutateTheSavedLevelWhileDiskConversionReadsIt() {
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());
        int restore = saveLoadSource.IndexOf("private static bool RestoreNativeSlot", StringComparison.Ordinal);
        int restoreEnd = saveLoadSource.IndexOf("private static void CaptureCuratedSessionState", restore, StringComparison.Ordinal);
        string restorePath = SourceSlice(saveLoadSource, restore, restoreEnd - restore);

        Assert.DoesNotContain("savedSession.Time =", restorePath);
        Assert.DoesNotContain("savedSession.Deaths =", restorePath);
        Assert.DoesNotContain("savedSession.DeathsInCurrentLevel =", restorePath);
        Assert.Contains("AkronDeepClone.CopyIntoDormant(savedLevel, level)", restorePath);
        Assert.DoesNotContain("AkronDeepClone.CopyIntoDormant(level, savedLevel)", restorePath);
        Assert.DoesNotContain("AkronDeepClone.CopyInto(level, savedLevel)", restorePath);
        Assert.DoesNotContain("saveSlot.SavedLevel =", restorePath);
        Assert.Contains("level.Session.Time = Math.Max(currentSessionTime, level.Session.Time);", restorePath);
    }

    [Fact]
    public void WarmStartPosRestorePreservesCurrentGlobalAndModuleSaveData() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int runtimeRestore = source.IndexOf("public static AkronSaveLoadResult RestoreRuntimeState", StringComparison.Ordinal);
        int runtimeRestoreEnd = source.IndexOf("public static AkronSaveLoadResult LoadRuntimeState", runtimeRestore, StringComparison.Ordinal);
        string runtimeRestorePath = SourceSlice(source, runtimeRestore, runtimeRestoreEnd - runtimeRestore);
        int nativeRestore = source.IndexOf("private static bool RestoreNativeSlot", StringComparison.Ordinal);
        int nativeRestoreEnd = source.IndexOf("private static void CaptureCuratedSessionState", nativeRestore, StringComparison.Ordinal);
        string nativeRestorePath = SourceSlice(source, nativeRestore, nativeRestoreEnd - nativeRestore);

        Assert.Contains("restoreGlobalSaveData: false", runtimeRestorePath);
        Assert.Contains("restoreGlobalSaveData && saveSlot.SaveDataState", nativeRestorePath);
        int moduleSaveDataGate = nativeRestorePath.IndexOf("if (restoreGlobalSaveData &&", StringComparison.Ordinal);
        int moduleSaveDataRestore = nativeRestorePath.IndexOf("saveSlot.ModuleSaveData.TryGetValue", moduleSaveDataGate, StringComparison.Ordinal);
        Assert.True(moduleSaveDataGate >= 0 && moduleSaveDataRestore > moduleSaveDataGate);
    }

    [Fact]
    public void PersistentGraphConversionRunsOnTheSerializedWorker() {
        string persistenceSource = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        int worker = persistenceSource.IndexOf("private static void RunWorker", StringComparison.Ordinal);
        int persist = persistenceSource.IndexOf("PersistRuntimeStateSnapshot", worker, StringComparison.Ordinal);
        int completion = persistenceSource.IndexOf("Completed.Enqueue", persist, StringComparison.Ordinal);

        Assert.True(worker >= 0);
        Assert.True(persist > worker);
        Assert.True(completion > persist);
        Assert.Contains("Task.Run(RunWorker)", persistenceSource);
    }

    [Fact]
    public void PersistentWorkerUsesRenderTargetPixelsCapturedAtSet() {
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());
        string graphSource = File.ReadAllText(GetSourcePath("SaveLoad", "akron-reconstruction-graph.cs"));
        int capture = saveLoadSource.IndexOf("public static AkronSaveLoadSlot CaptureRuntimeState", StringComparison.Ordinal);
        int virtualAssetMarker = saveLoadSource.IndexOf("AkronVirtualAssetReloadTracker.Mark()", capture, StringComparison.Ordinal);
        int roomClone = saveLoadSource.IndexOf("BuildNativeSlot(level, CurrentSlotName", virtualAssetMarker, StringComparison.Ordinal);
        int setFramePixels = saveLoadSource.IndexOf("CaptureSetFramePayloads(", capture, StringComparison.Ordinal);
        int retainAssets = saveLoadSource.IndexOf("AkronVirtualAssetReloadTracker.GetRegistrationsSince(virtualAssetMarker)", capture, StringComparison.Ordinal);
        int persist = saveLoadSource.IndexOf("internal static AkronSaveLoadResult PersistRuntimeStateSnapshot", roomClone, StringComparison.Ordinal);
        int workerScope = saveLoadSource.IndexOf("UseCapturedRenderTargets", persist, StringComparison.Ordinal);

        Assert.True(virtualAssetMarker > capture);
        Assert.True(roomClone > virtualAssetMarker);
        Assert.True(setFramePixels > roomClone);
        Assert.True(retainAssets > setFramePixels);
        Assert.True(workerScope > persist);
        Assert.DoesNotContain("VirtualContent.Assets.OfType<VirtualRenderTarget>()", graphSource);
        Assert.Contains("GetRenderTargetsSince(virtualAssetMarker)", saveLoadSource);
        Assert.Contains("new AkronSaveLoadSlotOwner(saveSlot, ReleaseRuntimeSlotResources)", saveLoadSource);
    }

    [Fact]
    public void FreshBaselineAndColdRestoreUseTheSameSingleInitializationUpdate() {
        string moduleSource = File.ReadAllText(GetModuleSourcePath());
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());
        string persistenceSource = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        int levelUpdate = moduleSource.IndexOf("private static void LevelOnUpdate", StringComparison.Ordinal);
        int pending = moduleSource.IndexOf("ConsumeFreshBaselineInitializationUpdate", levelUpdate, StringComparison.Ordinal);
        int originalUpdate = pending >= 0
            ? moduleSource.IndexOf("orig(self);", pending, StringComparison.Ordinal)
            : -1;
        int returnAfterUpdate = originalUpdate >= 0
            ? moduleSource.IndexOf("return;", originalUpdate, StringComparison.Ordinal)
            : -1;
        int pendingCapture = returnAfterUpdate >= 0
            ? moduleSource.IndexOf("IsFreshBaselineCapturePending(self)", returnAfterUpdate, StringComparison.Ordinal)
            : -1;
        int holdPendingCaptureClock = pendingCapture >= 0
            ? moduleSource.IndexOf("HoldSceneClockForSkippedLevelUpdate(self);", pendingCapture, StringComparison.Ordinal)
            : -1;
        int returnAfterPendingCapture = holdPendingCaptureClock >= 0
            ? moduleSource.IndexOf("return;", holdPendingCaptureClock, StringComparison.Ordinal)
            : -1;
        int initialization = moduleSource.IndexOf("internal static void RunFreshRoomInitializationUpdate", StringComparison.Ordinal);
        int initializationEnd = initialization >= 0
            ? moduleSource.IndexOf("private static void LevelOnBeforeRender", initialization, StringComparison.Ordinal)
            : -1;
        string initializationPath = initialization >= 0 && initializationEnd > initialization
            ? SourceSlice(moduleSource, initialization, initializationEnd - initialization)
            : string.Empty;
        int beforeUpdate = initializationPath.IndexOf("level.BeforeUpdate();", StringComparison.Ordinal);
        int update = initializationPath.IndexOf("level.Update();", StringComparison.Ordinal);
        int afterUpdate = initializationPath.IndexOf("level.AfterUpdate();", StringComparison.Ordinal);
        int freshRoom = saveLoadSource.IndexOf("private static bool TryLoadFreshRoom", StringComparison.Ordinal);
        int freshRoomEnd = saveLoadSource.IndexOf("internal static void DrainFreshRoomEntityLists", freshRoom, StringComparison.Ordinal);
        string freshRoomPath = SourceSlice(saveLoadSource, freshRoom, freshRoomEnd - freshRoom);
        int loadLevel = freshRoomPath.IndexOf("level.LoadLevel", StringComparison.Ordinal);
        int replayUpdate = loadLevel >= 0
            ? freshRoomPath.IndexOf("RunFreshRoomInitializationUpdate(level)", loadLevel, StringComparison.Ordinal)
            : -1;
        int drainLists = replayUpdate >= 0
            ? freshRoomPath.IndexOf("DrainFreshRoomEntityLists", replayUpdate, StringComparison.Ordinal)
            : -1;

        Assert.True(pending > levelUpdate);
        Assert.True(originalUpdate > pending && returnAfterUpdate > originalUpdate);
        Assert.True(pendingCapture > returnAfterUpdate);
        Assert.True(holdPendingCaptureClock > pendingCapture && returnAfterPendingCapture > holdPendingCaptureClock);
        Assert.True(initialization >= 0);
        Assert.True(beforeUpdate >= 0 && update > beforeUpdate && afterUpdate > update);
        Assert.True(loadLevel >= 0 && replayUpdate > loadLevel && drainLists > replayUpdate);
        Assert.Contains("ScheduleAfterStableEngineUpdate(", persistenceSource);
        Assert.Contains("() => CaptureFreshBaseline", persistenceSource);
        Assert.DoesNotContain("MaxWipeWaitAttempts", persistenceSource);
        Assert.DoesNotContain("if (level.Wipe != null)", persistenceSource);
    }

    [Fact]
    public void TransientScreenWipeBoundaryRestoresEveryWipeAtItsOriginalIndex() {
        Level level = CreateLevelWithRendererList(out List<Renderer> renderers);
        Renderer before = CreateUninitializedRenderer<LightingRenderer>();
        ScreenWipe firstWipe = CreateUninitializedRenderer<SpotlightWipe>();
        Renderer between = CreateUninitializedRenderer<DisplacementRenderer>();
        ScreenWipe secondWipe = CreateUninitializedRenderer<SpotlightWipe>();
        Renderer after = CreateUninitializedRenderer<LightingRenderer>();
        renderers.AddRange(new[] { before, firstWipe, between, secondWipe, after });
        SetLevelWipe(level, firstWipe);

        object detached = InvokeDetachTransientScreenWipes(level);

        Assert.Null(GetLevelWipe(level));
        Assert.Equal(new[] { before, between, after }, renderers);

        InvokeRestoreTransientScreenWipes(level, detached);

        Assert.Same(firstWipe, GetLevelWipe(level));
        Assert.Equal(new Renderer[] { before, firstWipe, between, secondWipe, after }, renderers);
    }

    [Fact]
    public void MissingTransientScreenWipeBoundaryLeavesAnActiveWipeUntouched() {
        Level level = CreateLevelWithRendererList(out List<Renderer> renderers);
        ScreenWipe wipe = CreateUninitializedRenderer<SpotlightWipe>();
        renderers.Add(wipe);
        SetLevelWipe(level, wipe);

        InvokeRestoreTransientScreenWipes(level, null);

        Assert.Same(wipe, GetLevelWipe(level));
        Assert.Same(wipe, Assert.Single(renderers));
    }

    [Fact]
    public void RuntimeStartPosCaptureExcludesTheTransientEntryWipe() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int capture = source.IndexOf("public static AkronSaveLoadSlot CaptureRuntimeState", StringComparison.Ordinal);
        int captureEnd = source.IndexOf("public static AkronSaveLoadResult SaveRuntimeState", capture, StringComparison.Ordinal);
        string capturePath = SourceSlice(source, capture, captureEnd - capture);

        Assert.Contains("bool isStartPosCapture = CurrentSlotName.StartsWith(AkronActions.StartPosStateSlotPrefix", capturePath);
        Assert.Contains("DetachTransientScreenWipes(level)", capturePath);
        Assert.Contains("RestoreTransientScreenWipes(level, entryWipes)", capturePath);
    }

    [Fact]
    public void FreshBaselineCaptureDoesNotRetainVirtualAssetsForReload() {
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());
        string trackerSource = File.ReadAllText(GetSourcePath("Core", "akron-virtual-asset-reload-tracker.cs"));
        int capture = saveLoadSource.IndexOf("internal static AkronSaveLoadSlotLease CaptureFreshRuntimeState", StringComparison.Ordinal);
        int captureEnd = saveLoadSource.IndexOf("internal static IReadOnlyList<string> GetRegisteredActionIdsForPersistence", capture, StringComparison.Ordinal);
        string capturePath = SourceSlice(saveLoadSource, capture, captureEnd - capture);
        int marker = capturePath.IndexOf("AkronVirtualAssetReloadTracker.Mark()", StringComparison.Ordinal);
        int baselineClone = capturePath.IndexOf("BuildPersistentBaselineSlot", StringComparison.Ordinal);
        int discard = capturePath.IndexOf("AkronVirtualAssetReloadTracker.DiscardSince", StringComparison.Ordinal);

        Assert.True(marker >= 0 && baselineClone > marker && discard > baselineClone);
        Assert.Contains("int start = ClampMarker(marker)", trackerSource);
        Assert.Contains("Registrations.RemoveRange(start, Registrations.Count - start)", trackerSource);
    }

    [Fact]
    public void ShutdownDrainsQueuedDiskWorkAndCommitsItsCompletion() {
        string persistenceSource = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        int shutdown = persistenceSource.IndexOf("public static void Shutdown", StringComparison.Ordinal);
        int wait = persistenceSource.IndexOf("DrainWorkerForShutdown(runningWorker", shutdown, StringComparison.Ordinal);
        int update = persistenceSource.IndexOf("Update();", wait, StringComparison.Ordinal);
        int worker = persistenceSource.IndexOf("private static void RunWorker", update, StringComparison.Ordinal);
        int empty = persistenceSource.IndexOf("if (Ready.Count == 0)", worker, StringComparison.Ordinal);
        int dequeue = persistenceSource.IndexOf("job = Ready.Dequeue();", empty, StringComparison.Ordinal);

        Assert.True(wait > shutdown);
        Assert.True(update > wait);
        Assert.True(empty > worker && dequeue > empty);
        Assert.DoesNotContain("shutdown started before the restart copy finished", persistenceSource);

        // The drain is bounded twice over: a budget for finishing normally, then a
        // cooperative cancel with its own budget. Cancelling rather than walking away
        // is what keeps the staging directory from being leaked and the real snapshot
        // directory from being touched by a job nobody joins, and the second budget is
        // there because a cancel is only seen at the next pace point.
        int drain = persistenceSource.IndexOf("private static bool DrainWorkerForShutdown", StringComparison.Ordinal);
        int drainEnd = persistenceSource.IndexOf("private static void PromoteReadyJobLocked", drain, StringComparison.Ordinal);
        string drainBody = SourceSlice(persistenceSource, drain, drainEnd - drain);
        int budget = drainBody.IndexOf("runningWorker.Wait(ShutdownDrainBudget)", StringComparison.Ordinal);
        int cancel = drainBody.IndexOf("AkronSnapshotPacing.Cancelled = true;", budget, StringComparison.Ordinal);
        int join = drainBody.IndexOf("runningWorker.Wait(ShutdownCancelBudget)", cancel, StringComparison.Ordinal);
        Assert.True(budget > 0);
        Assert.True(cancel > budget);
        Assert.True(join > cancel);
        // No unbounded wait anywhere on the shutdown path.
        Assert.DoesNotContain("runningWorker.GetAwaiter().GetResult();", persistenceSource);
    }

    // A drain that ran out of both budgets leaves the worker alive, and the worker owns its
    // own handle: it nulls it when it finds the queue empty. Clearing it from Shutdown as
    // well let the next Start in the same process create a second worker beside the
    // survivor, and two workers dequeue from one Ready queue into the one static
    // CaptureGraph, which is written for a single thread at a time. Everest reloads a mod
    // in-process, which is what makes that next Start real.
    //
    // Asserted on the source, which is weaker than a behavioural test and is what is
    // available: reproducing it needs a capture that outlives a 5 s drain and a 2 s cancel
    // and then an in-process reload, and nothing in this project can drive either - the
    // whole class is unreachable at runtime from here, which is why every other shutdown
    // test in this file reads the source too.
    [Fact]
    public void ATimedOutShutdownLeavesTheWorkerHandleToTheWorker() {
        string persistenceSource = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"))
            .Replace("\r\n", "\n");

        // The drain says whether it joined the worker: true from either budget, false only
        // after the warning that it is being left behind.
        int drain = persistenceSource.IndexOf("private static bool DrainWorkerForShutdown", StringComparison.Ordinal);
        Assert.True(drain > 0, "DrainWorkerForShutdown does not report whether the worker stopped.");
        int drainEnd = persistenceSource.IndexOf("private static void PromoteReadyJobLocked", drain, StringComparison.Ordinal);
        string drainBody = SourceSlice(persistenceSource, drain, drainEnd - drain);
        Assert.Equal(3, CountOccurrences(drainBody, "return true;"));
        Assert.Equal(1, CountOccurrences(drainBody, "return false;"));
        Assert.True(
            drainBody.IndexOf("return false;", StringComparison.Ordinal) >
            drainBody.IndexOf("did not stop when asked", StringComparison.Ordinal));

        // Shutdown clears the handle only on that answer, and nowhere else.
        Assert.Contains(
            "bool workerStopped = DrainWorkerForShutdown(runningWorker, outstanding);",
            persistenceSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (workerStopped) {\n                workerTask = null;\n            }",
            persistenceSource,
            StringComparison.Ordinal);
        // The remaining assignment is the worker's own, inside RunWorker's lock. Shutdown's
        // was one nesting level shallower, so the indentation is what tells them apart.
        Assert.DoesNotContain("\n            workerTask = null;", persistenceSource, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(persistenceSource, "workerTask = null;"));
    }

    [Fact]
    public void GracefulGameExitDrainsStartPosPersistence() {
        string moduleSource = File.ReadAllText(GetModuleSourcePath());
        int start = moduleSource.IndexOf("AkronStartPosPersistence.Start();", StringComparison.Ordinal);
        int subscribe = moduleSource.IndexOf("Engine.Instance.Exiting += EngineOnExiting;", start, StringComparison.Ordinal);
        int handler = moduleSource.IndexOf("private static void EngineOnExiting", subscribe, StringComparison.Ordinal);
        int shutdown = moduleSource.IndexOf("AkronStartPosPersistence.Shutdown();", handler, StringComparison.Ordinal);
        int unsubscribe = moduleSource.IndexOf("Engine.Instance.Exiting -= EngineOnExiting;", StringComparison.Ordinal);

        Assert.True(start >= 0 && subscribe > start && handler > subscribe && shutdown > handler);
        Assert.True(unsubscribe > subscribe);
    }

    [Fact]
    public void ShutdownWritesCompletedStartPosMetadataSynchronously() {
        string persistenceSource = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        string actionsSource = File.ReadAllText(GetActionsSourcePath());
        int shutdown = persistenceSource.IndexOf("public static void Shutdown", StringComparison.Ordinal);
        int update = persistenceSource.IndexOf("Update();", shutdown, StringComparison.Ordinal);
        int synchronousSave = persistenceSource.IndexOf("AkronActions.SaveAkronStartPosData();", update, StringComparison.Ordinal);
        int save = actionsSource.IndexOf("internal static bool SaveAkronStartPosData()", StringComparison.Ordinal);
        string savePath = SourceSlice(actionsSource, save, actionsSource.IndexOf("private static Dictionary<string, int> BuildRoomOrder", save, StringComparison.Ordinal) - save);

        Assert.True(update > shutdown && synchronousSave > update);
        Assert.Contains("Instance.SerializeSaveData", savePath);
        Assert.Contains("Instance.WriteSaveData", savePath);
    }

    [Fact]
    public void StagedSnapshotInstallationReplacesTheOldFileOnlyWhenCommitted() {
        string slotName = "Akron transactional snapshot " + Guid.NewGuid().ToString("N");
        string stagingDirectory = Path.Combine(Path.GetTempPath(), "akron-transactional-snapshot-" + Guid.NewGuid().ToString("N"));
        AkronReconstructionDocument oldDocument = MinimalDocument();
        AkronReconstructionDocument newDocument = MinimalDocument();
        try {
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                slotName, "Map/A", "old-room", 1, oldDocument, out string oldError), oldError);
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                slotName, "Map/A", "new-room", 1, newDocument, out string newError, stagingDirectory), newError);
            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                slotName, out AkronReconstructionDocument beforeCommit, out string beforeError), beforeError);
            Assert.Equal("old-room", beforeCommit.Room);

            using (AkronStartPosReconstruction.PreparedSnapshotInstall prepared =
                   AkronStartPosReconstruction.PrepareSnapshotInstall(slotName, stagingDirectory)) {
                Assert.True(prepared.Install(out string stagedError), stagedError);
                Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                    slotName, out AkronReconstructionDocument whileInstalled, out string installedError), installedError);
                Assert.Equal("new-room", whileInstalled.Room);
            }

            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                slotName, out AkronReconstructionDocument afterRollback, out string rollbackError), rollbackError);
            Assert.Equal("old-room", afterRollback.Room);
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                slotName, "Map/A", "new-room", 1, newDocument, out newError, stagingDirectory), newError);
            Assert.True(AkronStartPosReconstruction.InstallSnapshot(
                slotName, stagingDirectory, out string installError), installError);

            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                slotName, out AkronReconstructionDocument afterCommit, out string afterError), afterError);
            Assert.Equal("new-room", afterCommit.Room);
        } finally {
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
            if (Directory.Exists(stagingDirectory)) {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    // An install that fails before it has moved the slot's previous snapshot aside must
    // leave that snapshot where a load can still find it.
    //
    // The move that banks the previous file is the one most likely to fail: staging is
    // under the system temp path and the destination is under Saves, so on any install
    // where those are different volumes it is a copy of the whole snapshot and fails on
    // a full temp volume with the source untouched. Forced here with a read-only staging
    // directory, which is the same failure with no volume to fill - the move cannot
    // create the backup, and the previous snapshot never leaves the destination.
    //
    // Off Windows only: making a directory unwritable there needs an ACL edit, and the
    // branch under test is platform-neutral.
    [Fact]
    public void AFailedSnapshotInstallKeepsTheSnapshotTheSlotAlreadyHad() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        string slotName = "Akron failed install " + Guid.NewGuid().ToString("N");
        string stagingDirectory = Path.Combine(Path.GetTempPath(), "akron-failed-install-" + Guid.NewGuid().ToString("N"));
        try {
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                slotName, "Map/A", "old-room", 1, MinimalDocument(), out string oldError), oldError);
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                slotName, "Map/A", "new-room", 1, MinimalDocument(), out string newError, stagingDirectory), newError);
            File.SetUnixFileMode(stagingDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            using (AkronStartPosReconstruction.PreparedSnapshotInstall prepared =
                   AkronStartPosReconstruction.PrepareSnapshotInstall(slotName, stagingDirectory)) {
                Assert.False(prepared.Install(out string installError));
                Assert.NotEmpty(installError);
                // Nothing was banked, so there was nothing a rollback could fail to put
                // back and the Set reports the slot as kept, which it is.
                Assert.False(prepared.PreviousSnapshotLost);
            }

            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                slotName, out AkronReconstructionDocument kept, out string keptError), keptError);
            Assert.Equal("old-room", kept.Room);
        } finally {
            if (Directory.Exists(stagingDirectory)) {
                File.SetUnixFileMode(
                    stagingDirectory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                Directory.Delete(stagingDirectory, recursive: true);
            }
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    // A prepared install is one attempt, and a second one must be refused rather than
    // run on the first attempt's record.
    //
    // The sequence this closes: a first attempt claims a free slot, its move-in fails, and
    // its rollback deletes the claim. The record still says this object claimed the
    // destination. A snapshot then arrives at that slot, a second attempt fails before it
    // can bank it, and its rollback reads the stale claim and deletes a snapshot the
    // object never created.
    //
    // Forced with a read-only staging directory, which fails the move-in of the first
    // attempt (the staged file cannot be removed from a directory that cannot be written)
    // and the banking move of the second. Off Windows only, where making a directory
    // unwritable needs an ACL edit.
    [Fact]
    public void ASecondInstallAttemptIsRefusedRatherThanRunOnTheFirstOnesRecord() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        string slotName = "Akron second attempt " + Guid.NewGuid().ToString("N");
        string stagingDirectory = Path.Combine(Path.GetTempPath(), "akron-second-attempt-" + Guid.NewGuid().ToString("N"));
        try {
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                slotName, "Map/A", "new-room", 1, MinimalDocument(), out string newError, stagingDirectory), newError);
            File.SetUnixFileMode(stagingDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            using (AkronStartPosReconstruction.PreparedSnapshotInstall prepared =
                   AkronStartPosReconstruction.PrepareSnapshotInstall(slotName, stagingDirectory)) {
                // The slot is empty, so this attempt claims the destination and then
                // cannot move the staged file in. Its rollback removes the claim.
                Assert.False(prepared.Install(out string firstError));
                Assert.NotEmpty(firstError);
                Assert.False(AkronStartPosReconstruction.HasSnapshot(slotName));

                // A snapshot arrives in the slot between the two attempts.
                Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                    slotName, "Map/A", "old-room", 1, MinimalDocument(), out string oldError), oldError);

                Assert.False(prepared.Install(out string secondError));
                Assert.Contains("already been attempted", secondError);
            }

            // The snapshot that arrived in between is still loadable: the second attempt
            // never ran, so its rollback never acted on the first attempt's claim.
            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                slotName, out AkronReconstructionDocument kept, out string keptError), keptError);
            Assert.Equal("old-room", kept.Room);
        } finally {
            if (Directory.Exists(stagingDirectory)) {
                File.SetUnixFileMode(
                    stagingDirectory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                Directory.Delete(stagingDirectory, recursive: true);
            }
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
        }
    }

    // An install may only remove a destination it can prove it created.
    //
    // The install asks whether the slot already holds a snapshot by moving it aside and
    // reading the failure, and "missing" is also what comes back for a query the
    // filesystem could not complete: a folder in the path that has lost its search
    // permission, an IO error, a Windows attribute read a scanner is holding off.
    // Measured outside the suite, with the snapshot folder made unsearchable and then
    // searchable again mid-install: taking that answer at face value ends with
    // File.Delete removing a snapshot the install never touched, which is the loss this
    // whole path exists to prevent. So the emptiness answer is proved with an exclusive
    // create, and the rollback's delete is reached only through that proof.
    //
    // Asserted on the source because the divergence needs the filesystem to change
    // between two statements inside Install, which no test can arrange from outside;
    // what a test can hold is that the delete has no other route to it.
    [Fact]
    public void AnInstallOnlyRemovesADestinationItProvedItCreated() {
        string source = File.ReadAllText(GetSourcePath("SaveLoad", "akron-reconstruction-graph.cs"));
        int install = source.IndexOf("internal sealed class PreparedSnapshotInstall", StringComparison.Ordinal);
        int end = source.IndexOf("private const string CompareInfoSortNameKeyPrefix", install, StringComparison.Ordinal);
        Assert.True(install >= 0 && end > install);
        string transaction = SourceSlice(source, install, end - install);

        // The proof, and the only branch that may delete, gated on it.
        Assert.Contains("new FileStream(destinationPath, FileMode.CreateNew", transaction);
        Assert.Contains("destinationClaimed = true;", transaction);
        Assert.Contains("} else if (destinationClaimed) {\n", transaction);
        Assert.Equal(1, CountOccurrences(transaction, "File.Delete("));

        // Neither the install nor the rollback may ask the filesystem what it did: those
        // answers are the ones that cannot tell "nothing there" from "cannot say".
        Assert.DoesNotContain("File.Exists(destinationPath)", transaction);
        Assert.DoesNotContain("File.Exists(backupPath)", transaction);
        // The one existence question left is about the staged file the caller wrote, and
        // a wrong answer there refuses the install rather than removing anything.
        Assert.Equal(1, CountOccurrences(transaction, "File.Exists("));
        Assert.Contains("File.Exists(sourcePath)", transaction);
    }

    // A rollback that cannot do its job says so and stops; it does not throw.
    //
    // Both of its callers are already carrying a failure. Install's catch block calls it
    // on the way to reporting an IOException, so a throw there replaces the failure the
    // caller was about to see with a different one raised from inside a catch. Dispose
    // calls it at the end of a using, so a throw there discards whatever the using body
    // was propagating - which on the setup-pack import path is the only account of what
    // went wrong.
    //
    // Forced by putting a directory where the snapshot file belongs, which is a path no
    // file move can overwrite on any platform.
    [Fact]
    public void AFailedSnapshotRollbackIsReportedRatherThanThrownOutOfDispose() {
        string slotName = "Akron rollback failure " + Guid.NewGuid().ToString("N");
        string stagingDirectory = Path.Combine(Path.GetTempPath(), "akron-rollback-" + Guid.NewGuid().ToString("N"));
        string installedPath = AkronStartPosReconstruction.GetSnapshotPath(slotName);
        try {
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                slotName, "Map/A", "old-room", 1, MinimalDocument(), out string oldError), oldError);
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                slotName, "Map/A", "new-room", 1, MinimalDocument(), out string newError, stagingDirectory), newError);

            AkronStartPosReconstruction.PreparedSnapshotInstall prepared =
                AkronStartPosReconstruction.PrepareSnapshotInstall(slotName, stagingDirectory);
            using (prepared) {
                Assert.True(prepared.Install(out string stagedError), stagedError);
                File.Delete(installedPath);
                Directory.CreateDirectory(installedPath);
            }

            // Reaching here at all is the first assertion: leaving the using un-committed
            // runs the rollback, and its move onto a directory cannot succeed.
            Assert.True(Directory.Exists(installedPath));
            // The previous snapshot is in the staging directory, which the caller deletes
            // as soon as the completion returns, so this rollback has cost the slot its
            // restart copy. Saying so is what stops the failed Set from reporting that
            // the previous StartPos was kept.
            Assert.True(prepared.PreviousSnapshotLost);
        } finally {
            if (Directory.Exists(installedPath)) {
                Directory.Delete(installedPath, recursive: true);
            }
            AkronStartPosReconstruction.DeleteSnapshot(slotName);
            if (Directory.Exists(stagingDirectory)) {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    // The other half of the rollback above: what the player is told. A Set whose install
    // rolled back without putting the slot's snapshot file back leaves a slot that works
    // from memory for the rest of the session and has nothing to load afterwards, so the
    // sentence that says the previous StartPos was kept is the one thing it must not say.
    [Fact]
    public void AFailedSetSaysWhenThePreviousSnapshotCouldNotBePutBack() {
        string kept = AkronActions.DescribeFailedStartPosReplacement(
            2, "the restart copy failed", previousDurableStateLost: false);
        string lost = AkronActions.DescribeFailedStartPosReplacement(
            2, "the restart copy failed", previousDurableStateLost: true);

        Assert.Equal(
            "StartPos 2 was not replaced because the restart copy failed. " +
            "The previous StartPos 2 was kept.",
            kept);
        Assert.Equal(
            "StartPos 2 was not replaced because the restart copy failed. " +
            "The previous StartPos 2 could not be put back either, so it works until you " +
            "leave this map and then has to be set again.",
            lost);

        // Read from the install after its rollback has run, which is the only place the
        // answer exists.
        string source = File.ReadAllText(GetActionsSourcePath());
        int dispose = source.IndexOf("installedSnapshot?.Dispose();", StringComparison.Ordinal);
        int read = source.IndexOf(
            "installedSnapshot?.PreviousSnapshotLost == true", dispose, StringComparison.Ordinal);
        Assert.True(dispose >= 0);
        Assert.True(read > dispose);
    }

    [Fact]
    public void StartPosLoadsWaitForAStableEngineBoundary() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int loadStart = source.IndexOf("public static void LoadStartPos(Level level)", StringComparison.Ordinal);
        int loadEnd = source.IndexOf("public static void LoadStartPosSlot", loadStart, StringComparison.Ordinal);
        int deathStart = source.IndexOf("internal static void RestoreStartPosAfterDeath", StringComparison.Ordinal);
        int deathEnd = source.IndexOf("private static bool RestoreStartPos", deathStart, StringComparison.Ordinal);

        string loadMethod = SourceSlice(source, loadStart, loadEnd - loadStart);
        string deathMethod = SourceSlice(source, deathStart, deathEnd - deathStart);

        Assert.Contains("AkronModule.ScheduleAfterStableEngineUpdate", loadMethod);
        Assert.DoesNotContain("level.OnEndOfFrame", loadMethod);
        Assert.Contains("AkronModule.ScheduleAfterStableEngineUpdate", deathMethod);
        Assert.DoesNotContain("level.OnEndOfFrame", deathMethod);
    }

    [Fact]
    public void DeferredStartPosLoadStopsAfterTheSceneChanges() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int load = source.IndexOf("public static void LoadStartPos(Level level)", StringComparison.Ordinal);
        int schedule = source.IndexOf("AkronModule.ScheduleAfterStableEngineUpdate(() =>", load, StringComparison.Ordinal);
        int sceneGuard = source.IndexOf("if (Engine.Scene != level)", schedule, StringComparison.Ordinal);
        int restore = source.IndexOf("RestoreStartPos(", sceneGuard, StringComparison.Ordinal);

        Assert.True(schedule > load);
        Assert.True(sceneGuard > schedule);
        Assert.True(restore > sceneGuard);
    }

    [Fact]
    public void StartPosCaptureFiltersIgnoredEntitiesWithoutChangingTheLiveRoom() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        string ignoreSource = File.ReadAllText(GetSourcePath("Core", "AkronIgnoreSaveStateComponent.cs"));
        int captureStart = source.IndexOf("public static AkronSaveLoadSlot CaptureRuntimeState", StringComparison.Ordinal);
        int captureEnd = source.IndexOf("public static AkronSaveLoadResult SaveRuntimeState", captureStart, StringComparison.Ordinal);
        string captureMethod = SourceSlice(source, captureStart, captureEnd - captureStart);

        Assert.DoesNotContain("AkronIgnoreSaveStateComponent.RemoveAll(level);", captureMethod);
        Assert.DoesNotContain("AkronIgnoreSaveStateComponent.ReAddAll(level);", captureMethod);
        Assert.Contains("AkronIgnoreSaveStateComponent.RemoveAllFromSnapshot(saveSlot.SavedLevel);", captureMethod);
        Assert.Contains("Tracker.Refresh(level, force: true);", ignoreSource);
    }

    [Fact]
    public void SnapshotFilteringDropsThePlaybackGhostAndHandsBackItsTrailSlots() {
        string ignoreSource = File.ReadAllText(GetSourcePath("Core", "AkronIgnoreSaveStateComponent.cs"));
        int filterStart = ignoreSource.IndexOf(
            "internal static void RemoveAllFromSnapshot(Level level)",
            StringComparison.Ordinal);
        int filterEnd = ignoreSource.IndexOf("internal static class AkronSnapshotExclusion", filterStart, StringComparison.Ordinal);
        string filterMethod = SourceSlice(ignoreSource, filterStart, filterEnd - filterStart);

        // Removing the ghost from the entity list is only half of it. A trail
        // snapshot renders the ghost's own PlayerSprite and PlayerHair, so a
        // snapshot that keeps one keeps the ghost too, and the load then refuses
        // on the ghost rather than on the trail that held it.
        Assert.Contains("AkronSnapshotExclusion.IsExcludedFromSnapshot(entity)", filterMethod);
        Assert.Contains("AkronSnapshotExclusion.ReleaseTrailManagerSlot(entity)", filterMethod);
    }

    [Fact]
    public void PersistentRestoreDetachesThePlaybackGhostBeforeItReadsTheFreshRoomShape() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        string ignoreSource = File.ReadAllText(GetSourcePath("Core", "AkronIgnoreSaveStateComponent.cs"));
        int core = source.IndexOf(
            "private static AkronSaveLoadResult RestorePersistentRuntimeStateCore",
            StringComparison.Ordinal);
        int afterActionState = source.IndexOf(
            "private static AkronSaveLoadResult RestorePersistentRuntimeStateAfterActionState",
            core,
            StringComparison.Ordinal);
        string coreMethod = SourceSlice(source, core, afterActionState - core);
        int freshRoom = coreMethod.IndexOf("TryLoadFreshRoom(level, document.Room, out string freshRoomError)", StringComparison.Ordinal);
        int detach = coreMethod.IndexOf("AkronSnapshotExclusion.DetachFromLevel(level)", StringComparison.Ordinal);
        int reattach = coreMethod.IndexOf("AkronSnapshotExclusion.ReattachToLevel(level, detachedGhosts)", StringComparison.Ordinal);

        int detachMethod = ignoreSource.IndexOf("internal static List<Entity> DetachFromLevel", StringComparison.Ordinal);
        int reattachMethod = ignoreSource.IndexOf("internal static void ReattachToLevel", detachMethod, StringComparison.Ordinal);
        string detachPath = SourceSlice(ignoreSource, detachMethod, reattachMethod - detachMethod);
        Assert.Contains("ReleaseTrailManagerSlot(entity);", detachPath);

        // The rebuild resolves saved objects by their path in the live fresh room,
        // so the live room has to match the shape the snapshot was measured against
        // before anything reads it. The reattach sits in the finally so an exception
        // on its way to the rollback does not leave the room without its ghost.
        Assert.True(freshRoom > 0);
        Assert.True(detach > freshRoom);
        Assert.True(reattach > detach);
        Assert.Contains("} finally {", SourceSlice(coreMethod, detach, reattach - detach));

        // The live room shape has to be settled before the rebuild reads it, so the
        // detach must not sit inside the method that does the reading.
        int afterActionStateEnd = source.IndexOf(
            "private static bool ApplyPersistentRuntimeState",
            afterActionState,
            StringComparison.Ordinal);
        string restoreMethod = SourceSlice(source, afterActionState, afterActionStateEnd - afterActionState);

        Assert.DoesNotContain("AkronSnapshotExclusion.DetachFromLevel(level)", restoreMethod);

        // The outer bracket runs around the fresh room load, so it cannot be the
        // one that holds the playback ghost: the ghosts it would stash belong to
        // the room about to be destroyed, and putting those back would leave the
        // room with one more ghost after every Load.
        int outerStart = source.IndexOf(
            "private static AkronSaveLoadResult RestorePersistentRuntimeState(",
            StringComparison.Ordinal);
        int outerEnd = source.IndexOf(
            "private static AkronSaveLoadResult CacheRestoredRuntimeState",
            outerStart,
            StringComparison.Ordinal);
        string outerMethod = SourceSlice(source, outerStart, outerEnd - outerStart);

        Assert.Contains("AkronIgnoreSaveStateComponent.RemoveAll(level);", outerMethod);
        Assert.DoesNotContain("AkronSnapshotExclusion.DetachFromLevel(level)", outerMethod);
    }

    [Fact]
    public void TheIgnoreComponentDoesNotAlsoStashThePlaybackGhostAcrossARoomReload() {
        string ignoreSource = File.ReadAllText(GetSourcePath("Core", "AkronIgnoreSaveStateComponent.cs"));
        int removeAll = ignoreSource.IndexOf("public static void RemoveAll(Level level)", StringComparison.Ordinal);
        int reAddAll = ignoreSource.IndexOf("public static void ReAddAll(Level level)", removeAll, StringComparison.Ordinal);
        string removeAllMethod = SourceSlice(ignoreSource, removeAll, reAddAll - removeAll);

        // A mod can call the public IgnoreSaveState on anything, including a ghost.
        // If this bracket stashed one it would re-add the destroyed room's ghost on
        // top of the one the fresh room load built, once per Load.
        Assert.Contains("AkronSnapshotExclusion.IsExcludedFromSnapshot(ignoreComponent.Entity)", removeAllMethod);
    }

    [Fact]
    public void TheFreshRoomReloadClearsTheTrailsBeforeItUnloadsTheRoom() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int freshRoom = source.IndexOf(
            "private static bool TryLoadFreshRoom(Level level, string roomName, out string error)",
            StringComparison.Ordinal);
        int freshRoomEnd = source.IndexOf(
            "internal static void DrainFreshRoomEntityLists(EntityList entities)",
            freshRoom,
            StringComparison.Ordinal);
        string freshRoomMethod = SourceSlice(source, freshRoom, freshRoomEnd - freshRoom);
        int settle = freshRoomMethod.IndexOf("level.Entities.UpdateLists();", StringComparison.Ordinal);
        int clear = freshRoomMethod.IndexOf("TrailManager.Clear();", StringComparison.Ordinal);
        int unload = freshRoomMethod.IndexOf("level.UnloadLevel();", StringComparison.Ordinal);
        int load = freshRoomMethod.IndexOf("level.LoadLevel(Player.IntroTypes.Respawn);", StringComparison.Ordinal);

        // Celeste.Level.Reload is TrailManager.Clear(), UnloadLevel(),
        // LoadLevel(Respawn), in that order, and this method is Akron's copy of
        // that sequence. UnloadLevel keeps every Tags.Global entity, and a
        // TrailManager.Snapshot is Tags.Global while holding the PlayerSprite and
        // PlayerHair of the entity it was made from, so without the clear a
        // snapshot outlives the entity this reload destroys and keeps it reachable
        // with a null Scene while LoadLevel rebuilds the same map entity with the
        // same EntityID. A saved node can then pair with the dead copy.
        //
        // The settle in front of the clear is Akron's own. The load runs at a render
        // boundary, so a trail created during the update that just ran is still in
        // EntityList.toAdd with a null Scene; RemoveSelf ignores it and UpdateLists
        // installs toAdd before toRemove, so without settling first the newest trail
        // is installed by UnloadLevel after the clear and survives.
        Assert.True(clear >= 0, "TryLoadFreshRoom must clear the trails before it unloads the room.");
        Assert.True(settle >= 0, "TryLoadFreshRoom must settle pending entity adds before it clears the trails.");
        Assert.True(clear > settle);
        Assert.True(unload > clear);
        Assert.True(load > unload);
    }

    [Fact]
    public void StartPosCapturesRenderedProcessBuffersAtTheSetBoundary() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int captureStart = source.IndexOf("public static AkronSaveLoadSlot CaptureRuntimeState", StringComparison.Ordinal);
        int bufferCapture = source.IndexOf("AkronGameplayBufferState.Capture()", captureStart, StringComparison.Ordinal);
        int beforeSaveActions = source.IndexOf("action.BeforeSaveState?.Invoke(level);", captureStart, StringComparison.Ordinal);
        int roomClone = source.IndexOf("BuildNativeSlot(level, CurrentSlotName", captureStart, StringComparison.Ordinal);
        int restoreStart = source.IndexOf("private static AkronSaveLoadResult RestorePersistentRuntimeState", StringComparison.Ordinal);
        int bufferRestore = source.IndexOf("AkronGameplayBufferState.RestoreBestEffort(document.GameplayBuffers", restoreStart, StringComparison.Ordinal);

        Assert.True(captureStart >= 0);
        Assert.True(bufferCapture > captureStart);
        Assert.True(beforeSaveActions > bufferCapture);
        Assert.True(roomClone > beforeSaveActions);
        Assert.True(bufferRestore > restoreStart);
    }

    [Fact]
    public void GameplayBufferChangesCannotAbortAnAppliedStartPosRestore() {
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());
        string reconstructionSource = File.ReadAllText(GetSourcePath("SaveLoad", "akron-reconstruction-graph.cs"));

        Assert.Contains("public static void RestoreBestEffort", reconstructionSource);
        Assert.Contains("if (!Adapter.RestoreExisting", reconstructionSource);
        Assert.Contains("Skipped StartPos gameplay buffer", reconstructionSource);
        Assert.Contains("AkronGameplayBufferState.RestoreBestEffort(saveSlot.GameplayBuffers);", saveLoadSource);
        Assert.Contains("AkronGameplayBufferState.RestoreBestEffort(document.GameplayBuffers);", saveLoadSource);
        Assert.DoesNotContain("!AkronGameplayBufferState.Restore", saveLoadSource);
    }

    [Fact]
    public void StartPosPresentsTheSavedLevelBufferOnTheFirstRestoredFrame() {
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());
        string moduleSource = File.ReadAllText(GetModuleSourcePath());
        int runtimeRestore = saveLoadSource.IndexOf("public static AkronSaveLoadResult RestoreRuntimeState", StringComparison.Ordinal);
        int persistentRestore = saveLoadSource.IndexOf("private static AkronSaveLoadResult RestorePersistentRuntimeState", StringComparison.Ordinal);

        Assert.True(saveLoadSource.IndexOf("AkronGameplayBufferState.ArmLevelPresentation", runtimeRestore, StringComparison.Ordinal) > runtimeRestore);
        Assert.True(saveLoadSource.IndexOf("AkronGameplayBufferState.ArmLevelPresentation", persistentRestore, StringComparison.Ordinal) > persistentRestore);
        Assert.Contains("IL.Celeste.Level.Render += LevelOnRenderForStartPosPresentation", moduleSource);
        Assert.Contains("AkronGameplayBufferState.PresentArmedLevelBuffer", moduleSource);
    }

    [Fact]
    public void StartPosCaptureOnlyBlocksDuringTheNativeSetBoundary() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int captureStart = source.IndexOf("private static void CaptureStartPos", StringComparison.Ordinal);
        int captureEnd = source.IndexOf("private static void ApplyPlacedStartPosBeforeCapture", captureStart, StringComparison.Ordinal);
        string captureMethod = SourceSlice(source, captureStart, captureEnd - captureStart);
        int busyCheck = captureMethod.IndexOf("if (startPosCaptureInProgress)", StringComparison.Ordinal);
        int begin = captureMethod.IndexOf("startPosCaptureInProgress = true;", busyCheck, StringComparison.Ordinal);
        int save = captureMethod.IndexOf("SaveRuntimeState", begin, StringComparison.Ordinal);
        int release = captureMethod.IndexOf("startPosCaptureInProgress = false;", save, StringComparison.Ordinal);
        int enqueue = captureMethod.IndexOf("AkronStartPosPersistence.Enqueue", release, StringComparison.Ordinal);

        Assert.True(busyCheck >= 0 && begin > busyCheck && save > begin);
        Assert.True(release > save);
        Assert.True(enqueue > release);
    }

    [Fact]
    public void ExactStartPosRestoreDoesNotRewriteSavedGameplayStateAfterLoad() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int methodStart = source.IndexOf("private static bool RestoreStartPos(", StringComparison.Ordinal);
        int methodEnd = source.IndexOf("internal static void RelinkRuntimeRenderState", methodStart, StringComparison.Ordinal);
        string method = SourceSlice(source, methodStart, methodEnd - methodStart);

        Assert.DoesNotContain("ApplyStartPosToPlayer", method);
        Assert.DoesNotContain("RemoveStartPosDeathArtifacts", method);
        Assert.DoesNotContain("Session.RespawnPoint =", method);
        Assert.DoesNotContain("StartStartPosCameraFollow", method);
    }

    [Fact]
    public void StartPosRestoreRebuildsTheCumulativeSlotRegistry() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int methodStart = source.IndexOf("private static bool RestoreStartPos(", StringComparison.Ordinal);
        int methodEnd = source.IndexOf("internal static void RelinkRuntimeRenderState", methodStart, StringComparison.Ordinal);
        string method = SourceSlice(source, methodStart, methodEnd - methodStart);

        int preserveCatalog = method.IndexOf("Dictionary<string, AkronPersistedStartPosMap> currentStartPositionsByMap", StringComparison.Ordinal);
        int restore = method.IndexOf("AkronSaveLoadService.LoadRuntimeState", StringComparison.Ordinal);
        int restoreCatalog = method.IndexOf("RestoreStartPosCatalog", restore, StringComparison.Ordinal);
        int registryReload = method.IndexOf("LoadStartPositionsForLevel(currentLevel);", StringComparison.Ordinal);
        int loadedSlotUpdate = method.IndexOf("AkronModule.Session.LastLoadedStartPosSlot = loadedSlot;", StringComparison.Ordinal);

        Assert.True(preserveCatalog >= 0 && restore > preserveCatalog);
        Assert.True(restoreCatalog > restore);
        Assert.True(registryReload > restoreCatalog);
        Assert.True(loadedSlotUpdate > registryReload);
    }

    [Fact]
    public void RestoringAnOlderStartPosPreservesSlotsCreatedLater() {
        Dictionary<string, AkronPersistedStartPosMap> currentCatalog = new Dictionary<string, AkronPersistedStartPosMap> {
            ["Map/A"] = new AkronPersistedStartPosMap {
                Slots = new Dictionary<int, AkronPersistedStartPos> {
                    [1] = new AkronPersistedStartPos { AreaSid = "Map/A", Room = "room-a" },
                    [2] = new AkronPersistedStartPos { AreaSid = "Map/A", Room = "room-b" }
                }
            }
        };
        AkronModuleSaveData restoredSnapshotSaveData = new AkronModuleSaveData {
            StartPositionsByMap = new Dictionary<string, AkronPersistedStartPosMap> {
                ["Map/A"] = new AkronPersistedStartPosMap {
                    Slots = new Dictionary<int, AkronPersistedStartPos> {
                        [1] = new AkronPersistedStartPos { AreaSid = "Map/A", Room = "room-a" }
                    }
                }
            }
        };

        AkronActions.RestoreStartPosCatalog(restoredSnapshotSaveData, currentCatalog);

        Assert.Same(currentCatalog, restoredSnapshotSaveData.StartPositionsByMap);
        Assert.Equal(new[] { 1, 2 }, restoredSnapshotSaveData.StartPositionsByMap["Map/A"].Slots.Keys.OrderBy(slot => slot));
    }

    [Fact]
    public void PersistentRestoreRebuildsProcessTrackerKeysBeforeVerification() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int restoreStart = source.IndexOf("private static AkronSaveLoadResult RestorePersistentRuntimeState", StringComparison.Ordinal);
        int graphRestore = source.IndexOf("AkronStartPosReconstruction.Restore(document, freshRuntimeState)", restoreStart, StringComparison.Ordinal);
        int helperLoad = source.IndexOf("action.LoadState?.Invoke", graphRestore, StringComparison.Ordinal);
        int graphReapply = source.IndexOf("AkronStartPosReconstruction.Reapply(document, restore)", helperLoad, StringComparison.Ordinal);
        int trackerRefresh = source.IndexOf("Tracker.Refresh(level, force: true);", graphReapply, StringComparison.Ordinal);
        int verification = source.IndexOf("AkronStartPosReconstruction.Verify(", graphRestore, StringComparison.Ordinal);

        Assert.True(restoreStart >= 0);
        Assert.True(graphRestore > restoreStart);
        Assert.True(helperLoad > graphRestore);
        Assert.True(graphReapply > helperLoad);
        Assert.True(trackerRefresh > graphReapply);
        Assert.True(verification > trackerRefresh);
        int contextLength = Math.Min(verification - trackerRefresh + 200, source.Length - trackerRefresh);
        Assert.Contains("GetPostRestoreVerificationMasks(document)", SourceSlice(source, trackerRefresh, contextLength));
    }

    [Fact]
    public void StartPosCaptureAlwaysKeepsCumulativeTimeAndDeaths() {
        string source = File.ReadAllText(GetActionsSourcePath());

        Assert.Contains("SaveRuntimeState(level, stateSlotName, saveTimeAndDeaths: false)", source);
    }

    [Fact]
    public void StartPosHotkeyCaptureKeepsTheExistingPreUpdateBoundary() {
        string moduleSource = File.ReadAllText(GetModuleSourcePath());
        int hotkeys = moduleSource.IndexOf("HandleHotkeys(self);", StringComparison.Ordinal);
        int gameplayUpdate = moduleSource.IndexOf("orig(self);", hotkeys, StringComparison.Ordinal);

        Assert.True(hotkeys >= 0);
        Assert.True(gameplayUpdate > hotkeys);
    }

    [Fact]
    public void GameplayBufferQaProbeReadsTheFinishedRoomBeforeAkronHud() {
        string moduleSource = File.ReadAllText(GetModuleSourcePath());
        int baseRender = moduleSource.IndexOf("orig(self);", moduleSource.IndexOf("private static void EngineOnRenderCore", StringComparison.Ordinal), StringComparison.Ordinal);
        int pixelProbe = moduleSource.IndexOf("AkronCapture.CapturePendingGameplayBufferQaFrame();", baseRender, StringComparison.Ordinal);
        int akronHud = moduleSource.IndexOf("RenderAkronLevelHud(postRenderLevel);", baseRender, StringComparison.Ordinal);

        Assert.True(baseRender >= 0);
        Assert.True(pixelProbe > baseRender);
        Assert.True(akronHud > pixelProbe);
    }

    [Fact]
    public void ExactReferenceCommandCapturesTheFirstRenderedSetFrame() {
        string qaSource = File.ReadAllText(GetQaCommandsSourcePath());
        int method = qaSource.IndexOf("public static void QaStartPosReferenceCapture", StringComparison.Ordinal);
        int pixelRequest = qaSource.IndexOf("AkronCapture.CaptureGameplayBufferQaFrameNow(tag", method, StringComparison.Ordinal);
        int defer = qaSource.IndexOf("AkronAutomationService.DeferRunCompletion();", pixelRequest, StringComparison.Ordinal);
        int startPosCapture = qaSource.IndexOf("AkronActions.SetStartPos(level, captured =>", pixelRequest, StringComparison.Ordinal);
        int complete = qaSource.IndexOf("AkronAutomationService.CompleteDeferredRun();", startPosCapture, StringComparison.Ordinal);
        int methodEnd = qaSource.IndexOf("[Command(\"akron_qa_startpos_load_probe\"", startPosCapture, StringComparison.Ordinal);

        Assert.True(method >= 0);
        Assert.True(pixelRequest > method);
        Assert.True(defer > pixelRequest);
        Assert.True(startPosCapture > defer);
        Assert.True(complete > startPosCapture);
        Assert.True(methodEnd > startPosCapture);
        Assert.DoesNotContain("CapturePendingGameplayBufferQaFrame", SourceSlice(qaSource, method, methodEnd - method));
        Assert.DoesNotContain("RequestGameplayBufferQaCapture", SourceSlice(qaSource, method, methodEnd - method));
    }

    [Fact]
    public void EdgeCaptureKeepsAutomationOpenUntilTheDiskSnapshotCommits() {
        string qaSource = File.ReadAllText(GetQaCommandsSourcePath());
        int method = qaSource.IndexOf("public static void QaStartPosEdgeCapture", StringComparison.Ordinal);
        int defer = qaSource.IndexOf("AkronAutomationService.DeferRunCompletion();", method, StringComparison.Ordinal);
        int capture = qaSource.IndexOf("AkronActions.SetStartPos(level, captured =>", defer, StringComparison.Ordinal);
        int complete = qaSource.IndexOf("AkronAutomationService.CompleteDeferredRun();", capture, StringComparison.Ordinal);
        int methodEnd = qaSource.IndexOf("[Command(\"akron_qa_startpos_reference_capture\"", complete, StringComparison.Ordinal);

        Assert.True(method >= 0);
        Assert.True(defer > method);
        Assert.True(capture > defer);
        Assert.True(complete > capture);
        Assert.True(methodEnd > complete);
    }

    [Fact]
    public void LoadProbeRecordsItsPixelCaptureAfterTheRestoreFrame() {
        string qaSource = File.ReadAllText(GetQaCommandsSourcePath());
        int method = qaSource.IndexOf("public static void QaStartPosLoadProbe", StringComparison.Ordinal);
        int load = qaSource.IndexOf("AkronActions.LoadStartPos(level);", method, StringComparison.Ordinal);
        int probe = qaSource.IndexOf("Func<Level, bool> recordProbe =", load, StringComparison.Ordinal);
        int pixelCapture = qaSource.IndexOf("AkronCapture.RequestGameplayBufferQaCapture(", probe, StringComparison.Ordinal);
        int stableBoundary = qaSource.IndexOf("AkronModule.ScheduleAfterStableEngineUpdate(() =>", pixelCapture, StringComparison.Ordinal);

        Assert.True(method >= 0);
        Assert.True(load > method);
        Assert.True(probe > load);
        Assert.True(pixelCapture > probe);
        Assert.True(stableBoundary > pixelCapture);
    }

    [Fact]
    public void LoadProbeKeepsAutomationOpenUntilEndOfFrameStateIsRecorded() {
        string qaSource = File.ReadAllText(GetQaCommandsSourcePath());
        string automationSource = File.ReadAllText(GetSourcePath("Automation", "akron-automation-service.cs"));
        int method = qaSource.IndexOf("public static void QaStartPosLoadProbe", StringComparison.Ordinal);
        int defer = qaSource.IndexOf("AkronAutomationService.DeferRunCompletion();", method, StringComparison.Ordinal);

        Assert.True(method >= 0);
        Assert.True(defer > method);

        int stableBoundary = qaSource.IndexOf("AkronModule.ScheduleAfterStableEngineUpdate(() =>", method, StringComparison.Ordinal);
        Assert.True(stableBoundary > method);
        Assert.True(defer > stableBoundary);

        int complete = qaSource.IndexOf("AkronAutomationService.CompleteDeferredRun();", stableBoundary, StringComparison.Ordinal);

        Assert.True(complete > stableBoundary);
        Assert.Contains("if (HandleDeferredRun())", automationSource);
        Assert.Contains("DeferredRunFrameLimit", automationSource);
        Assert.Contains("FailDeferredRun", automationSource);
    }

    [Fact]
    public void IdlePollSurvivesTheFrameCounterThatAStartPosRestores() {
        string source = File.ReadAllText(GetSourcePath("Automation", "akron-automation-service.cs"));
        int process = source.IndexOf("public static void ProcessPendingCommands", StringComparison.Ordinal);
        Assert.True(process >= 0);
        int idleBranch = source.IndexOf("if (!hasActiveRun) {", process, StringComparison.Ordinal);
        Assert.True(idleBranch > process);
        int guard = source.IndexOf("if (Engine.FrameCounter < nextIdlePollFrame &&", idleBranch, StringComparison.Ordinal);
        Assert.True(guard > idleBranch);
        int rewind = source.IndexOf("nextIdlePollFrame - Engine.FrameCounter <= IdlePollFrames", guard, StringComparison.Ordinal);
        Assert.True(rewind > guard);
        int schedule = source.IndexOf("nextIdlePollFrame = Engine.FrameCounter + IdlePollFrames;", rewind, StringComparison.Ordinal);
        Assert.True(schedule > rewind);

        // FinalizeRun must not own this: LoadStartPos runs the restore on a later
        // engine boundary, so the run has already finalized by the time the counter
        // moves and a deadline written there is the pre-restore clock.
        int finalizeStart = source.IndexOf("private static void FinalizeRun(", StringComparison.Ordinal);
        int finalizeEnd = source.IndexOf("private static void WriteResult(", finalizeStart, StringComparison.Ordinal);
        string finalizeRun = SourceSlice(source, finalizeStart, finalizeEnd - finalizeStart);

        Assert.True(finalizeStart >= 0);
        Assert.True(finalizeEnd > finalizeStart);
        Assert.DoesNotContain("nextIdlePollFrame", finalizeRun);

        string actionsSource = File.ReadAllText(GetActionsSourcePath());
        int loadStartPos = actionsSource.IndexOf("public static void LoadStartPos(Level level)", StringComparison.Ordinal);
        int deferredRestore = actionsSource.IndexOf("AkronModule.ScheduleAfterStableEngineUpdate(", loadStartPos, StringComparison.Ordinal);

        Assert.True(loadStartPos >= 0);
        Assert.True(deferredRestore > loadStartPos);
    }

    [Fact]
    public void DeferredAutomationCompletionKeepsLaterCommandsQueued() {
        string source = File.ReadAllText(GetSourcePath("Automation", "akron-automation-service.cs"));
        int processingStart = source.IndexOf("isProcessing = true;", StringComparison.Ordinal);
        int deferredGate = source.IndexOf("if (HandleDeferredRun())", processingStart, StringComparison.Ordinal);
        int dequeue = source.IndexOf("PendingCommands.Dequeue()", processingStart, StringComparison.Ordinal);
        int completionStart = source.IndexOf("public static void CompleteDeferredRun()", StringComparison.Ordinal);
        int completionEnd = source.IndexOf("private static bool HandleDeferredRun()", completionStart, StringComparison.Ordinal);
        string completionMethod = SourceSlice(source, completionStart, completionEnd - completionStart);

        Assert.True(processingStart >= 0 && deferredGate > processingStart && dequeue > deferredGate);
        Assert.Contains("if (PendingCommands.Count == 0)", completionMethod);
        Assert.Contains("FinalizeRun();", completionMethod);
        Assert.Contains("WriteResult(status: \"pending\");", completionMethod);
        Assert.True(
            completionMethod.IndexOf("if (PendingCommands.Count == 0)", StringComparison.Ordinal) <
            completionMethod.IndexOf("FinalizeRun();", StringComparison.Ordinal));
    }

    [Fact]
    public void PersistentRestoreRejectsNonFiniteProcessGlobalFloats() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int apply = source.IndexOf("private static bool ApplyPersistentRuntimeState", StringComparison.Ordinal);
        int timeRateAssignment = source.IndexOf("Engine.TimeRate = state.EngineTimeRate;", apply, StringComparison.Ordinal);
        int finiteCheck = source.IndexOf("float.IsFinite(state.EngineTimeRate)", apply, StringComparison.Ordinal);

        Assert.True(apply >= 0 && finiteCheck > apply && timeRateAssignment > finiteCheck);
        Assert.Contains("float.IsFinite(state.GlitchValue)", SourceSlice(source, apply, timeRateAssignment - apply));
        Assert.Contains("float.IsFinite(state.DistortAnxiety)", SourceSlice(source, apply, timeRateAssignment - apply));
        Assert.Contains("float.IsFinite(state.DistortGameRate)", SourceSlice(source, apply, timeRateAssignment - apply));
    }

    [Fact]
    public void DeferredEngineActionsIsolateFailuresAndRenderHookUsesTheLevelTargetAnchor() {
        string moduleSource = File.ReadAllText(GetModuleSourcePath());
        int drain = moduleSource.IndexOf("private static void RunAfterEngineUpdateActions", StringComparison.Ordinal);
        int presentationHook = moduleSource.IndexOf("private static void LevelOnRenderForStartPosPresentation", StringComparison.Ordinal);

        Assert.True(drain >= 0);
        Assert.Contains("catch (Exception exception)", SourceSlice(moduleSource, drain, presentationHook - drain));
        Assert.Contains("field.Name == nameof(GameplayBuffers.Level)", SourceTail(moduleSource, presentationHook));
    }

    [Fact]
    public void ArmedGameplayPresentationIsClearedWhenTheLevelOrModuleEnds() {
        string moduleSource = File.ReadAllText(GetModuleSourcePath());
        string reconstructionSource = File.ReadAllText(GetSourcePath("SaveLoad", "akron-reconstruction-graph.cs"));

        Assert.Contains("public static void ResetLevelPresentation()", reconstructionSource);
        Assert.Contains("AkronGameplayBufferState.ResetLevelPresentation();", moduleSource);
        Assert.Contains("On.Celeste.Level.End += LevelOnEnd", moduleSource);
        Assert.Contains("On.Celeste.Level.End -= LevelOnEnd", moduleSource);
    }

    [Fact]
    public void StartPosInputWaitIgnoresHeldControlsUntilANewControlIsPressed() {
        AkronStartPosInputWait wait = new AkronStartPosInputWait();
        AkronStartPosInputFlags held = AkronStartPosInputFlags.MoveLeft | AkronStartPosInputFlags.Jump;

        wait.Begin(held, waitingForWipe: false);

        Assert.True(wait.Active);
        Assert.False(wait.Advance(held));
        Assert.False(wait.Advance(AkronStartPosInputFlags.MoveLeft));
        Assert.True(wait.Advance(AkronStartPosInputFlags.MoveLeft | AkronStartPosInputFlags.Dash));
        Assert.False(wait.Active);
    }

    [Fact]
    public void StartPosInputWaitTreatsDirectionChangesAsFreshInput() {
        AkronStartPosInputWait wait = new AkronStartPosInputWait();
        wait.Begin(AkronStartPosInputFlags.MoveLeft, waitingForWipe: false);

        Assert.True(wait.Advance(AkronStartPosInputFlags.MoveRight));
        Assert.False(wait.Active);
    }

    [Fact]
    public void StartPosInputWaitDoesNotAcceptInputUntilTheRespawnWipeFinishes() {
        AkronStartPosInputWait wait = new AkronStartPosInputWait();
        AkronStartPosInputFlags duringWipe = AkronStartPosInputFlags.Dash | AkronStartPosInputFlags.Jump;

        wait.Begin(AkronStartPosInputFlags.Dash, waitingForWipe: true);

        Assert.True(wait.WaitingForWipe);
        Assert.False(wait.Advance(duringWipe));

        wait.CompleteWipe(duringWipe);

        Assert.False(wait.WaitingForWipe);
        Assert.False(wait.Advance(duringWipe));
        Assert.False(wait.Advance(AkronStartPosInputFlags.Dash));
        Assert.True(wait.Advance(AkronStartPosInputFlags.Dash | AkronStartPosInputFlags.Grab));
    }

    [Fact]
    public void ClearingStartPosInputWaitReturnsItToAnInactiveState() {
        AkronStartPosInputWait wait = new AkronStartPosInputWait();
        wait.Begin(AkronStartPosInputFlags.Jump, waitingForWipe: false);

        wait.Clear();

        Assert.False(wait.Active);
        Assert.False(wait.WaitingForWipe);
        Assert.False(wait.Advance(AkronStartPosInputFlags.Dash));
    }

    [Fact]
    public void PersistentRestoreUsesStableActionIdsAndProtectsIgnoredEntitiesAndStats() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int restore = source.IndexOf("private static AkronSaveLoadResult RestorePersistentRuntimeState", StringComparison.Ordinal);
        int nextMethod = source.IndexOf("private static bool TryLoadFreshRoom", restore, StringComparison.Ordinal);
        string restoreMethod = SourceSlice(source, restore, nextMethod - restore);

        Assert.DoesNotContain("NextRegisteredActionId", source);
        Assert.Contains("GetRegisteredActionId", source);
        Assert.Contains("registered action set differs", restoreMethod);
        Assert.Contains("AkronIgnoreSaveStateComponent.RemoveAll(level);", restoreMethod);
        Assert.Contains("AkronIgnoreSaveStateComponent.ReAddAll(level);", restoreMethod);
        Assert.Contains("TryGetAreaModeStats", source);
    }

    [Fact]
    public void PersistentRegistrationIdsDoNotUseBuildSpecificMetadata() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int identityStart = source.IndexOf("private static string GetRegisteredActionId", StringComparison.Ordinal);
        int identityEnd = source.IndexOf("private static void AddRegisteredAction", identityStart, StringComparison.Ordinal);
        string identityMethods = SourceSlice(source, identityStart, identityEnd - identityStart);

        Assert.DoesNotContain("ModuleVersionId", identityMethods);
        Assert.DoesNotContain("MetadataToken", identityMethods);
        Assert.Contains("type.Assembly.GetName().Name", identityMethods);
        Assert.Contains("method.GetParameters()", identityMethods);
    }

    [Fact]
    public void ColdPersistentRestoreHonorsTheNativeStateGuard() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int loadCall = source.IndexOf(
            "RestorePersistentRuntimeState(level, normalizedSlotName, allowDeadPlayer)",
            StringComparison.Ordinal);
        int restore = source.IndexOf(
            "private static AkronSaveLoadResult RestorePersistentRuntimeState(",
            StringComparison.Ordinal);
        int guard = source.IndexOf("CanAccessNativeState(level, out _, allowDeadPlayer)", restore, StringComparison.Ordinal);
        int readSnapshot = source.IndexOf("TryLoadSnapshot(slotName", restore, StringComparison.Ordinal);

        Assert.True(loadCall >= 0);
        Assert.True(restore >= 0);
        Assert.True(guard > restore);
        Assert.True(readSnapshot > guard);
    }

    [Fact]
    public void NamedCallbackOwnersKeepTheirIdsWhenRegistrationOrderChanges() {
        RegistrationProbe firstProbe = new RegistrationProbe();
        RegistrationProbe secondProbe = new RegistrationProbe();
        AkronRegisteredSaveLoadAction first = Assert.IsType<AkronRegisteredSaveLoadAction>(
            AkronSaveLoadService.RegisterNamedSaveLoadAction(
                "first-probe",
                firstProbe.Save,
                firstProbe.Load,
                firstProbe.Clear,
                null,
                null,
                null));
        AkronRegisteredSaveLoadAction second = Assert.IsType<AkronRegisteredSaveLoadAction>(
            AkronSaveLoadService.RegisterNamedSaveLoadAction(
                "second-probe",
                secondProbe.Save,
                secondProbe.Load,
                secondProbe.Clear,
                null,
                null,
                null));
        try {
            Assert.NotEqual(first.Id, second.Id);
        } finally {
            AkronSaveLoadService.Unregister(first);
            AkronSaveLoadService.Unregister(second);
        }

        RegistrationProbe restartedFirstProbe = new RegistrationProbe();
        RegistrationProbe restartedSecondProbe = new RegistrationProbe();
        AkronRegisteredSaveLoadAction restartedSecond = Assert.IsType<AkronRegisteredSaveLoadAction>(
            AkronSaveLoadService.RegisterNamedSaveLoadAction(
                "second-probe",
                restartedSecondProbe.Save,
                restartedSecondProbe.Load,
                restartedSecondProbe.Clear,
                null,
                null,
                null));
        AkronRegisteredSaveLoadAction restartedFirst = Assert.IsType<AkronRegisteredSaveLoadAction>(
            AkronSaveLoadService.RegisterNamedSaveLoadAction(
                "first-probe",
                restartedFirstProbe.Save,
                restartedFirstProbe.Load,
                restartedFirstProbe.Clear,
                null,
                null,
                null));
        try {
            Assert.Equal(first.Id, restartedFirst.Id);
            Assert.Equal(second.Id, restartedSecond.Id);
        } finally {
            AkronSaveLoadService.Unregister(restartedFirst);
            AkronSaveLoadService.Unregister(restartedSecond);
        }
    }

    [Fact]
    public void DuplicateUnnamedCallbackOwnersAreRejected() {
        RegistrationProbe firstProbe = new RegistrationProbe();
        RegistrationProbe secondProbe = new RegistrationProbe();
        AkronRegisteredSaveLoadAction first = Assert.IsType<AkronRegisteredSaveLoadAction>(
            AkronSaveLoadService.RegisterSaveLoadAction(
                firstProbe.Save,
                firstProbe.Load,
                firstProbe.Clear,
                null,
                null,
                null));
        try {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                AkronSaveLoadService.RegisterSaveLoadAction(
                    secondProbe.Save,
                    secondProbe.Load,
                    secondProbe.Clear,
                    null,
                    null,
                    null));

            Assert.Contains("RegisterNamedSaveLoadAction", exception.Message);
        } finally {
            AkronSaveLoadService.Unregister(first);
        }
    }

    [Fact]
    public void DuplicateStaticRegistrationsReceiveStableDistinctIds() {
        AkronRegisteredSaveLoadAction first = Assert.IsType<AkronRegisteredSaveLoadAction>(
            AkronSaveLoadService.RegisterStaticTypes(typeof(RegistrationProbe), nameof(RegistrationProbe.SharedValue)));
        AkronRegisteredSaveLoadAction second = Assert.IsType<AkronRegisteredSaveLoadAction>(
            AkronSaveLoadService.RegisterStaticTypes(typeof(RegistrationProbe), nameof(RegistrationProbe.SharedValue)));
        try {
            Assert.NotEqual(first.Id, second.Id);
            Assert.EndsWith("|registration=0", first.Id, StringComparison.Ordinal);
            Assert.EndsWith("|registration=1", second.Id, StringComparison.Ordinal);
        } finally {
            AkronSaveLoadService.Unregister(first);
            AkronSaveLoadService.Unregister(second);
        }

        AkronRegisteredSaveLoadAction restartedFirst = Assert.IsType<AkronRegisteredSaveLoadAction>(
            AkronSaveLoadService.RegisterStaticTypes(typeof(RegistrationProbe), nameof(RegistrationProbe.SharedValue)));
        AkronRegisteredSaveLoadAction restartedSecond = Assert.IsType<AkronRegisteredSaveLoadAction>(
            AkronSaveLoadService.RegisterStaticTypes(typeof(RegistrationProbe), nameof(RegistrationProbe.SharedValue)));
        try {
            Assert.Equal(first.Id, restartedFirst.Id);
            Assert.Equal(second.Id, restartedSecond.Id);
        } finally {
            AkronSaveLoadService.Unregister(restartedFirst);
            AkronSaveLoadService.Unregister(restartedSecond);
        }
    }

    [Fact]
    public void CommittedPackCleanupCannotChangeTheImportResult() {
        string source = File.ReadAllText(GetSourcePath("Setups", "akron-setup-packs.cs"));
        int dispose = source.IndexOf("public void Dispose()", StringComparison.Ordinal);
        int end = source.IndexOf("\n        }\n    }\n}", dispose, StringComparison.Ordinal);
        string disposeMethod = SourceSlice(source, dispose, end - dispose);

        Assert.Contains("if (installed && !committed)", disposeMethod);
        Assert.Contains("catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)", disposeMethod);
        Assert.Contains("Could not delete staged StartPos import", disposeMethod);
    }

    [Fact]
    public void SetupPackSnapshotsCommitOnlyAfterMetadataPersistenceSucceeds() {
        string source = File.ReadAllText(GetSourcePath("Setups", "akron-setup-packs.cs"));
        int wholeReplace = source.IndexOf("persistMetadata: false", StringComparison.Ordinal);
        int wholePersist = source.IndexOf("!persistStartPosMetadata()", wholeReplace, StringComparison.Ordinal);
        int wholeCommit = source.IndexOf("prepared?.Commit();", wholePersist, StringComparison.Ordinal);
        int scopedReplace = source.IndexOf("persistMetadata: false", wholeReplace + 1, StringComparison.Ordinal);
        int scopedPersist = source.IndexOf("!persistStartPosMetadata()", scopedReplace, StringComparison.Ordinal);
        int scopedCommit = source.IndexOf("prepared?.Commit();", scopedPersist, StringComparison.Ordinal);

        Assert.True(wholeReplace >= 0 && wholePersist > wholeReplace && wholeCommit > wholePersist);
        Assert.True(scopedReplace > wholeReplace && scopedPersist > scopedReplace && scopedCommit > scopedPersist);
    }

    [Fact]
    public void StartPosCaptureCommitsItsSnapshotOnlyAfterMetadataPersistenceSucceeds() {
        string source = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-actions.cs"));
        int capture = source.IndexOf("internal static void CompletePersistentStartPosCapture", StringComparison.Ordinal);
        int prepare = source.IndexOf("PrepareSnapshotInstall", capture, StringComparison.Ordinal);
        int install = source.IndexOf("installedSnapshot.Install", prepare, StringComparison.Ordinal);
        int persist = source.IndexOf("PersistStartPos(slot, startPos, fileSlot, out previousMetadataLost)", install, StringComparison.Ordinal);
        int commit = source.IndexOf("installedSnapshot.Commit()", persist, StringComparison.Ordinal);

        Assert.True(capture >= 0 && prepare > capture);
        Assert.True(install > prepare && persist > install && commit > persist);
        Assert.Contains("if (!PersistStartPos(slot, startPos, fileSlot, out previousMetadataLost))", SourceSlice(source, persist - 16, 160));
    }

    [Fact]
    public void SetupPackImportRollsBackMemoryAlongWithSnapshotFiles() {
        string source = File.ReadAllText(GetSourcePath("Setups", "akron-setup-packs.cs"));
        int apply = source.IndexOf("public static void Apply(", StringComparison.Ordinal);
        int transaction = source.IndexOf("SetupImportStateTransaction", apply, StringComparison.Ordinal);
        int wholePersist = source.IndexOf("!persistStartPosMetadata()", transaction, StringComparison.Ordinal);
        int wholeCommit = source.IndexOf("stateTransaction?.Commit()", wholePersist, StringComparison.Ordinal);
        int scopedPersist = source.IndexOf("!persistStartPosMetadata()", wholePersist + 1, StringComparison.Ordinal);
        int scopedCommit = source.IndexOf("stateTransaction?.Commit()", scopedPersist, StringComparison.Ordinal);
        int rollback = source.IndexOf("private sealed class SetupImportStateTransaction", scopedCommit, StringComparison.Ordinal);

        Assert.True(transaction > apply && wholePersist > transaction && wholeCommit > wholePersist);
        Assert.True(scopedPersist > wholePersist && scopedCommit > scopedPersist && rollback > scopedCommit);
        Assert.Contains("prepared?.Install(previousSlots: existingTargetSlots)", source);
        Assert.Contains("prepared?.Install(importedSlotMap, existingTargetSlots)", source);
        string rollbackType = SourceTail(source, rollback);
        Assert.Contains("settings.ApplySetupPackState(previousSettings)", rollbackType);
        Assert.Contains("session.StartPositions = previousStartPositions", rollbackType);
        Assert.Contains("saveData.StartPositionsByMap = previousMaps", rollbackType);
        Assert.Contains("AkronActions.BeginStartPosReplacement(targetMapSid)", rollbackType);
        Assert.Contains("startPosReplacement?.Commit()", rollbackType);
        Assert.Contains("startPosReplacement?.Dispose()", rollbackType);
    }

    [Fact]
    public void SetupPackReplacementDefersDestructiveStartPosCleanupUntilCommit() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int replace = source.IndexOf("internal static void ReplacePersistedStartPositionsForMap", StringComparison.Ordinal);
        int replaceEnd = source.IndexOf("private static void EnsureStartPositionsLoaded", replace, StringComparison.Ordinal);
        string replacePath = SourceSlice(source, replace, replaceEnd - replace);

        Assert.Contains("replacementTransaction.DeferCleanup", replacePath);
        Assert.Contains("ClearStartPosRuntimeState", replacePath);
        Assert.Contains("DiscardStartPosRuntimeStateMemory", replacePath);
    }

    [Fact]
    public void StartPosReplacementTransactionRestoresPendingStateOnRollback() {
        FieldInfo pendingField = typeof(AkronActions).GetField(
            "PendingStartPositionsByFileAndMap",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(AkronActions).FullName, "PendingStartPositionsByFileAndMap");
        Dictionary<string, Dictionary<int, AkronStartPos>> pendingByMap =
            Assert.IsType<Dictionary<string, Dictionary<int, AkronStartPos>>>(pendingField.GetValue(null));
        int fileSlot = SaveData.Instance?.FileSlot ?? -1;
        string pendingKey = fileSlot.ToString(CultureInfo.InvariantCulture) + "|" +
                            AkronActions.GetCurrentStartPosProfileId() + "|Map/A";
        Dictionary<int, AkronStartPos> originalPending = new Dictionary<int, AkronStartPos> {
            [1] = new AkronStartPos { AreaSid = "Map/A", Room = "room-a" }
        };
        bool cleanupRan = false;

        pendingByMap.Clear();
        pendingByMap[pendingKey] = originalPending;
        try {
            using (AkronActions.StartPosReplacementTransaction transaction =
                   AkronActions.BeginStartPosReplacement("Map/A")) {
                transaction.DeferCleanup(1, () => cleanupRan = true);
                Assert.False(pendingByMap.ContainsKey(pendingKey));
            }

            Assert.Same(originalPending, pendingByMap[pendingKey]);
            Assert.False(cleanupRan);
        } finally {
            pendingByMap.Clear();
        }
    }

    [Fact]
    public void RegisteredClearStateFailureDoesNotEscapePostCommitCleanup() {
        int completedCallbacks = 0;
        object throwing = AkronSaveLoadService.RegisterSaveLoadAction(
            null,
            null,
            () => throw new InvalidOperationException("expected test failure"),
            null,
            null,
            null);
        object following = AkronSaveLoadService.RegisterSaveLoadAction(
            null,
            null,
            () => completedCallbacks++,
            null,
            null,
            null);
        MethodInfo runClearStateActions = typeof(AkronSaveLoadService).GetMethod(
            "RunClearStateActions",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(AkronSaveLoadService).FullName, "RunClearStateActions");

        try {
            Exception exception = Record.Exception(() => runClearStateActions.Invoke(null, null));

            Assert.Null(exception);
            Assert.Equal(1, completedCallbacks);
        } finally {
            AkronSaveLoadService.Unregister(throwing);
            AkronSaveLoadService.Unregister(following);
        }
    }

    [Fact]
    public void CommittingReplacementReleasesPendingOnlyRuntimeSlot() {
        const int fileSlot = -1;
        const int slot = 19;
        string areaSid = "Tests/PendingOnly-" + Guid.NewGuid().ToString("N");
        string pendingKey = fileSlot.ToString() + "|" + AkronActions.GetCurrentStartPosProfileId() + "|" + areaSid;
        string stateSlotName = AkronActions.GetStartPosStateSlotName(areaSid, slot, fileSlot);
        FieldInfo pendingField = typeof(AkronActions).GetField(
            "PendingStartPositionsByFileAndMap",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(AkronActions).FullName, "PendingStartPositionsByFileAndMap");
        Dictionary<string, Dictionary<int, AkronStartPos>> pendingByMap =
            Assert.IsType<Dictionary<string, Dictionary<int, AkronStartPos>>>(pendingField.GetValue(null));
        FieldInfo runtimeSlotsField = typeof(AkronSaveLoadService).GetField(
            "RuntimeSlots",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(AkronSaveLoadService).FullName, "RuntimeSlots");
        Dictionary<string, AkronSaveLoadSlotOwner> runtimeSlots =
            Assert.IsType<Dictionary<string, AkronSaveLoadSlotOwner>>(runtimeSlotsField.GetValue(null));
        int releases = 0;

        AkronVirtualAssetReloadTracker.Clear();
        int marker = AkronVirtualAssetReloadTracker.Mark();
        AkronVirtualAssetReloadTracker.Add(
            (VirtualRenderTarget) RuntimeHelpers.GetUninitializedObject(typeof(VirtualRenderTarget)));
        AkronSaveLoadSlot runtimeSlot = new AkronSaveLoadSlot(stateSlotName, "room", areaSid, saveTimeAndDeaths: false) {
            TrackedVirtualAssetRegistrations = AkronVirtualAssetReloadTracker.GetRegistrationsSince(marker)
        };
        AkronSaveLoadSlotOwner owner = new AkronSaveLoadSlotOwner(runtimeSlot, releasedSlot => {
            releases++;
            AkronVirtualAssetReloadTracker.Remove(releasedSlot.TrackedVirtualAssetRegistrations);
        });
        pendingByMap[pendingKey] = new Dictionary<int, AkronStartPos> {
            [slot] = new AkronStartPos {
                AreaSid = areaSid,
                Room = "room",
                ProfileId = AkronActions.GetCurrentStartPosProfileId()
            }
        };
        runtimeSlots[stateSlotName] = owner;

        try {
            using AkronActions.StartPosReplacementTransaction transaction =
                new AkronActions.StartPosReplacementTransaction(fileSlot, areaSid);
            transaction.Commit();

            Assert.False(runtimeSlots.ContainsKey(stateSlotName));
            Assert.False(pendingByMap.ContainsKey(pendingKey));
            Assert.Equal(1, releases);
            Assert.Equal(0, AkronVirtualAssetReloadTracker.Count);
        } finally {
            pendingByMap.Remove(pendingKey);
            if (runtimeSlots.Remove(stateSlotName, out AkronSaveLoadSlotOwner? remainingOwner) && remainingOwner != null) {
                remainingOwner.ReleaseOwnership();
            }
            AkronVirtualAssetReloadTracker.Clear();
        }
    }

    [Fact]
    public void SetupCaptureRejectsPendingStartPosSnapshot() {
        int fileSlot = SaveData.Instance?.FileSlot ?? -1;
        string areaSid = "Tests/PendingExport-" + Guid.NewGuid().ToString("N");
        string pendingKey = fileSlot.ToString() + "|" + AkronActions.GetCurrentStartPosProfileId() + "|" + areaSid;
        AkronStartPos pendingStartPos = new AkronStartPos {
            AreaSid = areaSid,
            Room = "room",
            ProfileId = AkronActions.GetCurrentStartPosProfileId(),
            StateSlotName = AkronActions.GetStartPosStateSlotName(areaSid, 1, fileSlot)
        };
        AkronModuleSession session = new AkronModuleSession {
            LoadedStartPositionsAreaSid = areaSid,
            StartPositions = new Dictionary<int, AkronStartPos> { [1] = pendingStartPos }
        };
        FieldInfo pendingField = typeof(AkronActions).GetField(
            "PendingStartPositionsByFileAndMap",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(AkronActions).FullName, "PendingStartPositionsByFileAndMap");
        Dictionary<string, Dictionary<int, AkronStartPos>> pendingByMap =
            Assert.IsType<Dictionary<string, Dictionary<int, AkronStartPos>>>(pendingField.GetValue(null));
        pendingByMap[pendingKey] = new Dictionary<int, AkronStartPos> { [1] = pendingStartPos };

        try {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                AkronSetupPacks.CaptureStartPositions(session, areaSid, out _));

            Assert.Contains("still saving", exception.Message, StringComparison.OrdinalIgnoreCase);
        } finally {
            pendingByMap.Remove(pendingKey);
        }
    }

    [Fact]
    public void RegisteredActionStateUsesDormantClonesOwnedByItsSlot() {
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());
        string deepCloneSource = File.ReadAllText(GetSourcePath("Core", "AkronDeepClone.cs"));

        Assert.Contains("AkronDeepClone.CloneDormant", saveLoadSource);
        Assert.Contains("saveSlot.SavedLevelEventInstances.AddRange", saveLoadSource);
        Assert.Contains("RunWithDormantEventClones", deepCloneSource);
        Assert.DoesNotContain(
            "ActionState[action.Id] = (Dictionary<Type, Dictionary<string, object>>) DeepClone(savedValues)",
            saveLoadSource);
    }

    [Fact]
    public void CompletionDrainIsolatesApplyFailures() {
        string source = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        int update = source.IndexOf("public static void Update()", StringComparison.Ordinal);
        int updateEnd = source.IndexOf("public static IDisposable SuppressBaselineCapture()", update, StringComparison.Ordinal);
        string updatePath = SourceSlice(source, update, updateEnd - update);
        int apply = updatePath.IndexOf("CompletePersistentStartPosCapture", StringComparison.Ordinal);
        int catchFailure = updatePath.IndexOf("catch (Exception exception)", apply, StringComparison.Ordinal);
        int dispose = updatePath.IndexOf("completion.Job.Dispose()", apply, StringComparison.Ordinal);

        Assert.True(apply >= 0 && catchFailure > apply && dispose > catchFailure);
        Assert.Contains("Could not apply a StartPos restart copy", updatePath);
    }

    [Fact]
    public void LateBaselineCaptureDrainsJobsThroughInstalledBaseline() {
        string source = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        int capture = source.IndexOf("private static void CaptureFreshBaseline(", StringComparison.Ordinal);
        int captureEnd = source.IndexOf("private static bool IsPendingBaselineGenerationLocked", capture, StringComparison.Ordinal);
        string capturePath = SourceSlice(source, capture, captureEnd - capture);
        int timer = capturePath.IndexOf("Stopwatch timer", StringComparison.Ordinal);
        int installed = capturePath.IndexOf(
            "out AkronSaveLoadSlotLease installedBaseline",
            timer,
            StringComparison.Ordinal);
        int queue = installed < 0
            ? -1
            : capturePath.IndexOf(
                "QueueWaitingJobsForBaselineLocked(expectedKey, installedBaseline)",
                installed,
                StringComparison.Ordinal);

        Assert.True(timer >= 0 && installed > timer && queue > installed);
    }

    [Fact]
    public void TrackerRangesStayEmptyWhenAResetShrinksPastTheMarker() {
        AkronVirtualAssetReloadTracker.Clear();
        AkronVirtualAssetReloadTracker.Add(
            (VirtualRenderTarget) RuntimeHelpers.GetUninitializedObject(typeof(VirtualRenderTarget)));
        int marker = AkronVirtualAssetReloadTracker.Mark();
        AkronVirtualAssetReloadTracker.Clear();

        try {
            Exception discardException = Record.Exception(() => AkronVirtualAssetReloadTracker.DiscardSince(marker));

            Assert.Null(discardException);
            Assert.Empty(AkronVirtualAssetReloadTracker.GetRenderTargetsSince(marker));
            Assert.Empty(AkronVirtualAssetReloadTracker.GetRegistrationsSince(marker));
        } finally {
            AkronVirtualAssetReloadTracker.Clear();
        }
    }

    [Fact]
    public void PersistenceCaptureAndGameThreadRestoreUseSeparateGraphs() {
        string source = File.ReadAllText(GetSourcePath("SaveLoad", "akron-reconstruction-graph.cs"));
        int facade = source.IndexOf("internal static class AkronStartPosReconstruction", StringComparison.Ordinal);
        string facadePath = SourceTail(source, facade);

        Assert.Contains("AkronReconstructionGraph CaptureGraph", facadePath);
        Assert.Contains("AkronReconstructionGraph RestoreGraph", facadePath);
        Assert.Contains("return CaptureGraph.Capture(savedState, freshState)", facadePath);
        Assert.Contains("return RestoreGraph.Restore(document, freshState)", facadePath);
        Assert.Contains("RestoreGraph.ReleaseOwnedPersistentResources()", facadePath);
    }

    [Fact]
    public void FailedPersistentRestoreReloadsThePreLoadRuntimeState() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int restore = source.IndexOf("private static AkronSaveLoadResult RestorePersistentRuntimeState(", StringComparison.Ordinal);
        int captureRollback = source.IndexOf("rollbackSlot = CaptureRuntimeState(", restore, StringComparison.Ordinal);
        int restoreCore = source.IndexOf("RestorePersistentRuntimeStateCore(level, document, out freshBaseline)", captureRollback, StringComparison.Ordinal);
        int restoreRollback = source.IndexOf("RestoreRuntimeState(level, rollbackSlot", restoreCore, StringComparison.Ordinal);
        int discardRollback = source.IndexOf("ReleaseRuntimeSlotResources(rollbackSlot)", restoreRollback, StringComparison.Ordinal);

        Assert.True(restore >= 0 && captureRollback > restore);
        Assert.True(restoreCore > captureRollback && restoreRollback > restoreCore && discardRollback > restoreRollback);
        Assert.Contains("capturePersistentResources: false", SourceSlice(source, captureRollback, 320));
    }

    [Fact]
    public void PixelTaggedLoadProbeCompletesAfterTheRenderCapture() {
        string qaSource = File.ReadAllText(GetSourcePath("Commands", "akron-qa-commands.cs"));
        string captureSource = File.ReadAllText(GetSourcePath("Tools", "akron-capture.cs"));

        int request = qaSource.IndexOf("RequestGameplayBufferQaCapture(", StringComparison.Ordinal);
        int pixelTag = qaSource.IndexOf("pixelTag,", request, StringComparison.Ordinal);
        int completion = qaSource.IndexOf("AkronAutomationService.CompleteDeferredRun", pixelTag, StringComparison.Ordinal);
        Assert.True(request >= 0 && pixelTag > request && completion > pixelTag);
        Assert.Contains("if (!waitForPixelCapture)", qaSource);
        Assert.Contains("pendingGameplayBufferQaCompletion", captureSource);
        Assert.Contains("completion?.Invoke()", captureSource);
    }

    [Fact]
    public void InMemoryRestoreRefreshesTheTrackerBeforeHelperCallbacks() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int restoreNative = source.IndexOf("private static bool RestoreNativeSlot", StringComparison.Ordinal);
        int trackerRefresh = source.IndexOf("Tracker.Refresh(level, force: true);", restoreNative, StringComparison.Ordinal);
        int methodEnd = source.IndexOf("private static void CaptureCuratedSessionState", restoreNative, StringComparison.Ordinal);

        Assert.True(trackerRefresh > restoreNative);
        Assert.True(trackerRefresh < methodEnd);
    }

    [Fact]
    public void SuccessfulPreUpdateStartPosStateChangeRendersBeforeSimulationAdvances() {
        string moduleSource = File.ReadAllText(GetModuleSourcePath());
        string actionsSource = File.ReadAllText(GetActionsSourcePath());
        int levelUpdate = moduleSource.IndexOf("private static void LevelOnUpdate", StringComparison.Ordinal);
        Assert.True(levelUpdate >= 0);

        int generationCapture = moduleSource.IndexOf("ulong startPosFrameGeneration = AkronActions.StartPosFrameGeneration;", levelUpdate, StringComparison.Ordinal);
        Assert.True(generationCapture > levelUpdate);

        int pendingRenderCheck = moduleSource.IndexOf("if (startPosFrameGeneration != renderedStartPosFrameGeneration)", levelUpdate, StringComparison.Ordinal);
        Assert.True(pendingRenderCheck > generationCapture);
        int heldClock = moduleSource.IndexOf("AkronRuntimeOptions.HoldSceneClockForSkippedLevelUpdate(self);", pendingRenderCheck, StringComparison.Ordinal);
        Assert.True(heldClock > pendingRenderCheck);

        int automation = moduleSource.IndexOf("AkronAutomationService.ProcessPendingCommands(self);", generationCapture, StringComparison.Ordinal);
        Assert.True(automation > generationCapture);

        int automationRestoreCheck = moduleSource.IndexOf("if (AkronActions.StartPosFrameGeneration != startPosFrameGeneration)", automation, StringComparison.Ordinal);
        Assert.True(automationRestoreCheck > automation);

        int hotkeys = moduleSource.IndexOf("HandleHotkeys(self);", automationRestoreCheck, StringComparison.Ordinal);
        Assert.True(hotkeys > automationRestoreCheck);

        int hotkeyRestoreCheck = moduleSource.IndexOf("if (AkronActions.StartPosFrameGeneration != startPosFrameGeneration)", hotkeys, StringComparison.Ordinal);
        Assert.True(hotkeyRestoreCheck > hotkeys);

        int gameplayUpdate = moduleSource.IndexOf("orig(self);", hotkeyRestoreCheck, StringComparison.Ordinal);
        Assert.True(gameplayUpdate > hotkeyRestoreCheck);

        int renderRelink = actionsSource.IndexOf("RelinkRuntimeRenderState(currentLevel);", StringComparison.Ordinal);
        Assert.True(renderRelink >= 0);

        int restoreNotification = actionsSource.IndexOf("StartPosFrameGeneration++;", renderRelink, StringComparison.Ordinal);
        Assert.True(restoreNotification > renderRelink);

        int successfulRestore = actionsSource.IndexOf("return true;", restoreNotification, StringComparison.Ordinal);
        Assert.True(successfulRestore > restoreNotification);

        int persistedStartPos = actionsSource.IndexOf("PersistStartPos(slot, startPos, fileSlot, out previousMetadataLost)", StringComparison.Ordinal);
        Assert.True(persistedStartPos >= 0);
        int captureNotification = actionsSource.IndexOf("StartPosFrameGeneration++;", persistedStartPos, StringComparison.Ordinal);
        Assert.True(captureNotification > persistedStartPos);

        int renderCore = moduleSource.IndexOf("private static void EngineOnRenderCore", StringComparison.Ordinal);
        int roomBufferCapture = moduleSource.IndexOf("AkronCapture.CapturePendingGameplayBufferQaFrame();", renderCore, StringComparison.Ordinal);
        int renderAcknowledgement = moduleSource.IndexOf("renderedStartPosFrameGeneration = AkronActions.StartPosFrameGeneration;", roomBufferCapture, StringComparison.Ordinal);
        Assert.True(renderCore >= 0);
        Assert.True(roomBufferCapture > renderCore);
        Assert.True(renderAcknowledgement > roomBufferCapture);
    }


    [Fact]
    public void ExactRoomClocksRestoreWithoutRewindingCumulativeStats() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());

        Assert.Contains("level.TimeActive = saveSlot.LevelTimeActive;", source);
        Assert.Contains("level.RawTimeActive = saveSlot.LevelRawTimeActive;", source);
        Assert.DoesNotContain("level.TimeActive = Math.Max(currentLevelTimeActive, saveSlot.LevelTimeActive);", source);
        Assert.DoesNotContain("level.RawTimeActive = Math.Max(currentLevelRawTimeActive, saveSlot.LevelRawTimeActive);", source);
        int restoreStart = source.IndexOf("public void RestoreWithoutRewinding(Level level)", StringComparison.Ordinal);
        int restoreEnd = source.IndexOf("private static AreaModeStats TryGetAreaModeStats", restoreStart, StringComparison.Ordinal);
        string restoreMethod = SourceSlice(source, restoreStart, restoreEnd - restoreStart);

        Assert.Contains("level.Session.Time = sessionTime;", restoreMethod);
        Assert.Contains("level.Session.Deaths = sessionDeaths;", restoreMethod);
        Assert.Contains("level.Session.DeathsInCurrentLevel = roomDeaths;", restoreMethod);
        Assert.Contains("SaveData.Instance.Time = saveDataTime;", restoreMethod);
        Assert.Contains("SaveData.Instance.TotalDeaths = totalDeaths;", restoreMethod);
        Assert.Contains("mode.TimePlayed = areaTime;", restoreMethod);
        Assert.Contains("mode.Deaths = areaDeaths;", restoreMethod);
        Assert.DoesNotContain("Math.Max", restoreMethod);
    }

    [Fact]
    public void ReconstructionParentFieldValidationUsesAConstantTimeIndex() {
        string source = File.ReadAllText(GetSourcePath("SaveLoad", "akron-reconstruction-graph.cs"));
        int validationStart = source.IndexOf("private static void ValidateNodeParentEdges", StringComparison.Ordinal);
        int validationEnd = source.IndexOf("private static bool TryGetFlatArrayIndex", validationStart, StringComparison.Ordinal);
        string validationMethod = SourceSlice(source, validationStart, validationEnd - validationStart);

        Assert.Contains("parentFieldValues", validationMethod);
        Assert.Contains("TryAdd", validationMethod);
        Assert.Contains("TryGetValue", validationMethod);
        Assert.DoesNotContain("SingleOrDefault", validationMethod);
    }

    [Fact]
    public void PersistentGraphReleasesFreshFmodEventsDisplacedByRestoreAndReapply() {
        string source = File.ReadAllText(GetSourcePath("SaveLoad", "akron-reconstruction-graph.cs"));
        int restoreStart = source.IndexOf("public AkronReconstructionRestore Restore(", StringComparison.Ordinal);
        int restoreEnd = source.IndexOf("public void ReleaseOwnedPersistentResources", restoreStart, StringComparison.Ordinal);
        int reapplyStart = source.IndexOf("public AkronReconstructionVerification Reapply(", restoreEnd, StringComparison.Ordinal);
        int reapplyEnd = source.IndexOf("public AkronReconstructionVerification Verify(", reapplyStart, StringComparison.Ordinal);

        string restoreMethod = SourceSlice(source, restoreStart, restoreEnd - restoreStart);
        string reapplyMethod = SourceSlice(source, reapplyStart, reapplyEnd - reapplyStart);
        Assert.Contains("context.ReleaseDisplacedEventInstances();", restoreMethod);
        Assert.Contains("context.ReleaseDisplacedEventInstances();", reapplyMethod);
    }

    [Fact]
    public void PersistentRestoreReleasesTemporaryRegisteredActionEvents() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int restoreStart = source.IndexOf("private static AkronSaveLoadResult RestorePersistentRuntimeStateCore", StringComparison.Ordinal);
        int restoreEnd = source.IndexOf("private static bool ApplyPersistentRuntimeState", restoreStart, StringComparison.Ordinal);
        string restoreMethod = SourceSlice(source, restoreStart, restoreEnd - restoreStart);

        Assert.Contains("finally", restoreMethod);
        Assert.Contains("AkronStartPosReconstruction.ReleaseEventInstances(actionRestore);", restoreMethod);
    }

    [Fact]
    public void StartPosClonesVisualRuntimeEntitiesInsteadOfRebuildingThem() {
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());
        string nativeSupportSource = File.ReadAllText(GetSourcePath("SaveLoad", "akron-native-savestate-support.cs"));
        int captureStart = saveLoadSource.IndexOf("public static AkronSaveLoadSlot CaptureRuntimeState", StringComparison.Ordinal);
        int captureEnd = saveLoadSource.IndexOf("public static AkronSaveLoadResult SaveRuntimeState", captureStart, StringComparison.Ordinal);
        int restoreStart = saveLoadSource.IndexOf("private static bool RestoreNativeSlot", StringComparison.Ordinal);
        int restoreEnd = saveLoadSource.IndexOf("private static void CaptureCuratedSessionState", restoreStart, StringComparison.Ordinal);

        Assert.True(captureStart >= 0 && captureEnd > captureStart);
        Assert.True(restoreStart >= 0 && restoreEnd > restoreStart);
        Assert.DoesNotContain("IgnoreVisualRuntimeEntities(level);", SourceSlice(saveLoadSource, captureStart, captureEnd - captureStart));
        Assert.DoesNotContain("RemoveClonedVisualRuntimeEntities(level);", SourceSlice(saveLoadSource, restoreStart, restoreEnd - restoreStart));
        Assert.DoesNotContain("type == typeof(DustEdges)", nativeSupportSource);
    }

    [Fact]
    public void StartPosKeepsCumulativeStatsWhenStatsRestoreIsOff() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());

        Assert.Contains("level.Session.Time = Math.Max(currentSessionTime, level.Session.Time);", source);
        Assert.Contains("level.Session.Deaths = Math.Max(currentDeaths, level.Session.Deaths);", source);
        Assert.Contains("level.Session.DeathsInCurrentLevel = Math.Max(currentDeathsInRoom, level.Session.DeathsInCurrentLevel);", source);
        Assert.Contains("savedSaveData.Time = Math.Max(currentSaveDataTime, savedSaveData.Time);", source);
        Assert.Contains("savedSaveData.TotalDeaths = Math.Max(currentTotalDeaths, savedSaveData.TotalDeaths);", source);
        Assert.Contains("Math.Max(currentAreaTimePlayed", source);
        Assert.Contains("Math.Max(currentAreaDeaths", source);
        Assert.Contains("level.TimeActive = saveSlot.LevelTimeActive;", source);
        Assert.Contains("level.RawTimeActive = saveSlot.LevelRawTimeActive;", source);
    }

    [Fact]
    public void StartPosBerryRestoreRewindsOnlyTheActiveMapProgress() {
        EntityID savedBerry = new EntityID("a-00", 1);
        EntityID collectedAfterSet = new EntityID("a-01", 2);
        IEqualityComparer<EntityID> entityIdComparer = EqualityComparer<EntityID>.Create(
            (left, right) => left.Level == right.Level && left.ID == right.ID,
            id => HashCode.Combine(id.Level, id.ID));
        AreaModeStats savedArea = new AreaModeStats {
            TotalStrawberries = 1,
            Strawberries = new HashSet<EntityID>(entityIdComparer) { savedBerry }
        };
        AkronBerryProgressSnapshot snapshot = AkronBerryProgressSnapshot.Capture(savedArea);
        AreaModeStats currentArea = new AreaModeStats {
            TotalStrawberries = 2,
            Strawberries = new HashSet<EntityID>(entityIdComparer) { savedBerry, collectedAfterSet }
        };
        HashSet<EntityID> currentBerrySet = currentArea.Strawberries;

        Assert.True(snapshot.TryRestore(currentArea, 7, out int restoredTotal, out string error), error);
        Assert.Equal(6, restoredTotal);
        Assert.Equal(1, currentArea.TotalStrawberries);
        Assert.Same(currentBerrySet, currentArea.Strawberries);
        Assert.True(currentArea.Strawberries.SetEquals(new[] { savedBerry }));
    }

    [Fact]
    public void StartPosBerryRestoreKeepsGoldenBerriesOutOfRegularTotals() {
        EntityID goldenBerry = new EntityID("a-00", 1);
        EntityID regularBerry = new EntityID("a-01", 2);
        IEqualityComparer<EntityID> entityIdComparer = EqualityComparer<EntityID>.Create(
            (left, right) => left.Level == right.Level && left.ID == right.ID,
            id => HashCode.Combine(id.Level, id.ID));
        AreaModeStats savedArea = new AreaModeStats {
            TotalStrawberries = 0,
            Strawberries = new HashSet<EntityID>(entityIdComparer) { goldenBerry }
        };
        AkronBerryProgressSnapshot snapshot = AkronBerryProgressSnapshot.Capture(savedArea);
        AreaModeStats currentArea = new AreaModeStats {
            TotalStrawberries = 1,
            Strawberries = new HashSet<EntityID>(entityIdComparer) { goldenBerry, regularBerry }
        };

        Assert.True(snapshot.TryRestore(currentArea, 6, out int restoredTotal, out string error), error);
        Assert.Equal(5, restoredTotal);
        Assert.Equal(0, currentArea.TotalStrawberries);
        Assert.True(currentArea.Strawberries.SetEquals(new[] { goldenBerry }));
    }

    [Fact]
    public void WarmAndColdStartPosPathsRestoreBerryProgressAfterFallibleWork() {
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());
        string modelsSource = File.ReadAllText(GetSourcePath("SaveLoad", "akron-save-load-models.cs"));
        int warmRestoreStart = saveLoadSource.IndexOf(
            "public static AkronSaveLoadResult RestoreRuntimeState",
            StringComparison.Ordinal);
        int warmRestoreEnd = saveLoadSource.IndexOf(
            "public static AkronSaveLoadResult LoadRuntimeState",
            warmRestoreStart,
            StringComparison.Ordinal);
        int coldRestoreStart = saveLoadSource.IndexOf(
            "private static AkronSaveLoadResult RestorePersistentRuntimeStateAfterActionState",
            StringComparison.Ordinal);
        int coldRestoreEnd = saveLoadSource.IndexOf(
            "private static bool ApplyPersistentRuntimeState",
            coldRestoreStart,
            StringComparison.Ordinal);
        string warmRestore = SourceSlice(saveLoadSource, warmRestoreStart, warmRestoreEnd - warmRestoreStart);
        string coldRestore = SourceSlice(saveLoadSource, coldRestoreStart, coldRestoreEnd - coldRestoreStart);

        Assert.Contains("saveSlot.BerryProgress = AkronBerryProgressSnapshot.Capture(level);", saveLoadSource);
        Assert.Contains("capture.Document.BerryProgress = saveSlot.BerryProgress;", saveLoadSource);
        Assert.True(
            warmRestore.IndexOf("AkronGameplayBufferState.Restore", StringComparison.Ordinal) <
            warmRestore.IndexOf("saveSlot.BerryProgress.TryRestore", StringComparison.Ordinal));
        Assert.True(
            coldRestore.IndexOf("AkronGameplayBufferState.Restore", StringComparison.Ordinal) <
            coldRestore.IndexOf("document.BerryProgress.TryRestore", StringComparison.Ordinal));
        Assert.DoesNotContain("public AkronBerryProgressSnapshot BerryProgress", modelsSource);
    }

    [Fact]
    public void SetupPackImportBindsStartPosBerryProgressToTheRecipientSave() {
        string source = File.ReadAllText(GetSourcePath("Setups", "akron-setup-packs.cs"));
        int prepareStart = source.IndexOf("private static PreparedStartPosImport PrepareStartPosImport", StringComparison.Ordinal);
        int prepareEnd = source.IndexOf("private static string GetSnapshotEntryName", prepareStart, StringComparison.Ordinal);
        string prepareImport = SourceSlice(source, prepareStart, prepareEnd - prepareStart);

        int level = prepareImport.IndexOf("Level recipientLevel = TryGetCurrentLevel();", StringComparison.Ordinal);
        int targetCheck = prepareImport.IndexOf(
            "string.Equals(recipientLevel?.Session?.Area.GetSID(), targetMapSid, StringComparison.Ordinal)",
            StringComparison.Ordinal);
        int capture = prepareImport.IndexOf("AkronBerryProgressSnapshot.Capture(recipientLevel)", StringComparison.Ordinal);
        int loop = prepareImport.IndexOf("foreach (KeyValuePair<int, AkronStartPosPackEntry>", StringComparison.Ordinal);
        Assert.True(level >= 0 && targetCheck > level && capture > targetCheck && capture < loop);
        Assert.Contains("document.BerryProgress = recipientBerryProgress;", prepareImport);
    }

    [Fact]
    public void CompletedStartPosMetadataUsesTheSynchronousModuleSavePath() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int save = source.IndexOf("internal static bool SaveAkronStartPosData()", StringComparison.Ordinal);
        int saveEnd = source.IndexOf("private static Dictionary<string, int> BuildRoomOrder", save, StringComparison.Ordinal);
        string savePath = SourceSlice(source, save, saveEnd - save);

        Assert.Contains("Instance.SerializeSaveData", savePath);
        Assert.Contains("Instance.WriteSaveData", savePath);
        Assert.Contains("Instance.ReadSaveData", savePath);
        Assert.Contains("persisted.SequenceEqual(serialized)", savePath);
        Assert.DoesNotContain("UserIO.SaveHandler", savePath);
    }

    [Fact]
    public void FailedStartPosMetadataVerificationRestoresThePreviousPersistedBytes() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int save = source.IndexOf("internal static bool SaveAkronStartPosData()", StringComparison.Ordinal);
        int saveEnd = source.IndexOf("private static Dictionary<string, int> BuildRoomOrder", save, StringComparison.Ordinal);
        string savePath = SourceSlice(source, save, saveEnd - save);

        int preserve = savePath.IndexOf("previousPersisted = AkronModule.Instance.ReadSaveData(fileSlot);", StringComparison.Ordinal);
        int write = savePath.IndexOf("AkronModule.Instance.WriteSaveData(fileSlot, serialized);", StringComparison.Ordinal);
        int verify = savePath.IndexOf("!persisted.SequenceEqual(serialized)", StringComparison.Ordinal);
        int restore = savePath.IndexOf("previousPersistedLost = !RestoreAkronStartPosData(fileSlot, previousPersisted);", verify, StringComparison.Ordinal);
        int restoreWrite = savePath.IndexOf("AkronModule.Instance.WriteSaveData(fileSlot, previousPersisted);", StringComparison.Ordinal);
        int restoreVerify = savePath.IndexOf("restored.SequenceEqual(previousPersisted)", StringComparison.Ordinal);

        Assert.True(preserve >= 0 && write > preserve && verify > write && restore > verify);
        Assert.True(restoreWrite > restore && restoreVerify > restoreWrite);
        Assert.Contains("if (writeStarted)", savePath);
        Assert.Contains("WriteSaveData(null)", savePath);
        Assert.Contains("private static bool RestoreAkronStartPosData", savePath);
        Assert.Contains("return matches;", savePath);
        Assert.Contains("return false;", savePath);
    }

    [Fact]
    public void SetupPackStartPosImportDoesNotResetEveryPersistedMap() {
        string source = File.ReadAllText(GetActionsSourcePath());

        Assert.DoesNotContain("saveData.StartPositionsByMap = new Dictionary<string, AkronPersistedStartPosMap>();", source);
        Assert.Contains("ReplacePersistedStartPositionsForMap", source);
        Assert.Contains("foreach (int previousSlot in previousSlots)", source);
        Assert.Contains("replacementSlots.Contains(previousSlot)", source);
        Assert.Contains("DiscardStartPosRuntimeStateMemory(areaSid, previousSlot)", source);
    }

    [Fact]
    public void StartPosSnapshotKeysDoNotUseLossyAreaSidSanitization() {
        string source = File.ReadAllText(GetActionsSourcePath());

        Assert.Contains("Encoding.UTF8", source);
        Assert.Contains("valueByte.ToString(\"x2\"", source);
        Assert.DoesNotContain("char.IsLetterOrDigit(character)", source);
    }

    [Fact]
    public void LoadingStartPosPreservesTheRespawnPreference() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int load = source.IndexOf("public static void LoadStartPos(Level level)", StringComparison.Ordinal);
        int loadEnd = source.IndexOf("public static void LoadStartPosSlot", load, StringComparison.Ordinal);
        int restore = source.IndexOf("private static bool RestoreStartPos(", StringComparison.Ordinal);
        int restoreEnd = source.IndexOf("internal static void RelinkRuntimeRenderState", restore, StringComparison.Ordinal);
        string loadPath = SourceSlice(source, load, loadEnd - load);
        string restorePath = SourceSlice(source, restore, restoreEnd - restore);

        Assert.DoesNotContain("enableRespawnAtStartPosAfterRestore", loadPath);
        Assert.DoesNotContain("enableRespawnAtStartPosAfterRestore", restorePath);
        Assert.Contains("AkronModule.Settings.RespawnAtStartPos = restoreRespawnAtStartPos;", restorePath);
    }

    [Fact]
    public void EnabledStartPosRespawnUsesTheLastLoadedSlotAfterDeath() {
        string source = File.ReadAllText(GetActionsSourcePath());
        string playerRuntimeSource = File.ReadAllText(GetPlayerRuntimeSourcePath());

        Assert.Contains("if (loadedSlot > 0)", source);
        Assert.Contains("AkronModule.Session.LastLoadedStartPosSlot = loadedSlot;", source);
        Assert.Contains("RestoreStartPosAfterDeath(Level level, AkronStartPos startPos)", source);
        Assert.Contains("deadBody.DeathAction = () =>", playerRuntimeSource);
        Assert.Contains("deadBody.DeathAction == null", playerRuntimeSource);
        Assert.Contains("!deadBody.HasGolden", playerRuntimeSource);
        Assert.Contains("if (Engine.Scene != level)", source);
        Assert.Contains("SpotlightWipe.FocusPoint = respawnPoint - restoredLevel.Camera.Position;", source);
        Assert.Contains("restoredLevel.DoScreenWipe(wipeIn: true, () => CompleteStartPosInputWaitWipe(restoredLevel));", source);
        Assert.Contains("level.Reload();", source);
        Assert.Equal(1, playerRuntimeSource.Split("AkronActions.RestoreStartPosAfterDeath(level, startPosRespawn)").Length - 1);
    }

    [Fact]
    public void SuccessfulStartPosLoadsWaitForFreshInputAndKeepBackdropPresentationRunning() {
        string actionsSource = File.ReadAllText(GetActionsSourcePath());
        string waitSource = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-input-wait.cs"));
        string moduleSource = File.ReadAllText(GetModuleSourcePath());
        string commandsSource = File.ReadAllText(GetSourcePath("Commands", "akron-startpos-commands.cs"));

        Assert.Contains("BeginStartPosInputWait(currentLevel, waitingForWipe: false);", actionsSource);
        Assert.Contains("BeginStartPosInputWait(restoredLevel, waitingForWipe: true);", actionsSource);
        Assert.Contains("CompleteStartPosInputWaitWipe(restoredLevel)", actionsSource);
        Assert.Contains("if (AkronActions.UpdateStartPosInputWait(self))", moduleSource);
        Assert.Contains("AkronRuntimeOptions.HoldSceneClockForSkippedLevelUpdate(level);", waitSource);
        Assert.Contains("level.Wipe?.Update(level);", waitSource);
        Assert.Contains("level.HiresSnow?.Update(level);", waitSource);
        Assert.Contains("level.Foreground.Update(level);", waitSource);
        Assert.Contains("level.Background.Update(level);", waitSource);
        Assert.Contains("AkronActions.ClearStartPosInputWait();", moduleSource);
        Assert.Contains("AkronModule.Settings.StartPosWaitForInput = waitForInput;", commandsSource);
        Assert.Contains("startpos-wait-for-input:", commandsSource);
        Assert.Contains("wait <on|off|status>", commandsSource);
    }

    [Fact]
    public void StartPosSnapshotsDoNotKeepDormantSoundHandles() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        string deepCloneSource = File.ReadAllText(GetDeepCloneSourcePath());
        string eventInstanceSource = File.ReadAllText(GetEventInstanceSourcePath());

        Assert.Contains("SavedLevelEventInstances = AkronDeepClone.CopyIntoDormant", source);
        Assert.Contains("restoredEventInstances.AddRange(AkronDeepClone.CopyIntoDormant(savedLevel, level));", source);
        Assert.Contains("ActivateDormantEventInstances(restoredEventInstances);", source);
        Assert.Contains("ReleaseDormantEventInstances(saveSlot.SavedLevelEventInstances);", source);
        Assert.Contains("saveSlot.PreCloneState = null;", source);
        Assert.Contains("ReleaseDormantEventInstances(saveSlot);", source);
        Assert.Contains("AkronEventInstanceUtils.Clone(eventInstance, cloneEventInstancesAsDormant)", deepCloneSource);
        Assert.Contains("DormantPlaybackStates.Add(clone", eventInstanceSource);
        Assert.Contains("eventInstance.start();", eventInstanceSource);
        Assert.Contains("eventInstance.release();", eventInstanceSource);
        Assert.DoesNotContain("DetachClonedSoundSourceInstances", source);
    }

    [Fact]
    public void LiveEventClonesDoNotKeepFrozenSetFrameState() {
        string source = File.ReadAllText(GetEventInstanceSourcePath());
        int cloneMethod = source.IndexOf("public static EventInstance Clone", StringComparison.Ordinal);
        int captureMethod = source.IndexOf("public static AkronPersistentEventInstanceState CapturePersistentState", StringComparison.Ordinal);
        string clonePath = SourceSlice(source, cloneMethod, captureMethod - cloneMethod);

        int dormantBranch = clonePath.IndexOf("if (dormant)", StringComparison.Ordinal);
        int liveBranch = clonePath.IndexOf("} else if (shouldPlay)", dormantBranch, StringComparison.Ordinal);
        int frozenState = clonePath.IndexOf("CapturedCloneStates.Add(clone", dormantBranch, StringComparison.Ordinal);

        Assert.True(dormantBranch >= 0);
        Assert.True(frozenState > dormantBranch);
        Assert.True(liveBranch > frozenState);
    }

    [Fact]
    public void ColdRestoredSoundsKeepTheirSavedDescriptionOnlyWhileDormant() {
        string source = File.ReadAllText(GetEventInstanceSourcePath());
        int restoreMethod = source.IndexOf("public static EventInstance RestorePersistentState", StringComparison.Ordinal);
        int activateMethod = source.IndexOf("public static void ActivateDormantEventInstances", StringComparison.Ordinal);
        int releaseMethod = source.IndexOf("public static void ReleaseEventInstances", StringComparison.Ordinal);

        Assert.True(restoreMethod >= 0);
        Assert.True(activateMethod > restoreMethod);
        Assert.True(releaseMethod > activateMethod);
        Assert.Contains(
            "CapturedCloneStates.Add(eventInstance, new PersistentEventState",
            source.Substring(restoreMethod, activateMethod - restoreMethod));

        string activation = source.Substring(activateMethod, releaseMethod - activateMethod);
        int removeDormantState = activation.IndexOf("DormantPlaybackStates.Remove(eventInstance);", StringComparison.Ordinal);
        int removeCapturedState = activation.IndexOf("CapturedCloneStates.Remove(eventInstance);", StringComparison.Ordinal);
        int skipStoppedSound = activation.IndexOf("if (!playback.ShouldPlay)", StringComparison.Ordinal);
        Assert.True(removeDormantState >= 0);
        Assert.True(removeCapturedState > removeDormantState);
        Assert.True(skipStoppedSound > removeCapturedState);
    }

    [Fact]
    public void NativeRestoreDoesNotClearSavedVertexLightState() {
        string nativeSupportSource = File.ReadAllText(GetSourcePath("SaveLoad", "akron-native-savestate-support.cs"));

        Assert.DoesNotContain("ClearVertexLights", nativeSupportSource);
    }

    [Fact]
    public void RestoredStartPosResetsAudioCameraBeforeStartingSavedSounds() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int setCamera = source.IndexOf("Audio.SetCamera(level.Camera);", StringComparison.Ordinal);
        int activateSounds = source.IndexOf("AkronEventInstanceUtils.ActivateDormantEventInstances(restoredEventInstances);", StringComparison.Ordinal);

        Assert.True(setCamera >= 0);
        Assert.True(activateSounds > setCamera);
    }

    [Fact]
    public void FullStateStartPosDeathRespawnCanCrossRooms() {
        string source = File.ReadAllText(GetActionsSourcePath());
        string playerRuntimeSource = File.ReadAllText(GetPlayerRuntimeSourcePath());

        Assert.Contains("IsStartPosUsableForDeath(level, lastLoaded)", source);
        Assert.Contains("!string.IsNullOrWhiteSpace(startPos.StateSlotName)", source);
        Assert.Contains("if (string.Equals(startPos.Room, level.Session.Level", playerRuntimeSource);
        Assert.DoesNotContain("string.Equals(startPos.Room, level.Session.Level) &&\n                (string.IsNullOrWhiteSpace(startPos.AreaSid)", playerRuntimeSource);
    }

    [Fact]
    public void ReplacingImportedStartPositionsPreservesOtherMaps() {
        AkronModuleSaveData saveData = new AkronModuleSaveData {
            StartPositionsByMap = new Dictionary<string, AkronPersistedStartPosMap> {
                ["Map/A"] = new AkronPersistedStartPosMap {
                    Slots = new Dictionary<int, AkronPersistedStartPos> {
                        [1] = new AkronPersistedStartPos { AreaSid = "Map/A", Room = "old-a" }
                    }
                },
                ["Map/B"] = new AkronPersistedStartPosMap {
                    Slots = new Dictionary<int, AkronPersistedStartPos> {
                        [2] = new AkronPersistedStartPos { AreaSid = "Map/B", Room = "keep-b" }
                    }
                }
            }
        };
        Dictionary<int, AkronStartPos> replacement = new Dictionary<int, AkronStartPos> {
            [3] = new AkronStartPos { AreaSid = "Map/A", Room = "new-a", Position = new Vector2(12f, 34f) }
        };

        AkronActions.ReplacePersistedStartPositionsForMap(saveData, "Map/A", replacement);

        Assert.Equal("new-a", Assert.Single(saveData.StartPositionsByMap["Map/A"].Slots).Value.Room);
        Assert.Equal("keep-b", Assert.Single(saveData.StartPositionsByMap["Map/B"].Slots).Value.Room);

        AkronActions.ReplacePersistedStartPositionsForMap(saveData, "Map/A", new Dictionary<int, AkronStartPos>());

        Assert.False(saveData.StartPositionsByMap.ContainsKey("Map/A"));
        Assert.Equal("keep-b", Assert.Single(saveData.StartPositionsByMap["Map/B"].Slots).Value.Room);
    }

    [Fact]
    public void AFailedRestartCopyClearsThePendingStartPosEntry() {
        const int fileSlot = 3;
        const int slot = 4;
        AkronStartPos startPos = new AkronStartPos {
            AreaSid = "Akron/PendingCleanup",
            Room = "a-01",
            StateSlotName = "Akron StartPos File 3 akron-pending-cleanup 4"
        };
        AddPendingStartPos(fileSlot, slot, startPos);
        Assert.True(AkronActions.HasPendingStartPosState(startPos.StateSlotName));

        AkronStartPosPersistence.Cancel(startPos.StateSlotName);
        long generation = (long) typeof(AkronStartPosPersistence)
            .GetField("nextGeneration", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        Assert.True(AkronStartPosPersistence.IsCurrent(startPos.StateSlotName, generation));

        // AkronModule.Instance is null in a headless test, so this completion takes
        // the "originating save file is gone" exit. That is one of the four exits
        // which used to leave the pending entry in place forever and make the slot
        // unloadable after a map round trip.
        AkronActions.CompletePersistentStartPosCapture(
            fileSlot,
            startPos.ProfileId,
            slot,
            startPos,
            startPos.StateSlotName,
            generation,
            AkronSaveLoadResult.Failed,
            "the disk worker failed",
            string.Empty,
            TimeSpan.Zero);

        Assert.False(AkronActions.HasPendingStartPosState(startPos.StateSlotName));
    }

    [Fact]
    public void EveryRestartCopyExitClearsThePendingEntryFromOneFinallyBlock() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int completion = source.IndexOf("internal static void CompletePersistentStartPosCapture", StringComparison.Ordinal);
        int completionEnd = source.IndexOf("private static void RollBackFailedStartPos", completion, StringComparison.Ordinal);
        string completionPath = SourceSlice(source, completion, completionEnd - completion);

        // The cleanup lives in a finally guarded by a single success flag, so a new exit
        // path cannot reintroduce the poison entry by forgetting its own cleanup call.
        int finallyBlock = completionPath.IndexOf("} finally {", StringComparison.Ordinal);
        int guard = completionPath.IndexOf("if (!committed) {", finallyBlock, StringComparison.Ordinal);
        int rollBack = completionPath.IndexOf("RollBackFailedStartPos(", guard, StringComparison.Ordinal);
        int rollBackStagedInstall = completionPath.IndexOf("installedSnapshot?.Dispose();", finallyBlock, StringComparison.Ordinal);

        Assert.True(finallyBlock >= 0);
        Assert.True(guard > finallyBlock);
        Assert.True(rollBack > guard);
        // The staged install must roll back before the slot state is restored or deleted.
        Assert.True(rollBackStagedInstall > finallyBlock && rollBackStagedInstall < guard);
        Assert.Equal(1, CountOccurrences(completionPath, "RollBackFailedStartPos("));
        Assert.Equal(0, CountOccurrences(completionPath, "DiscardFailedStartPos("));
        Assert.Equal(1, CountOccurrences(completionPath, "committed = true;"));

        // The parked previous state is released only once the snapshot install has
        // committed and its metadata is written, and before the success flag is set.
        int commitInstall = completionPath.IndexOf("installedSnapshot.Commit();", StringComparison.Ordinal);
        int releaseRollback = completionPath.IndexOf("ReleaseStartPosRollback(stateSlotName);", StringComparison.Ordinal);
        int persistMetadata = completionPath.IndexOf("PersistStartPos(slot, startPos, fileSlot, out previousMetadataLost)", StringComparison.Ordinal);
        int commitFlag = completionPath.IndexOf("committed = true;", StringComparison.Ordinal);
        Assert.True(persistMetadata >= 0 && commitInstall > persistMetadata);
        Assert.True(releaseRollback > commitInstall && releaseRollback < commitFlag);
        Assert.Contains(
            "installedSnapshot?.PreviousSnapshotLost == true || previousMetadataLost",
            completionPath);
    }

    [Fact]
    public void ADiscardedStartPosLeavesNoLoadableRemains() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int discard = source.IndexOf("private static void DiscardFailedStartPos", StringComparison.Ordinal);
        int discardEnd = source.IndexOf("private static void PublishPendingStartPos", discard, StringComparison.Ordinal);
        string discardPath = SourceSlice(source, discard, discardEnd - discard);

        Assert.Contains("RemovePendingStartPos(fileSlot, slot, startPos);", discardPath);
        Assert.Contains("CancelStartPosPersistence(startPos.StateSlotName);", discardPath);
        Assert.Contains("AkronSaveLoadService.ClearRuntimeState(startPos.StateSlotName);", discardPath);
        Assert.Contains("RemovePersistedStartPos(", discardPath);
        Assert.Contains("session.StartPositions.Remove(normalizedSlot);", discardPath);
        // The in-place catalog mutation has to invalidate the cached StartPos list.
        Assert.Contains("MarkStartPosCatalogChanged();", discardPath);
        Assert.Contains("was removed because", discardPath);
    }

    [Fact]
    public void ARoomChangeCannotForceAStartPosLoadOntoTheColdPath() {
        string moduleSource = File.ReadAllText(GetModuleSourcePath());
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());
        string playerRuntimeSource = File.ReadAllText(GetPlayerRuntimeSourcePath());

        // The warm slot is selected by map SID, save file and session nonce. The nonce is
        // regenerated only from the Level.Begin hook, which Celeste runs when the chapter
        // scene starts, not when the player walks into the next room - the repository
        // itself splits those two events (DeathsSinceLevelLoad in Begin,
        // DeathsSinceRoomTransition in Player.OnTransition). So a cross-room load in the
        // same session stays on the warm path and never pays for reconstruction.
        Assert.Equal(1, CountOccurrences(moduleSource, "Session.CurrentSessionNonce ="));
        int begin = moduleSource.IndexOf("private static void LevelOnBegin(", StringComparison.Ordinal);
        int beginEnd = moduleSource.IndexOf("private static void LevelOnEnd(", begin, StringComparison.Ordinal);
        Assert.Contains("Session.CurrentSessionNonce =", SourceSlice(moduleSource, begin, beginEnd - begin));
        Assert.Contains("On.Celeste.Level.Begin += LevelOnBegin;", moduleSource);
        Assert.Contains("Session.DeathsSinceRoomTransition = 0;", playerRuntimeSource);

        int matches = saveLoadSource.IndexOf(
            "private static bool MatchesCurrentNativeSession(",
            StringComparison.Ordinal);
        int matchesEnd = saveLoadSource.IndexOf("private static void CaptureCuratedSessionState(", matches, StringComparison.Ordinal);
        string matchesPath = SourceSlice(saveLoadSource, matches, matchesEnd - matches);
        Assert.DoesNotContain("Session.Level", matchesPath);
        Assert.DoesNotContain("LevelName", matchesPath);

        // Nothing on the room-change path drops the warm clone either.
        int transition = playerRuntimeSource.IndexOf("private static void PlayerOnTransition(", StringComparison.Ordinal);
        int transitionEnd = playerRuntimeSource.IndexOf("private static void ApplyDashCountOverride(", transition, StringComparison.Ordinal);
        string transitionPath = transitionEnd > transition
            ? SourceSlice(playerRuntimeSource, transition, transitionEnd - transition)
            : SourceTail(playerRuntimeSource, transition);
        Assert.DoesNotContain("ClearRuntimeState", transitionPath);
        Assert.DoesNotContain("DiscardRuntimeStateMemory", transitionPath);
    }

    [Fact]
    public void EveryStartPosLoadOutcomeReachesThePlayer() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int load = source.IndexOf("public static void LoadStartPos(Level level)", StringComparison.Ordinal);
        int loadEnd = source.IndexOf("public static void LoadStartPosSlot(", load, StringComparison.Ordinal);
        string loadPath = SourceSlice(source, load, loadEnd - load);

        // The two deferred-boundary guards used to return without a word, which looks
        // exactly like a dead hotkey.
        Assert.Contains("was not loaded: the scene changed.", loadPath);
        Assert.Contains("was not loaded: a capture is still finishing.", loadPath);

        // The deferred boundary swallows exceptions, so the restore reports its own.
        int restore = source.IndexOf(
            "private static bool RestoreStartPos(Level level, AkronStartPos startPos",
            StringComparison.Ordinal);
        int restoreEnd = source.IndexOf("private static void ReportStartPosLoadFailure(", restore, StringComparison.Ordinal);
        string restorePath = SourceSlice(source, restore, restoreEnd - restore);
        Assert.Contains("catch (Exception exception)", restorePath);
        Assert.Contains("ReportStartPosLoadFailure(", restorePath);

        // A rolled-back cold restore has to say that nothing changed, or it is
        // indistinguishable from the load never having run.
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());
        Assert.Contains("nothing was changed and you are still in ", saveLoadSource);
        Assert.Contains("its restart copy is still finishing", saveLoadSource);
        Assert.Contains("no restart copy of this StartPos exists on disk", saveLoadSource);
    }

    // What the refusal is about decides the sentence, and it is carried from the graph to
    // the toast through five hops. Every one of them can silently drop it and leave the
    // unit tests on AkronStartPosRefusal.Describe passing while no load in the game ever
    // reaches the map sentence again - which is the failure the message before this one
    // shipped with. Each hop is pinned where it happens rather than anywhere in the file.
    [Fact]
    public void AMapChangeRefusalKeepsItsKindAllTheWayToTheToast() {
        // 1. the rebuild's returned failure, and 2. a refusal thrown past the graph's own
        // handlers, both reach the same two fields on the load.
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());
        Assert.Contains(
            "SetPersistentSnapshotFailure(\"rebuild \" + restore.Error, restore.RefusedTypeName, restore.RefusedKind);",
            saveLoadSource);
        Assert.Contains("refusal?.RefusedKind ?? AkronReconstructionRefusalKind.SavedObject", saveLoadSource);

        // 3. the load hands both to the report rather than the type name alone.
        string actionsSource = File.ReadAllText(GetActionsSourcePath());
        int gate = actionsSource.IndexOf(
            "private static bool RestoreStartPosUnderPacingGate(",
            StringComparison.Ordinal);
        string gatePath = SourceTail(actionsSource, gate);
        Assert.Contains("AkronSaveLoadService.LastPersistentSnapshotRefusedKind", gatePath);

        // 4. the deferred boundary's own catch does the same for a thrown refusal.
        int restore = actionsSource.IndexOf(
            "private static bool RestoreStartPos(Level level, AkronStartPos startPos",
            StringComparison.Ordinal);
        int restoreEnd = actionsSource.IndexOf(
            "private static void ReportStartPosLoadFailure(",
            restore,
            StringComparison.Ordinal);
        string restorePath = SourceSlice(actionsSource, restore, restoreEnd - restore);
        Assert.Contains("refusal?.RefusedKind ?? AkronReconstructionRefusalKind.SavedObject", restorePath);

        // 5. the report builds the sentence from both.
        int report = actionsSource.IndexOf(
            "private static void ReportStartPosLoadFailure(",
            StringComparison.Ordinal);
        int reportEnd = actionsSource.IndexOf(
            "private static string DescribeRestoreFailure(",
            report,
            StringComparison.Ordinal);
        string reportPath = SourceSlice(actionsSource, report, reportEnd - report);
        Assert.Contains(
            "AkronStartPosRefusal.Describe(slotLabel, refusedTypeName, refusedKind)",
            reportPath);
    }

    [Fact]
    public void AFailedSetKeepsTheStartPosTheSlotAlreadyHeld() {
        const int fileSlot = 6;
        const int slot = 3;
        AkronStartPos startPos = new AkronStartPos {
            AreaSid = "Akron/AtomicSet",
            Room = "b-02",
            StateSlotName = "Akron StartPos File 6 akron-atomic-set 3 " + Guid.NewGuid().ToString("N")
        };

        try {
            // The StartPos the slot already held: a real snapshot in the real snapshot
            // directory, written through the same call the persistence worker uses.
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                startPos.StateSlotName,
                "Akron/AtomicSet",
                "b-01",
                fileSlot,
                MinimalReconstructionDocument(),
                out string saveError), saveError);
            Assert.True(AkronStartPosReconstruction.HasSnapshot(startPos.StateSlotName));

            // Now a new Set on the same slot: it parks what the slot held, publishes its
            // own pending entry, and its restart copy then fails.
            BeginStartPosRollback(slot, startPos.StateSlotName);
            AddPendingStartPos(fileSlot, slot, startPos);
            AkronStartPosPersistence.Cancel(startPos.StateSlotName);
            long generation = CurrentPersistenceGeneration();
            Assert.True(AkronStartPosPersistence.IsCurrent(startPos.StateSlotName, generation));

            CompleteWithFailure(fileSlot, slot, startPos, generation, "the disk worker failed");

            // The failed Set is the no-op: the previous snapshot is still there and still
            // loads. Before this change the failure deleted it along with its metadata.
            Assert.True(AkronStartPosReconstruction.HasSnapshot(startPos.StateSlotName));
            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                startPos.StateSlotName,
                out AkronReconstructionDocument document,
                out string loadError), loadError);
            Assert.Equal("b-01", document.Room);
            Assert.Equal(startPos.StateSlotName, document.SlotName);
            // The pending marker is gone, so the slot is loadable again rather than stuck
            // reporting that its restart copy has not finished.
            Assert.False(AkronActions.HasPendingStartPosState(startPos.StateSlotName));
        } finally {
            AkronStartPosReconstruction.DeleteSnapshot(startPos.StateSlotName);
        }
    }

    // startpos-set answers "is a StartPos active", which was read as "is it on disk".
    // Those are minutes apart: a Set returns as soon as its warm clone exists, and the
    // restart copy behind it parks while the player is in control. Measured on the Windows
    // machine at twenty-five minutes of play with nothing on disk, while the automation
    // query reported startpos-set: true throughout. The queue depth is what shows that
    // from outside.
    [Fact]
    public void OutstandingRestartCopiesAreCounted() {
        const int fileSlot = 7;
        const int slot = 5;
        AkronStartPos startPos = new AkronStartPos {
            AreaSid = "Akron/RestartCopyCount",
            Room = "c-01",
            StateSlotName = "Akron StartPos File 7 akron-restart-copy-count 5 " + Guid.NewGuid().ToString("N")
        };
        int outstandingBefore = AkronActions.PendingStartPosStateCount;

        // A Set publishes a pending entry for its slot and returns. Until that Set
        // commits, its restart copy is outstanding and the slot is not on disk.
        AddPendingStartPos(fileSlot, slot, startPos);
        try {
            Assert.True(AkronActions.HasPendingStartPosState(startPos.StateSlotName));
            Assert.Equal(outstandingBefore + 1, AkronActions.PendingStartPosStateCount);
            Assert.False(AkronStartPosReconstruction.HasSnapshot(startPos.StateSlotName));
        } finally {
            RemovePendingStartPosForTest(fileSlot, slot);
        }

        Assert.Equal(outstandingBefore, AkronActions.PendingStartPosStateCount);
    }

    [Fact]
    public void DurabilityIsReportedSeparatelyFromWhetherAStartPosIsSet() {
        string status = File.ReadAllText(GetSourcePath("Commands", "akron-startpos-commands.cs"));

        // All three, and startpos-set still there: it keeps its own meaning rather than
        // being redefined under an existing gate's feet.
        Assert.Contains("Log(\"startpos-set: \"", status);
        Assert.Contains("Log(\"startpos-snapshot-on-disk: \" +", status);
        Assert.Contains("AkronStartPosReconstruction.HasSnapshot(stateSlotName)", status);
        Assert.Contains("Log(\"startpos-restart-copy-outstanding: \" +", status);
        Assert.Contains("AkronActions.HasPendingStartPosState(stateSlotName)", status);
        Assert.Contains(
            "Log(\"startpos-restart-copies-outstanding: \" +\n            AkronActions.PendingStartPosStateCount",
            status.Replace("\r\n", "\n"));
    }

    // A refusal reaches the player as a toast, and no command could report the text of one
    // Akron raised, so the wording this branch reworked twice could not be asserted in a
    // scripted check. It is read back from the one place every toast passes through.
    [Fact]
    public void MessagesRaisedForThePlayerAreReadableAfterwards() {
        string first = "Akron read-back first " + Guid.NewGuid().ToString("N");
        string second = "Akron read-back second " + Guid.NewGuid().ToString("N");
        long raisedBefore = AkronToast.RaisedMessageCount;

        // The recording call the constructor makes. Constructing the entity itself needs a
        // live FNA, which a headless run does not have.
        AkronToast.RecordRaisedMessage(first);
        AkronToast.RecordRaisedMessage(second);

        Assert.Equal(raisedBefore + 2, AkronToast.RaisedMessageCount);
        Assert.Equal(new[] { first, second }, AkronToast.GetRecentMessages(2));

        // Bounded, and it drops the oldest rather than the newest: a gate reads the tail.
        for (int index = 0; index < 40; index++) {
            AkronToast.RecordRaisedMessage(
                "Akron read-back filler " + index.ToString(CultureInfo.InvariantCulture));
        }
        Assert.DoesNotContain(first, AkronToast.GetRecentMessages(128));
        Assert.Equal("Akron read-back filler 39", Assert.Single(AkronToast.GetRecentMessages(1)));

        // The constructor is what calls it, which is the half this headless run cannot
        // reach: without this line a real message is shown and recorded nowhere.
        Assert.Contains(
            "sequence = RecordRaisedMessage(message);",
            File.ReadAllText(GetSourcePath("Core", "AkronToast.cs")),
            StringComparison.Ordinal);
    }

    // The read-back is only usable by an in-game gate if the file queue will run it, and
    // the queue refuses anything not on its allowlist. This asserts the pair: the command
    // exists, and a command file naming it parses.
    [Fact]
    public void TheMessageReadBackCommandIsAllowlistedForAutomation() {
        Assert.Contains(
            "[Command(\"akron_qa_messages\"",
            File.ReadAllText(GetQaCommandsSourcePath()),
            StringComparison.Ordinal);
        Assert.True(AkronAutomationService.TryParseCommandFileForTesting(
            "token: akron-message-read-back-token-0123456789\nakron_qa_messages 3\n",
            "akron-message-read-back-token-0123456789",
            out IReadOnlyList<string> commands,
            out string error), error);
        Assert.Equal("akron_qa_messages 3", Assert.Single(commands));
    }

    [Fact]
    public void AnOutstandingRestartCopyFailingKeepsTheCommittedStartPosLoadable() {
        const int fileSlot = 9;
        const int slot = 2;
        string stateSlotName = "Akron StartPos File 9 akron-outstanding-job 2 " + Guid.NewGuid().ToString("N");
        AkronStartPos replacement = new AkronStartPos {
            AreaSid = "Akron/OutstandingJob",
            Room = "e-02",
            StateSlotName = stateSlotName
        };

        try {
            // A StartPos that is committed and loadable: a real snapshot on disk plus
            // the warm clone that serves same-session loads.
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                stateSlotName,
                "Akron/OutstandingJob",
                "e-01",
                fileSlot,
                MinimalReconstructionDocument(),
                out string saveError), saveError);
            StoreRuntimeSlotForTest(stateSlotName, "e-01", "Akron/OutstandingJob");

            // Set over it. The Set reports success and its restart copy is queued.
            // That is the state a slot now spends a whole session in, because the
            // worker does not run at all while the player is in control - so an
            // outstanding job is alive long enough to meet anything that can fail it.
            BeginStartPosRollback(slot, stateSlotName);
            StoreRuntimeSlotForTest(stateSlotName, "e-02", "Akron/OutstandingJob");
            AddPendingStartPos(fileSlot, slot, replacement);
            AkronStartPosPersistence.Cancel(stateSlotName);

            // The outstanding job fails, long after the Set said it had worked.
            CompleteWithFailure(
                fileSlot,
                slot,
                replacement,
                CurrentPersistenceGeneration(),
                "the save file it belongs to is no longer open");

            // Nothing the slot already had may go with it. The snapshot is still on
            // disk, still loads, and still describes its own room; the warm clone on
            // the canonical name is the committed one again, not the abandoned Set.
            Assert.True(AkronStartPosReconstruction.HasSnapshot(stateSlotName));
            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                stateSlotName,
                out AkronReconstructionDocument document,
                out string loadError), loadError);
            Assert.Equal("e-01", document.Room);
            AkronSaveLoadSlotLease? lease = AkronSaveLoadService.RetainRuntimeState(stateSlotName);
            try {
                Assert.NotNull(lease?.Slot);
                Assert.Equal("e-01", lease!.Slot!.LevelName);
            } finally {
                lease?.Dispose();
            }
            // And the slot is loadable rather than stuck reporting a pending copy.
            Assert.False(AkronActions.HasPendingStartPosState(stateSlotName));
        } finally {
            AkronStartPosReconstruction.DeleteSnapshot(stateSlotName);
            AkronSaveLoadService.DiscardRuntimeStateMemory(stateSlotName);
        }
    }

    [Fact]
    public void APendingRestartCopyThatNothingWillFinishDoesNotStallTheLoad() {
        const int fileSlot = 10;
        const int slot = 7;
        string stateSlotName = "Akron StartPos File 10 akron-load-wait 7 " + Guid.NewGuid().ToString("N");
        AkronStartPos startPos = new AkronStartPos {
            AreaSid = "Akron/LoadWait",
            Room = "f-01",
            StateSlotName = stateSlotName
        };
        AddPendingStartPos(fileSlot, slot, startPos);

        try {
            Stopwatch timer = Stopwatch.StartNew();
            AkronStartPosPersistence.FinishPendingRestartCopy(stateSlotName);
            timer.Stop();

            // No job is queued or running for this slot, so nothing will ever clear
            // the pending marker. The wait has to notice that on the first pump: it
            // holds the game thread, and burning the whole budget here would turn a
            // slot that cannot load into a freeze on top of it.
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), timer.Elapsed.ToString());
            Assert.True(AkronActions.HasPendingStartPosState(stateSlotName));
        } finally {
            RemovePendingStartPosForTest(fileSlot, slot);
        }
    }

    [Fact]
    public void ALoadThatCannotComeFromMemoryFinishesTheRestartCopyItNeeds() {
        string actionsSource = File.ReadAllText(GetActionsSourcePath());
        int restore = actionsSource.IndexOf(
            "private static bool RestoreStartPosUnderPacingGate(",
            StringComparison.Ordinal);
        int restoreEnd = actionsSource.IndexOf("private static void ReportStartPosRestoreTiming(", restore, StringComparison.Ordinal);
        string restorePath = SourceSlice(actionsSource, restore, restoreEnd - restore);

        int memoryCheck = restorePath.IndexOf(
            "AkronSaveLoadService.WillRestoreFromRuntimeMemory(level, startPos.StateSlotName)",
            StringComparison.Ordinal);
        int finish = restorePath.IndexOf(
            "AkronStartPosPersistence.FinishPendingRestartCopy(startPos.StateSlotName)",
            memoryCheck,
            StringComparison.Ordinal);
        int catalog = restorePath.IndexOf("currentStartPositionsByMap =", StringComparison.Ordinal);
        int load = restorePath.IndexOf("AkronSaveLoadService.LoadRuntimeState(", StringComparison.Ordinal);

        // A load that will be served from memory must never wait, and the wait has to
        // run before the catalog is snapshotted: a completion applied during it writes
        // the StartPos into save data, and the restore afterwards would put the old
        // catalog back over it.
        Assert.True(memoryCheck >= 0);
        Assert.True(finish > memoryCheck);
        Assert.True(catalog > finish);
        Assert.True(load > catalog);
    }

    [Fact]
    public void AFailedSetLeavesNoAbandonedWarmCloneOnASnapshotOnlySlot() {
        const int fileSlot = 8;
        const int slot = 5;
        string stateSlotName = "Akron StartPos File 8 akron-atomic-set-cold 5 " + Guid.NewGuid().ToString("N");
        AkronStartPos startPos = new AkronStartPos {
            AreaSid = "Akron/AtomicSetCold",
            Room = "d-02",
            StateSlotName = stateSlotName
        };

        try {
            // A slot whose warm clone is gone - after a restart, or after a session
            // mismatch dropped it - still has its snapshot, and that snapshot is what the
            // previous metadata pairs with.
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                stateSlotName,
                "Akron/AtomicSetCold",
                "d-01",
                fileSlot,
                MinimalReconstructionDocument(),
                out string saveError), saveError);
            BeginStartPosRollback(slot, stateSlotName);

            // The new Set's capture, on the canonical name, exactly where SaveRuntimeState
            // puts it. Nothing was parked, because there was no warm clone to park.
            StoreRuntimeSlotForTest(stateSlotName, "d-02", "Akron/AtomicSetCold");
            Assert.True(AkronSaveLoadService.HasRuntimeStateInMemory(stateSlotName));

            AddPendingStartPos(fileSlot, slot, startPos);
            AkronStartPosPersistence.Cancel(stateSlotName);
            CompleteWithFailure(fileSlot, slot, startPos, CurrentPersistenceGeneration(), "the disk worker failed");

            // The abandoned capture must not survive: leaving it on the canonical name
            // would make the next load restore the new state under the previous metadata.
            Assert.False(AkronSaveLoadService.HasRuntimeStateInMemory(stateSlotName));
            Assert.True(AkronStartPosReconstruction.HasSnapshot(stateSlotName));
        } finally {
            AkronStartPosReconstruction.DeleteSnapshot(stateSlotName);
            AkronSaveLoadService.DiscardRuntimeStateMemory(stateSlotName);
        }
    }

    [Fact]
    public void AFailedSetOnAnEmptySlotStillLeavesTheSlotEmpty() {
        const int fileSlot = 7;
        const int slot = 4;
        AkronStartPos startPos = new AkronStartPos {
            AreaSid = "Akron/AtomicSetEmpty",
            Room = "c-01",
            StateSlotName = "Akron StartPos File 7 akron-atomic-set-empty 4 " + Guid.NewGuid().ToString("N")
        };

        Assert.False(AkronStartPosReconstruction.HasSnapshot(startPos.StateSlotName));
        BeginStartPosRollback(slot, startPos.StateSlotName);
        AddPendingStartPos(fileSlot, slot, startPos);
        AkronStartPosPersistence.Cancel(startPos.StateSlotName);
        long generation = CurrentPersistenceGeneration();

        CompleteWithFailure(fileSlot, slot, startPos, generation, "the disk worker failed");

        Assert.False(AkronStartPosReconstruction.HasSnapshot(startPos.StateSlotName));
        Assert.False(AkronActions.HasPendingStartPosState(startPos.StateSlotName));
    }

    [Fact]
    public void RollingBackAFailedSetNeverTouchesTheSlotsDurableState() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int restore = source.IndexOf("private static void RestoreStartPosRollback", StringComparison.Ordinal);
        int restoreEnd = source.IndexOf("private static void CancelStartPosPersistence", restore, StringComparison.Ordinal);
        string restorePath = SourceSlice(source, restore, restoreEnd - restore);

        // Keeping the previous StartPos means keeping every durable part of it. None of
        // these may appear on the path that puts a slot back.
        Assert.True(restore >= 0);
        Assert.DoesNotContain("ClearRuntimeState", restorePath);
        Assert.DoesNotContain("DeleteSnapshot", restorePath);
        Assert.DoesNotContain("RemovePersistedStartPos", restorePath);
        Assert.Contains("RestoreParkedRuntimeState", restorePath);
        Assert.Contains("RestoreParkedRuntimeFreshBaseline", restorePath);
        Assert.Contains("MarkStartPosCatalogChanged();", restorePath);
        Assert.Contains("DescribeFailedStartPosReplacement(normalizedSlot, reason, previousDurableStateLost)", restorePath);

        // Which outcome applies is read from what the slot held when the Set began.
        int begin = source.IndexOf("private static StartPosRollback BeginStartPosRollback", StringComparison.Ordinal);
        int beginEnd = source.IndexOf("private static void ReleaseStartPosRollback", begin, StringComparison.Ordinal);
        string beginPath = SourceSlice(source, begin, beginEnd - begin);
        Assert.Contains("HadCommittedState = previousEntry != null", beginPath);
        Assert.Contains("AkronStartPosReconstruction.HasSnapshot(stateSlotName)", beginPath);
        Assert.Contains("AkronSaveLoadService.ParkRuntimeState(stateSlotName)", beginPath);
        Assert.Contains("AkronStartPosPersistence.ParkRuntimeFreshBaseline(stateSlotName)", beginPath);
    }

    private static AkronReconstructionDocument MinimalReconstructionDocument() {
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

    private static void BeginStartPosRollback(int slot, string stateSlotName) {
        typeof(AkronActions)
            .GetMethod("BeginStartPosRollback", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { slot, stateSlotName, null });
    }

    // Puts a real runtime slot on the canonical name through the real StoreRuntimeSlot, the
    // way a capture does. CaptureRuntimeState needs a live Level, which a headless test has
    // no way to build, so the slot itself is constructed directly.
    private static void StoreRuntimeSlotForTest(string slotName, string levelName, string mapSid) {
        typeof(AkronSaveLoadService)
            .GetMethod("StoreRuntimeSlot", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] {
                slotName,
                new AkronSaveLoadSlot(slotName, levelName, mapSid, saveTimeAndDeaths: false)
            });
    }

    private static long CurrentPersistenceGeneration() {
        return (long) typeof(AkronStartPosPersistence)
            .GetField("nextGeneration", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
    }

    // AkronModule.Instance is null in a headless test, so the completion takes the
    // "originating save file is gone" exit, one of the four failure exits.
    private static void CompleteWithFailure(
        int fileSlot,
        int slot,
        AkronStartPos startPos,
        long generation,
        string error
    ) {
        AkronActions.CompletePersistentStartPosCapture(
            fileSlot,
            startPos.ProfileId,
            slot,
            startPos,
            startPos.StateSlotName,
            generation,
            AkronSaveLoadResult.Failed,
            error,
            string.Empty,
            TimeSpan.Zero);
    }

    private static void AddPendingStartPos(int fileSlot, int slot, AkronStartPos startPos) {
        Type actions = typeof(AkronActions);
        startPos.ProfileId = AkronActions.GetCurrentStartPosProfileId();
        string key = (string) actions
            .GetMethod("BuildPendingStartPosKey", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { fileSlot, startPos.AreaSid, startPos.ProfileId })!;
        Dictionary<string, Dictionary<int, AkronStartPos>> pendingByKey =
            (Dictionary<string, Dictionary<int, AkronStartPos>>) actions
                .GetField("PendingStartPositionsByFileAndMap", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!;
        if (!pendingByKey.TryGetValue(key, out Dictionary<int, AkronStartPos>? pending)) {
            pending = new Dictionary<int, AkronStartPos>();
            pendingByKey[key] = pending;
        }
        pending[slot] = startPos;
    }

    private static void RemovePendingStartPosForTest(int fileSlot, int slot) {
        Type actions = typeof(AkronActions);
        Dictionary<string, Dictionary<int, AkronStartPos>> pendingByKey =
            (Dictionary<string, Dictionary<int, AkronStartPos>>) actions
                .GetField("PendingStartPositionsByFileAndMap", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!;
        foreach (Dictionary<int, AkronStartPos> pending in pendingByKey.Values) {
            pending.Remove(slot);
        }
        foreach (string key in pendingByKey
                     .Where(entry => entry.Value.Count == 0)
                     .Select(entry => entry.Key)
                     .ToArray()) {
            pendingByKey.Remove(key);
        }
    }

    // --- Snapshot pacing -------------------------------------------------
    // The worker allocates hundreds of megabytes per StartPos and every gen0
    // collection that causes stops the game thread, so the worker does not run
    // at all while the player is in control. It stops; it is not slowed down.

    [Fact]
    public void TheSnapshotWorkerStopsForExactlyAsLongAsThePlayerHasControl() {
        bool previousActive = AkronSnapshotPacing.GameplayActive;
        bool previousForcedOpen = AkronSnapshotPacing.ForcedOpen;
        try {
            AkronSnapshotPacing.ForcedOpen = false;

            AkronSnapshotPacing.GameplayActive = false;
            Assert.False(AkronSnapshotPacing.ShouldSuspend());

            AkronSnapshotPacing.GameplayActive = true;
            Assert.True(AkronSnapshotPacing.ShouldSuspend());

            // Shutdown joins the worker. A job mid-sleep must finish rather than
            // hold the join open until the player happens to pause.
            AkronSnapshotPacing.ForcedOpen = true;
            Assert.False(AkronSnapshotPacing.ShouldSuspend());
        } finally {
            AkronSnapshotPacing.GameplayActive = previousActive;
            AkronSnapshotPacing.ForcedOpen = previousForcedOpen;
        }
    }

    // Quitting cancels the job in flight, and the one place that knows how to say that in
    // a sentence a player can read is RunWorker's catch (OperationCanceledException). Both
    // stages of a job have to let the cancellation reach it.
    //
    // The capture walk paces once per document node, so the common cancellation lands
    // there - inside a catch that turned every exception into Failed("$", "<type>:
    // <message>"). The rollback message the player then read was "$:
    // OperationCanceledException: Celeste closed before its restart copy finished" rather
    // than the sentence on its own, and RunWorker's dedicated handler was close to dead
    // for the case it exists for. The snapshot write is the same story one stage later: it
    // paces once per buffer and reported through its own out-parameter.
    [Fact]
    public void QuittingMidJobReachesTheHandlerThatKnowsHowToWordIt() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-cancelled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        bool previousCancelled = AkronSnapshotPacing.Cancelled;
        try {
            AkronReconstructionGraph graph = new AkronReconstructionGraph(_ => false);
            PacingChainNode saved = BuildPacingChain(600, valueOffset: 10);
            PacingChainNode baseline = BuildPacingChain(600, valueOffset: 0);

            AkronSnapshotPacing.BeginPacedWork();
            try {
                AkronSnapshotPacing.Cancelled = true;
                OperationCanceledException cancelledCapture = Assert.Throws<OperationCanceledException>(
                    () => graph.Capture(saved, baseline));
                Assert.Equal(AkronSnapshotPacing.CancelledMessage, cancelledCapture.Message);
            } finally {
                AkronSnapshotPacing.Cancelled = false;
                AkronSnapshotPacing.EndPacedWork();
            }

            AkronReconstructionDocument document;
            AkronSnapshotPacing.BeginPacedWork();
            try {
                AkronReconstructionCapture capture = graph.Capture(saved, baseline);
                Assert.True(capture.Success, capture.Error);
                document = capture.Document;
            } finally {
                AkronSnapshotPacing.EndPacedWork();
            }

            AkronSnapshotPacing.BeginPacedWork();
            try {
                AkronSnapshotPacing.Cancelled = true;
                OperationCanceledException cancelledWrite = Assert.Throws<OperationCanceledException>(
                    () => AkronStartPosReconstruction.SaveSnapshot(
                        "cancelled", "Map/A", "room", 0, document, out _, directory));
                Assert.Equal(AkronSnapshotPacing.CancelledMessage, cancelledWrite.Message);
            } finally {
                AkronSnapshotPacing.Cancelled = false;
                AkronSnapshotPacing.EndPacedWork();
            }

            // And the write took its half-finished file with it, cancelled or not.
            Assert.Empty(Directory.GetFiles(directory));
        } finally {
            AkronSnapshotPacing.Cancelled = previousCancelled;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PacingIsInertOnAnyThreadThatIsNotRunningAPacedJob() {
        bool previousActive = AkronSnapshotPacing.GameplayActive;
        bool previousForcedOpen = AkronSnapshotPacing.ForcedOpen;
        try {
            // The game thread also captures graphs. Pacing must never sleep it,
            // which it cannot do because no paced job is in scope here.
            AkronSnapshotPacing.GameplayActive = true;
            AkronSnapshotPacing.ForcedOpen = false;
            Stopwatch timer = Stopwatch.StartNew();
            for (int call = 0; call < 100_000; call++) {
                AkronSnapshotPacing.Pace();
            }
            timer.Stop();
            Assert.True(timer.ElapsedMilliseconds < 1000, timer.ElapsedMilliseconds.ToString());
        } finally {
            AkronSnapshotPacing.GameplayActive = previousActive;
            AkronSnapshotPacing.ForcedOpen = previousForcedOpen;
        }
    }

    [Fact]
    public void PacingRedistributesASnapshotsCostWithoutManufacturingAnyMore() {
        // The whole justification for stopping the worker is that stopping is
        // free: the same job, suspended and resumed repeatedly, must allocate
        // the same number of bytes as one that runs straight through. An earlier
        // in-game reading suggested throttling raised total allocation by 30%,
        // which would have made pacing a net loss. This measures the worker
        // thread itself, which is the only way to answer that without a run
        // length confounding it.
        string directory = Path.Combine(Path.GetTempPath(), "akron-pacing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        bool previousActive = AkronSnapshotPacing.GameplayActive;
        bool previousForcedOpen = AkronSnapshotPacing.ForcedOpen;
        try {
            AkronSnapshotPacing.ForcedOpen = false;
            AkronSnapshotPacing.GameplayActive = false;
            // Warm the reflection and type-name caches so the comparison covers
            // the work, not one-time setup.
            RunPacedSnapshot(directory, "warmup", out _);

            RunPacedSnapshot(directory, "free", out long freeAllocated);

            // Now hold the gate shut and open it in bursts, so the same job
            // stops and restarts many times over.
            AkronSnapshotPacing.GameplayActive = true;
            long suspendedAllocated = 0;
            Stopwatch suspendedTimer = Stopwatch.StartNew();
            Thread worker = new Thread(() => RunPacedSnapshot(directory, "suspended", out suspendedAllocated));
            worker.IsBackground = true;
            worker.Start();
            for (int cycle = 0; cycle < 6 && worker.IsAlive; cycle++) {
                Thread.Sleep(30);
                AkronSnapshotPacing.GameplayActive = false;
                Thread.Sleep(15);
                AkronSnapshotPacing.GameplayActive = true;
            }
            AkronSnapshotPacing.GameplayActive = false;
            Assert.True(worker.Join(TimeSpan.FromSeconds(60)), "the suspended snapshot never finished");
            suspendedTimer.Stop();

            Assert.True(freeAllocated > 0);
            // Five percent covers jitter in the shared reflection caches. A
            // manufactured cost of the size that was reported would be six times
            // this.
            double ratio = suspendedAllocated / (double) freeAllocated;
            Assert.True(ratio < 1.05,
                "suspending the worker allocated " + suspendedAllocated + " bytes against " +
                freeAllocated + " for an uninterrupted run");
            // And it really did stop: the gate was shut for at least six 30 ms
            // stretches, none of which can overlap the work.
            Assert.True(suspendedTimer.ElapsedMilliseconds >= 180,
                "the suspended run took " + suspendedTimer.ElapsedMilliseconds + " ms, so it never waited");
        } finally {
            AkronSnapshotPacing.GameplayActive = previousActive;
            AkronSnapshotPacing.ForcedOpen = previousForcedOpen;
            Directory.Delete(directory, recursive: true);
        }
    }

    // One whole paced job: the capture walk and the snapshot write, both of
    // which call Pace, measured on the thread that runs them.
    private static void RunPacedSnapshot(string directory, string slotName, out long allocatedBytes) {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(_ => false);
        PacingChainNode saved = BuildPacingChain(600, valueOffset: 10);
        PacingChainNode baseline = BuildPacingChain(600, valueOffset: 0);

        long before = GC.GetAllocatedBytesForCurrentThread();
        AkronSnapshotPacing.BeginPacedWork();
        try {
            AkronReconstructionCapture capture = graph.Capture(saved, baseline);
            Assert.True(capture.Success, capture.Error);
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                slotName, "map", "room", 0, capture.Document, out string error, directory), error);
        } finally {
            AkronSnapshotPacing.EndPacedWork();
        }
        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static PacingChainNode BuildPacingChain(int count, int valueOffset) {
        PacingChainNode root = new PacingChainNode { Value = valueOffset };
        PacingChainNode current = root;
        for (int index = 1; index < count; index++) {
            current.Next = new PacingChainNode { Value = valueOffset + index };
            current = current.Next;
        }
        return root;
    }

    private sealed class PacingChainNode {
        public int Value;
        public string Label = new string('n', 64);
        public int[] Payload = new int[32];
        public PacingChainNode Next = null!;
    }

    [Fact]
    public void ThePersistenceWorkerPacesExactlyTheWorkThatBuildsASnapshot() {
        string persistenceSource = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        int worker = persistenceSource.IndexOf("private static void RunWorker()", StringComparison.Ordinal);
        int workerEnd = persistenceSource.IndexOf("private static string BuildBaselineKey(Level level)", worker, StringComparison.Ordinal);
        string workerBody = SourceSlice(persistenceSource, worker, workerEnd - worker);

        // The scope opens before the persist call and closes in a finally, so a
        // throw cannot leave the pacing scope attached to a pooled thread.
        int begin = workerBody.IndexOf("AkronSnapshotPacing.BeginPacedWork();", StringComparison.Ordinal);
        int persist = workerBody.IndexOf("AkronSaveLoadService.PersistRuntimeStateSnapshot(", begin, StringComparison.Ordinal);
        int finallyBlock = workerBody.IndexOf("} finally {", persist, StringComparison.Ordinal);
        int end = workerBody.IndexOf("AkronSnapshotPacing.EndPacedWork();", finallyBlock, StringComparison.Ordinal);
        Assert.True(begin >= 0);
        Assert.True(persist > begin);
        Assert.True(end > finallyBlock);
        Assert.Equal(1, CountOccurrences(workerBody, "AkronSnapshotPacing.BeginPacedWork();"));
        Assert.Equal(1, CountOccurrences(workerBody, "AkronSnapshotPacing.EndPacedWork();"));

        // Shutdown lifts the throttle before it drains the worker, and Start puts
        // it back so an Everest reload does not run permanently unthrottled.
        int shutdown = persistenceSource.IndexOf("public static void Shutdown()", StringComparison.Ordinal);
        int shutdownForcedOpen = persistenceSource.IndexOf("AkronSnapshotPacing.ForcedOpen = true;", shutdown, StringComparison.Ordinal);
        int shutdownDrain = persistenceSource.IndexOf("DrainWorkerForShutdown(runningWorker", shutdown, StringComparison.Ordinal);
        Assert.True(shutdownForcedOpen > shutdown);
        Assert.True(shutdownDrain > shutdownForcedOpen);
        Assert.Contains("AkronSnapshotPacing.ForcedOpen = false;", persistenceSource, StringComparison.Ordinal);

        // The gameplay signal is refreshed from the per-update pump, not from a
        // level-only hook, so leaving a level cannot strand the throttle on.
        int update = persistenceSource.IndexOf("public static void Update()", StringComparison.Ordinal);
        int signal = persistenceSource.IndexOf("AkronSnapshotPacing.GameplayActive =", update, StringComparison.Ordinal);
        int drain = persistenceSource.IndexOf("Completed.TryDequeue(", update, StringComparison.Ordinal);
        Assert.True(signal > update && signal < drain);
    }

    private static int CountOccurrences(string source, string value) {
        int count = 0;
        for (int index = source.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal)) {
            count++;
        }
        return count;
    }

    private static string SourceSlice(string source, int start, int length) {
        Assert.InRange(start, 0, source.Length);
        Assert.InRange(length, 0, source.Length - start);
        return source.Substring(start, length);
    }

    private static string SourceTail(string source, int start) {
        Assert.InRange(start, 0, source.Length);
        return source.Substring(start);
    }

    private static Level CreateLevelWithRendererList(out List<Renderer> renderers) {
        Level level = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level));
        RendererList rendererList = (RendererList) RuntimeHelpers.GetUninitializedObject(typeof(RendererList));
        renderers = new List<Renderer>();
        rendererList.Renderers = renderers;
        SceneRendererListBackingField.SetValue(level, rendererList);
        return level;
    }

    private static TRenderer CreateUninitializedRenderer<TRenderer>() where TRenderer : Renderer {
        return (TRenderer) RuntimeHelpers.GetUninitializedObject(typeof(TRenderer));
    }

    private static ScreenWipe? GetLevelWipe(Level level) {
        return LevelWipeField.GetValue(level) as ScreenWipe;
    }

    private static void SetLevelWipe(Level level, ScreenWipe wipe) {
        LevelWipeField.SetValue(level, wipe);
    }

    private static object InvokeDetachTransientScreenWipes(Level level) {
        return GetTransientScreenWipeMethod("DetachTransientScreenWipes")
            .Invoke(null, new object[] { level })!;
    }

    private static void InvokeRestoreTransientScreenWipes(Level level, object? detached) {
        GetTransientScreenWipeMethod("RestoreTransientScreenWipes")
            .Invoke(null, new[] { level, detached });
    }

    private static MethodInfo GetTransientScreenWipeMethod(string name) {
        return typeof(AkronSaveLoadService).GetMethod(
            name,
            BindingFlags.Static | BindingFlags.NonPublic
        ) ?? throw new MissingMethodException(typeof(AkronSaveLoadService).FullName, name);
    }

    private static string GetActionsSourcePath() {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null) {
            string candidate = Path.Combine(directory.FullName, "Source", "Actions", "akron-startpos-actions.cs");
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Akron repository root.");
    }

    private sealed class RegistrationProbe {
        public static int SharedValue = 1;

        public void Save(Dictionary<Type, Dictionary<string, object>> values, Level level) {
        }

        public void Load(Dictionary<Type, Dictionary<string, object>> values, Level level) {
        }

        public void Clear() {
        }
    }

    private static string GetPlayerRuntimeSourcePath() {
        return GetSourcePath("Module", "akron-module-player-runtime.cs");
    }

    private static string GetSaveLoadSourcePath() {
        return GetSourcePath("SaveLoad", "AkronSaveLoad.cs");
    }

    private static string GetDeepCloneSourcePath() {
        return GetSourcePath("Core", "AkronDeepClone.cs");
    }

    private static string GetEventInstanceSourcePath() {
        return GetSourcePath("Core", "akron-event-instance-utils.cs");
    }

    private static string GetModuleSourcePath() {
        return GetSourcePath("Module", "AkronModule.cs");
    }

    private static string GetQaCommandsSourcePath() {
        return GetSourcePath("Commands", "akron-qa-commands.cs");
    }

    // Anchored on a file this suite already locates rather than on a bare upward walk,
    // so a copy of the same name under bin/ cannot be picked up instead.
    private static string GetRepositoryFilePath(string fileName) {
        string sourceDirectory = Path.GetDirectoryName(Path.GetDirectoryName(GetSaveLoadSourcePath()))!;
        return Path.Combine(Path.GetDirectoryName(sourceDirectory)!, fileName);
    }

    private static string GetSourcePath(string directoryName, string fileName) {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null) {
            string candidate = Path.Combine(directory.FullName, "Source", directoryName, fileName);
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Akron repository root.");
    }

}
