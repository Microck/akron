# Place StartPos camera movement (#237)

## Fresh native baseline
QA155 uses the current recorder-fix package and a separate copy of the closed Prologue save, room0 (448 pixels wide). Native UI enables ordinary Free Camera at Speed240, Freeze gameplayOn, Mouse controlOff. From the left clamp, a200ms Right hold shifts three fixed landmarks left by approximately205 screen pixels. The same framing and200ms input in Place StartPos moves those landmarks0 pixels. Escape cancellation followed by the same control moves them approximately222 pixels. The differing control distances reflect real input delivery; the claimed contrast is movement/none/movement, not exact simulation timing. HUD frame8820 stays frozen throughout.

No world click, Set or Load was performed. Persistent StartPos storage is byte-identical; transient session-cache differences are separately documented. Free Camera is disabled before native Save/Quit/Exit0.

## Correction
The placement update advances only free-camera movement, after cancellation and panel ownership checks and before mouse-to-world conversion. It runs only when the Freeze-policy ownership early-return would skip normal runtime features, avoiding double movement on separately restricted maps. ImGui keyboard/text/mouse ownership and the placement panel suppress movement while editing controls. Shared movement retains ordinary policy, input normalization, speed, click-teleport suppression and room clamping.

The placement-only path does not acquire or restore a player-state lock: gameplay is already frozen and camera movement must not force StDummy into a placement snapshot. Ordinary Free Camera retains its existing player-control locking. Prior freeze/free-camera settings remain restored by the existing cancellation path.

## Verification status
Automated source guards supplement the fresh native baseline. Corrected-package native acceptance is pending, including ordinary/placement/recovery panning, panel input ownership, and placement entry from Free CameraOff without player-state mutation. No acceptance is inferred from automated checks alone.
