#!/usr/bin/env node
// Turns the JSONL perf records written by AkronPerformanceTelemetry into a
// comparison table, one row per run label.
//
//   node scripts/akron-perf/report.mjs [.tmp-perf directory] [--json]
//
// Each run file holds one header record and N sample records, one per 120-frame
// window. Only the windows where CelesteTAS was playing are kept. Everything
// else in the file is either the idle gap between `akron_perf record` and
// `akron_play_tas`, or the frames after playback ends, where CelesteTAS parks on
// the final frame and the level stops updating entirely. Parked frames cost
// about a seventh of a gameplay frame in allocation and are identical in every
// build, so averaging them in would bury the effect being measured.
// The first playing window is dropped as well: it straddles the start of
// playback and carries the JIT and first-touch costs.

import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";

const args = process.argv.slice(2);
const asJson = args.includes("--json");
const asGate = args.includes("--gate");
const asGc = args.includes("--gc");
const asGcDetail = args.includes("--gcdetail");
const dir = args.find((a) => !a.startsWith("--")) ?? ".tmp-perf";

// GCReason and GCType, from the CoreCLR GCStart_V2 event payload. Numbers are
// what the runtime emits; these names are what they mean.
const GC_REASON = [
    "AllocSmall", "Induced", "LowMemory", "Empty", "AllocLarge", "OutOfSpaceSOH",
    "OutOfSpaceLOH", "InducedNotForced", "Internal", "InducedLowMemory",
    "InducedCompacting", "LowMemoryHost", "PMFullGC", "LowMemoryHostBlocking",
];
const GC_TYPE = ["Blocking", "Background", "Foreground"];
const reasonName = (v) => GC_REASON[v] ?? `reason${v}`;
const typeName = (v) => GC_TYPE[v] ?? `type${v}`;

// Regression budgets, checked by --gate. Deliberately loose, set well above the
// worst measured value on the reference box so that ordinary run-to-run noise
// cannot fail the build.
//
// Two call-counter budgets used to sit here, encoding "HasSnapshot and the
// snapshot SHA-256 must never run on a render path again" as a number. Nothing
// in the mod ever wrote those counters, so both fields were absent from every
// record and both checks passed against a defaulted zero. A check that cannot
// fail is worse than no check, so they are gone with the counters themselves.
const BUDGETS = {
    p50Ms: 17.5,
    allocKbPerFrame: 200,
    gen2PerThousandFrames: 8,
};

const files = readdirSync(dir).filter((f) => f.endsWith(".jsonl")).sort();
if (files.length === 0) {
    console.error(`no .jsonl perf records in ${dir}`);
    process.exit(1);
}

function mean(values) {
    return values.length === 0 ? 0 : values.reduce((a, b) => a + b, 0) / values.length;
}

