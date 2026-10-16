# Escape input-counter verification

The IPS menu category counts the hardcoded Escape input alongside the configured Pause binding as one logical pause channel. Either source rising from neutral counts once. Holding either source, overlapping sources, or handing off without a neutral frame does not create a new pause edge. Confirm/cancel/action categories retain their existing semantics; this does not introduce global physical-key deduplication across categories.

## Native acceptance
Enable Labels Visible and Inputs per second. Show total, disable movement/actions and show max, enable Count menu. Release all keys and click Reset counter with the mouse. Dismiss/blur the popup and search before closing the overlay with K.

1. Escape opens native pause: total must become1. C resumes: total2.
2. Reset separately. Enter (configured Pause) then C must also produce1 then2.
3. A held Escape must count once, then a neutral release and later press may count again.
4. Restore all counting/label options and leave the game normally.

QA171 baseline showed Escape/C totals0→0→1, but matched Enter/C control0→1→2. Current IPS decays; read the persistent total (second number). Death/level entry reset totals, so keep this comparison death-free. Test inputs and onscreen results are the native acceptance; compiled wiring checks are additional regression protection. Duplicate physical bindings across unrelated menu channels are outside this narrow fix.
