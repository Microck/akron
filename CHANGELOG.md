# Changelog

All notable user-facing changes to Akron should be recorded here.

This project uses version tags that match the mod version in `everest.yaml`, while release headings can use readable public names such as `Akron Beta 42`. Keep release notes focused on player-visible behavior, public docs, packaging, `.akr` file contracts, and migration notes when they matter.

## Unreleased

### Added

- Let StartPos `.akr` and Community Pack uploads include portable room-state snapshots while stripping deaths, time, and other stats from imported room-state restores.

## Akron Beta 43

### Fixed

- Show at least 15 StartPos slots in the selector by default and arm StartPos death reloads after a successful StartPos load.

## Akron Beta 42

### Fixed

- Keep StartPos slots scoped per map and restore persisted StartPos player/session state across game restarts without rewinding time or deaths.

## Akron Beta 41

### Added

- Let the in-game Community Packs upload flow submit Auto Kill and Auto Deafen area packs, with generated metadata shown directly in the upload form.

### Changed

- Simplify the Upload Pack popup with aligned fields, generated text shown in editable fields, and no separate preview block.
- Keep Upload Pack feedback visible with a compact progress bar while Akron captures the full map and uploads the submission.

### Fixed

- Show the selected Upload Pack markers in automatic full-map captures, even when normal scanner marker export options are off.
- Replace raw Upload Pack completion states with a Discord confirmation/review prompt and theme the upload progress bar with the active Akron accent color.
- Show Upload Pack server failures in the popup and stop before full-map capture when the upload endpoint is unavailable.

## Akron Beta 40

### Added

- Add the in-game Community Packs upload flow for StartPos packs, including automatic full-map capture, generated metadata, saved attribution, and Discord moderation handoff.

### Changed

- Document the Community Packs upload and publication architecture without tying the player-facing catalog contract to provider-specific free-tier details.

## Akron Beta 39

### Fixed

- Keep Lag Pauser from counting Celeste's native freeze frames and native StartPos restores as lag spikes.
- Add an opt-in Lag Pauser Ignore SRT option that skips Speedrun Tool load-state hitches for a brief grace window.

## Akron Beta 38

### Fixed

- Write merged room collages after Room Capture and world-space `map.png` outputs after Map Capture, with scanner markers drawn after stitching at one game pixel and a Downscale option for safer large map exports.

## Akron Beta 37

### Added

- Add an in-game Upload Pack popup for submitting Community Pack drafts with section, attribution, generated title, description, and terms controls.

### Fixed

- Keep StartPos slots scoped per map and restore persisted StartPos player/session state across game restarts without rewinding time or deaths.
- Let Skip Cutscene run Celeste's active cutscene skip callback instead of leaving the level stuck in a skipping cutscene state.
- Let Akron's internal recorder find host FFmpeg and its Linux libraries from inside the Steam Runtime sandbox.
- Prefer the most specific matching SFX volume group so broad sound fragments do not shadow narrower controls such as Ridge Wind.
- Keep Pause Countdown from subtracting level clock time twice while waiting after unpause.
- Tighten several verified overlay, HUD, input, backup, StartPos, and runtime helper paths found during the player-visible checklist pass.

## Akron Beta 36

### Changed

- Keep Refill Clarity sprites and dialog source assets inside Akron's source resources while preserving the released mod zip layout.

## Akron Beta 35

### Fixed

- Keep Refill Clarity on the Better Refill Gems single-sprite replacement path while still applying Akron's color and opacity settings.

## Akron Beta 34

### Fixed

- Let Refill Clarity use Better Refill Gems-style sprite replacement for one-use dash crystals while keeping its color and opacity controls live.
- Let Entity Inspector click cycling continue when the next click lands on a different pixel of the same target stack.

## Akron Beta 33

### Fixed

- Keep hitbox rendering aligned with Celeste's gameplay camera and live collider data.

## Akron Beta 32

### Fixed

- Persist overlay category collapse state across restarts.
- Keep Entity Inspector pin popups on-screen, make same-target click cycling close after the last hit, reduce duplicate collapsed details, align pin targeting during zoomed-out views, and remove extra rectangular highlights from collider-backed targets.
- Keep regular StartPos captures on the active slot after Set, so Load immediately returns to the captured position.
- Let imported and shared StartPos entries without runtime snapshots remain selectable and load as position-only starts.

## Akron Beta 31

### Fixed

- Keep the overlay responsive in mod-heavy setups by avoiding duplicated row filtering while external tool panels are placed.

## Akron Beta 30

### Added

- Add Death Particles customization for color mode, preset shapes, custom canvas masks, and particle duration.
- Let each Auto Kill area keep its own conditions, copy configured defaults into newly placed areas, and highlight the selected area brighter while its conditions are edited.

### Fixed

- Prevent submenu clicks from selecting or activating overlay rows behind the popup.

## Akron Beta 29

### Added

