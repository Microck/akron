# Security release checklist

Use this checklist after the repository security tests pass and before publishing Akron or deploying Akron Discord. It covers controls that source changes cannot apply to provider accounts automatically.

## 1. Rotate exposed credentials

Rotate every credential that appeared in the retained local environment-file backup:

- Discord bot token
- R2 access key secret
- NVIDIA NIM API key
- GitHub personal access token
- GitHub webhook secret
- Upload-worker bot HMAC secret

Update the deployment secret stores directly. Do not put replacement values in Git, issue text, CI logs, or this checklist. Restart the affected services only after both sides of each shared secret have been updated.

After rotation, run a redacted full-history Gitleaks scan against every local and remote ref. Only then decide whether to remove the retained local jj revision containing the old backup. History cleanup is not a substitute for rotation.

## 2. Lock down host files

On the Akron Discord host, verify:

```bash
chmod 600 .env
find data -maxdepth 1 -type f -name 'akron-discord.sqlite*' -exec chmod 600 {} +
chmod 700 data
find .env data -maxdepth 1 -printf '%m %p\n'
```

The service also enforces restrictive creation modes, but existing backups and copied database files must be checked separately.

## 3. Configure upload-edge controls

For the public upload Worker:

- Deploy the current D1 migrations before the Worker code.
- Enable the scheduled cleanup trigger and confirm expired quarantine objects are deleted from both D1 and R2.
- Add Cloudflare rate limits for prepare, object upload, completion, and attribution routes.
- Keep direct-origin access behind Cloudflare so `CF-Connecting-IP` is authoritative.
- Configure an R2 lifecycle rule as a second cleanup layer for abandoned quarantine objects.
- Alert on quota rejection rate, quarantine bytes, cleanup failures, and moderation queue age.

## 4. Apply and verify Discord permissions

Run the normal server synchronization so the playtester announcements channel becomes staff/bot-write-only. Then verify with a non-staff Tester account that it cannot post messages or attachments there.

Confirm that upload attribution only reaches a member of the configured guild and that repeated claims remain within the configured cooldown.

## 5. Configure release secrets and approvals

- Set `AKRON_CELESTE_REFS_SHA256` to the reviewed SHA-256 of the exact private reference archive.
- Create protected `release-build` and `release` environments with required
  maintainer approval and self-review disabled.
- Restrict GameBanana, Tailscale, and GitHub publishing secrets to the isolated publish job/environment.
- Protect the default branches in both `Microck/akron` and
  `Microck/akron-discord` with required CI checks, no force pushes, and no
  deletion. Protect Akron's `refs/tags/v*` against unauthorized creation,
  movement, or deletion.
- Confirm failure artifacts contain only the sanitized JSON summary and use the configured short retention period.

## 6. Final verification

- Run the full Akron and Akron Discord test suites.
- Run npm and NuGet advisory scans.
- Run the redacted secret scan.
- Build the player archive and verify that it contains no PDB, environment, database, test, or credential files.
- Generate and retain the release SBOM and GitHub artifact attestation.
- Test the upload, moderation, catalog download, and pack-import paths on the remote Linux Mint machine before announcing the release.
