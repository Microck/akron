#!/usr/bin/env bash
# Akron StartPos performance harness.
#
# Deploys an Akron.zip to the remote Linux Mint test machine and runs one
# deterministic CelesteTAS movement scenario at several StartPos slot counts, in
# both warm (slots captured this session) and cold (slots reloaded from disk)
# states, then pulls the JSONL perf records back into ./.tmp-perf/ and prints a
# comparison table.
#
# One command, end to end:
#   scripts/akron-perf/run.sh
#
# Why a TAS and not synthetic keystrokes: frame-time comparisons across slot
# counts are only meaningful if the player does bit-identical things in every
# run. xdotool keystrokes are not frame-accurate; CelesteTAS playback is, and
# Akron already exposes akron_tas_file + akron_play_tas to the automation queue.
#
# Why warm and cold: AkronSaveLoadService.HasRuntimeState short-circuits on the
# in-memory RuntimeSlots dictionary, so a slot captured this session never
# touches the disk. The warm/cold delta is the direct measurement of the
# per-frame SHA-256 + File.Exists cost on cold slots.
#
# Why a third mode, during: warm and cold both wait for the background snapshot
# worker to finish before measuring anything, which measures a game that is no
# longer doing the thing under investigation. "during" starts scripted playback
# immediately after the slots are placed, so the 26 s of gameplay overlaps the
# worker's allocation burst. That overlap is the state a player is in for the
# first ~40 s after placing slots, and it is the only state in which the
# reported stutter reproduces. Check the offthr KB/f column in --gc output to
# confirm the overlap actually happened in a given run.
#
# Why one game boot per scenario: CelesteTAS parks on the final frame of a TAS
# file in its paused state, and while it is parked the Level update hook stops
# running. Akron's automation queue is driven from that hook when the scene is a
# Level (AkronModule.EngineOnUpdate returns early for Level scenes), so nothing
# can be sent to the game after playback ends. The harness therefore never talks
# to the game after akron_play_tas: it lets the recorder flush its per-window
# records, kills the process, and boots again for the next scenario. That also
# means no scenario inherits another scenario's managed heap.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

# The test box is somebody's machine, so nothing about it is hardcoded here.
# Every one of these must come from the environment and the script stops with a
# named variable rather than guessing at a host, an account or a password.
HOST="${AKRON_PERF_HOST:?set AKRON_PERF_HOST to the test box address}"
USER_NAME="${AKRON_PERF_USER:?set AKRON_PERF_USER to the account on that box}"
export SSHPASS="${AKRON_PERF_PASSWORD:?set AKRON_PERF_PASSWORD, or use an ssh key and drop sshpass}"
KNOWN_HOSTS="${AKRON_PERF_KNOWN_HOSTS:?set AKRON_PERF_KNOWN_HOSTS to a known_hosts file containing the test host key}"
[ -f "$KNOWN_HOSTS" ] || { echo "known_hosts file does not exist: ${KNOWN_HOSTS}" >&2; exit 1; }
LAUNCH_DIR="${AKRON_PERF_LAUNCH_DIR:?set AKRON_PERF_LAUNCH_DIR to the Celeste install on that box}"
GAME_ROOT="${AKRON_PERF_GAME_ROOT:-${LAUNCH_DIR}/files/game-root}"
WINE_PREFIX="${AKRON_PERF_WINE_PREFIX:-${LAUNCH_DIR}/wine-prefix}"
AUTO_DIR="${GAME_ROOT}/Saves/AkronAutomation"
PERF_DIR="${GAME_ROOT}/Saves/.tmp-perf"
TOKEN="${AKRON_PERF_TOKEN:-akron-perf-harness-$(date -u +%Y%m%d)-token}"

