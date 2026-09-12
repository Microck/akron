#!/usr/bin/env bash
# Bulk StartPos map sweep: every loaded map, one at a time, resumable.
#
# Runs scripts/akron-verify/map-sweep.py per map SID so a process crash costs
# one map instead of the batch, relaunches the game when it dies, and merges
# every per-map results.json into one aggregate with a bugs file.
#
# Do NOT use this to deploy builds or change mods; it only drives the game
# that is already installed. Backs up remote saves before the first sweep.
#
# Required env (same names as scripts/akron-verify/run.sh):
#   AKRON_PERF_HOST  AKRON_PERF_USER  AKRON_PERF_PASSWORD (or ssh key)
#   AKRON_PERF_KNOWN_HOSTS  AKRON_PERF_LAUNCH_DIR
# Optional: AKRON_PERF_GAME_ROOT (default $LAUNCH_DIR/files/game-root)
#   AKRON_PERF_WINE_PREFIX (default $LAUNCH_DIR/wine-prefix)
#   AKRON_PERF_TOKEN (default akron-bulk-<date>)
# Windows mode: AKRON_PERF_WINDOWS=1, AKRON_PERF_WINDOWS_TASK,
#   AKRON_PERF_WINDOWS_BACKUP (an existing save archive)
#
# Usage:
#   scripts/akron-verify/map-sweep-bulk.sh --output /tmp/startpos-bulk \
#     [--sides normal,b,c] [--rooms 2] [--filter Strawberry] [--no-resume]
#
# Resume (default): SID+side rows already marked pass in the aggregate
# results.json are skipped. Use --no-resume to re-run everything.
# Bugs: aggregate bugs.md lists every fail/blocked row with its reason and
# evidence directory. No bug is fixed here; this only records it.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)" || exit 1
cd "$REPO_ROOT" || exit 1

if [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ]; then
  sed -n '2,25p' "$0"
  exit 0
fi
HOST="${AKRON_PERF_HOST:?set AKRON_PERF_HOST to the test box address}"
USER_NAME="${AKRON_PERF_USER:?set AKRON_PERF_USER to the account on that box}"
export SSHPASS="${AKRON_PERF_PASSWORD:-}"
KNOWN_HOSTS="${AKRON_PERF_KNOWN_HOSTS:?set AKRON_PERF_KNOWN_HOSTS to a known_hosts file containing the test host key}"
[ -f "$KNOWN_HOSTS" ] || { echo "known_hosts file does not exist: ${KNOWN_HOSTS}" >&2; exit 1; }
LAUNCH_DIR="${AKRON_PERF_LAUNCH_DIR:?set AKRON_PERF_LAUNCH_DIR to the Celeste install on that box}"
GAME_ROOT="${AKRON_PERF_GAME_ROOT:-${LAUNCH_DIR}/files/game-root}"
WINE_PREFIX="${AKRON_PERF_WINE_PREFIX:-${LAUNCH_DIR}/wine-prefix}"
TOKEN="${AKRON_PERF_TOKEN:-akron-bulk-harness-$(date -u +%Y%m%d)-a3f9c2e7}"
WINDOWS="${AKRON_PERF_WINDOWS:-0}"
WINDOWS_TASK="${AKRON_PERF_WINDOWS_TASK:-\\AkronBulkInteractive}"
WINDOWS_BACKUP="${AKRON_PERF_WINDOWS_BACKUP:-}"
SIDES="normal,b,c"
ROOMS=2
FILTER=""
OUTPUT=""
RESUME=1

while [ $# -gt 0 ]; do
  case "$1" in
    --output) OUTPUT="$2"; shift ;;
    --sides) SIDES="$2"; shift ;;
    --rooms) ROOMS="$2"; shift ;;
    --filter) FILTER="$2"; shift ;;
    --no-resume) RESUME=0 ;;
    -h|--help) sed -n '2,25p' "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
  shift
done
[ -n "$OUTPUT" ] || { echo "missing --output DIR" >&2; exit 2; }

