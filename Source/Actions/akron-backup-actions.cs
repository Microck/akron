using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed class AkronBackupEntry {
    public string Path { get; set; }
    public string FileName { get; set; }
    public DateTime CreatedUtc { get; set; }
    public long SizeBytes { get; set; }
    public string Reason { get; set; }
    public string SaveSlot { get; set; }
    public bool Pinned { get; set; }
    // The files this archive says it could not read, as the archive itself recorded
    // them. Read back out of the metadata rather than remembered from the backup that
    // wrote it: by the time anyone restores, the process that made the archive is
    // several launches gone, and this is what a restore is refused on.
    public IReadOnlyList<string> SkippedFileNames { get; set; } = Array.Empty<string>();
    // The archive carries a metadata entry and it could not be read. Kept apart from an
    // empty SkippedFileNames because they are opposite answers: one says the archive
    // holds every file, the other says nobody knows. A restore refuses on both.
    public bool MetadataUnreadable { get; set; }
}

// One file the backup could not read. Both the status line and the archive metadata name these, because a
// backup that quietly dropped a file would be discovered at the worst possible moment.
internal sealed class AkronBackupSkippedFile {
    public string RelativePath { get; set; }
    public string Reason { get; set; }
}

public static class AkronBackupActions {
    private const string BackupFolderName = "AkronBackups";
    private const string MetadataEntryName = "_akron-backup.json";

    // Where a restore unpacks the archive and holds the folder's previous contents while it swaps them.
    internal const string RestoreWorkFolderName = "AkronRestore";
    private const string RestoreExtractedFolderName = "extracted";
    private const string RestorePreviousFolderName = "previous";

    // The folders under Saves that belong to Akron's own runtime rather than to the player's save data. A
    // backup never carries one, and a restore never moves, replaces or removes one.
    //
    // What puts a folder on this list: Akron opens, loads or hands its contents to a child process and
    // cannot let go of them while the game runs. That matters because of two separate Windows mechanisms
    // and is invisible on the Linux dev and test machines. Any open file handle beneath a folder blocks a
    // rename of that folder, regardless of its share mode, so a restore safely refuses. A loaded executable
    // image is different: Windows refuses to delete the mapped file but permits renaming both the file and
    // its parent folder, so only this list keeps a restore from moving the image aside and replacing it.
    // Linux renames and unlinks open and mapped files without complaint and enforces no share modes, so a
    // Linux run cannot distinguish either case. Saves/AkronNative/<rid>/cimgui.dll is the case that proved
    // it: Akron loads it at startup on every platform, and a restore deleted all 18 save files, threw on
    // that one DLL, extracted nothing, and left the player with no save files at all.
    //
    // Saves/AkronLogs and Saves/.tmp-perf are deliberately not on this list. Akron owns those two handles
    // and can close them, which ReleaseFilesAkronHoldsInSaves does immediately before the swap, so they
    // round-trip through a backup like any other file.
    private static readonly string[] AkronOwnedFolderNames = {
        // The archives themselves. A backup that carried the older ones would double in size every time.
        BackupFolderName,

        // One saved room state per StartPos slot. These measured 207-238 MB per startup backup on the test
        // box against a few hundred KB of actual save files, so the 1024 MB total-size cap held about four
        // backups and retention pruned to its keep-at-least floor on the fifth boot - the player's real
        // backup history destroyed by a feature they never asked to have backed up.
        //
        // A save backup is a copy of the save files. StartPos snapshots are neither: they are a practice
        // cache, regenerable by setting the slot again, and they already have their own transport in setup
        // packs. The consequence, stated where it is decided: a restore does not rewind StartPos saved
        // states with the save file. A slot the player has re-set since the backup was taken loads what it
        // holds now, not what it held then.
        AkronStartPosReconstruction.SnapshotDirectoryName,

        // The cimgui build Akron extracts from its own zip and loads at module load, unconditionally, for
        // the lifetime of the process. Windows refuses to delete the loaded image but permits renaming it
        // and the folder above it, so this name, not a failed rename, keeps a restore away from the library.
        // There is nothing to lose by leaving it: it is written again on the next launch if it goes missing.
        AkronImGuiRenderer.NativeDirectoryName,

        // Video output. ffmpeg is a child process holding its output file, and the replay buffer's segment
        // files, open for as long as it records; Akron cannot make it let go without throwing away the
        // recording the player is making. These are also not save data, and a few minutes of them outweigh
        // every save file in the folder several times over.
        AkronInternalRecorder.DefaultOutputFolderName,

        // A player-supplied ffmpeg binary, which is a loaded executable image while a recording runs.
        AkronInternalRecorder.ToolsFolderName,

        // The automation queue's command and result files. The command file is read with FileShare.None, and
        // the result file is written by the very command that asked for the restore.
        AkronAutomationService.DirectoryName,

        // Where a restore does its own work, so the next restore never picks it up and no backup carries it.
        RestoreWorkFolderName
    };

    // The share mode every file in Saves is read with while it is archived.
    //
    // This is about Windows, and it is invisible on Linux because Linux does not enforce share modes at all.
    // Windows refuses to open a file for reading when another handle holds it for writing, unless the reader
    // also permits writing. Akron holds two files under Saves open for writing while the game runs:
    // Saves/AkronLogs/akron-current.log for the whole session (Source/Core/akron-log.cs) and the performance
    // recorder's JSONL while a recording is active (Source/Core/akron-performance-telemetry.cs). So
    // ZipFile.CreateEntryFromFile, which hardcodes FileShare.Read, is a sharing violation on Windows for
    // exactly the files Akron writes itself.
    //
    // FileShare.Delete is the other half: without it, holding a file open here would block log rotation from
    // renaming akron-current.log and block retention from deleting files, for as long as the backup runs.
    internal const FileShare BackupSourceShare = FileShare.ReadWrite | FileShare.Delete;

    // The ZIP format stores MS-DOS timestamps and cannot represent anything outside this range.
    private const int MinimumZipYear = 1980;
    private const int MaximumZipYear = 2107;
    private const int MaxSkippedFilesNamedInStatus = 3;

    // S_IFREG. Unix ZIP entries carry the file type alongside the permission bits.
    private const int RegularFileTypeBits = 0x8000;

    private static readonly object Sync = new object();
    private static IReadOnlyList<AkronBackupEntry> cachedBackups;
    private static bool backupListDirty = true;
    private static string backupFolderOverrideForQa;
    private static bool startupBackupAttempted;
    private static double intervalSecondsUntilNextCheck = 5.0;
    private static double levelBeginSecondsUntilNextAllowed;

