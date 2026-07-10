#!/usr/bin/env bash
set -euo pipefail

if [ -z "${AKRON_CELESTE_REFS_URL:-}" ]; then
  echo "::error::AKRON_CELESTE_REFS_URL is required." >&2
  exit 1
fi

expected_sha256="$(printf '%s' "${AKRON_CELESTE_REFS_SHA256:-}" | tr '[:upper:]' '[:lower:]')"
if ! [[ "$expected_sha256" =~ ^[0-9a-f]{64}$ ]]; then
  echo "::error::AKRON_CELESTE_REFS_SHA256 must be the exact 64-character SHA-256 for the configured archive." >&2
  exit 1
fi

archive_path="$RUNNER_TEMP/akron-celeste-refs"
extract_dir="$RUNNER_TEMP/akron-celeste-refs-extract"
rm -rf "$extract_dir"
mkdir -p "$extract_dir" lib-stripped

curl_args=(-fL --retry 3 --retry-delay 2 --proto '=https' --tlsv1.2)
if [[ "$AKRON_CELESTE_REFS_URL" == https://api.github.com/* ]]; then
  auth_token="${AKRON_CELESTE_REFS_TOKEN:-${GITHUB_TOKEN:-}}"
  if [ -n "$auth_token" ]; then
    curl_args+=(-H "Authorization: Bearer $auth_token")
  fi
  curl_args+=(-H "Accept: application/octet-stream")
elif [ -n "${AKRON_CELESTE_REFS_TOKEN:-}" ]; then
  curl_args+=(-H "Authorization: Bearer $AKRON_CELESTE_REFS_TOKEN")
fi

curl "${curl_args[@]}" "$AKRON_CELESTE_REFS_URL" | python3 -c '
import pathlib
import sys

limit = 536_870_912
total = 0
with pathlib.Path(sys.argv[1]).open("wb") as output:
    while chunk := sys.stdin.buffer.read(1024 * 1024):
        total += len(chunk)
        if total > limit:
            raise SystemExit("Celeste reference archive exceeds the 512 MiB compressed download limit.")
        output.write(chunk)
' "$archive_path"

actual_sha256="$(sha256sum "$archive_path" | awk '{ print $1 }')"
if [ "$actual_sha256" != "$expected_sha256" ]; then
  echo "::error::Celeste reference archive SHA-256 does not match AKRON_CELESTE_REFS_SHA256." >&2
  exit 1
fi

# Validate every entry before extracting. Only directories and regular files are
# accepted, and uncompressed input is bounded to limit archive bombs.
python3 - "$archive_path" "$extract_dir" <<'PY'
import os
from pathlib import Path, PurePosixPath
import shutil
import stat
import sys
import tarfile
import zipfile

archive_path = Path(sys.argv[1])
extract_root = Path(sys.argv[2]).resolve()
max_entries = 10_000
max_uncompressed_bytes = 1_073_741_824


def destination(name: str) -> Path:
    if "\\" in name:
        raise ValueError(f"archive entry uses a backslash: {name!r}")
    path = PurePosixPath(name)
    if path.is_absolute() or ".." in path.parts:
        raise ValueError(f"archive entry escapes the extraction root: {name!r}")
    target = (extract_root / Path(*path.parts)).resolve()
    if os.path.commonpath((str(extract_root), str(target))) != str(extract_root):
        raise ValueError(f"archive entry escapes the extraction root: {name!r}")
    return target


def validate_limits(entry_count: int, total_size: int) -> None:
    if entry_count > max_entries:
        raise ValueError(f"archive has more than {max_entries} entries")
    if total_size > max_uncompressed_bytes:
        raise ValueError("archive expands beyond the 1 GiB safety limit")


if zipfile.is_zipfile(archive_path):
    with zipfile.ZipFile(archive_path) as archive:
        entries = archive.infolist()
        validate_limits(len(entries), sum(entry.file_size for entry in entries))
        for entry in entries:
            target = destination(entry.filename)
            mode = entry.external_attr >> 16
            file_type = stat.S_IFMT(mode)
            if file_type and not (stat.S_ISREG(mode) or stat.S_ISDIR(mode)):
                raise ValueError(f"zip entry is not a regular file or directory: {entry.filename!r}")
            if entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(entry) as source, target.open("wb") as output:
                shutil.copyfileobj(source, output)
elif tarfile.is_tarfile(archive_path):
    with tarfile.open(archive_path, mode="r:*") as archive:
        entries = archive.getmembers()
        validate_limits(len(entries), sum(entry.size for entry in entries if entry.isfile()))
        for entry in entries:
            target = destination(entry.name)
            if entry.isdir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            if not entry.isfile():
                raise ValueError(f"tar entry is not a regular file or directory: {entry.name!r}")
            source = archive.extractfile(entry)
            if source is None:
                raise ValueError(f"could not read tar entry: {entry.name!r}")
            target.parent.mkdir(parents=True, exist_ok=True)
            with source, target.open("wb") as output:
                shutil.copyfileobj(source, output)
else:
    raise ValueError("download is not a supported zip or tar archive")
PY

mapfile -t celeste_refs < <(find "$extract_dir" -type f -name Celeste.dll)
if [ "${#celeste_refs[@]}" -ne 1 ]; then
  echo "::error::Celeste reference archive must contain exactly one Celeste.dll." >&2
  exit 1
fi

reference_dir="$(dirname "${celeste_refs[0]}")"
for dll in Celeste.dll FNA.dll MMHOOK_Celeste.dll MonoMod.Utils.dll; do
  if [ ! -f "$reference_dir/$dll" ]; then
    echo "::error::Restored reference directory is missing $dll." >&2
    exit 1
  fi
done

rm -rf lib-stripped
mkdir -p lib-stripped
cp -R "$reference_dir/." lib-stripped/
