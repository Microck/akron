# StartPos cancellation input verification

Fixes #236. StartPos placement owns its raw right-button cancellation. A release
gate now suppresses that right-button state before forwarding mouse events to
ImGui, until the physical button is observed released. Other buttons are unchanged.

The gate is armed before every ImGui frame while placement is active, including
empty cleanup frames, and again before cancellation restores the main overlay.
This prevents a right-down sampled between Update and Render from entering ImGui's
trickled event queue and appearing later over a restored row. Earlier candidate
packages that omitted active-editor or cleanup-frame protection were retained
but never accepted as final native candidates.

## Native baseline and candidate

QA133 on the prior package first qualified ordinary right-click at the Air Dashes
row. During Place StartPos, right-clicking that same position closed placement
and opened the underlying binding context. Escape cancellation and right-click in
empty space restored the overlay without that context. No StartPos was placed
and no binding was changed.

QA134 used the same initial profile with the candidate package. Qualified
right-click cancellation over the row restored the overlay without a binding
context. Escape and empty-space controls also worked. A repeated row-cancellation
trial again restored a clean overlay; a subsequent separate right-click at the
same position opened the ordinary binding context, confirming recovery.

Short physical clicks were intermittently unobserved in this polling-based native
input route. The first candidate cancellation attempt left placement active and
was not scored as a successful cancellation. Some recovery attempts also showed
no context; these are retained as unqualified rather than claimed as passes.
The final cancellation/fresh-click pair is the qualified recovery evidence.

Both native Release and Debug runs passed all 2,471 tests. Pure gate tests cover
ordinary input, continued holds, release, fresh press, and repeated suppression.
Source-wiring tests require protection before both cleanup and placement-editor
rendering, and before cancellation restores the overlay. These are not an
instrumented ImGui queue or hardware timing test.

Release package SHA-256:
`2b7e74ed293003216e209e2c3d547be62219881a74cf4dc4f0b8a293ebe42141`.

## Limits

Native acceptance covers discrete clicks; extended held-right-button behavior and
precise wheel/event-queue race timing are supported by source review and pure tests,
not native frame-timing claims. A narrow residual case remains: if the button is
released while no renderer frames occur, that release is unobserved until a later
frame and a fresh press may be swallowed until release is observed. This is not a
permanent input lock. No unintended setting mutation or all-input-path acceptance
is claimed. Raw screenshots, profiles, logs, and package receipts remain private.
