# TAS handoff verification (issue #231)

## Change

Launch preflights CelesteTAS availability, its live public Enabled setting, and the configured file. It normalizes the path before the existing console-command/history bridge. Once synchronous startup reports an active TAS, Akron closes its overlay through the normal visibility API, releasing its own input and pause ownership. Rejected startup keeps the menu open. Optional Studio notification failure cannot turn a committed launch into a failure report. The user's input-blocking and pause preferences remain unchanged.

## Native evidence

Tested on native Linux Celeste/Everest with CelesteTAS 3.47.1. Release package SHA256: `51ae06b606a1a59aebf4b8c61a78b04e248682cd7461b5c66b674a8f5cc9ada5`. Native Release and Debug suites each passed 2,443 tests. Existing CL0006 warning unchanged.

QA117 reproduced the original bug: a finite upward-dash TAS moved with the menu closed or input blocking off, but its HUD advanced without ascent when the menu was open with blocking on. The qualified script held Up through dash initialization: 60 neutral frames, 1 Up+Dash, 14 Up, 45 neutral. Initial one-frame-aim experiments were unscored.

QA119 used actual native keyboard/mouse to invoke Play Configured TAS. With Block Gameplay Input on, both Pause While Open off (clip 021609) and on (022304) produced real upward dash travel and TAS HUD progression after the overlay closed. Independent frame inspection confirmed both. Ordinary Right and Up+Dash input with the overlay reopened and pause off remained blocked at the same visible position. The two preferences were preserved across launch; pause was then restored off.

The configured native AutoPauseDraft setting pauses at finite EOF; Right Control stopped playback afterward. This expected EOF pause is not a failed handoff. Screenshot 02 was captured before overlay closure despite its filename; later screenshots 03/05 show closure. Recordings dropped frames, so no exact timing claim is made. Golden follower was present; no scored death or room transition occurred.

QA120 verified native failure paths: disabled CelesteTAS displayed an enable-provider message and kept the overlay open; after enabling it through Mod Options, an existing empty TAS displayed the did-not-start message and kept the overlay open. The delayed disabled-case saved screenshot missed the toast; the live capture showed it, and separate native options screenshots confirm disabled/enabled states. Empty-file rejection is captured in screenshot 04.

Both processes exited normally. QA119 retained InfoHud on after an unsuccessful cleanup toggle, explicitly recorded in its evidence; QA120 used InfoHud off. These are isolated fixture profiles. Raw captures, provider archives and game assets are kept outside the public source repository.

## Boundaries

This verifies the installed provider and bounded native cases, not every CelesteTAS version, Studio failure, map or scripted command. Unit tests additionally cover rejected startup, startup/status exceptions, enabled-state reflection failures, ordering, history cleanup and absence of preference writes. There is no blanket TAS exemption in ordinary gameplay input blocking.
