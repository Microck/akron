# Control Display layout undo (#243)

The proposed change reads Ctrl+Z through ImGui's editor input stream rather
than mixing a raw Control snapshot with an update-scoped MInput key edge during
rendering. It does not undo the board while a
text editor or other ImGui item is active, and disables key-repeat for undo.

The historical setting-dependent failure has not been reproduced on current
main. Compiled-routing checks alone cannot verify actual key delivery, item
focus, undo data, or gameplay blocking.

## Native comparison

Use matched disposable profiles and exact package hashes. Keep gameplay and
the room unchanged during each comparison; preserve the original board.

1. With Block Gameplay Input On, open Control Display's layout editor, select
   Dash, and use the X stepper to change 0 to 2. Click a blank editor canvas
   region to release text/item focus, then press Ctrl+Z once. On unmodified
   main, record whether X remains 2 as reported.
2. Restore X, turn only Block Gameplay Input Off, repeat the same fresh edit
   and unfocused Ctrl+Z. Record whether undo succeeds in this control.
3. On the candidate repeat the On/Off comparisons. X should return to its
   pre-edit value in both, without movement or a room transition.
4. Separately qualify text-field undo ownership: an active field must not
   restore the whole board's undo snapshot. Release keys between gestures.
5. Restore the original board and settings, then exit normally.

Keep any unperformed control explicit. No human review or human testing is
claimed.

## Current clean-fixture observations, 2026-10-12

The baseline restored Dash X from 2 to 0 with Block Gameplay Input On, but
retained X2 in the planned Off control. This is the inverse of the historical
report, and the cause of the single missed chord is unresolved. Akron's own
MInput.Disabled scope ends before the render-time handler; these observations
do not establish gameplay blocking as the cause.

The candidate restored X2 to X0 in both On and Off legs using the same frozen
save inputs. Its extra screenshot round trip between blur and Ctrl+Z was absent
from the baseline sequence, so exact input timing was not matched. With a label
field focused and the board at X2, typing changed Dash to Dasha; a single
Ctrl+Z restored the label to Dash while retaining board X2. Because label edits
also capture whole-board undo snapshots, that visual result alone does not
identify which undo handler ran or prove exclusive text-field ownership. This
supports the bounded input-stream comparison, not a claim that the
historical Block-On failure was reproduced and fixed. Full provider integration
and other undo paths remain unqualified.

The tested package SHA-256 is
`409225182a7c0517beb445b27c4ce2c6a73b1e66e7170722dfe39ffbd6b77fea`.
The subsequent source edit only corrects its explanatory comment; behavior is
unchanged. A newly built package must retain a distinct identity and have its
verification recorded separately before final submission.

## Untouched-field supplement

The rebuilt package
`88501a881b450000268496640b3f0421e190ccfcbdfb722140f883d7cfee7358`
was checked in a separate fresh clean-core process, without the five providers.
With Block Gameplay Input On and a fresh editor history, a single stepper edit
changed X0 to X2. Focusing the untouched Label field showed its caret without
changing Dash. The first Ctrl+Z retained X2 and Dash. After key release and a
blank-canvas blur, a separate Ctrl+Z restored X0.

This avoids label-generated snapshot ambiguity and supports the bounded
active-field guard/control result. It does not establish universal text-editor
ownership, exact physical input timing, repeat semantics, the original
setting-dependent failure, or provider integration. The original board and
settings were restored except the backup timestamp; normal exit returned 0.
An unattributed total-death increase from 4 to 5 is recorded rather than treated
as proof of gameplay-state preservation.
