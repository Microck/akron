# StartPos native-save session linkage

## Reproduction

Two independent clean-core Celeste 1.4.0.0 / Everest 1.6580.0 profiles used
untouched main `2cee797dd3d7f07abed74bd09fd6a16ebf4e4ec2`, package
`c3397cecdbef21c68da25b7419a3f1ea2c439711f04d0d41d5ec64ad65be5e68`.
Both began with the same twelve frozen input files and performed two native
StartPos Sets. Only the treatment performed one logged warm Load between
those Sets. Native Save and Quit and normal Exit completed in both arms.

The control's final capture recorded Session.Time 11,271,340,000 ticks;
all three serialized native/ModSaveData session copies subsequently held
12,468,990,000 ticks. Native cold Continue showed an advancing Chapter clock
20:51.319 then 21:00.856, consistent with the saved 20:46.899.

The warm-load treatment's final capture recorded 10,933,380,000 ticks, but
all three saved session copies held only 9,700,370,000 ticks: 123.301 seconds
older than an already completed live capture. Session/current-room deaths
also remained 3, while global/area deaths increased to 5/4. Exact capture
and save bytes were independently inspected. Native cold Continue then
showed 16:19.302 and 16:29.859, versus the already observed prequit 19:22.953
and final-capture 18:13.338. The first settled cold clock was 183.651 seconds
below the prequit clock at the same supported pose. No StartPos operation
was performed during cold Continue. Independent final visual and file audits confirm both observations.

The Set captures are active interventions, including first-Set fresh-room
priming/rollback, not passive telemetry. Both arms use them equally. No
movement, deliberate death, provider or injected observer was used. This is
one bounded map/profile comparison, not universal state-fidelity evidence.

## Mechanism and change

Warm restore clones a saved Level into the live Level, replacing its Session,
while deliberately retaining global SaveData. Without rebinding the native
save reference, the new live session can advance while the saved session is
obsolete. The file/capture discrepancy is directly observed; exact live
object identity was not instrumented.

After the normal restore has selected its final live session and preserved
cumulative statistics, this patch links SaveData.CurrentSession_Safe to that
session. It does not copy time or other fields back into immutable snapshots,
replace global SaveData, or call StartSession. Everest's StartSession has
unrelated module-session reset and progression effects. Legacy CurrentSession
remains Everest's responsibility; its safe reference supports custom maps.

Full rollback retains its captured shared-clone alias and is unchanged.
The helper tolerates missing SaveData/session without creating data or clearing
an existing pointer; the full restore still requires a live Level and Session.
The callback ordering still lets registered restore actions
see the repaired reference. No persistent snapshot schema changes are made.

Installed native IL separately establishes that Save and Quit directly adds
one death to session, room, global and area counters without Player.Die. That
explains the matched ordinary +1 accounting; it does not prove uninterrupted
absence of gameplay deaths or whole-save equality.

## Verification status

Ten focused tests exercise direct reference linkage, later live updates,
repeated loads, null inputs, rollback preservation and production ordering.
Small production-cloner graph checks cover alias preservation and snapshot
isolation. These are helper/clone/source-order checks, not native lifecycle
simulation or full Level-graph rollback coverage.
The full Release suite passes 2,055 tests, including these ten tests. The
earlier filtered run contained eight tests before the two clone checks were
added. No human review/testing, push, PR or merge has been performed. Other
maps, providers, cold StartPos reconstruction, failure rollback, and alternate
time/death policy paths remain unqualified.


## Isolated candidate native acceptance, 2026-10-13

Exact package `7c89413a53d92424299420490b3423a5989801c95b99802544ca572dc3493d73`
(DLL `5206dd57320622fd6fead02e090c9f5de16c745677e16090b09e4c814447b236`)
completed the same fresh-input two-Set/one-warm-Load lifecycle, followed by a
new-process native Continue. Both processes exited normally with code zero.

The final witness held 12,503,840,000 ticks. All three first-save session
copies held 13,413,000,000 ticks (22:21.300), 90.916 seconds newer than the
witness and later than the visible prequit 21:54.644. Session/room deaths
were correctly 4/4; global/area were 5/4. Cold Continue displayed 22:26.485
then 22:36.889 at the same supported pose, with no cold Set or Load. It was
31.841 seconds newer than prequit and consistent with the sealed first save.
This contrasts with the independently verified main failure above.

Final native clock and slot selection were restored; native settings bytes
matched the original, and Akron preferences differed only in backup timestamp.
Both canonical snapshots and 134 sealed first-output files remained unchanged.
All 69 original images have verified hashes/dimensions; the independent auditor
directly viewed 25 and reconciled full file, log and custody accounting. Two
failed retirement-helper calls were retained without credit; later native
receipts confirmed both owned PIDs absent alongside ordinary exit evidence.

This is one bounded isolated-build repair, not combined-build acceptance,
full rollback coverage, live object-identity instrumentation, or a claim about
all session fields. The ten/eleven-change packages require their own evidence.
