#!/usr/bin/env bash
#
# Akron StartPos corpus collector.
#
# Runs INSIDE the akron-corpus sandbox pod (scripts/akron-corpus/k8s.yaml) after
# Celeste/Everest has been launched there with Akron automation enabled. For each
# target it enters the map, optionally warps to a room, captures one StartPos
# slot, waits for the background restart-copy worker to finish writing the
# snapshot, and copies it plus the matching Akron log into a timestamped
# per-target run directory. At the end it triggers the composition report and
# writes a SHA-256 manifest.
#
# The collector never deletes or overwrites anything under the game's Saves tree:
# it only reads snapshots out of Saves/AkronStartPos and copies them elsewhere.
#
# Snapshot file names are deterministic per slot (v10-<sha256(slotName)>.json.gz),
# so a rerun of a map overwrites the same file. The collector therefore records
# every snapshot's SHA-256 before capture and copies any file that is new or whose
# content changed. If the content is unchanged, it copies the exact deterministic
# file for the slot reported by status rather than selecting an unrelated snapshot.
#
# Usage:
#   collect.sh [--help] [target ...]
#
# Targets:
#   SID                  Enter the map's normal entry room and capture slot 1.
#   'SID|ROOM|SLOT'      Enter SID, warp once to ROOM, and capture SLOT (1-50).
#                        Quote this form so the shell does not interpret "|".
#
# Environment:
#   AKRON_CORPUS_GAME_ROOT   Required. The game root whose Saves/AkronAutomation
#                            and Saves/AkronStartPos are live. This is the value
#                            passed to the collector, not discovered.
#   AKRON_CORPUS_OUT         Output root. Default /data/corpus.
#   AKRON_CORPUS_TOKEN       The automation session token the game was launched
#                            with (AKRON_AUTOMATION_SESSION_TOKEN). Required.
#   AKRON_CORPUS_TIMEOUT     Seconds to wait for each snapshot. Default 300.
#
# Prerequisites (the pod must already be set up):
#   - The akron-corpus pod exists in sandboxes and its PVC is bound.
#   - Celeste + Everest + Akron (with automation) are installed and the game has
#     been launched with AKRON_AUTOMATION_ENABLED=1 and
#     AKRON_AUTOMATION_SESSION_TOKEN=<AKRON_CORPUS_TOKEN>.
#   - The mods/maps you want are installed in the game's Mods directory.
#
# Typical invocation via kubectl exec:
#   kubectl --context home-k8s -n sandboxes exec -it akron-corpus -- \
#     /data/scripts/akron-corpus/collect.sh \
#     some/collab/SID 'another/mod/SID|room-name|3'

set -euo pipefail

AUTO_REL="Saves/AkronAutomation"
SNAP_REL="Saves/AkronStartPos"
LOG_REL="Saves/AkronLogs/akron-current.log"

usage() {
    sed -n '2,/^set -euo pipefail$/p' "$0" | sed '$d'
}

if [ "${1:-}" = "--help" ] || [ "${1:-}" = "-h" ]; then
    usage
    exit 0
fi

# Parse every explicit target before looking at the game root or sending any
# automation command. Parsed values are kept in parallel arrays so delimiters
# in the original form cannot be reinterpreted later.
TARGET_SIDS=()
TARGET_ROOMS=()
TARGET_SLOTS=()
TARGET_STRUCTURED=()

parse_target() {
    local target="$1"
    local sid room slot rest normalized_slot

    if [[ "$target" == *'|'* ]]; then
        sid="${target%%|*}"
        rest="${target#*|}"
        if [[ "$rest" != *'|'* ]]; then
            echo "error: malformed target '${target}': expected SID|ROOM|SLOT" >&2
            return 1
        fi
        room="${rest%%|*}"
        slot="${rest#*|}"
        if [ -z "$sid" ] || [ -z "$room" ] || [ -z "$slot" ] || [[ "$slot" == *'|'* ]]; then
            echo "error: malformed target '${target}': expected non-empty SID and ROOM and one SLOT" >&2
            return 1
        fi
        if [[ ! "$slot" =~ ^[0-9]+$ ]]; then
            echo "error: malformed target '${target}': SLOT must be numeric and in the range 1-50" >&2
            return 1
        fi

        normalized_slot="$slot"
        while [ "${#normalized_slot}" -gt 1 ] && [[ "$normalized_slot" == 0* ]]; do
            normalized_slot="${normalized_slot#0}"
        done
        if [ "${#normalized_slot}" -gt 2 ] ||
           [ "$normalized_slot" -lt 1 ] || [ "$normalized_slot" -gt 50 ]; then
            echo "error: malformed target '${target}': SLOT must be numeric and in the range 1-50" >&2
            return 1
        fi

        TARGET_SIDS+=("$sid")
        TARGET_ROOMS+=("$room")
        TARGET_SLOTS+=("$normalized_slot")
        TARGET_STRUCTURED+=(1)
        return 0
    fi

    if [ -z "$target" ]; then
        echo "error: malformed target: SID must not be empty" >&2
        return 1
    fi
    TARGET_SIDS+=("$target")
    TARGET_ROOMS+=("")
    TARGET_SLOTS+=(1)
    TARGET_STRUCTURED+=(0)
}