    internal static int BackupListScanCountForQa { get; private set; }

    public static string LastStatus { get; private set; } = "No backup yet.";

    public static string BackupFolder =>
        backupFolderOverrideForQa ?? Path.Combine(GetSavesFolder(), BackupFolderName);

    public static string LastStatusForDisplay => FormatBackupTextForDisplay(LastStatus, BackupFolder, AkronModule.Settings.StreamerMode);

    public static string BackupFolderForDisplay => AkronModule.Settings.FormatPathForDisplay(BackupFolder);

    public static void NotifyStartupReady() {
        if (startupBackupAttempted || !AkronModule.Settings.BackupsEnabled || !AkronModule.Settings.BackupsOnStartup) {
            return;
        }

        startupBackupAttempted = true;
        CreateBackup("startup");
    }

    public static void NotifyShutdown() {
        if (!AkronModule.Settings.BackupsEnabled || !AkronModule.Settings.BackupsOnShutdown) {
            return;
        }

        CreateBackup("shutdown");
    }

    public static void NotifyLevelBegin(Level level) {
        if (!AkronModule.Settings.BackupsEnabled || !AkronModule.Settings.BackupsOnLevelBegin || levelBeginSecondsUntilNextAllowed > 0.0) {
            return;
        }

        levelBeginSecondsUntilNextAllowed = 30.0;
        CreateBackup("level-begin");
    }

    public static void UpdateInterval(float deltaSeconds) {
        if (levelBeginSecondsUntilNextAllowed > 0.0) {
            levelBeginSecondsUntilNextAllowed = Math.Max(0.0, levelBeginSecondsUntilNextAllowed - Math.Max(0f, deltaSeconds));
        }

        if (!AkronModule.Settings.BackupsEnabled || !AkronModule.Settings.BackupsEveryInterval) {
            return;
        }

        intervalSecondsUntilNextCheck -= Math.Max(0f, deltaSeconds);
        if (intervalSecondsUntilNextCheck > 0.0) {
            return;
        }

        int intervalMinutes = ClampBackupIntervalMinutes(AkronModule.Settings.BackupsIntervalMinutes);
        intervalSecondsUntilNextCheck = Math.Max(30.0, intervalMinutes * 60.0);
        DateTime lastBackup = GetLastBackupUtc();
        if (lastBackup == DateTime.MinValue || DateTime.UtcNow - lastBackup >= TimeSpan.FromMinutes(intervalMinutes)) {
            CreateBackup("interval");
        }
    }

    public static bool ShouldBackupBeforeSave(bool file, bool settings) {
        return AkronModule.Settings.BackupsEnabled &&
               AkronModule.Settings.BackupsOnSave &&
               (file || settings);
    }

    public static bool CreateBackup(string reason = "manual", bool showToast = true) {
        return TryCreateBackup(reason, showToast, out _);
    }

    // Reports the files the archive could not include as well as whether it was written at all. Restore needs
    // both: it is about to delete every save file, so a safety backup that is merely "created" is not enough.
    internal static bool TryCreateBackup(string reason, bool showToast, out IReadOnlyList<AkronBackupSkippedFile> skipped) {
        skipped = Array.Empty<AkronBackupSkippedFile>();
        lock (Sync) {
            try {
                string savesFolder = GetSavesFolder();
                if (!Directory.Exists(savesFolder)) {
                    return Fail("Backup failed: Saves folder not found.", showToast);
                }

                Directory.CreateDirectory(BackupFolder);
                string backupPath = BuildBackupPath(reason);
                skipped = WriteSavesArchive(
                    savesFolder,
                    backupPath,
                    skippedFiles => BuildMetadataJson(reason, skippedFiles));

                if (!VerifyZipReadable(backupPath)) {
                    File.Delete(backupPath);
                    return Fail("Backup failed: created ZIP could not be read.", showToast);
                }

                InvalidateBackupList();
                AkronModule.Settings.BackupsLastBackupUtcTicks = DateTime.UtcNow.Ticks;
                ApplyRetention();
                LastStatus = "Backup created: " + Path.GetFileName(backupPath) + DescribeSkippedFiles(skipped);
                if (skipped.Count > 0) {
                    Logger.Log(LogLevel.Warn, nameof(AkronModule), "Backup could not read " + skipped.Count +
                        " file(s): " + string.Join("; ", skipped.Select(entry => entry.RelativePath + ": " + entry.Reason)));
                }

                if (showToast) {
                    Toast(LastStatus);
                }
                return true;
            } catch (Exception exception) {
                return Fail("Backup failed: " + exception.Message, showToast);
            }
        }
    }

    // Writes the archive and returns the files it could not include. Separated from CreateBackup so the
    // archiving contract can be exercised directly against a real Saves-shaped directory: everything
    // game-shaped (settings, toasts, retention, the toast text) stays in CreateBackup.
    //
    // buildMetadataJson receives the skipped list, so the archive itself carries the record of what is
    // missing from it. The metadata entry is written last for that reason.
    //
    // Throwing means no usable archive was produced, and the half-written file is removed before the
    // exception leaves: a truncated ZIP sitting in the backup folder would be listed as a backup and offered
    // for restore.
    internal static IReadOnlyList<AkronBackupSkippedFile> WriteSavesArchive(
        string savesFolder,
        string backupPath,
        Func<IReadOnlyList<AkronBackupSkippedFile>, string> buildMetadataJson) {
        try {
            return WriteSavesArchiveCore(savesFolder, backupPath, buildMetadataJson);
        } catch {
            try {
                if (File.Exists(backupPath)) {
                    File.Delete(backupPath);
                }
            } catch {
                // The original failure is the one worth reporting, and it is already on its way up. A partial
                // archive that survives this is still not restorable: ZipFile.OpenRead rejects it, so it lists
                // as "unreadable" in the browser and VerifyZipReadable turns a restore of it away.
            }

            throw;
        }
    }

