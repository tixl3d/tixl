#!/usr/bin/env python3
"""
analysis_to_index.py — STAGE 3 of the video -> docs pipeline.

Reads the committed per-video analyses (written by the /analyze-videos skill) plus the hand-authored
UI-topic registry, and builds the reference indices the editor reads:

    references/video-analysis/<id>.md  +  references/topics/ui-topics.md
        -> references/indices/videos.json + mentions.json + topics.json

videos.json   = { "videos": [ { id, type, date, title, url, duration } ] }
topics.json   = { "topics": { "ui:<id>": { term, parent, synonyms, classes, docFile } } }
                  (docFile points at references/topics/ui/<id>.md — the editor loads it lazily)
mentions.json = { "op:<fullpath>" | "ui:<id>": [ { video, startSecond, duration, url, depth, style, purpose, confidence, note } ] }

Each mention line is `<start>[→<end>] [Op]/[ui:Id] · <depth> · <style> · <purpose> · <conf>% — <note>`. The start/end
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
TOPIC_DOCS_DIR = HELP / "references" / "topics" / "ui"
INDICES = HELP / "references" / "indices"
OP_INDEX = HELP / "docs" / "operators" / "index.json"

TS = r"\d{1,2}:\d{2}(?::\d{2})?(?:,\d+)?"   # M:SS / H:MM:SS, tolerating a ,ms suffix
RANGE_RE = re.compile(rf"^({TS})\s*(?:[→-]\s*({TS}))?")   # start, optional →end (arrow or hyphen)
MARK_RE = re.compile(r"\[((?:ui:)+)?([A-Za-z][A-Za-z0-9]*)\]")  # [DrawPoints] / [ui:Timeline] (tolerates [ui:ui:X])
DEPTHS = ("in-depth", "explained", "passing")
DEPTH_RANK = {"in-depth": 0, "explained": 1, "passing": 2}
STYLES = ("scripted", "answer", "discussion", "experiment")   # how structured/trustworthy the moment is
PURPOSES = ("Example", "Comparison", "Parameters", "Gotcha", "Concept", "Performance", "Tip")  # what the clip gives
CONF_RE = re.compile(r"(\d{1,3})%")                 # per-segment confidence, e.g. 85%
NOTE_RE = re.compile(r"[—–]\s*(.+)$")               # em/en dash only — NOT the range hyphen

# --- reference curation -----------------------------------------------------------------------------
# The raw analyses are exhaustive (every passing mention), which buries the few moments actually worth
# linking. We score each (operator/topic × merged-segment) reference and keep only the strongest, so the
# help UI shows a focused set. score = durationFactor × depthWeight × styleWeight.
MAX_REFERENCES = 500                         # global cap; None disables curation (keep everything)
DURATION_LO, DURATION_HI = 5, 30             # seconds → duration factor ramps linearly 0..1 across this
DEPTH_WEIGHTS = {"in-depth": 2.0, "explained": 1.5, "passing": 0.5}   # how much the moment teaches
STYLE_WEIGHTS = {"scripted": 2.0, "answer": 2.0, "discussion": 0.75, "experiment": 0.4}  # how trustworthy
# what the clip offers — gently favors the kinds someone stuck on an operator reaches for first
PURPOSE_WEIGHTS = {"Example": 1.2, "Comparison": 1.2, "Parameters": 1.1, "Gotcha": 1.1,
                   "Concept": 1.0, "Performance": 1.0, "Tip": 0.9}
DEPTH_DEFAULT = 0.5                           # unlabelled depth
STYLE_DEFAULT = 0.5                           # unlabelled style (the "null" case)
PURPOSE_DEFAULT = 1.0                         # unlabelled purpose — neutral
# A video may declare `focusesOn: [Op], [ui:Topic]` in its frontmatter — it's THE dedicated tutorial
# for those ops. For a focus key, that video's many moments collapse to ONE reference, boosted so it
# ranks as the canonical tutorial; and the video's *incidental* mentions of other ops are dropped.
FOCUS_BOOST = 5.0


def score_segment(seg):
    dur = seg.get("duration") or 0
    df = (dur - DURATION_LO) / (DURATION_HI - DURATION_LO)
    df = 0.0 if df < 0 else 1.0 if df > 1 else df
    return df * DEPTH_WEIGHTS.get(seg.get("depth"), DEPTH_DEFAULT) \
              * STYLE_WEIGHTS.get(seg.get("style"), STYLE_DEFAULT) \
              * PURPOSE_WEIGHTS.get(seg.get("purpose"), PURPOSE_DEFAULT)


def parse_seconds(ts):
    p = [int(x) for x in ts.split(",")[0].split(":")]
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

    summary = re.split(r"(?m)^##\s", body, maxsplit=1)[0].strip()   # the prose before the first heading
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
        meta_tokens = MARK_RE.sub("", head)                 # drop [markers] so op names can't match a purpose
        purpose = next((p for p in PURPOSES
                        if re.search(rf"(?<![A-Za-z]){p}(?![A-Za-z])", meta_tokens, re.I)), None)
        cm = CONF_RE.search(head)
        confidence = int(cm.group(1)) if cm else None
        mentions.append({"startSecond": start, "duration": duration, "marks": marks, "depth": depth,
                         "style": style, "purpose": purpose, "confidence": confidence, "note": note})
    return meta, summary, mentions


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
        tid = meta.get("id") or pascal(term)
        split = lambda key: [x.strip() for x in meta.get(key, "").split(",") if x.strip()]
        # Doc body lives in its own file; record a pointer the editor loads lazily.
        doc_file = f"references/topics/ui/{tid}.md" if (TOPIC_DOCS_DIR / f"{tid}.md").exists() else None
        topics[tid] = {"term": term, "parent": meta.get("parent") or None,
                       "synonyms": split("synonyms"), "classes": split("classes"), "docFile": doc_file}
    return topics


def merge_segments(segs):
    """Merge overlapping segments (same key+video); the deeper segment's note wins."""
    out = []
    for s in sorted(segs, key=lambda x: x["startSecond"]):
        if out and s["startSecond"] < out[-1]["startSecond"] + out[-1]["duration"]:   # true overlap only
            prev = out[-1]
            if DEPTH_RANK.get(s["depth"], 3) < DEPTH_RANK.get(prev["depth"], 3):
                prev["depth"], prev["style"], prev["purpose"], prev["confidence"], prev["note"] = \
                    s["depth"], s["style"], s["purpose"], s["confidence"], s["note"]
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

    def focus_keys(raw_value):                         # "[Rings], [ui:Timeline]" -> resolved index keys
        out = []
        for tok in (raw_value or "").replace("[", " ").replace("]", " ").split(","):
            tok = tok.strip()
            if not tok:
                continue
            kind, nm = ("ui", tok[3:]) if tok.lower().startswith("ui:") else ("op", tok)
            ks = keys_for(kind, nm)
            if ks:
                out.extend(ks)
            else:
                unknown[f"focusesOn:{tok}"] = unknown.get(f"focusesOn:{tok}", 0) + 1
        return out

    INDICES.mkdir(parents=True, exist_ok=True)
    videos, raw, unknown = [], {}, {}                  # raw[key][video] = [segment dicts]
    video_focus = {}                                   # vid -> set(focus keys); present only if declared
    video_summary, video_fulldur = {}, {}              # vid -> analysis summary / full length in seconds

    for path in sorted(ANALYSES.glob("*.md")):
        meta, summary, mentions = parse_analysis(path)
        vid = meta.get("video") or path.stem
        focuses = focus_keys(meta.get("focusesOn"))
        if focuses:
            video_focus[vid] = set(focuses)
        dur_label = meta.get("duration") or ""
        video_summary[vid] = summary
        video_fulldur[vid] = parse_seconds(dur_label) if ":" in dur_label else 0
        videos.append({"id": vid, "type": meta.get("type"), "date": meta.get("date"),
                       "title": meta.get("title"), "duration": dur_label, "summary": summary,
                       "focusesOn": focuses, "url": f"https://www.youtube.com/watch?v={vid}"})
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
                         "style": mn["style"], "purpose": mn["purpose"],
                         "confidence": mn["confidence"], "note": mn["note"]})

    full_idx, all_refs = {}, []                        # all_refs: (score, key, segment) for global ranking
    for key, by_vid in raw.items():
        flat = []
        for vid, segs in by_vid.items():
            focus = video_focus.get(vid)               # set of keys this video is the dedicated tutorial for
            if focus is not None and key not in focus:
                continue                               # a focused tutorial doesn't clutter ops it only name-drops
            segs_out = []
            for s in merge_segments(segs):
                segs_out.append({"video": vid, "startSecond": s["startSecond"], "duration": s["duration"],
                                 "url": f"https://www.youtube.com/watch?v={vid}&t={s['startSecond']}s",
                                 "depth": s["depth"], "style": s["style"], "purpose": s["purpose"],
                                 "confidence": s["confidence"], "note": s["note"],
                                 "score": round(score_segment(s), 4)})
            if focus is not None:                       # collapse this op's moments to one boosted "the tutorial" ref
                best = max(segs_out, key=lambda x: x["score"])
                best["startSecond"] = min(s["startSecond"] for s in segs_out)   # link to where coverage begins
                best["url"] = f"https://www.youtube.com/watch?v={vid}&t={best['startSecond']}s"
                best["momentCount"] = len(segs_out)
                best["focus"] = True
                # It's the whole-video tutorial: show the analysis summary, spanning the full length.
                best["note"] = video_summary.get(vid) or best["note"]
                best["duration"] = video_fulldur.get(vid) or best["duration"]
                best["score"] = round(best["score"] * FOCUS_BOOST, 4)
                segs_out = [best]
            for seg in segs_out:
                flat.append(seg)
                all_refs.append((seg["score"], key, seg))
        flat.sort(key=lambda m: (m["video"], m["startSecond"]))
        full_idx[key] = flat

    # Curated index: globally keep only the highest-scoring references (drop now-empty keys).
    all_refs.sort(key=lambda r: r[0], reverse=True)
    kept = all_refs if MAX_REFERENCES is None else all_refs[:MAX_REFERENCES]
    cutoff = kept[-1][0] if kept else 0.0
    mentions_idx = {}
    for _, key, seg in kept:
        mentions_idx.setdefault(key, []).append(seg)
    for key in mentions_idx:
        mentions_idx[key].sort(key=lambda m: -m["score"])   # best moment first within an operator

    topics_out = {f"ui:{tid}": {**t, "parent": f"ui:{t['parent']}" if t["parent"] else None}
                  for tid, t in topics.items()}

    (INDICES / "videos.json").write_text(
        json.dumps({"videos": videos}, ensure_ascii=False, indent=2), encoding="utf-8")
    (INDICES / "topics.json").write_text(
        json.dumps({"topics": topics_out}, ensure_ascii=False, indent=2), encoding="utf-8")
    (INDICES / "mentions.json").write_text(
        json.dumps(mentions_idx, ensure_ascii=False, indent=2), encoding="utf-8")
    (INDICES / "mentions.full.json").write_text(   # archive: every scored reference, before the cap
        json.dumps(full_idx, ensure_ascii=False, indent=2), encoding="utf-8")
    return videos, topics, mentions_idx, full_idx, cutoff, unknown


