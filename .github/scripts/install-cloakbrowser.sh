#!/usr/bin/env bash
set -euo pipefail

readonly chromium_version="146.0.7680.177.5"
readonly archive_url="https://github.com/CloakHQ/cloakbrowser/releases/download/chromium-v${chromium_version}/cloakbrowser-linux-x64.tar.gz"
readonly expected_sha256="4a12bcde95fa1bb1beef2b41ab5e5c27c36be78e3be3d0dac8c64d705216670e"
readonly archive_path="$RUNNER_TEMP/cloakbrowser-linux-x64-${chromium_version}.tar.gz"
readonly extract_dir="$RUNNER_TEMP/cloakbrowser-linux-x64-${chromium_version}"

curl -fL --retry 3 --retry-delay 2 --proto '=https' --tlsv1.2 \
  "$archive_url" -o "$archive_path"

actual_sha256="$(sha256sum "$archive_path" | awk '{ print $1 }')"
if [ "$actual_sha256" != "$expected_sha256" ]; then
  echo "::error::CloakBrowser Chromium archive SHA-256 verification failed." >&2
  exit 1
fi

python3 - "$archive_path" "$extract_dir" <<'PY'
import os
from pathlib import Path, PurePosixPath
import shutil
import sys
import tarfile

archive_path = Path(sys.argv[1])
extract_root = Path(sys.argv[2]).resolve()
max_entries = 20_000
max_uncompressed_bytes = 2_147_483_648


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


extract_root.mkdir(parents=True, exist_ok=False)
with tarfile.open(archive_path, mode="r:gz") as archive:
    entries = archive.getmembers()
    if len(entries) > max_entries:
        raise ValueError(f"archive has more than {max_entries} entries")
    total_size = sum(entry.size for entry in entries if entry.isfile())
    if total_size > max_uncompressed_bytes:
        raise ValueError("archive expands beyond the 2 GiB safety limit")

    for entry in entries:
        target = destination(entry.name)
        if entry.isdir():
            target.mkdir(parents=True, exist_ok=True)
            target.chmod(0o755)
            continue
        if not entry.isfile():
            raise ValueError(f"archive entry is not a regular file or directory: {entry.name!r}")
        source = archive.extractfile(entry)
        if source is None:
            raise ValueError(f"could not read archive entry: {entry.name!r}")
        target.parent.mkdir(parents=True, exist_ok=True)
        with source, target.open("wb") as output:
            shutil.copyfileobj(source, output)
        target.chmod(entry.mode & 0o755 or 0o644)
PY

mapfile -t browser_binaries < <(find "$extract_dir" -type f -name chrome)
if [ "${#browser_binaries[@]}" -ne 1 ] || [ ! -x "${browser_binaries[0]}" ]; then
  echo "::error::Verified CloakBrowser archive must contain exactly one executable chrome binary." >&2
  exit 1
fi

echo "CLOAKBROWSER_BINARY_PATH=${browser_binaries[0]}" >> "$GITHUB_ENV"
echo "CLOAKBROWSER_AUTO_UPDATE=false" >> "$GITHUB_ENV"
