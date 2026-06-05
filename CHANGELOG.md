# Changelog

All notable user-facing changes to Akron should be recorded here.

This project uses version tags that match the mod version in `everest.yaml`. Keep release notes focused on player-visible behavior, public docs, packaging, `.akr` file contracts, and migration notes when they matter.

## Unreleased

## 0.1.2-beta.9

- Keep legacy shortcut bindings from reappearing after startup normalization.
- Preserve right-side modifier keys when capturing menu bindings, including modifier-only binds such as `RightAlt` and `RightShift`.
- Keep Open Menu defaulted to `Tab` while allowing users to rebind it to another valid key.

## 0.1.2-beta.8

- Keep only Open Menu, Click Teleport Cursor, and Cursor Zoom Hold bound by default.
- Keep Open Menu user-customizable while restoring Tab only for missing or empty menu bindings.
- Stop built-in Casual profile defaults from enabling features automatically.

## 0.1.2-beta.7

- Keep Tab opening Akron's menu even when stale or custom menu bindings no longer include Tab.

## 0.1.2-beta.6

- Group the Sound tab's per-sound volume rows under collapsed Player, Objects, Entities, Ambience, and UI headers.
- Keep core Sound controls visible above the new groups.
- Reveal relevant Sound groups and children while searching, including group-name and individual sound matches.

## 0.1.2-beta.5

- Test the automated release path with GameBanana API authentication before the upload form.

## 0.1.2-beta.4

- Test the automated release path after tightening GameBanana login field selection.

## 0.1.2-beta.3

- Test the automated release path after cleaning release-conflict artifacts.

## 0.1.2-beta.2

- Test the automated release path after switching GameBanana publishing to the direct edit form.

## 0.1.2-beta.1

- Test the automated release path for GitHub, GameBanana, README links, and website links.

## 0.1.2-beta

- Add GitHub community templates for issues and pull requests.
- Document the repository formatting command for contributors.
- Make CI fail when the Celeste reference archive secret is not configured.

## 0.1.1-beta.3

- Add hidden showcase marker logs for OBS-synced feature demo recordings.
- Log ImGui top-level feature toggle intervals separately from popup detail changes.

## 0.1.1-beta.2

- Harden room and map capture exports.

## 0.1.1-beta.1

- Current beta version declared in `everest.yaml`.
- Public docs cover installation, first run, overlay use, feature policy, rulesets, `.akr` archives, troubleshooting, and contributor workflow.