- Add Entity Inspector cursor pinning: hold the inspector cursor bind to click entities or triggers in-game, cycle overlapping hits, view runtime and source-bound map properties, and copy an inspection report.
- Add Entity Inspector close and hover-preview controls, highlight pinned and hovered targets, keep solid-tile highlights scoped to the hovered tile, and let Cursor Tools use Entity Inspector as its left-click action.
- Add cursor hold binding controls to the Click Teleport, Cursor Tools, and Cursor Zoom popups while keeping Left Alt as the default.

### Changed

- Update overlay row and popup classification labels to match the latest dcheat classification list, including Backups, Logging, Autosave, recorder, custom label, keybind, and gameplay-mutating utility classifications.

### Fixed

- Let Celeste's Journal shortcut take priority over Akron's default Tab overlay bind in the overworld.
- Keep Entity Inspector's submenu aligned with Akron's ImGui HUD style, require the cursor hold bind for gameplay pinning, add report placement/detail defaults, keep fixed report corners anchored across size changes, and keep titlebar-collapsed reports reopenable.

## Akron Beta 28

### Added

- Add Inspector Pin to Entity Inspector: click entities or triggers in-game, cycle overlapping hits, view runtime and source-bound map properties, and copy an inspection report.
- Credit viddie's Inspector Pin suggestion in the docs.

### Fixed

- Let Celeste's Journal shortcut take priority over Akron's default Tab overlay bind in the overworld.
- Keep Entity Inspector's submenu aligned with Akron's ImGui HUD style, make the row enter a visible pick mode, add report placement/detail defaults, keep fixed report corners anchored across size changes, and keep titlebar-collapsed reports reopenable.

## Akron Beta 27

### Added

- Add mouse control for Free Camera and an optional Cursor Tools hold bind with per-tool checkboxes for Click Teleport, Cursor Zoom, Free Camera, and Freeze gameplay. Cursor Tools mouse movement is enabled by default while its Free Camera option is active.

### Fixed

- Keep Madeline visible when Free Camera freezes gameplay or Cursor Tools enables Free Camera.
- Keep Cursor Tools from freezing gameplay unless its Freeze gameplay suboption is enabled, and allow its Click Teleport option to work without the normal Click Teleport hold bind.
- Keep Cursor Tools Click Teleport aligned while Cursor Zoom is active, including near clamped edge focus, and move Madeline through the normal movement path so her hair follows during frozen teleports.
- Keep Freeze Gameplay highlighted while active, keep Cursor Tools popup labels readable, prevent repeated click teleports from desyncing Madeline's hair animation, and keep Cursor Tools click teleports targeted at the clicked cursor position while Free Camera and Cursor Zoom are active.

## Akron Beta 26

### Added

- Add Akron invincibility mode with per-effect controls for bottomless rescue, crush collision changes, lava and ice pushback, and spike ground refills.

### Changed

- Add a Diagnostic logging level for playtesting, make it the default, and aggregate repeated policy checks and feature-use records so Trace remains available without losing useful logs so quickly.

### Fixed

- Fix Creator > Map Capture exports on Linux so room images render the map instead of solid black frames.
- Fix Creator > Map Capture scans stalling or under-rendering rooms after moving through a chapter.
- Skip non-playable filler rooms during Creator > Map Capture so custom maps can finish exporting.

## Akron Beta 25

### Fixed

- Show the full suboption name on hover when a popup label is shortened with ellipses.
- Restore Madeline's collider after room and map capture, including stopped scans, so capture cannot leave her hitbox enlarged or suppressed.

## Akron Beta 24

### Added

- Add directional Dash Redirect controls for preserving selected dash directions when Celeste would redirect them.
- Add Auto Kill area conditions for speed ranges, movement direction, current dash count, grounded/airborne state, and player state.
- Collapse Auto Kill's optional area conditions under a Conditions section so the popup stays focused on method and area selection by default.

### Fixed

- Add Extended Camera Dynamics external tool rows, route Cursor Zoom zoom-out through ECD when its hooks are active, and keep Akron from resetting ECD-owned zoom state when inactive.

## Akron Beta 23

### Fixed

- Keep overlay search responsive while playing a map by filtering on stable row labels and aliases instead of live row values.
- Hide backup folder paths in Backups > Last Result while Streamer Mode is enabled.
- Include the implicit Start checkpoint in Creator checkpoint navigation when a map has no checkpoint entity there.

## Akron Beta 22

### Fixed

- Rebuild FrostHelper spinner renderers after StartPos restores so stale cloned border images do not crash rendering.

## Akron Beta 21

### Fixed

- Detect bottom killboxes accurately for hazard contact and respawn behavior.
- Keep StartPos-restored audio state intact after loading saved positions.
- Suppress Akron render surfaces while SpeedrunTool owns state rendering.
- Repair Air Jumps option handling near hazard and edge cases.
- Persist player option changes correctly, including Frame Stepper and EVM-related policy behavior.

## Akron Beta 20

### Changed

- Remove the duplicate Restart Level shortcut so Reload Chapter is the single chapter restart action.
- Add Viridity to special thanks.

