#!/usr/bin/env bash
set -euo pipefail

unzip -tqq Akron.zip
unzip -Z1 Akron.zip | grep -Fx bin/Akron.dll >/dev/null
unzip -Z1 Akron.zip | grep -Fx everest.yaml >/dev/null
unzip -Z1 Akron.zip | grep -Fx bin/Akron.pdb >/dev/null
if unzip -Z1 Akron.zip | grep -E '\.pdb$' | grep -Fvx bin/Akron.pdb; then
  echo "::error::Only Akron's portable PDB belongs in the player package."
  exit 1
fi
unzip -Z1 Akron.zip | grep -Fx LICENSE >/dev/null
unzip -Z1 Akron.zip | grep -Fx ThirdPartyNotices.txt >/dev/null
if unzip -Z1 Akron.zip | grep -Ei '(^|/)(sentry-cli[^/]*|sentry\.dll|libsentry[^/]*)(/|$)'; then
  echo "::error::Player package must not contain Sentry components."
  exit 1
fi
cmp LICENSE <(unzip -p Akron.zip LICENSE)
cmp licenses/third-party-notices.txt <(unzip -p Akron.zip ThirdPartyNotices.txt)
