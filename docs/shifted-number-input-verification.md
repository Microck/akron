# Shifted number-row input (#199)

## Scope

The ordinary overlay's existing manual character translator now maps Shift+0 through Shift+9 to the US-style punctuation string `)!@#$%^&*(`. The keyboard wrapper still accepts either Shift key and now calls a pure key/shift helper. Diagnostics retains its operating-system Unicode text-input path. This is not keyboard-layout, AltGr, IME, Caps Lock, numpad or key-repeat support.

## Automated verification

Eleven added cases cover all ten shifted/unshifted digits and existing A/a/underscore behavior. Both Release and Debug pass 2,196 tests locally. Tests use the pure key/boolean boundary to avoid executing stripped FNA KeyboardState methods in CI. Physical left/right Shift routing is established separately through native input, not claimed by the pure tests.

## Native baseline and first candidate

Linux native Celeste/Everest 1.6580.0. In baseline QA25, physical LeftShift+1, LeftShift+2, LeftShift+0, 1, LeftShift+a typed `1201A` in ordinary Search. The same sequence typed `!@)1A` in an unsent Diagnostics Title field. The Title was cleared and the dialog canceled, without sending diagnostics.

Initial candidate QA27 package `5c6b3557fa08813d04fa67c68075b7f4d0ec1a7b3dbcfcc4f0f3d7c9afab68b9` typed `!@)1A` with both physical Shift keys. It also accepted all ten shifted symbols and retained unshifted digits. Search cleared, blur worked and a fresh menu-toggle press closed the overlay. Normal Save and Quit/Exit code0. Original screenshots and profile/build records remain private.

The final helper extraction and parent's CI-compatible vector initialization were made after that first package. Final exact-package acceptance is recorded separately; initial evidence is not relabeled as final-package evidence. This is AI-operated native QA under the owner's explicit human-testing exception.

## Final exact-package recheck

QA29 rechecked Release SHA-256 `7e6c0ad803102d3a512fdee041fdb1c4e2725361862618ab3ad28e6145340810` after the pure-helper extraction and parent CI-compatible change. Both physical left/right Shift sequences typed `!@)1A`. Shift+0 through9 yielded `)!@#$%^&*(`, and unshifted0through9 yielded `0123456789`. Search cleared, blurred, and a fresh K closed the overlay. Settings differed only by the automatic backup timestamp. This is direct native proof for the final package; whole-profile preservation is not claimed.
