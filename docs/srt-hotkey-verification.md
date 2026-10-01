# Speedrun Tool hotkey verification

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