# -n: rsh runs inside a while-read loop over the map list; without -n, ssh
# would consume the loop's stdin and end the sweep after one map.
SSH_HOST_KEY_OPTIONS=(-n -o StrictHostKeyChecking=yes -o "UserKnownHostsFile=${KNOWN_HOSTS}" -o ConnectTimeout=20)
if [ -n "${SSHPASS:-}" ]; then
  rsh() { sshpass -e ssh "${SSH_HOST_KEY_OPTIONS[@]}" "${USER_NAME}@${HOST}" "$@"; }
else
  rsh() { ssh "${SSH_HOST_KEY_OPTIONS[@]}" "${USER_NAME}@${HOST}" "$@"; }
fi
SWEEP_ENV=(env "AKRON_PERF_HOST=${HOST}" "AKRON_PERF_USER=${USER_NAME}"
  "AKRON_PERF_GAME_ROOT=${GAME_ROOT}" "AKRON_PERF_TOKEN=${TOKEN}"
  "AKRON_PERF_KNOWN_HOSTS=${KNOWN_HOSTS}")
if [ "$WINDOWS" = "1" ]; then
  SWEEP_ENV+=("AKRON_PERF_WINDOWS=1")
fi
if [ -n "${SSHPASS:-}" ]; then
  SWEEP_ENV+=("AKRON_PERF_PASSWORD=${SSHPASS}")
fi

mkdir -p "$OUTPUT" || exit 1
AGGREGATE="$OUTPUT/results.json"
[ -f "$AGGREGATE" ] || echo "[]" > "$AGGREGATE"

say() { printf '\n== %s %s\n' "$(date -u +%H:%M:%S)" "$*"; }

stop_game() {
  if [ "$WINDOWS" = "1" ]; then
    rsh "taskkill /f /im Celeste.exe >nul 2>&1" >/dev/null 2>&1 || true
    return
  fi
  rsh "export WINEPREFIX=${WINE_PREFIX}
       pkill -u ${USER_NAME} -f '[C]eleste.exe' || true
       sleep 5
       pkill -9 -u ${USER_NAME} -f '[C]eleste.exe' || true
       pkill -u ${USER_NAME} -f '[w]inedbg' || true
       sleep 2
       /usr/bin/wineserver -k 2>/dev/null || true
       sleep 6" >/dev/null 2>&1 || true
}

launch_game() {
  if [ "$WINDOWS" = "1" ]; then
    rsh "schtasks /run /tn \"${WINDOWS_TASK}\"" >/dev/null 2>&1 || return 1
    local i
    for i in $(seq 1 90); do
      if game_alive; then
        # The scheduled task only confirms process creation. Give Steam,
        # Everest, and the automation service time to finish loading.
        sleep 60
        game_alive || return 1
        return 0
      fi
      sleep 5
    done
    echo "Windows scheduled task did not start Celeste" >&2
    return 1
  fi
  local launch_cmd="cd ${LAUNCH_DIR} && setsid env \
      AKRON_AUTOMATION_ENABLED=1 AKRON_AUTOMATION_SESSION_TOKEN='${TOKEN}' \
      DISPLAY=:0 XAUTHORITY=/home/${USER_NAME}/.Xauthority \
      XDG_RUNTIME_DIR=/run/user/1000 DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus \
      PATH=/home/${USER_NAME}/.local/bin:/usr/local/bin:/usr/bin:/bin \
      nohup ./start.n-w.sh >/tmp/akron-bulk-launch.log 2>&1 </dev/null & echo launched"
  if [ -n "${SSHPASS:-}" ]; then
    timeout 45 sshpass -e ssh "${SSH_HOST_KEY_OPTIONS[@]}" "${USER_NAME}@${HOST}" \
      "$launch_cmd" >/dev/null 2>&1 || true
  else
    timeout 45 ssh "${SSH_HOST_KEY_OPTIONS[@]}" "${USER_NAME}@${HOST}" \
      "$launch_cmd" >/dev/null 2>&1 || true
  fi
  local i
  # Commands are only processed once Everest finishes loading and the module
  for i in $(seq 1 90); do
    if rsh "[ '${GAME_ROOT}/log.txt' -nt /tmp/akron-bulk.marker ] && grep -q 'DONE LOADING' '${GAME_ROOT}/log.txt' 2>/dev/null"; then
      if rsh "for pid in \$(pgrep -u ${USER_NAME} -f '[C]eleste.exe'); do tr '\\0' '\\n' < /proc/\$pid/environ 2>/dev/null | grep -q '^AKRON_AUTOMATION_ENABLED=1' && exit 0; done; exit 1"; then
        sleep 10
        return 0
      fi
      echo "automation env is not set on the Celeste process" >&2
      return 1
    fi
    sleep 5
  done
  echo "game did not finish loading" >&2
  return 1
}

