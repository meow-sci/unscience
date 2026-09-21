#!/usr/bin/env bash
set -euo pipefail

BASE="$(sed -nE 's/^version = "(.*)"/\1/p' unscience/mod.toml)"
if [[ ! "$BASE" =~ ^[0-9A-Za-z._-]+$ ]]; then
  echo '::error::Invalid base version in unscience/mod.toml' >&2
  exit 1
fi

# Only branch pushes and manual branch runs may publish. PRs and tags build only.
if [[ "$GITHUB_EVENT_NAME" != push && "$GITHUB_EVENT_NAME" != workflow_dispatch ]] ||
   [[ "$GITHUB_REF" != refs/heads/* ]]; then
  echo 'publish=false' >> "$GITHUB_OUTPUT"
  exit 0
fi
BRANCH="${GITHUB_REF#refs/heads/}"
if [[ "$BRANCH" == release/* ]]; then
  NAME="${BRANCH#release/}"
  if [[ ! "$NAME" =~ ^[0-9A-Za-z._-]+$ ]]; then
    echo '::error::Release branch suffix must contain only letters, digits, dots, underscores or hyphens' >&2
    exit 1
  fi
  {
    echo 'publish=true'
    echo "version=$NAME"
    echo "modversion=$NAME"
    echo "tag=v$NAME"
    echo "title=unscience $NAME"
    echo 'prerelease=false'
    echo 'channel='
  } >> "$GITHUB_OUTPUT"
elif [[ "$BRANCH" == main ]]; then
  # GitHub increments this workflow-wide counter for every new run, across refs.
  # Reruns keep their number; publishing preserves any existing dated release.
  if [[ ! "${GITHUB_RUN_NUMBER:-}" =~ ^[1-9][0-9]*$ ]]; then
    echo '::error::Invalid workflow run number' >&2
    exit 1
  fi
  NAME="$(date -u +%Y.%m.%d).$GITHUB_RUN_NUMBER"
  {
    echo 'publish=true'
    echo "version=$NAME"
    echo "modversion=$NAME"
    echo "tag=v$NAME"
    echo "title=unscience $NAME"
    echo 'prerelease=false'
    echo 'channel='
  } >> "$GITHUB_OUTPUT"
elif [[ "$BRANCH" == feature/* ]]; then
  CHANNEL=feature
  # IDs distinguish simultaneous branches and reruns within the same UTC second.
  if [[ ! "$GITHUB_RUN_ID" =~ ^[0-9]+$ || ! "$GITHUB_RUN_ATTEMPT" =~ ^[0-9]+$ ]]; then
    echo '::error::Invalid workflow run identity' >&2
    exit 1
  fi
  STAMP="$(date -u +%Y%m%d-%H%M%S)-$GITHUB_RUN_ID-$GITHUB_RUN_ATTEMPT"
  {
    echo 'publish=true'
    echo "version=$CHANNEL-$STAMP"
    echo "modversion=$BASE-$CHANNEL.$STAMP"
    echo "tag=$CHANNEL-$STAMP"
    echo "title=unscience $CHANNEL $STAMP (UTC)"
    echo 'prerelease=true'
    echo "channel=$CHANNEL"
  } >> "$GITHUB_OUTPUT"
else
  echo 'publish=false' >> "$GITHUB_OUTPUT"
fi
