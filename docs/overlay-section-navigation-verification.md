# Overlay section reachability (#253)

## Native baseline
QA162 uses current PR344 code with empty Search and the original section preferences. At1364x1024 fullscreen and150% UI scale, Interface remains at the bottom and its rows can be scrolled locally; this is not complete Interface loss. Native display Options changed to960x540 (Window Scale3x). At150%, the three-column layout places Interface entirely below the client. Tested local/edge wheel attempts do not reveal it. Collapsing Bypass, Keybinds, Player and Sound exposes Interface and confirms1.500. Scale1.000, fullscreen4x and all section preferences were restored through the native UI; the retained settings diff confirms restoration. Save/Quit/Exit0 completed.

## Correction
The ImGui renderer plans stable unscrolled section positions and row capacities, then applies a separate vertical section offset. An overflow-only Top/Up/Down bar remains in a reserved bottom strip. Actual section windows are clipped above that strip, so window focus order cannot cover the controls. Per-section wheel scrolling remains separate. Collapsed headers remain reachable at the bottom. Resize/filter resets and content-shrink clamping keep the offset valid; transient UI reset returns to the top. Options ownership is reconciled when its submitted anchor disappears, and outer navigation is disabled while a popup/modal owns input.

This corrects the normal ImGui menu. The SpriteBatch fallback retains its existing layout and does not reset or consume the ImGui offset.

## Verification
Automated checks cover scroll bounds, collapsed-header reachability, planned row capacity, reserved navigation bounds, popup ownership and wheel separation. Native corrected acceptance is pending: three-column150% access to Interface without collapsing unrelated sections, local scrolling, popup ownership/no click-through, search/clear, collapse while scrolled, resize/fullscreen recovery and restored preferences. Automated checks are not a substitute for these live checks.
