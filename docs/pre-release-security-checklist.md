# Pre-release security checklist

Complete these owner-controlled settings before publishing Akron.

## Protect release authority

In the `Microck/akron` repository settings:

1. Create a protected environment named `release-build`.
2. Require a maintainer reviewer and prevent self-review for `release-build`.
3. Allow the CI and release refs that need the private build inputs; required
   reviewers remain the authorization boundary for this environment.
4. Move `AKRON_CELESTE_REFS_URL` and `AKRON_CELESTE_REFS_TOKEN` into the
   `release-build` environment.
5. Add `AKRON_CELESTE_REFS_SHA256` as a `release-build` environment variable.
   Its value must be the lowercase SHA-256 of the exact private archive.
6. Create a protected environment named `release`.
7. Require a maintainer reviewer and prevent self-review for `release`. Allow
   the default branch for manual dispatches and protected `v*` tags.
8. Move the GameBanana credentials or storage-state secrets, Tailscale OAuth
   secrets, and `AKRON_WEBSITE_TOKEN` into `release`.
9. Add a repository ruleset for `refs/tags/v*` that restricts tag creation,
   update, and deletion to maintainers. Do not allow tag force updates.
10. Protect the default branch in both `Microck/akron` and
    `Microck/akron-discord` with required CI checks and maintainer-only bypass.
    Block force pushes and branch deletion.

The release workflow fails closed if a tag moves between build and publishing.
Environment protection and the tag ruleset prevent an unreviewed actor from
authorizing that workflow in the first place.

## Configure the Akron Discord container identity

From the deployment account that owns `akron-discord/data`, print its numeric
identity:

```sh
id -u
id -g
```

Copy those exact numbers into the deployment `.env`:

```text
AKRON_DOCKER_UID=<output of id -u>
AKRON_DOCKER_GID=<output of id -g>
```

Then confirm the secret and database permissions before starting the service:

```sh
chmod 600 .env data/*.sqlite data/*.sqlite-shm data/*.sqlite-wal
chmod 700 data
```
