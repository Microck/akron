#!/usr/bin/env bash
# Akron in-game verification harness.
#
# Runs the checks that cannot be made in a unit test because they need a real
# Celeste process: StartPos loading across rooms and across maps, persistent
# slots surviving a game restart, log-level filtering, and StartPos load
# latency.
#
# One command, end to end:
#   scripts/akron-verify/run.sh
#
# Every check reports PASS or FAIL with the evidence it used. The exit code is
# the number of failed checks, so this is usable as a gate.
#
# What akron_qa_pixel_checkpoint can and cannot answer, because this harness is
# where the answer was got wrong once already.
#
# It hashes Celeste's own 320x180 gameplay render target, which is independent of
# the X session, window focus, compositing and scaling. It is not a restore
# oracle, in either direction:
#
#  - It samples whichever frame is rendered next, and the room composite advances
#    every update. Two captures of one static room with no load between them
#    differed in 88-98% of their pixels on the Windows machine, so a hash is only
#    ever comparable against another hash taken at the same point of the same
#    kind of frame.
#  - On the frame a StartPos load presents, the gameplay buffer normally holds
#    bytes Akron itself wrote back out of the snapshot: the restore puts the saved
#    gameplay buffers back where the live targets still take them and arms the
#    Level one, and Level.Render puts those bytes into the target before this
#    capture reads it. So a match there is the saved frame round-tripping through
#    the file, and says nothing about whether the room behind it was rebuilt
#    correctly. That presentation is best effort, so a load frame is either the
#    presented bytes or an ordinary rendered frame and the hash does not say
#    which; a resized target or a missing saved buffer leaves a warning in
#    akron-current.log, a scene change leaves nothing.
#
# The checks below therefore compare one load frame against another load frame,
# which is what that second point makes meaningful, and they are worded for it. Do
# not compare a capture-time frame against a restored one: the two are taken at
# different points and legitimately differ.
#
# What can be asserted about a restore is the load probe's state output - position,
# facing, dashes, state id, session flag, session counter - which is deterministic.
# Those named fields, not the whole rebuilt room. Check 1 prints that output and
# asserts none of it, and checks 2 and 3 do not print it at all, which is a hole worth
# knowing about:
# akron_qa_startpos_load_probe arms its capture whether or not the load succeeded, so
# a refused load still produces a hash and check 1 can reach a PASS on it. None of
# the PASS lines below claims the load succeeded, for that reason. Asserting those
# fields is the next thing this harness needs.

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

# The test box is somebody's machine, so nothing about it is hardcoded here.
# Every one of these must come from the environment and the script stops with a
# named variable rather than guessing at a host, an account or a password.
HOST="${AKRON_PERF_HOST:?set AKRON_PERF_HOST to the test box address}"
USER_NAME="${AKRON_PERF_USER:?set AKRON_PERF_USER to the account on that box}"
export SSHPASS="${AKRON_PERF_PASSWORD:?set AKRON_PERF_PASSWORD, or use an ssh key and drop sshpass}"
LAUNCH_DIR="${AKRON_PERF_LAUNCH_DIR:?set AKRON_PERF_LAUNCH_DIR to the Celeste install on that box}"
GAME_ROOT="${AKRON_PERF_GAME_ROOT:-${LAUNCH_DIR}/files/game-root}"
WINE_PREFIX="${AKRON_PERF_WINE_PREFIX:-${LAUNCH_DIR}/wine-prefix}"
AUTO_DIR="${GAME_ROOT}/Saves/AkronAutomation"
TOKEN="${AKRON_PERF_TOKEN:-akron-verify-harness-$(date -u +%Y%m%d)-token}"
AREA="${AKRON_VERIFY_AREA:-1}"
AWAY_ROOM="${AKRON_VERIFY_AWAY_ROOM:-3}"
OTHER_AREA="${AKRON_VERIFY_OTHER_AREA:-2}"
ZIP="${AKRON_VERIFY_ZIP:-${REPO_ROOT}/Akron.zip}"
SKIP_BUILD=0
SKIP_DEPLOY=0
ONLY_STARTPOS=0

while [ $# -gt 0 ]; do
    case "$1" in
        --skip-build) SKIP_BUILD=1 ;;
        --skip-deploy) SKIP_BUILD=1; SKIP_DEPLOY=1 ;;
        --zip) ZIP="$2"; SKIP_BUILD=1; shift ;;
        --area) AREA="$2"; shift ;;
        --only-startpos) ONLY_STARTPOS=1 ;;
        -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
    shift
