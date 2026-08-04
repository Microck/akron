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
    public void StartPosRestartUsesTheExactReconstructionSnapshot() {
        string source = File.ReadAllText(GetActionsSourcePath());
        string saveLoadSource = File.ReadAllText(GetSaveLoadSourcePath());

        Assert.DoesNotContain("AkronPersistentStartPosSnapshots", source);
        Assert.Contains("PersistRuntimeStateSnapshot", source);
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
    public void StartPosCaptureBuildsTheDiskSnapshotAfterTheEngineUpdateBoundary() {
        string source = File.ReadAllText(GetActionsSourcePath());
        string moduleSource = File.ReadAllText(GetModuleSourcePath());

        int schedule = source.IndexOf("SchedulePersistentStartPosCapture(", StringComparison.Ordinal);
        int setFrame = source.IndexOf("StartPosFrameGeneration++;", schedule, StringComparison.Ordinal);
        int firstSafeBoundary = source.IndexOf("AkronModule.ScheduleAfterStableEngineUpdate(() =>", schedule, StringComparison.Ordinal);
        int reload = source.IndexOf("level.Reload();", firstSafeBoundary, StringComparison.Ordinal);
        int secondSafeBoundary = source.IndexOf("Action finishCapture = () =>", reload, StringComparison.Ordinal);
        int secondSceneGuard = source.IndexOf("if (Engine.Scene != level)", secondSafeBoundary, StringComparison.Ordinal);
        int persist = source.IndexOf("PersistRuntimeStateSnapshot(", reload, StringComparison.Ordinal);
        int scheduleFinishCapture = source.IndexOf("AkronModule.ScheduleAfterStableEngineUpdate(finishCapture);", persist, StringComparison.Ordinal);
        int engineUpdate = moduleSource.IndexOf("private static void EngineOnUpdate", StringComparison.Ordinal);
        int renderCore = moduleSource.IndexOf("private static void EngineOnRenderCore", StringComparison.Ordinal);
        int drain = moduleSource.IndexOf("RunAfterEngineUpdateActions();", renderCore, StringComparison.Ordinal);
        int render = moduleSource.IndexOf("orig(self);", renderCore, StringComparison.Ordinal);

        Assert.True(schedule >= 0);
        Assert.True(setFrame > schedule);
        Assert.True(firstSafeBoundary > schedule);
        Assert.True(reload > firstSafeBoundary);
        Assert.True(secondSafeBoundary > reload);
        Assert.True(secondSceneGuard > secondSafeBoundary);
        Assert.True(secondSceneGuard < persist);
        Assert.True(persist > secondSafeBoundary);
        Assert.True(scheduleFinishCapture > persist);
        Assert.True(engineUpdate >= 0);
        Assert.True(renderCore > engineUpdate);
        Assert.True(drain > renderCore);
        Assert.True(render > drain);
        Assert.DoesNotContain("RunAfterEngineUpdateActions();", SourceSlice(moduleSource, engineUpdate, renderCore - engineUpdate));
        Assert.DoesNotContain("level.OnEndOfFrame += () =>", SourceSlice(source, schedule, persist - schedule));
    }

    [Fact]
    public void PersistentRestoreDoesNotReplaceGlobalSaveData() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        string modelsSource = File.ReadAllText(GetSourcePath("SaveLoad", "akron-save-load-models.cs"));
        int persistStart = source.IndexOf("public static AkronSaveLoadResult PersistRuntimeStateSnapshot", StringComparison.Ordinal);
        int persistEnd = source.IndexOf("public static AkronSaveLoadResult RestoreRuntimeState", persistStart, StringComparison.Ordinal);
        int restoreStart = source.IndexOf("private static AkronSaveLoadResult RestorePersistentRuntimeState", StringComparison.Ordinal);
        int restoreEnd = source.IndexOf("private static Dictionary<string", restoreStart, StringComparison.Ordinal);
        int persistentModelStart = modelsSource.IndexOf("internal sealed class AkronPersistentRuntimeState", StringComparison.Ordinal);
        string persistMethod = SourceSlice(source, persistStart, persistEnd - persistStart);
        string restoreMethod = SourceSlice(source, restoreStart, restoreEnd - restoreStart);
        string persistentModel = SourceTail(modelsSource, persistentModelStart);

        Assert.Contains("AkronPersistentRuntimeState.CaptureSaved", persistMethod);
        Assert.Contains("AkronPersistentRuntimeState.CaptureCurrent", persistMethod);
        Assert.Contains("AkronPersistentRuntimeState.CaptureCurrent", restoreMethod);
        Assert.Contains("ApplyPersistentRuntimeState", restoreMethod);
        Assert.DoesNotContain("SaveDataState", persistentModel);
        Assert.DoesNotContain("ModuleSaveData", persistentModel);
        Assert.DoesNotContain("SaveData.Instance =", restoreMethod);
        Assert.DoesNotContain("module._SaveData =", restoreMethod);
    }

    [Fact]
    public void StartPosCaptureCommitsItsDiskSnapshotOnlyAfterRuntimeRestoreSucceeds() {
        string actionsSource = File.ReadAllText(GetActionsSourcePath());
        int capture = actionsSource.IndexOf("private static void SchedulePersistentStartPosCapture", StringComparison.Ordinal);
        int persist = actionsSource.IndexOf("persistResult = AkronSaveLoadService.PersistRuntimeStateSnapshot(", capture, StringComparison.Ordinal);
        int stagingArgument = actionsSource.IndexOf("stagingDirectory);", persist, StringComparison.Ordinal);
        int restore = actionsSource.IndexOf("LoadRuntimeState(level, stateSlotName", persist, StringComparison.Ordinal);
        int install = actionsSource.IndexOf("AkronStartPosReconstruction.PrepareSnapshotInstall(", restore, StringComparison.Ordinal);
        int installSlot = actionsSource.IndexOf("stateSlotName,", install, StringComparison.Ordinal);
        int installDirectory = actionsSource.IndexOf("stagingDirectory);", installSlot, StringComparison.Ordinal);
        int metadata = actionsSource.IndexOf("AkronModule.Session.StartPositions[slot] = startPos;", install, StringComparison.Ordinal);

        Assert.True(capture >= 0);
        Assert.True(persist > capture);
        Assert.True(stagingArgument > persist);
        Assert.True(restore > persist);
        Assert.True(install > restore);
        Assert.True(installSlot > install);
        Assert.True(installDirectory > installSlot);
        Assert.True(metadata > install);
    }

    [Fact]
    public void SuccessfulStartPosCaptureDiscardsItsTransientRuntimeStateAfterDiskCommit() {
        string actionsSource = File.ReadAllText(GetActionsSourcePath());
        int capture = actionsSource.IndexOf("private static void SchedulePersistentStartPosCapture", StringComparison.Ordinal);
        int commit = actionsSource.IndexOf("installedSnapshot.Commit();", capture, StringComparison.Ordinal);
        int discard = actionsSource.IndexOf("AkronSaveLoadService.DiscardRuntimeStateMemory(stateSlotName);", commit, StringComparison.Ordinal);
        int publish = actionsSource.IndexOf("AkronModule.Session.StartPositions[slot] = startPos;", commit, StringComparison.Ordinal);

        Assert.True(capture >= 0);
        Assert.True(commit > capture);
        Assert.True(discard > commit);
        Assert.True(publish > discard);
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
    public void SetStartPosPresentsTheSavedBufferWhileTheFreshRoomInitializes() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int capture = source.IndexOf("private static void SchedulePersistentStartPosCapture", StringComparison.Ordinal);
        int reload = source.IndexOf("level.Reload();", capture, StringComparison.Ordinal);
        int getSavedState = source.IndexOf("GetRuntimeStateForDebug(stateSlotName)", reload, StringComparison.Ordinal);
        int armPresentation = source.IndexOf("AkronGameplayBufferState.ArmLevelPresentation", getSavedState, StringComparison.Ordinal);
        int nextStableUpdate = source.IndexOf("AkronModule.ScheduleAfterStableEngineUpdate", armPresentation, StringComparison.Ordinal);

        Assert.True(capture >= 0 && reload > capture);
        Assert.True(getSavedState > reload && armPresentation > getSavedState);
        Assert.True(nextStableUpdate > armPresentation);
    }

    [Fact]
    public void FailedFreshRoomSetupRestoresTheCapturedRoomBeforeDiscardingIt() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int capture = source.IndexOf("private static void SchedulePersistentStartPosCapture", StringComparison.Ordinal);
        int reload = source.IndexOf("level.Reload();", capture, StringComparison.Ordinal);
        int rollback = source.IndexOf("AkronSaveLoadService.LoadRuntimeState(", reload, StringComparison.Ordinal);
        int discard = source.IndexOf("AkronSaveLoadService.DiscardRuntimeStateMemory(stateSlotName);", rollback, StringComparison.Ordinal);

        Assert.True(capture >= 0 && reload > capture);
        Assert.True(rollback > reload && discard > rollback);
    }

    [Fact]
    public void StartPosCaptureBlocksOverlappingSetAndLoadUntilTheOriginalStateReturns() {
        string source = File.ReadAllText(GetActionsSourcePath());
        int captureStart = source.IndexOf("private static void CaptureStartPos", StringComparison.Ordinal);
        int captureEnd = source.IndexOf("private static void SchedulePersistentStartPosCapture", captureStart, StringComparison.Ordinal);
        string captureMethod = SourceSlice(source, captureStart, captureEnd - captureStart);
        int busyCheck = captureMethod.IndexOf("if (startPosCaptureInProgress)", StringComparison.Ordinal);
        int begin = captureMethod.IndexOf("startPosCaptureInProgress = true;", busyCheck, StringComparison.Ordinal);
        int save = captureMethod.IndexOf("SaveRuntimeState", begin, StringComparison.Ordinal);

        Assert.True(busyCheck >= 0 && begin > busyCheck && save > begin);

        int scheduleStart = captureEnd;
        int scheduleEnd = source.IndexOf("private static void ApplyPlacedStartPosBeforeCapture", scheduleStart, StringComparison.Ordinal);
        string scheduleMethod = SourceSlice(source, scheduleStart, scheduleEnd - scheduleStart);
        int secondBoundary = scheduleMethod.IndexOf("Action finishCapture = () =>", StringComparison.Ordinal);
        int restore = scheduleMethod.IndexOf("LoadRuntimeState(level, stateSlotName", secondBoundary, StringComparison.Ordinal);
        int release = scheduleMethod.LastIndexOf("startPosCaptureInProgress = false;", StringComparison.Ordinal);

        Assert.True(secondBoundary >= 0 && restore > secondBoundary && release > restore);

        int loadStart = source.IndexOf("public static void LoadStartPos(Level level)", StringComparison.Ordinal);
        int loadEnd = source.IndexOf("public static void LoadStartPosSlot", loadStart, StringComparison.Ordinal);
        string loadMethod = SourceSlice(source, loadStart, loadEnd - loadStart);
        Assert.Equal(2, loadMethod.Split("startPosCaptureInProgress", StringSplitOptions.None).Length - 1);
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
        int nextMethod = source.IndexOf("private static Dictionary<string", restore, StringComparison.Ordinal);
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
        int capture = source.IndexOf("private static void SchedulePersistentStartPosCapture", StringComparison.Ordinal);
        int prepare = source.IndexOf("PrepareSnapshotInstall", capture, StringComparison.Ordinal);
        int install = source.IndexOf("installedSnapshot.Install", prepare, StringComparison.Ordinal);
        int persist = source.IndexOf("PersistStartPos(slot, startPos)", install, StringComparison.Ordinal);
        int commit = source.IndexOf("installedSnapshot.Commit()", persist, StringComparison.Ordinal);

        Assert.True(capture >= 0 && prepare > capture);
        Assert.True(install > prepare && persist > install && commit > persist);
        Assert.Contains("if (!PersistStartPos(slot, startPos))", SourceSlice(source, persist - 16, 96));
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
    }

    [Fact]
    public void FailedPersistentRestoreReloadsThePreLoadRuntimeState() {
        string source = File.ReadAllText(GetSaveLoadSourcePath());
        int restore = source.IndexOf("private static AkronSaveLoadResult RestorePersistentRuntimeState(", StringComparison.Ordinal);
        int captureRollback = source.IndexOf("CaptureRuntimeState(level, rollbackSlotName", restore, StringComparison.Ordinal);
        int restoreCore = source.IndexOf("RestorePersistentRuntimeStateCore(level, document)", captureRollback, StringComparison.Ordinal);
        int restoreRollback = source.IndexOf("RestoreRuntimeState(level, rollbackSlot", restoreCore, StringComparison.Ordinal);
        int discardRollback = source.IndexOf("ReleaseDormantEventInstances(rollbackSlot)", restoreRollback, StringComparison.Ordinal);

        Assert.True(restore >= 0 && captureRollback > restore);
        Assert.True(restoreCore > captureRollback && restoreRollback > restoreCore && discardRollback > restoreRollback);
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

        int persistedStartPos = actionsSource.IndexOf("PersistStartPos(slot, startPos)", StringComparison.Ordinal);
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

        Assert.Contains("savedSession.Time = Math.Max(currentSessionTime, savedSession.Time);", source);
        Assert.Contains("savedSession.Deaths = Math.Max(currentDeaths, savedSession.Deaths);", source);
        Assert.Contains("savedSession.DeathsInCurrentLevel = Math.Max(currentDeathsInRoom, savedSession.DeathsInCurrentLevel);", source);
        Assert.Contains("savedSaveData.Time = Math.Max(currentSaveDataTime, savedSaveData.Time);", source);
        Assert.Contains("savedSaveData.TotalDeaths = Math.Max(currentTotalDeaths, savedSaveData.TotalDeaths);", source);
        Assert.Contains("Math.Max(currentAreaTimePlayed", source);
        Assert.Contains("Math.Max(currentAreaDeaths", source);
        Assert.Contains("level.TimeActive = saveSlot.LevelTimeActive;", source);
        Assert.Contains("level.RawTimeActive = saveSlot.LevelRawTimeActive;", source);
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
