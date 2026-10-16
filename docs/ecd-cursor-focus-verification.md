# Active ECD cursor focus verification (#227)

## Change and rendering contract

Active Extended Camera Dynamics replaces Level.ZoomFocusPoint during BeforeRender even when automatic zoom is suspended. For magnified Cursor Zoom, Akron now temporarily offsets the actual Camera during Engine.RenderCore (native BeforeRender/Render/AfterRender and world HUD), then restores the captured camera before interactive ImGui actions and on exceptions. The next update sees the unshifted camera, preventing accumulation. Nested rendering does not apply the offset twice.

At the center pointer, authored ECD framing is preserved. Relative pointer travel uses clamped focus minus (160,90), matching native focus travel at the requested scale. It deliberately does not replace ECD's authored center with a vanilla unzoomed origin. Existing zoom-out and scene-lease release behavior remain intact.

A narrowly gated ECD projection/inverse uses the same logical camera for HUD/point tools outside the physical render scope. Single screenshots use the scope; screenshot scans exclude it and keep their own tile camera. This does not globally unhook ECD or change other zoom providers.

## Package and tests

Release package SHA256: `a7d57d3e049ff2268af3dc3d0751c26a72655bb45becc81e692a88c8b63aebdc`.

2,452 native Release and Debug tests pass. Tests cover focus travel magnitude, center/unity behavior, finite-scale guards, projection round trips, repeated render restoration, exceptions, idempotent early disposal and source-level scanner/nesting/UI-boundary wiring. These do not constitute native injected-fault, nested-render or every scene-transition acceptance. The first test compile needed an explicit Action cast for xUnit's unconditional-throw overload; the retained compile failure was corrected before testing.

## Native baseline and candidate

Actual keyboard/mouse operation on Linux Celeste/Everest, installed ECD1.2.0. A private copied-geometry fixture enables ECD through metadata with authored75% resting zoom; no manual runtime hook toggle or injected camera state was used. Raw assets/captures are not published.

QA121: native Hooks active status, stationary player,150% Toggle. Pointer center(682,512), upper-right(1100,300), lower-left(260,740), center. Sign, lamp and ledge showed zero translation across settled captures, at unchanged scale. Release restored authored75%.

QA122 control: ordinary City room1, native Hooks inactive, same150% settings and pointer positions. Tower translation was approximately(-341,+191) and(+341,-193) desktop pixels, with center return(0,0), unchanged scale. This qualifies the input protocol independently of room boundaries.

QA124 candidate: same active-ECD fixture now responds approximately(-341,+191) and(+341,-192), returning to center without accumulating. Native active150→50→100→150→50 and release show the expected distinct scales and restoration. A recorded Hold test uses actual LeftAlt held2500ms at each of the upper-right and lower-left positions; enlarged views respond and release. The video ends before full interpolation settles; separate screenshot18 verifies settled restoration. All tested controls were restored to Cursor ZoomOff,100%,Hold,Allow zoom outOff,Reset on deactivateOff, and the process exited normally.

QA125 repeats the ordinary City hooks-inactive150% Toggle control on the candidate package; center and distant-pointer framing remain responsive.

QA123 is explicitly invalid and excluded: a storage-deduplicated entry-assembly symlink redirected assembly-relative mod lookup to another runtime folder, making the custom map unavailable. It was closed through the native error menu. QA124 materialized top-level assemblies, verified correct mod loading and used the unchanged candidate package. Earlier baseline sessions ran before deduplication.

## Boundaries

This is bounded native camera behavior, not universal ECD/map compatibility. Initial interpolation is distinct from steady pointer response. No exact timing claim is made from dropped-frame video. Scanner exclusion, projection inverses and exceptional disposal have automated coverage; no claim of fresh native full-map stitching, every HUD/point tool, injected rendering failure or all scene-transition combinations is made.

## Review correction: current gameplay pointer

Review found that Click Teleport captures its target before UpdateCursorZoom. Its inverse now predicts the current clamped mouse focus instead of using the previous frame's focus. General editor/placement inversion intentionally keeps the actual cached rendered focus because those modes can freeze the update. The final package is `f657eff85bbabd1bcabb61a3e1d8f94e8a029213492235528012142fa8a69fcb`; native Release and Debug each pass 2,453 tests. A fresh-pointer regression and source wiring checks cover that distinction.

QA127 could not qualify teleport input after a live Cursor binding change: new binding wrappers were uninitialized. This separate existing bug is tracked in #333. QA128 restarted with persisted V, activated150% zoom, moved from upper-right to a V+mouse click at(600,650), and visibly produced “Teleported to cursor.” This verifies native dispatch on the final package; the player is not clearly separable afterward, so it is not pixel-perfect destination or same-engine-frame timing acceptance. The recorded control and unit/order evidence must not be conflated. A native Retry cleaned up the disposable room; teleport was disabled and its default LeftAlt binding restored, zoom disabled/reset100/Hold.
