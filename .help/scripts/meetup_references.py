#!/usr/bin/env python3
"""
meetup_references.py -- Parse TiXL meet-up wiki notes into a cross-reference index.

Reads the GitHub wiki's `meetup.YYYYMMDD.md` pages (tolerant of the several line
formats in use) and emits, into an output folder, four review artifacts --
nothing is written back to the wiki:

  external-references.json  Structured index of meet-up videos and their
                            timecoded segments. Operator names found in segment
                            text are resolved against the generated operator
                            doc index (.help/docs/operators/index.json), and an
                            inverted operator -> segments backlink map is built.

  report.md                 Data-quality + index-gap report: undeclared emojis,
                            ambiguous operator names, displayed-vs-URL timestamp
                            mismatches, skipped stub pages, and pages missing
                            from the wiki Home index.

  Home.proposed.md          The wiki Home page with its "Meet-Up Notes" list
                            rebuilt to list every capture (reviewable diff).

  normalized/meetup.*.md    Each page rewritten to one canonical line format
                            with clickable deep links (reviewable diff).

The index is the spine the wider help cross-reference system reads from; the
YouTube-authoring and transcription scripts feed the same file.

Usage:
    python meetup_references.py [--wiki DIR] [--op-index FILE] [--out DIR]

Defaults resolve relative to this file's location in the repo, and the wiki
defaults to %TEMP%/tixl-wiki (a shallow clone of tixl3d/tixl.wiki).
"""

import argparse
import json
import os
import re
import sys
from pathlib import Path

# --- Emoji legend ----------------------------------------------------------
# Keys are written exactly as they appear in the notes (some carry a U+FE0F
# variation selector). `OFFICIAL` is the printed "Reference:" legend; the rest
# were found in the wild and the report suggests adding them to the legend.
OFFICIAL = {
    "\U0001F4AC": "chat",         # 💬
    "\U0001F5EF️": "chat",   # 🗯️
    "\U0001F4A1": "tip",          # 💡
    "\U0001F4D8": "op-reference", # 📘
    "\U0001F31F": "highlight",    # 🌟
    "\U0001F195": "feature",      # 🆕
    "\U0001F6E0️": "update", # 🛠️
    "\U0001F9E0": "background",   # 🧠
    "\U0001F4CA": "background",   # 📊
    "\U0001F5B1️": "ui",           # 🖱️  Parameter/Performance Window, panels, controls, drag behaviors
}
DISCOVERED = {
    "\U0001F3AF": "planned",      # 🎯
    "✨": "showcase",         # ✨
    "\U0001F4DD": "walkthrough",  # 📝
    "⚠️": "warning",    # ⚠️
    "\U0001F37F": "showcase",     # 🍿
    "❓": "question",         # ❓
}
EMOJI_LEGEND = {**OFFICIAL, **DISCOVERED}
# Longest keys first so a VS16 variant matches before its bare codepoint.
EMOJI_KEYS = sorted(EMOJI_LEGEND, key=len, reverse=True)

TS_CORE = r"\d{1,2}:\d{2}(?::\d{2})?"
ANY_TS_RE = re.compile(TS_CORE)
LINK_RE = re.compile(r"\[([^\]]*)\]\((https?://[^)]+)\)")
BOLD_HEADER_RE = re.compile(r"^\*\*(.+?)\*\*$")
LIST_ITEM_RE = re.compile(r"^([-*])\s+(.*)$")
URL_T_RE = re.compile(r"[?&]t=(\d+)s?")
VIDEO_ID_RE = re.compile(r"(?:youtube\.com/watch\?v=|youtu\.be/|img\.youtube\.com/vi/)([A-Za-z0-9_-]{6,})")
ISSUE_RE = re.compile(r"#(\d{2,})")
OP_TOKEN_RE = re.compile(r"[A-Za-z][A-Za-z0-9]+")
DATE_RE = re.compile(r"meetup\.(\d{8})")
SEP_LEAD_RE = re.compile(r"^\s*[–—-]\s*")
SEP_TRAIL_RE = re.compile(r"\s*[–—-]\s*$")


def is_emoji_char(ch):
    cp = ord(ch)
    return (0x1F300 <= cp <= 0x1FAFF or 0x2600 <= cp <= 0x27BF
            or 0x2B00 <= cp <= 0x2BFF or 0x1F000 <= cp <= 0x1F02F
            or cp in (0x2122, 0x2139))


