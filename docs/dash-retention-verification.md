# Native Retry dash-retention verification

Issue: #222. Candidate is stacked on PR309; this report covers its production change only.

## Change

Celeste rolls Session.Dashes back to DashesAtLevelStart during Level.Reload. Akron now retains the discarded amount in its module session for the practice HUD. Native Session fields and SaveData totals are never modified by this accounting. Session/Chapter/Both use the effective practice count; File still reads the native file total.

A reload context captures both session identities and the prior counts. Respawn reconciles all matching nested contexts before the existing autosave callback; successful return reconciles again idempotently. Finally always releases the context. If native respawn has already occurred before a later exception, its accounting can already have been committed; no final reconciliation is performed after the exception. Disabling preservation clears retained dashes at the next respawn, consistent with the jump option's reset timing.

## Native acceptance evidence

- Baseline package `619ec6d02ec9eed5fb6060c2fe1061b47aa56452c7a4e2146832420e72e2c8b1`: native Prologue Retry with both flags Off reset 1/1 to 0/0. Both On reset dashes to 0 while jumps stayed 1, reproduced twice.
- Candidate Release package `49c9ecd7f30052f39d40b37f57f23c63dd267d1c5d93e53f4bf92e7720a778c7`: first native Retry retained 1/1; another dash reached 2/1 and another Retry kept 2/1. A third Retry with no intervening dash also kept 2/1.
- Switching the candidate dash display showed Chapter 2, File 12, and Both 2/12. The saved starting file total was 10 and two actual dashes were performed.
- Full process exit and restart with the same profile retained Session 2/1. A fresh dash and jump reached 3/2; turning both preservation flags Off and retrying produced 0/1. The jump baseline is 1 after reentry, so this is not claimed as a blanket zero reset.
- Re-enabling dash preservation did not resurrect the discarded count (0/1). One new dash reached 1/1 and native Retry retained 1/1. Jump preservation remained Off in this independent toggle check.
- Release and Debug suites passed 2,260 cases each. Fourteen new cases cover arithmetic and compiled hook wiring/order. These are not a replacement for the live native reload observations.

## Scope

Live checks use an inherited three-dash Prologue fixture, inventory restoration and StartPos respawn disabled, ordinary keyboard gameplay and native pause Retry. No runtime console getters or injected gameplay state were used. This is not the exact original Forsaken City fixture. Static installed Reload IL independently establishes the native rollback mechanism, but does not itself prove runtime hook execution.

Successful StartPos death restoration bypasses Level.Reload and remains outside this correction. No claim is made for that path, arbitrary third-party state restoration, nested runtime reloads, or every mod combination. Screenshot files are unedited JPEG captures despite their historical .png suffix.

Nonzero native checkpoint baselines, room transitions, hazard deaths, and respawn-triggered autosave were not exercised live in this pass. Autosave ordering and nested-context handling were reviewed in source/compiled structure, not scored as native acceptance. Candidate settings were restored through the overlay; final process closure and evidence audit are recorded separately.
