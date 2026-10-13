# Player-number viewport origin

Dash Number and Speed Number project a world anchor through `WorldToHud`,
which includes the window-space viewport origin. Their identity SpriteBatch
already renders inside the graphics viewport. Applying both origins moves
these numbers down/right on letterboxed displays.

The candidate converts only these two number anchors back to viewport-local
coordinates. Existing mirror/inverted transforms then use the local viewport
extents; the existing inverted 96-unit adjustment is preserved. Font size,
headroom, user offsets, colors, opacity and world projection are unchanged.
Other world overlays, fixed resource bars and global rendering are untouched.

## Native observation before the change

The combined eight-fix build, based on `2cee797`, has the same unmodified
number-render path as main. At a 1364x1024 letterboxed cloud desktop, Dash
Number On displayed a readable `1` over the player's lower torso (glyph core
approximately x78–82/y715–724, player approximately x49–100/y673–741).
Turning it Off removed the glyph. These are original-pixel observations;
exact JPEG threshold bounds are approximate. Above-player placement failed,
while reversible visibility passed. No offset or display-mode edit was used.

This resembles the older closed issue #16 but does not establish its original
cause or prove every reported missing-number scenario. Current native
Speed Number and Hide Player observations are being recorded separately.
Issue #280 separately reports the same class of extra-origin displacement for
Show Triggers. This number-only patch does not fix or verify trigger overlays.

## Bounded isolated native comparison, 2026-10-13

Two clean-core profiles used twelve byte-identical frozen input files.
Unmodified main package `c3397cecdbef21c68da25b7419a3f1ea2c439711f04d0d41d5ec64ad65be5e68`
reproduced Dash `1` over the lower torso and Speed `0` over the face.
The isolated candidate package
`33aee839093e414fbb8021da59ca3ffcf61ea567ebc368d1fa1243147fd6290b`
placed both readable glyphs above the head at unchanged 1364x1024 and original
-18/-34 offsets. Dash moved approximately y720 to591; Speed y704 to575, with
unchanged x near81. Thresholded glyph-core comparisons independently matched
26 Dash pixels and 55 Speed pixels after exactly (0,-129). This is a bounded
original-image measurement, not whole-frame equality or general calibration.
An unrelated golden object's crown overlaps the dash-number region; readable
On/Off controls remain available without modifying that object.

Candidate controls also verified individual number removal, coexistence,
one brief Hide Player/restore cycle retaining both numbers, and final number
removal. Labels Visible stayed Off. Main baseline deliberately did not repeat
the Hide cycle; the earlier combined-eight observation has its own identity.

Both processes exited normally with code zero. Native settings bytes were
unchanged; other preferences restored except backup timestamp and ordering of
the same collapsed-section names. Each save retained unexplained deaths4 to5,
session/room3 to4, and time/nonce/attempt changes. No whole-state or death-free
claim is made. Original images, full settings, logs and accounting are retained.

Twelve pure arithmetic cases and 2,057 Release tests pass. Mirror/inversion,
alternative viewport sizes, moving-player behavior, numerical accuracy,
first-frame behavior and gameplay while hidden remain native-unqualified.
Zero-origin 1920x1080 arithmetic is unchanged; it was not observed natively.
Native Engine/Level rendering installs Engine.Viewport into GraphicsDevice.Viewport;
an external renderer leaving these unequal remains an unverified compatibility
case. The existing inverted 96-unit adjustment is preserved, not calibrated.
Other HUD projections, fixed resource bars and combined-build acceptance are
outside this isolated result. No human testing, push, pull request or merge has
occurred.
