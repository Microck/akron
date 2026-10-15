# Paused room reload verification (#320)

## Change

Reload Room removes the native TextMenu during Level.Reload but leaves the same Level paused. With no menu left to handle Resume, gameplay remains stuck and a fresh pause menu cannot open. The action now remembers whether the level was paused and, after Reload, invokes native Level.Unpause plus Akron's existing unpause handler if the same level is still current and paused. This runs after the removed menu is gone, avoiding a settings-save coroutine attached to a menu that is about to be destroyed. Unpaused reload does not take the extra path. Confirmation approve already closes its prompt before queuing reload; cancel never reloads.

The own unpause handler closes Pause Tracker timing, updates autosave/lag-pauser state and starts configured Pause Timer behavior. This is not a new global Everest event emission. Native Mod Options persistence and arbitrary third-party pause replacement during LoadLevel are not covered by this narrow correction.

## Native evidence

Linux Celeste 1.4.0.0 / Everest 1.6580.0; original Forsaken City left-ledge profile, isolated game and save copies. UI controls and native keyboard only; no injected getters or synthetic lifecycle calls.

- QA69/QA70 baseline: native pause followed by direct Reload Room discarded the menu, left a dim room with timer 0 and no usable Resume; Return/Escape did not recover. Runs ended with AltF4, not Save and Quit.
- QA72 candidate ZIP SHA256 `129f225902aa0676d14e2d3b076b52388d52a462e12e68ed3b9320944ba360c0`: direct paused Reload completed its wipe, the timer advanced and native C jump responded. The pause menu reopened normally. Original screenshots 01–05 preserve these observations.
- Unpaused Reload also resumed with responsive jump (06–07).
- Confirmation enabled: Cancel left the attempt timer intact and gameplay responsive (08–10). Confirm reset/reloaded and returned responsive gameplay (11).
- Pause Timer enabled at 3: sequence 13 visibly displayed 3, 2, 1, then room reveal continued. The room was black while the reload wipe was held by countdown. Afterwards gameplay/timer progressed; this is not an exact timing calibration.
- ConfirmReloadRoom, PauseCountdown and AllowPauseBuffering were restored Off. Post-exit persisted settings match prelaunch except backup timestamp. Save and Quit followed by native Exit completed successfully at 17:34:17 UTC. No recording was taken; 34 original screenshots and runtime receipts were preserved.

Release and Debug each pass 2,359 tests. Added source and emitted-code checks guard the order and native/own-handler calls; these supplement the live menu/gameplay checks. General third-party callbacks, other platforms, full autosave behavior and exact pause-stat counts remain unverified.

## Final integration smoke

QA74 repeated the direct paused Reload, responsive native jump, and reopening of the pause menu after incorporating the final #319 renderer fallback. Final ZIP SHA-256: `0d8ba7f867316d9159fe3d79c666bdd7a1ef2effa37fbb84cc8d5740a9e3066c`. Release and Debug each pass 2,359 tests. The unchanged original settings were retained; native Save and Quit and Exit completed. This is an integration smoke, not a rerun of every QA72 case.