ZIP="${AKRON_PERF_ZIP:-${REPO_ROOT}/Akron.zip}"
COUNTS="${AKRON_PERF_COUNTS:-0 1 3 5 9 15}"
MODES="${AKRON_PERF_MODES:-warm cold}"
REPS="${AKRON_PERF_REPS:-1}"
# The scenario file is 1560 input frames, 26 s at 60 fps. Recording is stopped
# by killing the game, so this only has to outlast playback.
PLAY_SECONDS="${AKRON_PERF_PLAY_SECONDS:-30}"
AREA="${AKRON_PERF_AREA:-1}"
LABEL="${AKRON_PERF_LABEL:-run}"
OUT_DIR="${AKRON_PERF_OUT:-${REPO_ROOT}/.tmp-perf}"
SKIP_BUILD=0
SKIP_DEPLOY=0
STRESS=0
# The recorder subscribes to the CoreCLR GC event source while recording, which
# is the only part of it that does work outside the game thread. --no-gcevents
# runs the identical scenario without that subscription, so its cost can be
# measured instead of assumed.
GC_EVENTS=1

while [ $# -gt 0 ]; do
    case "$1" in
        --skip-build) SKIP_BUILD=1 ;;
        --skip-deploy) SKIP_BUILD=1; SKIP_DEPLOY=1 ;;
        --stress) STRESS=1 ;;
        --no-gcevents) GC_EVENTS=0 ;;
        --zip) ZIP="$2"; SKIP_BUILD=1; shift ;;
        --counts) COUNTS="$2"; shift ;;
        --modes) MODES="$2"; shift ;;
        --reps) REPS="$2"; shift ;;
        --play-seconds) PLAY_SECONDS="$2"; shift ;;
        --label) LABEL="$2"; shift ;;
        --area) AREA="$2"; shift ;;
        --out) OUT_DIR="$2"; shift ;;
        -h|--help) sed -n '2,30p' "$0"; exit 0 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
    shift
done

say() { printf '\n== %s %s\n' "$(date -u +%H:%M:%S)" "$*"; }
SSH_HOST_KEY_OPTIONS=(-o StrictHostKeyChecking=yes -o "UserKnownHostsFile=${KNOWN_HOSTS}" -o ConnectTimeout=20)
rsh() { sshpass -e ssh "${SSH_HOST_KEY_OPTIONS[@]}" "${USER_NAME}@${HOST}" "$@"; }
rcp() { sshpass -e scp "${SSH_HOST_KEY_OPTIONS[@]}" "$@"; }

# ---------------------------------------------------------------- build/deploy

if [ "$SKIP_BUILD" -eq 0 ]; then
    say "Building Akron.zip"
    make build
fi

# stop_game also tears down wineserver and any winedbg the crash handler left
# behind. Without that, the next boot dies in MonoMod with "Access denied" out
# of NtProcessManager.GetModules: the stale wineserver session hands the new
# process a prefix it cannot enumerate modules in. Every leftover winedbg also
# opens a Wine Debugger window on the tester's desktop.
stop_game() {
    rsh "export WINEPREFIX=${WINE_PREFIX}
         pkill -u ${USER_NAME} -f '[C]eleste.exe' || true
         pkill -u ${USER_NAME} -f '[E]verestSplash' || true
         sleep 5
         pkill -9 -u ${USER_NAME} -f '[C]eleste.exe' || true
         pkill -u ${USER_NAME} -f '[w]inedbg' || true
         sleep 2
         /usr/bin/wineserver -k 2>/dev/null || true
         sleep 6" >/dev/null 2>&1 || true
}

if [ "$SKIP_DEPLOY" -eq 0 ]; then
    LOCAL_SHA="$(sha256sum "$ZIP" | cut -d' ' -f1)"
    say "Deploying ${ZIP} (${LOCAL_SHA})"
    stop_game
    # Existing convention in Mods/ is Akron.zip.before-<change>-<date>. This is
    # the only rollback path on that box, so it is taken once, non-destructively.
    rsh "cp -n '${GAME_ROOT}/Mods/Akron.zip' '${GAME_ROOT}/Mods/Akron.zip.before-perf-harness-$(date -u +%Y%m%d)' || true"
    rcp "$ZIP" "${USER_NAME}@${HOST}:/tmp/Akron-perf.zip"
    REMOTE_SHA="$(rsh "mv /tmp/Akron-perf.zip '${GAME_ROOT}/Mods/Akron.zip'; sha256sum '${GAME_ROOT}/Mods/Akron.zip' | cut -d' ' -f1")"
    if [ "$REMOTE_SHA" != "$LOCAL_SHA" ]; then
        echo "deploy sha mismatch: local=${LOCAL_SHA} remote=${REMOTE_SHA}" >&2
        exit 1
    fi
