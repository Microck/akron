#!/usr/bin/env bash
set -euo pipefail

mod_id="${AKRON_GAMEBANANA_MOD_ID:-681169}"
readme_path="${AKRON_README_PATH:-README.md}"
api_url="https://api.gamebanana.com/Core/Item/Data?itemtype=Mod&itemid=${mod_id}&fields=Files().aFiles()&return_keys=1&format=json_min&flags=JSON_UNESCAPED_SLASHES"

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required to parse the GameBanana API response." >&2
  exit 1
fi

if [ ! -f "$readme_path" ]; then
  echo "README path does not exist: $readme_path" >&2
  exit 1
fi

api_response="$(mktemp)"
trap 'rm -f "$api_response"' EXIT

curl -fsSL "$api_url" -o "$api_response"

latest_file_id="$(
  jq -r '
    .["Files().aFiles()"] // {}
    | to_entries
    | map(.value)
    | map(
        select(
          (._bIsArchived | not)
          and ((._sAnalysisResult // "ok") != "failed")
          and ((._sAvResult // "clean") != "infected")
        )
      )
    | sort_by(._tsDateAdded | tonumber)
    | last
    | ._idRow // empty
  ' "$api_response"
)"

if [ -z "$latest_file_id" ]; then
  echo "GameBanana API did not return an installable file for mod $mod_id." >&2
  exit 1
fi

FILE_ID="$latest_file_id" MOD_ID="$mod_id" perl -0pi -e '
  my $file_id = $ENV{"FILE_ID"};
  my $mod_id = $ENV{"MOD_ID"};

  s#everest:https://gamebanana\.com/mmdl/\d+,Mod,\Q$mod_id\E#everest:https://gamebanana.com/mmdl/$file_id,Mod,$mod_id#g;
  s#href="https://gamebanana\.com/(?:mods/download/\Q$mod_id\E|dl/\d+)"#href="https://gamebanana.com/dl/$file_id"#g;
' "$readme_path"

echo "README GameBanana links point to file $latest_file_id for mod $mod_id."