    private static IReadOnlyList<AkronBackupSkippedFile> WriteSavesArchiveCore(
        string savesFolder,
        string backupPath,
        Func<IReadOnlyList<AkronBackupSkippedFile>, string> buildMetadataJson) {
        List<AkronBackupSkippedFile> skipped = new List<AkronBackupSkippedFile>();
        using (ZipArchive archive = ZipFile.Open(backupPath, ZipArchiveMode.Create)) {
            foreach (string file in EnumerateFilesToArchive(savesFolder)) {
                string relativePath = GetRelativePath(savesFolder, file);
                FileStream source;
                try {
                    source = new FileStream(file, FileMode.Open, FileAccess.Read, BackupSourceShare);
                } catch (Exception exception) {
                    // Only the open is tolerated per file. A sharing violation, a missing file, a file deleted
                    // while we walked the folder and a permission error all land here, and none of them may
                    // cost the player everything else in Saves. The backup finishes and says what is missing
                    // from it, here and in the archive metadata.
                    //
                    // A failure once the entry exists is deliberately not caught: ZipArchiveMode.Create cannot
                    // remove an entry, so the archive would carry a truncated file that VerifyZipReadable
                    // accepts and a restore would write over a good save. That fails the whole backup instead,
                    // and WriteSavesArchive deletes the half-written archive on its way out.
                    skipped.Add(new AkronBackupSkippedFile {
                        RelativePath = relativePath,
                        Reason = exception.Message
                    });
                    continue;
                }

                using (source) {
                    AddFileEntry(archive, source, file, relativePath);
                }
            }

            ZipArchiveEntry metadataEntry = archive.CreateEntry(MetadataEntryName, CompressionLevel.Optimal);
            using StreamWriter writer = new StreamWriter(metadataEntry.Open(), Encoding.UTF8);
            writer.Write(buildMetadataJson(skipped));
        }

        return skipped;
    }

    // Every file under Saves that belongs to the player, with the folders Akron runs out of pruned at the
    // top level rather than filtered file by file. Every one of those folders is a top-level name, so
    // pruning there covers all of them and keeps the archive out of the StartPos snapshot tree and out of
    // the backup folder's own zips, neither of which it has any reason to read.
    private static IEnumerable<string> EnumerateFilesToArchive(string savesFolder) {
        string[] ownedFolders = BuildAkronOwnedFolders(savesFolder);
        foreach (string entry in Directory.EnumerateFileSystemEntries(savesFolder)) {
            if (IsAkronOwnedPath(ownedFolders, Path.GetFullPath(entry))) {
                continue;
            }

            if (!Directory.Exists(entry)) {
                yield return entry;
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(entry, "*", SearchOption.AllDirectories)) {
                yield return file;
            }
        }
    }

    private static void AddFileEntry(ZipArchive archive, FileStream source, string path, string entryName) {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        entry.LastWriteTime = ClampToZipTimestampRange(File.GetLastWriteTime(path));
        // ZipFile.CreateEntryFromFile stored the Unix mode here and ExtractToDirectory puts it back, so
        // dropping it would widen the automation service's owner-only files to whatever the umask allows the
        // first time a Linux or macOS player restored a backup. This assigns rather than ORs because
        // CreateEntry already defaults the field to a regular file at 0644, which no OR could narrow.
        // Windows entries carry no mode, the same as before.
        if (!OperatingSystem.IsWindows()) {
            entry.ExternalAttributes = (RegularFileTypeBits | (int) File.GetUnixFileMode(source.SafeFileHandle)) << 16;
        }

        using Stream destination = entry.Open();
        source.CopyTo(destination);
    }

    private static DateTimeOffset ClampToZipTimestampRange(DateTime value) {
        return value.Year < MinimumZipYear || value.Year > MaximumZipYear
            ? new DateTimeOffset(new DateTime(MinimumZipYear, 1, 1, 0, 0, 0))
            : new DateTimeOffset(value);
    }

    private static string DescribeSkippedFiles(IReadOnlyList<AkronBackupSkippedFile> skipped) {
        return DescribeSkippedFileNames(skipped.Select(entry => entry.RelativePath).ToList());
    }

    // Kept short enough for a toast, which is a hard constraint rather than a taste: a
    // toast is drawn as one unwrapped line of ActiveFont, so a message naming every file
    // and its reason runs off the side of the screen. Names and a count here; the archive
    // metadata carries the full list with reasons, and the log line at backup time
    // carries them too.
    private static string DescribeSkippedFileNames(IReadOnlyList<string> names) {
        if (names.Count == 0) {
            return string.Empty;
        }

        string named = string.Join(", ", names.Take(MaxSkippedFilesNamedInStatus));
        string more = names.Count > MaxSkippedFilesNamedInStatus
            ? ", +" + (names.Count - MaxSkippedFilesNamedInStatus).ToString(CultureInfo.InvariantCulture) + " more"
            : string.Empty;
        return " (could not read " + names.Count.ToString(CultureInfo.InvariantCulture) + ": " + named + more + ")";
    }

    // Why the selected backup must not be restored, or an empty string when it may be.
    //
    // The rule is the same one the pre-restore backup is held to, and it has to be,
    // because a restore applies an archive the same way whichever end it came from. Phase
    // one moves the live 0.celeste into `previous`, phase two finds no 0.celeste in the
    // archive to move back, and DiscardRestoreWorkFolder then removes `previous`. So
    // restoring a backup that could not read 0.celeste takes the player's 0.celeste away,
    // and before this refusal existed nothing said so - the file was recoverable only from
    // the pre-restore archive, by a player who worked out for themselves what had happened.
    //
    // This refuses rather than warning because there is nowhere to put a warning.
    // RestoreBackup only raises a confirmation prompt when the scene is a Level; at the
    // main menu, which is where a player with no save open restores from and which the
    // feature guide describes as the ordinary case, RestoreBackupConfirmed is called
    // directly and no prompt is shown at all. Refusing also leaves the live files where
    // they are, which is the safe direction, and costs nothing that cannot be had another
    // way: the archive is an ordinary ZIP in a folder the Backups tab opens, so a player
    // who wants what it does hold can take it out by hand.
    //
    // Read out of the archive here rather than off the entry the browser is holding. That
    // entry was built when the backup list was last scanned, and this decides whether the
    // player's save files are replaced, so the record it reads has to be the one inside
    // the file that is about to be unpacked. It is one small entry out of a ZIP that is
    // about to be unpacked whole, so the second read costs nothing worth counting.
    //
    // Separated from RestoreBackupConfirmed so the decision can be exercised without a
    // game. Everything around it needs Everest.PathGame, a scene and an open save.
    internal static string DescribeRestoreRefusal(string backupPath) {
        AkronBackupEntry archive = ReadBackupEntry(backupPath);
        if (archive.MetadataUnreadable) {
            // Fail closed. An archive whose own record of what it skipped cannot be read is
            // not an archive that skipped nothing, and only one of those is safe to unpack
            // over the player's save files. Reachable for a backup written by a build whose
            // metadata writer could still put a raw control character inside a reason, which
            // is not valid JSON and therefore not readable now.
            return "Restore stopped: the backup you picked does not say which files it holds.";
        }

        return archive.SkippedFileNames.Count == 0
            ? string.Empty
            : "Restore stopped: the backup you picked" + DescribeSkippedFileNames(archive.SkippedFileNames);
    }