fi

say "Uploading the TAS scenario"
# Records are collected per scenario with a tag glob, so records left over from
# an earlier sweep with the same tag would be pulled back in and reported as if
# they belonged to this one.
rsh "mkdir -p '${GAME_ROOT}/Saves/AkronPerfScenario' '${PERF_DIR}'; rm -f '${PERF_DIR}'/*.jsonl"
rcp scripts/akron-perf/scenario.tas "${USER_NAME}@${HOST}:${GAME_ROOT}/Saves/AkronPerfScenario/scenario.tas"
TAS_PATH="${GAME_ROOT}/Saves/AkronPerfScenario/scenario.tas"

# ------------------------------------------------------------------ game loop

launch_game() {
    # setsid prevents Wine children from keeping the SSH channel open after launch.
    # The local timeout provides a second bound if detachment fails; the poll below
    # remains the authority on whether boot succeeded.
    # Everest appends to log.txt and only rotates it into LogHistory at boot, so
    # a stale "Loaded assembly Akron" from the previous scenario would satisfy
    # the poll instantly. The marker makes the poll require a log written after
    # this launch.
    rsh "touch /tmp/akron-perf-launch.marker" >/dev/null
    timeout 45 sshpass -e ssh "${SSH_HOST_KEY_OPTIONS[@]}" "${USER_NAME}@${HOST}" \
      "cd ${LAUNCH_DIR} && setsid env \
      AKRON_AUTOMATION_ENABLED=1 AKRON_AUTOMATION_SESSION_TOKEN='${TOKEN}' \
      DISPLAY=:0 XAUTHORITY=/home/${USER_NAME}/.Xauthority \
      XDG_RUNTIME_DIR=/run/user/1000 DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus \
      PATH=/home/${USER_NAME}/.local/bin:/usr/local/bin:/usr/bin:/bin \
      nohup ./start.n-w.sh >/tmp/akron-perf-launch.log 2>&1 </dev/null & echo launched" >/dev/null 2>&1 || true

    # Everest loads mods at boot, so poll for Akron rather than sleeping blind.
    local ok=1 i
    for i in $(seq 1 60); do
        if rsh "[ '${GAME_ROOT}/log.txt' -nt /tmp/akron-perf-launch.marker ] && grep -q 'Loaded assembly Akron' '${GAME_ROOT}/log.txt' 2>/dev/null"; then ok=0; break; fi
        sleep 5
    done
    if [ "$ok" -ne 0 ]; then echo "game did not report Akron loaded" >&2; return 1; fi

    # The one silent failure mode of this loop: without the automation env on the
    # process every command.txt is ignored and nothing is written anywhere.
    if ! rsh "for pid in \$(pgrep -u ${USER_NAME} -f '[C]eleste.exe'); do tr '\\0' '\\n' < /proc/\$pid/environ 2>/dev/null | grep -q '^AKRON_AUTOMATION_ENABLED=1' && exit 0; done; exit 1"; then
        echo "automation env is not set on the Celeste process" >&2
        return 1
    fi
    sleep 25
}

