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
`akron-reconstruction-v10` snapshot files with the fixes applied. PR #192 shipped
that restore fix without the compression change and retained `akron-setup-v9`.
No snapshot re-export or compatibility path was required for that check.

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

## 2026-09-10 through 2026-09-12: shared ownership and lifecycle fixes

This check uses newly captured `akron-reconstruction-v11` snapshots, not the
v10 Reflection snapshots above. The setup container remains `akron-setup-v10`.
Older slots must be captured again because their snapshots lack the active
area's room-owned dust style.

The changes operate on ownership and identity, not map SIDs:

- Typed managed ownership admits grids and intermediate records without
  requiring lexical nesting or identical fresh-room populations.
- Entity matching uses concrete type and map SourceId. Named peer fields
  follow their identified owners when entity-list order changes.
- Retained map entities, session-suppressed built-in entities, pooled effects,
  and runtime entities with a proved scene owner can be reconstructed.
- Process caches retain their live identities. Mutable BlendState descriptors
  are copied as values, preserving aliases without copying graphics handles.
  Custom sidecar state is refused rather than silently discarded.
- The active area's dust-style entry shares the same clone graph as its
  controllers and is restored without replacing other areas' entries.
- Constructor-generated component callbacks can retain sibling components
  through their common entity owner. Attached components require saved-list
  membership; detached callbacks require an exact component field on the
  authenticated entity. Named fields preserve identity when same-type
  components swap attachment. Foreign owners, missing captured-component
  membership, and opaque captures remain refused.
- Scene-owned resources keep their canonical live identity when retained
  components have stale or missing references in the clean room, including
  components already detached at capture and reached first through a callback.
  Exact entity-field aliases establish ownership independently of traversal
  order. Competing owners are refused. The saved owner must prove the same
  scene; same-type occurrence counts cannot substitute for that ownership.
- Exact `EntityData` and `LevelData` records can survive in a mod session's cache
  after leaving their room. Their values and shared references restore as map
  data; runtime entity placement and resource checks remain separate.
- Capture and fresh-resource indexing use an explicit depth-first work stack.
  Node and alias order stay unchanged, but graph depth no longer consumes the
  save worker's native stack.
- Both resource indexes stop at registered live-instance anchors as well as
  live resource types. They do not walk private process-cache contents or
  scalar-array cells that cannot contain resources.

The current Release suite passed all 1,970 tests, with no failures or skips.
Coverage includes reordered same-type owner/peer pairs, contradictory map
identities, foreign-scene refusal, typed versus opaque ownership, coroutine
and direct-iterator ownership, readonly native copies, malformed resource
descriptors, and warm/disk dust-style alias preservation. A separate smoke
check using the installed FNA assembly passed descriptor copying, all twelve
settings, aliases through base/object/readonly fields, mutation isolation,
and refusal of custom sidecar data. It did not initialize a GPU.

An isolated 1,000-node capture on a 256 KiB worker stack reproduced a process
stack overflow in `CaptureContext.IndexFreshValue` before the traversal change.
The same executable now completes capture, serialization, deserialization,
and restore with matching values. The owning test suite also checks a deep
cyclic graph and shared leaf identity on that small stack.

The Windows Maya run captured both `Delta` and `Extra Ball`, then the process
exited when pause released the persistence worker. Windows Error Reporting
recorded `0xc00000fd` in `coreclr.dll`, not a managed load refusal. The original
command timeout concealed that process exit. A separate QA map reload produced
`0xc0000374` heap corruption; that native reload failure is not evidence of the
same bug.

The initial Windows candidate ran on Celeste 1.4.0.0 with Everest 1.6305.0
and the existing installed mod set. Its archive SHA-256:
`140d2f2cd34aa9eee90049f60252ff92900101b0351129afff48f1f7a8ae7596`.

The Forsaken City control passed capture, per-slot restart-copy checks,
export/import, independent cold loads, repeated warm loads, and a real Celeste
process restart. Player position, speed, facing, animation/frame, state,
stamina, and dashes matched capture; the controlled session flag and counter
also restored. Warm restore bodies took 15.3-20.1 ms; disk reconstruction took
3.1-3.9 seconds. These timings do not include every warm-all preparation step.

The sweep now waits for asynchronous export completion and checks the exact
archive's import result. It reimports before each slot's cold check so another
slot's preparation cannot conceal that path. It compares state only after
the load completion callback reports success.

A subsequent full 14-map Windows matrix passed the complete pipeline on
Forsaken City, Beginner Lobby, frozenflygone, spirialis, Hydro,
RadleyMcTuneston, Indecx, vitellary, Rocketguy2, ZZ-HeartSide, and RedBatNick.
That run preceded the final callback, camera, and bounded-stack changes.
Skunkynator still refused reconstruction, Maya timed out as described above,
and HankyMueller's requested `Easter Egg Puzzle` remained in a cutscene after
normal skip. A later check of `Double Vision` and `Feedback Loop` exposed
retained map metadata in a mod-session cache. No cutscene flags were forced.

The bounded-stack candidate
(`3aa277bd61b255e2583af4a52c22676e4282338769171f4f21a8af2fff69f4ad`)
passed the full pipeline for HankyMueller's `Double Vision` and `Feedback Loop`,
frozenflygone's `Lab-secret`, and Forsaken City's `1` and `6b`. Simulation
resumed after restoration; captured game images showed no error screen.
HankyMueller's four cold loads took 8.1-11.1 seconds and four warm loads
44.6-59.4 ms. Its retained metadata no longer caused reconstruction refusal.