    public static void OpenBackupFolder() {
        try {
            Directory.CreateDirectory(BackupFolder);
            StartShellOpen(BackupFolder);
            LastStatus = "Opened backup folder.";
        } catch (Exception exception) {
            LastStatus = "Open folder failed: " + exception.Message;
            Toast(LastStatus);
        }
    }

    public static IReadOnlyList<AkronBackupEntry> ListBackups() {
        lock (Sync) {
            if (!backupListDirty) {
                return cachedBackups;
            }

            if (TryScanBackups(out IReadOnlyList<AkronBackupEntry> backups)) {
                cachedBackups = backups;
                backupListDirty = false;
            }

            return backups;
        }
    }

    internal static IReadOnlyList<AkronBackupEntry> RefreshBackups() {
        lock (Sync) {
            InvalidateBackupList();
            return ListBackups();
        }
    }

    private static bool TryScanBackups(out IReadOnlyList<AkronBackupEntry> backups) {
        BackupListScanCountForQa++;
        try {
            if (!Directory.Exists(BackupFolder)) {
                backups = Array.Empty<AkronBackupEntry>();
                return true;
            }

            backups = Directory.EnumerateFiles(BackupFolder, "*.zip", SearchOption.TopDirectoryOnly)
                .Select(ReadBackupEntry)
                .OrderByDescending(entry => entry.CreatedUtc)
                .ToList();
            return true;
        } catch (Exception exception) {
            LastStatus = "Backup list failed: " + exception.Message;
            backups = Array.Empty<AkronBackupEntry>();
            return false;
        }
    }

    private static void InvalidateBackupList() {
        lock (Sync) {
            backupListDirty = true;
        }
    }

    internal static void SetBackupFolderForQa(string backupFolder) {
        lock (Sync) {
            backupFolderOverrideForQa = backupFolder;
            InvalidateBackupList();
        }
    }

    public static void RestoreBackup(AkronBackupEntry backup) {
        if (backup == null || string.IsNullOrWhiteSpace(backup.Path) || !File.Exists(backup.Path)) {
            LastStatus = "Restore failed: backup file missing.";
            Toast(LastStatus);
            return;
        }

        if (Engine.Scene is Level level) {
            AkronPromptMenu.Show(
                level,
                "Restore Backup",
                "Restore " + backup.FileName + "?\nA pre-restore backup will be created first.",
                new AkronPromptOption("Restore", () => RestoreBackupConfirmed(backup)));
            return;
        }

        RestoreBackupConfirmed(backup);
    }

    internal static void RestoreBackupForQa(AkronBackupEntry backup) {
        RestoreBackupConfirmed(backup);
    }

    internal static void ApplyRetentionForQa() {
        lock (Sync) {
            InvalidateBackupList();
            ApplyRetention();
        }
    }