# send <body> [timeout] - writes one automation run and waits for its result.
send() {
    local body="$1"
    local timeout_s="${2:-90}"
    # last-result.txt is removed first. Without that, the poll below can see the
    # previous run's "complete" and return before this run has started, which
    # silently desynchronises the whole scenario.
    rsh "rm -f '${AUTO_DIR}/last-result.txt'
         printf '%s\n' 'token: ${TOKEN}' > '${AUTO_DIR}/command.txt.part'
         cat >> '${AUTO_DIR}/command.txt.part' <<'AKRONEOF'
${body}
AKRONEOF
         mv '${AUTO_DIR}/command.txt.part' '${AUTO_DIR}/command.txt'" >/dev/null
    local i
    for i in $(seq 1 "$timeout_s"); do
        if rsh "[ ! -f '${AUTO_DIR}/command.txt' ] && head -1 '${AUTO_DIR}/last-result.txt' 2>/dev/null | grep -qE 'complete|rejected|failed'"; then
            rsh "cat '${AUTO_DIR}/last-result.txt'"
            return 0
        fi
        sleep 2
    done
    echo "automation run timed out after $((timeout_s * 2))s" >&2
    return 1
}

enter_level() {
    send "akron_qa_enter_level ${AREA} normal 0" 60 >/dev/null
    sleep 10
    # The HUD StartPos label is the per-frame O(N) path under test: it calls
    # DescribeStartPosIndex -> GetStartPositions -> HasRuntimeState -> HasSnapshot
    # once per rendered frame. Two switches gate it and both default off on the
    # tester's box, so both are set explicitly rather than inherited.
    # AkronHudRenderer.cs:71 reads LabelSystemVisible as a master switch, and
    # AkronHudRenderer.cs:168 reads StartPosShowLabel. With either one off the
    # StartPos list is never built during gameplay and the run measures nothing.
    send "akron_feature labels on
akron_startpos label on" 30 >/dev/null
}

# clear_slots - drops every configurable StartPos slot and its disk snapshot so a
# scenario starts from a known N=0 state. Chunked because the queue runs one
# command per frame and caps a run at 64 commands.
clear_slots() {
    local i body=""
    for i in $(seq 1 15); do body="${body}akron_startpos clear ${i}
"; done
    send "$body" 90 >/dev/null || true
}

# place_slots <n> [settle] - captures n StartPos slots. Every capture is a full
# runtime savestate plus a disk snapshot, which is the retained cost under test.
#
# settle=auto (default) waits for the background snapshot worker to finish before
# anything is measured. That worker is heavy: measured on this box it allocates
# 400-600 MB per second for roughly 40 seconds while it serialises the captured
# Level graphs, so a measurement started before it finishes reports the worker's
# allocation as if it were gameplay cost. The wait scales with N because the work
# does, and a cold restart needs it too, or the cold run finds no files on disk
# and is not actually cold.
#
place_slots() {
    local n="$1"
    [ "$n" -eq 0 ] && return 0
    local i body=""
    for i in $(seq 1 "$n"); do body="${body}akron_startpos set ${i}
"; done
    send "$body" 150 >/dev/null
    sleep $((20 + 5 * n))
}

# place_and_play_during <n> <label> - the reproduction state, and the only mode
# in which the worker and gameplay actually overlap.
#
# A first attempt sent the slot placements, skipped the settle sleep and then
# ran the normal record_and_play. It did not overlap: each `send` is its own
# file write plus a poll loop, and by the time playback started the worker had
# already drained. Measured, the four recorded windows before playback carried
# 253-378 MB of off-game-thread allocation each and the thirteen playback
# windows carried 0.1 MB. The run looked like a "during" run and was a settled
# run with an unsettled prelude.
#
# The fix is not a shorter sleep, it is one command file. The automation queue
# consumes exactly one command per game frame, so placing the slots and starting
# playback in a single run puts the first TAS frame fifteen frames after the
# first capture. Check offthr KB/f in --gc output: if it is not large, this run
# did not overlap and must not be reported as a during run.
#
# --stress does not apply to this mode. Stress has to be armed after playback
# starts, and there is nothing to arm it from here: this mode's whole point is
# that it says everything to the game in one file before playback begins.
place_and_play_during() {
    local n="$1"
    local label="$2"
    local i body=""
    body="akron_tas_file ${TAS_PATH}
"
    if [ "$GC_EVENTS" -eq 0 ]; then
        body="${body}akron_perf gcevents off
"
    fi
    body="${body}akron_perf reset
akron_perf record ${label}
"
    for i in $(seq 1 "$n"); do body="${body}akron_startpos set ${i}
"; done
    body="${body}akron_play_tas"
    send "$body" 150
    sleep "$PLAY_SECONDS"
}

