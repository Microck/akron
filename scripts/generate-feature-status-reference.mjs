#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";

const repoRoot = process.cwd();
const registryPath = path.join(repoRoot, "Source", "Core", "AkronFeatureRegistry.cs");
const outputPath = path.join(repoRoot, "docs", "reference", "feature-status-reference.mdx");

const source = fs.readFileSync(registryPath, "utf8");

const statusNames = {
  GoldberryHardlistClean: "Goldberry/Hardlist clear",
  RegularClean: "Normal clear",
  Cheat: "Cheat"
};

const statusDescriptions = {
  GoldberryHardlistClean: "Allowed by Akron's strict clear category.",
  RegularClean: "Normal clear for ordinary play, but not strict-approved by default.",
  Cheat: "Attempt-changing behavior or externally-assisted control."
};

const statusRank = {
  GoldberryHardlistClean: 0,
  RegularClean: 1,
  Cheat: 2
};

const skippedUiLabels = new Set();

const uiLabelReasons = {
  "SRT Capture State": "Captures the active Speedrun Tool state slot.",
  "SRT Restore State": "Restores the active Speedrun Tool state slot.",
  "SRT Clear State": "Clears the active Speedrun Tool state slot."
};

const uiStatusReasons = {
  GoldberryHardlistClean: "",
  RegularClean: "",
  Cheat: ""
};

const uiSuboptionReasons = {
  "Map Capture / Freeze timers": "Freezes timer state during capture, changing the timing evidence.",
  "Room Capture / Freeze timers": "Freezes timer state during capture, changing the timing evidence.",
  "Death Stats / PB loss prompt": "Shows local PB-loss context after deaths without changing gameplay state.",
  "Input History / Input history": "Adds recent input review beyond the current input display.",
  "Input History / Pin on death": "Keeps recent input history visible after death for review.",
  "Input History / Rows": "Changes how much recent input history is displayed.",
  "Input History / Show on death": "Shows recent input history after death for review.",
  "Room Stat Tracker / Freeze mode": "Controls tracker display behavior without changing live gameplay.",
  "Free Camera / Freeze gameplay": "Stops level simulation while the camera is moved independently.",
  "Safe Mode / Freeze best run": "Protects saved best-run stats from changes during the session.",
  "Safe Mode / Freeze deaths": "Protects the save-slot death counter from changes during the session.",
  "Safe Mode / Freeze jumps": "Protects the save-slot jump counter from changes during the session."
};

const categoryRules = [
  {
    category: "Proof and submission",
    patterns: [
      /Submission|Proof|EndScreen|End Screen|Endscreen|PauseTracker|Pause Tracker|MapVersion|Map Version|Golden|LagPauser|Lag Pauser|Journal|Arm Completion|Flag Completion|Build Clear Video|FpsBypass|FPS bypass|TpsBypass|TPS bypass/i
    ]
  },
  {
    category: "Capture and recording",
    patterns: [
      /Recorder|Recording|Replay|Capture|Screenshot|Output|Filename|Codec|Bitrate|Framerate|Resolution|Colorspace|Preview|CPU|NVIDIA|AMD|Preset|Clip Trigger/i
    ]
  },
  {
    category: "HUD and information",
    patterns: [
      /Widget|Input|Resource|RoomTimer|Room Timer|DeathStats|Death Stats|Hud|HUD|Label|SpeedNumber|Speed Number|ShowTaps|Control display|Counter|RoomLabel|Room labels|RefillClarity|Refill clarity|Attempts|Jump Stats|Dash Stats|No Short Numbers|Room Stat/i
    ]
  },
  {
    category: "StartPos and routing",
    patterns: [
      /StartPos|Retry|Reload|Warp|AutoKill|Auto Kill|FrameAdvance|Frame advance|Freeze|Timescale|Transition|PauseCountdown|Pause countdown|PauseTimer|Pause timer|InstantComplete|Instant Complete|Unlock|Hazard|FastLookout|Fast lookout|LevelEnter|Level intro|DeathPb|PB loss|Obtain|Berry|Backboost|Neutral Drop|Pause Buffering/i
    ]
  },
  {
    category: "Map and debug inspection",
    patterns: [
      /Debug|Mountain|Deload|Hitbox|Entity|Flag|Trigger|Camera|Zoom|Trajectory|Inspector|Viewer/i
    ]
  },
  {
    category: "Visual and accessibility",
    patterns: [
      /Visual|Screenshake|Noise|Trail|Madeline|HidePlayer|Hide player|DeathVisuals|Death visuals|RespawnAnimation|Respawn animation|GoldenTransparency|Golden Transparency|Hair|EffectSync|Effect Sync|Anxiety|Distortion|Glitch|Particles|Snow|Tentacles|Waterfalls|Heat Distortion|Ghost Trail|Stamina Flash/i
    ]
  },
  {
    category: "Audio and interop",
    patterns: [
      /Audio|Pitch|Volume|Split|Tas|TAS|SRT|Speedrun|Brokered|Interop/i
    ]
  },
  {
    category: "Movement and resources",
    patterns: [
      /Noclip|Invincibility|Infinite|Dash|Stamina|Movement|GroundRefill|Grab|ClickTeleport|InputAssist|Variant/i
    ]
  },
  {
    category: "Overlay and workflow configuration",
    patterns: [
      /Search|Theme|UI Scale|Opacity|Toast|Status|Visible|Streamer|Pause While Open|Open Options|Confirm|Autosave|Export Profile|Import Profile|Community Packs|Safe Mode|Reset|Previous Room|Next Room|Restart Level|Skip Cutscene|Berry Obtain Options|Room\b/i
    ]
  }
];

