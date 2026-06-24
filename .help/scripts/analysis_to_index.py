#!/usr/bin/env python3
"""
analysis_to_index.py — STAGE 3 of the video -> docs pipeline.

Reads the committed per-video analyses (written by the /analyze-videos skill) plus the hand-authored
UI-topic registry, and builds the reference indices the editor reads:

    references/video-analysis/<id>.md  +  references/topics/ui-topics.md
        -> references/indices/videos.json + mentions.json + topics.json

videos.json   = { "videos": [ { id, type, date, title, url, duration } ] }
topics.json   = { "topics": { "ui:<id>": { term, parent, synonyms, classes, doc } } }
mentions.json = { "op:<fullpath>" | "ui:<id>": [ { video, startSecond, duration, url, depth, style, confidence, note } ] }

Each mention line is `<start>[→<end>] [Op]/[ui:Id] · <depth> · <style> · <conf>% — <note>`. The start/end
give `startSecond` + `duration` (platform-agnostic, no human label). The note is user-facing and may
itself contain `[Op]` links for the help UI's auto-linker — those are NOT counted as mentions (only
markers before the note dash are). Overlapping segments of the same key+video are merged. Operators
resolve against docs/operators/index.json (case-insensitively); a bare marker that isn't an operator
falls back to the topic registry. Pure, in-repo, never touches git.
"""

import json
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
HELP = HERE.parent
ANALYSES = HELP / "references" / "video-analysis"
TOPICS_MD = HELP / "references" / "topics" / "ui-topics.md"
INDICES = HELP / "references" / "indices"
OP_INDEX = HELP / "docs" / "operators" / "index.json"

TS = r"\d{1,2}:\d{2}(?::\d{2})?"
RANGE_RE = re.compile(rf"^({TS})\s*(?:[→-]\s*({TS}))?")   # start, optional →end (arrow or hyphen)
MARK_RE = re.compile(r"\[((?:ui:)+)?([A-Za-z][A-Za-z0-9]*)\]")  # [DrawPoints] / [ui:Timeline] (tolerates [ui:ui:X])
DEPTHS = ("in-depth", "explained", "passing")
DEPTH_RANK = {"in-depth": 0, "explained": 1, "passing": 2}
STYLES = ("scripted", "answer", "discussion", "experiment")   # how structured/trustworthy the moment is
CONF_RE = re.compile(r"(\d{1,3})%")                 # per-segment confidence, e.g. 85%
NOTE_RE = re.compile(r"[—–]\s*(.+)$")               # em/en dash only — NOT the range hyphen


def parse_seconds(ts):
    p = [int(x) for x in ts.split(":")]
    return p[0] * 60 + p[1] if len(p) == 2 else p[0] * 3600 + p[1] * 60 + p[2]


def pascal(text):
    return "".join(w[:1].upper() + w[1:] for w in re.split(r"[^A-Za-z0-9]+", text) if w)


def parse_analysis(path):
    text = path.read_text(encoding="utf-8", errors="replace").replace("\r\n", "\n")
    meta, body = {}, text
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
        rm = RANGE_RE.match(s)
        if not rm:
            continue
        start = parse_seconds(rm.group(1))
        duration = max(0, parse_seconds(rm.group(2)) - start) if rm.group(2) else 0
        rest = s[rm.end():].strip()

        nm = NOTE_RE.search(rest)
        note = nm.group(1).strip() if nm else ""
        head = rest[:nm.start()] if nm else rest            # markers + depth, before the note
        marks = [("ui" if m.group(1) else "op", m.group(2)) for m in MARK_RE.finditer(head)]
        if not marks:
            continue
        depth = next((d for d in DEPTHS if d in head.lower()), None)
        style = next((f for f in STYLES if f in head.lower()), None)
        cm = CONF_RE.search(head)
        confidence = int(cm.group(1)) if cm else None
        mentions.append({"startSecond": start, "duration": duration, "marks": marks,
                         "depth": depth, "style": style, "confidence": confidence, "note": note})
    return meta, mentions


def parse_topics():
    if not TOPICS_MD.exists():
        return {}
    text = TOPICS_MD.read_text(encoding="utf-8", errors="replace").replace("\r\n", "\n")
    topics = {}
    for block in re.split(r"^##\s+", text, flags=re.M)[1:]:
        lines = block.splitlines()
        term = lines[0].strip()
        meta, i = {}, 1
        while i < len(lines):
            ln = lines[i].strip()
            if not ln:
                i += 1
                break
            m = re.match(r"([A-Za-z]+):\s*(.*)$", ln)
            if not m:
                break
            meta[m.group(1).lower()] = m.group(2).strip()
            i += 1
        doc = "\n".join(lines[i:]).strip()
        if doc.lower().startswith("_todo"):
            doc = ""
        tid = meta.get("id") or pascal(term)
        split = lambda key: [x.strip() for x in meta.get(key, "").split(",") if x.strip()]
        topics[tid] = {"term": term, "parent": meta.get("parent") or None,
                       "synonyms": split("synonyms"), "classes": split("classes"), "doc": doc}
    return topics


