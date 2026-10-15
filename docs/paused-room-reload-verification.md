# Paused room reload verification (#320)

## Change

Reload Room removes the native TextMenu during Level.Reload but leaves the same Level paused. With no menu left to handle Resume, gameplay remains stuck and a fresh pause menu cannot open. The action now remembers whether the level was paused and, after Reload, invokes native Level.Unpause followed by Everest's global unpause dispatcher if the same level is still current and paused. This runs after the removed menu is gone, avoiding a settings-save coroutine attached to a menu that is about to be destroyed. Unpaused reload does not take the extra path. Confirmation approve already closes its prompt before queuing reload; cancel never reloads.

Akron's existing OnUnpause subscription closes Pause Tracker timing, updates autosave/lag-pauser state and starts configured Pause Timer behavior. Other subscribers receive the same global notification. Native Mod Options persistence and arbitrary third-party pause replacement during LoadLevel are not covered by this narrow correction.

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

## Global lifecycle review follow-up

The initial fix called only Akron's private unpause handler. Review identified that other Everest subscribers would miss the matching global event because Reload destroyed the native menu callbacks. The corrected path uses a cached typed delegate to Everest's internal `Unpause(Level)` dispatcher immediately after native unpause, allowing the existing Akron subscription to account once. It resolves the exact signature through reflection because the dispatcher is not public in the mod reference API. It reads the current subscriber list at invocation, not a captured list. Subscriber exceptions retain native propagation semantics.

On an incompatible Everest build with no bindable dispatcher, a one-time warning explicitly reports degraded own-handler-only accounting. This is not a claim of global delivery on unknown versions. The verified native/reference assembly is Everest 1.6580.0. Native Mod Options persistence and arbitrary pause replacement remain outside scope.

Release and Debug each pass 2,362 tests. Isolated event tests verify current and late-added subscribers are each called once per notification and subscriber exceptions are not reflection-wrapped. These are automated tests, not a claim of live third-party-mod acceptance.

QA91 used Release ZIP `f3f7c4e1bf35f1daf84f066162091e5d49ffdd65aaaa5cd0fab4c6396f9c8b66` in the native Old Site 12d fixture. Direct paused Reload resumed responsive gameplay and allowed the native pause menu to reopen. With Pause Timer set to 3, reload progressed through its countdown and returned to gameplay. No unavailable-dispatcher fallback warning appeared in the native log. This validates the native recovery/countdown path; it does not inspect arbitrary third-party subscribers in the running game.

## CI reference compatibility

GitHub builds against stripped game reference assemblies. Their Everest event bodies are no-ops, so executing real event tests against those references is not a valid integration test. The native tests are now explicitly marked `NativeEverest` and skipped unless `AKRON_NATIVE_EVEREST_TESTS=1` is set with executable game references. They remain enabled in local native-reference Release and Debug runs. CI runs separate executable event fixtures through the same exact-signature resolver, covering current/late subscribers, single delivery, exception identity, and incompatible signatures. Emitted-code checks tie that resolver to the real Everest type and ensure direct exception-preserving invocation.

The updated native-reference suites pass 2,369 tests in Release and Debug, including both native event tests. Default mode reports those two integration tests as skipped, never passed. This test-only compatibility correction and resolver parameterization do not add new live third-party-mod coverage; QA91 remains the native gameplay evidence above.
