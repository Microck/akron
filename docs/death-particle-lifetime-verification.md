# Custom Death Particles lifetime verification (#225)

## Change

The previous implementation scaled rendered progress but left ownership with the native death component and dead body. Long bursts ended with their owner before completing; short geometric bursts could remain as a final ring until the wipe.

The actual PlayerDeadBody update now detects creation of its DeathEffect and creates a separate, non-colliding cosmetic entity. Position, appearance and configured duration are captured at that point. The original component, Duration, Percent, callbacks, coroutine waits, End and respawn timing remain untouched. Only its rendering is replaced. The cosmetic entity survives ordinary same-Level death reload, renders behind the native wipe, and expires independently in gameplay time. It supports the Retry update path while genuine pause/freeze stops its age. Room/session changes, disabling the feature, No Death Effect, actual state restores, explicit room reload and level exit clear remaining bursts. Saving state does not clear a live burst, including the temporary reload/rollback used to prepare a first StartPos baseline. Render failure uses a scoped bypass to reach genuine vanilla drawing.

This does not turn static respawn-intro drawing into another independent burst. External savestate handling clears restored cosmetic clones; it does not promise those systems never capture them.

## Native baseline and candidate

Both used Linux Celeste 1.4.0.0 / Everest 1.6580.0, isolated copies of the same original profile, and the initial Forsaken City room's left ledge/spike pit. Ordinary spike deaths used a rightward walk; Retry checks used the native pause menu. No runtime getters, injected state or synthetic lifecycle calls were used. Respawn modifiers and assist mode remained off.

- QA67 baseline ZIP SHA-256: `f3a1c9fbd7a5d0b94529b2da0f0447e27f263de369a095f14acd14bdfa881544`.
- QA68 candidate ZIP SHA-256: `c24f5807eaf09b3685d022f912d66919b1f8fba383d1be606714345e7f684a2c`.
- Shared Celeste.dll SHA-256: `eb2d2ac4bfb62c3aa0227f0b566a37d43eabb6827512fe9d9c311484b17b9d72`.

Actual live results:

1. Baseline Square / 3.00: compact burst covered by the ordinary wipe, absent at the death position after respawn. Candidate: a separate square ring remains over the spike pit while the player is already back at spawn, then shrinks and disappears. The separate spawn-intro effect is distinguishable from the retained death-position tail.
2. Baseline Square / 0.10: outward red ring remains until covered by the wipe. Candidate: a brief visible square burst disappears while the death area is still unobscured, before the wipe arrives.
3. Square / 0.834: ordinary outward burst, wipe and respawn, with no lingering pit tail afterward.
4. Native Retry / Square / 3.00: burst progresses through Retry and remains briefly after respawn. With the remaining tail paused, identical particle positions were visible at wall-clock 12:02:54 and 12:03:22. Resuming allowed it to finish; it was gone at 12:03:24.
5. Repeated Retry tails: enabling No Death Effect while paused cleared a visible tail. A subsequent tail also cleared when Custom Death Particles was switched off while paused.
6. Vanilla shape / 3.00: a distinct round tail remained over the death pit after the player respawned, then disappeared.
7. Customization off: ordinary native burst and respawn returned, with no retained tail. Defaults were restored through the UI to Off / Vanilla / 0.834, with No Death Effect off and unchanged colors/respawn settings. Save and Quit followed by Exit ended the process successfully at 16:14:26 UTC.

Five candidate MKV/MP4 recording pairs and original screenshot sequences were preserved locally. Capture resolution, audio-off recording, an unused F5 stop binding and built-in clock labels were explicit prelaunch fixture edits; these remain separate from the tested gameplay settings and are not a claim of full original-profile rollback.

## Limits and unverified cases

The native recorder reports dropped frames. CFR timestamps and frame-count differences are not actual elapsed game time. The chapter timer freezes/resets during death and advances during genuine pause, so it cannot measure particle age. Exact 0.10/0.834/3.00-second timing and exact respawn latency were not calibrated. The live claims are visible progression, relative termination, ownership across respawn, pause behavior and cleanup.