done

FAILURES=0
say() { printf '\n== %s %s\n' "$(date -u +%H:%M:%S)" "$*"; }
pass() { printf 'PASS  %s\n' "$*"; }
fail() { printf 'FAIL  %s\n' "$*"; FAILURES=$((FAILURES + 1)); }
rsh() { sshpass -e ssh -o StrictHostKeyChecking=no -o ConnectTimeout=20 "${USER_NAME}@${HOST}" "$@"; }
rcp() { sshpass -e scp -o StrictHostKeyChecking=no "$@"; }

# send <body> [timeout] - one automation run, waits for its result, echoes it.
# last-result.txt is removed first so the poll cannot see the previous run's
# terminal status and return before this run has started.
send() {
    local body="$1" timeout_s="${2:-60}" i
    rsh "rm -f '${AUTO_DIR}/last-result.txt'
         printf '%s\n' 'token: ${TOKEN}' > '${AUTO_DIR}/command.txt.part'
         cat >> '${AUTO_DIR}/command.txt.part' <<'AKRONEOF'
${body}
AKRONEOF
         mv '${AUTO_DIR}/command.txt.part' '${AUTO_DIR}/command.txt'" >/dev/null
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

pixel_hash() {
    rsh "sed -n 's/^sha256=//p' '${AUTO_DIR}/qa-pixel-$1.txt' 2>/dev/null"
}

stop_game() {
    rsh "export WINEPREFIX=${WINE_PREFIX}
         pkill -u ${USER_NAME} -f '[C]eleste.exe' || true
         sleep 5
         pkill -9 -u ${USER_NAME} -f '[C]eleste.exe' || true
         pkill -u ${USER_NAME} -f '[w]inedbg' || true
         sleep 2
         /usr/bin/wineserver -k 2>/dev/null || true
         sleep 6" >/dev/null 2>&1 || true
}

# Kills the game the way a crash does: no graceful signal first, so nothing in
# the process can flush anything on the way out. That is the whole point when
# checking that a setting reached disk before the process died.
#
# The cleanup afterwards is not optional. A SIGKILLed Celeste leaves its
# wineserver and one winedbg --auto per faulted thread behind, and the next boot
# then dies inside Everest's MonoMod hook install instead of loading Akron. A
# plain `wineserver -k` does not clear the winedbg processes, so they are killed
# by name first and the server is killed with -k9 rather than -k.
kill_game_hard() {
    rsh "export WINEPREFIX=${WINE_PREFIX}
         pkill -9 -u ${USER_NAME} -f '[C]eleste.exe' || true
         sleep 3
         pkill -9 -u ${USER_NAME} -f '[w]inedbg' || true
         pkill -9 -u ${USER_NAME} -f '[w]inedevice' || true
         /usr/bin/wineserver -k9 2>/dev/null || true
         sleep 3
         pkill -9 -u ${USER_NAME} -f '[w]ineserver' || true
         sleep 5" >/dev/null 2>&1 || true
}

launch_game() {
    rsh "touch /tmp/akron-verify.marker" >/dev/null
    timeout 45 sshpass -e ssh -o StrictHostKeyChecking=no "${USER_NAME}@${HOST}" \
      "cd ${LAUNCH_DIR} && setsid env \
      AKRON_AUTOMATION_ENABLED=1 AKRON_AUTOMATION_SESSION_TOKEN='${TOKEN}' \
      DISPLAY=:0 XAUTHORITY=/home/${USER_NAME}/.Xauthority \
      XDG_RUNTIME_DIR=/run/user/1000 DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus \
      PATH=/home/${USER_NAME}/.local/bin:/usr/local/bin:/usr/bin:/bin \
      nohup ./start.n-w.sh >/tmp/akron-verify-launch.log 2>&1 </dev/null & echo launched" >/dev/null 2>&1 || true
    local i
    for i in $(seq 1 60); do
        if rsh "[ '${GAME_ROOT}/log.txt' -nt /tmp/akron-verify.marker ] && grep -q 'Loaded assembly Akron' '${GAME_ROOT}/log.txt' 2>/dev/null"; then
            # The automation env failing is completely silent: the service
            # no-ops and writes nothing anywhere, so it is checked every boot.
            if rsh "for pid in \$(pgrep -u ${USER_NAME} -f '[C]eleste.exe'); do tr '\\0' '\\n' < /proc/\$pid/environ 2>/dev/null | grep -q '^AKRON_AUTOMATION_ENABLED=1' && exit 0; done; exit 1"; then
                sleep 25
                return 0
            fi
            echo "automation env is not set on the Celeste process" >&2
            return 1
        fi
        sleep 5
    done
    echo "game did not report Akron loaded" >&2
    return 1
}

enter_level() {
    send "akron_qa_enter_level ${AREA} normal 0" 60 >/dev/null
    sleep 8
}

if [ "$SKIP_BUILD" -eq 0 ]; then
    say "Building"
    make build >/dev/null || { echo "build failed" >&2; exit 1; }
fi

if [ "$SKIP_DEPLOY" -eq 0 ]; then
    say "Deploying $(sha256sum "$ZIP" | cut -d' ' -f1)"
    stop_game
    rsh "cp -n '${GAME_ROOT}/Mods/Akron.zip' '${GAME_ROOT}/Mods/Akron.zip.before-akron-verify-$(date -u +%Y%m%d)' 2>/dev/null || true"
    rcp "$ZIP" "${USER_NAME}@${HOST}:/tmp/Akron-verify.zip"
    LOCAL_SHA="$(sha256sum "$ZIP" | cut -d' ' -f1)"
    REMOTE_SHA="$(rsh "mv /tmp/Akron-verify.zip '${GAME_ROOT}/Mods/Akron.zip'; sha256sum '${GAME_ROOT}/Mods/Akron.zip' | cut -d' ' -f1")"
    [ "$LOCAL_SHA" = "$REMOTE_SHA" ] || { echo "deploy sha mismatch" >&2; exit 1; }
    launch_game || exit 1
fi

# ------------------------------------------------------------ 1. cross-room

say "Check 1: StartPos loads correctly from a different room"
enter_level
# Every capture that is compared here is compared against another capture from
# this run, so a file left by an earlier run is not stale evidence but a false
# PASS: two runs' leftovers match each other whatever happened this time. The tags
# are fixed, so the files have to go before anything writes one.
rsh "rm -f '${AUTO_DIR}'/qa-pixel-verify-*" >/dev/null 2>&1
# And the deletion is checked rather than assumed. A delete that quietly failed would
# leave the comparisons above reading last run's evidence while every message here
# said this run's.
STALE_PIXELS="$(rsh "ls -1 '${AUTO_DIR}'/qa-pixel-verify-* 2>/dev/null | wc -l" | tr -d '\r')"
if [ "${STALE_PIXELS:-1}" != "0" ]; then
    fail "setup: ${STALE_PIXELS:-?} pixel checkpoint file(s) from an earlier run could not be cleared, so every hash comparison below may match stale evidence"
fi
# The setup is sent one step at a time and each step is checked. A single
# batched run hides which command failed, and a reference capture that silently
# produced nothing turns every later comparison into a meaningless empty-string
# match.
# Every slot is cleared first: leftovers from an earlier run change
# startpos-count and make the index assertions ambiguous.
CLEAR_BODY=""
for slot in $(seq 1 15); do CLEAR_BODY="${CLEAR_BODY}akron_startpos clear ${slot}
"; done
send "$CLEAR_BODY" 90 >/dev/null
# These three must be one run. The automation queue executes one command per
# frame, so a single run puts them on adjacent frames. Split across separate
# runs they are seconds apart, and akron_qa_player_state leaves the player in
# mid-air, so by the time `set` arrives the player has fallen or died and the
# capture fails.
SET_OUT="$(send 'akron_qa_player_state 120 140 3 1234 0 0 1 55 Left
akron_qa_session_state akron-verify-flag verify-counter 7
akron_startpos set 1' 90)"
if ! printf '%s' "$SET_OUT" | grep -q 'startpos-set: true'; then
    fail "cross-room setup: akron_startpos set 1 did not report startpos-set: true"
    printf '%s\n' "$SET_OUT" | sed 's/^/      /' | head -20
fi
CAP_OUT="$(send 'akron_qa_startpos_reference_capture 1 verify-ref' 90)"
REF_HASH="$(pixel_hash verify-ref)"
if [ -z "$REF_HASH" ]; then
    printf '      reference capture output:\n'
    printf '%s\n' "$CAP_OUT" | sed 's/^/      /' | head -20
fi
REF_ROOM="$(send 'akron_status' 40 | sed -n 's/^startpos-room: //p')"

send "akron_qa_warp_room ${AWAY_ROOM}" 60 >/dev/null
sleep 4
AWAY_ROOM_ACTUAL="$(send 'akron_status' 40 | sed -n 's|^room: .*/ ||p')"

LOAD_OUT="$(send "akron_qa_startpos_load_probe 1 akron-verify-flag verify-counter verify-xroom" 90)"
XROOM_HASH="$(pixel_hash verify-xroom)"

# XROOM_HASH is the baseline every later load is compared against. It is NOT
# compared with REF_HASH: the reference capture is rendered at capture time,
# before the restore path runs, so the two legitimately differ (animated
# backdrop particles alone guarantee it) and asserting equality there reports a
# failure that is not one. What the later comparisons say is therefore bounded -
# see the note at the top of this file - and what they are worded to claim.
BASELINE_HASH="$XROOM_HASH"
if [ "$AWAY_ROOM_ACTUAL" = "$REF_ROOM" ]; then
    fail "cross-room: the away warp did not leave the StartPos room (${REF_ROOM}), so this check proves nothing"
elif [ -z "$XROOM_HASH" ]; then
    fail "cross-room: no pixel checkpoint after the load, so either the slot did not restore or the capture did not land"
else
    pass "cross-room: warped away to ${AWAY_ROOM_ACTUAL}, asked for slot 1 from there, and a frame was captured (${XROOM_HASH:0:16}); what restored is in the probe output below"
fi
# Anchored on the prefixes the probe really writes. The earlier pattern - startpos-load,
# probe, player, session, flag, counter - matched none of them: every line the probe
# records is qa-startpos-load-probe-* or qa-session-*, so this block printed nothing at
# all, and the only correctness evidence in this check was invisible.
printf '%s\n' "$LOAD_OUT" | grep -E '^qa-(startpos-load-probe|session)' | sed 's/^/      /'

# ------------------------------------------------------------- 2. cross-map

say "Check 2: StartPos survives leaving the map and coming back"
send "akron_qa_enter_level ${OTHER_AREA} normal 0" 90 >/dev/null
sleep 8
OTHER_MAP="$(send 'akron_status' 40 | sed -n 's/^room: //p')"
send "akron_qa_enter_level ${AREA} normal 0" 90 >/dev/null
sleep 8
LOAD_OUT="$(send "akron_qa_startpos_load_probe 1 akron-verify-flag verify-counter verify-xmap" 90)"
XMAP_HASH="$(pixel_hash verify-xmap)"

if [ -z "$OTHER_MAP" ] || [ "${OTHER_MAP%% *}" = "" ]; then
    fail "cross-map: never reached the second map, so this check proves nothing"
elif [ -z "$XMAP_HASH" ] || [ -z "$BASELINE_HASH" ]; then
    # Two missing captures compare equal to each other, which used to read as a PASS
    # for a pair of frames that were never taken.
    fail "cross-map: a pixel checkpoint is missing (cross-room='${BASELINE_HASH:0:16}' cross-map='${XMAP_HASH:0:16}'), so this check proves nothing"
elif [ "$BASELINE_HASH" = "$XMAP_HASH" ]; then
    pass "cross-map: went to ${OTHER_MAP} and came back, and the load frame hashed identically to the cross-room load"
else
    fail "cross-map: a map round trip changed the restored frame (cross-room=${BASELINE_HASH:0:16} cross-map=${XMAP_HASH:0:16})"
fi

# ------------------------------------------------- 3. persistence + latency

say "Check 3: the slot survives a full game restart (cold, disk-backed)"
SNAP_COUNT_BEFORE="$(rsh "ls -1 '${GAME_ROOT}/Saves/AkronStartPos'/*.json.gz 2>/dev/null | wc -l")"
stop_game
launch_game || exit 1
enter_level
COLD_START="$(date +%s%N)"
LOAD_OUT="$(send "akron_qa_startpos_load_probe 1 akron-verify-flag verify-counter verify-cold" 120)"
COLD_MS=$(( ($(date +%s%N) - COLD_START) / 1000000 ))
COLD_HASH="$(pixel_hash verify-cold)"

if [ -z "$COLD_HASH" ]; then
    fail "persistent slot: no pixel checkpoint after a cold load, so either the slot did not restore or the capture did not land"
elif [ "$BASELINE_HASH" = "$COLD_HASH" ]; then
    pass "persistent slot: after a full restart the load frame hashed identically to the warm load (${SNAP_COUNT_BEFORE} snapshots on disk)"
else
    fail "persistent slot: a cold load differs from the warm load (warm=${BASELINE_HASH:0:16} cold=${COLD_HASH:0:16})"
fi
printf '      cold load round trip including automation polling: %s ms\n' "$COLD_MS"

say "Check 4: repeated warm loads are not slower than the first"
WARM_MS=()
for i in 1 2 3; do
    start="$(date +%s%N)"
    send "akron_startpos load 1" 60 >/dev/null
    WARM_MS+=( $(( ($(date +%s%N) - start) / 1000000 )) )
    sleep 2
done
printf '      warm load round trips: %s ms, %s ms, %s ms\n' "${WARM_MS[0]}" "${WARM_MS[1]}" "${WARM_MS[2]}"
if [ "${WARM_MS[2]}" -le $(( ${WARM_MS[0]} * 2 )) ]; then
    pass "load latency: repeated loads did not degrade"
else
    fail "load latency: the third load (${WARM_MS[2]} ms) took more than twice the first (${WARM_MS[0]} ms)"
fi

# ------------------------------------------------------- 5. log-level filter

if [ "$ONLY_STARTPOS" -eq 1 ]; then
    say "Result: ${FAILURES} failed check(s)"
    exit "$FAILURES"
fi

say "Check 5: log-level filtering is a real verbosity ladder"
# This check measures what each level writes over a fixed window, so every level
# needs its own boot and its own empty log. The level is therefore written
# straight into modsettings before booting: that is the cheapest way to start a
# session already at a known level, and what is under test here is the filtering,
# not how the level got set. Check 6 covers how the level gets set.
LOG="${GAME_ROOT}/Saves/AkronLogs/akron-current.log"
SETTINGS="${GAME_ROOT}/Saves/modsettings-Akron.celeste"
ORIGINAL_LEVEL="$(rsh "sed -n 's/^LoggingLevel: *//p' '${SETTINGS}' | head -1")"
printf '      original LoggingLevel on the box: %s\n' "${ORIGINAL_LEVEL:-unknown}"

declare -A LEVEL_LINES
for level in Normal Diagnostic Verbose; do
    stop_game
    rsh "sed -i 's|^LoggingLevel:.*|LoggingLevel: ${level}|' '${SETTINGS}'
         rm -f '${LOG}'" >/dev/null
    launch_game || { fail "log level: could not boot at ${level}"; continue; }
    enter_level
    # The Diagnostic tier rolls per-event records into 60-second summaries, so
    # the window has to be longer than 60 s for the comparison to be fair.
    send "akron_startpos status" 40 >/dev/null
    sleep 70
    COUNT="$(rsh "wc -l < '${LOG}' 2>/dev/null || echo 0")"
    ROLLUP="$(rsh "grep -c 'feature uses recorded:' '${LOG}' 2>/dev/null || echo 0")"
    SEVERITIES="$(rsh "awk '{print \$2}' '${LOG}' 2>/dev/null | sort -u | tr '\n' ' '")"
    LEVEL_LINES[$level]="$COUNT"
    printf '      level=%-11s lines=%-7s rollup-lines=%-4s severities: %s\n' \
        "$level" "$COUNT" "$ROLLUP" "${SEVERITIES:-none}"

    case "$level" in
        Normal)
            if printf '%s' "$SEVERITIES" | grep -qE 'VERBOSE|TRACE|DIAGNOSTIC'; then
                fail "log level: Normal emitted DIAGNOSTIC, VERBOSE or TRACE lines"
            else
                pass "log level: Normal emitted only Normal-tier severities"
            fi ;;
        Diagnostic)
            if printf '%s' "$SEVERITIES" | grep -qE 'VERBOSE|TRACE'; then
                fail "log level: Diagnostic emitted VERBOSE or TRACE lines"
            elif [ "$ROLLUP" -lt 1 ]; then
                fail "log level: Diagnostic emitted no 'feature uses recorded' rollup line, which is the tier's whole point"
            else
                pass "log level: Diagnostic aggregated into ${ROLLUP} rollup line(s) and stayed below VERBOSE"
            fi ;;
        Verbose)
            if printf '%s' "$SEVERITIES" | grep -q 'TRACE'; then
                fail "log level: Verbose emitted TRACE lines"
            else
                pass "log level: Verbose emitted no TRACE lines"
            fi ;;
    esac
