#!/usr/bin/env python3
"""Select older published main or beta releases from paginated GitHub API JSON."""
import argparse
import json
import re
import sys


def matches_channel(release, channel):
    tag = release['tag_name']
    if channel == 'main':
        # Include the previous date-based main versions, but not named releases.
        return not release.get('prerelease') and bool(re.fullmatch(
            r'v(?:[1-9][0-9]*\.[1-9][0-9]*\.0|[0-9]{4}\.[0-9]{2}\.[0-9]{2}\.[0-9]+)', tag))
    # All beta branches share a pool with legacy feature prereleases.
    return release.get('prerelease') and (
        tag.startswith('feature-')
        or re.fullmatch(r'v[1-9][0-9]*\.[1-9][0-9]*\.0-beta', tag))


def stale_tags(pages, channel, keep):
    if channel not in ('main', 'feature') or keep < 1:
        raise ValueError('Channel must be main or feature and retention must be positive')
    releases = [release for page in pages for release in page
                if not release.get('draft') and matches_channel(release, channel)]
    # Retain the newest published builds, even when a branch targets an old commit.
    releases.sort(key=lambda release: (release['published_at'], release['id']), reverse=True)
    return [release['tag_name'] for release in releases[keep:]]


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--channel', choices=['main', 'feature'], required=True)
    parser.add_argument('--keep', type=int, required=True)
    args = parser.parse_args()
    for tag in stale_tags(json.load(sys.stdin), args.channel, args.keep):
        print(tag)
