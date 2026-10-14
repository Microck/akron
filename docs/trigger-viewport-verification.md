# Show Triggers viewport verification

Issue: #280. Verified by the assistant through native desktop input and screenshots on 2026-10-14; no human testing is claimed. The owner authorized a human-testing exception for these fixes.

## Change

The final Akron HUD pass targets `Engine.Viewport` with an identity SpriteBatch. `WorldToHudRect` returns window coordinates including the viewport origin. Show Triggers now removes that origin before drawing, so it is applied exactly once. The shared rectangle rasterizer is extracted without changing its fill, color, outline thickness, or rounding. Gameplay-buffer automation consumers retain their existing projection.

## Native before and after

Both trials used Celeste 1.4.0.0 / Everest 1.6580.0 on the cloud Linux desktop, a 1364×1024 native window with gameplay starting at y129, and Forsaken City Normal room 6. The independent Show Hitboxes trigger filter provided a native collider reference. The map's changeRespawnTrigger is 56×48 world units at room-local (24,40); its right and bottom edges agree with visible terrain landmarks.

- Before, base main `152e4254d83aa18d1690a22440ce317b7344ff6b`, package SHA-256 `e080c4d022d0db72da014cd6b017de340a563c030ca02a6b94c1f9d34c8a9091`: Show Triggers starts near y428 while the reference starts near y299. The extra vertical displacement is approximately 129 pixels.
- After, candidate Release package SHA-256 `941de874bf572878e88abd31547a354c24befdaf057ec0c3aeae17c80b673a5c`: Show Triggers starts near y299 and aligns with the independent collider reference within one pixel of rasterization differences.
- In each trial, gameplay was frozen through the public native control. Show Hitboxes and its trigger filter were Off during the scored Show Triggers Off → On → Off sequence. The two Off images in each trial are byte-identical. The target only appears when its independent toggle is On.
- Cleanup restored target Off, Show Hitboxes Off, the original trigger filter On, and ordinary unfrozen gameplay. Native Save and Quit followed by Exit completed normally. Candidate exit code was 0 at 19:58:10 UTC; the native window was subsequently absent.

Original native captures, launch receipts, saved disposable profiles, and independent image review are retained in the private QA evidence. These captures are JPEG bytes returned by the screenshot tool despite `.png` filenames; no image was edited to create the result. The frozen-gameplay toast is present outside the measured region.

## Automated checks and artifact identity

Release and Debug each pass 2,157 tests, including five new cases for vertical, horizontal, combined, zero, and negative viewport origins while preserving fractional dimensions. These unit tests cover the coordinate arithmetic, not renderer wiring or live graphics state. Native before/after verification covers the observed renderer behavior.

The exact changed source used for the Release trial is retained. `dotnet format` was run on the three changed C# files; this also normalizes pre-existing formatting within the two production files. A later Debug test build overwrote the worktree's top-level ZIP; it is not the live-tested Release artifact. The pinned Release package was separately preserved.

Build warnings include the existing CL0006 warning and NU1900 because vulnerability metadata could not be retrieved using a read-only NuGet cache location. No successful vulnerability audit is claimed.

## Scope and limits

This is one native trigger class and one letterboxed viewport, on a freshly reconstructed runtime shared by both trials. That reconstruction has documented binary differences from the older historical runtime; it is not represented as a byte-identical historical installation. Provider isolation, other trigger classes, horizontal letterboxing, resizing, zoom changes, mirror/inversion, other platforms, collision behavior, and broad game-state preservation were not live-tested. Mirror/inversion handling is inherited and unchanged. No runtime state getter or injected gameplay code was used in these trials.
