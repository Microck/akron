# Reload Room trigger teardown (#324)

Native CameraTargetTrigger.OnLeave uses its Scene. Player.Removed calls OnLeave for active triggers. Level.UnloadLevel can detach triggers before removing the player, producing a null-scene exception during Reload.

The action now queues a snapshot of tracked players for removal before calling native Reload, matching the established room-warp ordering. Actual removal stays in native UnloadLevel; trigger leave callbacks are neither cleared nor swallowed. The action rejects completed levels both on entry and inside its deferred callback, because native Reload returns immediately for a completed level and would otherwise leave queued removals without replacement.

## Native reproduction and candidate

Linux Celeste 1.4.0.0 / Everest1.6580.0, original Old Site A d3. Noclip was used through native UI only to stage the player at the Lookout camera-trigger platform, then disabled. No map edits, injected getters or synthetic lifecycle calls. Mod fixture includes TAS and SpeedrunTool.

QA97 integrated package encountered CameraTargetTrigger.OnLeave -> Player.Removed -> EntityList.UpdateLists -> Level.UnloadLevel -> Level.Reload during paused Reload after Lookout/DreamState checks. The later TAS GameInfo null-reference was secondary. This run was aborted after preserving the critical error and is not an integration pass.

QA98 historical PR318 package SHA256 `f3a1c9fbd7a5d0b94529b2da0f0447e27f263de369a095f14acd14bdfa881544` reproduced the same primary exception on unpaused Reload, with no Lookout interaction or DreamState toggle. This demonstrates the defect predates the later fixes. Native critical-error Exit completed; no Save and Quit claim is made for this baseline.

QA99 candidate Release SHA256 `aaba8ce8a311898e01b3c58546b1a68fb688c489eb7d97868f3edde56769994b`: unpaused Reload from the staged trigger region returned to the room without the critical error. A later paused Reload after native Lookout hold/release and DreamState On/Off checks also returned to gameplay and allowed the native pause menu to reopen. Original screenshots and logs are retained.

Release and Debug each pass 2,378 native-reference tests. Source-wiring assertions guard both completed-level checks and player-first ordering. Completed-level behavior is statically checked, not live-exercised. Third-party Reload veto/defer hooks, unusual Global-tagged players, previously queued trigger removals, and other game versions remain unverified. Audio capture limitations from QA95/96 remain unchanged; this change does not claim audible verification.