for target in "$@"; do
    parse_target "$target" || exit 1
done

: "${AKRON_CORPUS_GAME_ROOT:?set AKRON_CORPUS_GAME_ROOT to the game root whose Saves/AkronAutomation the launched game reads}"
: "${AKRON_CORPUS_TOKEN:?set AKRON_CORPUS_TOKEN to the automation session token the game was launched with}"
GAME_ROOT="${AKRON_CORPUS_GAME_ROOT%/}"
OUT_ROOT="${AKRON_CORPUS_OUT:-/data/corpus}"
TIMEOUT_S="${AKRON_CORPUS_TIMEOUT:-300}"

AUTO_DIR="${GAME_ROOT}/${AUTO_REL}"
SNAP_DIR="${GAME_ROOT}/${SNAP_REL}"
LOG_FILE="${GAME_ROOT}/${LOG_REL}"
TOKEN="${AKRON_CORPUS_TOKEN}"

if [ "${#TOKEN}" -lt 32 ]; then
    echo "error: AKRON_CORPUS_TOKEN must be at least 32 characters" >&2
    exit 1
fi

if [ ! -d "$AUTO_DIR" ]; then
    echo "error: automation directory does not exist: ${AUTO_DIR}" >&2
    exit 1
fi

mkdir -p "$OUT_ROOT"

# send <body> <timeout-seconds> - writes one automation run and waits for its
# result, using the same command-file protocol scripts/akron-perf/run.sh uses
# against a remote box, but locally against the running game.
send() {
    local body="$1"
    local timeout_s="${2:-$TIMEOUT_S}"
    rm -f "${AUTO_DIR}/last-result.txt"
    printf '%s\n' "token: ${TOKEN}" > "${AUTO_DIR}/command.txt.part"
    printf '%s' "$body" >> "${AUTO_DIR}/command.txt.part"
    printf '\n' >> "${AUTO_DIR}/command.txt.part"
    mv "${AUTO_DIR}/command.txt.part" "${AUTO_DIR}/command.txt"

    local waited=0
    while [ "$waited" -lt "$timeout_s" ]; do
        if [ ! -f "${AUTO_DIR}/command.txt" ] &&
           head -1 "${AUTO_DIR}/last-result.txt" 2>/dev/null | grep -qE 'complete|rejected|failed'; then
            cat "${AUTO_DIR}/last-result.txt"
            return 0
        fi
        sleep 2
        waited=$((waited + 2))
    done
    echo "error: automation run timed out after ${timeout_s}s" >&2
    return 1
}

# snapshot_manifest - "<sha256>  <path>" lines for every current .json.gz under
# Saves/AkronStartPos, sorted by path. Includes both path and digest so a rerun
# that overwrites a deterministic path is still detected as a change.
snapshot_manifest() {
    if [ -d "$SNAP_DIR" ]; then
        find "$SNAP_DIR" -maxdepth 1 -type f -name '*.json.gz' -print0 2>/dev/null |
            sort -z |
            while IFS= read -r -d '' f; do
                printf '%s  %s\n' "$(sha256sum "$f" | awk '{print $1}')" "$f"
            done
    fi
}

# encode_name <sid> - map SIDs can contain "/", spaces, ":" and other
# characters that are hostile in a path. Fold to a safe filename.
encode_name() {
    local value="$1"
    # Preserve alnum, dot, dash, underscore; everything else becomes a dash.
    printf '%s' "$value" | tr -c 'A-Za-z0-9._-' '-'
}