    public static void SetPinned(AkronBackupEntry backup, bool pinned) {
        if (backup == null || string.IsNullOrWhiteSpace(backup.Path)) {
            return;
        }

        try {
            string pinPath = GetPinPath(backup.Path);
            if (pinned) {
                File.WriteAllText(pinPath, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            } else if (File.Exists(pinPath)) {
                File.Delete(pinPath);
            }

            backup.Pinned = pinned;
            InvalidateBackupList();
            LastStatus = pinned ? "Pinned backup: " + backup.FileName : "Unpinned backup: " + backup.FileName;
            Toast(LastStatus);
        } catch (Exception exception) {
            LastStatus = "Pin update failed: " + exception.Message;
            Toast(LastStatus);
        }
    }

    public static string DescribeLastBackup() {
        DateTime lastBackup = GetLastBackupUtc();
        if (lastBackup == DateTime.MinValue) {
            return "Never";
        }

        TimeSpan age = DateTime.UtcNow - lastBackup;
        if (age.TotalMinutes < 1.0) {
            return "Just now";
        }

        if (age.TotalHours < 1.0) {
            return ((int) age.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " min ago";
        }

        if (age.TotalDays < 1.0) {
            return ((int) age.TotalHours).ToString(CultureInfo.InvariantCulture) + " hr ago";
        }

        return lastBackup.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    public static string DescribeBackupSummary() {
        IReadOnlyList<AkronBackupEntry> backups = ListBackups();
        if (backups.Count == 0) {
            return "0 backups";
        }

        return backups.Count + " backups | Last " + DescribeLastBackup();
    }

    public static int ClampBackupIntervalMinutes(int value) {
        return Math.Max(1, Math.Min(1440, value));
    }

    public static int ClampBackupRetentionDays(int value) {
        return Math.Max(0, Math.Min(3650, value));
    }

    public static int ClampBackupMaxCount(int value) {
        return Math.Max(1, Math.Min(10000, value));
    }

    public static int ClampBackupKeepAtLeast(int value) {
        return Math.Max(0, Math.Min(10000, value));
    }

    public static int ClampBackupMaxSizeMb(int value) {
        return Math.Max(0, Math.Min(1024 * 1024, value));
    }

    private static void RestoreBackupConfirmed(AkronBackupEntry backup) {
        lock (Sync) {
            try {
                string savesFolder = GetSavesFolder();
                if (!Directory.Exists(savesFolder)) {
                    Fail("Restore failed: Saves folder not found.", true);
                    return;
                }

                if (!VerifyZipReadable(backup.Path)) {
                    Fail("Restore failed: backup ZIP is not readable.", true);
                    return;
                }

                // Before the pre-restore backup rather than after it: a restore that is going
                // to be refused should not leave a backup behind to explain.
                string refusal = DescribeRestoreRefusal(backup.Path);
                if (!string.IsNullOrEmpty(refusal)) {
                    LastStatus = refusal;
                    Toast(LastStatus);
                    return;
                }

                // Everything below replaces the player's save files, so the safety net has to be complete. A
                // pre-restore backup that is merely written is not enough: a file it could not read is a file
                // that is about to be replaced with no copy of it anywhere. RestoreSavesFolder is written so
                // that a failure leaves the save files untouched, and this is what covers the case it cannot
                // - a restore that succeeds and turns out to have been the wrong backup.
                if (!TryCreateBackup("pre-restore", false, out IReadOnlyList<AkronBackupSkippedFile> preRestoreSkipped)) {
                    LastStatus = "Restore stopped: pre-restore backup failed.";
                    Toast(LastStatus);
                    return;
                }

                if (preRestoreSkipped.Count > 0) {
                    LastStatus = "Restore stopped: pre-restore backup" + DescribeSkippedFiles(preRestoreSkipped);
                    Toast(LastStatus);
                    return;
                }

                RestoreSavesFolder(
                    savesFolder,
                    backup.Path,
                    Path.Combine(savesFolder, RestoreWorkFolderName, DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture)),
                    ReleaseFilesAkronHoldsInSaves);

                bool reloaded = TryReloadOpenSaveData(out string reloadMessage);

                // The overworld is rebuilt either way. A new Overworld builds a new OuiFileSelect, and
                // that is what reads the restored files off disk, so it is how the restore becomes
                // visible when there was no save open to reload.
                Engine.Scene = new OverworldLoader(Overworld.StartMode.MainMenu);
                LastStatus = reloaded
                    ? "Restored backup: " + backup.FileName
                    : "Restored backup: " + backup.FileName + ", but the open save could not be reloaded: " + reloadMessage;
                Toast(LastStatus);
            } catch (Exception exception) {
                Fail("Restore failed: " + exception.Message, true);
            }
        }
    }

    private static void ApplyRetention() {
        IReadOnlyList<AkronBackupEntry> backups = ListBackups();
        if (backups.Count == 0) {
            return;
        }

        int keepAtLeast = Math.Min(ClampBackupKeepAtLeast(AkronModule.Settings.BackupsKeepAtLeast), backups.Count);
        HashSet<string> protectedPaths = backups
            .Where(entry => entry.Pinned)
            .Concat(backups.Take(keepAtLeast))
            .Select(entry => entry.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<AkronBackupEntry> delete = new List<AkronBackupEntry>();

        int maxCount = ClampBackupMaxCount(AkronModule.Settings.BackupsMaxCount);
        if (backups.Count > maxCount) {
            delete.AddRange(backups.Skip(maxCount).Where(entry => !protectedPaths.Contains(entry.Path)));
        }

        int maxAgeDays = ClampBackupRetentionDays(AkronModule.Settings.BackupsDeleteOlderThanDays);
        if (maxAgeDays > 0) {
            DateTime cutoff = DateTime.UtcNow - TimeSpan.FromDays(maxAgeDays);
            delete.AddRange(backups.Where(entry => entry.CreatedUtc < cutoff && !protectedPaths.Contains(entry.Path)));
        }

        long maxSizeBytes = ClampBackupMaxSizeMb(AkronModule.Settings.BackupsMaxTotalSizeMb) * 1024L * 1024L;
        if (maxSizeBytes > 0) {
            long totalSize = backups.Sum(entry => entry.SizeBytes);
            foreach (AkronBackupEntry entry in backups.OrderBy(entry => entry.CreatedUtc)) {
                if (totalSize <= maxSizeBytes) {
                    break;
                }

                if (protectedPaths.Contains(entry.Path)) {
                    continue;
                }

                delete.Add(entry);
                totalSize -= entry.SizeBytes;
            }
        }

        foreach (AkronBackupEntry entry in delete.DistinctBy(entry => entry.Path)) {
            try {
                File.Delete(entry.Path);
                string pinPath = GetPinPath(entry.Path);
                if (File.Exists(pinPath)) {
                    File.Delete(pinPath);
                }
            } catch (Exception exception) {
                LastStatus = "Retention failed: " + exception.Message;
            }
        }

        InvalidateBackupList();
    }

    // Lets go of the two files under Saves that Akron itself keeps open while the game runs, immediately
    // before a restore starts moving that folder around.
    //
    // This is a Windows problem and is invisible on Linux. Renaming either containing folder fails while
    // any file handle remains open beneath it, regardless of the handle's share mode. The log reopens itself
    // on the next line written.
    private static void ReleaseFilesAkronHoldsInSaves() {
        // The log goes last on purpose. StopRecording can write a warning line through AkronLog when the GC
        // event listener refuses to stop, and that line would reopen the handle we had just let go of.
        AkronPerformanceTelemetry.StopRecording();
        AkronLog.CloseLogFile();
    }

    // Puts the archive's copy of the save files back, in an order where no failure can leave the player
    // without both copies.
    //
    // The order is the whole point, and it is what the first version of this got wrong: it deleted every
    // save file and then extracted, so anything that went wrong in between cost the player everything. Here
    // the archive is unpacked into a staging folder first, which puts the step most likely to fail - a
    // corrupt member, a full disk, a name the filesystem will not take - before anything under Saves has
    // changed. Only then does the live folder change, and it changes by renaming, all of it or none of it:
    // SwapSavesFolderContents undoes every move it has made if it cannot finish, so a restore that stops
    // leaves the save files exactly as they were. Discarding the moved-aside copy is the last thing that
    // happens, after the restore has already succeeded.
    //
    // That ordering is what makes this safe rather than the list of folders a restore skips. A file Akron
    // does not know it is holding, a file another program has open or a folder added under Saves next year
    // cannot cost save data, because nothing is discarded until the swap has already worked. An open handle
    // beneath a folder makes its rename refuse, which is the safe direction. A loaded image does not: its
    // folder can move and only deleting the moved-aside image fails after the restored files are in place.
    //
    // A move can fail at all because of Windows, and none of it is visible on the Linux dev and test
    // machines: Windows blocks a folder rename when any open handle is beneath it, regardless of share mode,
    // but a loaded executable image is a mapped section rather than an open handle and does not block that
    // rename. Linux renames and unlinks both open and mapped files without complaint and enforces no share
    // modes at all. A Linux run cannot tell a safe restore from a destructive one, which is how the
    // destructive one shipped.
    //
    // releaseHeldFiles is called between the unpacking and the first change to Saves, so a restore that
    // stops while unpacking never costs the player a running performance recording.
    internal static void RestoreSavesFolder(string savesFolder, string backupPath, string workFolder, Action releaseHeldFiles) {
        string extracted = Path.Combine(workFolder, RestoreExtractedFolderName);
        string previous = Path.Combine(workFolder, RestorePreviousFolderName);
        Directory.CreateDirectory(extracted);
        Directory.CreateDirectory(previous);

        try {
            ZipFile.ExtractToDirectory(backupPath, extracted, overwriteFiles: true);
            string metadataPath = Path.Combine(extracted, MetadataEntryName);
            if (File.Exists(metadataPath)) {
                File.Delete(metadataPath);
            }
        } catch (Exception exception) {
            DiscardRestoreWorkFolder(savesFolder, workFolder);
            throw new IOException(
                "could not unpack the backup: " + exception.Message + " Your save files were not changed.",
                exception);
        }

        releaseHeldFiles();

        try {
            SwapSavesFolderContents(savesFolder, extracted, previous);
        } catch {
            // SwapSavesFolderContents undoes every move it made before it throws, and an empty `previous` is
            // the proof that it did. A `previous` with anything left in it still holds save files, and
            // removing it would be exactly the loss this method exists to prevent, so it stays where the
            // exception message says it is.
            if (!Directory.EnumerateFileSystemEntries(previous).Any()) {
                DiscardRestoreWorkFolder(savesFolder, workFolder);
            }

            throw;
        }

        DiscardRestoreWorkFolder(savesFolder, workFolder);
    }

    // The one step that changes the player's Saves folder, and it changes it all at once or not at all.
    //
    // Two phases: everything Saves holds moves into `previous`, then everything unpacked moves in. If either
    // phase stops for any reason, every move already made is undone in reverse and the folder is left the
    // way it was found. Undoing the second phase first is what frees the names the first phase needs to move
    // back into.
    //
    // Top-level entries rather than individual files, so a whole folder moves in one rename. That also means
    // a folder holding one file nobody can let go of fails as a folder, which is the safe direction: the
    // restore stops without having taken anything away.
    //
    // The entries are read into an array before any of them moves. Renaming entries out of a directory that
    // is still being enumerated can skip the ones that have not been reached yet, and a save file skipped
    // here is a save file that would silently survive a restore.
    private static void SwapSavesFolderContents(string savesFolder, string extracted, string previous) {
        string[] ownedFolders = BuildAkronOwnedFolders(savesFolder);
        List<string> movedAside = new List<string>();
        List<string> movedIn = new List<string>();
        try {
            foreach (string entry in ReadEntriesInAStableOrder(savesFolder)) {
                if (IsAkronOwnedPath(ownedFolders, Path.GetFullPath(entry))) {
                    continue;
                }

                string name = Path.GetFileName(entry);
                MoveEntry(entry, Path.Combine(previous, name));
                movedAside.Add(name);
            }

            foreach (string entry in ReadEntriesInAStableOrder(extracted)) {
                string name = Path.GetFileName(entry);
                // An entry Akron owns is dropped rather than moved in, because the live one was not taken
                // out: an archive written before those folders were excluded still carries copies of them,
                // and the copy of a loaded native library is precisely what a restore must not write over.
                if (IsAkronOwnedPath(ownedFolders, Path.GetFullPath(Path.Combine(savesFolder, name)))) {
                    continue;
                }

                MoveEntry(entry, Path.Combine(savesFolder, name));
                movedIn.Add(name);
            }
        } catch (Exception swapFailure) {
            try {
                foreach (string name in movedIn) {
                    MoveEntry(Path.Combine(savesFolder, name), Path.Combine(extracted, name));
                }

                foreach (string name in movedAside) {
                    MoveEntry(Path.Combine(previous, name), Path.Combine(savesFolder, name));
                }
            } catch (Exception rollbackFailure) {
                throw new IOException(
                    "could not replace the Saves folder (" + swapFailure.Message + ") and could not put back what it had already moved (" +
                    rollbackFailure.Message + "). Your save files are in " + previous + ".",
                    rollbackFailure);
            }

            throw new IOException(
                "could not replace the Saves folder: " + swapFailure.Message + " Your save files were not changed.",
                swapFailure);
        }
    }

    // Sorted so the same folder always moves in the same order. A step that can stop halfway is worth being
    // able to reproduce, and it means the point a restore gave up is the same on the second run as on the
    // first.
    private static string[] ReadEntriesInAStableOrder(string folder) {
        string[] entries = Directory.GetFileSystemEntries(folder);
        Array.Sort(entries, StringComparer.Ordinal);
        return entries;
    }

    // One rename to the filesystem either way; .NET splits it by type.
    private static void MoveEntry(string source, string destination) {
        if (Directory.Exists(source)) {
            Directory.Move(source, destination);
            return;
        }

        File.Move(source, destination);
    }

    private static void TryDeleteDirectory(string path) {
        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        } catch (Exception exception) {
            // Only ever called where the directory holds copies nobody needs any more, so a failure costs
            // disk space and nothing else. The restore it belongs to has already succeeded or already
            // reported why it did not.
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Could not remove restore working folder " + path + ": " + exception.Message);
        }
    }

    // Removes the folder one restore worked in, then Saves/AkronRestore itself if that was the last thing
    // left in it. Without the second half an empty AkronRestore stayed in Saves after every restore, which
    // took away the only meaning that folder had: that a restore did not finish.
    //
    // The container goes with a non-recursive delete, which is the whole race argument. Windows
    // RemoveDirectory fails with ERROR_DIR_NOT_EMPTY and Linux rmdir(2) fails with ENOTEMPTY, so the
    // emptiness check and the removal are the same syscall and there is no window in which this can take a
    // container out from under a work folder that is in it. Two restores cannot overlap inside one process
    // anyway - RestoreBackupConfirmed holds Sync for all of it - so what this covers is a second process
    // sharing the same Saves folder, and a work folder a crashed restore left behind.
    //
    // The one window that does exist: a second process that has created the container but not yet its work
    // folder inside it can have the container removed underneath it. Directory.CreateDirectory builds the
    // whole chain in one call, so that window is inside a single call, and losing it makes the create throw
    // before that restore has touched a single save file. A refusal, not lost data.
    private static void DiscardRestoreWorkFolder(string savesFolder, string workFolder) {
        TryDeleteDirectory(workFolder);

        try {
            // Composed from the constant rather than taken as the parent of workFolder, which would be the
            // Saves folder itself if a caller ever passed a work folder one level up.
            Directory.Delete(Path.Combine(savesFolder, RestoreWorkFolderName), recursive: false);
        } catch {
            // Nothing to report and nothing to do. A container that still holds something is one another
            // restore is working in or one a crashed restore left behind, and both are reasons to leave it
            // exactly where it is. Failing costs an empty directory, which is the whole of what this
            // method is tidying up.
        }
    }

    private static AkronBackupEntry ReadBackupEntry(string path) {
        FileInfo file = new FileInfo(path);
        string reason = string.Empty;
        string saveSlot = string.Empty;
        IReadOnlyList<string> skippedFileNames = Array.Empty<string>();
        bool metadataUnreadable = false;
        try {
            using ZipArchive archive = ZipFile.OpenRead(path);
            ZipArchiveEntry metadata = archive.GetEntry(MetadataEntryName);
            if (metadata != null) {
                // Through a StreamReader rather than straight off the entry stream, because
                // BuildMetadataJson is written with Encoding.UTF8 and that emits a byte-order
                // mark. StreamReader detects and drops it; a raw byte parse would not.
                using StreamReader reader = new StreamReader(metadata.Open(), Encoding.UTF8);
                using JsonDocument document = JsonDocument.Parse(reader.ReadToEnd());
                reason = ReadMetadataString(document.RootElement, "reason");
                saveSlot = ReadMetadataString(document.RootElement, "saveSlot");
                skippedFileNames = ReadSkippedFileNames(document.RootElement);
            }
        } catch {
            // Either the ZIP would not open or its metadata entry would not parse. Both
            // leave this process unable to say what the archive holds, and a restore has to
            // treat that as a reason to stop rather than as silence.
            reason = "unreadable";
            metadataUnreadable = true;
        }

        return new AkronBackupEntry {
            Path = path,
            FileName = file.Name,
            CreatedUtc = file.CreationTimeUtc > DateTime.MinValue ? file.CreationTimeUtc : file.LastWriteTimeUtc,
            SizeBytes = file.Length,
            Reason = reason,
            SaveSlot = saveSlot,
            SkippedFileNames = skippedFileNames,
            MetadataUnreadable = metadataUnreadable,
            Pinned = File.Exists(GetPinPath(path))
        };
    }

    private static string ReadMetadataString(JsonElement root, string key) {
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty(key, out JsonElement value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    // The names of the files a backup could not read, out of its own record of them.
    //
    // BuildMetadataJson writes each element as "<relative path>: <reason>", and only the
    // name is wanted here, because the message this feeds has to fit on one line. The
    // split is at the first ": ", so a save file whose own name contains that sequence is
    // named short in the message. That costs a few characters of text and nothing else:
    // the refusal is decided by how many entries there are, which no split can change.
    //
    // An element that is not a string still counts. It is a record of a file the archive
    // does not hold, however badly written, and the safe reading of a record nobody can
    // parse is not "nothing was skipped".
    private static IReadOnlyList<string> ReadSkippedFileNames(JsonElement root) {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("skippedFiles", out JsonElement skipped) ||
            skipped.ValueKind != JsonValueKind.Array) {
            return Array.Empty<string>();
        }

        List<string> names = new List<string>();
        foreach (JsonElement element in skipped.EnumerateArray()) {
            string record = element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : string.Empty;
            int separator = record.IndexOf(": ", StringComparison.Ordinal);
            names.Add(separator < 0 ? record : record.Substring(0, separator));
        }

        return names;
    }

    // Only an open save can be stale: Celeste writes SaveData.Instance back over its file on the next save,
    // so that in-memory copy must not overwrite the restored data. With no save open there is nothing to
    // correct, and starting a profile the player did not pick would load that slot's mod save data and mod
    // sessions behind their back. The stale slot is therefore the open one, not the backup's metadata slot;
    // those differ when a backup from one profile is restored while another profile is open. FileSlot -1 is
    // Celeste's real debug save rather than a no-save sentinel, which is why the check is on the instance.
    internal static bool TryReloadOpenSaveData(out string message) {
        SaveData open = SaveData.Instance;
        if (open == null) {
            message = string.Empty;
            return true;
        }

        // Neither of these can throw. FileSlot is a field, and SaveData.GetFilename either returns the slot
        // number or the literal "debug". Everything below them can, which is what the catch is for.
        int slot = open.FileSlot;
        string filename = SaveData.GetFilename(slot);

        try {
            SaveData restored = UserIO.Load<SaveData>(filename);
            if (restored != null) {
                SaveData.Start(restored, slot);
                message = string.Empty;
                return true;
            }

            message = "could not load " + filename;
        } catch (Exception exception) {
            // Everything here runs after the restored files are already on disk, so nothing that goes wrong
            // in it may reach the caller's "Restore failed" handler and tell the player their save files did
            // not come back. SaveData.Start runs every installed module's session load, so one mod throwing
            // is enough to reach this.
            message = "could not reload " + filename + ": " + exception.Message;
        }

        // The reload did not happen, so what is still in memory is either the pre-restore save or a
        // half-loaded one, and Celeste writes that back over its file at the next save - which would put the
        // data the restore just replaced straight back on top of the restored data. Dropping it leaves the
        // game with no save open, which is the state it boots into and the state the main menu the caller
        // returns to is built for.
        SaveData.Instance = null;
        return false;
    }

    private static bool VerifyZipReadable(string path) {
        try {
            using ZipArchive archive = ZipFile.OpenRead(path);
            foreach (ZipArchiveEntry entry in archive.Entries) {
                using Stream stream = entry.Open();
                if (entry.Length > 0) {
                    stream.ReadByte();
                }
            }

            return true;
        } catch {
            return false;
        }
    }

    private static string BuildBackupPath(string reason) {
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        string slot = SaveData.Instance == null ? "NoProfile" : "Slot" + SaveData.Instance.FileSlot.ToString(CultureInfo.InvariantCulture);
        string safeReason = SanitizeFileName(reason);
        string fileName = timestamp + "_" + slot + "_" + safeReason + ".zip";
        string path = Path.Combine(BackupFolder, fileName);
        int suffix = 2;
        while (File.Exists(path)) {
            path = Path.Combine(BackupFolder, timestamp + "_" + slot + "_" + safeReason + "_" + suffix.ToString(CultureInfo.InvariantCulture) + ".zip");
            suffix++;
        }

        return path;
    }

    private static string BuildMetadataJson(string reason, IReadOnlyList<AkronBackupSkippedFile> skippedFiles) {
        Level level = Engine.Scene as Level;
        IEnumerable<string> mods = Everest.Modules
            .Where(module => module?.Metadata != null && module.GetType().Name != "NullModule")
            .Select(module => JsonEscape(module.Metadata.Name + "@" + module.Metadata.VersionString))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("{");
        // Schema 2 because the archive contract changed: a schema 1 archive carried
        // Saves/AkronStartPos and a schema 2 one does not.
        builder.AppendLine("  \"schema\": 2,");
        builder.AppendLine("  \"reason\": \"" + JsonEscape(reason) + "\",");
        builder.AppendLine("  \"createdUtc\": \"" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "\",");
        builder.AppendLine("  \"gameVersion\": \"" + JsonEscape(Celeste.Instance?.Version?.ToString() ?? string.Empty) + "\",");
        builder.AppendLine("  \"modVersion\": \"" + JsonEscape(AkronModule.Instance?.Metadata?.VersionString ?? string.Empty) + "\",");
        builder.AppendLine("  \"saveSlot\": \"" + JsonEscape(SaveData.Instance == null ? string.Empty : SaveData.Instance.FileSlot.ToString(CultureInfo.InvariantCulture)) + "\",");
        builder.AppendLine("  \"profileName\": \"" + JsonEscape(SaveData.Instance?.Name ?? string.Empty) + "\",");
        builder.AppendLine("  \"area\": \"" + JsonEscape(level?.Session?.Area.GetSID() ?? string.Empty) + "\",");
        builder.AppendLine("  \"room\": \"" + JsonEscape(level?.Session?.Level ?? string.Empty) + "\",");
        builder.AppendLine("  \"mods\": [\"" + string.Join("\", \"", mods) + "\"],");
        // What a backup deliberately does not carry, named in the archive so someone
        // restoring one is not left to work it out from what is missing. A restore also
        // leaves these folders alone, so nothing in them is lost by restoring.
        builder.AppendLine("  \"excludedFolders\": [\"" +
            string.Join("\", \"", AkronOwnedFolderNames.Select(JsonEscape)) + "\"],");
        // Named in the archive itself so anyone restoring or debugging from a backup can see what it is
        // missing without having to have seen the toast that said so.
        builder.AppendLine("  \"skippedFiles\": [" + string.Join(", ", skippedFiles
            .Select(entry => "\"" + JsonEscape(entry.RelativePath + ": " + entry.Reason) + "\"")) + "]");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static DateTime GetLastBackupUtc() {
        long ticks = AkronModule.Settings.BackupsLastBackupUtcTicks;
        if (ticks <= 0) {
            return DateTime.MinValue;
        }

        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private static bool Fail(string message, bool showToast) {
        LastStatus = message;
        if (showToast) {
            Toast(message);
        }

        Logger.Log(LogLevel.Warn, nameof(AkronModule), message);
        return false;
    }

    private static void Toast(string message) {
        Engine.Scene?.Add(new AkronToast(message));
    }

    private static string GetSavesFolder() {
        return Path.Combine(Everest.PathGame, "Saves");
    }

    internal static string FormatBackupTextForDisplay(string text, string backupFolder, bool streamerMode) {
        if (!streamerMode || string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(backupFolder)) {
            return text ?? string.Empty;
        }

        string trimmed = Path.GetFullPath(backupFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string display = AkronModuleSettings.FormatPathForDisplay(trimmed, streamerMode);
        return text
            .Replace(trimmed, display)
            .Replace(trimmed.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), display);
    }

    // The one boundary between the player's save data and Akron's own runtime, used by the archive, by what
    // a restore moves aside, and by what a restore puts back. One predicate rather than one per caller,
    // because the three have to agree: an archive that carried a folder a restore leaves in place would try
    // to write over the live copy, and a restore that moved away a folder the archive does not carry would
    // take the player's StartPos slots with it and have nothing to put back.
    //
    // Every one of these is a folder directly under Saves, which is what lets all three callers apply the
    // boundary to a top-level entry and be done with it.
    //
    // Built once per walk. The archive opens every file under Saves and the maintainer's folder holds a
    // thousand of them, on the thread the game runs on.
    private static string[] BuildAkronOwnedFolders(string rootFolder) {
        string[] folders = new string[AkronOwnedFolderNames.Length];
        for (int index = 0; index < AkronOwnedFolderNames.Length; index++) {
            folders[index] = NormalizeFolderPath(Path.Combine(rootFolder, AkronOwnedFolderNames[index]));
        }

        return folders;
    }

    // Case-insensitively, because Windows and macOS resolve Saves/AKRONNATIVE to the folder holding the
    // loaded library and a comparison that missed it would put a restore right back where it started. The
    // cost on a case-sensitive filesystem is that a player folder differing only in case would be left out
    // of backups, which is the harmless direction to be wrong in.
    //
    // The separator check is what keeps AkronNativeExtra from matching AkronNative.
    private static bool IsAkronOwnedPath(string[] ownedFolders, string fullPath) {
        foreach (string folder in ownedFolders) {
            if (fullPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase) &&
                (fullPath.Length == folder.Length || fullPath[folder.Length] == Path.DirectorySeparatorChar)) {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeFolderPath(string path) {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string GetRelativePath(string root, string path) {
        Uri rootUri = new Uri(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
        Uri fileUri = new Uri(Path.GetFullPath(path));
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString()).Replace('\\', '/');
    }

    private static string SanitizeFileName(string value) {
        string fallback = string.IsNullOrWhiteSpace(value) ? "backup" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars()) {
            fallback = fallback.Replace(invalid, '-');
        }

        return fallback.Replace(' ', '-');
    }

    private static string GetPinPath(string backupPath) {
        return backupPath + ".pin";
    }

    // The metadata's only free-form text is a skipped file's reason, which is an exception
    // message, so this has to cover what one of those can hold. Every control character
    // has to go, not just the two line endings: JSON forbids a raw character below 0x20
    // inside a string, and one tab in a reason would make the whole file unparseable -
    // which is exactly the file a restore now reads to decide whether it may run.
    internal static string JsonEscape(string value) {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        StringBuilder escaped = new StringBuilder(value.Length);
        foreach (char character in value) {
            switch (character) {
                case '\\':
                    escaped.Append("\\\\");
                    break;
                case '"':
                    escaped.Append("\\\"");
                    break;
                case '\r':
                    escaped.Append("\\r");
                    break;
                case '\n':
                    escaped.Append("\\n");
                    break;
                case '\t':
                    escaped.Append("\\t");
                    break;
                default:
                    if (character < ' ') {
                        escaped.Append("\\u").Append(((int) character).ToString("x4", CultureInfo.InvariantCulture));
                    } else {
                        escaped.Append(character);
                    }
                    break;
            }
        }

        return escaped.ToString();
    }

    private static void StartShellOpen(string path) {
        string fileName;
        string arguments;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            fileName = "explorer.exe";
            arguments = "\"" + path + "\"";
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            fileName = "open";
            arguments = "\"" + path + "\"";
        } else {
            fileName = "xdg-open";
            arguments = "\"" + path + "\"";
        }

        using Process process = Process.Start(new ProcessStartInfo {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }
}
