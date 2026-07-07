# Release Runbook

Use this when cutting or repairing an Akron release.

## Goal

Ship one version consistently across the GitHub tag, GitHub Release, GameBanana mod page, README install links, and `akron.micr.dev`.

The GitHub tag is the source of truth. A release is complete only after every required public surface has been confirmed updated. If one surface is stale after publishing starts, treat the release as partially published and repair it from the same tag.

## Release Contract

Required public surfaces:

- GitHub tag: `vX.Y.Z`
- GitHub Release: readable title, notes, `Akron-vX.Y.Z.zip`, and `Akron-vX.Y.Z.zip.sha256`
- GameBanana: release update and downloadable file for the same tag
- README: Olympus one-click handoff and raw-download links point at Akron's stable install endpoints
- `akron.micr.dev`: docs are current, install endpoints resolve to the published release, and fallback GameBanana file ids point at the published file

Tags and artifact names stay canonical because external links use them. Public titles and changelog headings can use readable names such as `Akron Beta 42`. Do not mint a replacement tag for a sync or publishing failure unless rollback or unpublish has been explicitly chosen. Normal recovery repairs the same tag.

## Required Configuration

See [Release Configuration](./reference/release-configuration) for the non-secret configuration matrix.

Repository variables:

- `GAMEBANANA_SUBMISSION_ID`: GameBanana mod id. Current value is `681169`.
- `GAMEBANANA_BROWSER`: optional browser automation package used by the GameBanana publisher. Defaults to `cloakbrowser`.
- `GAMEBANANA_USER_AGENT`: optional browser user agent replayed by the GameBanana publisher when the stored auth was captured with a browser profile that GameBanana expects to see again.
- `TAILSCALE_EXIT_NODE`: optional Tailscale exit node name or Tailscale IP used to route GameBanana publishing through a trusted network.
- `TAILSCALE_TAGS`: optional comma-separated Tailscale tags for the ephemeral Actions node. Defaults to `tag:ci`.

Repository secrets:

- `AKRON_CELESTE_REFS_URL`: archive containing the stripped Celeste references needed by CI.
- `AKRON_CELESTE_REFS_TOKEN`: optional token for the Celeste reference archive.
- `AKRON_WEBSITE_TOKEN`: token with `contents:write` access to `Microck/akron-website`.
- `GAMEBANANA_COOKIES`: base64-encoded JSON cookie export for GameBanana. This can be a Playwright storage state, an array of cookie objects, or a simple object of cookie names to values.
- `GAMEBANANA_STORAGE_STATE_B64`: base64-encoded Playwright storage state for a manually verified GameBanana session.
- `GAMEBANANA_STORAGE_STATE_B64_GZ`: gzip-compressed and base64-encoded Playwright storage state. Use this instead of `GAMEBANANA_STORAGE_STATE_B64` if the plain base64 value exceeds GitHub's 48 KB secret limit.
- `GAMEBANANA_USERNAME` and `GAMEBANANA_PASSWORD`: fallback credentials for local/manual publisher runs. GitHub-hosted runners currently trigger GameBanana `UNKNOWN_DEVICE` captcha with direct username/password authentication, so automated releases require cookie or storage-state auth.
- `TS_OAUTH_CLIENT_ID` and `TS_OAUTH_SECRET`: required only when `TAILSCALE_EXIT_NODE` is set. The OAuth client must be allowed to create tagged auth keys for the configured `TAILSCALE_TAGS`.

The workflow checks required publishing configuration before publishing to GameBanana or creating/updating the GitHub Release.

## Preflight

1. Merge approved work into `main`.
2. Confirm `main` is green.
3. Pick the release version `X.Y.Z`.
4. Audit every commit since the previous release tag before writing release notes:

```bash
release_ref="${RELEASE_REF:-HEAD}"
previous_tag="$(git describe --tags --abbrev=0 --match 'v*' "${release_ref}^")"
git log --reverse --decorate --oneline "${previous_tag}..${release_ref}"
git log --reverse --format='%h %s%n%b' "${previous_tag}..${release_ref}"
git diff --name-status "${previous_tag}..${release_ref}"
```

For each commit in that range, either add a concrete user-facing note to the new
`CHANGELOG.md` section or write down why it is intentionally omitted, such as a
test-only, docs-only, CI-only, or internal refactor commit. Do not leave notes
for commits after `previous_tag` under the previous version's changelog section.
If a commit message references issues or feedback, inspect that commit before
summarizing it; do not rely on the latest commit alone.

5. Update `CHANGELOG.md` before tagging. The workflow extracts release notes from a readable heading such as `## Akron Beta 42`, the canonical `## X.Y.Z`, or their bracketed equivalents; the matching section must exist and must cover the audited commit range.
6. Update docs under `docs/` for user-facing changes.
7. Check for hardcoded version, file id, or release text references that need to change.
8. Run the local release preflight:

```bash
make preflight-release
```

`make preflight-release` checks formatting without rewriting files, builds, tests, packages `Akron.zip`, and verifies the zip can be read. If formatting fails, run `make format`, inspect the diff, and rerun the preflight.

## Package Contents Contract

Before publishing, the release package must be a valid zip and contain the expected Everest mod payload.

Required contents:

