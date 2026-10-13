# Skip Postcards regression verification

Issue: https://github.com/Microck/Akron/issues/279

The chapter-entry hook must find `Postcard` in the entering scene's entity list.
`Postcard` is not a tracked entity; querying `Tracker.GetEntity<Postcard>()` can
throw for the missing type key before held-confirm handling begins.

## Automated scope

Run `dotnet test tests/akron-tests.csproj --filter LevelEnterSkipTests`.
The tests cover an absent/empty entity list, unrelated entities, an untracked
postcard, and independent successive lookups. They intentionally avoid content
and audio initialization. They do not prove native chapter entry or hold timing.

## Native acceptance checklist

Use an isolated save and record the exact game, Everest, Akron and provider
versions. Preserve the unchanged comparison installation and settings.

1. With Skip Postcards Off and Skip Intro Off, enter Forsaken City from the Start
   checkpoint. Confirm the postcard appears and ordinary dismissal still works.
2. Enable Skip Postcards while leaving Skip Intro Off. Enter the same checkpoint
   without holding Confirm. Confirm there is no critical-error screen and the
   postcard remains available for normal dismissal.
3. Repeat entry and hold Confirm continuously. Confirm the postcard is skipped
   after the configured 0.73-second hold threshold and playable Level is reached.
   Use game-side hold-time evidence; external input-call duration is not latency.
4. Release Confirm before the threshold, then hold again. Confirm the elapsed
   hold resets rather than accumulating across the release.
5. Exercise a chapter entry without a postcard, and separately the existing
   B-side Skip Intro path. Absence of a postcard must not throw or enable an
   unintended skip. Verify policy refusal still prevents skipping.
6. Restore both settings Off, repeat the Off control, and inspect the error log.

Capture the relevant native screen and state before and after each transition.
Keep partial/blocked steps explicit. Do not call this fix runtime-verified until
these checks have actually run; unit tests and a successful build are not a
substitute for native acceptance.

## Native crash comparison, 2026-10-12

An AI-operated live Celeste/Everest comparison reproduced the reported crash
on unmodified main `2cee797dd3d7f07abed74bd09fd6a16ebf4e4ec2`. With Skip
Postcards On and Skip Intro Off, chapter entry displayed the critical-error
screen with `KeyNotFoundException` for `Celeste.Postcard`, through
`Tracker.GetEntity<T>` and `AkronModule.UpdateLevelEnterSkip`.

The candidate displayed the postcard instead and reached gameplay after
ordinary Confirm dismissal. Its installation receipt records byte-identical
starting save/settings files; a second frozen candidate prelaunch tree was not
retained for independent comparison.
A separate candidate both-skips-Off control also displayed the postcard,
reached gameplay, saved, and exited normally. A B-side Skip Intro Off entry
reached gameplay without the reported crash.

- Runtime: supplied Celeste 1.4.0.0-fna content, Everest
  `6580-azure-082e2-stable`, .NET 8.0.24; no providers beyond Akron.
- Comparison DLL SHA-256:
  `e29186cda4c0e2c9eb34a270812781f8cf03c167078518a13e50005eeedd6da2`.
- Candidate DLL SHA-256:
  `ea61d1ebe5e502ac7a818de9b94e4745bf6c6da93e646b8b07619234368d7a9a`.
  This private candidate also contains proposed fixes for #278 and #274.
- Both packages retain the beta84 manifest label; neither is the historical
  published beta84 package.
- Disposable fixtures used native chapter unlocking and a temporary overlay
  binding. Public modsettings were changed only while the game was closed,
  then visually checked. This does not verify mouse-toggle routing.
- The isolated runtime required clearing FMOD's executable-stack request in
  its copied library. Pristine bytes were retained; OS settings were unchanged.

This establishes the narrow crash regression, not full skip-behavior acceptance
or human testing. Exact 0.73-second timing, early-release/reset accumulation,
policy refusal, and a qualified absent-postcard transition
remain pending. A held-Confirm entry reached gameplay, but its exact live mode
and matched postcard premise were not established; external input-call duration
and stale saved XML are not sufficient evidence for those assertions.

The final Off control's explicit Climb selection and native Save and Quit
qualified `Celeste/1-ForsakenCity`, Normal, room `1`. Supplied map files matched
the pristine archive; canonical distribution geometry was not independently
verified. Original screenshots, logs, and fixture receipts are preserved in
`Akron-279-Native-Comparison-Qualified-Scope-20261012.zip`.

A supplemental, uninstrumented candidate run selected City B-SIDE explicitly
with both Skip Intro and Skip Postcards visibly On. It reached gameplay,
completed Save and Quit, and exited normally. Fresh saved XML qualified
`Celeste/1-ForsakenCity`, BSide, room `00`, InArea true, with one recorded death.
This adds the successful B-side / Skip Intro On entry control, not evidence of
intro suppression, postcard absence, skip timing, or reset behavior. Its raw
screenshots, logs, save snapshots, and manifest are preserved separately in
`Akron-279-Native-BSide-SkipIntro-On-Control-20261012.zip` (SHA-256
`72e2de109dc1f56ec4aa3c1d9554e4c8e95b2902853077beb93e6c69dded47ab`).
