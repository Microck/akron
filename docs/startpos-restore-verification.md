# StartPos restore verification

Verified on 2026-09-08 with the user's five supplied Reflection snapshots.

## Fixes

- A CrushBlock attack iterator can retain its sound after `Component.Removed`
  clears `Component.Entity`. Reconstruction now accepts that detached component
  through the already-proved iterator closure and validates its canonical
  reference. Attached components still have to name the iterator's entity.
- Ordinary StartPos loads preserve the captured animation and frame. Explicit
  spawn configuration still refreshes the pose when applying its overrides.
- Gameplay buffer restoration uploads the validated saved pixels without
  immediately reading them back from the GPU. Pixel comparison remains a QA
  check rather than a synchronous step in every warm load.

## Supplied pack

The pack's SHA-256 is
`d27cb79bf2576ada876e82e5cde4df11466bd9b3b3dfe45081b85ba332d18f42`.
Public beta 79 imports its `akron-setup-v9` container. The original verification
build also contained a separate, unpublished setup-compression change, so the
pack was imported with beta 79 first. Verification then used those same native
`akron-reconstruction-v10` snapshot files with the fixes applied. This PR is
based on beta 79 and excludes that compression change; its setup format remains
`akron-setup-v9`. No snapshot re-export or compatibility path was required.

Beta 79 reproduced the removed-sound refusal in slots 4 and 5. A later slot 2
load itself took 62.5 ms, but preparation spent another 17,067.6 ms retrying
those failing slots. This explains why an already-warm slot still appeared to
load slowly. The fixed build prepares all five slots successfully; subsequent
loads report zero additional preparation work.

## Linux Mint game checks

Used the documented jc141 installation with Everest 1.6418. The supplied
snapshots require Vidcutter, so Vidcutter 1.12.1 was installed. Other optional
mods were temporarily disabled after the full mod set exhausted the machine's
available memory. The original mod blacklist was backed up and restored.
Mod and save backups are under
`/home/microck/akron-startpos-pack-test-20260908/`.

End-of-frame player probes matched the supplied snapshot state:

| Slot | Room | Position | Animation | Frame |
| --- | --- | --- | --- | --- |
| 1 | b-00b | 9657, -2968 | idle | 0 |
| 2 | b-00 | 9842, -2800 | edge | 7 |
| 3 | b-02 | 10740, -2552 | idle | 0 |
| 4 | b-02 | 10783, -2136 | idle | 4 |
| 5 | b-02 | 10604, -1685 | empty ID, as captured | 1 |

QA readback of the presented 320x180 Level buffer matched the saved SHA-256
for all five slots. The game was also inspected visually. These buffer checks
confirm pixel restoration; the separate player probes check reconstructed
player state. A saved-buffer hash alone cannot prove subsequent simulation.

Before removing the redundant readback, temporary phase probes measured
697.6-945.7 ms inside buffer restoration on the slower repeated loads. After
removal, the same sequence measured 2.7-4.0 ms there. Warm restore totals for
slots 2, 3, 4, 5, 1, 2 were 46.2, 111.0, 83.3, 102.9, 39.4, and 58.8 ms.
All five remained warm, using 316.2 MB, with no preparation retries.
The temporary phase probes were removed from the final build.
The clean test archive was built with zero warnings and errors and installed
on Mint. Its SHA-256 is
`74e5057197e9c1e5a792edb0db4930ca2de9074dce1ec0ef7f4ed720a511e85c`.
Celeste was stopped after verification.

## Automated checks

- Original verification build: 1,900 Release tests passed, zero failed or skipped.
- Regression coverage includes Celeste's actual CrushBlock attack iterator,
  hoisted closure, detached SoundSource, and serialization round trip.
- Attached component ownership checks remain covered.
- Controlled live capture/load checks separately verified that ordinary loads
  preserve animation rather than recalculating it from movement speed.

## Limits

Cold reconstruction is still expensive: the timed fixed-build run spent
10.3 seconds loading slot 1, then 58.7 seconds preparing the other four slots.
This change fixes failed preparation and repeated-load stalls, not that initial
reconstruction cost. Timings apply to the reduced mod configuration above.

The separate preparation-recovery guard failure in the original log was not
reproduced. No guard was bypassed or weakened, and this case remains unresolved.