### Fixed

- Persist Akron overlay option changes when toggles, numeric inputs, selector dropdowns, or overlay close events update settings.
- Hide Celeste's bottom-right save/load icon when Hide Saving Icon is enabled.

## Akron Beta 19

### Fixed

- Render percent signs literally in tooltip descriptions, including Bloom Level's 0% and 100% text
- Keep Click Teleport from snapping the camera back when Free Camera is active
- Fully hide the saving icon when Hide Saving Icon is enabled or game-frame capture is active

## Akron Beta 18

### Removed

- Remove Akron profiles, rulesets, built-in preset modes, and related profile/ruleset archives, manifests, commands, and docs

### Fixed

- Keep the gameplay debug pass from running while gameplay is idle so the Akron overlay stays available outside active level play
- Keep auto kill, auto deafen, and other world-space area overlays aligned with gameplay positions
- Preserve FrostHelper persisted state when restoring StartPos or savestates
- Skip non-restorable static members during native state restore
- Persist modified Akron profile settings across game restarts

## Akron Beta 17

### Fixed

- Prevent Deload Spinners from adding simulated frames to level, session, or journal time.
- Make Deload Spinners a one-shot action so stale settings and setup imports cannot replay the simulation after restart.
- Keep Mintlify's generated Open Graph previews working by using an SVG docs logo asset instead of the PNG logo path that produced empty generated preview images.

## Akron Beta 16

### Fixed

- Keep the Akron overlay visible during death-related flows.
- Show Logging as a true On/Off toggle in the overlay.
- Fix zoom drift for auto kill, auto deafen, and hitbox overlays.
- Reload the latest loaded StartPos on death and preserve active runtime audio/state.

## Akron Beta 15

### Added

- Add Symbiote, Carbon, Retro, Coniferous, and Wine overlay theme presets.

### Changed

- Let Mintlify generate page-specific Open Graph previews for the docs site instead of using one static logo image.

### Fixed

- Prevent active Control Display key editor fields from copying into another key when selecting a different key before blurring the input.
- Prevent Akron's overlay hotkey from opening over Everest's Enable or Disable Mods menu, where Tab favorites or unfavorites mods.

## Akron Beta 14

### Added

- Add local Akron diagnostic logging under the Interface tab, including log level, warning mirroring, file rotation, retained files, and a test entry action.

### Changed

- Reclassify Motion Smoothing FPS Bypass as regular clean while keeping TPS Bypass, object interpolation, TAS mode, and Nasty mode marked as Cheat.

### Fixed

- Keep the overlay Search textbox focused while backspacing from narrow queries into broader result sets.

## Akron Beta 13

### Fixed

- Let the Open Menu key cancel hidden Auto Kill and Auto Deafen area selection and reopen Akron, so players are not stuck in a frozen selection mode.
- Restore Akron-managed cursor visibility after StartPos placement, Auto Kill area selection, and Auto Deafen area selection ends.

## Akron Beta 12

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

## Akron Beta 11

- Add Spawn Jelly, Spawn Theo, Set Inventory, Dream State, and Core Mode overlay actions.
- Add Previous Map, Next Map, Previous Checkpoint, and Next Checkpoint creator navigation actions.
- Put Spawn Jelly and Spawn Theo in Shortcuts as regular action buttons instead of triangle option rows.
- Add Set Inventory dash and jump configuration, setup persistence, console controls, and optional death restore behavior.
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

## Akron Beta 10

- Render hover help popups on ImGui's tooltip layer so they stay visible above overlay rows.

## Akron Beta 9

- Keep legacy shortcut bindings from reappearing after startup normalization.
- Preserve right-side modifier keys when capturing menu bindings, including modifier-only binds such as `RightAlt` and `RightShift`.
- Keep Open Menu defaulted to `Tab` while allowing users to rebind it to another valid key.

## Akron Beta 8

- Keep only Open Menu, Click Teleport Cursor, and Cursor Zoom Hold bound by default.
- Keep Open Menu user-customizable while restoring Tab only for missing or empty menu bindings.

## Akron Beta 7

- Keep Tab opening Akron's menu even when stale or custom menu bindings no longer include Tab.

## Akron Beta 6

- Group the Sound tab's per-sound volume rows under collapsed Player, Objects, Entities, Ambience, and UI headers.
- Keep core Sound controls visible above the new groups.
- Reveal relevant Sound groups and children while searching, including group-name and individual sound matches.

## Akron Beta 5

- Test the automated release path with GameBanana API authentication before the upload form.

## Akron Beta 4

- Test the automated release path after tightening GameBanana login field selection.

## Akron Beta 3

- Test the automated release path after cleaning release-conflict artifacts.

## Akron Beta 2

- Test the automated release path after switching GameBanana publishing to the direct edit form.

## Akron Beta 1

- Test the automated release path for GitHub, GameBanana, README links, and website links.

## Akron Beta

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
- Public docs cover installation, first run, overlay use, feature policy, `.akr` archives, troubleshooting, and contributor workflow.
