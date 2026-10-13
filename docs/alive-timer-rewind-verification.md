# Alive timer after native clock rewind

## Scope

This candidate addresses issue #249: a native first-room Retry can reset Celeste's
session clock after Akron has recorded its death-time Alive baseline. The displayed
Alive duration then remains clamped to zero until that old baseline is reached.

After the native Player.Added call, the candidate checks for a current session clock
below the Alive baseline. Only that condition rebases Alive start and its two
freeze-accounting fields. Equal or forward clocks retain their state. Room timing,
visit/death/berry counters, attempt history and feature policy are unchanged.

The existing death-time reset remains in place. This does not redefine ordinary
Alive timing as starting at the end of the respawn animation. It does not reconcile
arbitrary mid-life clock changes or reset the separate room timer.

## Automated verification

The focused tests exercise the production reconciliation helper with zero/nonzero
rewound clocks, active/inactive freeze, stale frozen duration, equal/forward clocks,
repeated additions, coherent restored timer data and unrelated state preservation.
They do not run a native Level.Reload, Player.Added hook chain or visible HUD.
The callback's absent-module guard and existing private freeze-release arithmetic
are also exercised. A non-null stripped EverestModule cannot exercise its _Session
getter in this test environment; that missing-module-session branch is source-reviewed,
not claimed as executed coverage.

## Bounded native verification

On 2026-10-13, separate unchanged-input main and isolated candidate fixtures
passed a native Never-mode before/after comparison on Celeste 1.4.0.0 and
Everest 1.6580.0. Both used the same five provider versions and City Normal
room 1 with FirstLevel and no collectible/checkpoint progression. The original
tracker layout overlapped Chapter; the same native Offset Y=100 was used in both
arms and restored to 0 afterward. No input save files were edited.

Main package SHA-256:
`c3397cecdbef21c68da25b7419a3f1ea2c439711f04d0d41d5ec64ad65be5e68`.
Candidate package SHA-256:
`edb49200bccb460f5ecc8283544e36008d9e68998ca64d3ef6e1ddc0a6a3d30d`.
Candidate DLL SHA-256:
`2fbfa707e7de3bb0a633aa3a7054cdbf3ceb72b095b621497673b37b8bf057e7`.

- Main before Retry: Chapter 20:55.331 to 21:12.603, Alive 08:04.109 to
  08:21.381. After its sole Retry, Chapter advanced 0:31.212 to 0:44.948
  while Alive remained 00:00.000 in both original images.
- Candidate before Retry: Chapter 20:03.719 to 20:22.266, Alive 07:12.497 to
  07:31.044. After its sole Retry, both Chapter and Alive advanced from
  00:24.038 to 00:39.253, below the old pre-Retry Chapter lower bound.
- Final main Alive start was 13,746,030,000 ticks against a reset session time
  of 2,852,430,000. Candidate Alive start was exactly 0 against session time
  2,375,580,000. Both retained frozen duration 0 and frozen-start -1.
- Each arm had matching complete serialized session copies and expected
  global/City/session/current-room death counts 6/5/1/5, with Akron registered
  counts 1/1. Alive/Tracker/Visible and Offset Y were restored before the sole
  Save and Quit. Never stayed enabled through that save and its preserved output;
  Paused/inactive was restored afterward at main menu. Both processes exited normally.

Original screenshots, full save/profile comparisons and independent custody
checks support this result. The first post-Retry screenshot in each arm retained
an unobscuring native save icon, consistent with settings-save activity; the
second cleared it. This icon is not evidence of fresh native session serialization.
Immediate transition
images do not establish an exact internal death-animation phase. The separate
room timer remained zero and is outside this repair. No native getter, debug
service, gameplay movement or repeated Retry was used. This qualifies only the
exact isolated package's Never-mode rewind case; combined builds require their
own native verification.

## Remaining native regression cases

Do not treat the earlier Retry lifecycle evidence with Alive disabled as timer
verification. For a repeatable comparison, use the following requirements.

Use separate unchanged-input main and candidate fixtures. Enable the actual Room
Stat Tracker and Alive line through native controls, with a documented freeze mode.
In a first-room/no-progress non-golden session, retain positive pre-Retry Alive and
Chapter values, invoke one actual Retry, and observe supported return. The candidate
must show two positive increasing Alive values while its reset Chapter clock is
still below the old pre-Retry baseline. Preserve all original images and saved
timer fields, restore options and retire normally.

A Never-mode comparison isolates the reported defect. Separately verify a selected
active-freeze/unfreeze path before claiming that runtime behavior. A no-rewind
respawn and coherent savestate round-trip are distinct regression cases, not implied
by the helper tests or the first-room comparison.

Warm graph restoration normally relinks without Player.Added; coherent restored
Alive state is preserved by the guard. The no-saved-Level restore fallback and every
third-party restore permutation are not qualified by this change's source tests.

No human review or testing, pull request, merge, whole-feature or whole-build
acceptance is asserted here.