const runs = [];
for (const file of files) {
    const lines = readFileSync(join(dir, file), "utf8").split("\n").filter((l) => l.trim() !== "");
    let header = null;
    const samples = [];
    for (const line of lines) {
        let record;
        try {
            record = JSON.parse(line);
        } catch {
            continue; // a truncated tail line means the game died mid-flush
        }
        if (record.type === "header") header = record;
        else if (record.type === "sample") samples.push(record);
    }
    // tasRunning is the authority on "scripted playback was driving the player".
    // playerMovedPx is not usable as a filter: it samples the position at window
    // boundaries and the scenario is frame-symmetric, so the player is usually
    // back where it started when a boundary lands. It is reported, not filtered
    // on, and it is only ever non-zero inside a tasRunning window.
    const playing = samples.filter((s) => s.tasRunning === true);
    const usable = playing.slice(1);
    if (header === null || usable.length === 0) continue;

    const frames = usable.reduce((a, s) => a + s.frames, 0);
    const buckets = {};
    for (const sample of usable) {
        for (const [name, value] of Object.entries(sample.buckets ?? {})) {
            buckets[name] = (buckets[name] ?? 0) + value.ms;
        }
    }

    // Frame counts by threshold, straight out of the histogram. Bounds are
    // 16.7 / 20 / 25 / 33 / 50 / 100 / 250 plus an overflow bucket, so bucket 0
    // is the only one at or under one vsync interval.
    const histogram = new Array(8).fill(0);
    for (const sample of usable) {
        (sample.histogram ?? []).forEach((v, i) => { histogram[i] += v; });
    }
    const sumFrom = (i) => histogram.slice(i).reduce((a, b) => a + b, 0);

    const gcFrames = { any: 0, over16: 0, over33: 0, over100: 0, gen2: 0, gen2Over100: 0 };
    for (const sample of usable) {
        for (const key of Object.keys(gcFrames)) {
            gcFrames[key] += sample.gcFrames?.[key] ?? 0;
        }
    }

    const spikes = usable.flatMap((s, w) => (s.spikes ?? []).map((sp) => ({ ...sp, window: w })));
    const gcEvents = usable.flatMap((s) => s.gcEvents ?? []);
    const gameThreadAlloc = usable.reduce((a, s) => a + (s.gc.gameThreadAllocatedBytes ?? 0), 0);
    const totalAlloc = usable.reduce((a, s) => a + s.gc.allocatedBytes, 0);

    runs.push({
        label: header.label,
        file,
        build: header.build,
        buildId: header.buildId ?? "",
        gcConfig: header.gcConfig ?? null,
        histogram,
        framesOver16: sumFrom(1),
        framesOver33: sumFrom(4),
        framesOver100: sumFrom(6),
        gcFrames,
        spikes,
        gcEvents,
        gameThreadAllocBytesPerFrame: gameThreadAlloc / Math.max(frames, 1),
        offThreadAllocBytesPerFrame: (totalAlloc - gameThreadAlloc) / Math.max(frames, 1),
        gcInfo: usable.at(-1).gcInfo ?? null,
        movedPx: usable.reduce((a, s) => a + (s.playerMovedPx ?? 0), 0),
        windows: usable.length,
        frames,
        placed: usable.at(-1).startposPlaced,
        warm: usable.at(-1).startposWarm,
        cold: usable.at(-1).startposCold,
        map: usable.at(-1).map,
        room: usable.at(-1).room,
        avgMs: mean(usable.map((s) => s.frameMs.avg)),
        p50Ms: mean(usable.map((s) => s.frameMs.p50)),
        // p95/p99/worst are maxed, not averaged: the question is how bad the
        // spikes get, and averaging windows hides exactly that.
        p95Ms: Math.max(...usable.map((s) => s.frameMs.p95)),
        p99Ms: Math.max(...usable.map((s) => s.frameMs.p99)),
        worstMs: Math.max(...usable.map((s) => s.frameMs.worst)),
        gen0: usable.reduce((a, s) => a + s.gc.gen0, 0),
        gen1: usable.reduce((a, s) => a + s.gc.gen1, 0),
        gen2: usable.reduce((a, s) => a + s.gc.gen2, 0),
        allocBytesPerFrame: usable.reduce((a, s) => a + s.gc.allocatedBytes, 0) / Math.max(frames, 1),
        allocMin: Math.min(...usable.map((s) => s.gc.allocatedBytes)),
        allocMax: Math.max(...usable.map((s) => s.gc.allocatedBytes)),
        allocSpread: Math.max(...usable.map((s) => s.gc.allocatedBytes)) /
            Math.max(Math.min(...usable.map((s) => s.gc.allocatedBytes)), 1),
        heapBytes: usable.at(-1).gc.totalMemoryBytes,
        bucketMsPerFrame: Object.fromEntries(
            Object.entries(buckets).map(([k, v]) => [k, v / Math.max(frames, 1)])),
    });
}

// A run whose windows disagree wildly about allocation is not in steady state:
// on this harness that means the StartPos persistence worker was still writing
// snapshots when the measurement started, and its allocation is being counted as
// gameplay cost. Loud, because the number is otherwise plausible and wrong.
for (const run of runs) {
    if (run.allocSpread > 3) {
        console.error(`WARNING ${run.label}: per-window allocation varies ${run.allocSpread.toFixed(1)}x ` +
            `(${(run.allocMin / 1048576).toFixed(1)} MB to ${(run.allocMax / 1048576).toFixed(1)} MB). ` +
            "The run was not in steady state. Increase the settle time after placing slots.");
    }
}

if (asJson) {
    console.log(JSON.stringify(runs, null, 2));
    process.exit(0);
}

