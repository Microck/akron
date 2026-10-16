# Recorder output directory startup (#210)

## Native baseline
QA157 used current PR343 code, x264/MKV320x180/30, audio/remuxOff and replay0. A default-folder recording produced595 coherent frames and decoded successfully. Through native Output UI, the folder was then set to the existing regular file empty.tas in that disposable game's root. Start Recording and warning Continue threw IOException from Directory.CreateDirectory outside Start's catch. The overlay briefly fell back; the next ImGui frame asserted and the process aborted with exit-6. The exact configured file path and both error stages are retained. No recovery or replay claim is inferred from that run.

QA158 independently set the equivalent existing file as Output Folder and explicitly enabled5 seconds with auto-startOff. Native Start Replay Buffer threw DirectoryNotFoundException during directory creation. A second attempt through CaptureFrame followed because the replay request remained armed. Engine.RunWithLogging terminated the game and opened its native error log. Exit code0 does not imply a healthy exit: this was fatal-error shutdown, not Save/Quit.

## Correction
Manual and replay path resolution, directory creation, output reservation and process-start configuration now sit inside normal startup error boundaries. Manual errors retain full diagnostics with bounded feedback. Replay failure disarms explicit/automatic requests and completion triggers, guards replay-only cleanup separately and retains both original and cleanup errors. It does not stop healthy manual recording. Completion arming does not announce success after an immediate failed start; no-scene deferred arming remains available.

## Verification
Release and Debug each pass2,539 tests, including startup-boundary, guarded cleanup/disarm and completion-feedback guards. Corrected native negative/valid-path recovery and concurrent recording checks are pending. Filesystem/source checks do not replace native acceptance.
