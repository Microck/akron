# Extended variant control identity verification (#232)

## Scope

Dynamic Extended Variant Mode rows now carry a namespaced popup key built from the provider's option name. Popup dispatch resolves the provider option before label-based core controls. Numeric/non-boolean row actions call the provider's configured-value toggle by name, not the shared label-based delta handler.

The same identity is used for popup visibility, anchors, fallback renderer value/action routing and capability checks. Visible labels and saved row keybinding IDs are unchanged. Core Dash Count remains the Akron control; EVM Dash Count remains the provider option.

## Baseline

AI-operated native cloud Linux Celeste/Everest, 1364×1024, UI scale 1.0, EVM 0.51.0. QA32 package SHA-256 `4fd334056aa0404ba2de2cbe53313b4e430d0c667d3549e7305ee3b6d27d1154`.

Search `dash count` exposed Player and EVM rows. Player popup showed core Max 2 and both refill controls checked. EVM triangle opened the same core popup. Clicking the EVM row label produced the toast `Dash count: 3.` and the core popup then showed Max 3. Core Max 2 was restored, refill settings retained, Search cleared and original layout restored. Normal exit code 0 at 2026-10-15 03:48:24 UTC.

The wrong baseline popup never exposed EVM Current; no native Current=-1 claim is made for that run. Screenshots show result states; click routing and actions are operator observations. Profile deltas include normal gameplay/session bookkeeping, not an unchanged-profile claim.

## Native candidate acceptance

QA33 tested Release SHA-256 `386f987a0bfa5d9d32e914b1ea18a409fbc0dacb8611088dc26d7f8dee8236f4` in the same native runtime and window.

- Core popup remains Max 2 with both refill controls checked and anchors to Player.
- EVM popup anchors to its external row and shows State Off, Configured/Current/Default -1.
- Native value edit configures 2 while State remains Off and Current remains -1. Pressing Return also dismissed the popup and cleared Search; the query and popup were reopened to inspect the committed state. No claim about that separate dismissal behavior is made.
- Core remains Max 2 after external configuration.
- External row toggles provider On/Current 2; core remains Max 2 with refill controls unchanged.
- External row toggles Off/Current -1 while Configured remains 2. Reset restores Configured/Current/Default -1.
- Non-colliding Spawn Dash Count opens its own provider editor.
- Final core popup remains unchanged. The provider master switch, enabled by provider operations, was returned to its original Off state; native tooltip confirms Off / 0 changed. Search and layout restored.

Screenshots are native unedited captures; physical input sequences are operator observations. Provider settings being restored is not a claim that the complete game/session profile is byte-identical.

## Automated checks

Release and Debug each pass 2,216 tests, including 12 new identity/routing regression cases. Pure key round-trip/rejection tests and source-contract checks supplement native QA. No human testing is claimed.

## Limits

Provider-specific ranges and gameplay semantics remain the external mod's responsibility. This fix does not rename options or rewrite saved keybind IDs. It does not claim exhaustive native coverage of every dynamic variant or the fallback renderer.
