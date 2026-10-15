# Safe Mode file totals verification (#224)

## Scope and implementation

Safe Mode previously captured `Jumps` on area-mode stats, which do not own the native file jump counter, and restored area deaths without restoring file-wide deaths. The fix protects `SaveData.TotalJumps` and `SaveData.TotalDeaths`, retains area-mode death and best-time protection, captures each enabled guard independently, and resets snapshots when save/area identities change.

This retains the existing level-update restoration timing. It does not promise atomic protection against every same-frame save, exit, or third-party mutation.

## Automated checks

Release and Debug suites each passed 2,233 tests, including 12 new cases for file totals, independent guards, activation and reactivation, save/area identity replacement, missing area stats, missing save, all guards disabled, and best-run fields. Existing CL0006 warnings remain. Synthetic-object tests supplement native checks; they do not replace them.

## Native baseline (QA36)

Celeste 1.4.0.0 / Everest 1.6580, native Linux desktop, 1364×1024 window. Exact baseline package SHA-256: `964dfdf8b110dbaaa80d10e7bed47005db22e7488bdd3a66c4247083468ccc81`.

Used the disposable Prologue save and native overlay controls. Jump Stats was explicitly File mode; Death Stats used `$T` with Always visibility. No runtime console or injected state was used.

- Safe Mode Off: ordinary jump changed file jumps 0→1; native Retry changed total deaths 3→4.
- Safe Mode On, Freeze deaths and Freeze jumps checked: ordinary jump still changed 1→2; Retry still changed deaths 4→5. Both failures persisted after settling.
- Master Off controls then reached 6 deaths / 3 jumps.
- Settings restored except the expected backup timestamp; native normal exit returned code 0.
- Final saved deaths were 7 while the last HUD showed 6. This extra saved death remains unexplained and is not attributed to a specific cause. Saved jumps were 3. Map aggregate tokens are not treated as raw area-mode death evidence.

## Native candidate (QA37)

Exact Release package SHA-256: `e1cfb9740199a81616b6e44ed51d3c24c4742c6c170ea2b929443b3eafe20642`. Same runtime and viewport, using QA36's retained profile. Source-root `Akron.zip` was subsequently overwritten by Debug output; it is not the tested Release artifact.

- Off controls: starting file totals 7 deaths / 3 jumps; ordinary jump reached 4 jumps; native Retry reached 8 deaths.
- Both guards On: ordinary jump retained 4 jumps and native Retry retained 8 deaths after full respawn.
- Only jump guard Off: an ordinary jump reached 5. Reenabling it retained 5 without rollback, and another ordinary jump still settled at 5.
- Only death guard Off: native Retry reached 9 deaths. Reenabling it retained 9 without rollback, and another Retry still settled at 9.
- Safe Mode master Off with guard preferences still On: ordinary jump reached 6 and native Retry reached 10 deaths.
- Restored all changed preferences through native UI before normal Save and Quit.

Unedited screenshots and launch/profile receipts are retained separately. Best-run chapter completion, raw intermediate area-mode counters, save switching, map-policy transitions, and same-frame disk persistence are not native acceptance claims from this run. A transitional jump screenshot without HUD is retained separately from its settled follow-up.

After normal Save and Quit, the saved file contains 11 deaths / 6 jumps, versus the last HUD's 10 / 6. As in the baseline, the additional saved death is unresolved. All guards were already Off during exit; the run does not claim guarded save/exit persistence. Akron settings differ only in the backup timestamp.
