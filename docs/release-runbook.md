# Release Akron on GitHub

1. Add player-facing changes under `## Unreleased` in CHANGELOG.md. Prepare a clean, current checkout of the branch you intend to release, in sync with the same branch on origin with the .NET SDK and the Celeste/Everest build references.
2. Run `scripts/release/cut-release.sh X.Y.Z` (or `X.Y.Z-beta.N`). It updates the Everest version and changelog, builds and tests the mod, then pushes that branch and the release tag together.
3. The Publish standalone release workflow builds the tagged revision and attaches the versioned ZIP, SHA-256 checksum, dependency list and software bill of materials to the GitHub release. To retry a failed release, rerun its failed jobs or dispatch the workflow with the same tag.
4. The final workflow step downloads the ZIP and checks the posted files, ZIP integrity and checksum. To check an existing tag, run `scripts/release/verify-release.sh vX.Y.Z`.
5. Install the versioned ZIP directly in the Everest Mods folder and open Akron with Tab.

See [release configuration](reference/release-configuration.mdx) for the build reference settings.
