using System;
using System.Collections.Generic;
using ImGuiNET;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawRestoreBackupPopupControls(string popupId) {
        IReadOnlyList<AkronBackupEntry> backups = AkronBackupActions.ListBackups();
        if (backups.Count == 0) {
            ImGui.TextWrapped("No backups found.");
            return;
        }

        ImGui.TextWrapped("Restoring creates a pre-restore backup first.");
        ImGui.Separator();
        foreach (AkronBackupEntry backup in backups) {
            ImGui.TextUnformatted(backup.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            ImGui.TextWrapped(backup.FileName);
            string details = FormatBackupSize(backup.SizeBytes);
            if (!string.IsNullOrWhiteSpace(backup.Reason)) {
                details += " | " + backup.Reason;
            }

            if (!string.IsNullOrWhiteSpace(backup.SaveSlot)) {
                details += " | slot " + backup.SaveSlot;
            }
            if (backup.Pinned) {
                details += " | pinned";
            }

            ImGui.TextDisabled(details);
            if (ImGui.Button("Restore##restore-backup-" + backup.FileName + popupId)) {
                AkronBackupActions.RestoreBackup(backup);
                CloseOptionsPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button((backup.Pinned ? "Unpin" : "Pin") + "##pin-backup-" + backup.FileName + popupId)) {
                AkronBackupActions.SetPinned(backup, !backup.Pinned);
            }

            ImGui.Separator();
        }
    }

    private void DrawBackupStatusPopupControls(string popupId) {
        ImGui.TextWrapped(AkronBackupActions.LastStatusForDisplay);
        ImGui.TextUnformatted("Last backup: " + AkronBackupActions.DescribeLastBackup());
        ImGui.TextWrapped("Folder: " + AkronBackupActions.BackupFolderForDisplay);
        if (ImGui.Button("Create backup now##" + popupId)) {
            AkronBackupActions.CreateBackup("manual");
        }
        ImGui.SameLine();
        if (ImGui.Button("Open folder##" + popupId)) {
            AkronBackupActions.OpenBackupFolder();
        }
    }

    private void DrawBackupTriggersPopupControls(string popupId) {
        DrawPopupCheckbox(
            "On launch",
            () => AkronModule.Settings.BackupsOnStartup,
            value => AkronModule.Settings.BackupsOnStartup = value,
            popupId,
            "Create a backup when Akron loads.");

        DrawPopupCheckbox(
            "On close",
            () => AkronModule.Settings.BackupsOnShutdown,
            value => AkronModule.Settings.BackupsOnShutdown = value,
            popupId,
            "Create a backup when Akron unloads.");

        DrawPopupCheckbox(
            "On save",
            () => AkronModule.Settings.BackupsOnSave,
            value => AkronModule.Settings.BackupsOnSave = value,
            popupId,
            "Create a backup before Celeste or Akron writes save data or settings to disk.");

        DrawPopupCheckbox(
            "On chapter",
            () => AkronModule.Settings.BackupsOnLevelBegin,
            value => AkronModule.Settings.BackupsOnLevelBegin = value,
            popupId,
            "Create a backup before entering a chapter or map, throttled to avoid repeated room-entry backups.");

        DrawPopupCheckbox(
            "Timed",
            () => AkronModule.Settings.BackupsEveryInterval,
            value => AkronModule.Settings.BackupsEveryInterval = value,
            popupId,
            "Create backups on a repeating minute interval.");

        DrawIntStepperRow(
            "Interval",
            () => AkronModule.Settings.BackupsIntervalMinutes,
            value => AkronModule.Settings.BackupsIntervalMinutes = AkronBackupActions.ClampBackupIntervalMinutes(value),
            -5,
            5,
            1,
            1440,
            popupId,
            "Minutes between timed backups.");
    }

    private void DrawBackupRetentionPopupControls(string popupId) {
        DrawIntStepperRow(
            "Max count",
            () => AkronModule.Settings.BackupsMaxCount,
            value => AkronModule.Settings.BackupsMaxCount = AkronBackupActions.ClampBackupMaxCount(value),
            -10,
            10,
            1,
            10000,
            popupId,
            "Maximum number of ZIP backups to keep.");

        DrawIntStepperRow(
            "Max age",
            () => AkronModule.Settings.BackupsDeleteOlderThanDays,
            value => AkronModule.Settings.BackupsDeleteOlderThanDays = AkronBackupActions.ClampBackupRetentionDays(value),
            -1,
            1,
            0,
            3650,
            popupId,
            "Delete backups older than this many days. Zero disables age deletion.");

        DrawIntStepperRow(
            "Max size MB",
            () => AkronModule.Settings.BackupsMaxTotalSizeMb,
            value => AkronModule.Settings.BackupsMaxTotalSizeMb = AkronBackupActions.ClampBackupMaxSizeMb(value),
            -128,
            128,
            0,
            1024 * 1024,
            popupId,
            "Maximum total backup folder size. Zero disables size deletion.");

        DrawIntStepperRow(
            "Keep at least",
            () => AkronModule.Settings.BackupsKeepAtLeast,
            value => AkronModule.Settings.BackupsKeepAtLeast = AkronBackupActions.ClampBackupKeepAtLeast(value),
            -1,
            1,
            0,
            10000,
            popupId,
            "Newest backups protected from automatic deletion.");
    }

    private static string FormatBackupSize(long bytes) {
        if (bytes < 1024L * 1024L) {
            return Math.Max(1L, bytes / 1024L) + " KB";
        }

        return (bytes / (1024L * 1024L)) + " MB";
    }
}