That candidate still refused Skunkynator's camera alias. Maya no longer
exited with stack overflow, but both restart copies remained pending after
120 seconds while the worker was in the fresh-resource index. The live-instance
boundary fix did not resolve that stall. A subsequent trace found repeated
`System.Reflection.Pointer` visits at more than 14 million path levels through
a Lua coroutine proxy. Reading `Pointer._ptr` boxes another `Pointer`, so
reference-identity cycle detection could never terminate that walk. Both indexes
now treat boxed pointers as native leaves; capture refuses them at the field path.

The installed LuaCutscenes coroutine resumes through Everest's shared native Lua
VM. Its `LuaCoroutine` exposes `MoveNext` and an unsupported `Reset`, not a
snapshot/rewind contract. Installed Speedrun Tool also marks Lua state as a
desynchronization risk and retains its native wrappers instead of copying their
handles. Akron now refuses unhandled Lua state before native cloning, reports
the capture failure, and preserves the normal rollback path. It does not claim
to persist or rewind native Lua execution.

The native-boundary candidate
(`11a91ea80c671dde9b038ed7edf3ca32d16c352fc7b93e7e1227cab986819961`)
passed the full pipeline again for HankyMueller's two rooms, `Lab-secret`, and
Forsaken City's two rooms. Maya's `Delta` refused capture with the Lua warning.
A subsequent command check found no slot and no outstanding restart copies;
after unfreezing, the scene remained `Level` and its screenshot showed gameplay
without an error screen. This is a verified safe refusal, not Lua restore support.

That candidate still refused Skunkynator's detached grid when the callback was
its first captured reference. The original regression used the real
`TileInterceptor` callback and failed before checking all exact entity-field
aliases. CI's stripped assembly removes that callback body, so the durable
fixture uses an executable constructor closure with the same concrete
sibling-grid ownership. It checks memory/disk camera identity and mutation of
the correct grid; foreign, missing, and competing owners remain refused.
The final callback-first candidate
(`53ef1c0e746e6a1dd84e25149be0983fb6b3a87577bdfc1778cff12c6f2fe2f4`)
passed capture, per-slot restart copies, export/import, independent cold loads,
repeated warm loads, and resumed simulation in Skunkynator's `a-0` and `a-2`.
Player and controlled session state matched capture. Four cold restore bodies
took 3.7-5.1 seconds; four warm bodies took 23.3-27.6 ms. Forsaken City's `1` and
`6b` passed on the same archive. Both post-restore screenshots showed gameplay
without an error screen. The Release build and archive integrity check passed.

The final run's command responses, map logs, screenshots, result JSON, and
deployed checksum are retained in the verification workspace's
`live-alias-final` directory.
The 1,970-test TRX is under the sibling `alias-final-tests` directory; the native
Lua refusal and resumed frame are under `live-native-final` and
`lua-refusal-resume`. Earlier candidate results remain separate from this run.

Linux Mint verification has not been performed for this change: SSH to the
documented machine timed out. Its old bulk archive inside `Saves` could not
be moved. New bulk runs create their recovery archive
outside `Saves`; a local archive round trip confirmed repeated backups do not
include an earlier recovery archive.

PR review follow-up: all 1,972 Release tests passed with zero failures or skips
against the exact stripped reference archive used by CI
(`ab72454daf77701ccf8bc36e591280551795b53734c453abbf7fec4cf94fcf8e`).
The tests no longer depend on stripped engine getters, constructors, indexers,
or `EntityID` hashing. Camera fixtures use distinct primitive scalar state;
the runtime identity index compares the same type, room, and numeric ID without
calling stripped engine methods. This follow-up has headless verification,
not a new live map sweep.

The snapshot bundle now caps combined expanded documents at 1 GiB. Streaming
tests accepted exactly that boundary and refused a fourth document before
delivering its callback when it crossed the limit. A throwaway export probe
also rejected repeated documents above the limit, preserved the existing
destination, and removed its staging files.

A throwaway harness exercised the real bulk-sweep control flow with command
substitutes: SSH recovery, unrecoverable SSH, failed relaunch, failed prelaunch,
missing and empty results, stale startup logs, and launch-time SSH failure.
All eight scenarios passed, including result retention and restart ordering;
the complete shell script also passed `bash -n`.

The follow-up CI-mode solution build completed with zero warnings or errors.
Package CRCs, PDB exclusion, and exact license/notice contents passed. That
headless review package is
`7feb6e6c83b77bafe04c75aa4da146191b65197498fd68d74feb3f948a58ec96`;
it is not the earlier Windows-deployed archive.

A second review found that an outer sweep timeout could leave a nonempty
results file containing only the first completed side. The aggregate now
retains those completed rows and records every missing requested side as
blocked, including in the summary counts and bugs list. The reproduction
first failed with only the normal-side pass present. After the fix, six
merge/summary smoke scenarios passed: partial timeout, partial failure,
complete failure with unavailable-side skips, incomplete success, a requested
subset, and failed prelaunch. Complete results do not gain artificial blocked
rows, and a retry replaces stale rows without removing another map's results.
