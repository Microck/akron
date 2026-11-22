#!/usr/bin/env bash
# Verify the downloadable GitHub release assets and ZIP checksum.
# Usage: scripts/release/verify-release.sh vX.Y.Z
set -euo pipefail

REPO="${GITHUB_REPOSITORY:-Microck/akron}"

tag="${1:-}"
[[ "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-beta(\.[0-9]+)?)?$ ]] ||
    { echo "verify-release: usage: verify-release.sh vX.Y.Z" >&2; exit 1; }

failures=0
pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; failures=$((failures + 1)); }

workdir="$(mktemp -d)"
trap 'rm -rf "$workdir"' EXIT

if gh release view "$tag" -R "$REPO" --json assets \
        -q '[.assets[].name] | join("\n")' > "$workdir/assets.txt"; then
    for suffix in ".zip" ".zip.sha256" ".dependencies.json" ".cdx.json"; do
        if grep -qxF "Akron-${tag}${suffix}" "$workdir/assets.txt"; then
            pass "GitHub Release carries Akron-${tag}${suffix}"
        else
            fail "GitHub Release is missing Akron-${tag}${suffix}"
        fi
    done
    gh release download "$tag" -R "$REPO" --pattern "Akron-${tag}.zip*" --dir "$workdir"
    if (cd "$workdir" && sha256sum -c "Akron-${tag}.zip.sha256" >/dev/null); then
        pass "release zip matches its published checksum"
    else
        fail "release zip does not match its published checksum"
    fi
    if unzip -tqq "$workdir/Akron-${tag}.zip" >/dev/null; then
        pass "release zip reads back cleanly"
    else
        fail "release zip is corrupt"
    fi
else
    fail "GitHub Release ${tag} does not exist"
fi

exit "$(( failures != 0 ))"