# ssh exit 255: connection failed, game state unknown — assume alive so we
# never relaunch during an outage. pgrep 0: alive. anything else: dead.
game_alive() {
  if [ "$WINDOWS" = "1" ]; then
    rsh "tasklist /fi \"IMAGENAME eq Celeste.exe\" | findstr /i \"Celeste.exe\" >nul" >/dev/null 2>&1
  else
    rsh "pgrep -u ${USER_NAME} -f '[C]eleste.exe' >/dev/null" >/dev/null 2>&1
  fi
  case $? in
    0) return 0 ;;
    255) return 0 ;;
    *) return 1 ;;
  esac
}

ssh_up() {
  if [ "$WINDOWS" = "1" ]; then
    rsh "ver" >/dev/null 2>&1
  else
    rsh "true" >/dev/null 2>&1
  fi
}

wait_ssh() {
  # Overnight bulk runs: a Tailscale relay blip can last hours. Never give up.
  local i=0
  while true; do
    i=$((i + 1))
    if ssh_up; then return 0; fi
    echo "ssh still down (try $i); sleeping 30s" >&2
    sleep 30
  done
}

 # Keep the sweep's recovery archive outside Saves so Akron's startup backup
 # cannot archive it again on every game launch.
 say "Waiting for ssh"
 wait_ssh || { echo "ssh never came back; aborting" >&2; exit 1; }

 STAMP="$(date -u +%Y%m%d)"
 if [ "$WINDOWS" = "1" ]; then
   if [ -z "$WINDOWS_BACKUP" ]; then
     echo "set AKRON_PERF_WINDOWS_BACKUP to an existing Windows save backup" >&2
     exit 1
   fi
   if ! rsh "dir \"${WINDOWS_BACKUP}\" >nul 2>&1"; then
     echo "Windows save backup does not exist: ${WINDOWS_BACKUP}" >&2
     exit 1
   fi
 else
   BACKUP_PATH="${LAUNCH_DIR}/AkronSavesBackup-bulk-${STAMP}.tar.gz"
   if ! rsh "test -s '${BACKUP_PATH}'"; then
     say "Backing up remote saves outside Saves"
     rsh "tar -czf '${BACKUP_PATH}.part' -C '${GAME_ROOT}/Saves' \
       --exclude='AkronSavesBackup-*.tar.gz' --exclude='AkronBackups' --exclude='AkronTestBackups' \
       --exclude='AkronStartPos' --exclude='AkronSetups' --exclude='AkronLogs' \
       --exclude='AkronAutomation' --exclude='AkronNative' --exclude='Cache' . &&
       tar -tzf '${BACKUP_PATH}.part' >/dev/null &&
       mv '${BACKUP_PATH}.part' '${BACKUP_PATH}'" || exit 1
   fi
 fi
 say "Launching game"
 if [ "$WINDOWS" = "1" ]; then
   if ! game_alive; then
     launch_game || exit 1
   fi
 elif ! game_alive || ! rsh "grep -q 'Loaded assembly Akron' '${GAME_ROOT}/log.txt' 2>/dev/null"; then
   stop_game
   launch_game || exit 1
 fi

 # Inventory all loaded maps through a throwaway output dir.
 # map-sweep.py creates --output itself, so point it one level under mktemp's dir.
 refresh_inventory() {
  local attempt list_dir inventory_args
   for attempt in 1 2 3; do
     list_dir="$(mktemp -d "${OUTPUT}/.inventory-XXXXXX")/inventory"
    say "Listing loaded maps (attempt ${attempt})"
    inventory_args=(--output "$list_dir" --list-only)
    [ -n "$FILTER" ] && inventory_args+=(--filter "$FILTER")
    if "${SWEEP_ENV[@]}" python3 scripts/akron-verify/map-sweep.py \
        "${inventory_args[@]}" > "$OUTPUT/map-list.txt.part"; then
       mv "$OUTPUT/map-list.txt.part" "$OUTPUT/map-list.txt"
       rm -rf "$(dirname "$list_dir")"
       return 0
     fi
     rm -f "$OUTPUT/map-list.txt.part"
     rm -rf "$(dirname "$list_dir")"
     # A timeout can leave Celeste alive but unable to process commands.
     # Capture the log first, then recycle the process on every retry.
    if [ "$WINDOWS" = "1" ]; then
      rsh "powershell -NoProfile -Command \"Get-Content -LiteralPath '${GAME_ROOT}/log.txt' -Tail 200\"" \
        > "$OUTPUT/inventory-attempt-${attempt}-game-log-tail.txt" 2>/dev/null || true
    else
      rsh "tail -c 8000 '${GAME_ROOT}/log.txt' 2>/dev/null" \
        > "$OUTPUT/inventory-attempt-${attempt}-game-log-tail.txt" 2>/dev/null || true
    fi
     say "Game did not answer inventory; restarting"
     stop_game
     launch_game || continue
   done
   return 1
 }
 if ! refresh_inventory; then
   echo "map inventory failed after retries; is the game up with automation enabled?" >&2
   exit 1
 fi
 MAP_COUNT="$(grep -c . "$OUTPUT/map-list.txt" || true)"
 say "Found ${MAP_COUNT} maps"
 cat "$OUTPUT/map-list.txt"

