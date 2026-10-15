# Custom Trail opacity verification (#248)

## Defect and change

The forced custom trail passed its opacity fraction as the third argument of `TrailManager.Add`. That argument is duration, not alpha: even zero-duration snapshots could briefly draw opaque cyan remnants. The new emitter multiplies the complete premultiplied color by the clamped opacity, uses a fixed 0.45-second fade duration, and skips new custom snapshots at zero. Non-custom Always mode retains its native color and previous 0.45-second duration.

This deliberately changes the previous accidental lifetime behavior: 100% no longer means a one-second trail. Existing snapshots can finish fading after an opacity change. Independent vanilla dash trails are outside this setting.

## Live native comparison, 2026-10-15

Both runs used native Linux Celeste 1.4.0.0 / Everest 1.6580.0, the same original isolated profile and Forsaken City room 10's safe lower floor. The room was reached through Akron's native room controls. All comparison movements were ordinary left/right walking, with no dash, injected runtime state, synthetic lifecycle invocation, or assist mode. Fixed color #44B7FF, Pulse off, cut rate 1, and Vanilla visibility were used except for the explicit Always control.

- QA65 baseline package SHA-256: `ba04e0879b5a5db2a933408f2a2dd1c047c26cff3715a2843346f17e7b8909a9`.
  At 100% walking produced cyan trails. At 0% a small bright cyan remnant remained during walking. Disabling Custom Trail removed it.
- QA66 candidate package SHA-256: `f3a1c9fbd7a5d0b94529b2da0f0447e27f263de369a095f14acd14bdfa881544`.
  100% and 50% produced visible cyan trails; the 50% capture was qualitatively dimmer. At 0%, rightward and leftward walks showed no cyan remnant. Re-enabling 100% restored the cyan trail. With Custom Trail off, Always mode still produced a native red trail while the stored custom opacity was zero.
- Candidate settings were restored through the UI to Custom Trail off, opacity 45, Vanilla visibility. Native Save and Quit followed by Exit completed successfully at 15:17:57 UTC. The saved Akron settings match the original except for the automatic backup timestamp.

The 12 original candidate captures cover configuration, both zero-opacity walks, the non-custom control, re-enable, and restoration. Captures are not phase-calibrated alpha measurements or lifetime measurements; no exact pixel-alpha percentage or native fade-duration timing is claimed. Fixed-color walking is the live acceptance scope. Pulse, rainbow, synchronized colors, other maps, and platform-specific rendering were not separately live-tested in this run.

## Automated checks

Release and Debug each pass 2,344 tests. New scalar cases check clamping and non-custom preservation, while an emitted-IL regression checks the color multiply, fixed duration argument, and branch skipping emission. These structural tests do not execute native rendering or replace the live comparison.
