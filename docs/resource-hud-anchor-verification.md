# Fixed resource HUD viewport anchors (#216)

The fixed stamina and dash HUD use an identity-transform SpriteBatch. Their
anchors must use the active graphics viewport's local width and height; FNA
already supplies its origin during rasterization. This candidate replaces the
1920x1080 literal positions with local size-relative positions, preserving the
nominal 300x35 footprint, 48px margins and TopLeft clock clearance. At 1920x1080
all six nominal positions are unchanged.

User offsets, ring/pip sizes, player-attached widgets and the existing shared
dash/stamina overlap adjustment are unchanged. This is not a global HUD scale
change. Arbitrary offsets, large pip counts, ring overflow, very narrow windows
and the existing approximate overlap bounds can still clip. There is no claim
of complete containment for all geometry.

## Bounded native verification, 2026-10-12

An isolated clean-core Celeste 1.4.0.0 / Everest 6580 comparison used matched
copies of twelve frozen input files. At the unchanged 1364x1024 cloud desktop,
the gameplay viewport was letterboxed with its top near screen y129. Baseline
TopRight was absent; changing only the anchor to TopLeft made the stamina bar
visible. Actual controls established HUD and Always Visible On, player-attached
Off, Bar style, zero offsets and the On / Cheat policy tooltip.

The exact candidate package `11e1183a861431eeb39e30749ce7ab9578f7e09795f49fae5299a79d3d4bd54a`
displayed all six anchors. Original pixels measured nominal 300x35 fills at
screen x48/532/1016 and y177/813 as applicable, consistent with viewport-local
48px margins and one application of the letterbox origin. A native +10 X offset
moved TopRight ten pixels right. A separate same-anchor, zero-offset Bar control
showed green stamina and magenta dash bars separated. This qualifies that one
bar-style overlap arrangement, not every resource geometry.

Both processes exited normally with code zero. Tested settings were restored
to their pre-prerequisite values except the ordinary backup timestamp; native
settings bytes were unchanged. Both saves recorded deaths4 to5 (session3 to4)
without a deliberate movement, Retry or death input in this case. Attribution
is unknown; no whole-save preservation claim is made. The raw native packet
retains originals, settings, package identities, logs and those deltas.

Resize and true1920x1080 controls remain untested. The current cloud desktop is
1364x1024; an earlier separate windowed route had produced an MIT-SHM failure,
so this case did not repeat mode changes. Ring/pip rendering, player-attached
projection, every viewport, unusual offsets and provider viewport changes are
unqualified. Pure geometry tests preserve the 1920x1080 arithmetic, not a live
reference-size observation. Earlier #240 invisible-stamina evidence is not
retrospectively attributed solely from this comparison.

This isolated candidate is not part of the combined eight-fix package. Root
authored the product change; no human testing, PR submission or merge occurred.