def consume_emoji_cluster(text):
    """text[0] is an emoji base; consume the whole grapheme cluster (ZWJ
    sequences, skin-tone modifiers, variation selectors). Returns (cluster, rest)."""
    i, n = 1, len(text)
    while i < n:
        cp = ord(text[i])
        if cp in (0xFE0F, 0x200D) or 0x1F3FB <= cp <= 0x1F3FF:
            i += 1
        elif ord(text[i - 1]) == 0x200D and is_emoji_char(text[i]):
            i += 1
        else:
            break
    return text[:i], text[i:]


def split_emojis(text):
    """Strip a run of leading emojis; return (categories, emojis, rest, unknown)."""
    text = text.lstrip()
    cats, emos, unknown = [], [], []
    while text:
        for key in EMOJI_KEYS:
            if text.startswith(key):
                cats.append(EMOJI_LEGEND[key])
                emos.append(key)
                text = text[len(key):].lstrip()
                break
        else:
            if is_emoji_char(text[0]):
                cluster, text = consume_emoji_cluster(text)
                cats.append("unknown")
                emos.append(cluster)
                unknown.append(cluster)
                text = text.lstrip()
                continue
            break
    return cats, emos, text, unknown


def parse_seconds(ts):
    parts = [int(p) for p in ts.split(":")]
    if len(parts) == 2:
        return parts[0] * 60 + parts[1]
    return parts[0] * 3600 + parts[1] * 60 + parts[2]


def fmt_seconds(secs):
    h, rem = divmod(secs, 3600)
    m, s = divmod(rem, 60)
    return f"{h}:{m:02d}:{s:02d}" if h else f"{m}:{s:02d}"


def iso_date(yyyymmdd):
    return f"{yyyymmdd[0:4]}-{yyyymmdd[4:6]}-{yyyymmdd[6:8]}"


def parse_title(heading):
    """Extract (topic title, duration label) from the H1, dropping date prefix."""
    raw = heading.lstrip("#").strip()
    topic = raw
    for sep in (" -- ", " / ", " /", "/ ", "/"):
        if sep in raw:
            topic = raw.split(sep, 1)[1].strip()
            break
    duration = None
    m = re.search(r"\((\d+(?:\.\d+)?\s*h(?:ours?)?)\)\s*$", topic)
    if m:
        duration = m.group(1).replace(" ", "")
        topic = topic[: m.start()].strip()
    if topic.lower().strip("_ ") in ("title missing", ""):
        topic = ""
    return topic, duration


def resolve_ops(text, by_shortname, ambiguous_out):
    """Resolve PascalCase tokens that name operators. Returns list of full paths."""
    found = []
    for tok in OP_TOKEN_RE.findall(text):
        paths = by_shortname.get(tok)
        if not paths:
            continue
        if len(paths) > 1:
            ambiguous_out.add(tok)
        for p in paths:
            if p not in found:
                found.append(p)
    return found


def parse_topic_line(body, page_url):
    """Parse one topic bullet body into a segment. Handles every observed shape:
    `[emoji text - ts](url)`, `[ts emoji text](url)`, `[ts](url) emoji text`,
    and plain `ts emoji text` (no link)."""
    url = None
    m = LINK_RE.search(body)
    if m:
        url = m.group(2)
        body = body[: m.start()] + m.group(1) + body[m.end():]

    tm = ANY_TS_RE.search(body)
    if not tm:
        return None
    tlabel = tm.group(0)
    secs = parse_seconds(tlabel)

    rest = (body[: tm.start()] + " " + body[tm.end():])
    rest = SEP_LEAD_RE.sub("", SEP_TRAIL_RE.sub("", rest)).strip()
    cats, emos, text, _unknown = split_emojis(rest)

    url_secs = None
    if url:
        um = URL_T_RE.search(url)
        if um:
            url_secs = int(um.group(1))

    return {
        "t": secs,
        "tLabel": tlabel,
        "category": cats[0] if cats else "uncategorized",
        "emoji": "".join(emos),
        "_emojis": emos,
        "text": text.strip(),
        "url": f"{page_url}&t={secs}s",
        "_url_seconds": url_secs,
        "issues": [],
        "notes": [],
        "section": None,
    }