collect_one() {
    local sid="$1"
    local room="$2"
    local slot="$3"
    local structured="$4"
    local safe_sid safe_room
    safe_sid="$(encode_name "$sid")"
    safe_room="$(encode_name "${room:-entry}")"
    local stamp
    stamp="$(date -u +%Y%m%dT%H%M%S.%NZ)"
    local run_dir="${OUT_ROOT}/${stamp}_${safe_sid}__${safe_room}__slot-${slot}"
    mkdir -p "$run_dir"

    # Snapshot paths and digests before this capture, so a new or changed file
    # is copied even when its deterministic path already existed.
    local before
    before="$(snapshot_manifest)"

    if [ "$structured" -eq 1 ]; then
        echo "== ${sid} / ${room} / slot ${slot} -> ${run_dir}"
    else
        echo "== ${sid} / entry / slot 1 -> ${run_dir}"
    fi

    # akron_qa_enter_map swaps Engine.Scene to a LevelLoader asynchronously.
    # Send it alone. akron_startpos status's send() output is always non-empty
    # (token/status/scene header lines), but when no Level is active it emits
    # "Akron command requires an active Level scene." and no success lines.
    # On slower maps, startpos-index can appear before a Player is tracked. An
    # empty akron_qa_probe reports "player: missing" until then, and only emits
    # its usage line once a Player exists, so readiness requires both signals.
    local enter_out
    enter_out="$(send "akron_qa_enter_map ${sid} normal" 120)"
    printf '%s\n' "$enter_out" > "${run_dir}/commands-enter.txt"
    if printf '%s' "$enter_out" | grep -qE 'qa-enter-map: not-found|qa-enter-map: missing-mode'; then
        echo "error: could not enter map ${sid}: see commands-enter.txt" >&2
        return 1
    fi
    local ready=0
    local waited=0
    local ready_out=""
    while [ "$waited" -lt "$TIMEOUT_S" ]; do
        ready_out="$(send 'akron_startpos status
akron_qa_probe' "$TIMEOUT_S")" || true
        if printf '%s' "$ready_out" | grep -q 'startpos-index:' &&
           printf '%s' "$ready_out" | grep -q 'usage: akron_qa_probe'; then
            ready=1
            break
        fi
        sleep 2
        waited=$((waited + 2))
    done
    if [ "$structured" -eq 1 ]; then
        printf '%s\n' "$ready_out" > "${run_dir}/status-enter-ready.txt"
    else
        printf '%s\n' "$ready_out" > "${run_dir}/status-ready.txt"
    fi
    if [ "$ready" -ne 1 ]; then
        echo "error: level never became active after entering ${sid}" >&2
        return 1
    fi

    if [ "$structured" -eq 1 ]; then
        local warp_out
        if ! warp_out="$(send "akron_qa_warp_room ${room}" 120)"; then
            printf '%s\n' "$warp_out" > "${run_dir}/commands-warp.txt"
            echo "error: warp automation failed for ${sid} / ${room}: see ${run_dir}/commands-warp.txt" >&2
            return 1
        fi
        printf '%s\n' "$warp_out" > "${run_dir}/commands-warp.txt"
        if printf '%s' "$warp_out" | grep -qE 'qa-warp-room: (not-found|missing room)'; then
            echo "error: room not found for ${sid} / ${room}: see ${run_dir}/commands-warp.txt" >&2
            return 1
        fi

        ready=0
        waited=0
        ready_out=""
        while [ "$waited" -lt "$TIMEOUT_S" ]; do
            ready_out="$(send 'akron_status
akron_qa_probe' "$TIMEOUT_S")" || true
            if printf '%s\n' "$ready_out" | grep -qxF "room: ${sid} / ${room}" &&
               printf '%s' "$ready_out" | grep -q 'usage: akron_qa_probe'; then
                ready=1
                break
            fi
            sleep 2
            waited=$((waited + 2))
        done
        printf '%s\n' "$ready_out" > "${run_dir}/status-ready.txt"
        if [ "$ready" -ne 1 ]; then
            echo "error: level never became ready in exact room ${sid} / ${room} after warp: see ${run_dir}/status-ready.txt" >&2
            return 1
        fi
    fi

    # A cutscene transition can briefly remove the Player after readiness has
    # passed. Retry the capture while the game remains unpaused, then pause
    # only after a Player accepted the start position.
    local set_out=""
    local set_waited=0
    local set_ready=0
    while [ "$set_waited" -lt "$TIMEOUT_S" ]; do
        set_out="$(send "akron_startpos set ${slot}" 120)" || true
        printf '%s\n' "$set_out" > "${run_dir}/commands-set.txt"
        if printf '%s\n' "$set_out" | grep -qxF 'startpos-set: true' &&
           printf '%s\n' "$set_out" | grep -qxF "startpos-slot: ${slot}"; then
            set_ready=1
            break
        fi
        sleep 2
        set_waited=$((set_waited + 2))
    done
    if [ "$set_ready" -ne 1 ]; then
        echo "error: start position slot ${slot} was not set for ${sid}: see ${run_dir}/commands-set.txt" >&2
        return 1
    fi
    send 'akron_qa_pause pause' 120 > "${run_dir}/commands-pause.txt"

    # Poll the status until the restart copy is no longer outstanding and the
    # snapshot is on disk.
    local waited=0
    local status_out=""
    local snapshot_ready=0
    while [ "$waited" -lt "$TIMEOUT_S" ]; do
        status_out="$(send 'akron_startpos status' "$TIMEOUT_S")" || true
        if printf '%s' "$status_out" | grep -q 'startpos-restart-copy-outstanding: false' &&
           printf '%s' "$status_out" | grep -q 'startpos-snapshot-on-disk: true' &&
           printf '%s\n' "$status_out" | grep -qxF "startpos-slot: ${slot}"; then
            snapshot_ready=1
            break
        fi
        sleep 3
        waited=$((waited + 3))
    done
    printf '%s\n' "$status_out" > "${run_dir}/status-final.txt"
    if [ "$snapshot_ready" -ne 1 ]; then
        echo "error: snapshot for ${sid} slot ${slot} was not ready before timeout: see ${run_dir}/status-final.txt" >&2
        return 1
    fi

    # Copy every snapshot that is new or whose content changed since the run
    # began. Preserve source files: cp -n never overwrites a destination.
    local after
    after="$(snapshot_manifest)"
    local copied=0
    while IFS= read -r -d '' after_line; do
        local hash path
        hash="${after_line%%  *}"
        path="${after_line#*  }"
        # Skip if the same path with the same digest already existed.
        if printf '%s\n' "$before" | grep -qF "${hash}  ${path}"; then
            continue
        fi
        cp -n "$path" "${run_dir}/$(basename "$path")"
        copied=$((copied + 1))
    done < <(printf '%s\n' "$after" | while IFS= read -r l; do printf '%s\0' "$l"; done)

    if [ "$copied" -eq 0 ]; then
        local slot_name=""
        local line
        while IFS= read -r line; do
            case "$line" in
                'startpos-state-slot: '*) slot_name="${line#startpos-state-slot: }" ;;
            esac
        done <<< "$status_out"
        if [ -z "$slot_name" ]; then
            echo "error: status did not report startpos-state-slot for ${sid} slot ${slot}: see ${run_dir}/status-final.txt" >&2
            return 1
        fi

        local slot_hash
        slot_hash="$(printf '%s' "$slot_name" | sha256sum | awk '{print $1}')"
        local slot_snapshot="${SNAP_DIR}/v10-${slot_hash}.json.gz"
        if [ ! -f "$slot_snapshot" ]; then
            echo "error: snapshot reported for ${sid} slot ${slot} does not exist: ${slot_snapshot}" >&2
            return 1
        fi
        cp -n "$slot_snapshot" "${run_dir}/$(basename "$slot_snapshot")"
        copied=1
    fi

    # Preserve the matching log tail.
    cp -f "$LOG_FILE" "${run_dir}/akron-current.log" 2>/dev/null || true
    echo "collected: ${copied} snapshot file(s)"
}

