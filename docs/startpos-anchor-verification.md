# Viewport-local StartPos and indicator anchors (#216)

## Scope

Replace the shared HUD box anchor's fixed 1920x1080 dimensions with the active graphics viewport. Drawing remains viewport-local; FNA applies the viewport origin once. StartPos TopLeft keeps its existing ordered-label stack. Global and label-style offsets remain additive. The helper also supplies both cheat-indicator styles and their normal obstruction plans.

## Automated verification

Release and Debug each passed 2,180 tests. Seven new cases exercise all nine anchors across five viewport sizes, the unknown-enum fallback, and an oversized label. These are pure coordinate tests, not native rendering tests. Changed C# files were formatted. Existing CL0006/NU1900 environment warnings remain; no successful vulnerability audit is claimed.

## Native acceptance, 2026-10-15

Linux native Celeste/Everest 1.6580.0, 1364x1024 window with 1364x766 gameplay viewport at (0,129). All controls operated through the visible UI; no injected game-state reads or writes. Player Overlap was Off.

Baseline (PR301 package): TopLeft appeared at the expected left edge. TopCenter centered near x960 rather than x682; Center appeared near (960,669) rather than (682,512). Selecting TopRight made the label absent; source arithmetic explains offscreen placement, but the screenshot alone proves absence only. The Off control removed the label.

Candidate Release package SHA-256: `079cc9f2050fbbcd692ddebd5ebd0aec96402420d6825bff4dd532c9d08291c6`.

- TopLeft remained in its original position.
- TopCenter centered at the visible viewport midpoint.
- TopRight stayed inside the visible right edge.
- Center aligned to the viewport center, including the letterbox origin exactly once.
- BottomRight stayed inside the lower-right game area.
- Native style offsets (-40,-30) moved the BottomRight label left/up accordingly.
- Restoring zero offsets and TopLeft reproduced the initial layout.
- The shared helper placed both Dot and Text cheat-indicator styles at TopRight inside the viewport.
- Labels and indicator were disabled again; all modified Akron preferences and panel layout were restored. The backup timestamp legitimately changed. Full save/session identity is not claimed preserved.

Original screenshots, saved settings, profiles and build/launch records are retained in the private QA evidence. Only one native viewport and the unset StartPos text were exercised. Automated size coverage is not equivalent to native checks at every resolution. No human-testing claim: this is AI-operated native QA under the owner's explicit exception.

## Separate known limitations

Player Overlap relocation still uses fixed 1920x1080 dimensions, and its player bounds include window-space viewport offsets while labels use viewport-local coordinates. Those pre-existing Fade/Move issues are outside this focused anchor patch and require separate native reproduction/fixes. Do not interpret this report as passing obstruction handling.
