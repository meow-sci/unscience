#!/usr/bin/env bash
set -euo pipefail

ZIP="$UNSCIENCE_DIST_DIR/unscience-$VERSION.zip"
if gh release view "$TAG" --repo "$GITHUB_REPOSITORY" >/dev/null 2>&1; then
  # Reruns keep their version and preserve an already-published release.
  echo "Preserving existing release $TAG"
  exit 0
fi

EXTRA=(--repo "$GITHUB_REPOSITORY")
[[ "$PRERELEASE" == "true" ]] && EXTRA+=(--prerelease --latest=false)
gh release create "$TAG" "$ZIP" \
  --target "$GITHUB_SHA" \
  --title "$TITLE" \
  --notes "**Installing over an existing install:** delete the old \`unscience/\` folder from your mods directory before unzipping — files removed from the mod are not removed by unzipping over it." \
  --generate-notes \
  "${EXTRA[@]}"
