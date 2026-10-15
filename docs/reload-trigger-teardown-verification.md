# Reload Room trigger teardown (#324)

Native CameraTargetTrigger.OnLeave uses its Scene. Player.Removed calls OnLeave for active triggers. Level.UnloadLevel can detach triggers before removing the player, producing a null-scene exception during Reload.

The action establishes a thread-local, nested-safe teardown scope around Reload. An IL hook at the native UnloadLevel call to UnloadEntities stable-partitions the native-selected list so players precede other entities. It does not queue removals or alter the original list. Native Global exclusions and relative order within each group are preserved. Reload, UnloadLevel, or UnloadEntities hooks that veto the native operation therefore leave players intact. The scope is restored in finally; a deferred orig invoked after the scope ends receives no stale teardown intent. Trigger leave callbacks are neither cleared nor swallowed. Completed levels are rejected both on action entry and inside its deferred callback.

## Native reproduction and candidate

Linux Celeste 1.4.0.0 / Everest1.6580.0, original Old Site A d3. Noclip was used through native UI only to stage the player at the Lookout camera-trigger platform, then disabled. No map edits, injected getters or synthetic lifecycle calls. Mod fixture includes TAS and SpeedrunTool.

QA97 integrated package encountered CameraTargetTrigger.OnLeave -> Player.Removed -> EntityList.UpdateLists -> Level.UnloadLevel -> Level.Reload during paused Reload after Lookout/DreamState checks. The later TAS GameInfo null-reference was secondary. This run was aborted after preserving the critical error and is not an integration pass.

QA98 historical PR318 package SHA256 `f3a1c9fbd7a5d0b94529b2da0f0447e27f263de369a095f14acd14bdfa881544` reproduced the same primary exception on unpaused Reload, with no Lookout interaction or DreamState toggle. This demonstrates the defect predates the later fixes. Native critical-error Exit completed; no Save and Quit claim is made for this baseline.

QA99 candidate Release SHA256 `aaba8ce8a311898e01b3c58546b1a68fb688c489eb7d97868f3edde56769994b`: unpaused Reload from the staged trigger region returned to the room without the critical error. A later paused Reload after native Lookout hold/release and DreamState On/Off checks also returned to gameplay and allowed the native pause menu to reopen. Original screenshots and logs are retained.

Release and Debug each pass 2,378 native-reference tests. Source-wiring assertions guard both completed-level checks and player-first ordering. Completed-level behavior is statically checked, not live-exercised. Third-party Reload veto/defer hooks, unusual Global-tagged players, previously queued trigger removals, and other game versions remain unverified. Audio capture limitations from QA95/96 remain unchanged; this change does not claim audible verification.

## Review follow-up: veto-safe native unload ordering

The first candidate queued players before the interceptable Reload call. Review correctly identified that a third-party Reload veto could then remove players without replacement. The revised native-list approach above supersedes that candidate. Automated executable fixtures cover matching/unrelated scopes, stable ordering without changing the caller list, veto/deferred-orig cleanup, nested throwing scopes, and exact-one native UnloadEntities call-site matching. QA99 evidence above describes the first candidate. QA101 revalidated the revised scoped native-list implementation.

QA101 Release ZIP SHA256 `753e39ed895f08cdc1725f80ee1579e38a2d498411d927b18e56817e21d16c04` repeated both unpaused and paused Reload from the camera-trigger region. Both returned to the native room without the critical error. Post-reload jump screenshots show Y444/V90 and Y446/V105 versus grounded Y456, and the native pause menu reopens. The revised native path is therefore live-verified for this fixture. Veto/defer behavior is covered by isolated executable scope/list tests, not a live third-party veto mod. Release and Debug each pass 2,384 native-reference tests; default mode passes 2,382 and explicitly skips the two native Everest event tests. No missing/ambiguous unload-hook warning appeared.
