# Noclip speed feedback verification (#208)

## Native baseline

QA145 on the original PR339 package reproduced the issue in Celeste/Everest with Noclip disabled. A single native edit of Speed to `0` left out-of-range text active; the effective speed clamped to its UI minimum while repeated identical `x0.1` toasts accumulated. Closing the overlay exposed the duplicate stack. Restoring `1` produced the normal single change toast. Screens 01–04 retain the baseline. A later attempted Grab edit clicked an underlying checkpoint action after Return had closed the popup; it is excluded and that profile is not reused.

## Correction

All four Noclip speed setter/adjuster feedback routes now normalize the proposed integer and return without a toast when it equals the stored value. Actual changes still update the setting and produce feedback. Existing unit conversion, rounding, and clamp rules are unchanged. This does not broadly change InputFloat editing or other popup actions.

Release and Debug each pass 2,510 tests, including repeated normalized proposals, subsequent genuine changes, and all four toast call-site guards. Read-only review found no blocking issue. The pre-existing keyboard decrement default-reset behavior for nonpositive proposals is outside this patch.

## Candidate native acceptance

Pending. QA148 uses an isolated profile copied from closed QA147, not the accidental checkpoint baseline. Native Speed/Grab Speed out-of-range edits and valid-value controls must be observed before reporting this fix verified.
