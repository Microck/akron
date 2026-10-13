# Independent Light and Bloom restoration

## Scope

Issue #256 reports Light Level or Bloom Level remaining applied after its own
switch is turned Off while Screen Tint stays On. The shared baseline previously
remained active until every visual option was disabled.

The candidate gives Alpha, Bloom.Base and Bloom.Strength independent ownership.
A baseline is captured only when that writable float property is first overridden;
Tint captures none. Off, effective policy denial and reset release ownership.
Re-enable acquires a fresh baseline. Renderer replacement releases the old object
before acquiring the new one. Missing/unwritable members remain unowned.

Release restores a baseline only if the property still equals Akron's last write.
A later different value is preserved. Equality is a coexistence safeguard, not
proof of who wrote a value, and does not establish arbitrary provider arbitration.

A narrow pre-LoadLevel callback releases only a reference-equal active visual
owner before the native room loader installs new Alpha/Base values. Detached
snapshot loaders cannot release the active owner's state. The callback performs
no global audio, recorder or policy reset. It relies on the existing engine-thread
lifecycle rather than providing a new general threading primitive.

For a custom path bypassing that callback, a changed-room fallback preserves
incoming Alpha/Base on reused renderers and releases lingering Strength if it still
matches Akron's last write. This assumes incoming room fields have been installed;
it does not claim to solve arbitrary custom restoration order or same-value writer
identity ambiguity.

## Verification boundaries

Managed tests exercise the actual ownership reconciler with plain float fields
and properties, independent toggles, re-enable, repeated calls, renderer identity,
missing members, external writes and owned-active pre-load ordering. They do not
execute native Level.LoadLevel or render lighting/bloom. Two source-routing checks
cover effective policy flags and the narrow pre-load callback before orig.
The final Release run passed all 2,066 tests with no failures or skips.

The reflected native renderer members are plain float fields. Arbitrary custom
property getters/setters that throw are not handled by this helper and are not
qualified by these tests.

## Native comparison, 2026-10-13

Qualified PASS for the two stationary Off-restoration branches. Main and the
isolated candidate each used a separate original sixteen-file profile, the same
five pinned providers and supplied City room1 fixture. Light0 and Bloom300 were
exercised separately, with Tint On at opacity0 and visible positive controls.
Each arm used one cycle per effect, not repeated trials.

On main, Light Off retained broad darkness while Tint stayed On; Bloom Off
retained strong glow. Each recovered only when Tint was also switched Off.
On the candidate, each effect recovered ordinary appearance immediately in its
Off observation while Tint remained On. Subsequent Tint Off stayed ordinary.
Two original screenshots per scored state showed advancing Chapter and the same
supported player/camera position. This is qualitative visual evidence, not a live
field getter, calibrated luminance or exact JPEG comparison.

Candidate Light C Chapter: 22:25.550 to 22:39.014.
Candidate Bloom C Chapter: 33:37.594 to 33:55.002.
Main Light C remained dark at 20:01.339 to 20:13.358; main Bloom C retained glow
at 28:40.145 to 28:55.581.

Both arms restored original Light100/Bloom50/Tint20/color0 and all toggles/panels,
performed one ordinary Save and Quit, restored native Chapter Off, and exited
normally. Native deaths ended 5/4/4/4, registered Akron deaths 0/0. All three
saved session copies agreed: main 20,644,290,000 ticks (34:24.429), candidate
25,194,680,000 ticks (41:59.468). Native/provider settings were byte-identical
to originals; Akron's only preference delta was its startup backup timestamp.
Visual use correctly retained RegularClean classification/history.

Qualification: candidate startup accidentally entered PICO-8 title before
Continue or target edits. The operator stopped, exited normally without play,
and used observed menu navigation to Continue in the same lifecycle. Full initial
and final save comparisons found no PICO progression delta. The deviation remains
part of the result. Settlement windows passed, but main early action timing uses
conservative conversation call bounds; candidate outer wrapper times are not
native dispatch/frame measurements. Action chronology is operator transcription,
not an independent complete input-event trace.

Exact candidate Akron.zip SHA256:
8734b647207b7cf1d24e75655c78ac21c5908a90d6e2513dc49726e0e9bfce2e

Exact packaged/build DLL SHA256:
9c2d635d8ca829a6f097c46f47e73db193ebef4a29fc7f22bd9367ee8b3a5316

Final independent audit SHA256:
b0a1f021d2f7ab2b212c344f6dd98f775e3a1a5d07ab2e4e06f4c980168ce0f0

Evidence retains all 194 original screenshots, complete settings/save differences,
static/generated/profile inventories, sampled safety and normal-exit receipts.
The archive is unchanged since managed/native testing; this results section was
added afterward and is not included in the mod archive.

Room transitions, policy changes, renderer replacement and provider interactions
need distinct native qualification; a stationary checkbox comparison alone cannot
establish them. No whole-feature, whole-build, human-testing or publication claim
is made here.
