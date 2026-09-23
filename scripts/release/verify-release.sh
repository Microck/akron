#!/usr/bin/env bash
# The runbook's completion checks as one deterministic pass. A release is
# complete only when every public surface serves the tag, so each check either
# proves its surface or fails the run; nothing is left to memory.
#
# Usage: scripts/release/verify-release.sh vX.Y.Z
# Needs: gh (authenticated), curl, jq, unzip, sha256sum.
set -euo pipefail

REPO="Microck/akron"
GAMEBANANA_MOD_ID="681169"

tag="${1:-}"
[[ "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-beta\.[0-9]+)?$ ]] ||
    { echo "verify-release: usage: verify-release.sh vX.Y.Z" >&2; exit 1; }
version="${tag#v}"

failures=0
pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; failures=$((failures + 1)); }

# GameBanana and the website deploy moments after the workflow ends, so the
# public-surface checks retry briefly before calling a surface stale.
retry() { # tries, sleep-seconds, description, command...
    local tries="$1" pause="$2" what="$3"; shift 3
    local attempt
    for attempt in $(seq 1 "$tries"); do
        if "$@"; then return 0; fi
        [ "$attempt" -lt "$tries" ] && sleep "$pause"
    done
    echo "verify-release: still failing after ${tries} attempts: ${what}" >&2
    return 1
}

# --- 1. GitHub Release: assets, checksum, zip integrity ----------------------
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

# --- 2. GameBanana: published file metadata ---------------------------------
# A private GameBanana mod can list files through Core/Item/Data but denies
# anonymous update and download requests. Keep the upload check separate from
# the public install path; the publisher verifies the linked update with auth.
files_api="https://api.gamebanana.com/Core/Item/Data?itemtype=Mod&itemid=${GAMEBANANA_MOD_ID}&fields=Files().aFiles()&return_keys=1&format=json_min&flags=JSON_UNESCAPED_SLASHES"
gamebanana_has_release_file() {
    local files github_md5
    files="$(curl -fsS "$files_api")" || return 1
    github_md5="$(md5sum "$workdir/Akron-${tag}.zip" | cut -d' ' -f1)"
    jq -e --arg md5 "$github_md5" --arg version "$version" '
        .["Files().aFiles()"] | any(.[];
            ((._sFile | ascii_downcase | gsub("[^a-z0-9]"; "")) | contains(($version | ascii_downcase | gsub("[^a-z0-9]"; ""))))
            and ._sMd5Checksum == $md5
            and ._bIsArchived == false
            and ._sAnalysisResult == "ok"
            and ._sAvResult == "clean")
    ' <<<"$files" >/dev/null
}
if retry 6 30 "GameBanana file matching ${version}" gamebanana_has_release_file; then
    pass "GameBanana lists the release archive with matching MD5"
else
    fail "GameBanana does not list the release archive with matching MD5"
fi

# --- 3. Public install endpoints must serve the GitHub release bytes ---------
release_url="https://github.com/${REPO}/releases/download/${tag}/Akron-${tag}.zip"
raw_points_at_release() {
    local redirect
    redirect="$(curl -fsS -o /dev/null -w '%{redirect_url}' "https://akron.micr.dev/raw")" || return 1
    [ "$redirect" = "$release_url" ]
}
if retry 6 30 "akron.micr.dev/raw -> ${release_url}" raw_points_at_release; then
    pass "akron.micr.dev/raw points to the public release archive"
else
    fail "akron.micr.dev/raw does not point to the public release archive"
fi

olympus_points_at_release() {
    local redirect
    redirect="$(curl -fsS -o /dev/null -w '%{redirect_url}' "https://akron.micr.dev/olympus")" || return 1
    [ "$redirect" = "everest:${release_url}" ]
}
if retry 6 30 "akron.micr.dev/olympus -> everest:${release_url}" olympus_points_at_release; then
    pass "akron.micr.dev/olympus hands Olympus the public release archive"
else
    fail "akron.micr.dev/olympus does not hand Olympus the public release archive"
fi

if curl -fsSL -o "$workdir/install.zip" "https://akron.micr.dev/raw" &&
        cmp -s "$workdir/install.zip" "$workdir/Akron-${tag}.zip"; then
    pass "public raw download is byte-identical to the GitHub release zip"
else
    fail "public raw download differs from the GitHub release zip"
fi

# --- 4. README on main keeps the stable install endpoints --------------------
readme="$(curl -fsS "https://raw.githubusercontent.com/${REPO}/main/README.md")"
for endpoint in "akron.micr.dev/olympus" "akron.micr.dev/raw"; do
    if grep -qF "$endpoint" <<<"$readme"; then
        pass "README points at ${endpoint}"
    else
        fail "README lost ${endpoint}"
    fi
done

if [ "$failures" -gt 0 ]; then
    echo "verify-release: ${failures} completion check(s) failed for ${tag}." >&2
    echo "verify-release: repair the same tag per docs/release-runbook.md." >&2
    exit 1
fi
echo "verify-release: every completion check passed for ${tag}."
