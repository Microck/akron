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

# --- 2. GameBanana: update naming this version --------------------------------
# The mod is private on GameBanana, so the anonymous API hides its file list;
# the update feed and the raw download links below are the public record.
updates_has_version() {
    local updates
    updates="$(curl -fsS "https://gamebanana.com/apiv11/Mod/${GAMEBANANA_MOD_ID}/Updates?_nPage=1")" || return 1
    grep -qF "$version" <<<"$updates"
}
if retry 6 30 "GameBanana update naming ${version}" updates_has_version; then
    pass "GameBanana updates name ${version}"
else
    fail "GameBanana updates do not name ${version}"
fi

# --- 3. akron.micr.dev install endpoints, bound to the tag by checksum -------
# /raw names the current GameBanana file id; /olympus must hand Olympus the
# same id, and the bytes GameBanana serves for it must be the exact release
# zip. That binds both endpoints to the tag harder than reading the mod page.
raw_redirect="$(curl -fsS -o /dev/null -w '%{redirect_url}' "https://akron.micr.dev/raw" || true)"
file_id="$(grep -oE '[0-9]+$' <<<"$raw_redirect" || true)"
if [ -n "$file_id" ]; then
    pass "akron.micr.dev/raw names GameBanana file ${file_id}"
else
    fail "akron.micr.dev/raw does not name a GameBanana file (redirect: '${raw_redirect}')"
fi

olympus_points_at_file() {
    [ -n "$file_id" ] || return 1
    local redirect
    redirect="$(curl -fsS -o /dev/null -w '%{redirect_url}' "https://akron.micr.dev/olympus")" || return 1
    grep -qF "gamebanana.com/mmdl/${file_id}" <<<"$redirect"
}
if retry 6 30 "akron.micr.dev/olympus -> mmdl/${file_id}" olympus_points_at_file; then
    pass "akron.micr.dev/olympus hands Olympus mmdl/${file_id}"
else
    fail "akron.micr.dev/olympus does not point at mmdl/${file_id}"
fi

gamebanana_serves_release_bytes() {
    [ -n "$file_id" ] || return 1
    curl -fsSL -o "$workdir/gamebanana.zip" "https://gamebanana.com/dl/${file_id}" || return 1
    local github_sum gamebanana_sum
    github_sum="$(awk '{print $1}' "$workdir/Akron-${tag}.zip.sha256" 2>/dev/null)" || return 1
    gamebanana_sum="$(sha256sum "$workdir/gamebanana.zip" | awk '{print $1}')"
    [ -n "$github_sum" ] && [ "$github_sum" = "$gamebanana_sum" ]
}
if retry 6 30 "GameBanana file ${file_id} matching the release checksum" gamebanana_serves_release_bytes; then
    pass "GameBanana file ${file_id} is byte-identical to the GitHub release zip"
else
    fail "GameBanana file ${file_id} does not match the GitHub release checksum"
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