// --gcdetail dumps what the runtime itself said about every collection in the
// kept windows, plus every slow frame with the generation counters that moved
// during it. This is the raw evidence behind the summary table; read it when
// the summary says something surprising.
if (asGcDetail) {
    for (const run of runs.sort((a, b) => a.label.localeCompare(b.label))) {
        console.log(`\n=== ${run.label}  (${run.frames} frames, ${run.windows} windows, build ${run.buildId})`);
        if (run.gcConfig) {
            const v = run.gcConfig.variables ?? {};
            console.log(`  config: serverGC=${run.gcConfig.serverGC} latency=${run.gcConfig.latencyMode} ` +
                `concurrentGC=${v.ConcurrentGC} heapCount=${v.HeapCount ?? "unset"} ` +
                `hardLimit=${v.GCHeapHardLimit ?? "unset"} events=${run.gcConfig.gcEvents}`);
        }
        const starts = run.gcEvents.filter((e) => e.k === "start");
        const pauses = run.gcEvents.filter((e) => e.k === "pause");
        if (starts.length > 0) {
            const byKind = new Map();
            for (const e of starts) {
                const key = `gen${e.gen} ${typeName(e.type)} ${reasonName(e.reason)}`;
                byKind.set(key, (byKind.get(key) ?? 0) + 1);
            }
            console.log("  collections by generation / type / reason:");
            for (const [key, count] of [...byKind].sort((a, b) => b[1] - a[1])) {
                console.log(`    ${String(count).padStart(5)}  ${key}`);
            }
        } else {
            console.log("  collections: no runtime GC events captured");
        }
        if (pauses.length > 0) {
            const sorted = [...pauses].map((p) => p.ms).sort((a, b) => b - a);
            const total = sorted.reduce((a, b) => a + b, 0);
            console.log(`  stop-the-world pauses: n=${sorted.length} total=${total.toFixed(1)} ms ` +
                `worst=${sorted[0].toFixed(1)} ms, top: ${sorted.slice(0, 8).map((v) => v.toFixed(1)).join(", ")}`);
        }
        if (run.spikes.length > 0) {
            console.log("  slow frames (>33 ms) with the collections that landed inside them:");
            for (const s of run.spikes.sort((a, b) => b.ms - a.ms).slice(0, 15)) {
                console.log(`    ${s.ms.toFixed(1).padStart(8)} ms   gen0+${s.gen0} gen1+${s.gen1} gen2+${s.gen2}` +
                    (s.gen0 === 0 ? "   (no collection in this frame)" : ""));
            }
        }
        if (run.gcInfo) {
            for (const [kind, info] of Object.entries(run.gcInfo)) {
                if (info.index === 0) {
                    console.log(`  gcInfo.${kind}: never happened in this process`);
                    continue;
                }
                console.log(`  gcInfo.${kind}: #${info.index} gen${info.generation} ` +
                    `concurrent=${info.concurrent} compacted=${info.compacted} ` +
                    `pauses=[${info.pauseMs.map((v) => v.toFixed(1)).join(", ")}] ms ` +
                    `heap=${(info.heapBytes / 1048576).toFixed(0)} MB ` +
                    `frag=${(info.fragmentedBytes / 1048576).toFixed(0)} MB ` +
                    `load=${(info.memoryLoadBytes / 1048576).toFixed(0)} MB` +
                    (info.generations?.length >= 4
                        ? `  LOH after=${(info.generations[3].sizeAfter / 1048576).toFixed(0)} MB`
                        : ""));
            }
        }
    }
    process.exit(0);
}

