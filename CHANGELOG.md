# Changelog

All notable user-facing changes to Akron should be recorded here.

This project uses version tags that match the mod version in `everest.yaml`, while release headings can use readable public names such as `Akron Beta 42`. Keep release notes focused on player-visible behavior, public docs, packaging, `.akr` file contracts, and migration notes when they matter.

## Unreleased

### Changed

- Auto Kill's timer mode measures the current attempt instead of cumulative chapter time, fires at most once per attempt, and re-arms on the next one. Enabling it in a chapter already past the threshold no longer kills on every respawn. Clear Areas now only clears this map's areas: it no longer switches the Method to Timer or leaves Auto Kill armed. The Auto Kill toast is raised only when the death actually happens, so Celeste's own Assist invincibility no longer produces a message with nothing dying (#164).
- Auto Kill and Auto Deafen areas belong to the map they were drawn on, so a set drawn for one chapter no longer fires in another chapter whose rooms cover the same coordinates. Areas from earlier builds carry no map and are dropped rather than migrated. Setup packs move to `akron-setup-v9`; packs from earlier builds must be exported again (#175).
- Recorder setup packs accept every value the recorder itself accepts, up to 360 FPS, 1000 Mbps, 15360x8640 and a 20-second keyframe interval, so an exported pack always imports again (#173).
- Akron writes a proof sidecar on every area completion, whatever the attempt's classification and whether or not a proof helper is on. The proof panel stays a helper surface and appears on the same terms as before (#174).
- Submission Mode is a mode you can leave: enabling it remembers what Proof-mode Overlay, Proof Recorder Guard, End Screen Helper, Pause Tracker and Map Version Stamp were, and disabling it puts those five values back (#174).

### Fixed

- Loading a Speedrun Tool savestate no longer hitches because of Akron. Speedrun Tool deep-cloned Akron's overlay and Akron's per-profile statistics on every save and load, and the statistics grow with every room ever played, so the load frame got slower the longer Akron had been in use. Both now stay live, which also means a savestate no longer rewinds room stats or StartPos metadata (#153).
- The Core Mode override covers the room it was applied in. It ends when the room changes or a StartPos slot is restored, so turning it off cannot write back a core mode captured somewhere else, and the row can no longer read On with nothing to restore after a restart. Cycle click behavior now cycles Hot, Cold, and off, so the row can turn the override off (#171).
- Proof sidecars are valid JSON again. Removing the unsafe restore override in the previous release left a trailing comma in the active-feature block, so every sidecar written since then failed to parse (#174).
- The Submission Mode recorder warning arms again whenever the recorder does, so disarming the recorder later in the same level warns again (#174).
- Proof sidecar names carry milliseconds, so two sidecars written in the same second no longer overwrite each other (#174).
- The proof sidecar's active-feature list includes Disable Playback, No Stamina Flash, Air Jumps, Dash Redirect, and Grab Mode (#174).

## Akron Beta 77

### Changed

- Turn toasts on by default and improve attempt classifications, including ongoing HUD and overlay features.
- Add recording support for more actions and settings, including spawning, cutscene skips, captures, autosave, hitboxes, and sound overrides.
- Add clearer overlay controls for HUD labels, trails, input blocking, confirmation actions, theme blur, and Speedrun Tool slots.

### Fixed

- Restore settings, bindings, Assist flags, and recording state correctly after level ends, backups, imports, and game exit.
- Fix cutscene skipping, Neutral Drop input, pause tracking, warps, room captures, completion recordings, and room statistics.
- Fix keyboard chord bindings, panel collapse and search behavior, several option values, cheat indicators, and setup-pack error messages.
- Update the docs and tooltips to match the current overlay and classification behavior.

### Removed

- Remove unused overlay controls, best segment times, marked-room helpers, and Akron's unreachable native numbered-savestate code.
- Setup packs now use `akron-setup-v8` and theme packs use `akron-overlay-theme-v2`; older packs must be exported again.

## Akron Beta 76

### Changed

- Route StartPos, Auto Kill, and Auto Deafen community submissions through Akron's moderated Upload Pack flow, with Discord map forums reserved for approved showcases.

### Fixed

- Restore saved StartPos positions and ledge, idle, or hanging poses before the first frame, including slots created before pose refresh was added.
- Keep Celeste's collision-query scratch data out of StartPos snapshots, repair affected v10 snapshots, and continue warming later slots when one slot fails.
- Restore StartPos slots when mods renumber named player states or wrap distinct unnamed states in one helper callback after relaunching Celeste.
- Load large modded-room StartPos snapshots without counting short-lived diagnostic field paths against the retained-memory limit.
- Restore StartPos slots in rooms whose mods retain nested decals or lazily cache room entities in scene renderers.

## Akron Beta 75

### Fixed

- Restore StartPos slots that retain a delayed sound from their owning entity, such as a punched Kevin after leaving and re-entering the map.
- Give newly loaded StartPos placements Celeste's correct idle, ledge, or hanging pose before the first input.
- Prevent a brief hitch after loading a Speedrun Tool savestate while Akron is enabled.
- Build every saved StartPos into the native warm cache during the first disk-backed load, so later slot loads are instant when the machine can retain them.
- Release native StartPos graphs after leaving their map, so warming another map cannot exhaust memory while every active-map slot stays instant.
- Keep StartPos slots in modded rooms whose helpers replace shader IDs with registered live effects after rendering.

## Akron Beta 74

### Fixed

- Keep enabled cheat-class labels from marking an attempt as Cheat while the master label visibility switch is off.

## Akron Beta 73

### Changed

- Load and rewrite StartPos slots in substantially less memory, and write them smaller, without changing saved-state or slot compatibility.

### Fixed

- Refuse malformed StartPos snapshots that omit saved field values or delegate targets instead of accepting incomplete reconstruction data.
- Load StartPos slots set during hook-wrapped routines whose iterator captures a mod's singleton handler, including XaphanHelper's Lightning Dash.
- Set StartPos slots in rooms where a mod attached member caches to an entity. The cache's compiled accessors are process state, so every Set in the room was removed over them; the saved state now rebinds to this install's own cache.
- Load StartPos slots saved while a modded dash routine runs. Hook wrappers without an owner and Everest's own coroutine plumbing now restore on their position in the routine's stack.
- Load StartPos slots set after skipping or finishing a cutscene. Celeste keeps the skip callback forever, so every later slot dragged the finished cutscene entity along and was refused over it; the callback is now dropped from the saved copy.
- Load StartPos slots set while an entity's routine is mid-flight with captured locals, such as riding a punched Kevin. The routine's closure and its callback now restore on the routine's own proof instead of being refused over evidence an idle room never has.

## Akron Beta 72

### Fixed

- Load StartPos slots saved near dust bunnies after leaving and re-entering the map.
- Restore StartPos slots in Celestial Resort clutter rooms.
- Restore StartPos slots with dust eyes on screen or lightning mid-flash.

## Akron Beta 71

### Changed

- **Set your StartPos slots again, and re-export any `.akr` setup packs you share.** The saved-state contract moves to `akron-reconstruction-v10` and the pack contract to `akron-setup-v7`; older slots and packs are refused rather than read.
- Store StartPos saved states in less than half the space and memory by writing each repeated type name once and dropping the format's own overhead. Measured on Summit and Farewell, saved states shrank 2.2x to 2.6x, so larger maps now fit under the 384 MiB per-state limit.

### Fixed

- Set StartPos slots on maps whose objects hold weak references, such as Spring Collab 2020's Heart of the Storm, instead of refusing every capture over the handle inside.
- Recognize the overworld atlas when a saved room reaches it through menu callbacks, instead of refusing the slot over a texture every install already has.
- Load large StartPos saved states from disk again. The reader's structure ceilings sat below what real maps produce, so a Summit slot saved fine and then refused every load after leaving the level; the ceilings now derive from the 384 MiB size limit, and a state too large to ever load back fails on the Set that writes it.

### Added

- Add `akron_qa_snapshot_report`, which measures every StartPos saved state on disk in the background and logs its size, structure counts against the read limits, and where the bytes go.

## Akron Beta 70

### Added

- Add a Disable Playback toggle for room demonstration ghosts.
- Read other StartPos slots in the background and report cache and restart-copy status.
- Add a Defer Engine GC option that moves Celeste's forced collections from death to the next StartPos load.

### Changed

- Raise the StartPos read-ahead budget to 512 MB and the single saved-state limit to 384 MiB.
- Move the saved-state and `.akr` setup-pack contracts to `akron-reconstruction-v9` and `akron-setup-v6`.
- Remove unused performance-report counters and make Diagnostic logging quieter than Verbose logging.
- Report the mod or map entity responsible when a StartPos cannot be rebuilt.

### Fixed

- Keep oversized StartPos captures from being repeated.
- Keep performance recordings from including stale frames or losing their final frames.
- Keep incomplete backups from being treated as restorable and clean up deleted profiles' StartPos data.
- Keep failed StartPos replacements and restores from losing existing data.
- Refuse StartPos loads that contain unreadable, mismatched, or unplaceable room objects.
- Keep StartPos loads working with chapter startup state, shared callbacks, renamed states, collections, and loaded artwork.
- Keep the automation queue alive across StartPos loads and remove obsolete snapshot files.
- Refuse StartPos saves that contain native memory addresses.

## Akron Beta 69

### Fixed

- Restore StartPos slots across rooms and after leaving or restarting a map, including positions set during room wipes in large custom maps such as Heart of the Storm.
- Restore StartPos in rooms with groups of linked mod entities, such as Spring Collab 2020's Ancient Engine.
- Keep loading a StartPos from re-enabling **Respawn at StartPos** after the player turns it off.

## Akron Beta 68

### Fixed

- Fix StartPos loads that could crash Spring Collab rooms or fail after a camera mod resized gameplay buffers.

## Akron Beta 67

### Added

- Add a **Wait for input after load** StartPos option that pauses gameplay until a fresh input, while backdrops and respawn wipes keep moving.

## Akron Beta 66

### Changed

- Recreate existing StartPos slots and setup packs for the v7 snapshot and v4 setup contracts.

### Fixed

- Restore berry collection progress when loading a StartPos, including after restarting Celeste.
- Keep each chapter's StartPos slots usable after switching chapters and setting the same slots elsewhere.

## Akron Beta 65

### Changed

- Keep StartPos Set and same-session Load on the fast in-memory path, then cache the first disk restore so later Loads stay fast.

### Fixed

- Restore stopped custom-map sounds from StartPos disk snapshots and keep later live sound changes current for subsequent Sets.
- Prevent replaced StartPos data, failed room baselines, and queued shutdown work from leaving stale or incomplete restart copies.
- Keep StartPos setup imports consistent when they replace a still-saving slot or a helper mod fails its cleanup callback.
- Block StartPos setup exports and uploads until every included restart copy has finished saving.
- Keep unfinished StartPos restart copies bound to the save file that created them.
- Reuse the true fresh-room baseline when a new StartPos follows a warm cross-room Load.
- Fall back to the restart-safe StartPos after leaving and re-entering a chapter invalidates its warm copy.
- Refresh the fresh-room baseline after normal reloads so StartPos uses current save progression.
- Match warm and restart room initialization without pausing Akron input for the full entry wipe.
- Keep permanent save progress and other mods' save data current during warm StartPos Loads.
- Keep helper-owned sounds dormant and releasable while Akron holds a StartPos snapshot or fresh-room baseline.
- Release replaced StartPos graphics after their background restart copy finishes without dropping graphics still owned by another slot.
- Keep failed or unfinished StartPos replacements from loading an older disk snapshot, and reject stale warm copies before helper load callbacks run.

## Akron Beta 64

### Changed

- Store dependency-free StartPos v6 snapshots on disk so exact room state survives closing and restarting Celeste, including active custom-map gameplay.
- Carry each exact StartPos v6 room snapshot inside setup and community packs instead of exporting coordinates alone.
- Use the `akron-setup-v3` pack contract. Akron rejects older `akron-setup-v2` local and community packs instead of importing partial room state.

### Fixed

- Restore the exact StartPos frame both in-process and after a full restart, including room objects, positions, speeds, registered helper state, audio, random state, and gameplay render buffers, before simulation continues.
- Keep later StartPos slots available after loading an earlier slot, including custom-map runtime effects and generated room state.
- Keep global save progress with the active file and bind imported StartPos snapshots to the recipient's save slot.
- Show the actual death position when no hazard overlaps the player instead of choosing the nearest spike or death zone.

## Akron Beta 63

### Fixed

- Show the seeker collision that killed the player instead of a nearby spinner in Show Hitboxes On Death.

## Akron Beta 62

### Fixed

- End numeric-field editing when the overlay closes so fields reopen unfocused and held gameplay keys are not typed into them.

## Akron Beta 61

### Fixed

- Keep native spinner and spike geometry in death hitboxes unless Fix Hitbox Pixels is enabled.
- Keep Everest's debug console closed while typing `.` in an Akron text field.
- Stop the Backups panel from reopening every backup ZIP on each rendered frame.

## Akron Beta 60

### Fixed

- Open the normal binding menu by right-clicking an actionable popup option instead of showing a separate binding list.
- Keep Everest's debug console closed when `.` is being captured or already belongs to an Akron binding.
- Keep every overlapping part of a spinner hitbox visible after death when All hitboxes is off.

## Akron Beta 59

### Added

- Bind actions inside option popups, including Frame Stepper's Step Once action.

### Fixed

- Give Akron temporary ownership of the period key when an Akron action uses it, so Everest does not also toggle debug hitboxes.
- Recenter positional sound effects on the restored StartPos room.
- Show Auto Kill areas with death hitboxes even when live area display is off.
- Preserve spinner collider shapes when only the death-object hitbox is shown.
- Draw Show Triggers outlines at 5 pixels.

## Akron Beta 58

### Added

- Route music and sound effects to separate output devices with Audio Splitter.

### Changed

- Group overlay features by task, with Frame Stepper under Global controls and Control Display under Player HUD controls.
- Keep the configured Timescale value separate from whether Timescale is enabled.
- Keep the configured Transition Speed value separate from whether Transition Speed is enabled.
- Clarify the Dream State and Golden Start tooltips.

### Fixed

- Keep the stable website download fallback synced to the latest GameBanana file after releases.
- Restore tooltip and search copy for Previous Room In Order and Next Room In Order.
- Keep Audio Splitter enabled while Celeste's audio system and music bus finish loading.
- Keep dropdowns and color pickers open when clicked more than once inside an options submenu.
- Show only the recorded death-object hitbox when All hitboxes is off.
- Gray out Timescale and Frame Stepper when no save session is active.
- Give submenu labels more room before shortening them with an ellipsis.

## Akron Beta 57

### Changed

- Use the configured overlay accent color for every enabled toggle, including multi-mode options.

### Fixed

- Prevent duplicate Auto Kill condition controls from triggering Dear ImGui ID conflicts.
- Limit Deload Spinners to one simulation per level to prevent repeated-use crashes.
- Simplify Show Hitboxes options and use a 5-pixel default trajectory thickness.

## Akron Beta 56

### Fixed

- Keep Refill Clarity within the refill's original sprite bounds while preserving Celeste's black outline.

## Akron Beta 55

### Changed

- License Akron-owned material under CC BY-NC-ND 4.0 and include the license and complete third-party notices in player packages.

### Fixed

- Keep Refill Clarity outlines complete for atlas-trimmed and custom refill sprites.

## Akron Beta 54

### Fixed

- Make captured overlay bindings activate immediately for every actionable option.
- Keep death hitboxes correctly aligned while the screen wipe covers them.

## Akron Beta 53

### Added

- Enlarge Community Pack previews in an in-game lightbox and open a pack's source Discord thread from its detail pane.

## Akron Beta 52

### Fixed

- Show Community Pack previews and Discord author avatars, close the Community Packs window with the Akron overlay, and report import progress and completion.

## Akron Beta 51

### Fixed

- Keep custom Control Display boards valid when importing or editing them, and avoid rebuilding the board every frame.

## Akron Beta 50

### Fixed

- Add Lag Pauser recovery grace after respawns and room transitions, plus a repeat cooldown after automatic pauses.

## Akron Beta 49

### Security

- Harden Community Pack archives, catalogs, previews, uploads, and setup imports against malformed or oversized input.
- Bound screenshot, map-stitching, automation, network, and local-file resource use.
- Add verified release artifacts with checksums, an SBOM, and provenance attestations.

### Fixed

- Preserve machine-local Auto Deafen bindings and StartPos slots belonging to other maps when importing shared setups.
- Keep imported StartPos room-state snapshots immediately available after import.
- Make Community Pack uploads and catalog publication recover safely from interrupted or concurrent operations.

## Akron Beta 48

### Fixed

- Keep StartPos deaths on Celeste's normal death animation and wipe before restoring across room changes, stop saved positional sounds from lingering after room transitions, and make Transition Speed values below 1x actually slow transitions.

## Akron Beta 47

### Fixed

- Use localized map names, such as Forsaken City, in generated Upload Pack titles.
- Show every submitted image in Discord moderator reviews and published galleries, with StartPos uploads beginning at Slot 1 and gallery navigation and download controls.

## Akron Beta 46

### Fixed

- Let Upload Pack submit its generated marked-room capture payload, keep edited upload text scoped to the current map, and make overlay binding capture update Celeste's native key bindings.

## Akron Beta 45

### Added

- Add an Only Marked Rooms map-capture option and use marked-room previews for Community Pack uploads, with multiple catalog images shown as an in-game carousel.

## Akron Beta 44

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
- Verify the Backups overlay and manual backup creation in a live Celeste/Everest session, including ZIP readability and metadata contents.

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
