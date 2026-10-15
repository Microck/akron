# JPEG capture row alignment verification

## Correction

Issue #267 reproduced in native Linux Celeste/Everest with the loaded FNA3D and SDL2 libraries recorded. At viewport 1178×663, the original JPEG raw tile had diagonal shear and colored scanlines; the merged JPEG compounded it. A matching PNG and aligned 1180×664 PNG/JPEG controls retained geometry. This reproduces the consequence in City room 1, not the historical Mirror Temple 1178×662 fixture.

The central SaveTexture JPEG branch now reads tightly packed RGBA and writes through pinned StbImageWriteSharp 1.16.7. Both raw and merged paths use this helper. It preserves requested dimensions, uses one writer per invocation, validates exact texture size/format and dimensions/pixel count before allocation, and leaves PNG on its existing path. Quality defaults to 90 and retains FNA_GRAPHICS_JPEG_SAVE_QUALITY, including zero meaning 90 and other integer values clamped to 1..100. Atomic output wrappers are unchanged. The managed DLL is included explicitly despite the project's default runtime exclusion, and third-party notices preserve the port's public-domain declaration and original stb dual-license text.

## Native paired acceptance

QA56 used exact Release SHA-256 3b26388695d222718bd1ff6cd622222ed08db8f1220719e5425adfdd8033ed2e. Four real Room Capture operations in City room 1 produced raw and merged PNG/JPEG at 1178×663 and aligned 1180×664. The previously corrupt JPEG width now retained upright geometry in both raw and merged output. Native viewport changes and capture operations were performed through the UI. Capture settings and viewport were restored; native Save and Quit followed by Exit 0. Twelve original screenshots, eight exports, metadata and run receipts are retained locally.

These comparisons establish geometry, not alpha/color perfection: background-removal output already contains pink/white artifacts in PNG and controls. Separate captures contain moving background elements and are not byte-identical. Whole-save deaths increased from 4 to 5 with one unassigned incidental death, while jumps 13 and dashes 0 were unchanged. Akron settings differed only in backup timestamp; native SetViewportOnce changes are recorded separately.

## Supplementary checks

Release and Debug passed 2,301 tests. Sixteen generated fixtures from the exact packaged encoder assembly cover widths 1,2,3,4,1177,1178,1179,1180 with odd/even heights. Pillow independently decoded exact dimensions and asymmetric interior RGB markers at top/middle/bottom; tiny widths use relaxed tolerance for JPEG chroma subsampling. Tests also cover invalid dimensions/buffer lengths, read-only destinations, stream ownership and repeated saves. These supplement the live game captures, rather than replacing them.

QA57 additionally exercised the real scaled capture path at 3×, producing 3534×1989 raw and merged PNG/JPEG. The isolated profile had ScreenshotScale=3 configured before launch, with original/configured snapshots retained; no runtime setter or injected capture was used. Native viewport adjustment selected 1178×663 and the actual export dimensions confirmed scaling. Background removal was Off for this pair. Raw and merged JPEG geometry remained intact with ordinary scene colors. Export options and viewport were restored through the UI; the prelaunch-only scale setting was restored after shutdown with the post-run profile retained. This is one approximately 7-megapixel room, not maximum-size or multi-tile performance certification. No all-platform, all-room, alpha-recovery, exact historical-fixture, or maximum-size performance certification is claimed.
