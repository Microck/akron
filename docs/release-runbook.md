# Release runbook

Akron releases are published by `.github/workflows/release.yml`.

## Required configuration

Repository variables:

- `GAMEBANANA_SUBMISSION_ID`: GameBanana mod id. Current value is `681169`.

Repository secrets:

- `AKRON_CELESTE_REFS_URL`: archive containing the stripped Celeste references needed by CI.
- `AKRON_CELESTE_REFS_TOKEN`: optional token for the Celeste reference archive.
- `AKRON_WEBSITE_TOKEN`: token with `contents:write` access to `Microck/akron-website`.
- `GAMEBANANA_STORAGE_STATE_B64`: base64-encoded Playwright storage state for a manually verified GameBanana session.
- `GAMEBANANA_USERNAME` and `GAMEBANANA_PASSWORD`: fallback credentials for local/manual publisher runs. GitHub-hosted runners currently trigger GameBanana `UNKNOWN_DEVICE` captcha with direct username/password authentication, so automated releases require `GAMEBANANA_STORAGE_STATE_B64`.

## Publish flow

1. Create and push the release tag.
2. Run the `Release` workflow with `tag_name` set to that tag and `prerelease` set as needed.
3. The workflow builds, tests, packages `Akron-<tag>.zip`, updates the GitHub Release, uploads the zip to GameBanana, then syncs the README and `Microck/akron-website` GameBanana fallback file ids.

The workflow checks publishing configuration before creating or updating the GitHub Release. If GameBanana storage auth or the website token is missing, the release stops before producing another partial release.
