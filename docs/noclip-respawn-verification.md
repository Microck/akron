# Noclip native Retry respawn (#263)

The candidate lets the original player update run during StIntroRespawn even
when Noclip is enabled. This advances the native player-owned respawn tween
and its normal completion/cleanup. It does not force StNormal, turn Noclip off,
or replace the native Retry callback. Other Noclip states keep the existing
collision-bypass movement path.

The bounded native Retry comparison is recorded below. The state-selection
tests are supporting checks, not a substitute for that live evidence or proof
of all Noclip behavior.

## Finite native comparison

Use matched owned saves and exact baseline/candidate package hashes in a safe
built-in room. Preserve actual map/room identity and starting settings. Keep
Invincibility, Hazard Accuracy, death/respawn modifiers and StartPos respawn
inactive. Disable Confirm Retry/Restart so the ordinary pause-menu Retry is
used; do not use a direct state change, reload, warp, or restore as a substitute.
Keep Noclip's Hide Player and Draw On Top controls Off for the visible comparison.

1. With Noclip Off, use ordinary Retry once and verify native respawn finishes
   and normal player presentation/movement returns.
2. Turn Noclip On and Retry once with neutral gameplay input. On the baseline,
   observe whether respawn remains unfinished while the room advances. If it
   stalls, turn only Noclip Off and verify that the same sequence can finish.
3. From identical starting inputs, repeat the Off and On Retry controls on the
   candidate. Respawn must finish while Noclip remains On, without an Off-only
   recovery. Verify both the actual saved setting and visible player behavior.
4. Separately verify that Noclip still moves through one safe solid boundary
   after respawn. Return to a safe position before turning it Off; do not enter
   hazards, change rooms, or conflate this with full Noclip lifecycle coverage.
5. Restore original settings and exit normally. Record expected Retry deaths
   separately from unrelated changes. State readbacks, if available, must use
   already-qualified public routes without enabling a debug listener.

Historical evidence used Mirror Temple e-01. A current comparison in another
room qualifies that room only. Cutscenes, other intro states, provider effects,
and all death/respawn modifiers remain outside this bounded comparison.

No human review or human testing is claimed.

## Recorded native comparison, 2026-10-12

In clean Celeste 1.4.0.0 / Everest 6580 / Akron, twelve frozen input save files
matched byte-for-byte. Actual saved map metadata was Celeste/1-ForsakenCity,
Normal, room 1, respawn (19,144). The baseline Off Retry returned a visible
player and accepted movement. Its On Retry left the player absent across
later views while the room animated; changing only Noclip Off returned the
player without another Retry.

The isolated candidate completed both Off and On Retry sequences. With the
actual Noclip setting still True, the returned player moved upward on input
without an Off recovery. This supports the bounded Retry regression fix.
The On sprite retained a crouched-looking frame; normal animation progression
and exact internal state/tween timing were not measured. Later wall occlusion
and return show movement, but do not conclusively qualify collision bypass.

Baseline package SHA-256:
`c3397cecdbef21c68da25b7419a3f1ea2c439711f04d0d41d5ec64ad65be5e68`.
Candidate package SHA-256:
`af0c8d8578313f82f7be6238c5b392db27315a5228a9b77e0229ccf0ca958a00`.
Both exited normally with code 0 and restored the original Akron settings
except the backup timestamp. Each total-death count changed from 4 to 7:
two explicit Retries plus one unattributed increment. Candidate upward
movement incidentally picked up a golden berry after the respawn comparison;
the owned session retained that difference. No whole-progress preservation
is claimed. A baseline post-recovery movement request had uncertain transport
delivery and was not repeated or credited as a successful movement control.

The evidence packet is `Akron-263-Native-Retry-Comparison-20261012.zip`.
Historical Temple, provider integration, other intro states, and modifier
combinations remain outside this comparison.
