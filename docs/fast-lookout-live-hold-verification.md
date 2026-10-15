# Fast Lookout live hold verification (#252)

The native coroutine caches acceleration (800) and maximum speed (240) before its movement loop. Scaling those initializers latches the entry-time hold state. The correction preserves the native cached values and scales their six read sites using the current feature, hold, and policy gates. It validates field ownership against the original coroutine type, not MonoMod's generated DMD wrapper. Missing or ambiguous initialization layouts disable the hook with an explicit warning.

## Native evidence

Linux Celeste 1.4.0.0 / Everest 1.6580.0, original Old Site A room d3, multiplier 3 and Q hold. Original game content was not edited. Native Noclip was used only for initial staging and disabled before all Lookout interaction tests. UI and keyboard actions only; no injected getters or synthetic game lifecycle.

QA89 baseline: enter unheld then hold while panning (B) travelled about 93 screen pixels; enter held then release before panning (A) travelled about 325. Held-at-entry/held-at-pan with a release gap (D) travelled about 278; feature Off (C) about 93. This reproduces entry-latched behavior.

QA90 first candidate failed closed: its ownership check rejected the DMD wrapper. Native startup warnings and ordinary movement prevented acceptance despite passing fixture tests. The fixture tests now include different method/field owners matching DMD layout.

QA94 corrected Release ZIP SHA256 `216167dbdbea2f96bbeacdccb0bdd9bd6dd9bb8daebf900c0e996ce367e5299e`: documented 200ms input matrix measured B about 263 pixels, A about 93, D about 325, C about 93. A's 4-pixel starting offset is accounted for. Terrain measurements support held versus ordinary movement and reverse baseline B/A behavior; no visible downward endpoint clamp. Physical input durations are not calibrated game-clock measurements, and these figures are not an exact 3x speed claim. D includes a release gap between entry and pan; continuous hold across that gap was not tested. Earlier alternate QA94 captures are preserved but excluded from acceptance because their action protocol is unavailable.

No missing-field/read warning appeared in corrected startup. Native Save and Quit and Exit completed. Fast Lookout and Noclip were Off afterwards, with Q retained as the isolated fixture binding. Original screenshots, package/runtime hashes, before/after profile copies and audit receipts are preserved.

Release and Debug each passed 2,375 native-reference tests at the tested candidate. Five IL fixture cases cover normal read rewriting, missing maximum-speed initializer, ambiguous acceleration initializer, DMD wrapper ownership and wrong original owner. Native node-path Lookouts, controller/mouse holds, other game versions and exact sub-frame input timing are not live-verified.
