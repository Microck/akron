# Speedrun Tool hotkey verification

## Focused normal editor regression (#274)

The proposed #274 follow-up extends text ownership to all focused ImGui text
and numeric editors, including Control Display's layout editor. It does not
reserve keyboard input simply because the overlay is open or hovered, and it
does not change SRT bindings or the Diagnostics Unicode input route.

The Release build and 2,046-test suite pass, including a new compiled-code wiring
check. The existing startup and focus-release tests also pass. These checks do
not prove SRT event suppression in the game. The bounded native comparison below
now covers the numeric-editor regression; broader focus lifecycle acceptance
remains pending. The September 30 evidence belongs to the earlier fix.

Verify the following on an isolated installation with the exact versions recorded:

- With SRT's original Tab binding retained, focus Control Display's numeric Width
  editor and press native Tab once. Height should receive editor focus and the
  SRT slot interface should stay closed.
- Click away, close the editor, and confirm SRT's Tab action works again.
- Check repeated focus/blur and overlay close/reopen, game-window focus loss,
  Diagnostics Unicode typing and its release paths, and neutral overlay hovering.
- Compare saved SRT bindings before/after; do not rewrite them to hide overlap.

### Native numeric-editor comparison, 2026-10-12

AI-operated live verification used restored Celeste 1.4.0.0-fna, Everest
1.6580.0, and official Speedrun Tool 3.27.21 on the cloud desktop. The unchanged
comparison was main `2cee797dd3d7f07abed74bd09fd6a16ebf4e4ec2`; the private
candidate also contained proposed fixes for #279 and #278. Their DLL hashes are
respectively `e29186cda4c0e2c9eb34a270812781f8cf03c167078518a13e50005eeedd6da2`
and `ea61d1ebe5e502ac7a818de9b94e4745bf6c6da93e646b8b07619234368d7a9a`.
Neither package's unchanged beta84 label identifies the historical release.

Both scored processes had identical 11-file starting Saves trees, unchanged SRT
bindings, and no observer installed. K opened Akron; SRT retained its original
Tab binding. Actual Control Display Width accepted typed `36` to `37`, with a
visible caret, before Tab was pressed.

- Unmodified main: Tab selected Height but also opened SRT's slot grid beneath
  Akron. Native screenshots preserve both the active editor and the conflict.
- Candidate: Tab selected Height while SRT's grid remained closed.
- Candidate release control: Shift+Tab returned to Width, which was restored to
  `36`. Clicking away closed the editor while keeping Akron open; Tab then
  opened SRT normally. Suppression was not blanket overlay-open blocking.

Both processes saved and exited normally with code 0. Post-exit settings were
preserved before original settings were restored on the closed disposable
profiles. Final provider YAML and bindings were identical.

The input route required a tiny supported native drag within the target. A
separate read-only diagnostic observed that instantaneous clicks missed the
polled Pressed state, while the tiny drag delivered it. That observer was removed
before both scored runs. A failed windowed MIT-SHM preparation is retained
separately and is not attributed to Akron. These diagnostic runs are not fix
acceptance evidence.

This establishes one ordinary numeric Width-to-Height transfer and release,
not every editor, repeated focus stress, window-focus loss, Diagnostics Unicode,
controller hotkeys, minus-sign input, or human testing. Those checks remain open.
Original screenshots, logs and identities are preserved in
`Akron-274-Native-Numeric-Focus-Comparison-20261012.zip`.

## Earlier diagnostics-only fix

The fix preserves Speedrun Tool's bindings and registers Akron's diagnostics text
handler only while an editable field has focus. Closing diagnostics, changing
scenes, resetting overlay input, or a renderer failure releases the handler.

## Local verification

- Release build: no warnings or errors.
- Full test suite: 1,982 passed, none skipped.
- Overlay tests cover startup subscription and queued Unicode lifetime.
- Repository format check passed.
- The packaged DLL matches the build, and the archive passes integrity checks.

Reference-only Celeste dependencies cannot prove native event dispatch. The
following checks ran in the actual game on the Linux Mint test machine.

## In-game verification

Tested September 30, 2026 on Linux Mint 22.3, X11, through the jc141 Wine launch
script, with Celeste 1.4.0.0, Everest 6418 stable, and installed SRT 3.27.20.
A temporary observer counted calls through SRT's original hotkey dispatcher and
then executed each original action. It also recorded the actual Everest text
event subscribers. Controller input used an SDL virtual controller recognized
by the game's normal GamePad API, not a physical controller.

The released beta 83 baseline reproduced both reports:

- Akron remained subscribed to TextInput.OnInput with diagnostics closed. SRT's
  actual subscriber predicate rejected hotkeys; tested keyboard and controller
  inputs produced no SRT callbacks.
- Entering Forsaken City removed Tab from SRT's save/load UI binding. The log
  explicitly reported Akron removing it.

The fixed archive passed these checks in the same installation:

- Startup and normal gameplay had no Akron text subscriber. Tab remained bound
  after level entry, clean exit, and a fresh launch. Keyboard save/load/clear
  and controller save worked again in a level after restarting.
- Keyboard F7/F8/F4 invoked save/load/clear. SRT created a savestate, loaded it,
  and cleared it. SDL controller X/B/Y reached those same original actions.
  Controller RightShoulder changed the room timer.
- A focused diagnostics title field registered exactly one Akron subscriber.
  Typed ASCII and translated accented/Greek text reached the field. Keyboard
  save and controller room-timer callbacks stayed blocked while it had focus.
- Escape, the Cancel button, and controller cancel released the subscriber.
  SRT actions resumed afterward. Reopened fields contained no stale text.
- Blank-report confirmation released the subscriber. Go back and refocusing
  the title registered it again.
- The upload/result screen had no Akron subscriber. Submission used a closed
  HTTPS loopback endpoint and reached the failure result without contacting the
  live support service. Returning to the form restored text focus normally.
- Changing to a new level with the field focused released the subscriber before
  SRT's next hotkey check. Hiding the overlay through its API also released it.

The original Akron archive and all 250 backed-up save/settings files were restored
byte for byte after verification. The observer mod, its cache, and its request
directory were removed. Player progress used a transient debug slot.

## Limits and recovery

These checks cover the tested build and environment. They do not establish
compatibility with every mod combination, SRT version, or physical controller.
The existing native text pipeline rendered a supplementary emoji as a replacement
character; accented and Greek characters arrived once. The input-lifetime fix
does not change that existing supplementary-character limitation.

If an older Akron version saved an empty SRT Tab binding, reassign it explicitly
in SRT's options. The fix preserves chosen bindings and does not infer old ones.
If both UI toggles use Tab, choose separate bindings manually.
