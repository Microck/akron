# Release runbook

Akron releases are published by `.github/workflows/release.yml`.

## Required configuration

Repository variables:

- `GAMEBANANA_SUBMISSION_ID`: GameBanana mod id. Current value is `681169`.
- `TAILSCALE_EXIT_NODE`: optional Tailscale exit node name or Tailscale IP used to route GameBanana publishing through a trusted network.
- `TAILSCALE_TAGS`: optional comma-separated Tailscale tags for the ephemeral Actions node. Defaults to `tag:ci`.

Repository secrets:

- `AKRON_CELESTE_REFS_URL`: archive containing the stripped Celeste references needed by CI.
- `AKRON_CELESTE_REFS_TOKEN`: optional token for the Celeste reference archive.
- `AKRON_WEBSITE_TOKEN`: token with `contents:write` access to `Microck/akron-website`.
- `GAMEBANANA_COOKIES`: base64-encoded JSON cookie export for GameBanana. This can be a Playwright storage state, an array of cookie objects, or a simple object of cookie names to values.
- `GAMEBANANA_STORAGE_STATE_B64`: base64-encoded Playwright storage state for a manually verified GameBanana session.
- `GAMEBANANA_STORAGE_STATE_B64_GZ`: gzip-compressed and base64-encoded Playwright storage state. Use this instead of `GAMEBANANA_STORAGE_STATE_B64` if the plain base64 value exceeds GitHub's 48 KB secret limit.
- `GAMEBANANA_USERNAME` and `GAMEBANANA_PASSWORD`: fallback credentials for local/manual publisher runs. GitHub-hosted runners currently trigger GameBanana `UNKNOWN_DEVICE` captcha with direct username/password authentication, so automated releases require `GAMEBANANA_STORAGE_STATE_B64`.
- `TS_OAUTH_CLIENT_ID` and `TS_OAUTH_SECRET`: required only when `TAILSCALE_EXIT_NODE` is set. The OAuth client must be allowed to create tagged auth keys for the configured `TAILSCALE_TAGS`.

## Optional Tailscale exit node

Use this when GameBanana accepts the stored session from your machine but rejects the same cookies from GitHub-hosted runner IPs.

On the trusted machine or network that can use the GameBanana session, advertise it as a Tailscale exit node:

```bash
sudo tailscale up --advertise-exit-node
```

Approve the advertised exit node in the Tailscale admin console. Disable key expiry for that connector if it is meant to support unattended releases.

Create a Tailscale OAuth client with the writable `auth_keys` scope and a tag such as `tag:ci`. Add a matching ACL tag owner rule in Tailscale if needed, then store the credentials:

```bash
gh secret set TS_OAUTH_CLIENT_ID -R Microck/Akron
gh secret set TS_OAUTH_SECRET -R Microck/Akron
gh variable set TAILSCALE_TAGS -R Microck/Akron --body tag:ci
gh variable set TAILSCALE_EXIT_NODE -R Microck/Akron --body <exit-node-name-or-100.x.y.z>
```

The release workflow joins the tailnet before GameBanana publishing, selects `TAILSCALE_EXIT_NODE` with `tailscale up --exit-node=...`, prints `tailscale status`, and prints the public egress IP from `ifconfig.me`. Verify that this IP matches the trusted exit node before treating a release failure as a cookie problem.

## GameBanana storage state size

Before setting the secret, verify the captured state can open the Akron edit form without a permit or captcha page:

```bash
npx playwright codegen --browser=chromium \
  --load-storage=gamebanana-storage-state.json \
  https://gamebanana.com/mods/edit/681169
```

Only continue once the loaded browser is visibly authenticated as the Akron owner and can edit the mod. A state that only passes Cloudflare but cannot edit `681169` will still fail in GitHub Actions because direct username/password login triggers GameBanana `UNKNOWN_DEVICE`.

Do not upload a full browser profile state if it is too large for GitHub secrets. Keep only GameBanana cookies first:

```bash
jq -c '
  {
    cookies: [
      .cookies[]
      | select((.domain | ltrimstr(".")) | endswith("gamebanana.com"))
    ],
    origins: []
  }
' gamebanana-storage-state.json > gamebanana-storage-state.min.json

npx playwright codegen --browser=chromium \
  --load-storage=gamebanana-storage-state.min.json \
  https://gamebanana.com/mods/edit/681169

base64 < gamebanana-storage-state.min.json | tr -d '\n' \
  | gh secret set GAMEBANANA_STORAGE_STATE_B64 -R Microck/Akron --body-file -
```

If the minified state does not open the edit form, do not set it as `GAMEBANANA_STORAGE_STATE_B64`; use the verified full state through the gzip path instead.

If the minimized cookie state is still larger than 48 KB, store the gzip form:

```bash
gzip -9c gamebanana-storage-state.min.json | base64 | tr -d '\n' \
  | gh secret set GAMEBANANA_STORAGE_STATE_B64_GZ -R Microck/Akron --body-file -
```

## Publish flow

1. Create and push the release tag.
2. Run the `Release` workflow with `tag_name` set to that tag and `prerelease` set as needed.
3. The workflow builds, tests, packages `Akron-<tag>.zip`, uploads the zip to GameBanana, updates the GitHub Release, then syncs the README and `Microck/akron-website` GameBanana fallback file ids.

The workflow checks publishing configuration before publishing and uploads to GameBanana before creating or updating the GitHub Release. If GameBanana storage auth, edit permission, or the website token is wrong, the release stops before producing another partial GitHub release.
