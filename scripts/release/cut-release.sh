#!/usr/bin/env bash
# Deterministic release cut. Runs the runbook's prepare and publish phases as
# one command: validate the tree, move the changelog's Unreleased section under
# the release heading, bump everest.yaml, run the preflight and the package
# contents contract, commit, push main, and push the tag that triggers
# .github/workflows/release.yml. Completion checks then run automatically in
# .github/workflows/verify-release.yml.
#
# Main and v* tags are admin-bypass only by ruleset, so this runs locally as
# the release owner rather than inside Actions.
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

# --- validate the tree ------------------------------------------------------
[ "$(git rev-parse --abbrev-ref HEAD)" = "main" ] || fail "not on main"
[ -z "$(git status --porcelain)" ] || fail "working tree is not clean"
git fetch origin main --quiet
[ "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)" ] ||
    fail "main is not in sync with origin/main"
git rev-parse -q --verify "refs/tags/${tag}" >/dev/null &&
    fail "tag ${tag} already exists"
grep -qF "Version: ${version}" everest.yaml &&
    fail "everest.yaml already says ${version}; pick the next version"

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
if release_name in changelog:
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
listing="$(unzip -l Akron.zip)"
for entry in "${required_contents[@]}"; do
    grep -qF " ${entry}" <<<"$listing" || fail "Akron.zip is missing ${entry}"
done
grep -qE " bin/runtimes/win[^ ]*/cimgui\.dll" <<<"$listing" ||
    fail "Akron.zip is missing a Windows cimgui.dll runtime"
echo "cut-release: package contents contract holds"

# --- commit, push, tag ------------------------------------------------------
# History says "chore(release): prepare beta 71", so the readable name drops
# its Akron prefix here.
commit_name="${release_name#Akron }"
git add CHANGELOG.md everest.yaml
git commit -m "chore(release): prepare ${commit_name,,}"
git push origin main
git tag "$tag"
git push origin "$tag"

cat <<DONE
cut-release: ${tag} pushed.
The Release workflow publishes it, then Verify Release runs the completion
checks. Watch both:
  gh run watch -R Microck/akron \$(gh run list -R Microck/akron --workflow Release --limit 1 --json databaseId -q '.[0].databaseId')
  gh run list -R Microck/akron --workflow 'Verify Release' --limit 1
DONE
