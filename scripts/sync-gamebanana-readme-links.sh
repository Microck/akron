#!/usr/bin/env bash
set -euo pipefail

mod_id="${AKRON_GAMEBANANA_MOD_ID:-681169}"
readme_path="${AKRON_README_PATH-README.md}"
website_source_path="${AKRON_WEBSITE_SOURCE_PATH-}"
api_url="https://api.gamebanana.com/Core/Item/Data?itemtype=Mod&itemid=${mod_id}&fields=Files().aFiles()&return_keys=1&format=json_min&flags=JSON_UNESCAPED_SLASHES"

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required to parse the GameBanana API response." >&2
  exit 1
fi

if [ -n "$readme_path" ] && [ ! -f "$readme_path" ]; then
  echo "README path does not exist: $readme_path" >&2
  exit 1
fi

if [ -n "$website_source_path" ] && [ ! -f "$website_source_path" ]; then
  echo "Akron website source path does not exist: $website_source_path" >&2
  exit 1
fi

if [ -z "$readme_path" ] && [ -z "$website_source_path" ]; then
  echo "Set AKRON_README_PATH or AKRON_WEBSITE_SOURCE_PATH before syncing links." >&2
  exit 1
fi

latest_file_id="${AKRON_GAMEBANANA_FILE_ID-}"

if [ -z "$latest_file_id" ]; then
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
fi

if [ -z "$latest_file_id" ]; then
  echo "GameBanana API did not return an installable file for mod $mod_id." >&2
  exit 1
fi

if ! [[ "$latest_file_id" =~ ^[0-9]+$ ]]; then
  echo "GameBanana file ID must be numeric: $latest_file_id" >&2
  exit 1
fi

if [ -n "$readme_path" ]; then
  FILE_ID="$latest_file_id" MOD_ID="$mod_id" perl -0pi -e '
    my $file_id = $ENV{"FILE_ID"};
    my $mod_id = $ENV{"MOD_ID"};

    s#(?:everest:)?https://gamebanana\.com/mmdl/\d+,Mod,\Q$mod_id\E#https://akron.micr.dev/everest#g;
    s#(\[<img src="docs/images/olympus-one-click-install\.png"[^]]*>\]\()[^)]+(\))#$1https://akron.micr.dev/everest$2#g;
    s#(<a href=")[^"]*(">\s*<img src="docs/images/olympus-one-click-install\.png")#$1https://akron.micr.dev/everest$2#g;
    s#https://gamebanana\.com/(?:mods/download/\Q$mod_id\E|dl/\d+)#https://gamebanana.com/dl/$file_id#g;
  ' "$readme_path"

  echo "README GameBanana links point to file $latest_file_id for mod $mod_id."
fi

if [ -n "$website_source_path" ]; then
  FILE_ID="$latest_file_id" MOD_ID="$mod_id" perl -0pi -e '
    my $file_id = $ENV{"FILE_ID"};
    my $mod_id = $ENV{"MOD_ID"};

    s#const gamebananaModId = "\d+";#const gamebananaModId = "$mod_id";#g;
    s#const gamebananaFallbackFileId = "\d+";#const gamebananaFallbackFileId = "$file_id";#g;
  ' "$website_source_path"

  echo "Akron website fallback links point to file $latest_file_id for mod $mod_id."
fi