// --gc is the pass/fail table. Rows are cells (the run label with its -rN
// repetition suffix removed) so that run-to-run spread is visible rather than
// averaged away, and every count is a count of frames, not an average over
// them. "gc>16.7" is the number the target of zero is about: frames that both
// exceeded one vsync interval and contained a collection.
if (asGc) {
    const cells = new Map();
    for (const run of runs) {
        const cell = run.label.replace(/-r\d+$/, "");
        if (!cells.has(cell)) cells.set(cell, []);
        cells.get(cell).push(run);
    }

    const stat = (values) => {
        const m = mean(values);
        const sd = values.length < 2
            ? 0
            : Math.sqrt(values.reduce((a, v) => a + (v - m) ** 2, 0) / values.length);
        return { m, sd };
    };
    const fmt = ({ m, sd }, digits = 1) =>
        `${m.toFixed(digits)}+-${sd.toFixed(digits)}`;

    const gcColumns = [
        ["cell", (rs) => rs[0].label.replace(/-r\d+$/, ""), 22],
        ["n", (rs) => String(rs.length), 2],
        ["frames", (rs) => String(Math.round(mean(rs.map((r) => r.frames)))), 6],
        [">16.7", (rs) => fmt(stat(rs.map((r) => r.framesOver16)), 0), 12],
        [">33", (rs) => fmt(stat(rs.map((r) => r.framesOver33)), 0), 10],
        [">100", (rs) => fmt(stat(rs.map((r) => r.framesOver100)), 0), 9],
        ["worst ms", (rs) => fmt(stat(rs.map((r) => r.worstMs)), 0), 12],
        ["gen2", (rs) => fmt(stat(rs.map((r) => r.gen2)), 1), 9],
        ["gc>16.7", (rs) => fmt(stat(rs.map((r) => r.gcFrames.over16)), 0), 12],
        ["gc>33", (rs) => fmt(stat(rs.map((r) => r.gcFrames.over33)), 0), 10],
        ["gc>100", (rs) => fmt(stat(rs.map((r) => r.gcFrames.over100)), 0), 9],
        ["gen2>100", (rs) => fmt(stat(rs.map((r) => r.gcFrames.gen2Over100)), 0), 9],
        ["KB/f", (rs) => fmt(stat(rs.map((r) => r.allocBytesPerFrame / 1024)), 0), 10],
        ["offthr KB/f", (rs) => fmt(stat(rs.map((r) => r.offThreadAllocBytesPerFrame / 1024)), 0), 12],
        ["heap MB", (rs) => fmt(stat(rs.map((r) => r.heapBytes / 1048576)), 0), 10],
    ];

    const pad2 = (t, w) => String(t).padEnd(w).slice(0, Math.max(w, String(t).length));
    console.log(gcColumns.map(([n, , w]) => pad2(n, w)).join("  "));
    console.log(gcColumns.map(([, , w]) => "-".repeat(w)).join("  "));
    for (const [, rs] of [...cells].sort((a, b) => a[0].localeCompare(b[0]))) {
        console.log(gcColumns.map(([, get, w]) => pad2(get(rs), w)).join("  "));
    }
    console.log("\nCounts are per run over the frames listed. +- is population sd across the runs in the cell.");
    console.log("gc>N counts frames that exceeded N ms AND had a collection complete inside them.");
    console.log("offthr KB/f is process allocation minus game-thread allocation: the persistence worker's share.");
    process.exit(0);
}

if (asGate) {
    let violations = 0;
    for (const run of runs) {
        const checks = [
            ["p50 ms", run.p50Ms, BUDGETS.p50Ms],
            ["alloc KB/frame", run.allocBytesPerFrame / 1024, BUDGETS.allocKbPerFrame],
            ["gen2 per 1000 frames", run.gen2 / (run.frames / 1000), BUDGETS.gen2PerThousandFrames],
        ];
        for (const [name, actual, budget] of checks) {
            if (actual > budget) {
                console.log(`FAIL  ${run.label}: ${name} = ${actual.toFixed(3)}, budget ${budget}`);
                violations++;
            }
        }
    }
    console.log(violations === 0
        ? `PASS  ${runs.length} run(s) within budget`
        : `${violations} budget violation(s)`);
    process.exit(violations === 0 ? 0 : 1);
}

const columns = [
    ["run", (r) => r.label, 26],
    ["build", (r) => r.buildId, 13],
    ["N", (r) => String(r.placed), 3],
    ["warm", (r) => String(r.warm), 4],
    ["cold", (r) => String(r.cold), 4],
    ["frames", (r) => String(r.frames), 6],
    ["p50 ms", (r) => r.p50Ms.toFixed(2), 7],
    ["p95 ms", (r) => r.p95Ms.toFixed(2), 7],
    ["p99 ms", (r) => r.p99Ms.toFixed(2), 7],
    ["worst ms", (r) => r.worstMs.toFixed(2), 8],
    ["gen0", (r) => String(r.gen0), 5],
    ["gen2", (r) => String(r.gen2), 5],
    ["KB/frame", (r) => (r.allocBytesPerFrame / 1024).toFixed(1), 8],
    ["heap MB", (r) => (r.heapBytes / 1048576).toFixed(1), 8],
];

const pad = (text, width) => String(text).padEnd(width).slice(0, Math.max(width, String(text).length));
console.log(columns.map(([name, , w]) => pad(name, w)).join("  "));
console.log(columns.map(([, , w]) => "-".repeat(w)).join("  "));
for (const run of runs.sort((a, b) => a.label.localeCompare(b.label))) {
    console.log(columns.map(([, get, w]) => pad(get(run), w)).join("  "));
}

const bucketNames = [...new Set(runs.flatMap((r) => Object.keys(r.bucketMsPerFrame)))].sort();
if (bucketNames.length > 0) {
    console.log("\nper-subsystem ms per frame");
    console.log(pad("run", 26) + "  " + bucketNames.map((n) => pad(n, 22)).join("  "));
    for (const run of runs) {
        console.log(pad(run.label, 26) + "  " +
            bucketNames.map((n) => pad((run.bucketMsPerFrame[n] ?? 0).toFixed(3), 22)).join("  "));
    }
}
