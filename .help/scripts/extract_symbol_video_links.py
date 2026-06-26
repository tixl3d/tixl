#!/usr/bin/env python3
"""
extract_symbol_video_links.py — migration STAGE 0: catalog the hand-authored YouTube links.

Operators carry curated links in their `.t3ui` files (`Links: [{Title, Description, LinkUrl, LinkType}]`).
Many point at YouTube tutorials that overlap our extracted video index. This walks the source `.t3ui`
files and writes a manifest of every YouTube link — which operator, which video, the curated title/blurb,
and whether that video is already in our corpus — so the rest of the migration can:
  • transcribe + analyze the *new* videos (with focusesOn = the linking operator),
  • dedup the ones already indexed,
  • strip the migrated links back out of the `.t3ui` files.

Output: .help/.tmp/symbol-video-links.json  (+ a console summary). Pure read; touches nothing else.
"""

import json
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
HELP = HERE.parent
ROOT = HELP.parent
OPERATORS = ROOT / "Operators"
VIDEOS_JSON = HELP / "references" / "indices" / "videos.json"
OUT = HELP / ".tmp" / "symbol-video-links.json"

BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.S)          # .t3ui annotates GUIDs with /*Name*/ — not valid JSON
YT_ID = re.compile(r"(?:youtu\.be/|[?&]v=)([A-Za-z0-9_-]{11})")


def op_path_from_file(p: Path) -> str:
    # Operators/<Package>/<sub…>/<Name>.t3ui  ->  <Package>.<sub…>.<Name>
    rel = p.relative_to(OPERATORS).with_suffix("")
    return ".".join(rel.parts)


def load_t3ui(p: Path):
    try:
        return json.loads(BLOCK_COMMENT.sub("", p.read_text(encoding="utf-8")))
    except Exception:
        return None


def main():
    corpus = set()
    if VIDEOS_JSON.exists():
        corpus = {v["id"] for v in json.loads(VIDEOS_JSON.read_text(encoding="utf-8")).get("videos", [])}

    entries, unparsed = [], 0
    for p in OPERATORS.rglob("*.t3ui"):
        if "/bin/" in p.as_posix() or "/obj/" in p.as_posix():
            continue
        data = load_t3ui(p)
        if data is None:
            unparsed += 1
            continue
        for link in data.get("Links") or []:
            url = link.get("LinkUrl") or ""
            m = YT_ID.search(url)
            if not m:
                continue
            entries.append({
                "operator": op_path_from_file(p),
                "shortName": p.stem,
                "videoId": m.group(1),
                "title": link.get("Title") or "",
                "description": link.get("Description") or "",
                "linkType": link.get("LinkType") or "",
                "url": url,
                "file": p.relative_to(ROOT).as_posix(),
                "inCorpus": m.group(1) in corpus,
            })

    by_video = {}
    for e in entries:
        by_video.setdefault(e["videoId"], []).append(e)
    new_ids = sorted(v for v in by_video if not v in corpus)

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({"links": entries, "newVideoIds": new_ids}, ensure_ascii=False, indent=2),
                   encoding="utf-8")

    print(f"{len(entries)} YouTube links across {len({e['file'] for e in entries})} operators")
    print(f"{len(by_video)} distinct videos — {len(by_video) - len(new_ids)} already in corpus, {len(new_ids)} NEW")
    if unparsed:
        print(f"  ({unparsed} .t3ui files could not be parsed)")
    print(f"NEW (need transcription): {', '.join(new_ids)}")
    print(f"-> {OUT.relative_to(ROOT).as_posix()}")


if __name__ == "__main__":
    main()
