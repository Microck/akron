# StartPos spawn edit persistence verification (#235)

## Fix

Committed slot edits now update persisted spawn metadata before changing the runtime object. Snapshot format and location metadata are retained exactly; the room snapshot is not recaptured or rewritten. Pending captures keep their exact runtime object and defer metadata publication to capture completion. Stale slot/profile/map/state identities are rejected. Save failure restores the previous catalog entry and leaves runtime configuration unchanged, with a visible failure message.

Popup and command edits now apply only the requested field. This avoids replacing the other fields with unrelated global defaults when editing an existing slot. The existing public all-fields method remains available.

## Automated verification

Both Release and Debug suites passed 2,246 tests, including 13 new cases. These exercise committed metadata replacement, synthetic rebuilds, format/location retention, unrelated slots, single-field preservation, pending ownership, stale objects, missing metadata, failed-save rollback and clamping. Production identity checks and UI/command routing are source assertions, not native executions. Existing CL0006 warnings remain.

## Native baseline QA38

Celeste 1.4.0.0, Everest 1.6580, native Linux desktop, 1364×1024 window. Exact baseline package SHA-256: `e1cfb9740199a81616b6e44ed51d3c24c4742c6c170ea2b929443b3eafe20642`.

The disposable Prologue profile initially had no StartPos slots. A native Set captured slot 1. Ordinary Set intentionally captures native state rather than the configured spawn defaults. Edits made while its restart copy was pending were retained when the first Load flushed that copy; this was a pending-capture control, not the regression.

After the slot was committed at 5 dashes / 50% stamina / Facing Left:
1. Edited it to 2 dashes / 25% stamina / Facing Right without recapturing.
2. First Load showed the native Dash Number at 2 and a quarter-full Stamina Bar while Wait for input held the restored frame.
3. Released the hold with a short normal Right input, reopened the popup, and found it reverted to 5 / 50 / Left.
4. Loaded again without edits: Dash Number was 5 and Stamina Bar half-full.

Popup facing values are verified; physical facing is not a separate quantified acceptance claim. Releasing the input wait caused small movement and normal refills, so only held-frame HUD captures are used for spawn values. Popup clicks during input wait did not open it; screenshot 06 is retained but is not popup-value evidence. All preferences were restored except the expected backup timestamp, and native exit returned code 0.

## Native candidate QA39

Exact tested Release SHA-256: `619ec6d02ec9eed5fb6060c2fe1061b47aa56452c7a4e2146832420e72e2c8b1`. Source-root Akron.zip was later overwritten by the Debug build; it is not the tested Release artifact.

Used the same retained committed slot and snapshot. No Set or recapture occurred in either candidate process.

- Initial popup showed 5 / 50 / Left.
- Editing only Dashes to 2 preserved Stamina 50, Facing Left, Idle and Grab, despite different global defaults.
- Edited Stamina to 25 and Facing to Right.
- First cold Load showed 2 dashes and quarter stamina; the popup retained 2 / 25 / Right afterward.
- Repeated warm Load without edits again showed 2 and quarter stamina.
- Saved and exited the application normally, then relaunched the same runtime and profile without resetting either.
- After full restart, the popup retained 2 / 25 / Right, and a new Load again showed 2 and quarter stamina.
- Restored global defaults through an empty selected slot, then restored the original selected slot, wait setting and HUD toggles, retaining the edited test slot.

The snapshot SHA-256 before editing, before restart and after restart was `84c66f0057c48fcd584472cbb8dec0a9b8bd1b69e3b498edf5d3ccf5f4147107`. It resides under the runtime's Saves/AkronStartPos directory, outside the profile tree, and is preserved with the evidence.

No console commands, runtime getters/setters or injected state were used for acceptance. Native disk-failure injection, stale-profile edits, pending-capture replacement failure, and every spawn field combination were not exercised; tests and source review cover bounded parts of those paths. No human QA is claimed.
