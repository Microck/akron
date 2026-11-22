#!/usr/bin/env bash
# Build both editions in disposable worktrees and publish their branches and
# source tags together. Run as the release owner with main ruleset bypass.
#
# Usage: scripts/release/cut-release.sh X.Y.Z[-beta.N]
set -euo pipefail

fail() { echo "cut-release: $*" >&2; exit 1; }

version="${1:-}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-beta\.[0-9]+)?$ ]] ||
    fail "usage: cut-release.sh X.Y.Z or X.Y.Z-beta.N (got '${version}')"
tag="v${version}"

# The changelog heading and commit message use the readable name the runbook
# describes: "Akron Beta N" for beta versions, "Akron X.Y.Z" otherwise.
if [[ "$version" =~ -beta\.([0-9]+)$ ]]; then
    release_name="Akron Beta ${BASH_REMATCH[1]}"
else
    release_name="Akron ${version}"
fi

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

[ "$(git symbolic-ref --short HEAD)" = main ] || fail "run this from main"
[ -z "$(git status --porcelain)" ] || fail "working tree is not clean"
git fetch origin refs/heads/main:refs/remotes/origin/main refs/heads/offline:refs/remotes/origin/offline
[ "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)" ] || fail "main is not in sync with origin"
existing_tags="$(git ls-remote --tags origin "refs/tags/$tag" "refs/tags/offline/$tag")"
[ -z "$existing_tags" ] || fail "a release tag already exists"

# Stage both commits away from the maintainer's checkout. Publish all four refs
# in one transaction only after both preflights pass.
stage="$(mktemp -d)"
cleanup() {
    for edition in main offline; do
        if [ -e "$stage/$edition/.git" ]; then git worktree remove --force "$stage/$edition"; fi
    done
    rmdir "$stage"
}
trap cleanup EXIT

prepare() (
    edition="$1"
    archive="$2"
    git worktree add --quiet --detach "$stage/$edition" "origin/$edition"
    cd "$stage/$edition"
    if [ -d "$repo_root/lib-stripped" ]; then ln -s "$repo_root/lib-stripped" lib-stripped; fi
    # --- move Unreleased under the release heading ------------------------------
    # The section must exist and carry at least one note: an empty release would
    # publish a changelog section that tells players nothing.
    python3 - "$version" "$release_name" <<'PY'
import re, sys

version, release_name = sys.argv[1], sys.argv[2]
with open("CHANGELOG.md", encoding="utf-8") as handle:
    changelog = handle.read()

match = re.search(r"## Unreleased\n(.*?)(?=\n## |\Z)", changelog, re.S)
if not match:
    sys.exit("cut-release: CHANGELOG.md has no Unreleased section")
notes = match.group(1).strip("\n")
if not any(line.strip().startswith("-") for line in notes.splitlines()):
    sys.exit("cut-release: the Unreleased section carries no notes; "
             "write the release notes before cutting")
if f"## {release_name}" in changelog.splitlines():
    sys.exit(f"cut-release: CHANGELOG.md already has a {release_name} section")

replacement = f"## Unreleased\n\n## {release_name}\n\n{notes}\n"
changelog = changelog.replace(match.group(0), replacement, 1)
with open("CHANGELOG.md", "w", encoding="utf-8") as handle:
    handle.write(changelog)
print(f"cut-release: moved Unreleased notes under '{release_name}'")
PY

    # --- bump the mod version ---------------------------------------------------
    # Only the first Version key is Akron's own; the dependency pins stay.
    python3 - "$version" <<'PY'
import sys

version = sys.argv[1]
with open("everest.yaml", encoding="utf-8") as handle:
    lines = handle.readlines()
for index, line in enumerate(lines):
    if line.strip().startswith("Version:"):
        lines[index] = line.split("Version:")[0] + f"Version: {version}\n"
        break
else:
    sys.exit("cut-release: everest.yaml has no Version key")
with open("everest.yaml", "w", encoding="utf-8") as handle:
    handle.writelines(lines)
print(f"cut-release: everest.yaml -> {version}")
PY

    # --- preflight and package contents contract --------------------------------
    make preflight-release

    required_contents=(
        "everest.yaml"
        "bin/Akron.dll"
        "bin/ImGui.NET.dll"
        "bin/runtimes/linux-x64/native/libcimgui.so"
        "bin/runtimes/osx/native/libcimgui.dylib"
        "Dialog/English.txt"
        "LICENSE"
        "ThirdPartyNotices.txt"
    )
    listing="$(unzip -l "$archive")"
    for entry in "${required_contents[@]}"; do
        grep -qF " ${entry}" <<<"$listing" || fail "$archive is missing ${entry}"
    done
    grep -qE " bin/runtimes/win[^ ]*/cimgui\.dll" <<<"$listing" ||
        fail "$archive is missing a Windows cimgui.dll runtime"
    echo "cut-release: package contents contract holds"

    git add CHANGELOG.md everest.yaml
    git commit -m "chore(release): prepare ${release_name#Akron } ($edition)"
)

prepare main Akron.zip
prepare offline "Akron-Offline-${tag}.zip"
main_commit="$(git -C "$stage/main" rev-parse HEAD)"
offline_commit="$(git -C "$stage/offline" rev-parse HEAD)"
git push --atomic origin "$main_commit:refs/heads/main" "$offline_commit:refs/heads/offline" \
    "$main_commit:refs/tags/$tag" "$offline_commit:refs/tags/offline/$tag"
git merge --ff-only "$main_commit"
echo "Published paired source tags $tag and offline/$tag. The Release workflow will publish both builds."
