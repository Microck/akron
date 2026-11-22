---
title: Pre-release security checklist
description: Configure protected release environments, provider access, and deployment file permissions.
---

Complete these owner-controlled settings before publishing Akron.

This checklist covers standing repository and provider hardening. Use
[Security release checklist](./security-release-checklist.md) for credential
rotation and deployment checks after a security incident or exposed secret.

## Protect release authority

In the `Microck/akron` repository settings:

1. Create a protected environment named `release-build`.
2. Do not require deployment approval for `release-build`; CI uses this
   environment for every build and test run.
3. Allow only the CI and release refs that need the private build inputs.
4. Move `AKRON_CELESTE_REFS_URL` and `AKRON_CELESTE_REFS_TOKEN` into the
   `release-build` environment.
5. Add `AKRON_CELESTE_REFS_SHA256` as a `release-build` environment variable.
   Its value must be the lowercase SHA-256 of the exact private archive.
6. Create a protected environment named `release`.
7. Require a maintainer reviewer for `release` when a second maintainer is
   available, and prevent self-review in that configuration. A solo maintainer
   may self-authorize only after completing this checklist. Allow the default
   branch for manual dispatches and protected `v*` tags.
8. Add a repository ruleset for `refs/tags/v*` that restricts tag creation,
   update, and deletion to maintainers. Do not allow tag force updates.
9. Protect the default branch in `Microck/akron` with required CI checks and maintainer-only bypass.
    Block force pushes and branch deletion.

The release workflow fails closed if a tag moves between build and publishing.
The release environment and tag ruleset restrict publishing authority, while
`release-build` remains available to required CI checks.