def parse_page(path):
    text = path.read_text(encoding="utf-8", errors="replace")
    lines = text.replace("\r\n", "\n").split("\n")
    yyyymmdd = DATE_RE.search(path.name).group(1)
    page = {"file": path.name, "yyyymmdd": yyyymmdd, "date": iso_date(yyyymmdd),
            "title": "", "duration": None, "video_id": None, "intro": "",
            "topics": [], "segments": []}

    heading = next((ln for ln in lines if ln.startswith("# ")), "")
    page["title"], page["duration"] = parse_title(heading)

    mid = VIDEO_ID_RE.search(text)
    page["video_id"] = mid.group(1) if mid else None

    for ln in lines[1:]:
        s = ln.strip()
        if not s or s.startswith(("#", "[", "!", "Reference:")):
            continue
        page["intro"] = s
        break

    # Any list item with a timestamp is a topic; bold-only lines set the current
    # section; list items without a timestamp are notes on the previous topic.
    section = None
    for ln in lines:
        s = ln.strip()
        if not s:
            continue
        bh = BOLD_HEADER_RE.match(s)
        if bh:
            section = bh.group(1).strip().rstrip(":").strip()
            continue
        li = LIST_ITEM_RE.match(s)
        if not li:
            continue
        item = li.group(2).strip()
        if not item:
            continue
        if ANY_TS_RE.search(item):
            page["topics"].append({"body": item, "section": section, "notes": []})
        elif page["topics"]:
            page["topics"][-1]["notes"].append(item)
    return page


def is_stub(page):
    return not page["video_id"] or not page["topics"]


def build(wiki_dir, op_index_path, out_dir):
    op_index = json.loads(Path(op_index_path).read_text(encoding="utf-8"))
    by_shortname = op_index.get("by_shortname", {})

    meetup_files = sorted(wiki_dir.glob("meetup.*.md"), key=lambda p: p.name)
    all_dates = [DATE_RE.search(p.name).group(1) for p in meetup_files]

    pages, stubs = [], []
    ambiguous_ops, undeclared_emojis, ts_mismatches = set(), {}, []

    for path in meetup_files:
        page = parse_page(path)
        if is_stub(page):
            stubs.append(page)
            continue
        page_url = f"https://www.youtube.com/watch?v={page['video_id']}"
        for topic in page["topics"]:
            seg = parse_topic_line(topic["body"], page_url)
            if seg is None:
                continue
            seg["notes"] = topic["notes"]
            seg["section"] = topic["section"]
            text_for_ops = seg["text"] + " " + " ".join(seg["notes"])
            seg["ops"] = resolve_ops(text_for_ops, by_shortname, ambiguous_ops)
            seg["issues"] = sorted({int(x) for x in ISSUE_RE.findall(
                topic["body"] + " " + " ".join(seg["notes"]))})
            for e in seg.pop("_emojis", []):
                if e not in OFFICIAL:
                    undeclared_emojis[e] = undeclared_emojis.get(e, 0) + 1
            us = seg.pop("_url_seconds", None)
            if us is not None and us != seg["t"]:
                ts_mismatches.append((page["file"], seg["tLabel"], fmt_seconds(us), seg["text"]))
            page["segments"].append(seg)
        # per-segment duration = gap to the next chapter — a proxy for how deeply a topic was covered
        segs = page["segments"]
        for i in range(len(segs)):
            segs[i]["durationSec"] = (segs[i + 1]["t"] - segs[i]["t"]) if i + 1 < len(segs) else None
        pages.append(page)

    backlinks = {}
    for page in pages:
        vid = page["video_id"]
        for seg in page["segments"]:
            dur = seg.get("durationSec")
            ref = {"source": {"type": "youtube", "id": vid},
                   "url": seg["url"], "date": page["date"], "tLabel": seg["tLabel"],
                   "category": seg["category"],
                   "durationMin": round(dur / 60) if dur else None,
                   "title": seg["text"],
                   "summary": None}  # filled later by the LLM enrichment skill
            for op in seg["ops"]:
                backlinks.setdefault(f"op:{op}", []).append(ref)

    def seg_json(seg):
        d = {k: seg[k] for k in ("t", "tLabel", "category", "emoji", "text",
                                 "url", "ops", "issues", "notes")}
        if seg.get("durationSec") is not None:
            d["durationSec"] = seg["durationSec"]
        if seg["section"]:
            d["section"] = seg["section"]
        return d

    videos = [{
        "id": p["video_id"], "type": "meetup",
        "source": {"type": "youtube", "id": p["video_id"]},
        "date": p["date"], "title": p["title"],
        "url": f"https://www.youtube.com/watch?v={p['video_id']}",
        "thumbnail": f"https://img.youtube.com/vi/{p['video_id']}/maxresdefault.jpg",
        "durationLabel": p["duration"], "sourcePage": p["file"][:-3],
        "segments": [seg_json(s) for s in p["segments"]],
    } for p in pages]

    index = {"schemaVersion": 1, "emojiLegend": dict(EMOJI_LEGEND),
             "videos": videos, "mentions": backlinks}

    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "external-references.json").write_text(
        json.dumps(index, ensure_ascii=False, indent=2), encoding="utf-8")

    write_report(out_dir, pages, stubs, all_dates, wiki_dir,
                 ambiguous_ops, undeclared_emojis, ts_mismatches, backlinks)
    write_home_proposal(out_dir, wiki_dir, pages)
    write_normalized(out_dir, pages, all_dates)
    return pages, stubs, backlinks, ts_mismatches, undeclared_emojis, ambiguous_ops


