# Active save-root backup verification

Issue #255. Assistant-operated native Celeste 1.4.0.0 / Everest 1.6580.0 on the cloud Linux desktop, 1364×1024, Forsaken City Normal room 6, 2026-10-15. No human testing is claimed; the owner authorized that exception.

## Fix and boundary

Use `Everest.PathSettings` for the shared backup/restore save-folder resolver. Static inspection of the installed Everest assembly established that this public property follows Celeste's cached `UserIO.SavePath`, including its supported environment/platform resolution. Do not independently reread environment variables after game initialization. Creation, archive listing/retention, restore staging and destination now share the authoritative active root.

Existing installation-root archives are left untouched and outside the active listing when roots differ. They are not migrated: historical ZIPs can contain only logs while claiming no skipped files. Manually moving such an archive into the new active backup folder is unsafe; this narrow resolver fix does not add provenance validation or repair those old archives. Other installation-local Akron catalogs are not migrated.

## Native baseline

QA18 used PR299 candidate Release `dce4389d260d88c85e52c4118cb8789e4b8f973526b8ae3d9df122fdb081045e`; PR299 changes Search only. The supported `EVEREST_SAVEPATH` selected a disposable profile separate from the installation. Both Backups → Create Now and Last Result → Create backup now reported success, but both ZIPs contained only `AkronLogs/akron-current.log` and `_akron-backup.json`. All 17 active `.celeste` save/settings/module files were omitted. Metadata reported schema 2 and an empty skipped-file list. Stable status screenshots, original ZIPs and file hashes preserve the failure. No baseline restore was attempted. Normal exit code 0.

## Candidate native round trip

Exact Release ZIP SHA-256: `7e768678207ceb13a3d9022f706c185a6ba9696136ba43d54f9f9635e442bad0`.

- Same reconstructed runtime and copied baseline final profile, without profile edits.
- Startup archives contained all 17 active files byte-identical to the prelaunch profile.
- Each native manual button created a ZIP under the actual profile's `Saves/AkronBackups`. All 17 entries matched active on-disk bytes immediately afterward; CRC checks passed.
- The native Restore browser listed only the four new active-root archives. Old installation-root archives remained unchanged.
- Deliberately collapsed StartPos through native UI, closed the overlay to save, and verified that changed preference on disk.
- Selected the known-complete second manual backup, accepted the native Restore confirmation, and returned to the main menu. All 17 immediate-restored files matched the target ZIP byte-for-byte. The complete pre-restore safety archive preserved the deliberately changed settings.
- Continued the restored save and reopened the overlay: StartPos was expanded again. Last Result reported the expected restored filename without an open-save reload failure qualifier. The original installation-root archives remained hash-identical.

Original unedited native screenshots, profiles, archives, source snapshots and receipts are retained privately. Screenshot bytes are JPEG despite `.png` names. They are still images, not an uninterrupted input trace. Later gameplay and normal save can change counters/time; exact-byte restoration applies to the immediate snapshot, not the entire later session.

## Checks and limits

Release and Debug each pass 2,166 tests. Four new cases cover the authoritative resolver's source wiring and selected-root archive/restore helpers with decoy-root and excluded-folder preservation. The profile/XDG/installation test parameters are filesystem fixtures, not live executions of each platform resolver. Native verification above exercises `EVEREST_SAVEPATH`; Windows, macOS, Linux XDG default and installation-fallback native trials are not claimed here.

The first candidate launch failed during X11 startup with MIT-SHM `X_ShmPutImage` BadValue, exit 1. Its startup backup was valid, but that attempt provides no UI acceptance. The same package/profile/environment was retried without edits and completed the native checks above. The retry exited normally with code 0 at 01:01:27 UTC; window absence was subsequently verified. Both launch records are retained. Existing updater connection failures, CL0006 and NU1900 warnings remain; no warning-free startup or successful vulnerability audit is claimed.
