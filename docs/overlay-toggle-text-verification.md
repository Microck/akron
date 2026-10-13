# Overlay toggle while typing (#238)

The keyboard toggle now respects a visible overlay's ImGui text ownership.
Keyboard state still advances while the editor owns input, preventing a held
toggle from being replayed on blur. Controller toggles keep their existing path.

The Level export-field comparison below passed live. Compiled-wiring checks
support the guard and state-update ordering; they do not establish text
delivery, focus timing, or controller behavior by themselves.

## Native comparison

Use an isolated profile and a verified unused printable overlay binding such
as K. Preserve the original settings, exact package identities and screenshots.

1. On unmodified main, focus Room Capture's Export path with a visible caret.
   Press only K. Record whether the overlay closes instead of entering the key.
2. On the candidate with matching settings, repeat the same focus and single-key
   action. Verify the overlay stays open and the actual text gains K.
3. Type K again after release. Then leave the editor while K is held: the
   overlay must not close just because focus ended. Release K, press it anew,
   and confirm ordinary toggle behavior returns.
4. Repeat in the main-menu overlay if its editor is available, and verify that
   non-text controls do not unnecessarily trap the toggle. Controller behavior
   requires actual controller input; do not infer it from the keyboard test.
5. Restore settings and exit normally. Do not export or overwrite capture files.

Report any unperformed leg separately. These are AI-assisted changes; no human
review or human testing has been claimed.

## Native comparison result, 2026-10-12

An AI-operated Celeste 1.4.0.0-fna / Everest 6580-azure-082e2-stable /
.NET 8.0.24 comparison used exactly matching sets of twelve starting save and
settings files. On unmodified main `2cee797`, pressing K once with a visible
caret at the end of Room Capture's Export path closed the overlay. The
candidate instead appended `k` and retained the editor. A released second K
appended another character; two Backspaces visibly restored the original path.
After clicking outside the editor, a fresh K closed the overlay normally.

Final saved export paths equal the original. Both processes exited normally;
final Akron settings differ only by automatic backup timestamp. An unexplained
death-counter increment is retained in the evidence, with no claim of
death-free operation or preservation of those counters.

- Comparison DLL SHA-256:
  `e29186cda4c0e2c9eb34a270812781f8cf03c167078518a13e50005eeedd6da2`.
- Candidate DLL SHA-256:
  `62e3637f3d03ba0ad8ce3b468498b1ad000f055ebc96d777e76e81ac29c3a8e9`.
  This candidate contains only #238 on the same main base, not other proposed
  fixes with the same beta84 manifest label.
- Evidence: `Akron-238-Native-Text-Toggle-Comparison-20261012.zip`, SHA-256
  `98fbbf47ef81706315e6e6d0e837e7d06312d0b6ddd1b1fbc05ece172d4244d7`.

This qualifies the Level export field and released-key blur recovery. Holding
K through blur, controllers, arbitrary editors and main-menu text editing
remain untested; Room Capture's main-menu arrow was disabled. No capture or
export was performed. This is not human review or full input-system acceptance.

## Review follow-up: diagnostics ownership

The Level diagnostics early return now refreshes the overlay-toggle keyboard snapshot, matching the global-scene route. A toggle key held while Cancel or Send closes diagnostics is already recorded as held rather than becoming a synthetic press afterward. Two compiled-wiring regressions cover both Level and global guards; the Level case fails before the correction. Reference-only FNA cannot execute live keyboard-state reads, so these managed checks are not native held-key evidence. Fresh live diagnostics-close verification remains pending.

The shared diagnostics Close path also refreshes keyboard state before releasing the dialog owner. This covers a key press seen by the later ImGui render poll after the update guard already sampled input. A third before-failing compiled-wiring regression verifies that close-boundary refresh precedes owner release. Actual native held-key delivery across Cancel remains a separate pending QA case.

Ending the text session now clears published text ownership immediately. A session revision also prevents a dialog closed during drawLayout from republishing stale ImGui capture flags or re-subscribing text input at the end of that same frame. Two managed state regressions fail before this correction and cover immediate release, stale-frame rejection and next-frame ownership recovery. These do not exercise native ImGui focus or key dispatch; fresh post-Cancel toggle verification remains pending.