done

# The ladder itself: quieter tiers must not out-log louder ones. This is the
# regression that the enum reorder was meant to fix, so it is asserted directly.
if [ "${LEVEL_LINES[Normal]:-0}" -le "${LEVEL_LINES[Diagnostic]:-0}" ] &&
   [ "${LEVEL_LINES[Diagnostic]:-0}" -le "${LEVEL_LINES[Verbose]:-0}" ]; then
    pass "log level: line counts are monotonic Normal(${LEVEL_LINES[Normal]}) <= Diagnostic(${LEVEL_LINES[Diagnostic]}) <= Verbose(${LEVEL_LINES[Verbose]})"
else
    fail "log level: the ladder is not monotonic - Normal=${LEVEL_LINES[Normal]:-?} Diagnostic=${LEVEL_LINES[Diagnostic]:-?} Verbose=${LEVEL_LINES[Verbose]:-?}"
fi

# ------------------------------------------- 6. the level survives a crash

say "Check 6: a log level chosen through the UI path survives a kill -9"
# The reported defect was that choosing a level in the Logging popup did not
# stick: the game was killed while the overlay was still open and came back at
# the old level, so the log stayed full of VERBOSE and TRACE lines.
#
# The popup's radio buttons are ImGui controls, and no automation surface can
# click one, so the immediate write they depend on had never been exercised in a
# real game. akron_log_level closes that: it calls AkronLog.ApplyLoggingLevel,
# which is the entire body of the radio button, so running the command drives
# the row itself rather than an imitation of it.
#
# akron_menu_input is the paired control. It changes a setting that is persisted
# in modsettings but is written with no immediate save, so it must NOT survive
# the kill. Without it, a level that survived would not prove the immediate
# write did anything: something else could have flushed settings.
stop_game
if launch_game; then
    enter_level
    send "akron_log_level trace" 40 >/dev/null
    BOOT_LEVEL="$(rsh "sed -n 's/^LoggingLevel: *//p' '${SETTINGS}' | head -1")"

    # The overlay is open for the same reason it was open when this was
    # reported: it is the state the popup is reached from.
    send "akron_overlay show" 40 >/dev/null
    SET_OUT="$(send 'akron_log_level diagnostic
