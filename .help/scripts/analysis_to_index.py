#!/usr/bin/env python3
"""
analysis_to_index.py — STAGE 3 of the video -> docs pipeline.

Reads the committed per-video analyses (written by the /analyze-videos skill) and builds the
reference index the editor reads:

    references/video-analysis/<id>.md  ->  references/indices/videos.json + mentions.json

videos.json   = { "videos": [ { id, type, date, title, url, duration } ] }
mentions.json = { "op:<fullpath>": [ { video, t, tLabel, url, depth, note } ], ... }

Operators are taken from explicit [OpName] markers in each mention line and resolved against the
generated operator doc index (docs/operators/index.json). Pure, in-repo, never touches git.
See .agentic/DOCUMENTATION_ECOSYSTEM.md.
"""

import json
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
HELP = HERE.parent
ANALYSES = HELP / "references" / "video-analysis"
INDICES = HELP / "references" / "indices"
OP_INDEX = HELP / "docs" / "operators" / "index.json"

TS_RE = re.compile(r"^(\d{1,2}:\d{2}(?::\d{2})?)")
OP_RE = re.compile(r"\[([A-Za-z][A-Za-z0-9]*)\]")
DEPTHS = ("in-depth", "explained", "passing")
NOTE_RE = re.compile(r"[—–]\s*(.+)$")  # em/en dash only — NOT the hyphen inside "in-depth"


def parse_seconds(ts):
    p = [int(x) for x in ts.split(":")]
    return p[0] * 60 + p[1] if len(p) == 2 else p[0] * 3600 + p[1] * 60 + p[2]


def parse_analysis(path):
    text = path.read_text(encoding="utf-8", errors="replace").replace("\r\n", "\n")
    meta = {}
    body = text
    fm = re.match(r"^---\n(.*?)\n---\n", text, re.S)
    if fm:
        for line in fm.group(1).splitlines():
            if ":" in line:
                k, v = line.split(":", 1)
                meta[k.strip()] = v.strip()
        body = text[fm.end():]

    mentions = []
    for line in body.splitlines():
        s = line.strip()
        if not (s.startswith("- ") or s.startswith("* ")):
            continue
        s = s[2:].strip()
        tm = TS_RE.match(s)
        if not tm:
            continue
        rest = s[tm.end():].strip()
        ops = OP_RE.findall(rest)
        if not ops:
            continue
        depth = next((d for d in DEPTHS if d in rest.lower()), None)
        nm = NOTE_RE.search(rest)
        mentions.append({"t": parse_seconds(tm.group(1)), "tLabel": tm.group(1),
                         "ops": ops, "depth": depth, "note": nm.group(1).strip() if nm else ""})
    return meta, mentions


def build():
    op_index = json.loads(OP_INDEX.read_text(encoding="utf-8"))
    by_short = op_index.get("by_shortname", {})
    # Case-insensitive fallback: TiXL capitalizes acronyms as Ik/Sdf/Obj/2d (IkChain, LoadObj,
    # Image2dSDF), but transcripts/extractors routinely over-capitalize them (IKChain, LoadOBJ).
    # Only unambiguous lowercase forms qualify, so this never silently picks the wrong operator.
    ci = {}
    for k in by_short:
        ci.setdefault(k.lower(), []).append(k)
    ci = {lk: ks[0] for lk, ks in ci.items() if len(ks) == 1}
    INDICES.mkdir(parents=True, exist_ok=True)
    videos, mentions_idx, unknown = [], {}, {}

    for path in sorted(ANALYSES.glob("*.md")):
        meta, mentions = parse_analysis(path)
        vid = meta.get("video") or path.stem
        url0 = f"https://www.youtube.com/watch?v={vid}"
        videos.append({"id": vid, "type": meta.get("type"), "date": meta.get("date"),
                       "title": meta.get("title"), "duration": meta.get("duration"), "url": url0})
        for mn in mentions:
            for short in mn["ops"]:
                paths = by_short.get(short) or by_short.get(ci.get(short.lower(), ""))
                if not paths:
                    unknown[short] = unknown.get(short, 0) + 1
                    continue
                for full in paths:
                    mentions_idx.setdefault(f"op:{full}", []).append(
                        {"video": vid, "t": mn["t"], "tLabel": mn["tLabel"],
                         "url": f"{url0}&t={mn['t']}s", "depth": mn["depth"], "note": mn["note"]})

    (INDICES / "videos.json").write_text(
        json.dumps({"videos": videos}, ensure_ascii=False, indent=2), encoding="utf-8")
    (INDICES / "mentions.json").write_text(
        json.dumps(mentions_idx, ensure_ascii=False, indent=2), encoding="utf-8")
    return videos, mentions_idx, unknown


def main():
    if not ANALYSES.is_dir() or not any(ANALYSES.glob("*.md")):
        print(f"No analyses found in {ANALYSES} — run /analyze-videos first.")
        return
    videos, mentions, unknown = build()
    print(f"{len(videos)} video(s) · {len(mentions)} operators with mentions · "
          f"{sum(len(v) for v in mentions.values())} total mentions")
    if unknown:
        top = sorted(unknown.items(), key=lambda kv: -kv[1])[:12]
        print("Unresolved op names (check the brackets in the analyses): "
              + ", ".join(f"{n}×{c}" for n, c in top))
    print("-> references/indices/videos.json + mentions.json  (nothing committed)")


if __name__ == "__main__":
    main()
