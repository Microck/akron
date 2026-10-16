# Vanilla assist routing verification (#228)

## Contract

Vanilla handlers are identified by the installed provider's `AbstractVanillaVariant` base class, with an explicit typed mapping to Celeste's thirteen canonical assist fields. They use `VanillaVariantOptions.SetVariantValue(Variant, object)`. Genuine extended variants retain `ModOptionsEntries.SetVariantValue` and the generic current-value getter. Known vanilla identifiers with a missing or incompatible contract fail locally rather than falling back to the generic store. Missing SaveData is transient and is not cached as a permanent integration failure.

The overlay renders after the provider's Level wrappers have restored persistent player assists. Vanilla Current therefore combines that canonical player preference with a nondefault map value only when a Level, active vanilla hooks, a provider session, and absence of a user override all permit it. Map provenance follows that decision rather than value equality. Unavailable reads are displayed explicitly and are not classified as active user cheats. The changed count and suppressed-value text use canonical assist fields.

Writes validate the route and assist type before enabling the master switch. Provider rejection propagates failure; configured values are stored or removed only after successful active writes. Ownership tracking still observes partial provider mutation in a finally block; this is not a transactional rollback guarantee. Popup and row actions display failure messages.

## Native regression evidence

Linux Celeste 1.4.0.0 / Everest 6580 with EVM 0.51.0. All gameplay inputs were real desktop key/mouse events; no injected runtime state or synthetic lifecycle was used for acceptance.

QA114 baseline, package `95e53afa8ccf93cc7bc5170dfb453a13ab28a2a839b1fd15219271f72ed30d33`, used the safe left wall of Forsaken City room1. Both-Off, vanilla No Grabbing On, and Off recovery all visibly climbed. True No Grabbing On blocked the same attempt, and its Off recovery climbed. Native booleans were corroborated by explicit UI feedback.

QA115 candidate, package `0d00b1aece635de84363dbd04e67eb702b1ba7f2d34f653b5c56182973d3f6f3`, repeated the five conditions with grounded starts: clips 010835 (both-Off rise), 010941 (vanilla-On block), 011023 (Off recovery rise), 011108 (True-On block), 011153 (final Off rise). Input was Left150ms then grab+Left+Up400ms, without jump or dash. The golden follower remained present; no scored deaths or transitions occurred. Ambiguous search-focus staging and a corrected Timescale1x toggle were excluded. Clean exit0 and post-exit profile retained.

QA116 final runtime package `8e65f989abb7e41080063acc1d99a618c7aeb1f9a6729047da28df1ac4523557` additionally exposes popup failure messages. Its Air Dashes popup correctly distinguishes configured Two while Off from active Current Two after toggling the row. Native grounded-refill trials show one upward burst in Normal's first airborne arc (011623), two before landing in Two (011817), three before landing in Infinite (011919), and one again after Reset to Normal (012009). Normal's later burst occurs after landing and is explicitly excluded from the airborne count. The enum test uses three separated upward dash presses; it does not infer dash count merely from inventory or hair color. Reset displays Current Normal and State Off.

QA116 also rechecked No Grabbing on the final runtime: clip012121 On blocks visible wall rise; clip012215 Off restores it, with explicit toasts15/17. Cleanup restores Air Dashes Normal, No Grabbing Off, True No Grabbing Off, and EVM master Off/0 changed.

All recordings show dropped-frame warnings. Conclusions concern visible sustained movement/burst contrasts, not exact input timing, engine-frame counts or speed equality. The tests do not establish all thirteen vanilla variants individually, all map-trigger combinations, or every provider version. Map precedence and missing-contract cases have automated coverage, not live custom-map acceptance.

## Automated checks

2,436 native-runtime tests passed in both Release and Debug for the final runtime package, with zero failures/skips. Coverage includes all thirteen assist field/type mappings; actual base-chain classification; missing handler/base/UI/enum/mapping/signature/save prerequisites; transient save recovery; map-level/hook/session/user-override gates; unavailable-value policy; map provenance; and production preflight/configured-write ordering. Existing numeric-gesture coverage remains enabled. The existing CL0006 warning is unchanged.

Original screenshots, recording containers/sidecars, decoded visual audits, launch/exit receipts, profiles, package hashes and test logs are preserved in the private QA evidence set. Raw runtime assets are not committed with this fix.
