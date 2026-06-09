using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
}

public static class AkronBackupActions {
    private const string BackupFolderName = "AkronBackups";
    private const string MetadataEntryName = "_akron-backup.json";
    private static readonly object Sync = new object();
    private static bool startupBackupAttempted;
    private static double intervalSecondsUntilNextCheck = 5.0;
    private static double levelBeginSecondsUntilNextAllowed;

    public static string LastStatus { get; private set; } = "No backup yet.";

    public static string BackupFolder => Path.Combine(GetSavesFolder(), BackupFolderName);

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
        lock (Sync) {
            try {
                string savesFolder = GetSavesFolder();
                if (!Directory.Exists(savesFolder)) {
                    return Fail("Backup failed: Saves folder not found.", showToast);
                }

                Directory.CreateDirectory(BackupFolder);
                string backupPath = BuildBackupPath(reason);
                using (ZipArchive archive = ZipFile.Open(backupPath, ZipArchiveMode.Create)) {
                    foreach (string file in Directory.EnumerateFiles(savesFolder, "*", SearchOption.AllDirectories)) {
                        if (IsInsideBackupFolder(file)) {
                            continue;
                        }

                        string relativePath = GetRelativePath(savesFolder, file);
                        archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Optimal);
                    }

                    ZipArchiveEntry metadataEntry = archive.CreateEntry(MetadataEntryName, CompressionLevel.Optimal);
                    using StreamWriter writer = new StreamWriter(metadataEntry.Open(), Encoding.UTF8);
                    writer.Write(BuildMetadataJson(reason));
                }

                if (!VerifyZipReadable(backupPath)) {
                    File.Delete(backupPath);
                    return Fail("Backup failed: created ZIP could not be read.", showToast);
                }

                AkronModule.Settings.BackupsLastBackupUtcTicks = DateTime.UtcNow.Ticks;
                ApplyRetention();
                LastStatus = "Backup created: " + Path.GetFileName(backupPath);
                if (showToast) {
                    Toast(LastStatus);
                }
                return true;
            } catch (Exception exception) {
                return Fail("Backup failed: " + exception.Message, showToast);
            }
        }
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
        try {
            if (!Directory.Exists(BackupFolder)) {
                return Array.Empty<AkronBackupEntry>();
            }

            return Directory.EnumerateFiles(BackupFolder, "*.zip", SearchOption.TopDirectoryOnly)
                .Select(ReadBackupEntry)
                .OrderByDescending(entry => entry.CreatedUtc)
                .ToList();
        } catch (Exception exception) {
            LastStatus = "Backup list failed: " + exception.Message;
            return Array.Empty<AkronBackupEntry>();
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

                if (!CreateBackup("pre-restore", false)) {
                    LastStatus = "Restore stopped: pre-restore backup failed.";
                    Toast(LastStatus);
                    return;
                }

                DeleteCurrentSaveFiles(savesFolder);
                ZipFile.ExtractToDirectory(backup.Path, savesFolder, overwriteFiles: true);
                string metadataPath = Path.Combine(savesFolder, MetadataEntryName);
                if (File.Exists(metadataPath)) {
                    File.Delete(metadataPath);
                }

                if (!TryLoadRestoredSaveData(backup, out string loadMessage)) {
                    LastStatus = "Restored files, but live reload failed: " + loadMessage;
                    Toast(LastStatus);
                    return;
                }

                Engine.Scene = new OverworldLoader(Overworld.StartMode.MainMenu);
                LastStatus = "Restored backup: " + backup.FileName;
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
    }

    private static void DeleteCurrentSaveFiles(string savesFolder) {
        foreach (string file in Directory.EnumerateFiles(savesFolder, "*", SearchOption.AllDirectories)) {
            if (!IsInsideBackupFolder(file)) {
                File.Delete(file);
            }
        }

        foreach (string directory in Directory.EnumerateDirectories(savesFolder, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length)) {
            if (!IsInsideBackupFolder(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) {
                Directory.Delete(directory);
            }
        }
    }

    private static AkronBackupEntry ReadBackupEntry(string path) {
        FileInfo file = new FileInfo(path);
        string reason = string.Empty;
        string saveSlot = string.Empty;
        try {
            using ZipArchive archive = ZipFile.OpenRead(path);
            ZipArchiveEntry metadata = archive.GetEntry(MetadataEntryName);
            if (metadata != null) {
                using StreamReader reader = new StreamReader(metadata.Open(), Encoding.UTF8);
                string json = reader.ReadToEnd();
                reason = ExtractJsonString(json, "reason");
                saveSlot = ExtractJsonString(json, "saveSlot");
            }
        } catch {
            reason = "unreadable";
        }

        return new AkronBackupEntry {
            Path = path,
            FileName = file.Name,
            CreatedUtc = file.CreationTimeUtc > DateTime.MinValue ? file.CreationTimeUtc : file.LastWriteTimeUtc,
            SizeBytes = file.Length,
            Reason = reason,
            SaveSlot = saveSlot,
            Pinned = File.Exists(GetPinPath(path))
        };
    }

    private static bool TryLoadRestoredSaveData(AkronBackupEntry backup, out string message) {
        int slot = DetermineRestoreSlot(backup);
        if (slot < 0) {
            message = "no save slot could be determined";
            return false;
        }

        string filename = SaveData.GetFilename(slot);
        SaveData saveData = UserIO.Load<SaveData>(filename);
        if (saveData == null) {
            message = "could not load " + filename;
            return false;
        }

        SaveData.Start(saveData, slot);
        message = string.Empty;
        return true;
    }

    private static int DetermineRestoreSlot(AkronBackupEntry backup) {
        if (int.TryParse(backup?.SaveSlot, NumberStyles.Integer, CultureInfo.InvariantCulture, out int metadataSlot) &&
            metadataSlot >= 0) {
            return metadataSlot;
        }

        return SaveData.Instance?.FileSlot ?? -1;
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

    private static string BuildMetadataJson(string reason) {
        Level level = Engine.Scene as Level;
        IEnumerable<string> mods = Everest.Modules
            .Where(module => module?.Metadata != null && module.GetType().Name != "NullModule")
            .Select(module => JsonEscape(module.Metadata.Name + "@" + module.Metadata.VersionString))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine("  \"schema\": 1,");
        builder.AppendLine("  \"reason\": \"" + JsonEscape(reason) + "\",");
        builder.AppendLine("  \"createdUtc\": \"" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "\",");
        builder.AppendLine("  \"gameVersion\": \"" + JsonEscape(Celeste.Instance?.Version?.ToString() ?? string.Empty) + "\",");
        builder.AppendLine("  \"modVersion\": \"" + JsonEscape(AkronModule.Instance?.Metadata?.VersionString ?? string.Empty) + "\",");
        builder.AppendLine("  \"saveSlot\": \"" + JsonEscape(SaveData.Instance == null ? string.Empty : SaveData.Instance.FileSlot.ToString(CultureInfo.InvariantCulture)) + "\",");
        builder.AppendLine("  \"profileName\": \"" + JsonEscape(SaveData.Instance?.Name ?? string.Empty) + "\",");
        builder.AppendLine("  \"area\": \"" + JsonEscape(level?.Session?.Area.GetSID() ?? string.Empty) + "\",");
        builder.AppendLine("  \"room\": \"" + JsonEscape(level?.Session?.Level ?? string.Empty) + "\",");
        builder.AppendLine("  \"mods\": [\"" + string.Join("\", \"", mods) + "\"]");
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

    private static bool IsInsideBackupFolder(string path) {
        string backupFolder = Path.GetFullPath(BackupFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(backupFolder, StringComparison.OrdinalIgnoreCase);
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

    private static string JsonEscape(string value) {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private static string ExtractJsonString(string json, string key) {
        string marker = "\"" + key + "\":";
        int index = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) {
            return string.Empty;
        }

        int start = json.IndexOf('"', index + marker.Length);
        if (start < 0) {
            return string.Empty;
        }

        int end = json.IndexOf('"', start + 1);
        return end < 0 ? string.Empty : json.Substring(start + 1, end - start - 1);
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
