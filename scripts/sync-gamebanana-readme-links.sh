#!/usr/bin/env bash
set -euo pipefail

mod_id="${AKRON_GAMEBANANA_MOD_ID:-681169}"
readme_path="${AKRON_README_PATH-README.md}"
website_source_path="${AKRON_WEBSITE_SOURCE_PATH-}"
website_api_path="${AKRON_WEBSITE_API_PATH-}"
website_vercel_path="${AKRON_WEBSITE_VERCEL_PATH-}"
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

if [ -n "$website_api_path" ] && [ ! -f "$website_api_path" ]; then
  echo "Akron website API path does not exist: $website_api_path" >&2
  exit 1
fi

if [ -n "$website_vercel_path" ] && [ ! -f "$website_vercel_path" ]; then
  echo "Akron website Vercel path does not exist: $website_vercel_path" >&2
  exit 1
fi

if [ -z "$readme_path" ] && [ -z "$website_source_path" ] && [ -z "$website_api_path" ] && [ -z "$website_vercel_path" ]; then
  echo "Set AKRON_README_PATH, AKRON_WEBSITE_SOURCE_PATH, AKRON_WEBSITE_API_PATH, or AKRON_WEBSITE_VERCEL_PATH before syncing links." >&2
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

    s#(?:everest:)?https://gamebanana\.com/mmdl/\d+,Mod,\Q$mod_id\E#https://akron.micr.dev/olympus#g;
    s#https://akron\.micr\.dev/everest#https://akron.micr.dev/olympus#g;
    s#(\[<img src="docs/images/olympus-one-click-install\.png"[^]]*>\]\()[^)]+(\))#$1https://akron.micr.dev/olympus$2#g;
    s#(<a href=")[^"]*(">\s*<img src="docs/images/olympus-one-click-install\.png")#$1https://akron.micr.dev/olympus$2#g;
    s#https://gamebanana\.com/(?:mods/download/\Q$mod_id\E|dl/\d+)#https://akron.micr.dev/raw#g;
    s#(\[<img src="docs/images/raw-download\.png"[^]]*>\]\()[^)]+(\))#$1https://akron.micr.dev/raw$2#g;
    s#(<a href=")[^"]*(">\s*<img src="docs/images/raw-download\.png")#$1https://akron.micr.dev/raw$2#g;
  ' "$readme_path"

  echo "README install links point to Akron stable install endpoints for mod $mod_id."
fi

for website_fallback_path in "$website_source_path" "$website_api_path"; do
  if [ -z "$website_fallback_path" ]; then
    continue
  fi

  FILE_ID="$latest_file_id" MOD_ID="$mod_id" perl -0pi -e '
    my $file_id = $ENV{"FILE_ID"};
    my $mod_id = $ENV{"MOD_ID"};

    s#const gamebananaModId = "\d+";#const gamebananaModId = "$mod_id";#g;
    s#const gamebananaFallbackFileId = "\d+";#const gamebananaFallbackFileId = "$file_id";#g;
  ' "$website_fallback_path"

  echo "$website_fallback_path points to file $latest_file_id for mod $mod_id."
done

if [ -n "$website_vercel_path" ]; then
  FILE_ID="$latest_file_id" MOD_ID="$mod_id" node - "$website_vercel_path" <<'EOF'
const fs = require("node:fs");

const filePath = process.argv[2];
const fileId = process.env.FILE_ID;
const modId = process.env.MOD_ID;

const config = JSON.parse(fs.readFileSync(filePath, "utf8"));
for (const route of config.redirects ?? []) {
  if (route.source === "/olympus") {
    route.destination = `everest:https://gamebanana.com/mmdl/${fileId},Mod,${modId}`;
  } else if (route.source === "/raw") {
    route.destination = `https://gamebanana.com/dl/${fileId}`;
  }
}
fs.writeFileSync(filePath, `${JSON.stringify(config, null, 2)}\n`);
EOF

  echo "Akron website redirect routes point to file $latest_file_id for mod $mod_id."
fi