def parse_home_meetups(wiki_dir):
    home = wiki_dir / "Home.md"
    if not home.exists():
        return set()
    return set(re.findall(r"\(meetup\.(\d{8})\)", home.read_text(encoding="utf-8")))


def write_report(out_dir, pages, stubs, all_dates, wiki_dir,
                 ambiguous_ops, undeclared_emojis, ts_mismatches, backlinks):
    listed = parse_home_meetups(wiki_dir)
    non_stub = {p["yyyymmdd"] for p in pages}
    missing = sorted(non_stub - listed)
    seg_count = sum(len(p["segments"]) for p in pages)
    op_links = sum(len(s["ops"]) for p in pages for s in p["segments"])

    L = ["# Meet-up reference parse report\n"]
    L.append(f"- Pages parsed: **{len(pages)}**  ·  stub/empty skipped: **{len(stubs)}**  "
             f"·  total meetup pages: **{len(all_dates)}**")
    L.append(f"- Segments: **{seg_count}**  ·  operator links resolved: **{op_links}**  "
             f"·  operators with backlinks: **{len(backlinks)}**\n")

    L.append("## Pages missing from the wiki Home index")
    if missing:
        L.append(f"{len(missing)} capture(s) exist but are not linked from `Home.md` "
                 "(see `Home.proposed.md` for the rebuilt list):\n")
        L += [f"- {iso_date(d)} (`meetup.{d}`)" for d in missing]
    else:
        L.append("None — every parsed capture is already linked.")
    L.append("")

    L.append("## Stub / empty pages skipped")
    L += [f"- `{p['file']}` ({p['date']}) — "
          f"{'no video id' if not p['video_id'] else 'no topic lines'}" for p in stubs]
    L.append("")

    L.append("## Displayed-vs-URL timestamp mismatches")
    if ts_mismatches:
        L.append("Link text and the `&t=` in the URL disagree. The index used the "
                 "**displayed** time; verify which is correct:\n")
        L += [f"- `{f}`: shows **{shown}** but URL points to **{fromurl}** — {txt}"
              for f, shown, fromurl, txt in ts_mismatches]
    else:
        L.append("None.")
    L.append("")

    L.append("## Undeclared emojis (used but not in the printed legend)")
    if undeclared_emojis:
        L.append("Consider adding these to the `Reference:` line (mapped ones are already "
                 "handled by the parser — see `emojiLegend` in the JSON):\n")
        for e, n in sorted(undeclared_emojis.items(), key=lambda kv: -kv[1]):
            mapped = EMOJI_LEGEND.get(e)
            L.append(f"- {e}  ×{n} → " + (f"`{mapped}`" if mapped else "**unmapped**"))
    else:
        L.append("None.")
    L.append("")

    L.append("## Ambiguous operator names")
    if ambiguous_ops:
        L.append("These short names resolve to more than one operator; all matches were "
                 "linked:\n")
        L += [f"- `{tok}`" for tok in sorted(ambiguous_ops)]
    else:
        L.append("None.")
    L.append("")
    (out_dir / "report.md").write_text("\n".join(L) + "\n", encoding="utf-8")


