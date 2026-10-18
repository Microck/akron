# Ear Aid native QA fixture source

This generated data-only map uses only vanilla Celeste entities and sounds. It contains no game assets, recordings, or user profile data. Run `python3 build.py OUTPUT_MOD_DIRECTORY` to generate the mod folder. Place that folder under a native Everest Mods directory.

Rooms: fire, lava, move, fuse, wind, core, unlock, golden, seeker. Use the normal debug load command `load AkronQA/sounds ROOM`. CoreMode is Hot; wind uses the real Farewell ambience. The unlock room has a native blackGem configured to complete the area; dash-collect it and complete the normal sequence before visiting golden on a fresh session. Do not pretend debug mode alone grants golden eligibility.

The seeker room uses a vanilla fallingBlock, enclosed pocket, and normal player-on-top activation to crush a seeker. The move room requires jumping onto a native moveBlock. The fuse room requires an ordinary dash into a lightningBlock. No code injects audio events or entity behavior.

Authored as reproducible test source for Akron issue218 / PR379. Native recordings and screenshots are separate local evidence and are not included here.
