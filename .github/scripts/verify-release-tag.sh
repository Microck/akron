#!/usr/bin/env bash
set -euo pipefail

if ! [[ "${RELEASE_TAG:-}" =~ ^v[0-9A-Za-z][0-9A-Za-z._-]*$ ]]; then
  echo "::error::RELEASE_TAG has an unsupported format." >&2
  exit 1
fi
if ! [[ "${EXPECTED_RELEASE_COMMIT:-}" =~ ^[0-9a-f]{40}$ ]]; then
  echo "::error::EXPECTED_RELEASE_COMMIT must be a full commit SHA." >&2
  exit 1
fi

remote_url="https://github.com/${GITHUB_REPOSITORY}.git"
direct_sha=""
peeled_sha=""
while IFS=$'\t' read -r object_sha ref_name; do
  case "$ref_name" in
    "refs/tags/${RELEASE_TAG}") direct_sha="$object_sha" ;;
    "refs/tags/${RELEASE_TAG}^{}") peeled_sha="$object_sha" ;;
  esac
done < <(git ls-remote "$remote_url" "refs/tags/${RELEASE_TAG}" "refs/tags/${RELEASE_TAG}^{}")

resolved_commit="${peeled_sha:-$direct_sha}"
if [ -z "$resolved_commit" ]; then
  echo "::error::Release tag no longer exists on the remote." >&2
  exit 1
fi
if [ "$resolved_commit" != "$EXPECTED_RELEASE_COMMIT" ]; then
  echo "::error::Release tag moved after the immutable build was created." >&2
  exit 1
fi

echo "Verified ${RELEASE_TAG} still resolves to ${EXPECTED_RELEASE_COMMIT}."