def write_home_proposal(out_dir, wiki_dir, pages):
    home = wiki_dir / "Home.md"
    if not home.exists():
        return
    lines = home.read_text(encoding="utf-8").replace("\r\n", "\n").split("\n")
    start = next((i for i, ln in enumerate(lines)
                  if ln.strip().lower().startswith("### meet-up notes")), None)
    if start is None:
        return
    end = start + 1
    while end < len(lines) and not (lines[end].startswith("#") or lines[end].startswith("----")):
        end += 1
    bullets = [f"- [{p['date']} {p['title']}](meetup.{p['yyyymmdd']})"
               for p in sorted(pages, key=lambda p: p["yyyymmdd"], reverse=True)]
    new = lines[:start + 1] + [""] + bullets + [""] + lines[end:]
    (out_dir / "Home.proposed.md").write_text("\n".join(new), encoding="utf-8")


CANON_LEGEND = ("Reference: 💬🗯️Chat   💡Tip  📘Op reference   🌟Highlight   "
                "🆕Feature   🛠️Updates   🧠📊Background   🎯Planned   🍿Showcase   🖱️UI")


def write_normalized(out_dir, pages, all_dates):
    norm_dir = out_dir / "normalized"
    norm_dir.mkdir(parents=True, exist_ok=True)
    ordered = sorted(all_dates)
    for p in pages:
        i = ordered.index(p["yyyymmdd"])
        nav = []
        if i > 0:
            nav.append(f"[← Prev](meetup.{ordered[i - 1]})")
        if i + 1 < len(ordered):
            nav.append(f"[Next →](meetup.{ordered[i + 1]})")

        title = p["title"] + (f" ({p['duration']})" if p["duration"] else "")
        vid = p["video_id"]
        out = [f"# TiXL Meetup {p['date']} / {title}", " | ".join(nav), ""]
        if p["intro"]:
            out += [p["intro"], ""]
        out += [f"[![Introduction](https://img.youtube.com/vi/{vid}/maxresdefault.jpg)]"
                f"(https://www.youtube.com/watch?v={vid})", "", CANON_LEGEND, "",
                "## Topics / Time-codes", ""]
        section = None
        for seg in p["segments"]:
            if seg["section"] != section:
                section = seg["section"]
                if section:
                    out += ["", f"**{section}:**", ""]
            emoji = seg["emoji"] + " " if seg["emoji"] else ""
            out.append(f"- [{emoji}{seg['text']} – {seg['tLabel']}]({seg['url']})")
            out += [f"  * {note}" for note in seg["notes"]]
        out.append("")
        (norm_dir / p["file"]).write_text("\n".join(out), encoding="utf-8")


def main():
    here = Path(__file__).resolve()
    repo_root = here.parents[2]
    ap = argparse.ArgumentParser(description="Parse TiXL meet-up wiki notes into a reference index.")
    ap.add_argument("--wiki", type=Path,
                    default=Path(os.environ.get("TEMP", "/tmp")) / "tixl-wiki",
                    help="Path to a checkout of the tixl3d/tixl.wiki repo.")
    ap.add_argument("--op-index", type=Path,
                    default=repo_root / ".help" / "docs" / "operators" / "index.json",
                    help="Path to the generated operator doc index.json.")
    ap.add_argument("--out", type=Path, default=here.parent / "_meetup_out",
                    help="Output folder for the generated review artifacts.")
    args = ap.parse_args()

    if not args.wiki.exists():
        sys.exit(f"Wiki folder not found: {args.wiki}\n"
                 "Clone it first:  git clone --depth 1 https://github.com/tixl3d/tixl.wiki.git")
    if not args.op_index.exists():
        sys.exit(f"Operator index not found: {args.op_index}")

    pages, stubs, backlinks, mism, undecl, ambig = build(args.wiki, args.op_index, args.out)
    print(f"Parsed {len(pages)} pages ({len(stubs)} stubs skipped).")
    print(f"Segments: {sum(len(p['segments']) for p in pages)} | "
          f"operator backlinks: {len(backlinks)} | timestamp mismatches: {len(mism)} | "
          f"undeclared emojis: {len(undecl)} | ambiguous ops: {len(ambig)}")
    print(f"Output written to: {args.out}")


if __name__ == "__main__":
    main()