record_and_play() {
    local label="$1"
    # Stress mode runs the overlay churn loop concurrently with scripted
    # gameplay: visibility toggled every 15 frames, UI mutated every frame, and
    # a forced full GC every 120 frames. It only exists in Debug builds
    # (Source/Module/akron-module-stress.cs is inside #if DEBUG), so --stress
    # requires a Debug --zip. Frame-time and allocation numbers from a stress
    # run are NOT comparable with a normal run, because that forced GC.Collect
    # flattens exactly the gen2 signal the perf sweep measures. Use it to prove
    # the game survives the churn, not to compare against a baseline.
    # Stress mode is armed AFTER the recorder and the TAS are started, not
    # before. Once it is on it stalls the automation queue: measured on the box,
    # every command sent after `akron_qa_stress on` timed out, so anything the
    # harness still needs to say to the game has to be said first.
    send "akron_tas_file ${TAS_PATH}" 30 >/dev/null
    if [ "$GC_EVENTS" -eq 0 ]; then
        send "akron_perf gcevents off" 30 >/dev/null
    fi
    send "akron_perf reset
akron_perf record ${label}
akron_startpos status" 60
    send "akron_play_tas" 30 >/dev/null
    if [ "$STRESS" -eq 1 ]; then
        local stress_out
        stress_out="$(send "akron_qa_stress on 20260813" 40)"
        if ! printf '%s' "$stress_out" | grep -q 'stress: on'; then
            echo "stress mode did not start; is this a Debug build?" >&2
            printf '%s\n' "$stress_out" | head -5 >&2
            return 1
        fi
    fi
    sleep "$PLAY_SECONDS"
    if [ "$STRESS" -eq 1 ]; then
        if rsh "pgrep -u ${USER_NAME} -f '[C]eleste.exe' >/dev/null"; then
            echo "stress: PASS - the game survived ${PLAY_SECONDS}s of scripted play under overlay churn"
            echo "stress: telemetry for this run is best-effort. Stress mode stalls the automation"
            echo "stress: queue, so the recorder cannot be stopped cleanly and the final window may be lost." 
        else
            echo "stress: FAIL - the game died during the run" >&2
            return 1
        fi
    fi
}

# --------------------------------------------------------------------- driver

mkdir -p "$OUT_DIR"
for rep in $(seq 1 "$REPS"); do
for count in $COUNTS; do
    for mode in $MODES; do
        tag="${LABEL}-n${count}-${mode}-r${rep}"
        say "Scenario ${tag}"
        stop_game
        launch_game || { echo "skipping ${tag}: launch failed" >&2; continue; }
        enter_level
        clear_slots
        # during: capture the slots and start playback from one command file, so
        # the 26 s of gameplay lands inside the worker's allocation burst. warm
        # and cold both wait the worker out first.
        if [ "$mode" = "during" ]; then
            place_and_play_during "$count" "$tag"
            stop_game
            rcp "${USER_NAME}@${HOST}:${PERF_DIR}/*${tag}.jsonl" "$OUT_DIR/" || true
            continue
        fi
        place_slots "$count"
        if [ "$mode" = "cold" ]; then
            # Slots exist on disk now. Restarting empties RuntimeSlots, so every
            # HasRuntimeState probe has to reach the snapshot layer.
            stop_game
            launch_game || { echo "skipping ${tag}: relaunch failed" >&2; continue; }
            enter_level
        fi
        record_and_play "$tag" || true
        stop_game
        rcp "${USER_NAME}@${HOST}:${PERF_DIR}/*${tag}.jsonl" "$OUT_DIR/" || true
    done
done
done

say "Comparison table"
node "${REPO_ROOT}/scripts/akron-perf/report.mjs" "$OUT_DIR"
