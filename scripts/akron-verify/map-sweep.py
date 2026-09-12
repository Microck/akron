#!/usr/bin/env python3
"""Capture, export/import, cold-load and warm-load StartPos across loaded maps.

Uses the existing in-game QA commands on an explicitly configured test machine.
Back up its saves first. This writes StartPos and setup packs in throwaway save
slot 99; it neither deploys a build nor changes the enabled mod set.
"""

import argparse
import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import tempfile
import time


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--filter", default="", help="substring of loaded map SID")
    parser.add_argument("--exact", action="store_true", help="match --filter as a full SID instead of a substring")
    parser.add_argument("--limit", type=int, default=0, help="maximum maps; 0 means all matches")
    parser.add_argument("--sides", default="normal", help="comma-separated normal,b,c")
    parser.add_argument("--rooms", type=int, default=2, choices=range(1, 6))
    parser.add_argument("--room", action="append", help="exact room name to check; repeat for up to five rooms")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--list-only", action="store_true", help="print matching map SIDs and exit without checking")
    return parser.parse_args()


class Game:
    def __init__(self, output):
        self.output = output
        output.mkdir(parents=True, exist_ok=False)
        self.index = 0
        host = os.environ["AKRON_PERF_HOST"]
        user = os.environ["AKRON_PERF_USER"]
        self.remote_python = os.environ.get("AKRON_PERF_REMOTE_PYTHON", "python3")
        self.windows = os.environ.get("AKRON_PERF_WINDOWS") == "1"
        self.root = os.environ["AKRON_PERF_GAME_ROOT"]
        self.token = os.environ["AKRON_PERF_TOKEN"]
        self.environment = dict(os.environ)
        password = os.environ.get("AKRON_PERF_PASSWORD") or os.environ.get("SSHPASS")
        self.ssh_prefix = []
        if password:
            self.environment["SSHPASS"] = password
            self.ssh_prefix = ["sshpass", "-e"]
        # -n: never read stdin, so the sweep is safe to run inside a
        # while-read loop without eating the caller's input.
        ssh_options = ["ssh", "-n", "-o", "ConnectTimeout=15", "-o", "StrictHostKeyChecking=yes"]
        if os.environ.get("AKRON_PERF_KNOWN_HOSTS"):
            ssh_options.extend(["-o", "UserKnownHostsFile=" + os.environ["AKRON_PERF_KNOWN_HOSTS"]])
        self.connection_directory = tempfile.TemporaryDirectory(prefix="akron-map-ssh-")
        ssh_options.extend(["-o", "ControlMaster=auto", "-o", "ControlPersist=60",
                            "-o", "ControlPath=" + self.connection_directory.name + "/socket"])
        self.ssh_target = user + "@" + host
        self.remote_stage_path = "C:\\Users\\" + user + "\\akron-automation-upload.txt"
        self.ssh = self.ssh_prefix + ssh_options + [self.ssh_target]
        self.scp = self.ssh_prefix + ["scp"] + ssh_options[2:]

    def remote(self, program):
        command = self.ssh + [self.remote_python + " -c " + shlex.quote(program)]
        last_error = None
        for attempt in range(4):
            try:
                return subprocess.run(command, env=self.environment, check=True,
                                      capture_output=True, text=True, timeout=25).stdout
            except (subprocess.TimeoutExpired, subprocess.CalledProcessError) as error:
                last_error = error
                time.sleep(2 * (attempt + 1))
        raise TimeoutError("remote ssh failed after retries: " + str(last_error))

    def windows_shell(self, program):
        command = self.ssh + [program]
        return subprocess.run(command, env=self.environment, check=True, capture_output=True,
                              text=True, timeout=25).stdout

    def automation_directory(self):
        separator = "\\" if self.windows else "/"
        return self.root + separator + "Saves" + separator + "AkronAutomation"

    def write_remote_file(self, path, text):
        if not self.windows:
            directory = os.path.dirname(path)
            self.remote(
                "from pathlib import Path; p=Path(" + repr(directory) + ");"
                "(p/'" + os.path.basename(path) + "').write_text(" + repr(text) + ")"
            )
            return
        with tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False) as staged:
            staged.write(text)
            staged_name = staged.name
        try:
            # Windows OpenSSH scp rejects destination paths containing spaces.
            # Upload to the user's profile, then move it with cmd.exe.
            subprocess.run([*self.scp, staged_name,
                            self.ssh_target + ":" + self.remote_stage_path.replace("\\", "/")],
                           env=self.environment, check=True, capture_output=True,
                           text=True, timeout=30)
            self.windows_shell('move /y "' + self.remote_stage_path + '" "' + path + '"')
        finally:
            os.unlink(staged_name)

    def remove_remote_file(self, path):
        if not self.windows:
            self.remote(
                "from pathlib import Path; Path(" + repr(path) + ").unlink(missing_ok=True)"
            )
            return
        self.windows_shell('if exist "' + path + '" del /f /q "' + path + '"')

    def read_remote_file(self, path):
        if not self.windows:
            return self.remote(
                "from pathlib import Path; import sys; sys.stdout.write(Path(" +
                repr(path) + ").read_text(errors='replace'))"
            )
        result = subprocess.run(self.ssh + ['type "' + path + '"'], env=self.environment,
                                capture_output=True, text=True, errors="replace", timeout=30)
        return result.stdout

    def remote_file_exists(self, path):
        if not self.windows:
            return self.remote(
                "from pathlib import Path; print(Path(" + repr(path) + ").exists())"
            ).strip() == "True"
        return self.windows_shell(
            'if exist "' + path + '" (echo YES) else (echo NO)').strip() == "YES"

    def log_size(self):
        path = self.root + ("/" if not self.windows else "\\") + \
            "Saves" + ("\\" if self.windows else "/") + "AkronLogs" + \
            ("\\" if self.windows else "/") + "akron-current.log"
        if not self.windows:
            return int(self.remote(
                "from pathlib import Path; print(Path(" + repr(path) + ").stat().st_size)"
            ))
        return int(self.windows_shell(
            "powershell -NoProfile -Command \"(Get-Content -Raw -LiteralPath '" + path +
            "').Replace(([char]13).ToString()+([char]10).ToString(),([char]10).ToString()).Length\""
        ).strip())

    def log(self, offset):
        path = self.root + ("/" if not self.windows else "\\") + \
            "Saves" + ("\\" if self.windows else "/") + "AkronLogs" + \
            ("\\" if self.windows else "/") + "akron-current.log"
        if not self.windows:
            return self.remote(
                "from pathlib import Path; import sys; p=Path(" +
                repr(path) +
                "); f=p.open('rb'); f.seek(" + str(offset) +
                " if p.stat().st_size >= " + str(offset) +
                " else 0); sys.stdout.write(f.read().decode(errors='replace'))"
            )
        whole = self.read_remote_file(path)
        return whole[offset:] if len(whole) >= offset else whole

    def send(self, body, label):
        self.index += 1
        evidence = self.output / f"{self.index:04d}-{label}.txt"
        directory = self.automation_directory()
        command_path = directory + ("\\" if self.windows else "/") + "command.txt"
        result_path = directory + ("\\" if self.windows else "/") + "last-result.txt"
        payload = "token: " + self.token + "\n" + body + "\n"
        staging_path = directory + ("\\" if self.windows else "/") + "command.txt.part"
        self.remove_remote_file(command_path)
        self.remove_remote_file(result_path)
        self.write_remote_file(staging_path, payload)
        if self.windows:
            self.windows_shell('move /y "' + staging_path + '" "' + command_path + '"')
        else:
            self.remote(
                "from pathlib import Path; Path(" + repr(staging_path) +
                ").rename(" + repr(command_path) + ")"
            )
        deadline = time.monotonic() + 180
        response = ""
        while time.monotonic() < deadline:
            time.sleep(1)
            if self.remote_file_exists(command_path):
                continue
            if not self.remote_file_exists(result_path):
                continue
            response = self.read_remote_file(result_path).replace("\r\n", "\n")
            if response.startswith(("status: complete", "status: failed", "status: rejected")):
                evidence.write_text(response)
                if not response.startswith("status: complete"):
                    raise RuntimeError(response[:500])
                return response
        evidence.write_text(response)
        raise TimeoutError("In-game command did not finish: " + label)



