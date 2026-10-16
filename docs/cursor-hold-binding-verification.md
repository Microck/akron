# Live cursor-hold binding verification

Fixes #333. Cursor popup capture, Clear, and Default now update the existing
ButtonBinding and its underlying Binding rather than replacing the initialized
wrapper. Delayed capture resolves the current settings field when it completes.
The shared row is used by Click Teleport, Cursor Zoom, Cursor Tools, Menu Mouse,
Entity Inspector, and Fast Lookout.

## Native baseline and candidate

The baseline QA127 displayed newly captured bindings but Click Teleport did not
dispatch. Restarting with the persisted V binding in QA128 produced an explicit
teleport success toast. Installed Everest IL confirms a new ButtonBinding has no
VirtualButton until input initialization, whereas the initialized VirtualButton
retains the existing Binding and reads its current input lists.

Candidate QA129 used a fresh, isolated ordinary Forsaken City room 1 profile,
with physical native runtime assemblies. All edits below were made through the
running game's GUI, with real keyboard/mouse input and no intervening restart:

- Click Teleport: capture V; V plus mouse produces a teleport success toast and
  visible relocation. Clear; a V plus mouse attempt leaves the captured player
  position unchanged. Default restores LeftAlt; Alt plus mouse dispatches again.
- Cursor Zoom: capture V at 150% Toggle; V magnifies and another V restores the
  baseline view. Clear; a V attempt remains at baseline. Default restores LeftAlt;
  Alt magnifies and another Alt returns to baseline. Two scene landmarks measure
  approximately 1.5 times their baseline size in both active captures.
- Cleanup: both features Off, default LeftAlt bindings, Zoom 100% Hold, allow
  zoom out and reset-on-deactivate Off. Native Save and Quit and Exit completed,
  process exit code 0 at 2026-10-16 04:02:47 UTC.

The initial zoom attempt used R, which also opened the existing Restart Chapter
shortcut. Those screenshots are retained but excluded from clean acceptance.
The restart was canceled; the qualified sequence then used V. A separate cleanup
input-routing problem affected bound-window C events; physical keyboard C
successfully opened Mod Options and completed Save and Quit. Menu Mouse was
switched Off during that diagnosis and remained Off.

Release package SHA-256:
`66b731617bc3e6738050d930cbd629c047936cafe7c3cc4546bc1705868e0a3f`.
Both native Release and Debug test runs passed all 2,459 tests. Regression tests
cover wrapper/Binding identity, capture/Clear/Default, copying independent input
lists, mouse/controller list preservation, delayed resolution, and null fallback.

## Limits

Native acceptance covers Click Teleport and Cursor Zoom, not every caller or
controller hardware. Negative Clear cases are captured endpoints under the
recorded input protocol, not continuous proof of every intermediate frame.
No pixel-perfect teleport destination or frame-perfect latency is claimed.
Screenshots, profiles, logs, package/cache hashes, and qualified attempts are
retained in the private QA129 audit. No game assets are included in this PR.
