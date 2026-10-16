# Air-jump input consumption verification

Issue #51: the grounded jump consumed one of the configured extra jumps.

## Correction

Apply the extension directly after native Player.Update, within the surrounding gameplay-input scope. A thread-local, nested-safe context observes ConsumeBuffer only for Input.Jump while native update is active. Native ground/coyote/wall/climb/super jump paths consume that buffer even though the raw Pressed edge remains true. Such handled presses must not spend the air-jump allowance. Current grace still preserves an available vanilla jump, without vetoing a newly expired grace tick merely because grace was positive before update.

Close the native context before Akron invokes its own jump; suppress an enclosing nested context during that invocation and restore it in finally. Reset the quota on pre/post ground, player identity change, confirmed death, feature-off, noclip and unload. Check player/scene/session identity before applying the extension.

## Native baseline and acceptance

Linux Celeste 1.4.0.0 / Everest 1.6580.0-azure-082e2. Input-only TAS launched through the real configured-file UI, with no state injection. City room1 left shelf, no landing between the three presses. The unchanged 129-frame input is: 60 neutral, 1 Jump, 3 neutral, 1 Jump, 3 neutral, 1 Jump, 60 neutral. Presses occur at61,65,69.

QA142 baseline at stacked head91fbbc444b4927a1de497c4ca3521cb55097ac25: Extra3 / Extra1 / Extra2 / Off produced 3 / 1 / 2 / 1 upward impulses.

QA144 corrected Release package969620e4f90a89b68ac9b6889bf330352190fd9d74ad4603ee32dedb8a9eb0e3: the identical matrix produced 3 / 2 / 3 / 1. Extra1 directly captured logical Speed Y -105 at61 and65, then -45 at69 without a third reset. Extra2 captured61/65; its third reset is supported across a missing69 by68 -60 to70 -90. Extra3 has one-frame gaps at each press, with adjacent logical Speed and script progression supporting each reset. Off has only its initial jump.

These are native recorded trajectories and displayed input/Speed, not endpoint-only assertions. Capture gaps are explicitly qualified; interpolated Vel is not used as logical Speed. No all-platform, exhaustive wall/coyote/dash compatibility or frame-perfect acquisition claim. A separate 205-frame input-only extension after normal Retry directly captured a second grounded jump and one replenished extra jump after landing, with the next press denied. Held Jump showed a decreasing vanilla jump countdown rather than newly triggered impulses. This does not prove same-instance savestate restoration. Raw media and runtime assets remain private.

Release and Debug each pass 2,501 tests against native Everest references. Predicate and source-boundary tests complement, rather than replace, the live acceptance above.