A native StartPos load attempted while paused was rejected by the existing paused/transition/cutscene guard. It did not run, so this is not a live save-load cleanup pass. Room-transition cleanup, SpeedrunTool restore, forced renderer failure, additional shape/color combinations and other platforms remain source/structural coverage rather than live acceptance in this run. No claim of those cases passing is made.

## Automated and source checks

Release and Debug each pass 2,357 tests. New tests check independent age/duration progress and inspect emitted code to ensure the owner does not write native DeathEffect or PlayerDeadBody fields. Structural checks cover Retry/pause tags, state cleanup, save exclusion and fallback wiring. These do not replace native rendering tests. Read-only source/IL review found and helped resolve Retry update, fallback recursion and state/level cleanup gaps before the tested candidate was frozen.

## Review follow-up: capture versus restore

Review identified that the shared render-suppression helper also runs after saves, and that explicit Reload Room retains the same Level/session/room identity. Clearing moved from that generic helper to actual native/persistent restore boundaries and SpeedrunTool load callbacks. Explicit Reload Room clears immediately before the native reload. A narrowly scoped capture bracket preserves original cosmetic entities through temporary first-Set baseline preparation and rebinds their session only after successful rollback; failed rollback cancels them.

Follow-up Release package SHA-256: `b25eb02a54cbb2b09b189800e616788618974fedb6fb105a53fcd1c5c9d6d0b0`. Release and Debug each pass 2,357 tests.

- QA69 old package: a visible paused Retry tail remained after explicit Reload Room. QA70 follow-up: the equivalent reload cleared the tail. Both runs exposed the separate paused-menu softlock tracked in #320; neither counts as a successful gameplay-resume check.
- Normal-speed first-Set recordings were inconclusive: a substantial ring disappeared around capture, but recorder stalls/dropped frames prevent distinguishing premature clearing from elapsed simulation time. These are not preservation passes.
- QA71 used the unchanged follow-up package and native Timescale 0.1 to extend observation. After a real spike death and respawn, setting Slot 2 left a substantial separate pit ring visible beside the returned player. Immediately pausing showed both the ring and the “StartPos 2 captured” toast. This directly verifies post-respawn save preservation. Source tracing establishes that ordinary Level.LoadLevel clears the shared baseline cache, so this sequence also exercises first-baseline preparation; no injected getter or lifecycle call was used.
- After native unpause, Load 2 succeeded. The completed “Loaded StartPos 2” observation showed no tail in the unobscured lower pit region. The overlay obscured part of the view; the immediately preceding action screenshot was pending and is not the completion evidence.
- Repeated Set attempts while the restart copy was pending were rejected. Native pause allowed its background copy to complete. Paused capture failures and pending attempts are excluded from acceptance.

These results supplement the core lifetime checks above. They do not establish exact duration, forced renderer failure, SpeedrunTool behavior, or all transition paths.

## Renderer fallback follow-up

A second review found that a custom-render failure after the native dead body disappeared could not fall back through that dead body's component. The independent entity now owns a persistent vanilla fallback using DeathEffect.Draw under the recursion bypass. It retains its age/duration and remains the sole renderer while customization is enabled; disabling customization still restores native component rendering. If vanilla drawing itself fails, the cosmetic is cancelled safely. This exceptional branch is source/structural coverage; no live renderer exception was injected or reproduced. The preceding b25 package live results predate this additional fallback-only correction.

QA73 ordinary-path regression used final Release ZIP `8f58a673208be1cc5d9c90b08a36ab3388cebe8b14e8e65e01d7e739b7ebc3f9`. A real spike death with Square/3 retained a substantial pit tail beside the respawned player; native pause retained that tail and switching customization off cleared it. Defaults were restored and the session exited through Save and Quit and Exit. Both Release and Debug pass 2,357 tests. This smoke test validates the normal owner/disable path after the fallback edit; it does not exercise a thrown renderer.
