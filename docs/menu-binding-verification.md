# Menu binding identity verification

## Fix

Issue #220 remains reproducible in the current Menu Bind (Keyboard) screen, not only the removed all-settings editor. The native row retains `Monocle.Binding`; default normalization previously replaced its `ButtonBinding` wrapper when clearing the last usable mapping. The row then edited an orphaned binding while runtime and saves used Tab.

Normalization now changes an existing binding's collections in place, preserving the wrapper, native Binding, existing lists and initialized virtual input reference. Only an absent wrapper is created. The default-selection predicate and supported controller/chord rules are unchanged.

## Native baseline and candidate

QA45 and QA46 used separate original-profile copies on Linux Celeste 1.4.0.0 / Everest 1.6580, City room 1, native keyboard input and the production mod-options screen. No runtime getters or injected state were used.

- Baseline package SHA-256: `49c9ecd7f30052f39d40b37f57f23c63dd267d1c5d93e53f4bf92e7720a778c7`.
- Candidate package SHA-256: `9474c860dd53cc6f56c38cbd04d900a71f0ffc2529d8b15a273c26bb8314e434`.
- Runtime SHA-256: `eb2d2ac4bfb62c3aa0227f0b566a37d43eabb6827512fe9d9c311484b17b9d72`.

Baseline: replacing nonempty K with O worked and persisted. Clearing K then assigning L displayed L, but two L presses did not open the overlay. Reopening showed Tab, matching the saved setting. Original K was restored and worked.

Candidate: clear K→L and clear L→O both worked immediately, reopened correctly and saved the intended key. Previous keys stopped opening the overlay. Cancelling a remap preserved O. Clearing and leaving displayed/reopened the coherent default Tab; native Reset also restored Tab. A replacement after Reset worked. O continued working after a full process restart; the reopened row still showed O. Original K was restored and verified functional afterward.

Native remap capture times out if a tool round trip is too slow. The initial baseline timeout attempt was retained but not scored as a product defect; subsequent remap confirmation and key entry were batched within the capture window. Tab was not tested as an in-game toggle because this fixture also binds it to SpeedrunTool.

## Automated and review scope

The final suite passed 2,261 tests in Release and Debug against the native references. A source invariant guards the reset identity pattern because the CI stripped Everest fixture has invalid IL in binding collection getters. Seven additional behavioral binding tests passed against native references during development but are not shipped in the stripped-reference suite. Native UI acceptance, rather than that source invariant, establishes the gameplay behavior.

Controller editing, arbitrary simultaneous imports and all possible keyboard layouts are not separately live-certified. Original input profiles remain untouched. Disposable saved time and death counters change; the baseline gained one unexplained death, so no whole-profile/stat restoration or zero-death claim is made. Raw evidence is retained locally, not represented as remotely uploaded. Human QA is excepted by the owner's authorization; no human testing is claimed.
