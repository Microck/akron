# Native transition-duration ownership fixture

Data-only map with the vanilla SummitBackgroundManager (AscendManager). No runtime code, injected writer, game assets, or user profile is included.

Generate with `python3 build.py OUTPUT_MOD_DIRECTORY` and place the result under Everest Mods. Load with the normal debug command `load AkronQA/transition ascend`.

Set Transition Speed to enabled 0.5x before jumping. The jump crosses the manager's Y threshold and starts the native Summit ascent. The native routine eventually sets NextTransitionDuration to 0.05 seconds. This room has no ceiling or upper destination, so the ascent cannot start an outgoing room transition that would consume/reset that field. Leave the override enabled for several more frames, disable it normally, and inspect `get Level.NextTransitionDuration` with CelesteTAS's read-only console command.

Old restoration returns stale 0.65. Ownership-aware restoration retains the native writer's newer 0.05. A separate disabled-override control should retain native 0.05, and a release before ascent should restore initial 0.65.

The fixture exercises a genuine native competing writer, not every third-party mod. A same-valued later write is indistinguishable to a value-based ownership guard. Raw screenshots and execution receipts are separate local evidence.
