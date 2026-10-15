# ECD Cursor Zoom release verification

Issue #246. Native Linux Celeste1.4.0.0/Everest1.6580.0, ECD1.2.0. All acceptance uses real game UI and native inputs, without runtime field injection.

## Baseline QA104
Package `b477587d25430f538e685105aa6918abfdfc20d4b7bf1add6d459e547609e3ae`.
In ordinary City10z, Cursor Zoom150% Toggle deactivation restored normal scale. With Allow zoom out and50%, first Toggle Off left the half-scale view applied, including a later settled screenshot. Stored50% remained. Explicit Reset recovered normal scale. A later missed Reset click was a desktop-focus issue and is not scored as a Reset failure. Native SaveQuit/Exit0; settings restored.

## Candidate QA106
Package `6787c562660c8393fee3995df8407d55880873dd79c9bc8b00ec52df65dcc204`.
- First50% Toggle Off restores normal scale while keeping configured50 and Reset on deactivate Off.
- Ordinary movement after release crossed into adjacent City11; camera rendering remained normal. This is not a same-room pose comparison.
- Re-activate50%, then first explicit Reset: normal scale/configured100.
- While active at50%, configure150% and close overlay: native magnification works, and deactivation restores normal scale.
- Native SaveQuit/Exit0, final CursorZoomOff/100/Hold/AllowZoomOutOff. No release-pending warning in the run log.

## Custom map QA107
A private content-only fixture copies installed Prologue geometry under a new SID and supplies ECD's documented map metadata: EnableExtendedCamera=true, RestingZoomFactor=.75. No custom DLL or manual hook/runtime setter. Native Everest Map List enters the map; native room selection stages room0 before scoring. The copied game asset is not part of this source change or a distributable deliverable.

The map-resting baseline is captured before the override.50% activation visibly shrinks terrain; first and repeated Toggle Off return to the captured map-resting scale after settling, rather than leaving50% or forcing ordinary100%. Movement after the first settled release remains normal. Configured50% is preserved. A first explicit Reset while50% is active also returns to the map baseline. Immediate post-release screenshots can show interpolation; settled observations are the acceptance oracle. This covers a resting custom camera, not every ECD trigger/cutscene combination.

## Implementation and tests
Capture the first pre-force viewport in a scene-bound lease, including before potentially partial foreign calls. Release restores that viewport before native Level.ResetZoom dispatch. Active ECD's hook then evaluates and installs the current map camera; inactive vanilla retains the restored viewport. Restore ECD automatic mode afterward. Cleanup failure retains ownership; successful camera reset is not repeated merely because automatic-mode restoration is pending. A new force re-arms camera reset without replacing the original baseline.

The same cleanup handles deactivation, explicit Reset, failed force, scene end and module unload. ECD-to-native handoff retains the scene lease: restore the original viewport and dispatch native ResetZoom once, then suppress ECD automatic zoom on every native override frame before writing native zoom. Direct native zoom on a positively active ECD map acquires the same lease. Returning below100% rearms native preparation while retaining the original viewport. Preparation rearms cleanup before any callback, including a callback that mutates state and throws. A different scene only receives provider-global automatic-mode restoration, never an old viewport. Cleanup exceptions log once per pending episode. Unload makes one attempt; a failure there remains an explicit warning, not a promise of background retry after hooks are removed.

Native Release/Debug tests:2407 passed each with AKRON_NATIVE_EVEREST_TESTS=1. Callback fixtures cover capture-once, inactive/active reset order, scene mismatch, failed reset/automatic restore, idempotent release and re-force after partial cleanup. They do not replace real ECD/native acceptance.

Bounds: native >=100% without an active ECD lease retains its prior reset-to1 behavior. With an active ECD lease, release dispatches the map reset path. Arbitrary interrupted manual camera/cutscene state is not restored. ECD render-time focus replacement remains separate issue #227; this change does not claim to fix or verify cursor-focus fidelity on active ECD maps. Buffer allocation size is not asserted. No synthetic fault injection is presented as native gameplay verification.


## Active-ECD native handoff revision (QA109/QA110)
Review found that releasing the lease before installing native zoom allows active ECD automatic interpolation to alter the requested scale in the same update. QA109 exercised the original package on the same resting75% map: native150 was visibly magnified, but a fixed dirt platform measured142×95 pixels versus77×51 at rest and51×34 at half scale. This is not a total failure to magnify; screenshots alone do not identify the competing writer.

QA110 uses revised package `9fe77c968c2d2f475dd4fed8d3fce275556d7e4a58bd61c6a4304a8eac46f388`. Native controls perform50→150→50 while active, then release; direct150 activation/release; and a new150 activation followed by explicit Reset. The sign face measures89 pixels wide at rest,60 at half, and179 in first/later handoff and direct150 captures. The fully visible direct150 dirt platform is154×103 versus77×51 at rest. Settled releases and explicit Reset return the platform to77×51. Settings finish Off/100/Hold/Allow zoom out Off; native Save and Quit/Exit0.

Framing moves between initial and later150 captures despite stable pointer input: handoff sign approximately(-132,-207) pixels and direct activation(0,-124), with unchanged width. Scale/ownership acceptance does not establish focus or anchor fidelity; that remains issue #227. Immediate release/reset frames can interpolate before returning to map-resting scale.

CI compatibility: reference-only FNA strips Viewport constructor/getter bodies. Lease fixtures seed and read the retained value storage by reflection so distinct viewport and callback assertions execute in both CI and native-reference builds. No runtime camera code changed for this compatibility correction.
