#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";

const repoRoot = process.cwd();
const sourceDir = path.join(repoRoot, "Source");
const overlayDir = path.join(sourceDir, "Overlay");
const registryPath = path.join(sourceDir, "Core", "AkronFeatureRegistry.cs");
const earAidPath = path.join(sourceDir, "Runtime", "akron-ear-aid.cs");
const outputDir = path.join(repoRoot, "docs", "feature-guide");

const overlaySource = fs
  .readdirSync(overlayDir)
  .filter((file) => file === "AkronOverlay.cs" || /^akron-overlay-.*\.cs$/.test(file))
  .sort()
  .map((file) => fs.readFileSync(path.join(overlayDir, file), "utf8"))
  .join("\n");
const registrySource = fs.readFileSync(registryPath, "utf8");
const earAidSource = fs.readFileSync(earAidPath, "utf8");

const statusNames = {
  GoldberryHardlistClean: "Goldberry/Hardlist clear",
  RegularClean: "Normal clear",
  Cheat: "Cheat"
};

const baseTabs = [
  "Global",
  "Level",
  "StartPos",
  "Bypass",
  "Keybinds",
  "Player",
  "Sound",
  "Creator",
  "Interface",
  "Labels",
  "Shortcuts",
  "Internal Recorder"
];

const externalToolTabs = [
  "Motion Smoothing",
  "Speedrun Tool",
  "CelesteTAS",
  "Extended Variant Mode"
];

const tabMeta = {
  Global: {
    slug: "global",
    description: "Session-wide controls for speed, safety, submission metadata, autosave, and transition behavior.",
    intro: "The Global tab collects options that affect the whole current Akron session rather than one HUD surface or one room tool."
  },
  Level: {
    slug: "level",
    description: "Level-scoped tools for automation, freezing, proof helpers, visual suppression, and hitbox display.",
    intro: "The Level tab focuses on the current level scene. Several rows are useful for routing and review, but state-changing rows can mark the attempt as Cheat."
  },
  StartPos: {
    slug: "startpos",
    description: "Capture, restore, place, cycle, and respawn from Akron StartPos slots.",
    intro: "The StartPos tab is Akron's public position and room-state restore workflow. These rows are meant for room-lab routing and are Cheat when used in submitted clean attempts."
  },
  Bypass: {
    slug: "bypass",
    description: "Save and completion mutation actions for unlocks, completion state, and berry obtain flows.",
    intro: "The Bypass tab contains explicit save or completion mutation tools. Treat this tab as local testing and recovery tooling, not clean-run setup."
  },
  Keybinds: {
    slug: "keybinds",
    description: "Review Akron overlay bindings and the built-in action bindings exposed by the current build.",
    intro: "The Keybinds tab shows binding rows. Runtime rows can also appear for actions that have custom bindings, but the stable built-in rows are listed here."
  },
  Player: {
    slug: "player",
    description: "Player movement, resource, presentation, assist, invincibility, noclip, trail, and comfort controls in Akron.",
    intro: "The Player tab covers Madeline-facing controls. Visual-only rows are generally presentation settings; movement, resource, teleport, and input-assist rows are Cheat."
  },
  Sound: {
    slug: "sound",
    description: "Audio routing, low-volume handling, speed, pitch, and per-sound volume overrides.",
    intro: "The Sound tab changes audio presentation. Per-sound rows come from Akron's current sound override list."
  },
  Creator: {
    slug: "creator",
    description: "Map inspection, camera, capture, room warp, and export tools for creators and QA.",
    intro: "The Creator tab is for map inspection and documentation work. Warps, independent camera tools, entity data, and hitbox-style inspection can mark attempts as Cheat."
  },
  Interface: {
    slug: "interface",
    description: "Overlay appearance, Streamer Mode, `.akr` pack import/export, community packs, and search.",
    intro: "The Interface tab controls the Akron overlay itself and the public `.akr` pack workflows."
  },
  Labels: {
    slug: "labels",
    description: "HUD label visibility, room labels, status labels, input panels, counters, and custom labels.",
    intro: "The Labels tab controls label-style HUD surfaces. It includes a master visibility gate plus individual rows that keep their own enabled state."
  },
  Shortcuts: {
    slug: "shortcuts",
    description: "One-shot actions for options, retry, reloads, input-assist shortcuts, and cutscene skipping.",
    intro: "The Shortcuts tab exposes action rows that are useful during play. Reloads and input-assist actions have different policy effects, so check the status column before using them in a submission context."
  },
  "Internal Recorder": {
    slug: "internal-recorder",
    description: "Recording, replay buffer, completion clips, proof helpers, encoder settings, output, audio, and presets.",
    intro: "The Internal Recorder tab configures Akron's local FFmpeg-backed recording and proof helper surfaces."
  },
  "External tools": {
    slug: "external-tools",
    description: "Options that appear only when Motion Smoothing, Speedrun Tool, CelesteTAS, or Extended Variant Mode is loaded.",
    intro: "External tool rows are not part of Akron's base overlay tabs. They appear only when the relevant mod is loaded, and they are documented separately so the main feature pages stay focused on Akron's own options."
  }
};

const controlNames = {
  Action: "Action",
  InlineNumericToggle: "Numeric toggle",
  NumericRow: "Number",
  NumericToggle: "Numeric toggle",
  PolicyToggle: "Toggle",
  SearchInput: "Text input",
  Selector: "Selector",
  SelectorDropdown: "Selector",
  Toggle: "Toggle",
  KeybindOverview: "Keybind"
};

const manualDescriptions = {
  "Air Jumps": "Open controls for extra airborne jumps.",
  "Bloom Level": "Adjust bloom presentation for the current level.",
  "Broken Window": "Override volume for the matching Celeste sound event.",
  "Capture StartPos State": "Save the current room, player state, session state, and Akron metadata into the active StartPos slot.",
  "Configured TAS File": "Show the configured TAS file basename used by Akron's CelesteTAS handoff.",
  "Core Block": "Override volume for the matching Celeste sound event.",
  "Community Packs": "Search map-linked community `.akr` packs from the configured community pack index.",
  "Dash Bar": "Show a dash-resource display.",
  "Dash Number": "Show the current dash count as a number.",
  "Death": "Override volume for the matching Celeste sound event.",
  "Dream Block": "Override volume for the matching Celeste sound event.",
  "Fireball": "Override volume for the matching Celeste sound event.",
  "Extended Variants Master": "Toggle Extended Variant Mode's master switch when the mod is loaded.",
  "Extended Variants Randomizer": "Open Extended Variant Mode randomizer controls when the mod is loaded.",
  "Export Profile": "Export the selected Whole, StartPos, Keybinds, Auto Kill, Auto Deafen, Recorder, Audio, or HUD profile scope as a `.akr` profile pack.",
  "Export Room Times": "Export room-time data when the optional room timer source is available.",
  "Hide Heat Distortion": "Hide heat-distortion presentation.",
  "Hide Snow": "Hide snow presentation.",
  "Hide Tentacles": "Hide tentacle presentation.",
  "Hide Waterfalls": "Hide waterfall presentation.",
  "Hide Wind Snow": "Hide wind-snow presentation.",
  "In-game only": "Restrict Akron menu bindings so they only fire while a level is active.",
  "Import Profile": "Open a file browser to import a selected `.akr` profile pack using the selected profile scope.",
  "Light Level": "Adjust light presentation for the current level.",
  "No Anxiety": "Suppress anxiety-style visual effects.",
  "No Distortion": "Suppress distortion-style visual effects.",
  "No Glitch": "Suppress glitch-style visual effects.",
  "No Particles": "Suppress particle presentation.",
  "Player Overlap": "Fade or move HUD labels when Madeline overlaps them.",
  "Play Configured TAS": "Start CelesteTAS playback for Akron's configured TAS file path.",
  "Reduced Visual Noise": "Apply Akron's reduced visual-noise presentation settings.",
  "Screen Tint": "Apply a configurable screen tint.",
  "Show Hitbox Trail": "Draw recent player collision boxes behind Madeline.",
  "Spring": "Override volume for the matching Celeste sound event.",
  "SRT Capture State": "Capture the active Speedrun Tool state slot.",
  "SRT Clear State": "Clear the active Speedrun Tool state slot.",
  "SRT Restore State": "Restore the active Speedrun Tool state slot.",
  "SRT Room Time": "Show Speedrun Tool's current room timer value when its room-timer export is available.",
  "SRT Slot": "Show the active Akron slot used for Speedrun Tool state capture and restore actions.",
  "SRT Status": "Show whether Akron can read Speedrun Tool status, slot state, and room time right now.",
  "StartPos Clear": "Clear the active StartPos slot.",
  "StartPos Load": "Load the active StartPos slot.",
  "StartPos Next": "Move to the next saved StartPos in chapter order.",
  "StartPos Previous": "Move to the previous saved StartPos in chapter order.",
  "StartPos Set": "Capture the current position and room state into the active StartPos slot.",
  "StartPos Slot 1-9": "Show bindings for loading fixed StartPos slots.",
  "Stamina Bar": "Show a stamina-resource display.",
  "Status": "Show compact HUD labels for the active profile, ruleset stack, and attempt status."
};

const featureKindOverrides = {
  "Timescale": "Timescale"
};

const statusOverrides = {
  "Capture StartPos State": "Cheat",
  "Cursor Zoom": "Cheat",
  "In-game only": "Normal clear",
  "Open Menu": "Goldberry/Hardlist clear",
  "Player Overlap": "Normal clear",
  "Restore StartPos State": "Cheat",
  "Show Hitboxes": "Cheat",
  "StartPos Clear": "Cheat",
  "StartPos Load": "Cheat",
  "StartPos Next": "Cheat",
  "StartPos Previous": "Cheat",
  "StartPos Set": "Cheat",
  "StartPos Slot 1-9": "Cheat"
};

