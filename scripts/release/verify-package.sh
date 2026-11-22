#!/usr/bin/env bash
set -euo pipefail

version="$(awk -F': ' '/^[[:space:]]*Version:/ {print $2; exit}' everest.yaml)"
archive="Akron-Offline-v${version}.zip"
unzip -tqq "$archive"
listing="$(unzip -Z1 "$archive")"
grep -Fx bin/Akron.dll <<<"$listing" >/dev/null
grep -Fx everest.yaml <<<"$listing" >/dev/null
grep -Fx bin/Akron.pdb <<<"$listing" >/dev/null
if grep -E '\.pdb$' <<<"$listing" | grep -Fvx bin/Akron.pdb; then
  echo "::error::Only Akron's portable PDB belongs in the player package."
  exit 1
fi
grep -Fx LICENSE <<<"$listing" >/dev/null
grep -Fx ThirdPartyNotices.txt <<<"$listing" >/dev/null
if grep -Ei '(^|/)(sentry-cli[^/]*|sentry\.dll|libsentry[^/]*)(/|$)' <<<"$listing"; then
  echo "::error::Player package must not contain Sentry components."
  exit 1
fi
cmp LICENSE <(unzip -p "$archive" LICENSE)
cmp licenses/third-party-notices.txt <(unzip -p "$archive" ThirdPartyNotices.txt)
