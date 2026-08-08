using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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

    [Fact]
    public void PendingStartPosEntriesAreIsolatedByCelesteFileSlot() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int publish = source.IndexOf("private static void PublishPendingStartPos", StringComparison.Ordinal);
        int publishEnd = source.IndexOf("private static void RemovePendingStartPos", publish, StringComparison.Ordinal);
        int load = source.IndexOf("internal static void LoadStartPositionsForLevel", StringComparison.Ordinal);
        int loadEnd = source.IndexOf("internal static IEnumerable<KeyValuePair<int, AkronStartPos>>", load, StringComparison.Ordinal);
        string publishPath = SourceSlice(source, publish, publishEnd - publish);
        string loadPath = SourceSlice(source, load, loadEnd - load);

        Assert.Contains("BuildPendingStartPosKey(fileSlot, areaSid)", publishPath);
        Assert.Contains("BuildPendingStartPosKey(GetCurrentFileSlot(), areaSid)", loadPath);
    }

    [Fact]
    public void BackgroundStartPosCompletionRequiresTheOriginatingSaveFile() {
        string actionsSource = File.ReadAllText(GetActionsSourcePath());
        string persistenceSource = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        int completion = actionsSource.IndexOf("internal static void CompletePersistentStartPosCapture", StringComparison.Ordinal);
        int completionEnd = actionsSource.IndexOf("private static void ApplyPlacedStartPosBeforeCapture", completion, StringComparison.Ordinal);
        string completionPath = SourceSlice(actionsSource, completion, completionEnd - completion);

        Assert.Contains("IsOriginatingSaveFileActive(fileSlot, saveData)", completionPath);
        Assert.Contains("PersistStartPos(slot, startPos, fileSlot, saveData)", completionPath);
        Assert.Contains("FileSlot = fileSlot", persistenceSource);
        Assert.Contains("SaveData = saveData", persistenceSource);
        Assert.Contains("completion.Job.FileSlot", persistenceSource);
        Assert.Contains("completion.Job.SaveData", persistenceSource);
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
        int cancel = replacePath.IndexOf("AkronStartPosPersistence.Cancel", validation, StringComparison.Ordinal);
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

    [Fact]
    public void NormalRoomLoadsRefreshTheSameRoomFreshBaseline() {
        string source = File.ReadAllText(GetSourcePath("Actions", "akron-startpos-persistence.cs"));
        int notify = source.IndexOf("public static void NotifyLevelReady", StringComparison.Ordinal);
        int notifyEnd = source.IndexOf("public static long Enqueue", notify, StringComparison.Ordinal);
        string notifyPath = SourceSlice(source, notify, notifyEnd - notify);
        int loadHook = source.IndexOf("private static void LevelOnLoadLevel", StringComparison.Ordinal);
        int capture = source.IndexOf("private static void CaptureFreshBaseline", loadHook, StringComparison.Ordinal);
        string loadHookPath = SourceSlice(source, loadHook, capture - loadHook);

        Assert.Contains("bool refreshBaseline = false", notifyPath);
        Assert.Contains("if (refreshBaseline)", notifyPath);
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
    public void FreshBaselineCaptureExcludesTheTransientEntryWipe() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int capture = source.IndexOf("internal static AkronSaveLoadSlotLease CaptureFreshRuntimeState", StringComparison.Ordinal);
        int captureEnd = source.IndexOf("internal static IReadOnlyList<string> GetRegisteredActionIdsForPersistence", capture, StringComparison.Ordinal);
        string capturePath = SourceSlice(source, capture, captureEnd - capture);

        Assert.Contains("ScreenWipe entryWipe = level.Wipe", capturePath);
        Assert.Contains("level.Wipe = null", capturePath);
        Assert.Contains("level.RendererList.Renderers.RemoveAt", capturePath);
        Assert.Contains("level.Wipe = entryWipe", capturePath);
        Assert.Contains("level.RendererList.Renderers.Insert", capturePath);
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
        int wait = persistenceSource.IndexOf("runningWorker?.GetAwaiter().GetResult();", shutdown, StringComparison.Ordinal);
        int update = persistenceSource.IndexOf("Update();", wait, StringComparison.Ordinal);
        int worker = persistenceSource.IndexOf("private static void RunWorker", update, StringComparison.Ordinal);
        int empty = persistenceSource.IndexOf("if (Ready.Count == 0)", worker, StringComparison.Ordinal);
        int dequeue = persistenceSource.IndexOf("job = Ready.Dequeue();", empty, StringComparison.Ordinal);

        Assert.True(wait > shutdown);
        Assert.True(update > wait);
        Assert.True(empty > worker && dequeue > empty);
        Assert.DoesNotContain("shutdown started before the restart copy finished", persistenceSource);
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
    public void StartPosCapturesRenderedProcessBuffersAtTheSetBoundary() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int captureStart = source.IndexOf("public static AkronSaveLoadSlot CaptureRuntimeState", StringComparison.Ordinal);
        int bufferCapture = source.IndexOf("AkronGameplayBufferState.Capture()", captureStart, StringComparison.Ordinal);
        int beforeSaveActions = source.IndexOf("action.BeforeSaveState?.Invoke(level);", captureStart, StringComparison.Ordinal);
        int roomClone = source.IndexOf("BuildNativeSlot(level, CurrentSlotName", captureStart, StringComparison.Ordinal);
        int restoreStart = source.IndexOf("private static AkronSaveLoadResult RestorePersistentRuntimeState", StringComparison.Ordinal);
        int bufferRestore = source.IndexOf("AkronGameplayBufferState.Restore(document.GameplayBuffers", restoreStart, StringComparison.Ordinal);

        Assert.True(captureStart >= 0);
        Assert.True(bufferCapture > captureStart);
        Assert.True(beforeSaveActions > bufferCapture);
        Assert.True(roomClone > beforeSaveActions);
        Assert.True(bufferRestore > restoreStart);
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

        int restore = method.IndexOf("AkronSaveLoadService.LoadRuntimeState", StringComparison.Ordinal);
        int registryReload = method.IndexOf("LoadStartPositionsForLevel(currentLevel);", StringComparison.Ordinal);
        int loadedSlotUpdate = method.IndexOf("AkronModule.Session.LastLoadedStartPosSlot = loadedSlot;", StringComparison.Ordinal);

        Assert.True(restore >= 0);
        Assert.True(registryReload > restore);
        Assert.True(loadedSlotUpdate > registryReload);
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
        Assert.Contains("nextIdlePollFrame = Engine.FrameCounter;", automationSource);
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
        int persist = source.IndexOf("PersistStartPos(slot, startPos, fileSlot, saveData)", install, StringComparison.Ordinal);
        int commit = source.IndexOf("installedSnapshot.Commit()", persist, StringComparison.Ordinal);

        Assert.True(capture >= 0 && prepare > capture);
        Assert.True(install > prepare && persist > install && commit > persist);
        Assert.Contains("if (!PersistStartPos(slot, startPos, fileSlot, saveData))", SourceSlice(source, persist - 16, 128));
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
        string pendingKey = "-1|Map/A";
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
        string pendingKey = fileSlot.ToString() + "|" + areaSid;
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
            [slot] = new AkronStartPos { AreaSid = areaSid, Room = "room" }
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
        string pendingKey = fileSlot.ToString() + "|" + areaSid;
        AkronStartPos pendingStartPos = new AkronStartPos {
            AreaSid = areaSid,
            Room = "room",
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

        int successfulRestore = actionsSource.IndexOf("restoredStartPos = true;", restoreNotification, StringComparison.Ordinal);
        Assert.True(successfulRestore > restoreNotification);

        int persistedStartPos = actionsSource.IndexOf("PersistStartPos(slot, startPos, fileSlot, saveData)", StringComparison.Ordinal);
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
    public void LoadingStartPosArmsNativeDeathReloadAfterSuccessfulRestore() {
        string source = File.ReadAllText(GetActionsSourcePath());
        string playerRuntimeSource = File.ReadAllText(GetPlayerRuntimeSourcePath());

        Assert.Contains("enableRespawnAtStartPosAfterRestore: true", source);
        Assert.Contains("enableRespawnAtStartPosAfterRestore && restoredStartPos", source);
        Assert.Contains("if (loadedSlot > 0)", source);
        Assert.Contains("AkronModule.Session.LastLoadedStartPosSlot = loadedSlot;", source);
        Assert.Contains("RestoreStartPosAfterDeath(Level level, AkronStartPos startPos)", source);
        Assert.Contains("deadBody.DeathAction = () =>", playerRuntimeSource);
        Assert.Contains("deadBody.DeathAction == null", playerRuntimeSource);
        Assert.Contains("!deadBody.HasGolden", playerRuntimeSource);
        Assert.Contains("if (Engine.Scene != level)", source);
        Assert.Contains("SpotlightWipe.FocusPoint = respawnPoint - restoredLevel.Camera.Position;", source);
        Assert.Contains("restoredLevel.DoScreenWipe(wipeIn: true);", source);
        Assert.Contains("level.Reload();", source);
        Assert.Equal(1, playerRuntimeSource.Split("AkronActions.RestoreStartPosAfterDeath(level, startPosRespawn)").Length - 1);
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

    private static string SourceSlice(string source, int start, int length) {
        Assert.InRange(start, 0, source.Length);
        Assert.InRange(length, 0, source.Length - start);
        return source.Substring(start, length);
    }

    private static string SourceTail(string source, int start) {
        Assert.InRange(start, 0, source.Length);
        return source.Substring(start);
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
