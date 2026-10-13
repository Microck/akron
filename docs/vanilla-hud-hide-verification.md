# Hide Vanilla HUD retained-buffer suppression (#240)

The candidate suppresses final HiresRenderer compositing and the Level render
call for its separate Everest SubHUD renderer, only for the current Level's
native HUD instances while the preference and map policy allow hiding. It leaves renderer visibility and buffer refresh unchanged.
The SubHUD gate uses the public Level IL hook rather than unsupported hooks
on Everest-internal types. This avoids stale retained images and does not store hidden visibility into
SRT snapshots or a competing visibility owner's snapshot. Unrelated
high-resolution renderers are not suppressed.

Renderer identities are resolved at render time because SRT can replace them
between update and rendering. The earlier cached-identity draft was never
natively accepted and has been superseded. The subsequent Hires-only revision
was insufficient to suppress the separate SubHUD pass; any timer-only evidence
for that revision does not qualify the final two-path revision.

The final candidate has a bounded native Chapter clock comparison recorded
below. Identity-selection, compiled wiring and structural IL checks alone do
not prove native behavior, exact first-frame timing or all HUD layers.

## Bounded native comparison

Use matched disposable profiles and exact package hashes in an ordinary safe
built-in room. Preserve the original native Speedrun Clock and both hide flags.

1. Set native Speedrun Clock to Chapter. With Hide Vanilla HUD Off and the
   overlay closed, take two separated views showing that the timer advances.
2. Turn Hide Vanilla HUD On, close the overlay, and take two settled native
   window views. Record whether the old timer remains visible and frozen on
   current main; do not use a capture pipeline that independently hides HUDs.
3. Turn hiding Off. Confirm the visible timer resumes at its current value.
4. On the candidate repeat the same Off/On/Off sequence. The timer must be
   absent while On, and return advancing while Off. Confirm the overlay itself
   remains usable and an independently enabled Akron HUD is not hidden by this
   vanilla-only control.
5. Restore the original clock and hide settings and exit normally.

Keep pause/cutscene behavior, other native HUD widgets, map-policy boundaries,
other mods and renderer modes explicitly unqualified unless actually exercised.
No human review or testing is claimed.

## Recorded native Chapter clock comparison, 2026-10-12

Clean Celeste 1.4.0.0 / Everest 6580 / Akron runs used twelve byte-matched
frozen input save files. Actual saved metadata was Celeste/1-ForsakenCity,
Normal, room 1, respawn (19,144); no provider mods were loaded.

Current main's clock advanced while hiding was Off, but stayed visibly frozen
at 13:44.177 across two settled On views while room particles changed. Turning
hiding Off recovered an advancing clock. The final candidate's Off clock
advanced from 12:55.302 to 13:02.153, was absent in both settled On views, and
returned advancing from 14:04.050 to 14:10.782 after Off. Actual On settings and
usable overlay controls were retained. This qualifies the selected Chapter
clock regression only.

Baseline package SHA-256:
`c3397cecdbef21c68da25b7419a3f1ea2c439711f04d0d41d5ec64ad65be5e68`.
Final candidate package SHA-256:
`af1fc407ab7d7783367c9045af9dfea9b0e3e31f6d59b2e685166715320dcb4f`.
The separately retained c77c9a Hires-only candidate also passed the clock
sequence but is superseded; its evidence does not establish final-build or
SubHUD acceptance.

The final log has no SubHUD hook-installation warning. That is installation
evidence, not visible SubHUD behavior: no available tagged-entity fixture was
verified. The independent stamina HUD control remained invisible, so neither
its preservation nor attribution to #216 is established. First-frame behavior,
SRT restores, pause-owner overlap, other HUD widgets and provider interactions
remain unqualified here.

All three legs exited normally with code 0. Native settings were byte-restored;
Akron settings differed only by the backup timestamp. Deaths increased from
4 to 5 in each leg without an established cause, so whole-progress preservation
is not claimed. Runtime UI sequences were not identical: the baseline alone
tried Always Visible, and the superseded candidate briefly entered and exited
the PICO title during setup. The final candidate had no such detour.

Evidence: `Akron-240-Native-Chapter-Clock-Comparison-20261012.zip`.
No human testing or review is claimed.
