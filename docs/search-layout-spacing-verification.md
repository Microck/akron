# Search layout spacing verification (issue #202)

## Scope

Search temporarily expands matching sections even when the user saved them collapsed. The layout must reserve expanded body height, both while stacking built-in sections and planning external-tool placement. Clearing Search retains the saved collapse preference.

The ImGui draw uses its existing per-frame Search snapshot throughout. The fallback layout takes its own snapshot. A Search edit during Interface drawing applies on the following frame, preserving the collapse-state fix from #299.

## Baseline

AI-operated native Celeste/Everest on the cloud Linux desktop, 1364 × 1024 window, UI scale 1.0, Forsaken City. QA30 used package SHA-256 `7e6c0ad803102d3a512fdee041fdb1c4e2725361862618ab3ad28e6145340810`.

- Expanded Creator, Search `next`: four matching rows and Interface below them.
- Collapsed Creator, same Search: Interface overlaps the first two matching rows. Two settled captures reproduce the result.
- Clear and reopen: saved collapsed state remains. Original expanded layout restored before normal exit.

No room/map result was activated. Input events and reopen/exit actions are operator observations; screenshots provide visual state evidence. Run exited normally with code 0 at 2026-10-15 03:31:13 UTC. Full profile comparison records normal time/death/session bookkeeping changes and the backup timestamp; this is not a claim of an unchanged profile.

## Native candidate acceptance

QA31 uses Release package SHA-256 `4fd334056aa0404ba2de2cbe53313b4e430d0c667d3549e7305ee3b6d27d1154`, same runtime/window/profile baseline.

- Expanded control remains correct.
- With Creator saved collapsed, Search `next` displays all four rows and places Interface below them, matching the expanded control. A second settled capture confirms the state.
- Clear restores Creator collapsed. Search `camera` temporarily expands matching Creator and Extended Camera Dynamics rows.
- Changing `camera` back to `next` repeats the non-overlapping result.
- Clear and close/reopen retain Creator plus all four pre-existing external-tool collapse preferences. Original Creator-expanded layout restored with empty query.

AI-operated native checks, not human QA. No runtime injection or direct state mutation was used for acceptance; native mouse/keyboard and screenshots were used.

## Automated checks

Eight regression cases cover effective collapse height, saved preference preservation, edit-during-draw transitions, and snapshot wiring. Release and Debug each pass all 2,204 tests. These checks supplement native testing; they do not replace it.

## Limits

This change addresses Search-expanded collapsed sections. It does not claim to fix every layout overlap, every resolution/UI scale, or the separate pre-existing discrepancy between external-tool reserved height and the height recalculated inside DrawActionWindow. No human testing is claimed.