const optionMenus = {
  "Timescale": [
    ["Enabled", "Toggle gameplay timescale on or off.", "Cheat"],
    ["Decrease", "Lower the gameplay timescale multiplier.", "Cheat"],
    ["Increase", "Raise the gameplay timescale multiplier.", "Cheat"],
    ["Reset", "Return gameplay speed to normal.", "Cheat"]
  ],
  "Safe Mode": [
    ["Freeze deaths", "Restore the current save slot's death counter while Safe Mode is active.", "Cheat"],
    ["Freeze jumps", "Restore the current save slot's jump counter while Safe Mode is active.", "Cheat"],
    ["Freeze best run", "Restore the current save slot's best-run fields while Safe Mode is active.", "Cheat"]
  ],
  "Control Display": [
    ["Corner", "Choose the screen corner used by the input board.", "Goldberry/Hardlist clear"],
    ["Source", "Choose whether keys light from Celeste actions or physical keyboard keys.", "Goldberry/Hardlist clear"],
    ["Scale", "Set the input-board scale percentage.", "Goldberry/Hardlist clear"],
    ["Opacity", "Set the input-board opacity percentage.", "Goldberry/Hardlist clear"],
    ["Compact keyboard", "Replace the layout with a tight WASD-style block with actions attached.", "Goldberry/Hardlist clear"],
    ["Split clusters", "Replace the layout with separated menu/action and direction clusters.", "Goldberry/Hardlist clear"],
    ["Save Full .akr", "Export the complete Control Display setup, including layout, labels, keys, colors, and text scale.", "Goldberry/Hardlist clear"],
    ["Import Latest .akr", "Import the newest full Control Display preset from Saves/AkronControlDisplay.", "Goldberry/Hardlist clear"],
    ["Label preset: Names", "Apply action-name labels to the existing board keys without moving them.", "Goldberry/Hardlist clear"],
    ["Label preset: Keyboard", "Apply keyboard-style labels to the existing board keys without moving them.", "Goldberry/Hardlist clear"],
    ["Label preset: Arrows", "Apply arrow-style labels to the existing board keys without moving them.", "Goldberry/Hardlist clear"],
    ["Label preset: Short", "Apply compact labels to the existing board keys without moving them.", "Goldberry/Hardlist clear"],
    ["Add key", "Add a new visible key to the editable input board layout.", "Goldberry/Hardlist clear"],
    ["Duplicate", "Duplicate the selected input-board key.", "Goldberry/Hardlist clear"],
    ["Remove", "Remove the selected input-board key when more than one key remains.", "Goldberry/Hardlist clear"],
    ["Selected", "Choose which input-board key is being edited.", "Goldberry/Hardlist clear"],
    ["Label", "Set the text shown inside the selected input-board key.", "Goldberry/Hardlist clear"],
    ["Visible", "Show or hide the selected input-board key.", "Goldberry/Hardlist clear"],
    ["X", "Set the selected key's horizontal board position.", "Goldberry/Hardlist clear"],
    ["Y", "Set the selected key's vertical board position.", "Goldberry/Hardlist clear"],
    ["Width", "Set the selected key's width.", "Goldberry/Hardlist clear"],
    ["Height", "Set the selected key's height.", "Goldberry/Hardlist clear"],
    ["Text scale", "Set the selected key's label text scale.", "Goldberry/Hardlist clear"],
    ["Game action", "Choose the Celeste action used by the selected key when Source is Game Actions.", "Goldberry/Hardlist clear"],
    ["Keyboard keys", "Set the physical keys used by the selected key when Source is Keyboard Keys.", "Goldberry/Hardlist clear"],
    ["Fill", "Set the selected key's background color.", "Goldberry/Hardlist clear"],
    ["Pressed", "Set the selected key's held-input color.", "Goldberry/Hardlist clear"],
    ["Stroke", "Set the selected key's outline color.", "Goldberry/Hardlist clear"],
    ["Text", "Set the selected key's label color.", "Goldberry/Hardlist clear"],
    ["Outline", "Set the selected key's outline width.", "Goldberry/Hardlist clear"]
  ],
  "Autosave": [
    ["Backup behavior", "Configure when Akron-owned backup state is saved.", "Normal clear"]
  ],
  "Transition Speed": [
    ["Multiplier", "Adjust the next room-transition duration multiplier.", "Cheat"]
  ],
  "Auto Kill": [
    ["Map-time threshold", "Kill the attempt when current map time reaches the configured threshold.", "Cheat"],
    ["Selected areas", "Kill the attempt when Madeline enters selected rectangles.", "Cheat"],
    ["Draw areas", "Draw selected Auto Kill rectangles below the Akron menu.", "Cheat"],
    ["Clear areas", "Remove all selected Auto Kill rectangles.", "Cheat"]
  ],
  "Auto Deafen": [
    ["Hotkey", "Set the Discord Toggle Deafen hotkey Akron will press.", "Normal clear"],
    ["Test hotkey", "Press the configured hotkey immediately.", "Normal clear"],
    ["Selected areas", "Trigger deafen when Madeline enters selected rectangles.", "Normal clear"],
    ["Draw areas", "Draw selected Auto Deafen rectangles below the Akron menu.", "Normal clear"],
    ["Clear areas", "Remove selected Auto Deafen rectangles and restore if Akron believes Discord is deafened.", "Normal clear"]
  ],
  "Respawn Time": [
    ["Seconds", "Configure the post-death respawn delay.", "Normal clear"],
    ["Real time", "Use real elapsed time so Akron timescale does not stretch or shrink the delay.", "Normal clear"]
  ],
  "Pause Timer": [
    ["Seconds", "Configure how long gameplay is held after unpausing.", "Cheat"],
    ["Remove pause darkening", "Remove Celeste's leftover pause darkening during the countdown.", "Cheat"]
  ],
  "Lag Pauser": [
    ["Threshold ms", "Set the frame-time spike threshold that triggers the pause.", "Goldberry/Hardlist clear"],
    ["Triggers", "Show how many frame-time spikes triggered Lag Pauser.", "Goldberry/Hardlist clear"],
    ["Last spike", "Show the duration of the most recent frame-time spike.", "Goldberry/Hardlist clear"],
    ["Reset threshold", "Restore the default spike threshold.", "Goldberry/Hardlist clear"]
  ],
  "Light Level": [
    ["Percent", "Set the lighting override percentage while Light Level is enabled.", "Normal clear"]
  ],
  "Bloom Level": [
    ["Percent", "Set bloom suppression or amplification while Bloom Level is enabled.", "Normal clear"]
  ],
  "Screen Tint": [
    ["Opacity", "Set screen tint opacity.", "Normal clear"],
    ["Tint", "Choose the screen tint color.", "Normal clear"]
  ],
  "Deload Spinners": [
    ["Seconds before deload", "Configure spinner deload simulation timing.", "Cheat"]
  ],
  "Show Hitboxes": [
    ["Active only", "Hide inactive entities from live hitbox rendering.", "Cheat"],
    ["Hide player", "Hide the player collider from live hitbox rendering.", "Cheat"],
    ["Hazards", "Draw spike, blade, and death-object hitboxes.", "Cheat"],
    ["Solids", "Draw solid collision boxes.", "Cheat"],
    ["Triggers", "Draw trigger areas.", "Cheat"],
    ["Sync Hitboxes", "Clear cached hitbox draw state and rebuild live hitboxes on the next frame.", "Cheat"],
    ["Show Hitbox Trail", "Draw recent player collision boxes behind Madeline.", "Cheat"],
    ["Length", "Set how many previous player hitbox positions are kept.", "Cheat"],
    ["Trail %", "Set maximum opacity for older hitbox trail samples.", "Cheat"],
    ["Line", "Set live hitbox outline thickness.", "Cheat"],
    ["Black outline", "Add a black contrast border behind colored hitbox lines.", "Cheat"],
    ["Fill %", "Set interior opacity for drawn hitboxes.", "Cheat"],
    ["Player", "Set Madeline/player hitbox color.", "Cheat"],
    ["Solids color", "Set solid collision grid color.", "Cheat"],
    ["Hazards color", "Set spike, blade, and death-object color.", "Cheat"],
    ["Triggers color", "Set trigger area color.", "Cheat"],
    ["Other", "Set fallback color for other collidable entities.", "Cheat"],
    ["Reset style", "Restore Akron's default hitbox style.", "Cheat"]
  ],
  "Show Hitbox Trail": [
    ["Length", "Set how many recent player collision boxes are kept.", "Cheat"],
    ["Opacity", "Set hitbox trail opacity.", "Cheat"]
  ],
  "Show Hitboxes On Death": [
    ["All hitboxes", "During the death window, draw the regular hitbox set using Show Hitboxes settings.", "Normal clear"],
    ["Player marker", "Mark Madeline's recorded death position even when all hitboxes or Hide player is on.", "Normal clear"],
    ["Death object", "Set the last recorded death-object hitbox color.", "Normal clear"],
    ["Player marker color", "Set the last recorded player-position marker color.", "Normal clear"]
  ],
  "Smart StartPos": [
    ["Smart StartPos mode", "Use nearby stored StartPos data automatically when respawning.", "Cheat"]
  ],
  "StartPos": [
    ["Set", "Capture the current position and room state into the active StartPos slot.", "Cheat"],
    ["Load", "Restore the active StartPos slot.", "Cheat"],
    ["Clear", "Clear the active StartPos slot.", "Cheat"],
    ["Previous", "Cycle to the previous saved StartPos in chapter order.", "Cheat"],
    ["Next", "Cycle to the next saved StartPos in chapter order.", "Cheat"],
    ["Load Slot 1-9", "Restore a fixed StartPos slot.", "Cheat"]
  ],
  "Place StartPos": [
    ["Placement mode", "Enter the frozen free-camera placement editor.", "Cheat"],
    ["Open editor", "Freeze gameplay, activate free camera, and place StartPos previews with the mouse.", "Cheat"],
    ["Slot count", "Set how many StartPos slots the selector and auto-advance cycle through.", "Cheat"],
    ["Dashes", "Keep the native dash count or force 0-5 dashes after spawning.", "Cheat"],
    ["Stamina %", "Keep native stamina or force a stamina percentage after spawning.", "Cheat"],
    ["Facing", "Keep native facing or force left/right after spawning.", "Cheat"],
    ["Idle speed", "Clear speed after spawning so the StartPos begins idle.", "Cheat"],
    ["Spawn grabbing", "Attempt to enter Celeste's climb/grab state after spawning.", "Cheat"],
    ["Preview opacity", "Set mouse placement preview opacity.", "Cheat"]
  ],
  "StartPos Switcher": [
    ["Previous", "Cycle to the previous StartPos in chapter order.", "Cheat"],
    ["Next", "Cycle to the next StartPos in chapter order.", "Cheat"],
    ["Bind Previous", "Set a menu binding for cycling to the previous StartPos.", "Cheat"],
    ["Bind Next", "Set a menu binding for cycling to the next StartPos.", "Cheat"],
    ["Clear Previous", "Clear the custom previous-StartPos binding.", "Cheat"],
    ["Clear Next", "Clear the custom next-StartPos binding.", "Cheat"]
  ],
  "StartPos HUD": [
    ["Color", "Set the text color for the StartPos label.", "Cheat"],
    ["Anchor", "Choose the screen position for the StartPos HUD label.", "Cheat"],
    ["Format", "Choose full, compact, or slot plus count label text.", "Cheat"],
    ["Offset X", "Move the StartPos label horizontally from its anchor.", "Cheat"],
    ["Offset Y", "Move the StartPos label vertically from its anchor.", "Cheat"],
    ["Scale", "Set the StartPos label text scale.", "Cheat"],
    ["Opacity", "Set the StartPos label opacity.", "Cheat"],
    ["Line spacing", "Set multiline spacing for the StartPos label.", "Cheat"],
    ["Shadow", "Draw a black outline plus offset shadow behind the StartPos label.", "Cheat"],
    ["Shadow opacity", "Set the StartPos label shadow opacity.", "Cheat"],
    ["Shadow X", "Set the StartPos label shadow horizontal offset.", "Cheat"],
    ["Shadow Y", "Set the StartPos label shadow vertical offset.", "Cheat"],
    ["Shadow color", "Set the StartPos label shadow color.", "Cheat"]
  ],
  "Berry Obtain Options": [
    ["Regular berries", "Include normal and winged red strawberries in obtain actions.", "Normal clear"],
    ["Golden berries", "Include golden berry entities and the 1A winged golden memorial berry.", "Normal clear"],
    ["Moon berry", "Include Farewell's moon berry when present.", "Normal clear"]
  ],
  "Air Jumps": [
    ["Infinite", "Allow every unhandled airborne jump press to trigger another normal jump.", "Cheat"],
    ["Extra jumps", "Configure a limited number of extra airborne jumps.", "Cheat"]
  ],
  "Grab Mode": [
    ["Hold", "Use Celeste's normal hold-to-grab behavior.", "Normal clear"],
    ["Toggle", "Toggle grab state from the configured control.", "Normal clear"],
    ["Invert", "Invert the grab input behavior.", "Normal clear"]
  ],
  "Noclip": [
    ["Speed", "Configure noclip movement speed.", "Cheat"],
    ["Grab speed", "Configure slower movement while grab is held.", "Cheat"],
    ["Draw on top", "Render Madeline above most objects while noclip is active.", "Cheat"],
    ["Hide player", "Hide the player sprite while noclip is active.", "Cheat"]
  ],
  "Hazard Accuracy": [
    ["Tint", "Tint the gameplay screen when Hazard Accuracy detects invalid contact.", "Cheat"],
    ["Reset", "Restore default tint settings.", "Cheat"]
  ],
  "Frame Stepper": [
    ["Freeze", "Toggle frozen gameplay.", "Cheat"],
    ["Step once", "Advance one frozen frame.", "Cheat"],
    ["Hold repeat", "Repeat frame stepping while the key is held.", "Cheat"],
    ["Delay", "Set the initial hold time before repeated frame steps begin.", "Cheat"],
    ["Interval", "Set time between repeated frame steps while the key is held.", "Cheat"]
  ],
  "Fast Lookout": [
    ["Multiplier", "Set lookout camera speed while the hold bind is pressed.", "Cheat"]
  ],
  "Golden Start": [
    ["Run Golden Start", "Run Celeste's give_golden helper when Akron detects the first-room start context.", "Goldberry/Hardlist clear"]
  ],
  "Golden Transparency": [
    ["Opacity", "Set golden berry and follower opacity.", "Normal clear"],
    ["Reset opacity", "Restore the default golden transparency opacity.", "Normal clear"]
  ],
  "Ground Refills": [
    ["Dash refill", "Allow ground contact to restore dash charges.", "Cheat"],
    ["Stamina refill", "Allow ground contact to restore stamina.", "Cheat"]
  ],
  "Click Teleport": [
    ["Cursor action", "Hold the cursor hotkey, then click to teleport to the in-room cursor position.", "Cheat"]
  ],
  "Show Trajectory": [
    ["Mode: Map-aware/Simple", "Choose simple prediction or map-aware truncation at selected map collisions.", "Cheat"],
    ["Stop on solids", "Stop map-aware prediction at solid collision.", "Cheat"],
    ["Stop on hazards", "Stop map-aware prediction at spikes or death objects.", "Cheat"],
    ["Frames", "Set preview length.", "Cheat"],
    ["Opacity", "Set trajectory opacity percentage.", "Cheat"],
    ["Thickness", "Set trajectory line thickness in screen pixels.", "Cheat"],
    ["Hitbox step", "Draw one predicted frame hitbox every N simulated frames.", "Cheat"],
    ["Path lines", "Draw the red and green trajectory path lines.", "Cheat"],
    ["Black shadow", "Draw black outlines/shadows behind trajectory lines, markers, and hitboxes.", "Cheat"],
    ["Point markers", "Draw small dots along each predicted path.", "Cheat"],
    ["Start marker", "Draw the square marker at the prediction start position.", "Cheat"],
    ["Frame hitboxes", "Draw player hitboxes at regular predicted frames along each trajectory.", "Cheat"],
    ["End hitboxes", "Draw predicted final player collider rectangles at the end of each path.", "Cheat"],
    ["Hitbox outlines", "Draw colored outlines around trajectory frame and end hitboxes.", "Cheat"],
    ["Hitbox fill", "Fill trajectory frame and end hitboxes with a low-opacity branch color.", "Cheat"],
    ["Use hitbox color", "Use Show Hitboxes' player color for trajectory end hitboxes.", "Cheat"],
    ["Jump held", "Set the path color for jump-pressed/held prediction.", "Cheat"],
    ["Jump released", "Set the path color for jump-released prediction.", "Cheat"],
    ["End hitbox", "Set the custom end-hitbox color when not inheriting Show Hitboxes' player color.", "Cheat"]
  ],
  "Stamina Bar": [
    ["Player bar", "Attach the small stamina bar to Madeline.", "Cheat"],
    ["Player", "Choose whether the player-attached stamina bar appears above or below Madeline.", "Cheat"],
    ["Player X", "Move the player-attached stamina bar horizontally.", "Cheat"],
    ["Player Y", "Move the player-attached stamina bar vertically.", "Cheat"],
    ["Player %", "Scale the player-attached stamina bar.", "Cheat"],
    ["HUD bar", "Show the large fixed-position stamina bar.", "Cheat"],
    ["HUD", "Choose the large stamina meter screen position.", "Cheat"],
    ["Style: Bar/Ring", "Choose rectangular bar or circular ring presentation.", "Cheat"],
    ["HUD X", "Move the fixed HUD stamina bar horizontally.", "Cheat"],
    ["HUD Y", "Move the fixed HUD stamina bar vertically.", "Cheat"],
    ["Low STA", "Set the stamina value where the bar switches to low-stamina color.", "Cheat"],
    ["Always visible", "Keep the stamina bar visible even when stamina is full.", "Cheat"],
    ["Danger marker", "Mark Celeste's native tired threshold at 20 stamina.", "Cheat"],
    ["Loss/refund pulse", "Show a trailing fill segment when stamina changes.", "Cheat"],
    ["Show overflow", "Let stamina above the vanilla maximum overflow the meter instead of clamping.", "Cheat"],
    ["Hide while paused", "Hide stamina bars while Celeste is paused.", "Cheat"],
    ["Normal", "Set the normal stamina color.", "Cheat"],
    ["Low", "Set the low-stamina color.", "Cheat"],
    ["Fill", "Set the stamina meter background fill color.", "Cheat"],
    ["Line", "Set the stamina meter outline and low-threshold marker color.", "Cheat"],
    ["Overflow", "Set the color for stamina above the vanilla maximum.", "Cheat"]
  ],
  "Dash Bar": [
    ["Player bar", "Attach the dash bar near Madeline.", "Cheat"],
    ["Player", "Choose the player-attached dash-bar placement relative to Madeline.", "Cheat"],
    ["Player X", "Move the player-attached dash bar horizontally.", "Cheat"],
    ["Player Y", "Move the player-attached dash bar vertically.", "Cheat"],
    ["Player %", "Scale the player-attached dash bar.", "Cheat"],
    ["HUD bar", "Show a fixed-position dash resource display.", "Cheat"],
    ["HUD", "Choose the fixed dash display screen position.", "Cheat"],
    ["Style: Pips/Bar", "Choose discrete dash pips or a segmented dash meter.", "Cheat"],
    ["HUD X", "Move the fixed HUD dash bar horizontally.", "Cheat"],
    ["HUD Y", "Move the fixed HUD dash bar vertically.", "Cheat"],
    ["Always visible", "Keep the dash display visible even when dashes are full.", "Cheat"],
    ["Show label", "Show the DASH label next to dash pips.", "Cheat"],
    ["Show depleted", "Show depleted dash slots instead of only filled charges.", "Cheat"],
    ["Hide while paused", "Hide dash bars while Celeste is paused.", "Cheat"],
    ["Filled", "Set the color for available dash charges.", "Cheat"],
    ["Empty", "Set the color for depleted dash charges.", "Cheat"],
    ["Fill", "Set the dash meter background fill color.", "Cheat"],
    ["Line", "Set the dash meter outline color.", "Cheat"],
    ["Text", "Set the dash label text color.", "Cheat"]
  ],
  "Dash Count": [
    ["On spawn", "Set current dashes to the configured maximum when Madeline spawns in a room.", "Cheat"],
    ["On transition", "Set current dashes to the configured maximum after room transitions.", "Cheat"],
    ["Maximum", "Set the forced maximum dash count used by Dash Count.", "Cheat"]
  ],
  "Dash Number": [
    ["Offset X", "Move the dash number horizontally near Madeline.", "Cheat"],
    ["Offset Y", "Move the dash number vertically near Madeline.", "Cheat"],
    ["Color", "Set the dash number text color.", "Cheat"]
  ],
  "Speed Number": [
    ["Mode: Total/Horizontal/Vertical", "Choose whether the number uses total, horizontal, or vertical speed.", "Cheat"],
    ["Offset X", "Move the speed number horizontally near Madeline.", "Cheat"],
    ["Offset Y", "Move the speed number vertically near Madeline.", "Cheat"],
    ["Color", "Set the speed number text color.", "Cheat"]
  ],
  "Madeline Colors": [
    ["No dash", "Customize depleted-dash blue hair.", "Normal clear"],
    ["One dash", "Customize normal dash-available hair.", "Normal clear"],
    ["Two dash", "Customize two-dash pink hair.", "Normal clear"],
    ["Three dash", "Customize hair when Madeline has three dashes.", "Normal clear"],
    ["Four dash", "Customize hair when Madeline has four dashes.", "Normal clear"],
    ["Five dash", "Customize hair when Madeline has five or more dashes.", "Normal clear"],
    ["Gradient", "Animate customized states between Gradient A and Gradient B.", "Normal clear"],
    ["Gradient A", "Set the gradient start color.", "Normal clear"],
    ["Gradient B", "Set the gradient end color.", "Normal clear"]
  ],
  "Madeline Hair Length": [
    ["No dash", "Set hair segment count when Madeline has no dash.", "Normal clear"],
    ["One dash", "Set hair segment count when Madeline has one dash.", "Normal clear"],
    ["Two dash", "Set hair segment count when Madeline has two dashes.", "Normal clear"],
    ["Three dash", "Set hair segment count when Madeline has three dashes.", "Normal clear"],
    ["Four dash", "Set hair segment count when Madeline has four dashes.", "Normal clear"],
    ["Five dash", "Set hair segment count when Madeline has five or more dashes.", "Normal clear"]
  ],
  "Madeline Effect Sync": [
    ["Dash particles", "Match dash burst particles to the active hair color.", "Normal clear"],
    ["Dash trail", "Match custom trail color to the active hair color.", "Normal clear"],
    ["Death effect", "Match the death burst effect to the active hair color.", "Normal clear"],
    ["Feather color", "Allow Madeline Colors to keep coloring feather-state hair.", "Normal clear"],
    ["Crown color", "Match compatible crown sprites to the active hair color.", "Normal clear"]
  ],
  "Custom Trail": [
    ["Mode: Fixed/Rainbow", "Choose fixed color or rainbow color cycling for custom trails.", "Normal clear"],
    ["Pulse", "Pulse custom trail brightness over time.", "Normal clear"],
    ["Cut rate", "Emit one custom trail every N frames.", "Normal clear"],
    ["Opacity", "Set custom trail opacity percentage.", "Normal clear"],
    ["Rainbow", "Set rainbow cycle speed.", "Normal clear"],
    ["Color", "Set the fixed custom trail color.", "Normal clear"]
  ],
  "Prevent Down Dash Redirects": [
    ["Normal", "Restore pure down when no horizontal input is held.", "Cheat"],
    ["Diagonal", "Preserve diagonal down redirects.", "Cheat"]
  ],
  "Audio Splitter": [
    ["Refresh devices", "Refresh FMOD output devices visible to Celeste.", "Normal clear"],
    ["Music", "Choose the output device for the music audio route.", "Normal clear"],
    ["SFX", "Choose the output device for the sound-effects audio route.", "Normal clear"],
    ["Ambience", "Choose the output device for the ambience audio route.", "Normal clear"],
    ["UI", "Choose the output device for the UI audio route.", "Normal clear"],
    ["Unclassified", "Choose the output device for sounds Akron cannot classify into another route.", "Normal clear"]
  ],
  "Allow Low Volume": [
    ["Music volume", "Set forced low music volume.", "Normal clear"],
    ["SFX volume", "Set forced low SFX volume.", "Normal clear"]
  ],
  "Audio Speed": [
    ["Policy: Normal/Sync/Independent", "Choose normal speed, Akron timescale sync, or an independent audio speed.", "Normal clear"],
    ["Speed", "Set the independent audio speed multiplier.", "Normal clear"]
  ],
  "Pitch Shift": [
    ["Policy: Preserve/Follow/Independent", "Choose normal pitch, pitch following audio speed, or an independent pitch.", "Normal clear"],
    ["Pitch", "Set the independent pitch multiplier.", "Normal clear"]
  ],
  "Free Camera": [
    ["Speed", "Set camera pan speed in world pixels per second.", "Cheat"],
    ["Freeze gameplay", "Pause the whole level while free camera is active.", "Cheat"]
  ],
  "Camera Offset": [
    ["Offset X", "Configure current-level horizontal camera offset.", "Cheat"],
    ["Offset Y", "Configure current-level vertical camera offset.", "Cheat"],
    ["Reset offset", "Return the configured offset to 0,0.", "Cheat"]
  ],
  "Cursor Zoom": [
    ["Zoom", "Set whole-screen level zoom percentage.", "Cheat"],
    ["Allow zoom out", "Allow zoom below the normal 100% view.", "Cheat"],
    ["Hold", "Apply zoom only while the Cursor Zoom bind is held.", "Cheat"],
    ["Toggle", "Press the Cursor Zoom bind to toggle the zoomed view on or off.", "Cheat"],
    ["Reset on deactivate", "Reset zoom to 100% when Hold is released or Toggle is turned off.", "Cheat"],
    ["Scroll step", "Set percent changed per mouse-wheel notch.", "Cheat"],
    ["Reset zoom", "Return the level camera to neutral 1.0x without changing whether Cursor Zoom is enabled.", "Cheat"]
  ],
  "Room Capture": [
    ["Capture Room", "Capture overlapping tiles across the current room.", "Goldberry/Hardlist clear"],
    ["Stop Scan", "Stop the active room capture scan.", "Goldberry/Hardlist clear"],
    ["Export path", "Set the export path relative to the Celeste install folder.", "Goldberry/Hardlist clear"],
    ["Format", "Choose the room capture image format.", "Goldberry/Hardlist clear"],
    ["Wait", "Set frames to wait before each tile capture.", "Goldberry/Hardlist clear"],
    ["Horizontal", "Set the horizontal tile offset between captures.", "Goldberry/Hardlist clear"],
    ["Vertical", "Set the vertical tile offset between captures.", "Goldberry/Hardlist clear"],
    ["Export markers", "Draw StartPos, Auto Kill, and Auto Deafen overlays into exported image tiles.", "Goldberry/Hardlist clear"],
    ["StartPos markers", "Include all saved StartPos slots for each captured room when Export markers is on.", "Goldberry/Hardlist clear"],
    ["Auto Kill areas", "Include configured Auto Kill areas when Export markers is on.", "Goldberry/Hardlist clear"],
    ["Auto Deafen areas", "Include configured Auto Deafen areas when Export markers is on.", "Goldberry/Hardlist clear"],
    ["Freeze timers", "Keep level timers pinned while capture camera positions settle.", "Cheat"],
    ["Noclip + hide Madeline", "During capture, put Madeline in a dummy non-collidable hidden state.", "Cheat"],
    ["Remove background", "Exclude background layers from captured tiles.", "Goldberry/Hardlist clear"],
    ["Remove foreground", "Exclude foreground layers from captured tiles.", "Goldberry/Hardlist clear"]
  ],
  "Map Capture": [
    ["Capture Map", "Capture each room in the current map.", "Goldberry/Hardlist clear"],
    ["Stop Scan", "Stop the active map capture scan.", "Goldberry/Hardlist clear"],
    ["Export path", "Set the export path relative to the Celeste install folder.", "Goldberry/Hardlist clear"],
    ["Format", "Choose the map capture image format.", "Goldberry/Hardlist clear"],
    ["Wait", "Set frames to wait before each tile capture.", "Goldberry/Hardlist clear"],
    ["Horizontal", "Set the horizontal tile offset between captures.", "Goldberry/Hardlist clear"],
    ["Vertical", "Set the vertical tile offset between captures.", "Goldberry/Hardlist clear"],
    ["Export markers", "Draw configured markers into exported image tiles.", "Goldberry/Hardlist clear"],
    ["StartPos markers", "Include all saved StartPos slots for each captured room when Export markers is on.", "Goldberry/Hardlist clear"],
    ["Auto Kill areas", "Include configured Auto Kill areas when Export markers is on.", "Goldberry/Hardlist clear"],
    ["Auto Deafen areas", "Include configured Auto Deafen areas when Export markers is on.", "Goldberry/Hardlist clear"],
    ["Freeze timers", "Keep level timers pinned while capture camera positions settle.", "Cheat"],
    ["Noclip + hide Madeline", "During capture, put Madeline in a dummy non-collidable hidden state.", "Cheat"],
    ["Remove background", "Exclude background layers from captured tiles.", "Goldberry/Hardlist clear"],
    ["Remove foreground", "Exclude foreground layers from captured tiles.", "Goldberry/Hardlist clear"]
  ],
  "Theme": [
    ["Theme", "Cycle Akron's built-in themes and the custom theme slot.", "Normal clear"],
    ["Copy to Custom", "Copy the current theme colors and presentation values into the editable Custom slot.", "Normal clear"],
    ["Export .akr", "Export the current overlay theme as a single-purpose `.akr` theme pack.", "Normal clear"],
    ["Import Latest .akr", "Import the newest `.akr` theme pack from Saves/AkronThemes.", "Normal clear"],
    ["Custom name", "Set the name shown for the custom theme and exported theme pack.", "Normal clear"],
    ["Window", "Set the custom theme window/background color.", "Normal clear"],
    ["Header", "Set the custom theme header color.", "Normal clear"],
    ["Header hover", "Set the custom theme hovered header and active UI color.", "Normal clear"],
    ["Frame", "Set the custom theme input/value box color.", "Normal clear"],
    ["Text", "Set the custom theme main text color.", "Normal clear"],
    ["Muted", "Set the custom theme inactive indicator color.", "Normal clear"],
    ["Disabled", "Set the custom theme disabled-row text color.", "Normal clear"],
    ["Opacity", "Set overlay background opacity.", "Normal clear"],
    ["UI scale", "Scale ImGui overlay windows for DPI and readability.", "Normal clear"],
    ["Blur", "Store the overlay blur presentation amount.", "Normal clear"],
    ["Anim ms", "Set stored overlay animation duration.", "Normal clear"],
    ["Floating button", "Enable floating activation-button settings for non-keyboard workflows.", "Normal clear"],
    ["Button %", "Set the floating activation button scale.", "Normal clear"],
    ["Button alpha", "Set the floating activation button opacity.", "Normal clear"],
    ["Button in levels", "Allow the floating activation button in active levels.", "Normal clear"],
    ["Button in menus", "Allow the floating activation button outside active levels.", "Normal clear"],
    ["Search autofocus", "Focus Akron search automatically when the overlay opens.", "Normal clear"]
  ],
  "Export Profile": [
    ["Name", "Set the exported `.akr` profile pack name.", "Normal clear"],
    ["Whole", "Export every supported profile section into one `.akr` profile pack.", "Normal clear"],
    ["StartPos", "Export only StartPos profile data into the `.akr` profile pack.", "Normal clear"],
    ["Keybinds", "Export only Akron menu keybind profile data into the `.akr` profile pack.", "Normal clear"],
    ["Auto Kill", "Export only Auto Kill profile data into the `.akr` profile pack.", "Normal clear"],
    ["Auto Deafen", "Export only Auto Deafen profile data into the `.akr` profile pack.", "Normal clear"],
    ["Recorder", "Export only Internal Recorder profile data into the `.akr` profile pack.", "Normal clear"],
    ["Audio", "Export only audio profile data into the `.akr` profile pack.", "Normal clear"],
    ["HUD", "Export only HUD profile data into the `.akr` profile pack.", "Normal clear"]
  ],
  "Import Profile": [
    ["Whole", "Import every supported profile section from the selected `.akr` profile pack.", "Normal clear"],
    ["StartPos", "Import only StartPos profile data from the selected `.akr` profile pack.", "Normal clear"],
    ["Keybinds", "Import only Akron menu keybind profile data from the selected `.akr` profile pack.", "Normal clear"],
    ["Auto Kill", "Import only Auto Kill profile data from the selected `.akr` profile pack.", "Normal clear"],
    ["Auto Deafen", "Import only Auto Deafen profile data from the selected `.akr` profile pack.", "Normal clear"],
    ["Recorder", "Import only Internal Recorder profile data from the selected `.akr` profile pack.", "Normal clear"],
    ["Audio", "Import only audio profile data from the selected `.akr` profile pack.", "Normal clear"],
    ["HUD", "Import only HUD profile data from the selected `.akr` profile pack.", "Normal clear"]
  ],
  "Community Packs": [
    ["Catalog", "Connect to the configured community pack index and reload map-specific packs.", "Normal clear"],
    ["Import", "Download and import the selected `.akr` pack.", "Normal clear"]
  ],
  "Visible": [
    ["Offset X", "Bulk-edit horizontal offset for every label row.", "Normal clear"],
    ["Offset Y", "Bulk-edit vertical offset for every label row.", "Normal clear"],
    ["Scale", "Bulk-edit text scale for every label row.", "Normal clear"],
    ["Opacity", "Bulk-edit opacity for every label row.", "Normal clear"],
    ["Line spacing", "Bulk-edit multiline spacing for every label row.", "Normal clear"],
    ["Shadow", "Bulk-edit shadow visibility for every label row.", "Normal clear"],
    ["Shadow opacity", "Bulk-edit shadow opacity for every label row.", "Normal clear"],
    ["Shadow X", "Bulk-edit shadow horizontal offset for every label row.", "Normal clear"],
    ["Shadow Y", "Bulk-edit shadow vertical offset for every label row.", "Normal clear"],
    ["Shadow color", "Bulk-edit shadow color for every label row.", "Normal clear"]
  ],
  "Player Overlap": [
    ["Mode: Fade/Move", "Choose whether overlapped labels fade or move away from Madeline.", "Normal clear"],
    ["Only current label", "Apply overlap behavior only to the label Madeline overlaps.", "Normal clear"],
    ["Padding", "Add extra HUD pixels around each text label for overlap detection.", "Normal clear"],
    ["Opacity", "Set label opacity while Madeline overlaps it in Fade mode.", "Normal clear"],
    ["Top left", "Move overlapped labels to the top-left anchor in Move mode.", "Normal clear"],
    ["Top center", "Move overlapped labels to the top-center anchor in Move mode.", "Normal clear"],
    ["Top right", "Move overlapped labels to the top-right anchor in Move mode.", "Normal clear"],
    ["Middle left", "Move overlapped labels to the middle-left anchor in Move mode.", "Normal clear"],
    ["Center", "Move overlapped labels to the center anchor in Move mode.", "Normal clear"],
    ["Middle right", "Move overlapped labels to the middle-right anchor in Move mode.", "Normal clear"],
    ["Bottom left", "Move overlapped labels to the bottom-left anchor in Move mode.", "Normal clear"],
    ["Bottom center", "Move overlapped labels to the bottom-center anchor in Move mode.", "Normal clear"],
    ["Bottom right", "Move overlapped labels to the bottom-right anchor in Move mode.", "Normal clear"],
    ["Offset X", "Set horizontal offset from the move anchor.", "Normal clear"],
    ["Offset Y", "Set vertical offset from the move anchor.", "Normal clear"]
  ],
  "Room": [
    ["Color", "Set room label text color.", "Normal clear"],
    ["Offset X", "Move the room label horizontally from its anchor.", "Normal clear"],
    ["Offset Y", "Move the room label vertically from its anchor.", "Normal clear"],
    ["Scale", "Set room label text scale.", "Normal clear"],
    ["Opacity", "Set room label opacity.", "Normal clear"],
    ["Line spacing", "Set room label multiline spacing.", "Normal clear"],
    ["Shadow", "Draw a black outline plus offset shadow behind the room label.", "Normal clear"],
    ["Shadow opacity", "Set room label shadow opacity.", "Normal clear"],
    ["Shadow X", "Set room label shadow horizontal offset.", "Normal clear"],
    ["Shadow Y", "Set room label shadow vertical offset.", "Normal clear"],
    ["Shadow color", "Set room label shadow color.", "Normal clear"]
  ],
  "Death Stats": [
    ["Format", "Configure death-stat text tokens.", "Goldberry/Hardlist clear"],
    ["Disabled", "Hide the death-stat label.", "Goldberry/Hardlist clear"],
    ["After death", "Show death stats after death.", "Goldberry/Hardlist clear"],
    ["Pause menu", "Show death stats in the pause menu.", "Goldberry/Hardlist clear"],
    ["Death + menu", "Show death stats after death and in the pause menu.", "Goldberry/Hardlist clear"],
    ["Always", "Always show the death-stat label.", "Goldberry/Hardlist clear"],
    ["PB loss prompt", "Show a restart prompt when the current death PB is no longer possible.", "Normal clear"],
    ["Color", "Set death-stat label text color.", "Goldberry/Hardlist clear"],
    ["Offset X", "Move the death-stat label horizontally from its anchor.", "Goldberry/Hardlist clear"],
    ["Offset Y", "Move the death-stat label vertically from its anchor.", "Goldberry/Hardlist clear"],
    ["Scale", "Set death-stat label text scale.", "Goldberry/Hardlist clear"],
    ["Opacity", "Set death-stat label opacity.", "Goldberry/Hardlist clear"],
    ["Line spacing", "Set death-stat label multiline spacing.", "Goldberry/Hardlist clear"],
    ["Shadow", "Draw a black outline plus offset shadow behind the death-stat label.", "Goldberry/Hardlist clear"],
    ["Shadow opacity", "Set death-stat label shadow opacity.", "Goldberry/Hardlist clear"],
    ["Shadow X", "Set death-stat label shadow horizontal offset.", "Goldberry/Hardlist clear"],
    ["Shadow Y", "Set death-stat label shadow vertical offset.", "Goldberry/Hardlist clear"],
    ["Shadow color", "Set death-stat label shadow color.", "Goldberry/Hardlist clear"]
  ],
  "Input History": [
    ["Current inputs", "Show the current movement, jump, dash, and grab input chord.", "Normal clear"],
    ["Input history", "Show a rolling list of recent input chords and held-frame counts.", "Normal clear"],
    ["Rows", "Set how many input-history rows stay visible.", "Normal clear"],
    ["Placement", "Move input history between the left HUD column and right side.", "Normal clear"],
    ["Opacity", "Set input-history panel opacity.", "Normal clear"],
    ["Compact rows", "Use tighter input-history rows.", "Normal clear"],
    ["Pin on death", "Freeze latest input rows after death until the next fresh input.", "Normal clear"],
    ["Show on death", "Temporarily show input history after death even when the panel is off.", "Normal clear"],
    ["Transition rows", "Insert a marker row when moving between rooms.", "Normal clear"],
    ["Text color", "Set input history text color.", "Normal clear"],
    ["Event color", "Set input history event marker color.", "Normal clear"],
    ["Offset X", "Move the input history panel horizontally from its anchor.", "Normal clear"],
    ["Offset Y", "Move the input history panel vertically from its anchor.", "Normal clear"],
    ["Scale", "Set input history text scale.", "Normal clear"],
    ["Line spacing", "Set input history multiline spacing.", "Normal clear"],
    ["Shadow", "Draw a black outline plus offset shadow behind input history.", "Normal clear"]
  ],
  "Inputs per second": [
    ["Placement", "Choose the screen side for the inputs-per-second counter.", "Normal clear"],
    ["Scale", "Set counter scale percentage.", "Normal clear"],
    ["Opacity", "Set counter opacity percentage.", "Normal clear"],
    ["Show total", "Show total counted input presses since level entry or last death.", "Normal clear"],
    ["Show max", "Show the highest rolling inputs-per-second value since level entry or last death.", "Normal clear"],
    ["Count movement", "Count left, right, up, and down rising-edge presses.", "Normal clear"],
    ["Count actions", "Count jump, dash, grab, crouch dash, and talk rising-edge presses.", "Normal clear"],
    ["Count menu", "Also count confirm, cancel, and pause/menu rising-edge presses.", "Normal clear"],
    ["Reset counter", "Clear current, total, and max input counts.", "Normal clear"],
    ["Text", "Set counter text color.", "Normal clear"],
    ["Offset X", "Move the counter horizontally from its anchor.", "Normal clear"],
    ["Offset Y", "Move the counter vertically from its anchor.", "Normal clear"],
    ["Line spacing", "Set counter multiline spacing.", "Normal clear"],
    ["Shadow", "Draw a black outline plus offset shadow behind the counter.", "Normal clear"]
  ],
  "Room Timer": [
    ["Color", "Set room timer text color.", "Normal clear"],
    ["Offset X", "Move the room timer horizontally from its anchor.", "Normal clear"],
    ["Offset Y", "Move the room timer vertically from its anchor.", "Normal clear"],
    ["Scale", "Set room timer text scale.", "Normal clear"],
    ["Opacity", "Set room timer opacity.", "Normal clear"],
    ["Line spacing", "Set room timer multiline spacing.", "Normal clear"],
    ["Shadow", "Draw a black outline plus offset shadow behind the room timer.", "Normal clear"],
    ["Shadow opacity", "Set room timer shadow opacity.", "Normal clear"],
    ["Shadow X", "Set room timer shadow horizontal offset.", "Normal clear"],
    ["Shadow Y", "Set room timer shadow vertical offset.", "Normal clear"],
    ["Shadow color", "Set room timer shadow color.", "Normal clear"]
  ],
  "Room Stat Tracker": [
    ["Color", "Set room stat tracker text color.", "Normal clear"],
    ["Room name", "Include the current room name.", "Normal clear"],
    ["Deaths", "Include deaths since entering the current room.", "Normal clear"],
    ["In-game time", "Include elapsed room time.", "Normal clear"],
    ["Strawberries", "Include strawberries collected in this room visit.", "Normal clear"],
    ["Alive time", "Include time since the latest respawn.", "Normal clear"],
    ["Hide if golden", "Hide the room stat tracker during golden berry attempts.", "Normal clear"],
    ["Freeze mode", "Choose how timer display freezes.", "Normal clear"],
    ["Offset X", "Move the room stat tracker horizontally from its anchor.", "Normal clear"],
    ["Offset Y", "Move the room stat tracker vertically from its anchor.", "Normal clear"],
    ["Scale", "Set room stat tracker text scale.", "Normal clear"],
    ["Opacity", "Set room stat tracker opacity.", "Normal clear"],
    ["Line spacing", "Set room stat tracker multiline spacing.", "Normal clear"],
    ["Shadow", "Draw a black outline plus offset shadow behind the room stat tracker.", "Normal clear"],
    ["Shadow opacity", "Set room stat tracker shadow opacity.", "Normal clear"],
    ["Shadow X", "Set room stat tracker shadow horizontal offset.", "Normal clear"],
    ["Shadow Y", "Set room stat tracker shadow vertical offset.", "Normal clear"],
    ["Shadow color", "Set room stat tracker shadow color.", "Normal clear"]
  ],
  "Attempts": [
    ["Color", "Set attempts label text color.", "Goldberry/Hardlist clear"],
    ["Offset X", "Move the attempts label horizontally from its anchor.", "Goldberry/Hardlist clear"],
    ["Offset Y", "Move the attempts label vertically from its anchor.", "Goldberry/Hardlist clear"],
    ["Scale", "Set attempts label text scale.", "Goldberry/Hardlist clear"],
    ["Opacity", "Set attempts label opacity.", "Goldberry/Hardlist clear"],
    ["Line spacing", "Set attempts label multiline spacing.", "Goldberry/Hardlist clear"],
    ["Shadow", "Draw a black outline plus offset shadow behind the attempts label.", "Goldberry/Hardlist clear"],
    ["Shadow opacity", "Set attempts label shadow opacity.", "Goldberry/Hardlist clear"],
    ["Shadow X", "Set attempts label shadow horizontal offset.", "Goldberry/Hardlist clear"],
    ["Shadow Y", "Set attempts label shadow vertical offset.", "Goldberry/Hardlist clear"],
    ["Shadow color", "Set attempts label shadow color.", "Goldberry/Hardlist clear"]
  ],
  "Status": [
    ["Color", "Set status label text color.", "Normal clear"],
    ["Offset X", "Move the status label horizontally from its anchor.", "Normal clear"],
    ["Offset Y", "Move the status label vertically from its anchor.", "Normal clear"],
    ["Scale", "Set status label text scale.", "Normal clear"],
    ["Opacity", "Set status label opacity.", "Normal clear"],
    ["Line spacing", "Set status label multiline spacing.", "Normal clear"],
    ["Shadow", "Draw a black outline plus offset shadow behind the status label.", "Normal clear"],
    ["Shadow opacity", "Set status label shadow opacity.", "Normal clear"],
    ["Shadow X", "Set status label shadow horizontal offset.", "Normal clear"],
    ["Shadow Y", "Set status label shadow vertical offset.", "Normal clear"],
    ["Shadow color", "Set status label shadow color.", "Normal clear"]
  ],
  "Toasts": [
    ["Color", "Set toast text color.", "Normal clear"],
    ["Anchor", "Choose the screen position for option feedback messages.", "Normal clear"],
    ["Offset X", "Move toast messages horizontally from their anchor.", "Normal clear"],
    ["Offset Y", "Move toast messages vertically from their anchor.", "Normal clear"],
    ["Scale", "Set toast text scale.", "Normal clear"],
    ["Opacity", "Set toast opacity.", "Normal clear"],
    ["Line spacing", "Set toast multiline spacing.", "Normal clear"],
    ["Shadow", "Draw a black outline plus offset shadow behind toasts.", "Normal clear"],
    ["Shadow opacity", "Set toast shadow opacity.", "Normal clear"],
    ["Shadow X", "Set toast shadow horizontal offset.", "Normal clear"],
    ["Shadow Y", "Set toast shadow vertical offset.", "Normal clear"],
    ["Shadow color", "Set toast shadow color.", "Normal clear"]
  ],
  "Cheat Indicator": [
    ["Only cheating", "Hide the indicator until the attempt status differs from the initial status.", "Normal clear"],
    ["Anchor", "Choose the status indicator screen alignment.", "Normal clear"],
    ["Style: Text/Dot", "Choose text badge or single status dot.", "Normal clear"],
    ["Scale", "Set indicator scale percentage.", "Normal clear"],
    ["Opacity", "Set indicator opacity percentage.", "Normal clear"]
  ],
  "+ Custom": [
    ["Visible", "Show this custom label when its event condition passes.", "Normal clear"],
    ["Name", "Set the custom label's human-readable name.", "Normal clear"],
    ["Text", "Set template text with variables such as room, room time, status, FPS, speed, and conditionals.", "Normal clear"],
    ["Anchor", "Choose the custom label's screen alignment.", "Normal clear"],
    ["Absolute position", "Use exact HUD coordinates instead of the shared anchored label container.", "Normal clear"],
    ["Screen X", "Set the custom label's absolute HUD X coordinate.", "Normal clear"],
    ["Screen Y", "Set the custom label's absolute HUD Y coordinate.", "Normal clear"],
    ["Offset X", "Move the custom label horizontally from its anchor or absolute point.", "Normal clear"],
    ["Offset Y", "Move the custom label vertically from its anchor or absolute point.", "Normal clear"],
    ["Scale", "Set custom label text scale.", "Normal clear"],
    ["Opacity", "Set custom label opacity.", "Normal clear"],
    ["Line spacing", "Set custom label multiline spacing.", "Normal clear"],
    ["Color", "Set custom label text color.", "Normal clear"],
    ["Text align", "Align text inside the label's measured line.", "Normal clear"],
    ["Font", "Choose a preset text size for the label.", "Normal clear"],
    ["Shadow", "Draw a black outline plus offset shadow behind the custom label.", "Normal clear"],
    ["Shadow opacity", "Set custom label shadow opacity.", "Normal clear"],
    ["Shadow X", "Set custom label shadow horizontal offset.", "Normal clear"],
    ["Shadow Y", "Set custom label shadow vertical offset.", "Normal clear"],
    ["Shadow color", "Set custom label shadow color.", "Normal clear"],
    ["Event", "Choose when the label is visible: always, on death, while buttons are held, or on noclip death markers.", "Normal clear"],
    ["Delay", "Set delay before event labels appear.", "Normal clear"],
    ["Duration", "Set how long event labels remain visible.", "Normal clear"],
    ["Event style override", "Temporarily use event scale, color, and opacity while the event is active.", "Normal clear"],
    ["Event scale", "Set event override scale.", "Normal clear"],
    ["Event opacity", "Set event override opacity.", "Normal clear"],
    ["Event color", "Set event override color.", "Normal clear"],
    ["Duplicate", "Duplicate the active custom label.", "Normal clear"],
    ["Delete", "Delete the active custom label after confirmation.", "Normal clear"],
    ["Export .akr", "Export custom labels as a `.akr` pack.", "Normal clear"],
    ["Import Latest .akr", "Import the latest saved custom-label `.akr` pack from Saves/AkronHudLabels.", "Normal clear"]
  ],
  "Replay Settings": [
    ["Start Replay Buffer", "Start rolling replay capture for the current game scene.", "Goldberry/Hardlist clear"],
    ["Stop", "Stop rolling replay capture.", "Goldberry/Hardlist clear"],
    ["Save", "Save the current replay buffer window.", "Goldberry/Hardlist clear"],
    ["Buffer", "Set seconds of rolling FFmpeg segments kept available for manual replay saves.", "Goldberry/Hardlist clear"],
    ["Auto-start", "Choose whether replay capture starts manually, in levels, or everywhere.", "Goldberry/Hardlist clear"],
    ["Bind save key", "Set the key used to save the replay buffer without opening the overlay.", "Goldberry/Hardlist clear"],
    ["Clear", "Clear only the custom replay-buffer save binding.", "Goldberry/Hardlist clear"]
  ],
  "Output": [
    ["Output folder", "Choose where recordings and clips are written.", "Goldberry/Hardlist clear"],
    ["Filename template", "Configure clip filename tokens.", "Goldberry/Hardlist clear"],
    ["Container", "Choose MKV, MP4, MOV, or WebM output container.", "Goldberry/Hardlist clear"],
    ["Auto remux", "Keep the crash-resistant capture target and produce an MP4 share copy after recording.", "Goldberry/Hardlist clear"],
    ["Sort clips", "Set saved clip browser grouping preference.", "Goldberry/Hardlist clear"],
    ["Filter clips", "Set saved clip browser filter preference.", "Goldberry/Hardlist clear"]
  ],
  "Codec": [
    ["Quality", "Set simple encoder quality target before fine-tuning bitrate or rate control.", "Goldberry/Hardlist clear"],
    ["Rate ctrl", "Choose encoder rate-control mode for quality, file size, or lossless output.", "Goldberry/Hardlist clear"],
    ["Keyframe", "Set seconds between keyframes, or 0 to let the encoder decide.", "Goldberry/Hardlist clear"],
    ["Dropped-frame warning", "Show a warning when the recorder cannot capture frames at the configured cadence.", "Goldberry/Hardlist clear"]
  ],
  "Audio": [
    ["Full mix track", "Record the mixed game-audio track.", "Goldberry/Hardlist clear"],
    ["Music track", "Record an isolated music track when the backend can provide it.", "Goldberry/Hardlist clear"],
    ["SFX track", "Record an isolated sound-effects track when the backend can provide it.", "Goldberry/Hardlist clear"],
    ["Ambience track", "Record an isolated ambience track when the backend can provide it.", "Goldberry/Hardlist clear"],
    ["Record muted audio", "Capture game audio even when in-game playback is muted, when supported.", "Goldberry/Hardlist clear"],
    ["Mix", "Set full-mix recording volume percentage.", "Goldberry/Hardlist clear"],
    ["Music", "Set music track recording volume percentage.", "Goldberry/Hardlist clear"],
    ["SFX", "Set SFX track recording volume percentage.", "Goldberry/Hardlist clear"],
    ["Amb", "Set ambience track recording volume percentage.", "Goldberry/Hardlist clear"]
  ],
  "Clip Triggers": [
    ["Last death", "Auto-save a clip around the latest death trigger.", "Goldberry/Hardlist clear"],
    ["Respawn to death", "Auto-save clips from respawn through the next death.", "Goldberry/Hardlist clear"],
    ["Room entry to clear", "Auto-save clips from room entry through a clear.", "Goldberry/Hardlist clear"],
    ["Checkpoint clear", "Auto-save clips when a checkpoint is cleared.", "Goldberry/Hardlist clear"],
    ["Berry collect", "Auto-save clips when a berry is collected.", "Goldberry/Hardlist clear"],
    ["Golden death", "Auto-save clips when a golden attempt dies.", "Goldberry/Hardlist clear"],
    ["Pre-roll", "Set seconds included before event-based clip triggers.", "Goldberry/Hardlist clear"],
    ["Post-roll", "Set seconds included after event-based clip triggers.", "Goldberry/Hardlist clear"]
  ],
  "Output Folder": [
    ["Path", "Set the recorder output folder. Empty uses Saves/AkronRecordings.", "Goldberry/Hardlist clear"]
  ],
  "Filename Template": [
    ["Template", "Use tokens such as chapter, room, timestamp, death, and attempt.", "Goldberry/Hardlist clear"]
  ],
  "Colorspace Args": [
    ["FFmpeg filter", "Set optional FFmpeg video-filter colorspace arguments.", "Goldberry/Hardlist clear"]
  ],
  "FPS Bypass": [
    ["Target FPS", "Set the draw target while FPS Bypass is enabled.", "Cheat"],
    ["Method", "Choose interval or dynamic render cadence behavior.", "Cheat"],
    ["Smooth Camera", "Choose Fancy, Fast, or Off camera smoothing.", "Cheat"],
    ["Objects", "Choose extrapolate or interpolate object smoothing.", "Cheat"],
    ["TAS mode", "Keep overworld updates locked for TAS compatibility.", "Cheat"],
    ["Subpixel Madeline", "Draw Madeline and held items at subpixel render positions.", "Cheat"],
    ["Smooth background", "Use high-resolution background compositing in Fancy camera smoothing.", "Cheat"],
    ["Smooth foreground", "Use high-resolution foreground compositing in Fancy camera smoothing.", "Cheat"],
    ["Hide edge gaps", "Slightly zoom the level to hide gaps introduced by fractional camera offsets.", "Cheat"],
    ["Silly mode", "Apply an experimental smoothing preset to FPS Bypass rendering.", "Cheat"]
  ],
  "TPS Bypass": [
    ["Target TPS", "Set the simulation update target while TPS Bypass is enabled.", "Cheat"]
  ],
  "Extended Variants Randomizer": [
    ["Change variants randomly", "Enable the external variant randomizer loop.", "Cheat"],
    ["Reroll mode", "Replace the randomized variant set instead of changing one option at a time.", "Cheat"],
    ["Display enabled variants", "Show the active variant list on-screen.", "Cheat"],
    ["Interval", "Set seconds between randomizer changes; zero uses screen-change behavior.", "Cheat"],
    ["Max", "Set the maximum number of simultaneously randomized variants.", "Cheat"]
  ]
};

