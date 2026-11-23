#!/usr/bin/env python3
"""Fails a tag build early when the tag and the version files disagree.

The editor finds its extensions in the GitHub release named v<TixlVersion>, the installers carry TixlVersion, and
Linux app stores read the release history from the metainfo file. So for a tag v4.4.0.6:
  - Tixl.props must say <TixlVersion>4.4.0.6</TixlVersion>
  - Installer/Linux/common/app.tixl.TiXL.metainfo.xml must have <release version="4.4.0.6" ...>

usage: check-release-version.py <tag>      e.g. check-release-version.py v4.4.0.6
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2

    tag = sys.argv[1]
    problems = []
    if not re.fullmatch(r"v\d+\.\d+\.\d+\.\d+", tag):
        problems.append(f"Tag '{tag}' is not v<major>.<minor>.<patch>.<build>; the editor looks for exactly that name "
                        "when it loads extensions.")

    version = tag[1:]
    with open(os.path.join(ROOT, "Tixl.props"), encoding="utf-8") as file:
        match = re.search(r"<TixlVersion>([^<]+)</TixlVersion>", file.read())
    tixl_version = match.group(1) if match else "?"
    if tixl_version != version:
        problems.append(f"Tixl.props has TixlVersion {tixl_version}, the tag is {tag}.")

    metainfo = os.path.join(ROOT, "Installer", "Linux", "common", "app.tixl.TiXL.metainfo.xml")
    with open(metainfo, encoding="utf-8") as file:
        if f'<release version="{version}"' not in file.read():
            problems.append(f'{os.path.relpath(metainfo, ROOT)} has no <release version="{version}" date="..."/> entry.')

    if problems:
        print("Release version check failed (see Installer/RELEASING.md):")
        for problem in problems:
            print("  " + problem)
        return 1

    print(f"Release version check passed: {tag}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
