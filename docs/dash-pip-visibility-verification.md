# Empty dash-pip visibility verification

Issue #275. Assistant-operated native Celeste 1.4.0.0 / Everest 1.6580.0 on the cloud Linux desktop, 1364×1024, Forsaken City Normal room 6, 2026-10-15. No human testing is claimed; the owner authorized that exception.

## Change

Skip depleted pips before drawing either their fill or outline when Empty pips is disabled. Occupied pips, slot spacing, label placement and the separate Bar renderer are unchanged. The shared predicate has seven regression cases; these test the visibility decision rather than renderer wiring or pixels.

## Native before and after

QA21 baseline used PR300 candidate package `7e768678207ceb13a3d9022f706c185a6ba9696136ba43d54f9f9635e442bad0` (backup-root changes, no pip change). Through native UI, enabled Dash Count with maximum 2, disabled ground dash refill through Ground Refills, then used ordinary Left+X to dash against the left wall. Dash Number visibly reported 1. Freeze Gameplay held the observed state for the comparisons. Empty color was set to green through the normal hex field, with magenta occupied charges.

- Baseline fixed HUD Pips On → Off → On: the green depleted fill remained visible with Off. Persisted settings confirmed Off. The Bar-style On → Off control correctly removed green empty fill while keeping its black meter background.
- Candidate exact Release SHA-256 `735f4f76a00d5a199a17e684f54f1d19c05bd8e48ce3d9ba766f1caf490573fc`: repeated the same fixed-HUD Pips sequence. Off removed the depleted pip and On restored it; the occupied magenta charge and label remained.
- Additional candidate coverage enabled player-attached Pips alongside the fixed HUD. Player Y was set to -100 through the public control to avoid the frozen-gameplay toast. Both displays removed the green depleted pip with Off and retained the occupied pip. This is additional after coverage, not a matched player-attached baseline.
- Both candidate Bar displays retained their expected On/Off empty-fill behavior. The black background remains by design; this fix does not change Bar rendering.

All scored gameplay captures show Dash Number 1 and Gameplay frozen. Native screenshots are unedited JPEG bytes despite their `.png` names; still images do not provide an uninterrupted input trace. Original profiles, settings, launch receipts, exact tested source/package and independent reviews are retained privately.

## Cleanup and checks

Restored original pip style, color, empty-pip preference, player offset and visibility, HUD toggle, dash-number toggle, dash-count override, ground-refill settings and normal unfrozen gameplay through native UI. Restored Akron settings differ only in the backup timestamp. Candidate native Save and Quit / Exit completed with code 0 at 01:38:49 UTC, followed by verified window absence. Full save-state preservation is not claimed: ordinary play/save changes counters, timing and attempt records.

Release and Debug each pass 2,173 tests, including the seven added predicate cases. Changed C# formatting command was run. Existing CL0006 and NU1900 warnings remain; no successful vulnerability audit is claimed. Read-only code review found no blocking semantic issue; native tests supply the observed renderer acceptance.

## Limits

One two-slot/one-charge state and one viewport were tested. Zero-charge, five-slot, other scales, other maps/platforms, inline-fallback placement and renderer alignment are not independently live-verified here. The player-attached offset was an evidence-visibility adjustment, not an alignment fix. Candidate Content/orig assets are shared with the completed baseline through symlinks to avoid duplicating 2.3 GB; writable game, Mods, logs and profile paths are separate, with retained shared-asset hash controls. Both use the documented reconstructed runtime, not a byte-identical historical installation.