def main():
    if not ANALYSES.is_dir() or not any(ANALYSES.glob("*.md")):
        print(f"No analyses found in {ANALYSES} — run /analyze-videos first.")
        return
    videos, topics, mentions, full_idx, cutoff, unknown = build()
    full_total = sum(len(v) for v in full_idx.values())
    kept_total = sum(len(v) for v in mentions.values())
    op_keys = [k for k in mentions if k.startswith("op:")]
    ui_keys = [k for k in mentions if k.startswith("ui:")]
    print(f"{len(videos)} video(s) · {len(topics)} UI topics defined")
    print(f"{len(full_idx)} keys / {full_total} references scored -> "
          f"kept top {kept_total} (cutoff score {cutoff:.3f})")
    print(f"curated index covers {len(op_keys)} operators + {len(ui_keys)} UI topics")
    by_depth, by_style, by_purpose = {}, {}, {}
    for segs in mentions.values():
        for s in segs:
            by_depth[s["depth"]] = by_depth.get(s["depth"], 0) + 1
            by_style[s["style"]] = by_style.get(s["style"], 0) + 1
            by_purpose[s.get("purpose")] = by_purpose.get(s.get("purpose"), 0) + 1
    fmt = lambda d: ", ".join(f"{k}:{v}" for k, v in sorted(d.items(), key=lambda kv: -kv[1]))
    print(f"  depth: {fmt(by_depth)}")
    print(f"  style: {fmt(by_style)}")
    print(f"  purpose: {fmt(by_purpose)}")
    if unknown:
        top = sorted(unknown.items(), key=lambda kv: -kv[1])[:12]
        print("Unresolved markers: " + ", ".join(f"{n}×{c}" for n, c in top))
    print("-> indices/videos.json + topics.json + mentions.json (curated) + mentions.full.json"
          "  (nothing committed)")


if __name__ == "__main__":
    main()