function extractBlock(name) {
  const marker = `private static readonly Dictionary`;
  const start = source.indexOf(name);
  if (start === -1) {
    return "";
  }

  const declarationStart = source.lastIndexOf(marker, start);
  const bodyStart = source.indexOf("{", start);
  if (declarationStart === -1 || bodyStart === -1) {
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

function unescapeCString(value) {
  return value.replace(/\\"/g, '"').replace(/\\\\/g, "\\");
}

function categoryFor(kind, label, reason) {
  const haystack = `${kind} ${label}`;
  if (/SRT|Speedrun|Brokered/i.test(haystack)) {
    return "Audio and interop";
  }
  for (const rule of categoryRules) {
    if (rule.patterns.some((pattern) => pattern.test(haystack))) {
      return rule.category;
    }
  }
  return "Overlay and workflow configuration";
}

function mdEscape(value) {
  return value
    .replaceAll("|", "\\|")
    .replaceAll("\n", " ")
    .trim();
}

function sortRows(rows) {
  return rows.sort((left, right) => {
    const category = left.category.localeCompare(right.category);
    if (category !== 0) {
      return category;
    }

    const status = statusRank[left.status] - statusRank[right.status];
    if (status !== 0) {
      return status;
    }

    return left.label.localeCompare(right.label);
  });
}

function parseFeatureDefinitions() {
  const block = extractBlock("Definitions");
  const pattern = /\{\s*AkronFeatureKind\.([A-Za-z0-9_]+),\s*new FeatureDefinition\(\s*AkronFeatureKind\.[A-Za-z0-9_]+,\s*AkronStatus\.([A-Za-z0-9_]+),\s*"((?:\\"|[^"])*)",\s*"((?:\\"|[^"])*)"\s*\)\s*\}/g;
  const rows = [];

  for (const match of block.matchAll(pattern)) {
    const [, kind, status, rawLabel, rawReason] = match;
    const label = unescapeCString(rawLabel);
    const reason = unescapeCString(rawReason);
    rows.push({
      type: "Feature",
      kind,
      label,
      status,
      reason,
      category: categoryFor(kind, label, reason)
    });
  }

  return rows;
}

function parseUiLabels() {
  const block = extractBlock("UiLabelClassifications");
  const pattern = /\{\s*"((?:\\"|[^"])*)",\s*AkronStatus\.([A-Za-z0-9_]+)\s*\}/g;
  const rows = [];

  for (const match of block.matchAll(pattern)) {
    const [, rawLabel, status] = match;
    const label = unescapeCString(rawLabel);
    if (skippedUiLabels.has(label)) {
      continue;
    }

    rows.push({
      type: "UI row",
      kind: "",
      label,
      status,
      reason: uiLabelReasons[label] ?? uiStatusReasons[status],
      category: categoryFor("", label, "")
    });
  }

  return rows;
}

function parseUiSuboptions() {
  const block = extractBlock("UiSuboptionClassifications");
  const pattern = /BuildUiSuboptionKey\(\s*"((?:\\"|[^"])*)",\s*"((?:\\"|[^"])*)"\s*\),\s*AkronStatus\.([A-Za-z0-9_]+)/g;
  const rows = [];

  for (const match of block.matchAll(pattern)) {
    const [, rawParent, rawSuboption, status] = match;
    const parent = unescapeCString(rawParent);
    const suboption = unescapeCString(rawSuboption);
    rows.push({
      type: "Suboption",
      kind: "",
      label: `${parent} / ${suboption}`,
      status,
      reason: uiSuboptionReasons[`${parent} / ${suboption}`] ?? "This suboption has its own policy status because it changes how the parent row behaves.",
      category: categoryFor("", `${parent} ${suboption}`, "")
    });
  }

  return rows;
}

function tableFor(rows) {
  const lines = [
    "| Surface | Type | Status | Reason |",
    "|---|---|---|---|"
  ];

  for (const row of rows) {
    lines.push(
      `| ${mdEscape(row.label)} | ${row.type}${row.kind ? ` (${row.kind})` : ""} | ${statusNames[row.status] ?? row.status} | ${mdEscape(row.reason)} |`
    );
  }

  return lines.join("\n");
}

const rows = sortRows([
  ...parseFeatureDefinitions(),
  ...parseUiLabels(),
  ...parseUiSuboptions()
]);

if (rows.length === 0) {
  throw new Error("No feature rows were parsed from AkronFeatureRegistry.cs");
}

const grouped = Map.groupBy(rows, (row) => row.category);
const categorySections = Array.from(grouped.entries())
  .map(([category, categoryRows]) => `## ${category}\n\n${tableFor(categoryRows)}`)
  .join("\n\n");

const statusSummary = Object.entries(statusNames)
  .map(([status, label]) => `| ${label} | ${statusDescriptions[status]} |`)
  .join("\n");

const output = `---
title: Feature status reference
description: Reference for Akron feature, UI row, and suboption policy status.
---

Akron's feature registry is the source of truth for feature names, classifications, and policy reasons.

## Statuses

| Status | Meaning |
|---|---|
${statusSummary}

## Reading The Tables

- \`Feature\` rows come from dedicated \`AkronFeatureKind\` definitions.
- \`UI row\` rows are policy-visible overlay rows without a dedicated runtime feature kind.
- \`Suboption\` rows are tooltip or popup options that can be stricter than their parent row.
- Blank reason cells mean the row is classified as a policy-visible UI label; behavior details live in the feature guide.
- Categories are reader-facing groups for related feature and option surfaces.

${categorySections}
`;

fs.writeFileSync(outputPath, output);
console.log(`Wrote ${path.relative(repoRoot, outputPath)} with ${rows.length} rows.`);