SKIPPED=0
while IFS=$'\t' read -r SID _REST; do
  [ -n "$SID" ] || continue
  SAFE="$(printf '%s' "$SID" | tr '/ ' '__')"
  MAP_DIR="$OUTPUT/per-map/${SAFE}"
  # Resume: skip maps whose every requested side already passed in this aggregate.
  if [ "$RESUME" -eq 1 ] && [ -s "$AGGREGATE" ]; then
    if python3 - "$AGGREGATE" "$SID" "$SIDES" <<'EOF'; then
import json, sys
agg, sid, sides = sys.argv[1], sys.argv[2], sys.argv[3].split(",")
try:
    rows = json.load(open(agg))
except Exception:
    sys.exit(1)
want = {(sid, s) for s in sides}
done = {(r.get("sid"), r.get("side")) for r in rows if r.get("status") in ("pass", "skip")}
  # A side that passed is done; a side the map does not provide (skip) is
  # also done, or maps without b/c sides would re-sweep on every resume.
sys.exit(0 if want <= done else 1)
EOF
      echo "SKIP (already pass) $SID"
      SKIPPED=$((SKIPPED + 1))
      continue
    fi
  fi
  if ! game_alive; then
    say "Game died; relaunching before $SID"
    python3 - "$AGGREGATE" "$SID" <<'EOF'
import json, sys
agg, sid = sys.argv[1], sys.argv[2]
rows = json.load(open(agg))
rows.append({"sid": sid, "side": "all", "status": "blocked",
             "reason": "game process was gone before this map started; relaunched"})
