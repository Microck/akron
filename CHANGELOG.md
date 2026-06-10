# Changelog

All notable user-facing changes to Akron should be recorded here.

This project uses version tags that match the mod version in `everest.yaml`. Keep release notes focused on player-visible behavior, public docs, packaging, `.akr` file contracts, and migration notes when they matter.

## Unreleased

### Fixed

- Use a stable docs-hosted Open Graph preview image after Mintlify's generated Akron preview returned an empty PNG.

## 0.1.2-beta.15

### Added

- Add Symbiote, Carbon, Retro, Coniferous, and Wine overlay theme presets.

### Changed

- Let Mintlify generate page-specific Open Graph previews for the docs site instead of using one static logo image.

### Fixed

- Prevent active Control Display key editor fields from copying into another key when selecting a different key before blurring the input.
- Prevent Akron's overlay hotkey from opening over Everest's Enable or Disable Mods menu, where Tab favorites or unfavorites mods.

## 0.1.2-beta.14

### Added

- Add local Akron diagnostic logging under the Interface tab, including log level, warning mirroring, file rotation, retained files, and a test entry action.

### Changed

- Reclassify Motion Smoothing FPS Bypass as regular clean while keeping TPS Bypass, object interpolation, TAS mode, and Nasty mode marked as Cheat.

### Fixed

- Keep the overlay Search textbox focused while backspacing from narrow queries into broader result sets.

## 0.1.2-beta.13

### Fixed

- Let the Open Menu key cancel hidden Auto Kill and Auto Deafen area selection and reopen Akron, so players are not stuck in a frozen selection mode.
- Restore Akron-managed cursor visibility after StartPos placement, Auto Kill area selection, and Auto Deafen area selection ends.

## 0.1.2-beta.12

### Added

- Add a Backups overlay tab for managing Akron save backups from inside the game.
- Add manual backup creation for the current Celeste `Saves` folder.
- Add automatic backup triggers for Akron launch, Akron close, save/settings writes, chapter entry, and timed intervals.
- Add a restore browser for backup ZIPs, including backup timestamps, file names, sizes, reasons, save slots, and pinned state.
- Add ZIP metadata in `_akron-backup.json` with the backup reason, creation time, Celeste version, Akron version, save slot, profile name, current area/room when available, and enabled Everest modules.
- Add backup pinning through sidecar `.pin` files so important backups are protected from automatic cleanup.
- Add retention cleanup by maximum count, maximum age, maximum total folder size, and protected newest backups.
- Add a `Last Result` popup with the latest backup status, last backup age, backup folder path, manual-create action, and open-folder action.
- Add user-facing docs for backup creation, restore behavior, metadata, pinning, and retention rules.
- Add feature tooltips for overlay actions.

### Changed

- Group automatic backup triggers into a `Triggers` submenu so the Backups tab stays compact.
- Make restore create a `pre-restore` safety backup before extracting the selected ZIP.
- Make restore reload the restored save data and return to the main menu so Celeste does not keep using stale in-level save state.
- Exclude `Saves/AkronBackups` from future backup ZIPs so backups do not recursively include older backups.
- Rename `Skip Cutscene / Dialogue` to `Skip Cutscene`.
- Verify the Backups overlay and manual backup creation on the remote Windows Celeste test box, including ZIP readability and metadata contents.

### Fixed

- Prevent save/load restore crashes when a modded runtime rejects readonly field writes during deep clone.
- Prevent FrostHelper and other gameplay renderers from being interrupted by Akron overlay rendering.
- Preserve graphics device state after Akron draws ImGui overlay content.

## 0.1.2-beta.11

- Add Spawn Jelly, Spawn Theo, Set Inventory, Dream State, and Core Mode overlay actions.
- Add Previous Map, Next Map, Previous Checkpoint, and Next Checkpoint creator navigation actions.
- Put Spawn Jelly and Spawn Theo in Shortcuts as regular action buttons instead of triangle option rows.
- Add Set Inventory dash and jump configuration, profile persistence, console controls, and optional death restore behavior.
- Make Dream State toggle Madeline's dream dash inventory state directly from the Player tab.
- Make Core Mode configurable as Hot or Cold, add Toggle/Cycle click behavior, and restore the level's original mode when a toggle is turned off.
- Add console controls and status output for Set Inventory and Core Mode.
- Simplify Akron's Mod Options menu so the in-game overlay carries the detailed feature controls.
- Group Creator navigation actions more predictably and rename in-order room warps to Previous Room In Order and Next Room In Order.
- Show Auto Kill and Auto Deafen area selection previews while placing areas, including a single-pixel marker before the first corner is set.
- Improve HUD/overlay scaling so rows, popups, resource bars, and area markers stay aligned across viewport scales.
- Render dash and speed numbers above Madeline instead of centered on her body.
- Add 1px submenu outlines and stop triangle hover targets from showing duplicate info tooltips.
- Keep ImGui popup positions and value rows stable when overlay scale changes.
- Add player-hurtbox hitbox filter and color controls to commands and UI.
- Update hitbox default colors to follow CelesteTAS conventions where available.
- Draw Madeline's hazard hurtbox with CelesteTAS-compatible bounds and pixel rounding.
- Classify spinner-style hazards for hitboxes and trajectory collision checks.
- Hide unknown collidable helper entities from the live hitbox overlay when Akron cannot classify them confidently.
- Keep hitbox lines at least 1 screen pixel thick so persisted thin settings remain visible.
- Move practice area pixel marker labels below the marked edge.
- Update README install buttons and public docs to use Akron's stable install endpoints.
- Document release configuration/runbook details for GitHub, GameBanana, README, and website release sync.
- Credit viddie's Spawn Jelly, Set Inventory, Dream State, and Core Mode suggestions in the docs.

## 0.1.2-beta.10

- Start fresh installs with no active Akron profile or primary ruleset, so features stay off until explicitly enabled.
- Add `None` profile and ruleset selections for clearing Akron defaults from commands and overlay UI.
- Render hover help popups on ImGui's tooltip layer so they stay visible above overlay rows.

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
