# Confirmed Speedrun Tool restore verification

Issue: https://github.com/Microck/Akron/issues/278

The Confirm callback must not replace a Level's entities while the native pause
menu is updating them. The confirmed path now captures the selected slot and
queues the load through Akron's existing stable engine-update boundary. It
checks that the original Level and Session are still current before loading.
The save/load service still performs its policy checks at execution time.
The confirmation-Off path keeps its existing immediate behavior.

## Automated scope

`ConfirmedStateLoadTests` covers unchanged/replaced/missing scene and session
identities, source wiring and slot capture, and the actual deferred queue's
wait-for-boundary, random-scope deferral and exactly-once execution behavior.
It does not invoke Speedrun Tool or prove a native restore succeeds.

## Native acceptance checklist

Use an isolated profile and an explicitly disposable SRT slot. Record the exact
Celeste, Everest, Akron and provider versions and preserve the user's saves.

1. Reproduce the issue's safe City A-side `6zb` capture and displaced pose.
   Confirm the selected slot is genuinely empty before the single setup capture.
2. Enable Confirm Load State and open the prompt with the real native binding.
   Cancel. Verify the displaced pose, slot contents and scene remain unchanged.
3. Open the prompt again and Confirm once. Verify the captured pose returns,
   Level updates and rendering continue, no critical screen appears, and the
   logs contain no collection-modified exception.
4. Verify the expected time/death preservation setting and SRT slot identity.
   Ensure a changed active-slot setting cannot redirect an already-queued load.
5. Check refusal when scene/session or policy changes before the deferred action
   executes. No restore may be applied to a different scene or session.
6. Repeat with confirmation Off as a separate control. Then restore settings,
   remove only the owned test slot/binding, and verify clean normal retirement.

Keep the unmodified comparison build separate. Use actual native prompt and
gameplay evidence; source inspection and passing unit tests alone do not close
this issue or establish provider-wide compatibility. Record partial steps and
blocked checks rather than treating them as passes.

## Native crash comparison, 2026-10-12

AI-operated live verification reproduced the collection-modified crash on
unmodified main `2cee797dd3d7f07abed74bd09fd6a16ebf4e4ec2`. Akron's own
Save State action captured disposable slot 9; native movement displaced the
player. Cancel in the actual Akron confirmation prompt preserved that
displacement. Reopening the prompt and confirming caused
`InvalidOperationException: Collection was modified` in `Celeste.Level.Update`.

The candidate used the same 11 frozen input Saves files and created its own
in-process snapshot; no snapshot was transferred between processes. Cancel
again preserved the displacement. Confirm restored the captured position,
closed the prompt, and allowed subsequent native movement and fresh rendering
without the exception. A separately frozen confirmation-Off control, differing
only in that setting, restored directly and also allowed subsequent movement.
Each run logged one save and one load for slot 9 and ended through normal Exit
with code 0. The baseline's exit code does not negate its observed critical error.

- Runtime: supplied Celeste 1.4.0.0-fna content, Everest
  `6580-azure-082e2-stable`, .NET 8.0.24; Speedrun Tool 3.27.21.
- Provider ZIP SHA-256:
  `c5cf070f4f69969e9166f95fca375a0a97b306e7f6c19fe22fa0928710ac07e4`.
- Comparison DLL SHA-256:
  `e29186cda4c0e2c9eb34a270812781f8cf03c167078518a13e50005eeedd6da2`.
- Candidate DLL SHA-256:
  `ea61d1ebe5e502ac7a818de9b94e4745bf6c6da93e646b8b07619234368d7a9a`.
  This private candidate also contains proposed fixes for #279 and #274.
- Disposable Akron bindings were J for Save State and L for Load State;
  Speedrun Tool's independent hotkeys were not used for the comparison.
- `SaveTimeAndDeaths` and the provider's public freeze setting were Off.
  The fixture was the supplied content's saved Normal City room `1`, not the
  historical room `6zb` fixture. Supplied content identity is recorded separately.
- Settings were prepared while the game was closed. Original screenshots,
  logs, frozen Saves files, provider identity and exit receipts were retained.
  Four additional Backups entries in the input manifests are outside the
  independently compared frozen Saves trees.

This verifies the narrow confirmed-load crash regression and continued input,
not full restored-state identity, human testing, all time/death policies, queue
slot retargeting, scene/session invalidation, or policy changes while queued.
Confirm also supplies native game input, so transient sprite/follower changes
are not evidence of complete state equivalence. The provider thumbnail's
"Not Saved Yet" label did not independently describe the broker's saved state;
the actual save/load logs and position restoration establish this tested path.