json.dump(rows, open(agg, "w"), indent=2)
EOF
    stop_game
    launch_game || { echo "relaunch failed; skipping $SID" >&2; continue; }
  fi
  mkdir -p "$MAP_DIR" || exit 1
  # map-sweep.py creates its output dir itself, so give each attempt a fresh
  # numbered run directory under the map's evidence directory.
  RUN_NUM="$(find "$MAP_DIR" -maxdepth 1 -type d -name 'attempt-*' 2>/dev/null | wc -l)"
  RUN_DIR="$MAP_DIR/attempt-$RUN_NUM"
  say "Sweeping $SID ($SIDES, rooms=$ROOMS)"
  if "${SWEEP_ENV[@]}" timeout 1500 python3 scripts/akron-verify/map-sweep.py \
      --exact --filter "$SID" --sides "$SIDES" --rooms "$ROOMS" --output "$RUN_DIR"; then
    :
  else
    echo "WARN $SID sweep exited $?; game may have crashed" >&2
    # The .NET fatal (SuspendThread / 0x80131506) lands in the game's log.txt,
    # which a relaunch truncates — snapshot it before stop_game.
    if [ "$WINDOWS" = "1" ]; then
      rsh "powershell -NoProfile -Command \"Get-Content -LiteralPath '${GAME_ROOT}/log.txt' -Tail 200\"" \
        > "$RUN_DIR/game-log-tail.txt" 2>/dev/null || true
    else
      rsh "tail -c 8000 '${GAME_ROOT}/log.txt' 2>/dev/null" \
        > "$RUN_DIR/game-log-tail.txt" 2>/dev/null || true
    fi
    if ! ssh_up; then
      echo "WARN ssh is down after $SID; waiting for the network before continuing" >&2
      wait_ssh || { echo "ssh stayed down; skipping $SID" >&2; continue; }
    else
      # A failed command can leave Celeste alive but unable to consume the
      # next command. Recycle after every failed map, not only after a dead
      # process, so one hung map cannot contaminate the following maps.
      echo "WARN recycling game after failed sweep for $SID" >&2
      stop_game
      launch_game || { echo "relaunch failed; skipping $SID" >&2; continue; }
    fi
  fi
  python3 - "$AGGREGATE" "$RUN_DIR" "$SID" <<'EOF'
import json, os, sys, tempfile
from pathlib import Path
agg_path, run_dir, sid = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
agg = json.load(open(agg_path))
# A fresh run replaces the whole map: drop every prior row for this SID so
# retries never accumulate stale fail rows next to the new ones. Partial
# runs simply run again because not all sides are done.
agg = [r for r in agg if r.get("sid") != sid]
results = run_dir / "results.json"
if results.exists():
    for row in json.load(open(results)):
        row["evidence"] = str(run_dir)
        agg.append(row)
else:
    agg.append({"sid": sid, "side": "all", "status": "blocked",
                "reason": "sweep produced no results.json (crash or timeout)",
                "evidence": str(run_dir)})
fd, staged = tempfile.mkstemp(dir=str(agg_path.parent), suffix=".json")
with os.fdopen(fd, "w") as handle:
    json.dump(agg, handle, indent=2)
os.replace(staged, agg_path)
EOF
done < "$OUTPUT/map-list.txt"
# Aggregate summary + bugs file (record only; never fixed here).
python3 - "$AGGREGATE" "$OUTPUT" <<'EOF'
import json, os, tempfile
from pathlib import Path
from collections import Counter
agg_path, out = Path(__import__("sys").argv[1]), Path(__import__("sys").argv[2])
rows = json.load(open(agg_path))
counts = Counter(r.get("status", "?") for r in rows)
summary = {"maps": len({r.get("sid") for r in rows}),
           "rows": len(rows), **dict(counts)}
fd, staged = tempfile.mkstemp(dir=str(out), suffix=".json")
with os.fdopen(fd, "w") as handle:
    json.dump(summary, handle, indent=2)
os.replace(staged, out / "summary.json")
bugs = ["# Bulk sweep bugs (record only, not fixed)", ""]
for r in rows:
    if r.get("status") in ("fail", "blocked"):
        bugs.append(f"- {r.get('status','?').upper()} {r.get('sid')} [{r.get('side')}] "
                    f"{r.get('reason','')} (evidence: {r.get('evidence','?')})")
if len(bugs) == 2:
    bugs.append("- none")
fd, staged = tempfile.mkstemp(dir=str(out), suffix=".md")
with os.fdopen(fd, "w") as handle:
    handle.write("\n".join(bugs) + "\n")
os.replace(staged, out / "bugs.md")
print(json.dumps(summary))
EOF

say "Bulk sweep finished"
echo "aggregate: $AGGREGATE"
echo "bugs: $OUTPUT/bugs.md"
echo "resumed skips this run: $SKIPPED (authoritative counts: $OUTPUT/summary.json)"