def probe(output, prefix):
    fields = ("position", "speed", "facing", "animation", "animation-frame", "state", "stamina", "dashes")
    captured = {}
    for field in fields:
        value_pattern = r"(.*)$" if field == "animation" else r"(.+)$"
        match = re.search(r"^" + re.escape(prefix + "-" + field + ": ") + value_pattern, output, re.M)
        if not match:
            raise RuntimeError("Missing player probe: " + prefix + "-" + field)
        captured[field] = match.group(1)
    return captured


def require_loaded(output, slot):
    if f"qa-startpos-load-probe: loaded;slot={slot}\n" not in output:
        raise RuntimeError(f"Slot {slot} did not load; see the load response and map log")
    if f"startpos-last-loaded-slot: {slot}\n" not in output:
        raise RuntimeError(f"Slot {slot} was not recorded as loaded")


def room_inventory(game):
    rooms = []
    offset = 0
    while offset >= 0:
        inventory = game.send(f"akron_qa_list_rooms {offset} 100", "rooms")
        rooms.extend(json.loads(name) for name in
                     re.findall(r"^qa-map-room: (.+)$", inventory, re.M))
        next_page = re.search(r"^qa-map-rooms-next: (-?\d+)$", inventory, re.M)
        if not next_page:
            raise RuntimeError("Room inventory was incomplete")
        next_offset = int(next_page.group(1))
        if next_offset != -1 and next_offset <= offset:
            raise RuntimeError("Room inventory did not advance")
        offset = next_offset
    return list(dict.fromkeys(rooms))


