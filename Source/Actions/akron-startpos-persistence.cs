using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
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

    // Slots queued for background snapshot deserialization, in the order they should
    // be read. Loading one slot on a map usually means the next slot is wanted soon,
    // and the expensive half of that next load is pure data that can be read early.
    private static readonly Queue<string> PrewarmQueue = new Queue<string>();

    // How long a Load will hold the game thread waiting for the restart copy it
    // needs. The realistic wait is the job already running plus the one being
    // waited on; this is the backstop for a job that can never become runnable,
    // and it is deliberately shorter than the point where a player decides the
    // game has hung.
    private static readonly TimeSpan RestartCopyLoadWaitBudget = TimeSpan.FromSeconds(10);

    // How long quitting will wait for the queue. Measured on the Linux test box a
    // full 15-slot queue took 18.6 s to drain, which reads as a crash. Anything
    // still unwritten after this budget is reported as not saved instead of
    // holding a closed window open, and the job in flight is cancelled at its next
    // pace point so nothing is left half-written or leaked in the staging area.
    private static readonly TimeSpan ShutdownDrainBudget = TimeSpan.FromSeconds(5);

    // How long the cancelled job gets to notice. Pace points are a few kilobytes
    // apart, so this is generous; it exists because cancellation is cooperative and
    // an unbounded second wait would put the hang straight back.
    private static readonly TimeSpan ShutdownCancelBudget = TimeSpan.FromSeconds(2);

    // How long quitting will wait for the read-ahead. It should return at once - it
    // tests shuttingDown at its loop head and its abandon predicate reads the same
    // flag, with the gate forced open above so a parked read is awake within a sleep
    // slice - but "should return at once" is not a bound, and quitting cannot hang on
    // one. Shorter than the queue drain because there is nothing to save here: a read
    // this gives up on costs a slower load in the next process and nothing else.
    private static readonly TimeSpan ShutdownPrewarmBudget = TimeSpan.FromSeconds(2);

    private const int DrainPollMilliseconds = 5;

    private static long workerAllocatedBytes;
    private static long workerJobsFinished;
    private static string runningStateSlotName;
    private static Task workerTask;
    private static Task prewarmTask;
    private static long prewarmGeneration;
    // Per-run accounting for the one summary line. Every queued slot ends in exactly
    // one of these buckets or is still in PrewarmQueue when the run ends, so the line
    // can account for the whole queue instead of reporting a bare warmed count that
    // reads as a failure when the map was already warm.
    private static int prewarmQueued;
    private static int prewarmWarmed;
    private static int prewarmAlreadyCached;
    private static int prewarmBudgetFull;
    private static int prewarmNotStored;
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
            // Everest can unload and reload a mod in one process, so clear the
            // pacing overrides the previous Shutdown set.
            AkronSnapshotPacing.ForcedOpen = false;
            AkronSnapshotPacing.Cancelled = false;
        }
        On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
        On.Celeste.SaveData.TryDeleteModSaveData += SaveDataOnTryDeleteModSaveData;

        // Snapshots written under a previous format version are dead the moment the
        // format moves, and nothing else ever removes them, so one file per slot is
        // left on disk at every bump. Sweeping them is a directory listing plus a
        // delete each, and nothing waits on the result, so it runs off the game
        // thread once per Start rather than on any player-facing path.
        //
        // Nothing awaits this task, so nothing would ever observe a throw out of it. The
        // filters inside the sweep name the filesystem failures it expects; this catch is
        // total because the alternative for anything else is an unobserved Task, and the
        // whole method only ever reclaims disk space. The next launch tries again.
        Task.Run(static () => {
            try {
                SweepSupersededSnapshots();
            } catch (Exception exception) {
                AkronLog.Warn(nameof(AkronStartPosPersistence),
                    "Could not sweep superseded StartPos snapshots: " + exception);
            }
        });
    }

    // Removes StartPos snapshots whose file name carries a format version older than
    // the one this build writes, and returns what that recovered.
    //
    // These files need no liveness test, which is the only reason this method exists
    // and a sweep of current-format orphans does not. A snapshot is read through
    // exactly one path, AkronStartPosReconstruction.GetSnapshotPath(slotName), which
    // builds the current name; a file under an older name is never opened by any
    // slot, on any save file, ever again. Even if one did arrive at a current path,
    // ValidateDocumentHeader refuses a document written against a fresh-room baseline
    // this build no longer builds. So there is nothing to prove about who might still
    // want the file - only that the name test cannot misfire.
    //
    // A partial sweep is not a partial state: every file it deletes is independently
    // dead, so stopping halfway leaves the remainder exactly as it was.
    internal static (int Files, long Bytes) SweepSupersededSnapshots(string directory = null) {
        int files = 0;
        long bytes = 0;
        try {
            string probePath = AkronStartPosReconstruction.GetSnapshotPath(string.Empty, directory);
            string root = Path.GetDirectoryName(probePath);
            if (string.IsNullOrEmpty(root) ||
                !TryReadSnapshotNaming(Path.GetFileName(probePath), out SnapshotNaming naming)) {
                return (0, 0);
            }

            foreach (string path in Directory.EnumerateFiles(root)) {
                if (!naming.IsSuperseded(Path.GetFileName(path))) {
                    continue;
                }

                long size = 0;
                try {
                    FileInfo info = new FileInfo(path);
                    size = info.Exists ? info.Length : 0;
                    File.Delete(path);
                } catch (Exception exception) when (exception is IOException ||
                                                   exception is UnauthorizedAccessException ||
                                                   exception is ArgumentException ||
                                                   exception is NotSupportedException) {
                    // The last two are a name in the folder that File.Delete will not take at
                    // all. One of those must skip that file rather than end the sweep, the same
                    // as a file another process has open.
                    AkronLog.Warn(nameof(AkronStartPosPersistence),
                        "Could not remove the superseded StartPos snapshot " + path + ": " + exception.Message);
                    continue;
                }
                files++;
                bytes += size;
            }
        } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) {
            // The snapshot folder is missing or unreadable. Nothing here is worth
            // reporting as a failure: the sweep only ever reclaims disk, and the next
            // launch tries again.
            return (files, bytes);
        }

        if (files > 0) {
            AkronLog.Normal(nameof(AkronStartPosPersistence),
                "Removed " + files.ToString(CultureInfo.InvariantCulture) +
                " StartPos snapshot(s) saved under a format this build no longer reads, recovering " +
                (bytes / 1024.0 / 1024.0).ToString("F1", CultureInfo.InvariantCulture) +
                " MB. Those slots have to be set again.");
        }
        return (files, bytes);
    }

    // The shape of a snapshot file name, read back from the writer rather than
    // repeated here.
    //
    // A version literal written out here would go on matching after the format had
    // moved off it, and this is the test that decides whether a file is deleted, so
    // the one thing it must never do is call a live name superseded. The format
    // moved twice while this was being written, which is the whole argument. Instead
    // it asks GetSnapshotPath for the name this build writes for a known slot, finds
    // that slot's digest inside it, and takes what is on either side as the version
    // prefix and the extension. Anything it cannot parse leaves the sweep doing
    // nothing, which is the safe direction for a delete.
    private readonly struct SnapshotNaming {
        private readonly string versionStem;
        private readonly string versionSeparator;
        private readonly string suffix;
        private readonly int digestLength;
        private readonly int currentVersion;

        public SnapshotNaming(string versionStem, string versionSeparator, int currentVersion, int digestLength, string suffix) {
            this.versionStem = versionStem;
            this.versionSeparator = versionSeparator;
            this.currentVersion = currentVersion;
            this.digestLength = digestLength;
            this.suffix = suffix;
        }

        // True for a name of exactly the shape this build writes, carrying a lower
        // format version. Three separate reasons for the strictness:
        //
        // - Whole-name, not prefix: the temporary file a write lands through is
        //   "<current name>.<guid>.tmp", and deleting one would break a copy in
        //   flight.
        // - Lower, not merely different: a build that is older than the files it
        //   finds is a downgrade, and taking a newer build's snapshots away from it
        //   would be gratuitous - they become readable again on the way back up.
        // - The digest has to look like a digest: the folder is Akron's, but a
        //   file the player put there is not something to delete on a length match.
        public bool IsSuperseded(string fileName) {
            int prefixLength = fileName.Length - digestLength - suffix.Length;
            if (prefixLength <= versionStem.Length + versionSeparator.Length ||
                !fileName.EndsWith(suffix, StringComparison.Ordinal) ||
                !fileName.StartsWith(versionStem, StringComparison.Ordinal) ||
                string.CompareOrdinal(fileName, prefixLength - versionSeparator.Length, versionSeparator, 0, versionSeparator.Length) != 0) {
                return false;
            }

            int digitsStart = versionStem.Length;
            int digitsLength = prefixLength - versionSeparator.Length - digitsStart;
            if (!TryReadVersion(fileName, digitsStart, digitsLength, out int version) || version >= currentVersion) {
                return false;
            }

            for (int index = prefixLength; index < prefixLength + digestLength; index++) {
                char character = fileName[index];
                if (!(character >= '0' && character <= '9') && !(character >= 'a' && character <= 'f')) {
                    return false;
                }
            }
            return true;
        }

        private static bool TryReadVersion(string value, int start, int length, out int version) {
            version = 0;
            // Bounded so a name carrying a very long run of digits cannot overflow the
            // comparison into looking older than it is.
            if (length <= 0 || length > 9) {
                return false;
            }
            for (int index = start; index < start + length; index++) {
                char character = value[index];
                if (character < '0' || character > '9') {
                    return false;
                }
                version = (version * 10) + (character - '0');
            }
            return true;
        }
    }

    private static bool TryReadSnapshotNaming(string currentFileName, out SnapshotNaming naming) {
        naming = default;
        if (string.IsNullOrEmpty(currentFileName)) {
            return false;
        }

        // Mirrors BuildSnapshotSlotDigest(string.Empty): the digest of the slot name
        // GetSnapshotPath was probed with. Locating it splits the name into the parts
        // that do not depend on the slot.
        string digest = Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant();
        int digestStart = currentFileName.IndexOf(digest, StringComparison.Ordinal);
        if (digestStart <= 0) {
            return false;
        }

        string prefix = currentFileName.Substring(0, digestStart);
        string suffix = currentFileName.Substring(digestStart + digest.Length);
        if (suffix.Length == 0) {
            return false;
        }

        // The prefix is "<stem><version number><separator>". Splitting it that way is
        // what lets the test say "the same scheme, an older number" rather than "not
        // the current name", which would also match anything else sharing the folder.
        int digitsStart = 0;
        while (digitsStart < prefix.Length && (prefix[digitsStart] < '0' || prefix[digitsStart] > '9')) {
            digitsStart++;
        }
        int digitsEnd = digitsStart;
        int currentVersion = 0;
        while (digitsEnd < prefix.Length && prefix[digitsEnd] >= '0' && prefix[digitsEnd] <= '9') {
            currentVersion = (currentVersion * 10) + (prefix[digitsEnd] - '0');
            digitsEnd++;
        }
        if (digitsEnd == digitsStart || digitsEnd - digitsStart > 9) {
            return false;
        }

        naming = new SnapshotNaming(
            prefix.Substring(0, digitsStart),
            prefix.Substring(digitsEnd),
            currentVersion,
            digest.Length,
            suffix);
        return true;
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
            // A room load always rebuilds the room, so any retained baseline
            // describes the previous build and must be dropped. Deciding this
            // from observed state instead would need a freshness check that can
            // see every input room construction reads, including arbitrary
            // module session and save data, and no such check exists.
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
        int slot,
        AkronStartPos startPos,
        string stateSlotName
    ) {
        AkronSaveLoadSlotLease savedState = AkronSaveLoadService.RetainRuntimeState(stateSlotName);
        if (savedState?.Slot == null) {
            savedState?.Dispose();
            return 0;
        }

        string baselineKey = BuildBaselineKey(savedState.Slot);
        // Read before the lock: it walks the registered-action list, which the game
        // thread owns, and holding Sync across that buys nothing.
        IReadOnlyList<string> registeredActionIds = AkronSaveLoadService.GetRegisteredActionIdsForPersistence();

        PersistenceJob job;
        lock (Sync) {
            // nextGeneration is handed out here and in Cancel, and both do it under Sync.
            // The number is the only thing that tells a completion worth applying from one
            // a newer Set or a Cancel has superseded, so two callers that read the same
            // value would let a cancelled Set be applied to the slot. Every caller is on
            // the game thread today; this is the state where being wrong about that later
            // would be silent.
            job = new PersistenceJob {
                FileSlot = fileSlot,
                ProfileId = startPos.ProfileId,
                Slot = slot,
                StartPos = startPos,
                StateSlotName = stateSlotName,
                Generation = ++nextGeneration,
                SavedState = savedState,
                RegisteredActionIds = registeredActionIds
            };
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

    // The runtime fresh baseline belongs to the slot's committed state: without it a warm
    // clone restored across rooms cannot be persisted again. A Set replaces it, so it is
    // parked next to the warm clone and put back if the Set never commits. Parking moves
    // the lease to a private key rather than disposing it, so the graph stays alive.
    internal static string ParkRuntimeFreshBaseline(string stateSlotName) {
        if (string.IsNullOrWhiteSpace(stateSlotName)) {
            return null;
        }
        lock (Sync) {
            if (!RuntimeFreshBaselines.Remove(stateSlotName, out AkronSaveLoadSlotLease baseline)) {
                return null;
            }

            string parkedKey = stateSlotName + " (parked " + Guid.NewGuid().ToString("N") + ")";
            RuntimeFreshBaselines[parkedKey] = baseline;
            return parkedKey;
        }
    }

    internal static void RestoreParkedRuntimeFreshBaseline(string parkedKey, string stateSlotName) {
        if (string.IsNullOrWhiteSpace(parkedKey) || string.IsNullOrWhiteSpace(stateSlotName)) {
            return;
        }
        lock (Sync) {
            if (!RuntimeFreshBaselines.Remove(parkedKey, out AkronSaveLoadSlotLease baseline)) {
                return;
            }
            if (RuntimeFreshBaselines.Remove(stateSlotName, out AkronSaveLoadSlotLease abandonedBaseline)) {
                abandonedBaseline.Dispose();
            }
            RuntimeFreshBaselines[stateSlotName] = baseline;
        }
    }

    internal static void DiscardParkedRuntimeFreshBaseline(string parkedKey) {
        if (string.IsNullOrWhiteSpace(parkedKey)) {
            return;
        }
        lock (Sync) {
            if (RuntimeFreshBaselines.Remove(parkedKey, out AkronSaveLoadSlotLease baseline)) {
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

    // Queues every remaining placed slot of the current map for background snapshot
    // reads. Replaces any queue still outstanding: the slots that matter are the ones
    // near whatever the player just loaded, and an older queue is by definition staler.
    public static void PrewarmSnapshots(IReadOnlyList<string> stateSlotNames) {
        PrewarmRunSummary superseded;
        lock (Sync) {
            superseded = TakePrewarmRunSummaryLocked(replaced: true);
            prewarmGeneration++;
            PrewarmQueue.Clear();
            if (!shuttingDown && stateSlotNames != null) {
                foreach (string stateSlotName in stateSlotNames) {
                    if (!string.IsNullOrWhiteSpace(stateSlotName)) {
                        PrewarmQueue.Enqueue(stateSlotName);
                    }
                }
                prewarmQueued = PrewarmQueue.Count;
                StartPrewarmWorkerLocked();
            }
        }
        // Outside the lock: a file write on this thread while the worker is waiting on
        // the same lock would be a stall for a diagnostic.
        ReportPrewarmRun(superseded);
    }

    // Drops the queue and aborts the read in flight. Called whenever the player asks
    // for something that must not wait behind speculative work.
    public static void CancelPrewarm() {
        PrewarmRunSummary superseded;
        lock (Sync) {
            superseded = TakePrewarmRunSummaryLocked(replaced: true);
            prewarmGeneration++;
            PrewarmQueue.Clear();
        }
        ReportPrewarmRun(superseded);
    }

    // A run ends either by draining, which the worker reports, or by being replaced
    // here. Reporting both is what stops a queue that is only ever partly drained -
    // the case the whole feature exists for, since a map holds more slots than one
    // non-gameplay window can read - from writing nothing to the log at all.
    private static PrewarmRunSummary TakePrewarmRunSummaryLocked(bool replaced) {
        PrewarmRunSummary summary = new PrewarmRunSummary(
            prewarmQueued,
            prewarmWarmed,
            prewarmAlreadyCached,
            prewarmBudgetFull,
            prewarmNotStored,
            replaced);
        prewarmQueued = 0;
        prewarmWarmed = 0;
        prewarmAlreadyCached = 0;
        prewarmBudgetFull = 0;
        prewarmNotStored = 0;
        return summary;
    }

    private readonly struct PrewarmRunSummary {
        internal PrewarmRunSummary(
            int queued,
            int warmed,
            int alreadyCached,
            int budgetFull,
            int notStored,
            bool replaced
        ) {
            Queued = queued;
            Warmed = warmed;
            AlreadyCached = alreadyCached;
            BudgetFull = budgetFull;
            NotStored = notStored;
            Replaced = replaced;
        }

        internal int Queued { get; }
        internal int Warmed { get; }
        internal int AlreadyCached { get; }
        internal int BudgetFull { get; }
        internal int NotStored { get; }
        internal bool Replaced { get; }
    }

    // How many slots are still waiting to be read. Zero with a non-zero warmed count is
    // "the whole map is warm"; zero with a non-zero budget refusal is "the map did not
    // fit". Both are in akron_status, because a cache nobody can see is a cache a later
    // change can disable without a single test going red.
    internal static int PrewarmQueueLength {
        get {
            lock (Sync) {
                return PrewarmQueue.Count;
            }
        }
    }

    private static void StartPrewarmWorkerLocked() {
        if (shuttingDown || PrewarmQueue.Count == 0 || prewarmTask is { IsCompleted: false }) {
            return;
        }
        prewarmTask = Task.Run(RunPrewarmWorker);
    }

    private static void RunPrewarmWorker() {
        while (true) {
            string stateSlotName;
            long generation;
            PrewarmRunSummary finished = default;
            lock (Sync) {
                // The loop only ends on an empty queue, never on a generation change.
                // Ending on a generation change would race a caller that has already
                // refilled the queue and seen this task as still running.
                if (shuttingDown || PrewarmQueue.Count == 0) {
                    prewarmTask = null;
                    finished = TakePrewarmRunSummaryLocked(replaced: false);
                    stateSlotName = null;
                    generation = 0;
                } else {
                    stateSlotName = PrewarmQueue.Dequeue();
                    generation = prewarmGeneration;
                }
            }
            if (stateSlotName == null) {
                // Logged outside the lock. A file write on this thread while the game
                // thread is waiting on the same lock would be a stall for a diagnostic.
                ReportPrewarmRun(finished);
                return;
            }

            // The read paces on the same gate the restart copy uses, so it allocates
            // nothing at all while the player is in control. The abandon predicate is
            // the same cancellation the read stream polls: a parked read is holding the
            // snapshot file open and a half-built document in memory, and both have to
            // go the moment the queue is replaced.
            AkronSnapshotPacing.BeginPacedWork(() => IsPrewarmCancelled(generation));
            try {
                AkronPrewarmOutcome outcome = AkronStartPosReconstruction.PrewarmSnapshot(
                    stateSlotName,
                    () => IsPrewarmCancelled(generation));
                lock (Sync) {
                    // A run superseded mid-read has already had its counters reset and
                    // reported by whoever superseded it, so only count against the run
                    // this slot was taken from.
                    if (generation == prewarmGeneration) {
                        CountPrewarmOutcomeLocked(outcome);
                    }
                }
            } catch (Exception exception) {
                // Prewarming is speculative. A failure here costs a slower load and
                // nothing else, so it must not take the worker or the game down.
                AkronLog.Diagnostic(nameof(AkronStartPosPersistence),
                    "StartPos prewarm failed for " + stateSlotName + ": " + exception.Message);
            } finally {
                AkronSnapshotPacing.EndPacedWork();
            }
        }
    }

    private static void CountPrewarmOutcomeLocked(AkronPrewarmOutcome outcome) {
        switch (outcome) {
            case AkronPrewarmOutcome.Stored:
                prewarmWarmed++;
                break;
            case AkronPrewarmOutcome.AlreadyCached:
                prewarmAlreadyCached++;
                break;
            case AkronPrewarmOutcome.BudgetFull:
                prewarmBudgetFull++;
                break;
            default:
                prewarmNotStored++;
                break;
        }
    }

    // One line per run, not one per slot: with 50 slots the per-slot version would be
    // the noisiest thing in the log for something the player cannot act on.
    //
    // The line has to account for every slot it queued. "warmed 0 of 3" was written
    // whenever a load re-queued a map that was already warm, which reads as a broken
    // feature and was the opposite; and a run that was replaced before it drained -
    // the normal case now that the queue is filled after the load and drains only
    // where the player is not in control - wrote nothing at all.
    private static void ReportPrewarmRun(PrewarmRunSummary run) {
        if (run.Queued <= 0) {
            return;
        }

        AkronLog.Diagnostic(nameof(AkronStartPosPersistence),
            DescribePrewarmRun(
                run.Queued, run.Warmed, run.AlreadyCached, run.BudgetFull, run.NotStored, run.Replaced) +
            ". Holding " +
            (AkronStartPosReconstruction.PrewarmedSnapshotBytes / (1024d * 1024d))
                .ToString("F1", CultureInfo.InvariantCulture) + " MB of the " +
            (AkronStartPosReconstruction.MaxPrewarmedSnapshotBytes / (1024d * 1024d))
                .ToString("F1", CultureInfo.InvariantCulture) + " MB budget.");
    }

    // The accounting half of that line, split out from the holdings so it can be
    // asserted exactly. Every queued slot has to land in one clause: a bare warmed
    // count cannot tell "the map was already warm" from "nothing worked".
    internal static string DescribePrewarmRun(
        int queued,
        int warmed,
        int alreadyCached,
        int budgetFull,
        int notStored,
        bool replaced
    ) {
        List<string> reasons = new List<string>();
        if (alreadyCached > 0) {
            reasons.Add(alreadyCached.ToString(CultureInfo.InvariantCulture) + " already cached");
        }
        if (budgetFull > 0) {
            reasons.Add(budgetFull.ToString(CultureInfo.InvariantCulture) +
                        " did not fit the remaining budget");
        }
        if (notStored > 0) {
            reasons.Add(notStored.ToString(CultureInfo.InvariantCulture) + " could not be read");
        }
        // Slots still in the queue, plus the one abandoned mid-read if the run was
        // replaced while the worker held it. Derived rather than counted so the clauses
        // always add up to the queue, whatever the worker was doing when it was
        // superseded.
        int neverRead = queued - warmed - alreadyCached - budgetFull - notStored;
        if (neverRead > 0) {
            reasons.Add(neverRead.ToString(CultureInfo.InvariantCulture) +
                        (replaced ? " not read before the queue was replaced" : " never read"));
        }

        return "StartPos prewarm warmed " + warmed.ToString(CultureInfo.InvariantCulture) + " of " +
               queued.ToString(CultureInfo.InvariantCulture) + " slots for this map" +
               (reasons.Count == 0 ? "" : ": " + string.Join(", ", reasons));
    }

    private static bool IsPrewarmCancelled(long generation) {
        lock (Sync) {
            return shuttingDown || generation != prewarmGeneration;
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
        // Runs once per engine update for every scene. A paused level and a
        // StartPos input wait both count as not in control: neither simulates
        // gameplay, so a stall the player cannot act through is a stall worth
        // taking to get the snapshot written sooner. Death wipes and room
        // transitions are deliberately not on this list - both still animate on
        // a clock the player is watching, and the death wipe in particular is
        // the exact frame the deferred-collection work exists to keep clear.
        AkronSnapshotPacing.GameplayActive = AkronModule.Instance != null &&
                                             Engine.Scene is Level level &&
                                             !level.Paused &&
                                             !AkronActions.IsStartPosInputWaitActive(level);
        while (Completed.TryDequeue(out PersistenceCompletion completion)) {
            try {
                if (IsCurrent(completion.Job.StateSlotName, completion.Job.Generation)) {
                    AkronActions.CompletePersistentStartPosCapture(
                        completion.Job.FileSlot,
                        completion.Job.ProfileId,
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
                AkronLog.Warn(nameof(AkronStartPosPersistence),
                    "Could not apply a StartPos restart copy: " + exception);
            } finally {
                completion.Job.Dispose();
                DeleteStagingDirectory(completion.StagingDirectory);
            }
        }
    }

    // Finishes the restart copy a Load is about to need, on the game thread, now.
    //
    // The gate that stops the worker exists to keep collections out of frames the
    // player is playing. A Load is not one of those frames: it is a pause the
    // player asked for and already waits seconds through, and the alternative is
    // refusing a slot they set earlier in the session. So the requested slot jumps
    // the queue, the gate is forced open, and this thread waits for that one job.
    //
    // Only the requested slot is waited on. Draining the whole queue here would
    // turn one Load into a wait for every slot the player has ever set this
    // session, which is the stall this is meant to avoid, not cause.
    internal static void FinishPendingRestartCopy(string stateSlotName) {
        if (string.IsNullOrWhiteSpace(stateSlotName) ||
            !AkronActions.HasPendingStartPosState(stateSlotName)) {
            return;
        }

        Stopwatch timer = Stopwatch.StartNew();
        using PacingGateHold gate = HoldPacingGateOpen();
        try {
            lock (Sync) {
                PromoteReadyJobLocked(stateSlotName);
                StartWorkerLocked();
            }

            while (timer.Elapsed < RestartCopyLoadWaitBudget) {
                bool inFlight;
                lock (Sync) {
                    inFlight = IsRestartCopyInFlightLocked(stateSlotName);
                }
                // Completions apply on the game thread, and this is it. Sampling
                // in-flight before the pump rather than after is what makes the
                // exit below safe: a job that finished between the two is applied
                // by this pump, so the pending check sees the result.
                Update();
                if (!AkronActions.HasPendingStartPosState(stateSlotName)) {
                    return;
                }
                if (!inFlight) {
                    // Nothing is queued or running for this slot, so no completion
                    // will ever clear it. The Load reports the pending slot rather
                    // than spinning out the budget for an answer that will not come.
                    return;
                }
                Thread.Sleep(DrainPollMilliseconds);
            }

            AkronLog.Warn(nameof(AkronStartPosPersistence),
                "StartPos restart copy for " + stateSlotName + " did not finish within " +
                RestartCopyLoadWaitBudget.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) +
                " s of the load that needed it.");
        } finally {
            timer.Stop();
            AkronLog.Diagnostic(nameof(AkronStartPosPersistence),
                "Load waited " + timer.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) +
                " ms for the restart copy of " + stateSlotName + ".");
        }
    }

    // Opens the pacing gate for as long as the game thread is inside something the
    // player is already waiting through, and puts it back afterwards.
    //
    // Two callers, both the same shape. A Load that needs an outstanding restart copy
    // finishes it here rather than refusing the slot, and a StartPos load holds the
    // gate open for its whole duration so the map's other slots are read while the
    // game thread is rebuilding the live scene. Neither is a frame the player is
    // playing: the game thread is blocked for seconds in both, so background work
    // there costs nothing that a frame counter can see.
    //
    // Nesting is fine and happens on every cold load: the inner hold restores the
    // outer hold's value, not the pre-load value.
    // Under Sync, because the matching Dispose is: reading the old value and writing the
    // new one has to be one step against a Shutdown that forces the gate open and against
    // another holder, or a hold can save a value that was already stale and put it back.
    // Neither caller holds Sync when it gets here.
    internal static PacingGateHold HoldPacingGateOpen() {
        lock (Sync) {
            bool previousForcedOpen = AkronSnapshotPacing.ForcedOpen;
            AkronSnapshotPacing.ForcedOpen = true;
            return new PacingGateHold(previousForcedOpen);
        }
    }

    internal readonly struct PacingGateHold : IDisposable {
        private readonly bool previousForcedOpen;

        internal PacingGateHold(bool previousForcedOpen) {
            this.previousForcedOpen = previousForcedOpen;
        }

        public void Dispose() {
            lock (Sync) {
                // Once Shutdown has forced the gate open it must stay open, so this
                // restore is skipped then. Every holder runs on the game thread today,
                // which is why they cannot interleave, but the gate is the one piece of
                // state where getting that wrong would park a worker nobody will ever
                // wake.
                if (!shuttingDown) {
                    AkronSnapshotPacing.ForcedOpen = previousForcedOpen;
                }
            }
        }
    }

    // Quitting must stay bounded. The queue is drained at full speed for a budget,
    // and whatever is left is cancelled rather than held onto.
    //
    // Giving up is safe because a job only ever writes into its own staging
    // directory: the move into the real snapshot directory is
    // PreparedSnapshotInstall.Install, which runs on the game thread in Update. So
    // abandoning a job mid-write cannot damage a snapshot that already exists, and a
    // slot that was already restart-safe is never affected. Asking the job to stop
    // rather than walking away from the thread is also what lets Update run for it,
    // which is what disposes its leases and deletes its staging directory.
    //
    // What a quit costs a slot whose copy did not finish is the copy, and that is
    // reported per slot through the normal rollback message.
    //
    // True when the worker really stopped, which is what decides whether Shutdown may
    // clear the handle to it.
    private static bool DrainWorkerForShutdown(Task runningWorker, int outstanding) {
        if (runningWorker == null) {
            return true;
        }

        if (outstanding > 0) {
            AkronLog.Normal(nameof(AkronStartPosPersistence),
                "Finishing outstanding restart-safe StartPos copies before exit (" +
                outstanding.ToString(CultureInfo.InvariantCulture) + " left). This can take a few seconds.");
        }
        if (runningWorker.Wait(ShutdownDrainBudget)) {
            return true;
        }

        AkronLog.Warn(nameof(AkronStartPosPersistence),
            "Restart-safe StartPos copies did not finish within " +
            ShutdownDrainBudget.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) +
            " s of closing. The slots that did not finish are reported individually and " +
            "have to be set again; the game is not held open any longer.");
        AkronSnapshotPacing.Cancelled = true;
        if (runningWorker.Wait(ShutdownCancelBudget)) {
            return true;
        }

        // Cancellation is cooperative: it is only seen when the job reaches its next
        // pace point, which is normally a few kilobytes of work away but is not a
        // hard bound. Waiting past this point would put back the hang the budget
        // exists to remove, so the thread is left to die with the process. The cost
        // is one staging directory under the temp path, which nothing reads and the
        // next quit does not add to unless it hits the same case.
        AkronLog.Warn(nameof(AkronStartPosPersistence),
            "A StartPos restart copy did not stop when asked. Closing without it; it may " +
            "leave one akron-startpos-* directory behind under the system temp path.");
        return false;
    }

    // Moves the requested slot's job to the head of the queue. A Load is a direct
    // request for one slot, so it outranks copies queued earlier for slots nobody
    // is waiting on.
    private static void PromoteReadyJobLocked(string stateSlotName) {
        if (Ready.Count < 2 ||
            !Ready.Any(job => string.Equals(job.StateSlotName, stateSlotName, StringComparison.Ordinal))) {
            return;
        }

        PersistenceJob[] queued = Ready.ToArray();
        Ready.Clear();
        foreach (PersistenceJob job in queued.Where(job =>
                     string.Equals(job.StateSlotName, stateSlotName, StringComparison.Ordinal))) {
            Ready.Enqueue(job);
        }
        foreach (PersistenceJob job in queued.Where(job =>
                     !string.Equals(job.StateSlotName, stateSlotName, StringComparison.Ordinal))) {
            Ready.Enqueue(job);
        }
    }

    private static bool IsRestartCopyInFlightLocked(string stateSlotName) {
        return string.Equals(runningStateSlotName, stateSlotName, StringComparison.Ordinal) ||
               Ready.Any(job => string.Equals(job.StateSlotName, stateSlotName, StringComparison.Ordinal));
    }

    public static IDisposable SuppressBaselineCapture() {
        suppressBaselineCapture++;
        return new BaselineCaptureSuppression();
    }

    public static void Shutdown() {
        Task runningWorker;
        Task runningPrewarm;
        int outstanding;
        lock (Sync) {
            if (!started) {
                return;
            }
            shuttingDown = true;
            // The joins below wait for the worker. Force the gate open so a job
            // that is mid-sleep finishes at full speed instead of waiting for a
            // player who has already closed the window.
            AkronSnapshotPacing.ForcedOpen = true;
            prewarmGeneration++;
            PrewarmQueue.Clear();
            runningPrewarm = prewarmTask;
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
            outstanding = Ready.Count + (runningWorker == null ? 0 : 1);
        }

        bool prewarmStopped = runningPrewarm == null || runningPrewarm.Wait(ShutdownPrewarmBudget);
        if (!prewarmStopped) {
            AkronLog.Warn(nameof(AkronStartPosPersistence),
                "The StartPos read-ahead did not stop within " +
                ShutdownPrewarmBudget.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) +
                " s of closing. Closing without it; it holds nothing the next launch needs.");
        }
        AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        bool workerStopped = DrainWorkerForShutdown(runningWorker, outstanding);
        Update();
        AkronActions.SaveAkronStartPosData();
        On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
        On.Celeste.SaveData.TryDeleteModSaveData -= SaveDataOnTryDeleteModSaveData;

        lock (Sync) {
            // A drain that ran out of budget leaves jobs queued, and every one of them
            // holds a retained lease on a saved state and on a fresh baseline. Everest can
            // unload and reload a mod inside one process - Start above is written for
            // exactly that - so a lease left here does not die with the process: it
            // survives into the next run, where the job holding it is queued behind a
            // worker that will never be told what it was for. Dropping them here is what
            // makes Start's "this is a fresh run" true.
            //
            // Safe to do under Sync: the worker takes its job out of Ready under this same
            // lock, so nothing in this queue is in flight, and a worker still alive finds
            // the queue empty and stops.
            while (Ready.TryDequeue(out PersistenceJob abandoned)) {
                abandoned.Dispose();
            }
            // Read by a Load to tell "still coming" from "nothing will finish this". A
            // name left behind would answer "still coming" for a slot the next run has no
            // worker for.
            runningStateSlotName = null;
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
            // Each handle is cleared only when the task behind it really stopped. A worker
            // or a read-ahead that outlived its budget is still in its loop and still owns
            // its handle: it clears the handle itself on the way out. Clearing it here
            // would let an Everest reload in the same process start a second one alongside
            // the survivor, and then let the survivor null out the newcomer's handle when
            // it finally exits.
            //
            // For the capture worker the second one is worse than a duplicate handle. Both
            // would dequeue from Ready, and both would then enter the one static
            // CaptureGraph, which is written for a single thread at a time. Leaving the
            // handle costs nothing instead: both queues are emptied above, so a survivor
            // that reaches its loop head finds nothing to do and clears its own handle,
            // and one still inside a job serves whatever the next run has queued by the
            // time it comes back round. Either way one worker, which is what Ready is
            // written for.
            if (workerStopped) {
                workerTask = null;
            }
            if (prewarmStopped) {
                prewarmTask = null;
            }
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

    private static bool SaveDataOnTryDeleteModSaveData(
        On.Celeste.SaveData.orig_TryDeleteModSaveData orig,
        int fileSlot
    ) {
        AkronModuleSaveData deletedProfile = null;
        try {
            deletedProfile = ReadProfileSaveData(fileSlot);
        } catch (Exception exception) {
            // Profile deletion must not depend on optional StartPos cleanup. Without
            // readable ownership metadata, leaving files alone is the safe direction.
            AkronLog.Warn(nameof(AkronStartPosPersistence),
                "Could not read StartPos metadata before deleting save slot " +
                fileSlot.ToString(CultureInfo.InvariantCulture) + ": " + exception.Message);
        }

        bool deleted = orig(fileSlot);
        if (deletedProfile == null) {
            return deleted;
        }

        try {
            // TryDeleteModSaveData can report another module's failure after Akron's
            // file was removed. Check Akron's file itself instead of trusting the
            // aggregate result before taking its now-unowned snapshots away.
            if (AkronModule.Instance != null && AkronModule.Instance.ReadSaveData(fileSlot) == null) {
                AkronActions.DeleteStartPosSnapshotsForProfile(fileSlot, deletedProfile);
            }
        } catch (Exception exception) {
            AkronLog.Warn(nameof(AkronStartPosPersistence),
                "Could not remove StartPos snapshots for deleted save slot " +
                fileSlot.ToString(CultureInfo.InvariantCulture) + ": " + exception.Message);
        }
        return deleted;
    }

    private static AkronModuleSaveData ReadProfileSaveData(int fileSlot) {
        byte[] serialized = AkronModule.Instance?.ReadSaveData(fileSlot);
        if (serialized == null) {
            return null;
        }

        using MemoryStream stream = new MemoryStream(serialized, writable: false);
        using StreamReader reader = new StreamReader(stream);
        return YamlHelper.Deserializer.Deserialize<AkronModuleSaveData>(reader);
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
            AkronLog.Warn(nameof(AkronStartPosPersistence),
                "Could not prepare the fresh StartPos baseline: " + exception);
        } finally {
            timer.Stop();
            AkronLog.Verbose(nameof(AkronStartPosPersistence),
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
                    runningStateSlotName = null;
                    workerTask = null;
                    return;
                }
                job = Ready.Dequeue();
                // Read under the lock by a Load waiting for this slot, so it can
                // tell "still coming" from "nothing will ever finish this".
                runningStateSlotName = job.StateSlotName;
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

            if (AkronSnapshotPacing.Cancelled) {
                // Shutdown gave up on the queue. Starting this job would only
                // produce a staging directory nobody will ever install.
                Completed.Enqueue(new PersistenceCompletion(
                    job,
                    AkronSaveLoadResult.Failed,
                    AkronSnapshotPacing.CancelledMessage,
                    string.Empty,
                    TimeSpan.Zero));
                continue;
            }

            string stagingDirectory = Path.Combine(
                Path.GetTempPath(),
                "akron-startpos-" + Guid.NewGuid().ToString("N"));
            Stopwatch timer = Stopwatch.StartNew();
            // RunWorker is one synchronous loop on one pool thread, so a
            // per-thread allocation delta around the persist call is exactly this
            // job's allocation and nothing else. Process-wide counters cannot say
            // that: they integrate every other thread over however long the job
            // took, which is what made an earlier "the worker allocates 30% more
            // when paced" reading impossible to attribute.
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            AkronSaveLoadResult result;
            string error;
            TimeSpan parked;
            // Everything this job touches is already retained and immutable, so
            // taking longer cannot change what it produces or how much it
            // allocates. Pacing only decides when those bytes are spent.
            AkronSnapshotPacing.BeginPacedWork();
            try {
                Directory.CreateDirectory(stagingDirectory);
                result = AkronSaveLoadService.PersistRuntimeStateSnapshot(
                    job.SavedState.Slot,
                    job.FreshBaseline.Slot,
                    job.RegisteredActionIds,
                    stagingDirectory,
                    out error);
            } catch (OperationCanceledException) {
                result = AkronSaveLoadResult.Failed;
                error = AkronSnapshotPacing.CancelledMessage;
            } catch (Exception exception) {
                result = AkronSaveLoadResult.Failed;
                error = exception.GetType().Name + ": " + exception.Message;
            } finally {
                parked = AkronSnapshotPacing.ParkedTime;
                AkronSnapshotPacing.EndPacedWork();
            }
            timer.Stop();
            RecordWorkerAllocation(
                job.StateSlotName,
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
                timer.Elapsed,
                parked);
            // The completion reports working time, not wall-clock time. Wall clock
            // is dominated by however long the player stayed in control, so a log
            // line built from it reads as if one snapshot took a minute.
            Completed.Enqueue(new PersistenceCompletion(
                job,
                result,
                error,
                stagingDirectory,
                timer.Elapsed - parked));
        }
    }

    // Cumulative worker-thread allocation, so a run can be compared against
    // another run without reasoning about how long either one lasted.
    internal static long WorkerAllocatedBytes => Interlocked.Read(ref workerAllocatedBytes);
    internal static long WorkerJobsFinished => Interlocked.Read(ref workerJobsFinished);

    private static void RecordWorkerAllocation(
        string stateSlotName,
        long allocatedBytes,
        TimeSpan elapsed,
        TimeSpan parked
    ) {
        Interlocked.Add(ref workerAllocatedBytes, allocatedBytes);
        Interlocked.Increment(ref workerJobsFinished);
        AkronLog.Diagnostic(nameof(AkronStartPosPersistence),
            "StartPos restart copy for " + stateSlotName + " finished in " +
            (elapsed - parked).TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) +
            " ms of work (" + parked.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) +
            " ms parked while the player was in control) and allocated " +
            (allocatedBytes / (1024d * 1024d)).ToString("F1", CultureInfo.InvariantCulture) +
            " MB on the worker thread.");
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
            AkronLog.Warn(nameof(AkronStartPosPersistence),
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
        public string ProfileId { get; init; } = string.Empty;
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
