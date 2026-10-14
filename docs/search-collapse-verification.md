# Search collapse persistence verification

Issue: #254. Assistant-operated native Celeste/Everest verification on 2026-10-14. No human testing is claimed; the owner authorized the human-testing exception.

## Change

Snapshot whether Search is active once at the start of each ImGui menu draw. Interface can edit the query while later panels are still being drawn. Those panels now use the same frame snapshot as earlier panels, preventing temporarily expanded search matches from overwriting saved collapsed preferences. The next frame restores preferences for all toggleable sections, including matches that disappeared.

## Native before and after

Both trials used the cloud Linux desktop, Celeste 1.4.0.0 / Everest 1.6580.0, a 1364×1024 window, and Forsaken City Normal room 6. The candidate profile was copied from the baseline's restored normal-exit profile without edits. Search Autofocus was Off; Search was explicitly focused with native pointer input. Block Gameplay Input was On and Pause While Open was Off.

- Baseline main `00caeb2485e4462c6bbe4c4cf4c6df981d05dc57`, Release ZIP SHA-256 `941de874bf572878e88abd31547a354c24befdaf057ec0c3aeae17c80b673a5c`: collapsed StartPos and the four external panels (CelesteTAS, Extended Camera Dynamics, Extended Variant Mode, Speedrun Tool). No-search close/reopen retained all five. Entering `camera`, then native Ctrl+A / Backspace expanded all four external panels. Closing persisted only StartPos as collapsed; reopening confirmed the regression.
- Candidate Release ZIP SHA-256 `dce4389d260d88c85e52c4118cb8789e4b8f973526b8ae3d9df122fdb081045e`: the identical camera → clear sequence retained all five collapsed panels, visibly and in saved settings, including after reopening.
- Additional candidate control: camera → advance → clear also retained all five, including reopening. Saved baseline, after-clear, and after-switch-clear settings were byte-identical.
- Restored the original four collapsed external panels and expanded StartPos through native UI. Save and Quit followed by Exit completed normally; candidate exit code 0 at 23:50:11 UTC. Native window absence was then verified.

Independent review of the original native screenshots and persisted settings corroborates these bounded results. Captures are unedited JPEG bytes returned by the screenshot tool despite `.png` filenames. Original captures, profiles, package, launch/exit receipts, and independent review are retained privately. Still images are not an uninterrupted input trace.

## Automated checks and source custody

Release and Debug each pass 2,162 tests, including four frame-state transition cases and a source-wiring regression. The tests cover frame-snapshot stability and wiring, while the native runs establish observed persistence and restoration. Exact tested production source and test source are retained. The frozen Release package is separate from the worktree ZIP overwritten by the later Debug build.

`dotnet format` was run on the two changed C# files, normalizing pre-existing renderer formatting. The two existing theme-index expressions retain their original spacing to avoid unrelated source-text-test churn. The initial Release run exposed that formatting-sensitive assertion; the final Release and Debug runs pass. Existing CL0006 and NU1900 warnings remain; no successful vulnerability audit is claimed.

## Limits

The active `camera` query still overlaps Extended Camera Dynamics rows under StartPos. This is an existing layout issue (#202), not fixed or passed by this change. The sound-header input attempt in the baseline was ineffective; StartPos became the successfully observed earlier-panel control. No claim is made for every query, dynamic resize, provider isolation, other platforms, historical runtime identity, or all game-state preservation. The runtime is the same documented fresh reconstruction for both trials. No game console or injected runtime state was used.
