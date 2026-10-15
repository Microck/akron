# Extended Variant numeric edit verification

Issue #233. Native Linux Celeste 1.4.0.0, Everest 1.6580.0, EVM 0.51.0. Root operated the actual ImGui fields with desktop mouse/keyboard; no runtime state injection or synthetic provider calls were used for acceptance.

## Baseline QA102
PR325 package SHA256 `753e39ed895f08cdc1725f80ee1579e38a2d498411d927b18e56817e21d16c04`.
Enabled Zoom Level0.5, selected the Value text and typed `1`, `.`, `5` without changing focus. Final configured1.5/current1/Off persisted after defocus. Explicit row toggle correctly produced current1.5 and magnified rendering. Reset restored default. Native Save and Quit/Exit0.

## Candidate QA103
Release package SHA256 `b477587d25430f538e685105aa6918abfdfc20d4b7bf1add6d459e547609e3ae`.
- Off Zoom edited to0.5 stayed Off/current1.
- Enabled Zoom0.5, same text gesture `1`, `.`, `5`: final On/configured1.5/current1.5, visibly magnified world. Defocus preserved it.
- A separate active edit ending exactly at1 produced Off/current1. A fresh edit to1.5 stayed Off/current1; prior enabled intent did not leak.
- Reset Zoom, close/reopen overlay, switch to integer Jump Count. Off configuration2 kept current1. Explicit enable gave current2.
- One focused integer gesture2→1→3 (Ctrl+A between values, no focus loss) finished On/current3 after defocus. The provider's visible extra-jump indicators also changed.
- Reset followed by ordinary + configured2 while remaining Off/current1. Restored both variants to defaults before native Save and Quit.

The overlay captures activation after drawing each input but before handling its changed value. Only text changes use the captured activation intent; +/- keep existing behavior. Matching deactivation clears intent after any final change. Each new activation replaces a keyed per-overlay record. Old public interop signature, provider validation, policy checks and cheat accounting remain intact.

Release/Debug native-reference tests:2391 passed each with `AKRON_NATIVE_EVEREST_TESTS=1`. New tests cover keyed intent lifetime, original public signature and actual source wiring/event ordering. They are not presented as fake-provider integration tests. Native tests above provide the actual installed-provider acceptance.

Bounds: no claim for simultaneous external randomizer/provider edits, every numeric option, controller input, stepper default-crossing changes, or transactional Escape rollback. This change intentionally covers numeric text gestures, not string/enum/boolean editors.

## Review correction / QA105
The second review found that applying the default still emitted an `on | 1` toast despite the panel correctly showing Off. The applied-value message now uses the same default comparison as option state. Three additional message tests cover float and integer defaults/nondefaults (2394 native tests pass in each Release/Debug run).

QA105 package `c1a2aca7e00adabbe85013b58edbdc27de72edb54ffe63bf7839457fb3123be7` was checked natively: enabled Zoom0.5 → intermediate1 shows both panel Off and toast `off | 1`; continuing the same edit to1.5 shows On/current1.5 and `on | 1.5`; a separate gesture ending at1 shows Off and `off | 1` after defocus. No mutation semantics changed in this revision.