def merge_segments(segs):
    """Merge overlapping segments (same key+video); the deeper segment's note wins."""
    out = []
    for s in sorted(segs, key=lambda x: x["startSecond"]):
        if out and s["startSecond"] < out[-1]["startSecond"] + out[-1]["duration"]:   # true overlap only
            prev = out[-1]
            if DEPTH_RANK.get(s["depth"], 3) < DEPTH_RANK.get(prev["depth"], 3):
                prev["depth"], prev["style"], prev["confidence"], prev["note"] = \
                    s["depth"], s["style"], s["confidence"], s["note"]
            end = max(prev["startSecond"] + prev["duration"], s["startSecond"] + s["duration"])
            prev["duration"] = end - prev["startSecond"]
        else:
            out.append(dict(s))
    return out


def build():
    op_index = json.loads(OP_INDEX.read_text(encoding="utf-8"))
    by_short = op_index.get("by_shortname", {})
    # Case-insensitive op fallback: TiXL capitalizes acronyms as Ik/Sdf/Obj/2d, but extractors
    # routinely over-capitalize (IKChain, LoadOBJ). Only unambiguous lowercase forms qualify.
    ci = {}
    for k in by_short:
        ci.setdefault(k.lower(), []).append(k)
    ci = {lk: ks[0] for lk, ks in ci.items() if len(ks) == 1}

    topics = parse_topics()
    topic_lookup = {}
    for tid, t in topics.items():
        topic_lookup[tid.lower()] = tid
        for syn in t["synonyms"]:
            topic_lookup.setdefault(syn.lower(), tid)

    def keys_for(kind, name):
        if kind == "ui":
            tid = topic_lookup.get(name.lower())
            return [f"ui:{tid}"] if tid else []
        paths = by_short.get(name) or by_short.get(ci.get(name.lower(), ""))
        if paths:
            return [f"op:{p}" for p in paths]
        tid = topic_lookup.get(name.lower())          # bare op-name might be a UI topic
        return [f"ui:{tid}"] if tid else []

    INDICES.mkdir(parents=True, exist_ok=True)
    videos, raw, unknown = [], {}, {}                  # raw[key][video] = [segment dicts]

    for path in sorted(ANALYSES.glob("*.md")):
        meta, mentions = parse_analysis(path)
        vid = meta.get("video") or path.stem
        videos.append({"id": vid, "type": meta.get("type"), "date": meta.get("date"),
                       "title": meta.get("title"), "duration": meta.get("duration"),
                       "url": f"https://www.youtube.com/watch?v={vid}"})
        for mn in mentions:
            for kind, name in mn["marks"]:
                ks = keys_for(kind, name)
                if not ks:
                    label = f"ui:{name}" if kind == "ui" else name
                    unknown[label] = unknown.get(label, 0) + 1
                    continue
                for key in ks:
                    raw.setdefault(key, {}).setdefault(vid, []).append(
                        {"startSecond": mn["startSecond"], "duration": mn["duration"], "depth": mn["depth"],
                         "style": mn["style"], "confidence": mn["confidence"], "note": mn["note"]})

    mentions_idx = {}
    for key, by_vid in raw.items():
        flat = []
        for vid, segs in by_vid.items():
            for s in merge_segments(segs):
                flat.append({"video": vid, "startSecond": s["startSecond"], "duration": s["duration"],
                             "url": f"https://www.youtube.com/watch?v={vid}&t={s['startSecond']}s",
                             "depth": s["depth"], "style": s["style"], "confidence": s["confidence"],
                             "note": s["note"]})
        flat.sort(key=lambda m: (m["video"], m["startSecond"]))
        mentions_idx[key] = flat

    topics_out = {f"ui:{tid}": {**t, "parent": f"ui:{t['parent']}" if t["parent"] else None}
                  for tid, t in topics.items()}

    (INDICES / "videos.json").write_text(
        json.dumps({"videos": videos}, ensure_ascii=False, indent=2), encoding="utf-8")
    (INDICES / "topics.json").write_text(
        json.dumps({"topics": topics_out}, ensure_ascii=False, indent=2), encoding="utf-8")
    (INDICES / "mentions.json").write_text(
        json.dumps(mentions_idx, ensure_ascii=False, indent=2), encoding="utf-8")
    return videos, topics, mentions_idx, unknown


def main():
    if not ANALYSES.is_dir() or not any(ANALYSES.glob("*.md")):
        print(f"No analyses found in {ANALYSES} — run /analyze-videos first.")
        return
    videos, topics, mentions, unknown = build()
    op_keys = [k for k in mentions if k.startswith("op:")]
    ui_keys = [k for k in mentions if k.startswith("ui:")]
    total = sum(len(v) for v in mentions.values())
    print(f"{len(videos)} video(s) · {len(topics)} UI topics defined")
    print(f"{len(op_keys)} operators + {len(ui_keys)} UI topics with mentions · {total} segments")
    if unknown:
        top = sorted(unknown.items(), key=lambda kv: -kv[1])[:12]
        print("Unresolved markers: " + ", ".join(f"{n}×{c}" for n, c in top))
    print("-> references/indices/videos.json + topics.json + mentions.json  (nothing committed)")


if __name__ == "__main__":
    main()
