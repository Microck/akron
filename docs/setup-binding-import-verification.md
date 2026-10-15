# Setup import binding verification

## Baseline reproduction

QA60 ran native Linux Celeste/Everest with exact Release SHA-256 `744b8bf2d2b8ceeb3f2e4630b835de92f94dea2578c61feab7c3e49db740d04f`. In the original City room, Cursor Tools was Off and Cursor Zoom was On with LeftAlt, 200 percent, Toggle activation, and reset-on-deactivate Off. Two complete native keypress cycles established working magnification and restoration.

A genuine keybind-only setup was exported through Akron and selected through the official Zenity file chooser. Cancellation left the working binding intact. After import, repeated LeftAlt presses no longer magnified the game, while the popup still displayed LeftAlt. A second import produced the explicit success notification and the same activation failure. Save and Quit, native Exit, and a new process restored working magnification with the saved binding unchanged.

The cancellation control exposed a separate issue: a harmless GTK transient-parent warning caused cancellation to be reported as file-browser failure. That classification is not fixed by this binding change. Initial unsuccessful bound-window cancellation attempts are excluded from acceptance evidence. Zoom settings were restored, while baseline export scope/name remained changed; profile and save differences are retained. Both baseline processes exited with code 0.

## Correction

Update the keyboard, controller, and mouse lists on the existing ButtonBinding rather than replacing its wrapper. Everest's initialized VirtualButton retains the underlying Binding, so in-place updates keep input connected. The unusable Open Menu safety fallback goes through the same mutation path. Missing wrappers are created without registering new virtual inputs; normal Everest initialization remains responsible for their lifecycle.

Whole and Keybinds imports share the correction. Failed setup rollback uses the same in-place updates but skips import-only Open Menu normalization. The pre-existing snapshot representation still does not distinguish an originally null wrapper from an empty wrapper and filters unusable key/button sentinel entries; this change does not claim to alter that representation.

## Candidate native acceptance

QA61 used exact Release SHA-256 `0d28307981de2aa9a5602f9b458769704b9171df69820166b9129f7987e2a4f1` with a fresh copy of the same original profile. The same native zoom positive control passed. A fresh keybind-only export/import retained immediate LeftAlt activation and deactivation without opening the binding editor or restarting. A second import in the same process also retained both directions. A separate genuine Whole setup export/import showed its success notification and retained activation and deactivation.

Twenty-one original screenshots, genuine archives, launch/exit receipts, and profiles are retained separately. Native restoration returned zoom to 100 percent/Hold/Off and export preferences to Whole with an empty name. The final Akron settings differ only in the automatic backup timestamp. Save counters retain one incidental death and elapsed time; no jump or dash was intentionally used. Native Save and Quit followed by Exit completed with code 0. This demonstrates the reproduced Cursor Zoom path and the exercised scopes; it does not certify every binding, controller/mouse hardware, the safety-fallback UI path, or live persistence-failure injection.

## Supplemental checks

Release and Debug each pass 2,310 tests. New checks seed actual wrapper objects and assert their identity survives import, repeat import, omitted-property handling, the Open Menu fallback, Whole import, and failed Whole/StartPos metadata persistence. Missing-wrapper coverage asserts creation without a VirtualButton. A source-presence check guards rollback's normalization flag.

These are deliberately limited: stripped Everest reference methods cannot establish imported collection contents or functioning virtual input. Real input functionality is established by the native acceptance above. Read-only native IL inspection confirmed Everest's initialization and the collection setters' in-place Binding semantics. No runtime getters, injected game state, or fabricated picker were used for the live acceptance.
