# Offline releases

Run the [release command on main](https://github.com/Microck/akron/blob/main/docs/release-runbook.md) to publish both editions. It pins this branch with an `offline/vX.Y.Z` tag. The GitHub release for `vX.Y.Z` includes the offline ZIP, checksum, dependency list, and SBOM.

For a local offline package, run `make package`. The filename includes the version in `everest.yaml`, for example `Akron-Offline-v1.0.0.zip`.
