using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Celeste;
using Celeste.Mod;
using Monocle;

namespace Celeste.Mod.Akron;

// StartPos has two storage tiers. The exact native clone serves Set and every
// same-process Load. A single worker converts that immutable clone to the
// restart-safe graph without freezing the game thread.
internal static class AkronStartPosPersistence {
    private static readonly object Sync = new object();
    private static readonly Queue<PersistenceJob> Ready = new Queue<PersistenceJob>();
    private static readonly List<PersistenceJob> WaitingForBaseline = new List<PersistenceJob>();
    private static readonly ConcurrentQueue<PersistenceCompletion> Completed =
        new ConcurrentQueue<PersistenceCompletion>();
    private static readonly Dictionary<string, long> LatestGenerations =
        new Dictionary<string, long>(StringComparer.Ordinal);
    private static readonly Dictionary<string, AkronSaveLoadSlotLease> FreshBaselines =
        new Dictionary<string, AkronSaveLoadSlotLease>(StringComparer.Ordinal);
    // Each warm StartPos keeps the fresh graph that was used to build its disk
    // snapshot. The entry follows the runtime slot lifetime, so a cross-room warm
    // restore can reuse a true fresh baseline without retaining every visited room.
    private static readonly Dictionary<string, AkronSaveLoadSlotLease> RuntimeFreshBaselines =
        new Dictionary<string, AkronSaveLoadSlotLease>(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> PendingBaselineGenerations =
        new Dictionary<string, long>(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> PendingBaselineInitializationGenerations =
        new Dictionary<string, long>(StringComparer.Ordinal);

    private static Task workerTask;
    private static long nextGeneration;
    private static long nextBaselineGeneration;
    private static int suppressBaselineCapture;
    private static bool started;
    private static bool shuttingDown;

    public static void Start() {
        lock (Sync) {
            if (started) {
                return;
            }
            started = true;
            shuttingDown = false;
        }
        On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
    }

    public static void NotifyLevelReady(Level level, bool refreshBaseline = false) {
        if (level == null || !started || suppressBaselineCapture > 0) {
            return;
        }

        string expectedKey = BuildBaselineKey(level);
        long captureGeneration;
        lock (Sync) {
            // A baseline is only useful while its room is current. Each queued or
            // running job retains its own lease, so dropping the cache here cannot
            // invalidate work that already started.
            EvictOtherBaselinesLocked(expectedKey);
            FailWaitingJobsExceptLocked(
                expectedKey,
                "the room changed before its fresh-room baseline was ready");
            if (refreshBaseline) {
                FailWaitingJobsForBaselineLocked(
                    expectedKey,
                    "the room reloaded before its fresh-room baseline was ready");
                if (FreshBaselines.Remove(expectedKey, out AkronSaveLoadSlotLease staleBaseline)) {
                    staleBaseline.Dispose();
                }
            } else if (FreshBaselines.ContainsKey(expectedKey) ||
                       PendingBaselineGenerations.ContainsKey(expectedKey)) {
                return;
            }

            captureGeneration = ++nextBaselineGeneration;
            PendingBaselineGenerations[expectedKey] = captureGeneration;
            PendingBaselineInitializationGenerations[expectedKey] = captureGeneration;
        }

        AkronModule.ScheduleAfterStableEngineUpdate(
            () => CaptureFreshBaseline(level, expectedKey, captureGeneration));
    }

    public static bool ConsumeFreshBaselineInitializationUpdate(Level level) {
        string key = BuildBaselineKey(level);
        lock (Sync) {
            if (!PendingBaselineGenerations.TryGetValue(key, out long captureGeneration) ||
                !PendingBaselineInitializationGenerations.TryGetValue(key, out long initializationGeneration) ||
                captureGeneration != initializationGeneration) {
                return false;
            }

            PendingBaselineInitializationGenerations.Remove(key);
            return true;
        }
    }

    public static bool IsFreshBaselineCapturePending(Level level) {
        string key = BuildBaselineKey(level);
        lock (Sync) {
            if (!PendingBaselineGenerations.TryGetValue(key, out long captureGeneration)) {
                return false;
            }

            // The first room update must run before capture. Once that update is
            // consumed, hold later fixed-timestep updates until the render-boundary
            // capture completes so lag catch-up cannot move the baseline forward.
            return !PendingBaselineInitializationGenerations.TryGetValue(key, out long initializationGeneration) ||
                   initializationGeneration != captureGeneration;
        }
    }

    public static long Enqueue(
        int fileSlot,
        AkronModuleSaveData saveData,
        int slot,
        AkronStartPos startPos,
        string stateSlotName
    ) {
        if (saveData == null) {
            return 0;
        }
        AkronSaveLoadSlotLease savedState = AkronSaveLoadService.RetainRuntimeState(stateSlotName);
        if (savedState?.Slot == null) {
            savedState?.Dispose();
            return 0;
        }

        PersistenceJob job = new PersistenceJob {
            FileSlot = fileSlot,
            SaveData = saveData,
            Slot = slot,
            StartPos = startPos,
            StateSlotName = stateSlotName,
            Generation = ++nextGeneration,
            SavedState = savedState,
            RegisteredActionIds = AkronSaveLoadService.GetRegisteredActionIdsForPersistence()
        };
        string baselineKey = BuildBaselineKey(savedState.Slot);

        lock (Sync) {
            LatestGenerations[stateSlotName] = job.Generation;
            if (FreshBaselines.TryGetValue(baselineKey, out AkronSaveLoadSlotLease baseline)) {
                job.FreshBaseline = baseline.Retain();
                AttachRuntimeFreshBaselineLocked(job.StateSlotName, baseline);
                Ready.Enqueue(job);
                StartWorkerLocked();
            } else if (PendingBaselineGenerations.ContainsKey(baselineKey)) {
                WaitingForBaseline.Add(job);
            } else {
                Completed.Enqueue(new PersistenceCompletion(
                    job,
                    AkronSaveLoadResult.Failed,
                    "fresh-room baseline is unavailable until the room is loaded normally",
                    string.Empty,
                    TimeSpan.Zero));
            }
        }
        return job.Generation;
    }

    public static void AttachRuntimeFreshBaseline(
        string stateSlotName,
        AkronSaveLoadSlotLease baseline
    ) {
        if (string.IsNullOrWhiteSpace(stateSlotName) || baseline?.Slot == null) {
            return;
        }
        lock (Sync) {
            AttachRuntimeFreshBaselineLocked(stateSlotName, baseline);
        }
    }

    public static void UseRuntimeFreshBaseline(string stateSlotName) {
        if (string.IsNullOrWhiteSpace(stateSlotName)) {
            return;
        }
        lock (Sync) {
            if (!RuntimeFreshBaselines.TryGetValue(stateSlotName, out AkronSaveLoadSlotLease runtimeBaseline) ||
                runtimeBaseline?.Slot == null) {
                return;
            }

            string baselineKey = BuildBaselineKey(runtimeBaseline.Slot);
            AkronSaveLoadSlotLease currentBaseline = runtimeBaseline.Retain();
            EvictOtherBaselinesLocked(baselineKey);
            if (FreshBaselines.Remove(baselineKey, out AkronSaveLoadSlotLease previousBaseline)) {
                previousBaseline.Dispose();
            }
            FreshBaselines[baselineKey] = currentBaseline;
            PendingBaselineGenerations.Remove(baselineKey);
            PendingBaselineInitializationGenerations.Remove(baselineKey);
            FailWaitingJobsExceptLocked(
                baselineKey,
                "the room changed before its fresh-room baseline was ready");
            QueueWaitingJobsForBaselineLocked(baselineKey, currentBaseline);
            StartWorkerLocked();
        }
    }

    public static void RemoveRuntimeFreshBaseline(string stateSlotName) {
        if (string.IsNullOrWhiteSpace(stateSlotName)) {
            return;
        }
        lock (Sync) {
            if (RuntimeFreshBaselines.Remove(stateSlotName, out AkronSaveLoadSlotLease baseline)) {
                baseline.Dispose();
            }
        }
    }

    public static void ClearRuntimeFreshBaselines() {
        lock (Sync) {
            foreach (AkronSaveLoadSlotLease baseline in RuntimeFreshBaselines.Values) {
                baseline.Dispose();
            }
            RuntimeFreshBaselines.Clear();
        }
    }

    public static void Cancel(string stateSlotName) {
        if (string.IsNullOrWhiteSpace(stateSlotName)) {
            return;
        }
        lock (Sync) {
            LatestGenerations[stateSlotName] = ++nextGeneration;
        }
    }

    public static bool IsCurrent(string stateSlotName, long generation) {
        lock (Sync) {
            return LatestGenerations.TryGetValue(stateSlotName, out long current) && current == generation;
        }
    }

    public static void Update() {
        while (Completed.TryDequeue(out PersistenceCompletion completion)) {
            try {
                if (IsCurrent(completion.Job.StateSlotName, completion.Job.Generation)) {
                    AkronActions.CompletePersistentStartPosCapture(
                        completion.Job.FileSlot,
                        completion.Job.SaveData,
                        completion.Job.Slot,
                        completion.Job.StartPos,
                        completion.Job.StateSlotName,
                        completion.Job.Generation,
                        completion.Result,
                        completion.Error,
                        completion.StagingDirectory,
                        completion.Elapsed);
                }
            } catch (Exception exception) {
                // Completion applies file and metadata changes on the game
                // thread. One failure must not strand later leases or prevent
                // Shutdown from saving metadata and removing its hook.
                Logger.Log(LogLevel.Warn, nameof(AkronStartPosPersistence),
                    "Could not apply a StartPos restart copy: " + exception);
            } finally {
                completion.Job.Dispose();
                DeleteStagingDirectory(completion.StagingDirectory);
            }
        }
    }

    public static IDisposable SuppressBaselineCapture() {
        suppressBaselineCapture++;
        return new BaselineCaptureSuppression();
    }

    public static void Shutdown() {
        Task runningWorker;
        lock (Sync) {
            if (!started) {
                return;
            }
            shuttingDown = true;
            foreach (PersistenceJob waiting in WaitingForBaseline) {
                Completed.Enqueue(new PersistenceCompletion(
                    waiting,
                    AkronSaveLoadResult.Failed,
                    "fresh-room baseline was not ready before shutdown",
                    string.Empty,
                    TimeSpan.Zero));
            }
            WaitingForBaseline.Clear();
            runningWorker = workerTask;
        }

        runningWorker?.GetAwaiter().GetResult();
        Update();
        AkronActions.SaveAkronStartPosData();
        On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;

        lock (Sync) {
            foreach (AkronSaveLoadSlotLease baseline in FreshBaselines.Values) {
                baseline.Dispose();
            }
            FreshBaselines.Clear();
            foreach (AkronSaveLoadSlotLease baseline in RuntimeFreshBaselines.Values) {
                baseline.Dispose();
            }
            RuntimeFreshBaselines.Clear();
            PendingBaselineGenerations.Clear();
            PendingBaselineInitializationGenerations.Clear();
            LatestGenerations.Clear();
            started = false;
            shuttingDown = false;
            workerTask = null;
        }
    }

    private static void LevelOnLoadLevel(
        On.Celeste.Level.orig_LoadLevel orig,
        Level self,
        Player.IntroTypes playerIntro,
        bool isFromLoader
    ) {
        orig(self, playerIntro, isFromLoader);
        NotifyLevelReady(self, refreshBaseline: true);
    }

    private static void CaptureFreshBaseline(
        Level level,
        string expectedKey,
        long captureGeneration
    ) {
        if (Engine.Scene != level || suppressBaselineCapture > 0 ||
            !string.Equals(BuildBaselineKey(level), expectedKey, StringComparison.Ordinal)) {
            lock (Sync) {
                if (!RemovePendingBaselineGenerationLocked(expectedKey, captureGeneration)) {
                    return;
                }
                FailWaitingJobsForBaselineLocked(
                    expectedKey,
                    "fresh-room baseline became unavailable before capture");
            }
            return;
        }

        lock (Sync) {
            if (!IsPendingBaselineGenerationLocked(expectedKey, captureGeneration)) {
                return;
            }
            if (PendingBaselineInitializationGenerations.TryGetValue(expectedKey, out long initializationGeneration) &&
                initializationGeneration == captureGeneration) {
                AkronModule.ScheduleAfterStableEngineUpdate(
                    () => CaptureFreshBaseline(level, expectedKey, captureGeneration));
                return;
            }
            if (shuttingDown) {
                PendingBaselineGenerations.Remove(expectedKey);
                PendingBaselineInitializationGenerations.Remove(expectedKey);
                FailWaitingJobsForBaselineLocked(
                    expectedKey,
                    "fresh-room baseline was interrupted by shutdown");
                return;
            }
            if (FreshBaselines.TryGetValue(expectedKey, out AkronSaveLoadSlotLease existingBaseline)) {
                PendingBaselineGenerations.Remove(expectedKey);
                PendingBaselineInitializationGenerations.Remove(expectedKey);
                QueueWaitingJobsForBaselineLocked(expectedKey, existingBaseline);
                StartWorkerLocked();
                return;
            }
        }

        Stopwatch timer = Stopwatch.StartNew();
        AkronSaveLoadSlotLease baseline = null;
        try {
            baseline = AkronSaveLoadService.CaptureFreshRuntimeState(
                level,
                "Akron fresh-room baseline " + expectedKey);
            if (baseline?.Slot == null) {
                baseline?.Dispose();
                lock (Sync) {
                    if (RemovePendingBaselineGenerationLocked(expectedKey, captureGeneration)) {
                        FailWaitingJobsForBaselineLocked(
                            expectedKey,
                            "fresh-room baseline capture returned no state");
                    }
                }
                return;
            }

            lock (Sync) {
                if (!IsPendingBaselineGenerationLocked(expectedKey, captureGeneration)) {
                    baseline.Dispose();
                    return;
                }
                if (shuttingDown) {
                    PendingBaselineGenerations.Remove(expectedKey);
                    PendingBaselineInitializationGenerations.Remove(expectedKey);
                    baseline.Dispose();
                    return;
                }
                if (FreshBaselines.TryGetValue(
                        expectedKey,
                        out AkronSaveLoadSlotLease installedBaseline)) {
                    // A warm restore installed this room's baseline while the
                    // game-thread clone ran. Use it for jobs queued during that
                    // window instead of leaving their leases stranded.
                    PendingBaselineGenerations.Remove(expectedKey);
                    PendingBaselineInitializationGenerations.Remove(expectedKey);
                    baseline.Dispose();
                    QueueWaitingJobsForBaselineLocked(expectedKey, installedBaseline);
                    StartWorkerLocked();
                    return;
                }
                FreshBaselines[expectedKey] = baseline;
                PendingBaselineGenerations.Remove(expectedKey);
                PendingBaselineInitializationGenerations.Remove(expectedKey);
                QueueWaitingJobsForBaselineLocked(expectedKey, baseline);
                StartWorkerLocked();
            }
        } catch (Exception exception) {
            baseline?.Dispose();
            lock (Sync) {
                if (RemovePendingBaselineGenerationLocked(expectedKey, captureGeneration)) {
                    FailWaitingJobsForBaselineLocked(
                        expectedKey,
                        "fresh-room baseline capture failed: " + exception.GetType().Name + ": " + exception.Message);
                }
            }
            Logger.Log(LogLevel.Warn, nameof(AkronStartPosPersistence),
                "Could not prepare the fresh StartPos baseline: " + exception);
        } finally {
            timer.Stop();
            Logger.Log(LogLevel.Debug, nameof(AkronStartPosPersistence),
                "Fresh StartPos baseline prepared in " + timer.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms.");
        }
    }

    private static bool IsPendingBaselineGenerationLocked(string baselineKey, long generation) {
        return PendingBaselineGenerations.TryGetValue(baselineKey, out long currentGeneration) &&
               currentGeneration == generation;
    }

    private static bool RemovePendingBaselineGenerationLocked(string baselineKey, long generation) {
        if (!IsPendingBaselineGenerationLocked(baselineKey, generation)) {
            return false;
        }

        PendingBaselineGenerations.Remove(baselineKey);
        if (PendingBaselineInitializationGenerations.TryGetValue(baselineKey, out long initializationGeneration) &&
            initializationGeneration == generation) {
            PendingBaselineInitializationGenerations.Remove(baselineKey);
        }
        return true;
    }

    private static void QueueWaitingJobsForBaselineLocked(
        string baselineKey,
        AkronSaveLoadSlotLease baseline
    ) {
        for (int index = WaitingForBaseline.Count - 1; index >= 0; index--) {
            PersistenceJob waiting = WaitingForBaseline[index];
            if (!string.Equals(BuildBaselineKey(waiting.SavedState.Slot), baselineKey, StringComparison.Ordinal)) {
                continue;
            }
            if (!LatestGenerations.TryGetValue(waiting.StateSlotName, out long currentGeneration) ||
                currentGeneration != waiting.Generation) {
                WaitingForBaseline.RemoveAt(index);
                Completed.Enqueue(new PersistenceCompletion(
                    waiting,
                    AkronSaveLoadResult.NoState,
                    "superseded by a newer Set",
                    string.Empty,
                    TimeSpan.Zero));
                continue;
            }
            waiting.FreshBaseline = baseline.Retain();
            AttachRuntimeFreshBaselineLocked(waiting.StateSlotName, baseline);
            WaitingForBaseline.RemoveAt(index);
            Ready.Enqueue(waiting);
        }
    }

    private static void AttachRuntimeFreshBaselineLocked(
        string stateSlotName,
        AkronSaveLoadSlotLease baseline
    ) {
        AkronSaveLoadSlotLease retainedBaseline = baseline.Retain();
        if (RuntimeFreshBaselines.Remove(stateSlotName, out AkronSaveLoadSlotLease previousBaseline)) {
            previousBaseline.Dispose();
        }
        RuntimeFreshBaselines[stateSlotName] = retainedBaseline;
    }

    private static void FailWaitingJobsForBaselineLocked(string baselineKey, string error) {
        for (int index = WaitingForBaseline.Count - 1; index >= 0; index--) {
            PersistenceJob waiting = WaitingForBaseline[index];
            if (!string.Equals(BuildBaselineKey(waiting.SavedState.Slot), baselineKey, StringComparison.Ordinal)) {
                continue;
            }
            WaitingForBaseline.RemoveAt(index);
            Completed.Enqueue(new PersistenceCompletion(
                waiting,
                AkronSaveLoadResult.Failed,
                error,
                string.Empty,
                TimeSpan.Zero));
        }
    }

    private static void FailWaitingJobsExceptLocked(string baselineKey, string error) {
        for (int index = WaitingForBaseline.Count - 1; index >= 0; index--) {
            PersistenceJob waiting = WaitingForBaseline[index];
            if (string.Equals(BuildBaselineKey(waiting.SavedState.Slot), baselineKey, StringComparison.Ordinal)) {
                continue;
            }
            WaitingForBaseline.RemoveAt(index);
            Completed.Enqueue(new PersistenceCompletion(
                waiting,
                AkronSaveLoadResult.Failed,
                error,
                string.Empty,
                TimeSpan.Zero));
        }
    }

    private static void EvictOtherBaselinesLocked(string baselineKey) {
        foreach (string cachedKey in FreshBaselines.Keys
                     .Where(key => !string.Equals(key, baselineKey, StringComparison.Ordinal))
                     .ToArray()) {
            AkronSaveLoadSlotLease baseline = FreshBaselines[cachedKey];
            FreshBaselines.Remove(cachedKey);
            baseline.Dispose();
        }
    }

    private static void StartWorkerLocked() {
        if (shuttingDown || Ready.Count == 0 || workerTask is { IsCompleted: false }) {
            return;
        }
        workerTask = Task.Run(RunWorker);
    }

    private static void RunWorker() {
        while (true) {
            PersistenceJob job;
            lock (Sync) {
                if (Ready.Count == 0) {
                    workerTask = null;
                    return;
                }
                job = Ready.Dequeue();
            }

            if (!IsCurrent(job.StateSlotName, job.Generation)) {
                Completed.Enqueue(new PersistenceCompletion(
                    job,
                    AkronSaveLoadResult.NoState,
                    "superseded by a newer Set",
                    string.Empty,
                    TimeSpan.Zero));
                continue;
            }

            string stagingDirectory = Path.Combine(
                Path.GetTempPath(),
                "akron-startpos-" + Guid.NewGuid().ToString("N"));
            Stopwatch timer = Stopwatch.StartNew();
            AkronSaveLoadResult result;
            string error;
            try {
                Directory.CreateDirectory(stagingDirectory);
                result = AkronSaveLoadService.PersistRuntimeStateSnapshot(
                    job.SavedState.Slot,
                    job.FreshBaseline.Slot,
                    job.RegisteredActionIds,
                    stagingDirectory,
                    out error);
            } catch (Exception exception) {
                result = AkronSaveLoadResult.Failed;
                error = exception.GetType().Name + ": " + exception.Message;
            }
            timer.Stop();
            Completed.Enqueue(new PersistenceCompletion(job, result, error, stagingDirectory, timer.Elapsed));
        }
    }

    private static string BuildBaselineKey(Level level) {
        return (SaveData.Instance?.FileSlot ?? -1).ToString(CultureInfo.InvariantCulture) + "|" +
               (level?.Session?.Area.GetSID() ?? string.Empty) + "|" +
               (level?.Session?.Level ?? string.Empty) + "|" +
               string.Join("\n", AkronSaveLoadService.GetRegisteredActionIdsForPersistence());
    }

    private static string BuildBaselineKey(AkronSaveLoadSlot slot) {
        return (slot?.FileSlot ?? -1).ToString(CultureInfo.InvariantCulture) + "|" +
               (slot?.MapSid ?? string.Empty) + "|" +
               (slot?.LevelName ?? string.Empty) + "|" +
               string.Join("\n", slot?.ActionState.Keys.OrderBy(id => id, StringComparer.Ordinal) ?? Enumerable.Empty<string>());
    }

    private static void DeleteStagingDirectory(string directory) {
        if (string.IsNullOrWhiteSpace(directory)) {
            return;
        }
        try {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) {
            Logger.Log(LogLevel.Warn, nameof(AkronStartPosPersistence),
                "Could not delete staged StartPos snapshot: " + exception.Message);
        }
    }

    private sealed class BaselineCaptureSuppression : IDisposable {
        private bool disposed;

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            suppressBaselineCapture--;
        }
    }

    private sealed class PersistenceJob : IDisposable {
        public int FileSlot { get; init; }
        public AkronModuleSaveData SaveData { get; init; }
        public int Slot { get; init; }
        public AkronStartPos StartPos { get; init; }
        public string StateSlotName { get; init; } = string.Empty;
        public long Generation { get; init; }
        public AkronSaveLoadSlotLease SavedState { get; init; }
        public AkronSaveLoadSlotLease FreshBaseline { get; set; }
        public IReadOnlyList<string> RegisteredActionIds { get; init; } = Array.Empty<string>();

        public void Dispose() {
            if (SavedState?.Slot != null) {
                SavedState.Slot.PersistentRenderTargets =
                    new Dictionary<object, AkronReconstructionResourcePayload>();
            }
            SavedState?.Dispose();
            FreshBaseline?.Dispose();
        }
    }

    private sealed class PersistenceCompletion {
        public PersistenceCompletion(
            PersistenceJob job,
            AkronSaveLoadResult result,
            string error,
            string stagingDirectory,
            TimeSpan elapsed
        ) {
            Job = job;
            Result = result;
            Error = error ?? string.Empty;
            StagingDirectory = stagingDirectory ?? string.Empty;
            Elapsed = elapsed;
        }

        public PersistenceJob Job { get; }
        public AkronSaveLoadResult Result { get; }
        public string Error { get; }
        public string StagingDirectory { get; }
        public TimeSpan Elapsed { get; }
    }
}
