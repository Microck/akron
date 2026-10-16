# Disabled EVM string-editor verification

Fixes #230. The string popup now reads the exact configured text that its setter
updates, with the current provider value used only when no configured entry
exists. Empty and whitespace text are retained as editor state. The generic typed
getter is deliberately avoided because it parses/trims strings and treats blank
values as absent, which would recreate the active InputText model mismatch.

## Baseline

QA131 ran the unchanged PR334 package in an isolated ordinary Forsaken City room
with native Everest and Extended Variant Mode. Color Grading began Off with empty
current/default values. Four physical letters produced `cold`; after all keys
were released and the field remained focused, identical configured-value toasts
continued filling the screen. The captured timer advanced from 43.571 to 50.898
seconds between the valid-text and idle captures. Blurring stopped the flood,
but the Value field became empty while Configured still showed cold. Enabling
applied Current cold and a visibly blue grade. Reset restored the default scene.

An earlier bound-window C event was not delivered, producing `old`; that attempt
is retained but excluded from the valid-cold control. An initial navigation
focus miss triggered Reload Room before the scored baseline. Neither is treated
as a product failure. Native Save and Quit and Exit completed with code 0.

## Automated coverage and scope

Both native Release and Debug configurations pass 2,466 tests. New tests cover
exact configured text including empty and surrounding whitespace, absent keys,
other variants, mutation between reads, null values/name, fallback after removal,
and string-popup wiring. Getter tests do not claim to exercise ImGui callbacks.

This change does not alter provider conversion or activation. Blank strings may
still be rejected on activation and surrounding whitespace is trimmed by that
existing path. Enabled-string edits through the default and rejected provider
writes are distinct existing behaviors, not covered by this disabled-editor fix.

Release package SHA-256:
`81c477d361514a8d38536ff7d85156ce02e39e71b3b6beb611512e72d8f14b25`.

## Candidate live results

QA132 used the same initial profile and candidate package. Four separate physical
letters produced four normal configured-value notifications, one for each text
change. Keeping the field focused with all keys released did not grow the stack.
Blurring and reopening retained cold in the Value field. Empty text, a single
space, and padded cold also remained stable during the sampled idle periods.
Enabling the padded value applied the existing trimmed cold grade successfully;
Reset restored Off/default and the normal scene.

These are native screenshot observations under the recorded input protocol,
not an instrumented callback count or a frame-perfect timing claim. The existing
value-edit freeze affects gameplay timers and toast lifetime, so wall-clock idle
and stable stack size are the relevant controls. The four-per-letter stack is
observed for this trial, not a promise about every platform's text event batching.
No game assets or private profiles are included in this PR.