# --- resolve target list ------------------------------------------------------

if [ "${#TARGET_SIDS[@]}" -eq 0 ]; then
    echo "No map SIDs supplied; querying akron_qa_list_maps..."
    LIST_OUT="$(send 'akron_qa_list_maps' 120)"
    while IFS= read -r line; do
        case "$line" in
            'qa-map: '*) ;;
            *) continue ;;
        esac
        sid="${line#*sid=}"
        sid="${sid%%;*}"
        [ -n "$sid" ] && parse_target "$sid"
    done <<< "$LIST_OUT"
    printf '%s\n' "$LIST_OUT" > "${OUT_ROOT}/qa-list-maps.txt"
fi

if [ "${#TARGET_SIDS[@]}" -eq 0 ]; then
    echo "error: no maps to collect" >&2
    exit 1
fi

# --- collect ------------------------------------------------------------------

for i in "${!TARGET_SIDS[@]}"; do
    collect_one "${TARGET_SIDS[$i]}" "${TARGET_ROOMS[$i]}" \
        "${TARGET_SLOTS[$i]}" "${TARGET_STRUCTURED[$i]}"
done

# --- composition report + manifest -------------------------------------------

echo "== triggering snapshot composition report"
send 'akron_qa_snapshot_report' 60 > "${OUT_ROOT}/snapshot-report-trigger.txt" || true
sleep 15
cp -f "$LOG_FILE" "${OUT_ROOT}/akron-current-final.log" 2>/dev/null || true

MANIFEST="${OUT_ROOT}/manifest.sha256"
: > "$MANIFEST"
find "$OUT_ROOT" -maxdepth 2 -type f -name '*.json.gz' -print | sort |
    while IFS= read -r f; do
        rel="${f#"${OUT_ROOT}/"}"
        sha256sum "$f" | awk -v r="$rel" '{print $1 "  " r}'
    done >> "$MANIFEST"

echo "== done. Collected under ${OUT_ROOT}; manifest: ${MANIFEST}"
