# Ground refill hair-color verification

## Correction

Issue #247: the post-player-update ground-refill clamp already preserves the actual dash count, but native hair selection and Akron's custom palette run earlier and see the transient refill. Native hair can therefore stay red at zero charges; a custom one-dash color can likewise hide the configured no-dash color.

The native player update now carries an allocation-free, thread-local scoped context. Before the existing single native hair update, the matching player/scene/session's blocked ground-refill increase is corrected. Current master, dash-refill, dead, grounding and policy conditions are checked. A finally restores the previous context, including nested calls and exception unwinding. The existing post-update fallback and stamina correction remain.

Native hair interpolation, special-state colors, overrides and custom palette ordering remain authoritative. The patch does not simulate hair twice or directly write hair color, lastDashes or flash timers. The unchanged post-update fallback retains its previous same-object reload/mod-callback limitations; this is not a general refill-side-effect or provider-ordering repair.

## Live baseline and candidate

Separate fresh original-profile copies on Linux Celeste 1.4.0.0 / Everest 1.6580, City room 1. Normal native assists; EVM master Off; MotionSmoothing disabled. All runtime actions used ordinary native keyboard/mouse controls. No runtime getters, injected state or helper lifecycle callbacks.

- QA48 baseline package SHA-256: `9474c860dd53cc6f56c38cbd04d900a71f0ffc2529d8b15a273c26bb8314e434`.
- QA49 candidate package SHA-256: `f32f2ffcce52fb1c67cbff1dc5c6b319d80c96d6a42448cd9feb4957ed5cf37b`.
- Celeste.dll SHA-256: `eb2d2ac4bfb62c3aa0227f0b566a37d43eabb6827512fe9d9c311484b17b9d72`.

Baseline: Ground Refills On / Dash refill Off; a real dash into the safe left wall left the visible Dash Number at 0 but native hair red. With No-dash FF8000 and One-dash FFFFFF enabled, the grounded zero-dash hair was white. Ground Refills Off restored actual 1/white.

Candidate: the same real consumption produced settled 0/blue native hair and 0/orange custom hair. After an ordinary jump, the captured settled landing remained 0/orange. Master Off restored 1/white. With dash refill allowed and only stamina refill blocked, a real dash returned to 1/white, so the dash correction did not apply to the stamina-only control. Re-enabling dash blocking and consuming another dash produced 0/orange again. Turning custom colors Off returned 0/blue; turning Ground Refills Off restored 1/red. Original settings were restored through native UI.

Airborne screenshots were not obtained: baseline jump attempts and one candidate attempt were captured only after landing. They are not airborne negative-control proof. Native/managed tests do not substitute for missing airborne evidence. No stamina-consumption, arbitrary provider, scene-reload, all-character-state or whole-build certification is asserted.

## Tests and custody

2,271 tests passed in both Release and Debug. Nine pure production-predicate cases cover grounding, disabled/master, allowed dash refill, death, unchanged counts and consumption. A Cecil test checks correction/visual/native ordering, one hair delegate invocation, finally restoration, retained fallback and policy call, and no direct visual-field writes.

The native baseline has 13 original screenshots and the candidate 15. Source-root ZIPs may be later Debug output; the frozen Release archives are the tested artifacts. Disposable saved time/statistics change and are disclosed separately; no unchanged-profile or zero-death claim. Raw artifacts are retained locally and are not claimed remotely uploaded. Owner-authorized human-QA exception applies; no human testing is claimed.

## Review correction and exact successor recheck

The first PR review identified duplicate runtime telemetry from authorizing both the pre-hair correction and the retained fallback through TryUseRuntime. The pre-hair correction now uses non-recording CanUse authorization; the existing fallback remains the single recording path. The Cecil routing test requires non-recording authorization in the early hook and exactly one retained fallback TryUseRuntime call.

QA51 rechecked the exact successor Release SHA-256 `2355f415f06eb8b861e7677a38b57d3530c59e89b61cab4eeb7c68208b4ab9ad` in a separate fresh original City profile. A real left dash into the safe wall produced settled HUD 0/native blue. With No-dash FF8000 and One-dash FFFFFF enabled, zero-charge hair was orange. Disabling Ground Refills restored actual 1/white. Original settings were restored, with only the backup timestamp differing. Nine original screenshots and final profile/log receipts are retained locally. Exit 0; saved totals changed by +1 death and +1 dash, with jumps unchanged; the incidental death cause is not established.

Both successor Release and Debug suites passed 2,271 tests. QA49 remains evidence for the initial artifact, while QA51 supplies the successor's bounded native acceptance. This is not an additional airborne, stamina, special-state, or telemetry-volume live certification.
