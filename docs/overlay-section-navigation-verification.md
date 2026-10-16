# Overlay section reachability (#253)

## Native baseline
QA162 uses current PR344 code with empty Search and the original section preferences. At1364x1024 fullscreen and150% UI scale, Interface remains at the bottom and its rows can be scrolled locally; this is not complete Interface loss. Native display Options changed to960x540 (Window Scale3x). At150%, the three-column layout places Interface entirely below the client. Tested local/edge wheel attempts do not reveal it. Collapsing Bypass, Keybinds, Player and Sound exposes Interface and confirms1.500. Scale1.000, fullscreen4x and all section preferences were restored through the native UI; the retained settings diff confirms restoration. Save/Quit/Exit0 completed.

## Correction
The ImGui renderer plans stable unscrolled section positions and row capacities, then applies a separate vertical section offset. An overflow-only Top/Up/Down bar remains in a reserved bottom strip. Actual section windows are clipped above that strip, so window focus order cannot cover the controls. Per-section wheel scrolling remains separate. Collapsed headers remain reachable at the bottom. Resize resets and content-shrink clamping keep the offset valid; transient UI reset returns to the top. Options ownership is reconciled when its submitted anchor disappears, and outer navigation is disabled while a popup/modal owns input.

This corrects the normal ImGui menu. The SpriteBatch fallback retains its existing layout and does not reset or consume the ImGui offset.

## Verification
Automated checks cover scroll bounds, collapsed-header reachability, planned row capacity, reserved navigation bounds, popup ownership and wheel separation. Native corrected acceptance is pending: three-column150% access to Interface without collapsing unrelated sections, local scrolling, popup ownership/no click-through, search/clear, collapse while scrolled, resize/fullscreen recovery and restored preferences. Automated checks are not a substitute for these live checks.


## First candidate and review revision
QA163 confirmed native section access at960x540/150%, local Interface scrolling, last-section collapse/reopen, popup dismissal without navigation click-through, and restoration to100% without collapsing unrelated sections. It also exposed a search-focus regression: typing from the bottom Interface moved the outer view to the top and hid the input. This run is incomplete acceptance, not a final pass.

The revision retains/clamps outer position while filtering and reveals pending search/programmatic options targets before clipping. A pending search row is scrolled into the local viewport before Begin; focus requests remain until submission. Navigation explicitly relinquishes search focus. Missing/collapsed options targets and cancellation clear pending ownership.

Review also identified that changing clipped panel heights can clamp local scroll. Per-section desired scroll is now independent of ImGui's observed geometry-clamped position. Height/max changes restore the desired value; genuine local scrolling replaces it, including boundary wheel gestures. Restoration defers during pointer scrolling/dragging. Tests cover geometry round-trips, unchanged clamped frames, user changes, boundary gestures and reveal precedence. Revised native acceptance remains pending.