- `everest.yaml`
- `bin/Akron.dll`
- `bin/ImGui.NET.dll`
- `bin/runtimes/linux-x64/native/libcimgui.so`
- `bin/runtimes/osx/native/libcimgui.dylib`
- at least one Windows `cimgui.dll` runtime under `bin/runtimes/`
- `Dialog/English.txt`

Check the local package:

```bash
make package
unzip -l Akron.zip
```

Missing required contents means do not publish. Fix packaging first.

## Publish

1. Push `main`.
2. Create and push the tag:

```bash
git tag vX.Y.Z
git push origin vX.Y.Z
```

3. The tag triggers `.github/workflows/release.yml`. You can also rerun the `Release` workflow manually with `tag_name` set to the existing tag and `prerelease` set as needed.

What the workflow does:

- checks out the tag
- restores Celeste reference DLLs
- restores, builds, and tests Akron
- verifies `Akron.zip`, copies it to `Akron-<tag>.zip`, and writes a SHA-256 file
- extracts release notes from `CHANGELOG.md`
- checks publishing configuration
- publishes the release update and zip to GameBanana
- creates or updates the GitHub Release
- syncs README GameBanana links on `main`
- syncs `Microck/akron-website` fallback GameBanana file ids

## Completion Checks

A release is complete only after all checks pass.

1. GitHub Release:

```bash
gh release view vX.Y.Z -R Microck/Akron
gh release download vX.Y.Z -R Microck/Akron --pattern 'Akron-vX.Y.Z*' --dir /tmp/akron-release-check
(cd /tmp/akron-release-check && sha256sum -c Akron-vX.Y.Z.zip.sha256)
unzip -t /tmp/akron-release-check/Akron-vX.Y.Z.zip
```

2. Workflow health:

```bash
gh run list -R Microck/Akron --workflow Release --limit 5
gh run list -R Microck/Akron --workflow 'Sync GameBanana README Links' --limit 5
```

3. GameBanana:

- Verify the rendered mod page shows the new release/update.
- Verify the latest file is downloadable and matches the released version.
- Verify `https://akron.micr.dev/olympus` resolves to the current `everest:` install URL and `https://akron.micr.dev/raw` resolves to the new raw download file.

4. README:

```bash
grep -n 'akron.micr.dev/olympus\|akron.micr.dev/raw' README.md
```

Confirm the image buttons point at Akron's stable install endpoints.

5. `akron.micr.dev`:

- Verify the rendered public site reflects the released docs.
- Verify install/download links resolve to the current GameBanana file.
- Verify any changed docs pages render correctly.

Rendered public pages should be checked with a browser-capable CLI or agent workflow when possible. API and text checks are useful, but they do not catch stale rendered content, broken layout, or bad public links.

## Partial Publish Recovery

Use the same tag for recovery unless rollback/unpublish has been explicitly chosen.

### Release workflow failed before GameBanana publishing

No public release should exist yet. Fix the configuration, changelog, package, or build failure, then rerun the existing tag:

```bash
gh workflow run release.yml -R Microck/Akron -f tag_name=vX.Y.Z -f prerelease=false
```

### GameBanana failed

Inspect the failed workflow logs and the uploaded `gamebanana-debug-vX.Y.Z` artifact. Common causes are expired storage state, missing edit permission, captcha/permit pages, or an untrusted runner IP.

If the stored session is invalid, refresh the storage state using the storage-state procedure below, update the secret, and rerun the release workflow for the same tag.

If the runner IP is the issue and `TAILSCALE_EXIT_NODE` is configured, verify the workflow's Tailscale status and public egress IP before treating the cookie as bad.

### GitHub Release failed after GameBanana succeeded

Rerun the release workflow for the same tag. The workflow updates existing GameBanana/GitHub state where possible and uploads release assets with `--clobber`.

```bash
gh workflow run release.yml -R Microck/Akron -f tag_name=vX.Y.Z -f prerelease=false
gh release view vX.Y.Z -R Microck/Akron
```

### README link sync failed

Run the sync workflow or run the script locally after identifying the intended GameBanana file id:

```bash
gh workflow run sync-gamebanana-readme-links.yml -R Microck/Akron
AKRON_GAMEBANANA_FILE_ID=<file-id> bash scripts/sync-gamebanana-readme-links.sh
```

Commit and push the README update if running locally.

### `akron.micr.dev` link sync failed

Inspect the `Sync Akron website GameBanana links` step. The usual causes are a missing or under-scoped `AKRON_WEBSITE_TOKEN`, a changed website source path, or a stale GameBanana file id.

Verify the website repository state:

```bash
gh repo view Microck/akron-website
gh run list -R Microck/Akron --workflow Release --limit 5
```

If the release workflow already published GameBanana and GitHub successfully, repair the website from the same file id and tag rather than creating a new Akron release.

### Rendered public pages are stale

First distinguish deployment delay from wrong source state:

- Check the committed README and `Microck/akron-website` source values.
- Check the rendered pages again after the deployment window.
- If source is wrong, repair the source and redeploy.
- If source is right but rendered output is stale, inspect the website deployment provider before changing release assets.

## Exceptional Rollback Or Unpublish

Rollback or unpublish is not the normal path for release failures. Use it only after an explicit decision, because deleted or replaced public releases can confuse users, external links, and GameBanana/Olympus install state.

Prefer repairing the same tag when the issue is auth, upload, README sync, website sync, or rendered-page staleness.

## Optional Tailscale Exit Node

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

## GameBanana Storage State Size

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
