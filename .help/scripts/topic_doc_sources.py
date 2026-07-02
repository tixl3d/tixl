#!/usr/bin/env python3
"""
topic_doc_sources.py — gather source material for writing a ui: topic's embedded help.

Prints the bundle the write-topic-docs skill distills into a topic's doc: the transcript passages
where the topic is explained (found via the ui: deep-link index, ranked in-depth > explained >
passing, recent first), the implementing class(es) to fact-check against, and any existing .help/
page. Read-only, in-repo.

    python topic_doc_sources.py ui:Timeline   # one topic
    python topic_doc_sources.py               # every topic whose doc is still empty
"""

import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
HELP = HERE.parent
IDX = HELP / "references" / "indices"
SRTS = HELP / ".tmp" / "video-transcripts"
DOCS = HELP / "docs"

DEPTH_RANK = {"in-depth": 0, "explained": 1, "passing": 2}
WIN_BEFORE, WIN_AFTER = 75, 90      # seconds of transcript to pull around each deep-link
TOP = 5
CUE = re.compile(r"(\d\d):(\d\d):(\d\d),\d+\s*-->")


def date_key(d):
    return int(d.replace("-", "")) if d else 0


def srt_cues(path):
    """[(start_seconds, text)] from a final .srt."""
    out = []
    if not path.exists():
        return out
    for block in re.split(r"\n\s*\n", path.read_text(encoding="utf-8", errors="replace").strip()):
        lines = [l for l in block.splitlines() if l.strip()]
        ti = next((i for i, l in enumerate(lines) if "-->" in l), None)
        if ti is None:
            continue
        m = CUE.search(lines[ti])
        if m:
            s = int(m.group(1)) * 3600 + int(m.group(2)) * 60 + int(m.group(3))
            out.append((s, " ".join(lines[ti + 1:]).strip()))
    return out


def span_text(vid, t):
    cues = srt_cues(SRTS / f"{vid}.srt")
    picked = [tx for (s, tx) in cues if t - WIN_BEFORE <= s <= t + WIN_AFTER and tx]
    return " ".join(picked)


def time_label(seconds):
    seconds = int(seconds or 0)
    h, rem = divmod(seconds, 3600)
    m, s = divmod(rem, 60)
    return f"{h}:{m:02d}:{s:02d}" if h else f"{m}:{s:02d}"


def main():
    # Windows console defaults to cp1252, which can't encode arrows / em-dashes in notes.
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

    topics = json.loads((IDX / "topics.json").read_text(encoding="utf-8"))["topics"]
    mentions = json.loads((IDX / "mentions.json").read_text(encoding="utf-8"))
    videos = {v["id"]: v for v in json.loads((IDX / "videos.json").read_text(encoding="utf-8"))["videos"]}

    arg = sys.argv[1] if len(sys.argv) > 1 else None
    if arg:
        key = arg if arg.startswith("ui:") else f"ui:{arg}"
        if key not in topics:
            sys.exit(f"Unknown topic '{key}'. See ids in {IDX / 'topics.json'}.")
        targets = [key]
    else:
        targets = [k for k, v in topics.items() if not v.get("doc")]
        print(f"# {len(targets)} topic(s) with an empty doc\n")

    for key in targets:
        t = topics[key]
        print("=" * 88)
        head = f"{key}   term: {t['term']}   classes: {', '.join(t['classes']) or '(none)'}"
        if t.get("parent"):
            head += f"   parent: {t['parent']}"
        print(head)

        hits = sorted(p.relative_to(HELP).as_posix() for p in DOCS.rglob("*.md")
                      if t["term"].replace(" ", "").lower() in p.stem.replace(" ", "").lower())
        if hits:
            print("existing .help pages:", ", ".join(hits))

        ms = list(mentions.get(key, []))
        ms.sort(key=lambda m: (DEPTH_RANK.get(m.get("depth"), 3),
                               -date_key(videos.get(m["video"], {}).get("date"))))
        if not ms:
            print("video explanations: (none yet — run /analyze-videos with the topic vocabulary first)")
        for m in ms[:TOP]:
            v = videos.get(m["video"], {})
            start = m.get("startSecond", 0)
            txt = span_text(m["video"], start)
            print(f"\n--- [{m.get('depth')}] {v.get('date', '?')} "
                  f"\"{(v.get('title') or m['video'])[:60]}\" @ {time_label(start)}  {m.get('url', '')}")
            if m.get("note"):
                print(f"    note: {m['note']}")
            print("    " + (txt[:1400] if txt else "(transcript span empty)"))
        print()


if __name__ == "__main__":
    main()
