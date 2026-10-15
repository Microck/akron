# Linux setup-picker cancellation verification

## Reproduced baseline

Issue #250 was reproduced again during QA60 on exact Release `744b8bf2d2b8ceeb3f2e4630b835de92f94dea2578c61feab7c3e49db740d04f`. Official Debian Zenity 4.1.90 opened normally through Akron's native Import Setup action. Desktop Cancel closed the actual dialog; the game then logged a file-browser failure containing GTK's transient-parent informational diagnostic and a missing-kdialog fallback. A later selected-file import succeeded. The initial unsuccessful bound-window cancellation attempt is not counted as a cancellation. No exact subprocess exit-code trace was captured; the native operator sequence, dialog observations, and log corroborate the reported symptom.

## Correction and limits

Recognize only the known transient-parent diagnostic, on every nonblank stderr line, for direct Zenity or the explicitly configured `flatpak-spawn --host zenity` command. The allowance requires exit code 1 and empty stdout. Normal GTK process/time prefixes and whitespace are accepted; unknown or mixed diagnostic output remains a failure through this new allowance. Successful selection and existing fallback flow are unchanged.

This is deliberately narrower than ignoring stderr. It does not redesign the pre-existing generic substring cancellation rules, change subprocess output buffering, or certify every Zenity version/localization or exit-code environment override. Hosted Zenity is covered by classification tests, not a live Flatpak session.

## Supplemental tests

Release and Debug each pass 2,332 tests. Twenty-two new pure classification cases cover direct and hosted Zenity, the observed warning and normal prefixes, repeated lines/CRLF, mixed errors, other commands, noncancel exit statuses, unexpected stdout, and existing empty cancellation. They do not prove native process launch, fallback invocation, notifications, or log output; those require the native acceptance recorded separately.

## Native candidate acceptance

QA62 exercised exact Release `ba04e0879b5a5db2a933408f2a2dd1c047c26cff3715a2843346f17e7b8909a9` with the same genuine official Zenity installation. A fresh HUD-scope setup was exported through Akron. Confirmed Escape and Cancel-button closures each showed “No setup pack selected.” The final Cancel sequence preserves eight successive original screenshots, including the notification. No second picker appeared after confirmed closure. The game log contained no failed-browser warning during this sequence; this is log/visual evidence, not a subprocess invocation trace.

A later native selected-file confirmation showed “Imported HUD setup issue250-picker-control.” This checks the real selected-file path still works after cancellation. Unconfirmed input attempts and captures that missed the short notification are explicitly excluded from notification claims. Export scope/name were restored through the native UI to Whole/empty. Original screenshots, archive, logs, profiles, and launch receipts are retained locally.

Native Save and Quit followed by Exit completed with code 0.
