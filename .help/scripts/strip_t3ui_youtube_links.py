#!/usr/bin/env python3
"""
strip_t3ui_youtube_links.py — migration final step: remove YouTube links from operator .t3ui files.

The video references now live in the .help index (or the video is a retired silent showcase), so the
hand-authored YouTube links in operator `.t3ui` files are redundant. This removes ONLY link objects whose
`LinkUrl` is a youtube URL, leaving every other link, all other JSON, and the inline /*…*/ comments and
formatting untouched (it does NOT round-trip the file through a JSON parser). If an operator's `Links`
array ends up empty it becomes `[]`.

Usage:
    python .help/scripts/strip_t3ui_youtube_links.py <file.t3ui> ...   # specific files (test run)
    python .help/scripts/strip_t3ui_youtube_links.py                   # all source .t3ui with a yt link
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OPERATORS = ROOT / "Operators"
YT = re.compile(r"youtu\.be|youtube\.com", re.I)
# The Links array; objects hold no nested brackets, so the first ] closes it.
LINKS = re.compile(r'(?P<ind>^[ \t]*)"Links"[ \t]*:[ \t]*\[(?P<body>.*?)\](?P<tail>,?)', re.S | re.M)
OBJ = re.compile(r"\{.*?\}", re.S)


def strip_text(text):
    m = LINKS.search(text)
    if not m:
        return text, 0
    objs = OBJ.findall(m.group("body"))
    if not objs:
        return text, 0
    kept = [o for o in objs if not YT.search(o)]
    removed = len(objs) - len(kept)
    if removed == 0:
        return text, 0
    ind = m.group("ind")
    item = ind + "  "
    if kept:
        rebuilt = f'{ind}"Links": [\n' + ",\n".join(item + o for o in kept) + f"\n{ind}]" + m.group("tail")
    else:
        rebuilt = f'{ind}"Links": []' + m.group("tail")
    return text[:m.start()] + rebuilt + text[m.end():], removed


def candidates():
    for p in OPERATORS.rglob("*.t3ui"):
        if "/bin/" in p.as_posix() or "/obj/" in p.as_posix():
            continue
        if YT.search(p.read_text(encoding="utf-8")):
            yield p


def main():
    files = [Path(a) for a in sys.argv[1:]] or list(candidates())
    total_files = total_links = 0
    for p in files:
        new, removed = strip_text(p.read_text(encoding="utf-8"))
        if removed:
            p.write_text(new, encoding="utf-8")
            total_files += 1
            total_links += removed
            print(f"  {removed} link(s) <- {p.resolve().relative_to(ROOT).as_posix()}")
    print(f"removed {total_links} YouTube link(s) from {total_files} operator file(s)")


if __name__ == "__main__":
    main()