function extractBlock(source, marker) {
  const start = source.indexOf(marker);
  if (start === -1) {
    return "";
  }

  const bodyStart = source.indexOf("{", start);
  if (bodyStart === -1) {
    return "";
  }

  let depth = 0;
  for (let index = bodyStart; index < source.length; index++) {
    const char = source[index];
    if (char === "{") {
      depth++;
    } else if (char === "}") {
      depth--;
      if (depth === 0) {
        return source.slice(bodyStart, index + 1);
      }
    }
  }

  return "";
}

function extractSwitchCase(source, label) {
  const marker = `case "${label}":`;
  const start = source.indexOf(marker);
  if (start === -1) {
    return "";
  }

  const remainder = source.slice(start + marker.length);
  const nextMatch = remainder.match(/\n\s*(case "[^"]+":|default:)/);
  if (!nextMatch || nextMatch.index === undefined) {
    return source.slice(start);
  }

  return source.slice(start, start + marker.length + nextMatch.index);
}

function unescapeCString(value) {
  return value.replace(/\\"/g, '"').replace(/\\\\/g, "\\");
}

function parseStringDictionary(source, marker) {
  const block = extractBlock(source, marker);
  const rows = new Map();
  const pattern = /\[\s*"((?:\\"|[^"])*)"\s*\]\s*=\s*"((?:\\"|[^"])*)"/g;
  for (const match of block.matchAll(pattern)) {
    rows.set(unescapeCString(match[1]), unescapeCString(match[2]));
  }
  return rows;
}

function parseFeatureDefinitions() {
  const block = extractBlock(registrySource, "Definitions");
  const rows = new Map();
  const pattern = /\{\s*AkronFeatureKind\.([A-Za-z0-9_]+),\s*new FeatureDefinition\(\s*AkronFeatureKind\.[A-Za-z0-9_]+,\s*AkronStatus\.([A-Za-z0-9_]+),\s*"((?:\\"|[^"])*)",\s*"((?:\\"|[^"])*)"\s*\)\s*\}/g;
  for (const match of block.matchAll(pattern)) {
    rows.set(match[1], {
      status: statusNames[match[2]] ?? match[2],
      label: unescapeCString(match[3]),
      reason: unescapeCString(match[4])
    });
  }
  return rows;
}

function parseUiLabels() {
  const block = extractBlock(registrySource, "UiLabelClassifications");
  const rows = new Map();
  const pattern = /\{\s*"((?:\\"|[^"])*)",\s*AkronStatus\.([A-Za-z0-9_]+)\s*\}/g;
  for (const match of block.matchAll(pattern)) {
    rows.set(unescapeCString(match[1]), statusNames[match[2]] ?? match[2]);
  }
  return rows;
}

const actionDescriptions = parseStringDictionary(overlaySource, "ActionDescriptions");
const featureDefinitions = parseFeatureDefinitions();
const uiLabelStatuses = parseUiLabels();

function soundLabels() {
  const labels = [];
  const pattern = /new SoundDefinition\(\s*"[^"]+",\s*"((?:\\"|[^"])*)"/g;
  for (const match of earAidSource.matchAll(pattern)) {
    labels.push(unescapeCString(match[1]));
  }
  return labels;
}

function findFeatureKind(text, startIndex) {
  const lineEnd = text.indexOf("\n", startIndex);
  const window = text.slice(startIndex, lineEnd === -1 ? Math.min(text.length, startIndex + 240) : lineEnd);
  const match = window.match(/AkronFeatureKind\.([A-Za-z0-9_]+)/);
  return match?.[1] ?? "";
}

function addRow(rows, tab, label, control, featureKind = "") {
  if (label === "StartPos Slot ") {
    label = "StartPos Slot 1-9";
  }

  if (!label || rows.some((row) => row.label.toLowerCase() === label.toLowerCase())) {
    return;
  }

  rows.push({ tab, label, control, featureKind: featureKind || featureKindOverrides[label] || "" });
}

function parseRowsFromBlock(tab, block) {
  const rows = [];
  const callPattern = /\b(InlineNumericToggle|NumericToggle|NumericRow|SelectorDropdown|Selector|PolicyToggle|Toggle|Action)\(\s*"((?:\\"|[^"])*)"/g;
  for (const match of block.matchAll(callPattern)) {
    addRow(rows, tab, unescapeCString(match[2]), controlNames[match[1]] ?? match[1], findFeatureKind(block, match.index));
  }

  if (tab === "StartPos") {
    addRow(rows, tab, "StartPos", "Action", "StartPosTools");
    addRow(rows, tab, "Place StartPos", "Toggle", "StartPosTools");
    addRow(rows, tab, "StartPos Switcher", "Action", "StartPosTools");
  }

  if (tab === "Interface") {
    addRow(rows, tab, "Search", "Text input");
  }

  return rows;
}

function rowsForTab(tab) {
  if (tab === "Global") {
    return parseRowsFromBlock(tab, extractBlock(overlaySource, "private static List<OverlayEntry> BuildGlobalEntries"))
      .filter((row) => row.label !== "FPS Bypass" && row.label !== "TPS Bypass");
  }

  if (tab === "Labels") {
    const rows = [
      { tab, label: "Visible", control: "Toggle", featureKind: "" },
      { tab, label: "Player Overlap", control: "Toggle", featureKind: "" }
    ];
    const block = extractBlock(overlaySource, "private static List<OverlayEntry> BuildLabelEntries");
    const labelPattern = /\[\s*"((?:\\"|[^"])*)"\s*\]\s*=\s*(LabelPolicyToggle|LabelToggle)\(\s*"((?:\\"|[^"])*)"/g;
    for (const match of block.matchAll(labelPattern)) {
      addRow(rows, tab, unescapeCString(match[3]), "Toggle", findFeatureKind(block, match.index));
    }
    addRow(rows, tab, "+ Custom", "Action", "CustomHudLabels");
    return rows;
  }

  if (tab === "Keybinds") {
    const rows = [{ tab, label: "In-game only", control: "Toggle", featureKind: "" }];
    addRow(rows, tab, "Open Menu", "Keybind", "");
    const specs = extractBlock(overlaySource, "private static IEnumerable<KeybindOverviewSpec> BuildDefaultKeybindOverviewSpecs");
    const pattern = /new KeybindOverviewSpec\("((?:\\"|[^"])*)"/g;
    for (const match of specs.matchAll(pattern)) {
      addRow(rows, tab, unescapeCString(match[1]), "Keybind", "");
    }
    return rows;
  }

  if (tab === "Sound") {
    const rows = parseRowsFromBlock(tab, extractSwitchCase(overlaySource, tab));
    for (const label of soundLabels()) {
      addRow(rows, tab, label, "Toggle", "");
    }
    return rows;
  }

  return parseRowsFromBlock(tab, extractSwitchCase(overlaySource, tab))
    .filter((row) => !(tab === "Creator" && row.label === "Export Room Times"));
}

function rowsForExternalTools() {
  const rows = [];
  for (const tab of externalToolTabs) {
    const blockMarker = {
      "Motion Smoothing": "",
      "Speedrun Tool": "private static List<OverlayEntry> BuildSpeedrunToolEntries",
      "CelesteTAS": "private static List<OverlayEntry> BuildCelesteTasEntries",
      "Extended Variant Mode": "private static List<OverlayEntry> BuildExtendedVariantEntries"
    }[tab];
    if (tab === "Motion Smoothing") {
      addRow(rows, tab, "FPS Bypass", "Numeric toggle", "FpsBypass");
      addRow(rows, tab, "TPS Bypass", "Numeric toggle", "TpsBypass");
      continue;
    }

    for (const row of parseRowsFromBlock(tab, extractBlock(overlaySource, blockMarker))) {
      rows.push(row);
    }
  }

  addRow(rows, "Extended Variant Mode", "Extended Variants Master", "Toggle", "ExtendedVariantMode");
  addRow(rows, "Extended Variant Mode", "Extended Variants Randomizer", "Toggle", "ExtendedVariantMode");
  addRow(rows, "Speedrun Tool", "Export Room Times", "Action", "");
  return rows;
}

function mdEscape(value) {
  return String(value ?? "")
    .replaceAll("|", "\\|")
    .replaceAll("\n", " ")
    .trim();
}

function statusFor(row) {
  if (statusOverrides[row.label]) {
    return statusOverrides[row.label];
  }

  if (row.featureKind && featureDefinitions.has(row.featureKind)) {
    return featureDefinitions.get(row.featureKind).status;
  }

  if (uiLabelStatuses.has(row.label)) {
    return uiLabelStatuses.get(row.label);
  }

  if (soundLabels().includes(row.label)) {
    return "Normal clear";
  }

  return "Not individually classified";
}

function descriptionFor(row) {
  if (manualDescriptions[row.label]) {
    return manualDescriptions[row.label];
  }

  if (soundLabels().includes(row.label)) {
    return "Override volume for the matching Celeste sound event.";
  }

  if (actionDescriptions.has(row.label)) {
    return sanitizeDescription(actionDescriptions.get(row.label));
  }

  if (row.featureKind && featureDefinitions.has(row.featureKind)) {
    return sanitizeDescription(featureDefinitions.get(row.featureKind).reason);
  }

  if (row.label.startsWith("StartPos Slot ")) {
    return "Show the binding used to load the matching StartPos slot.";
  }

  return "Policy-visible overlay row in the current Akron UI.";
}

function sanitizeDescription(value) {
  return value
    .replace(/\bcurrent ruleset, profile, run state,\b/g, "current ruleset, run state,")
    .replace(/\bactive profile, ruleset stack, and attempt status\b/g, "active ruleset stack and attempt status")
    .replace(/\bprofile scope\b/g, "setup scope")
    .replace(/\bprofile \.akr pack\b/g, ".akr setup pack")
    .replace(/\bprofile scope as a \.akr pack\b/g, "setup scope as a .akr pack")
    .trim();
}

function tableFor(rows) {
  return [
    "| Option | Control | What it does | Attempt status |",
    "|---|---|---|---|",
    ...rows.map((row) => `| ${mdEscape(row.label)} | ${mdEscape(row.control)} | ${mdEscape(descriptionFor(row))} | ${mdEscape(statusFor(row))} |`)
  ].join("\n");
}

function menuRowsFor(rows) {
  const labels = new Set(rows.map((row) => row.label));
  const menuRows = [];
  for (const row of rows) {
    const docs = optionMenus[row.label];
    if (!docs) {
      continue;
    }

    for (const [suboption, description, status] of docs) {
      menuRows.push({
        option: row.label,
        suboption,
        description,
        status: status || statusFor(row)
      });
    }
  }

  // Custom labels render with runtime names. The player-facing row is `+ Custom`,
  // but the popup also handles existing custom label rows.
  if (labels.has("+ Custom") && !menuRows.some((row) => row.option === "+ Custom")) {
    for (const [suboption, description, status] of optionMenus["+ Custom"] ?? []) {
      menuRows.push({ option: "+ Custom", suboption, description, status });
    }
  }

  return menuRows;
}

function menusTableFor(rows) {
  const menuRows = menuRowsFor(rows);
  if (menuRows.length === 0) {
    return "";
  }

  return `\n## Option Menus\n\nRows with a triangle expose the controls listed below.\n\n${[
    "| Option | Menu control | What it changes | Status |",
    "|---|---|---|---|",
    ...menuRows.map((row) => `| ${mdEscape(row.option)} | ${mdEscape(row.suboption)} | ${mdEscape(row.description)} | ${mdEscape(row.status)} |`)
  ].join("\n")}\n`;
}

function externalTableFor(rows) {
  return [
    "| Tool | Option | Control | What it does | Attempt status |",
    "|---|---|---|---|---|",
    ...rows.map((row) => `| ${mdEscape(row.tab)} | ${mdEscape(row.label)} | ${mdEscape(row.control)} | ${mdEscape(descriptionFor(row))} | ${mdEscape(statusFor(row))} |`)
  ].join("\n");
}

function writePage(tab) {
  const meta = tabMeta[tab];
  const rows = rowsForTab(tab);
  if (rows.length === 0) {
    throw new Error(`No rows parsed for ${tab}`);
  }

  const output = `---
title: ${tab}
description: ${meta.description}
---

${meta.intro}

${tableFor(rows)}
${menusTableFor(rows)}
`;

  fs.writeFileSync(path.join(outputDir, `${meta.slug}.mdx`), `${output.trimEnd()}\n`);
  return rows.length;
}

function writeIndex() {
  const cards = [...baseTabs, "External tools"].map((tab) => {
    const meta = tabMeta[tab];
    return `<Card title="${tab}" icon="list-checks" href="/feature-guide/${meta.slug}">${meta.description}</Card>`;
  }).join("\n  ");

  const output = `---
title: Feature guide
description: Player-focused guide to Akron overlay tabs, options, controls, and attempt-status badges for the current build.
---

Use this section when you want to know what a visible Akron option does. Pages are organized by overlay tab and list the current public rows with their control type, behavior, and attempt status.

<CardGroup cols={2}>
  ${cards}
</CardGroup>
`;

  fs.writeFileSync(path.join(outputDir, "index.mdx"), `${output.trimEnd()}\n`);
}

function writeExternalToolsPage() {
  const meta = tabMeta["External tools"];
  const rows = rowsForExternalTools();
  if (rows.length === 0) {
    throw new Error("No external tool rows were parsed");
  }

  const output = `---
title: External tools
description: ${meta.description}
---

${meta.intro}

Motion Smoothing adds FPS and TPS bypass rows to the Global tab. Extended Variant Mode can also expose variant-specific rows at runtime; those dynamic rows depend on the installed Extended Variant Mode option list.

${externalTableFor(rows)}
${menusTableFor(rows)}
`;

  fs.writeFileSync(path.join(outputDir, "external-tools.mdx"), `${output.trimEnd()}\n`);
  return rows.length;
}

fs.mkdirSync(outputDir, { recursive: true });
writeIndex();

let total = 0;
for (const tab of baseTabs) {
  total += writePage(tab);
}
total += writeExternalToolsPage();

console.log(`Wrote ${baseTabs.length + 2} feature guide pages with ${total} option rows.`);
