#!/usr/bin/env python3
"""Select older published prerelease tags from paginated GitHub API JSON."""
import argparse
import json
import sys


def stale_tags(pages, channel, keep):
    if channel != 'feature' or keep < 1:
        raise ValueError('Channel must be feature and retention must be positive')
    releases = [release for page in pages for release in page
                if release.get('prerelease') and not release.get('draft')
                and release['tag_name'].startswith(channel + '-')]
    # Retain the newest published builds, even when a branch targets an old commit.
    releases.sort(key=lambda release: (release['published_at'], release['id']), reverse=True)
    return [release['tag_name'] for release in releases[keep:]]


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--channel', choices=['feature'], required=True)
    parser.add_argument('--keep', type=int, required=True)
    args = parser.parse_args()
    for tag in stale_tags(json.load(sys.stdin), args.channel, args.keep):
        print(tag)
