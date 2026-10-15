# Auto Kill native pause verification

## Scope and correction

Issue #221: Engine.DeltaTime remains positive during native pause, so the attempt accumulator incorrectly counted pause-menu time. Auto Kill also ran before native Level.Update without a native suspension guard.

The update hook now samples native Paused/wasPaused/Frozen immediately before the native delegate. Only attempt accumulation is skipped if either the before or after state is suspended. Room-stat freeze accounting and room transitions still run. The public one-argument OnLevelUpdate signature is retained. Auto Kill now executes after the native delegate establishes pause state, but before attempt-time advancement. It checks native suspension before its recording/action path, covering timer and area execution. The deferred action also requires unchanged player, session, StartPos generation, current/next scene identity and an incomplete level. Positive finite scaled delta advances only for active updates.

Transition and cutscene accounting is unchanged; their existing execution guards remain. The deferred placement also skips Auto Kill on Akron freeze/countdown early returns; these paths have not all been live-certified. Area checks now use post-movement positions. No all-freeze-mode or all-area-interaction certification is claimed.

## Baseline

QA50 ran native Linux Celeste 1.4.0.0 / Everest 1.6580, fresh original City save, timer-only 120 s, normal assists, EVM master Off and MotionSmoothing disabled. Auto Kill Off control held native pause 126.189 s with Deaths 1 / Alive 9.588 unchanged. Auto Kill On, a fresh native Retry, and native pause yielded Deaths 3 / Alive 9.758. The next scored death appeared with native pause still open: Deaths 4 / Alive 0, 127.019 s after Retry and 115.289 s after the pause timestamp. Auto Kill was disabled after the first scored event; its deferred death animation played after resume.

Alive is a separate HUD clock, not the Auto Kill accumulator. The result establishes the paused-death consequence, not exact timer precision. The whole run saved five additional deaths, including three native Retry actions, the scored event, and one unassigned incidental death. Settings restored except backup timestamp and ordering of the same collapsed-section set. Exit 0. Twelve original screenshots and independent audit retained locally.

## Candidate

QA52 exact Release SHA-256: 93bf617012d5f6b1b2b95520a607f5a8f08201d4b36c3ffed4a0ba2c8be22b5d. A separate fresh original profile used the same timer-only 120 s configuration and native Retry boundary. Native pause held from 09:46:21.574 to 09:48:41.869 UTC (140.295 s), with Deaths 2 and Alive 17.119 unchanged. Resume did not kill immediately. Further active observations showed Alive 86.173 and 112.744 with Deaths 2, then the next observation showed Deaths 3 / Alive 8.432 after the first timed death and respawn. Auto Kill was disabled immediately after that observation, before another threshold. No gameplay movement or hazards were introduced during the scored hold and active wait.

This demonstrates that the long pause no longer spends the attempt, while active-time expiry still works. Screenshots bracket the event; they do not establish an exact frame or millisecond of firing. No direct runtime timer getter was used. Thirteen original screenshots, timestamps and final profiles/logs are retained locally. Settings restored except backup timestamp and ordering of the same collapsed-section set. Exit 0. Saved totals increased by four deaths (two native Retry actions, one scored timer event and one unassigned incidental death); jumps and dashes were unchanged. No zero-death or whole-profile-restoration claim.

## Tests and limits

Release and Debug each passed 2,286 tests. Added behavioral tests cover native suspension flags, thousands of excluded updates, both pause-boundary directions, remaining active delta, unchanged fired state and unrelated counters, invalid delta and missing session. Cecil routing checks supplement those behaviors. They are structural checks, not live area-mode, native frozen-state, arbitrary provider, or death-rearm certification. Native actions use ordinary keyboard/mouse controls, without runtime getters or injected state. Owner-authorized human-QA exception applies; no human testing is claimed.

## Review follow-up

The initial PR review identified the pause-opening boundary: a pre-native kill could run before native pause input was processed. The successor moves evaluation after both native update routes and rejects stale lifecycle ownership. Structural regression checks require the deferred action after native/input-scoped updates, before attempt statistics, and no Auto Kill call in the pre-native runtime feature path.

Successor Release SHA-256: d1309464aeafc3b5a58de6dc9f9ccb62e5481131e4ff6bbbfcab300155e5625f. Both suites pass 2,286 tests. QA54 rechecked the exact successor with the original 60 s timer. On an aged active attempt, Alive showed 106.165 s before native pause. Enabling the already-due timer while paused left Deaths 0 and that age unchanged; resume produced the Auto kill triggered at 60s toast and one death. Auto Kill was disabled after this controlled event.

A fresh native Retry then reset the next scored attempt. Native pause held from 10:13:38.870 to 10:15:06.307 UTC (87.437 s), with Deaths 2 / Alive 25.755 unchanged. Resume stayed alive; a later observation still showed Deaths 2 with about 56 s active age. The next observation showed Deaths 3 after normal active expiry, and Auto Kill was disabled before another threshold. No movement or environmental hazard was introduced during these waits. This supports due-while-paused protection, fresh pause exclusion, resume, active expiry and re-arming across the controlled events. It does not establish exact same-frame pause-input timing, area-mode behavior, or millisecond threshold precision.

Sixteen original screenshots, timestamp receipts and final profiles/logs are retained locally. Settings restored except backup timestamp and ordering of the same collapsed-section set. Exit 0 and retained window-absence receipt. Saved totals increased by four deaths (two controlled timer deaths, one native Retry and one unassigned incidental death); jumps and dashes were unchanged. QA52 remains historical evidence for the earlier 93bf artifact.
