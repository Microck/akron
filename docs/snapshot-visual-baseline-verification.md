# Visual baselines in new native snapshots

## Scope

The stationary #256 fix alone can still recapture its forced renderer values
during first Set or warm capture. Immediately after CopyIntoDormant creates the
detached SavedLevel, the capture route now copies underlying baselines into
matching owned renderer properties. The live renderer and ownership state stay
unchanged. Owner and source renderer identities must match, destination renderer
must be distinct, and source and captured values must equal the last Akron write.
Different external values and unowned properties remain untouched.

This includes the first-Set rollback graph. An enabled restore can reacquire and
reapply the setting; a disabled restore receives underlying values. Already saved
snapshots with lost provenance are not migrated or repaired. Equal-value external
writers, throwing custom accessors and arbitrary provider capture paths remain
outside this qualification.

## Recovery and verification provenance

On 2026-10-13 the cloud executor was replaced during final menu cleanup. The
previous full native evidence files and unpublished follow-up workspace became
unavailable. The three isolated production files were subsequently reconstructed
from the published #256 parent and retained exact patch/method excerpts. Their
SHA-256 values match the independently recorded pre-replacement isolated files:

- runtime adapter: 880739a50c66cc5dbe67df0aa1d01ee81f46917c2914a07a5f047a59caca0b63
- ownership helper: c509c3504d98e9ac71ad7ba9695d4fa22f560b1604333a5007e1425479376d3b
- native capture: f0b2f6031c5dbca7b09822a2218fdb55b2441271bb1cc0e051091481baf756c9

These hashes identify the pre-integration isolated files, not a complete recovered
native artifact. The unpublished original eight tests were not recoverable. Eight
new tests were authored for the same boundaries: detached normalization/live
preservation, rollback reacquisition, disabled restoration, different values,
identity guards, unowned zero access, actual dormant graph cloning/aliasing, and
production routing. They are new tests, not recovered originals. A plain renderer
graph clone does not establish native Level behavior.

## Historical native observations and limits

Assistant-operated native testing of the combined fourteen-change artifact had
shown Light recovery after first Set, Bloom recovery after a second Set, and
ordinary appearance after one warm Load with Bloom already Off. The earlier
thirteen-change diagnostic had failed the first Light branch; its Bloom/Load
branches were not reached. The revised original-settings appearance also passed.
Independent audit observed three matching complete saved Session copies at
1:22:17.174, later than both the loaded slot and pre-save display; native deaths
remained 5/4/4/4 and Akron registered deaths 0/0.

The tested combined package SHA-256 was
`d92825e5c07220c55e481c5de8a9dd4ba5b10105236cfa497e3279e8872992d5`
and DLL SHA-256 was
`548a79699d7768eebaf1d3d58b89f6e28c87c3f5f589f24703674dc8b0664716`.
Its original screenshots, raw saved files and full terminal evidence are now
unavailable. Retained findings are explicitly reconstructed records, not original
bytes. The final native Chapter-Off restoration, normal Exit and terminal custody
were not completed. No complete native protocol pass or native verification of
this newly assembled package is claimed.

Other historical qualifications: tiny left-drag UI activation was needed; an
immediate capture could precede settled rendering; first-Set pause was delayed
approximately 216 seconds. Both rejected Save-and-Quit calls delivered no game
input; the later explicitly approved call succeeded before executor replacement.

Human testing remains pending under the owner's explicit exception. Review, CI
and new managed tests do not erase missing native evidence or qualify cold
Continue, arbitrary maps/providers, old snapshot repair or whole-build behavior.

The newly authored tests and reconstructed isolated source passed all 2074
Release tests with ContinuousIntegrationBuild=true. This is a fresh managed run,
not reuse of the lost earlier 2074-test log. Independent source/test/doc review
confirmed the exact production hashes and the evidence limitations above.
