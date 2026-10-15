# Dream State refresh verification (#269)

Dream State previously changed Session.Inventory.DreamDash without updating existing DreamBlock instances, which cache the capability and particle palette. The action now snapshots existing DreamBlock entities and calls native ActivateNoRoutine or DeactivateNoRoutine after the inventory change. Existing player and policy guards remain. No room reload or private-field write is used.

## Native before/after

Linux Celeste 1.4.0.0 / Everest 1.6580.0. Original Old Site A room 12d, native world spawn 2160,304. Entry used chapter selection and Creator room navigation. All testing used native UI and keyboard, without injected state or runtime getters.

QA75 baseline package SHA-256: `0d8ba7f867316d9159fe3d79c666bdd7a1ef2effa37fbb84cc8d5740a9e3066c`.
QA76 candidate package SHA-256: `576bb3594dbf7ca07c4f180ccbf9d7ad658f0456b33d2bd8c4099bb390a4cfcb`.

Baseline: set Dream State On and use native Retry to build active blocks. Toggle Off without moving or reloading: UI reports Off, but both blocks retain bright outlines and colored particles in immediate and settled observations. Native Retry with Off unchanged produces muted gray/blue outlines and desaturated particles.

Candidate: copied the baseline's native-saved post-exit profile byte-for-byte, preserving room 12d and spawn. Initial Off is muted. Toggling On immediately produces bright outlines/colored particles. Toggling Off immediately returns muted outlines/desaturated particles, already consistent with a subsequent native Retry at Off. Repeated On and native Retry at On remain visually consistent, and a final Off toggle restores muted presentation. No lingering full-white block or stuck flash was observed. Position and camera geometry remained stationary.

The candidate exited with native Save and Quit and Exit. Persisted Akron settings differ only by backup timestamp. Screenshots and process/profile receipts are preserved. Session.Dreaming is a distinct native flag and is not used as evidence of DreamDash capability.

## Limits

This verifies presentation classes for the two static vanilla dream blocks in the reported room, not equality of randomized particle pixels. Native Setup regenerates particles and consumes random values. Traversal/collision, moving-block tween creation, subclass-specific state, cutscene-owned activation routines and other platforms were not tested. The public native refresh and Setup methods are nonvirtual; this is not a promise to synchronize every modded subclass lifecycle.

Release and Debug each pass 2,360 tests. The new emitted-code check verifies both native refresh calls, a stable enumeration snapshot, and absence of an action-driven Level.Reload. It supplements, rather than replaces, native visual evidence.
