# Cached directional input blocking (#201)

## Change

MInput.Disabled is not a read-time guard for cached directional values in the pinned Monocle runtime. The level-update scope now snapshots and zeros registered integer, scalar and joystick current/previous caches (including joystick pre-inversion caches), then restores captured object instances and the previous Disabled state in finally. It does not poll input a second time or replace raw keyboard/mouse state. The closure is confined to the blocked path, with no added closure allocation in normal LevelOnUpdate. Reflection metadata is cached; blocked frames still allocate snapshots and boxes.

## Automated checks

Five new cases cover all cache types, exact integer preservation, previous Disabled true/false, callback exceptions, registry mutation and nested scopes. Release and Debug each pass 2,185 tests against the local runtime. Changed helper/test C# formatted. The helper uses backing fields so tests do not depend on stripped Celeste getters/constructors. Actual CI remains a separate stage. Existing CL0006/NU1900 warnings remain.

## Native acceptance: 2026-10-15

Linux native Celeste/Everest 1.6580.0, Forsaken City, 1364x766 gameplay viewport. Fresh isolated save profile. Native mouse/keyboard only; no injected runtime state. Pause While Open Off, gameplay Freeze Off, Free Camera Off, Search empty and blurred.

Baseline QA25 on PR302: Right held for a requested 300ms moved Madeline roughly127px with Block On, and about120px on repeat. Off and overlay-closed controls also moved. Terrain remained fixed.

Candidate QA26 package SHA-256: `cc1fd59d0d27899d6bd1f56b58e07a25d2e83c7ecdf5d5f56ec6b34e732ebc53`.

- Block On: Right and Left holds left the grounded player in place while snow/entity animation continued.
- Block Off: Right moved the player.
- Restore On: repeat Right remained blocked.
- Close overlay: fresh Right moved normally; reopening remained usable.
- Native Search accepted camera, cleared normally, and a fresh menu toggle closed the overlay.
- Settings restored except normal backup timestamp. Full save/session identity is not claimed preserved.

Initial QA26 build only reached the title menu, then exited normally. A read-only review caught an avoidable closure allocation; final package above incorporates that correction and an independent finally for Disabled restoration. Only the final package received the scored gameplay acceptance.

## Limits

This patch and live acceptance cover cached directional movement. It preserves existing button behavior; it does not claim to fix pre-existing Invert/Toggle GrabCheck, DebugOverridePressed, buffered-button or held-across-close cases. It does not suppress mods reading raw hardware directly. Controller/feather/glider runtime coverage was not performed; unit cases cover their framework cache shapes. Requested key durations are not exact simulation-frame counts. This is AI-operated native QA under the owner's human-test exception, not a human-testing claim. Original evidence remains private.