class RoomUnavailable(RuntimeError):
    """The room has not reached a state where StartPos capture is supported."""


def finish_room_cutscene(game):
    deadline = time.monotonic() + 30
    requested_skip = False
    while time.monotonic() < deadline:
        status = game.send("akron_qa_cutscene_state status", "cutscene-state")
        match = re.search(r"qa-cutscene-state: in-cutscene=(True|False);skipping=(True|False);", status)
        if not match:
            raise RuntimeError("Could not inspect the room's cutscene state")
        active, skipping = match.groups()
        if active == "False" and skipping == "False":
            return requested_skip
        if active == "True" and skipping == "False" and not requested_skip:
            game.send("akron_skip_cutscene", "skip-cutscene")
            requested_skip = True
        time.sleep(1)
    raise RoomUnavailable("Room remained in a cutscene; capture was not tested")


def check_map(game, area, side, room_count, selected_rooms=None):
    report = {"sid": area["sid"], "area": area["id"], "side": side, "rooms": []}
    try:
        log_offset = game.log_size()
    except TimeoutError:
        log_offset = 0
    try:
        entry = game.send(f"akron_qa_enter_level {area['id']} {side} 99", "enter")
        if "qa-enter-level: missing-mode" in entry:
            return {**report, "status": "skip", "reason": "map has no requested side"}
        if "qa-enter-level: area=" not in entry:
            raise RuntimeError("Map entry failed: " + entry[:500])
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            time.sleep(2)
            status = game.send("akron_status", "entry-state")
            if "scene: Level\n" in status and "room: " + area["sid"] + " / " in status:
                break
        else:
            raise TimeoutError("Map did not finish loading: " + area["sid"])
        available = room_inventory(game)
        if not available:
            raise RuntimeError("No room inventory available")
        selected = selected_rooms or list(dict.fromkeys(
            available[index * len(available) // room_count] for index in range(room_count)))
        if any(room not in available for room in selected):
            raise RuntimeError("A requested room does not exist on this side")
        game.send("akron_startpos wait on\nakron_startpos respawn off\n" +
                  "\n".join(f"akron_startpos clear {slot}" for slot in range(1, 16)), "prepare")
        references = {}
        for slot, room in enumerate(selected, 1):
            warped = game.send('akron_freeze off\nakron_qa_warp_room "' + room + '"', "warp")
            if "qa-warp-room: room=" + room + "\n" not in warped:
                raise RuntimeError("Room warp failed: " + room)
            time.sleep(2)
            requested_cutscene_skip = finish_room_cutscene(game)
            capture = game.send(
                "akron_qa_session_state sweep-flag sweep-counter 73\n"
                f"akron_qa_startpos_reference_capture {slot} sweep-{game.index}-{slot}\n"
                "akron_qa_messages 5", "capture")
            if "qa-startpos-reference-capture: captured" not in capture:
                raise RuntimeError("Capture refused in room " + room)
            references[slot] = probe(capture, "qa-startpos-reference-capture")
            captured_room = re.search(r"^startpos-room: (.*)$", capture, re.M)
            if not captured_room:
                raise RuntimeError("Capture did not report its saved room")
            report["rooms"].append({"room": captured_room.group(1), "requested_room": room,
                                    "slot": slot, "state": references[slot],
                                    "cutscene_skip_requested": requested_cutscene_skip})

        game.send("akron_qa_pause pause", "pause-for-save")
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            persisted = game.send("akron_startpos status", "persistence")
            if "startpos-restart-copies-outstanding: 0\n" in persisted:
                break
            time.sleep(2)
        else:
            raise RuntimeError("Restart copies did not finish while paused")
        for slot in references:
            durable = game.send(f"akron_startpos slot {slot}", "persisted-slot")
            if "startpos-set: true\n" not in durable or "startpos-snapshot-on-disk: true\n" not in durable:
                raise RuntimeError(f"Slot {slot} lost its restart copy; see the map log")

        # Reimport before each slot's first load so another slot's warm-up cannot
        # conceal a failed cold restore. The second load exercises its warm copy.
        exported = game.send(f"akron_setup export startpos map-sweep-{time.time_ns()}", "export")
        export_match = re.search(r"^setup-export-started: (.+\.akr)$", exported, re.M)
        if not export_match:
            raise RuntimeError("Snapshot export did not start")
        export_path = export_match.group(1)
        deadline = time.monotonic() + 1200
        while time.monotonic() < deadline:
            export_status = game.send("akron_setup", "export-status")
            if "setup-export-in-progress: false\n" in export_status:
                break
            time.sleep(2)
        else:
            raise TimeoutError("Snapshot export was still running after 20 minutes; loads were not tested")
        for slot in references:
            game.send("akron_qa_pause pause", "pause-for-import")
            imported = game.send('akron_setup import startpos "' + export_path + '"', "import")
            if "setup-imported: true\n" not in imported:
                raise RuntimeError("Snapshot pack import failed")
            game.send("akron_qa_pause unpause", "resume")
            for path in ("cold", "warm"):
                loaded = game.send(
                    "akron_qa_session_state sweep-flag sweep-counter 0\n"
                    f"akron_qa_startpos_load_probe {slot} sweep-flag sweep-counter\nakron_startpos status",
                    path + "-load")
                require_loaded(loaded, slot)
                actual = probe(loaded, "qa-startpos-load-probe")
                if actual != references[slot]:
                    raise RuntimeError(f"Slot {slot} state mismatch: expected {references[slot]}, got {actual}")
                if "qa-session-flag: sweep-flag=true" not in loaded or "qa-session-counter: sweep-counter=73" not in loaded:
                    raise RuntimeError(f"Slot {slot} did not restore controlled session state")
        report["status"] = "pass"
    except (RuntimeError, TimeoutError) as error:
        # Timeouts (in-game hang, game crash, ssh loss) must land here too, or
        # the sweep dies before the per-side log snapshot below is taken — the
        # snapshot is the crash evidence the bulk runner needs.
        report.update(status="fail", reason=type(error).__name__ + ": " + str(error))
        if isinstance(error, RoomUnavailable):
            report["status"] = "blocked"
        if isinstance(error, TimeoutError):
            report["requires_recovery"] = True
            if str(error).startswith("remote ssh failed"):
                report["status"] = "blocked"
    try:
        new_log = game.log(log_offset)
    except TimeoutError:
        new_log = ""
    (game.output / f"map-{area['id']}-{side}.log").write_text(new_log)
    errors = [line for line in new_log.splitlines()
              if any(message in line for message in (
                  "could not be loaded", "could not be kept warm", "was removed because",
                  "was not replaced because"))]
    cold = re.findall(r"StartPos cold restore finished in ([\d.]+) ms", new_log)
    warm = re.findall(r"StartPos warm restore finished in ([\d.]+) ms", new_log)
    report.update(cold_ms=[float(value) for value in cold], warm_ms=[float(value) for value in warm])
    if report["status"] == "pass" and (errors or len(cold) < len(references) or len(warm) < len(references)):
        report.update(status="fail", reason="Restore errors or missing cold/warm evidence")
    if errors:
        report["errors"] = errors
    return report


def main():
    args = arguments()
    if args.room and len(args.room) > 5:
        raise ValueError("At most five --room arguments are supported")
    sides = args.sides.split(",")
    if any(side not in ("normal", "b", "c") for side in sides):
        raise ValueError("Sides must be normal,b,c")
    game = Game(args.output)
    inventory = game.send("akron_qa_list_maps", "inventory")
    areas = [{"sid": sid, "id": int(area)} for sid, area in
             re.findall(r"^qa-map: sid=([^;]+);id=(\d+);", inventory, re.M)
             if (sid.lower() == args.filter.lower() if args.exact and args.filter else args.filter.lower() in sid.lower())]
    if args.limit:
        areas = areas[:args.limit]
    if not areas:
        raise RuntimeError("No maps matched")
    if args.list_only:
        for area in areas:
            print(f"{area['sid']}\tid={area['id']}", flush=True)
        return 0
    reports = []
    blocked_reason = None
    for area in areas:
        for side in sides:
            print("Checking", area["sid"], side, flush=True)
            if blocked_reason:
                report = {"sid": area["sid"], "area": area["id"], "side": side,
                          "rooms": [], "status": "blocked", "reason": blocked_reason}
            else:
                try:
                    report = check_map(game, area, side, args.rooms, args.room)
                except Exception as error:  # noqa: BLE001 - preserve evidence on harness errors
                    report = {"sid": area["sid"], "area": area["id"], "side": side,
                              "rooms": [], "status": "blocked",
                              "reason": "harness: " + type(error).__name__ + ": " + str(error)[:300]}
                    blocked_reason = report["reason"]
                if report.get("requires_recovery"):
                    blocked_reason = f"Not tested after a timeout during {area['sid']} [{side}]: {report['reason']}"
            reports.append(report)
            staged = args.output / "results.json.part"
            staged.write_text(json.dumps(reports, indent=2))
            staged.replace(args.output / "results.json")
            print(report["status"].upper(), area["sid"], side, report.get("reason", ""), flush=True)
    print(json.dumps({status: sum(row["status"] == status for row in reports)
                      for status in ("pass", "fail", "skip", "blocked")}), flush=True)
    return int(any(row["status"] in ("fail", "blocked") for row in reports))


if __name__ == "__main__":
    raise SystemExit(main())
