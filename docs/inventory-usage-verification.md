# Set Inventory usage isolation (#261)

## Fix

Set Inventory writes dash capacity and current player charges, but no longer reads or writes native Session.Dashes usage. The restore snapshot captures inventory and Air Jumps configuration only. Its legacy SessionDashes property is retained for serialization/API shape compatibility and is unused by capture/restore.

The change does not alter Celeste's native death/reload statistics lifecycle or unrelated savestate restoration. Native Level.Reload resets Session.Dashes to DashesAtLevelStart; cumulative file usage is a separate oracle. Removing the inventory write does not mean session usage persists across every death.

## Baseline QA34

AI-operated native cloud Linux Celeste/Everest, 1364×1024, UI scale 1.0. Exact baseline Release SHA-256: `386f987a0bfa5d9d32e914b1ea18a409fbc0dacb8611088dc26d7f8dee8236f4`.

Created a new disposable save in native slot 1 and entered Prologue. Dash Stats Session mode read 0. Both mode (chapter/file) read 0/0 and Dash Number read 0 charges. Configuration-only changes from Dashes 2 to 1 and back to 2, with Jumps 0 and Restore on Death off, left these values unchanged.

Clicking Set without a dash input produced 2/0 usage and two charges. One physical rightward dash then produced 3/1. Returning the display to Session confirmed 3. Full saved-slot audit also found Session.Dashes 3, TotalDashes 1, inventory capacity 2, and level-start usage 0.

Settings and layout were restored; the new disposable slot was preserved as evidence. Existing settings differ only in backup timestamp. Normal exit code 0 at 2026-10-15 04:19:35 UTC. The save also contains one death whose cause was not observed, so this is not a death-free run claim. No death/Retry action occurred in the scored sequence.

## Native candidate QA35

Exact Release SHA-256 `964dfdf8b110dbaaa80d10e7bed47005db22e7488bdd3a66c4247083468ccc81`, same runtime/window and fresh native slot 1.

- Configuration-only control leaves usage 0/0 and charges 0.
- Set Inventory grants two charges while chapter/file usage remains 0/0.
- Six actual alternating left/right dashes increment both usage counters together, ending 6/6.
- Changing capacity to three and applying preserves usage 6/6 while charges become three.
- Restoration-off native Retry settles to usage 0/6 with three charges, establishing native session reset behavior.
- Enable Restore on Death and apply one charge: usage remains 0/6. One real dash produces 1/7.
- Native Retry with restoration armed settles to usage 0/7 with the original three charges restored. Cumulative file usage remains seven.

This is native support for the inventory/usage separation and the final death lifecycle. It is not a direct observation of internal pre-reload usage restoration, and does not claim a greater-than-five session snapshot was restored: that run's greater-than-five case is the earlier capacity change at 6/6. The native reset behavior is controlled separately, with compiled-IL tests supporting the absence of inventory usage writes.

## Automated checks

Release and Debug each pass 2,221 tests. Five added compiled-IL regression cases reject direct native usage access and legacy snapshot usage access in apply/capture/restore, while confirming capacity and player-charge stores remain. These are structural checks, not substitutes for native behavior testing.

## Limits

No human QA is claimed. Physical inputs and window absence are operator observations. This does not change the pre-existing supported-range clamps on restored capacity/charges. It does not change StartPos or save-state usage restoration.