akron_menu_input off' 60)"
    LIVE_LEVEL="$(rsh "sed -n 's/^LoggingLevel: *//p' '${SETTINGS}' | head -1")"
    LIVE_MENU="$(rsh "sed -n 's/^ConsumeGameplayInputInMenu: *//p' '${SETTINGS}' | head -1")"

    kill_game_hard
    DEAD_LEVEL="$(rsh "sed -n 's/^LoggingLevel: *//p' '${SETTINGS}' | head -1")"
    DEAD_MENU="$(rsh "sed -n 's/^ConsumeGameplayInputInMenu: *//p' '${SETTINGS}' | head -1")"

    printf '      before=%s live=%s after-kill=%s | control live=%s after-kill=%s\n' \
        "$BOOT_LEVEL" "$LIVE_LEVEL" "$DEAD_LEVEL" "$LIVE_MENU" "$DEAD_MENU"

    if ! printf '%s' "$SET_OUT" | grep -q 'log-level: Diagnostic;saved=true'; then
        fail "crash persistence: akron_log_level did not report that the write reached disk"
        printf '%s\n' "$SET_OUT" | sed 's/^/      /' | head -10
    elif [ "$BOOT_LEVEL" != "Trace" ]; then
        fail "crash persistence: could not park the level at Trace first, so a Diagnostic result proves nothing"
    elif [ "$DEAD_LEVEL" != "Diagnostic" ]; then
        fail "crash persistence: the level reverted to ${DEAD_LEVEL} after a kill -9, which is the reported defect"
    elif [ "$DEAD_MENU" != "true" ]; then
        fail "crash persistence: the control setting also survived, so this check cannot tell an immediate write from an ambient settings flush"
    else
        pass "crash persistence: Trace -> Diagnostic reached disk while running and survived a kill -9, while the unsaved control setting did not"
    fi
else
    fail "crash persistence: could not boot the game"
fi

if [ -n "$ORIGINAL_LEVEL" ]; then
    stop_game
    rsh "sed -i 's|^LoggingLevel:.*|LoggingLevel: ${ORIGINAL_LEVEL}|' '${SETTINGS}'" >/dev/null
    printf '      restored LoggingLevel to %s\n' "$ORIGINAL_LEVEL"
fi

say "Result: ${FAILURES} failed check(s)"
exit "$FAILURES"
