#!/usr/bin/env python3
"""
enrich_meta.py — backfill YouTube metadata into the transcript meta sidecars.

For every video in the library (discovered exactly like video_to_srt.py), fetch its YouTube
upload date, title, channel and thumbnail via yt-dlp and merge them into
.help/.tmp/video-transcripts/<id>.meta.json — no API key, decoupled from transcription:

    <id>.meta.json  +=  { date: <upload YYYY-MM-DD>, title, channel, duration, thumbnail }

Tutorials/updates rarely carry a date in their filename; this is where their `date` comes from
(the upload date also wins over the filename date for meetups, which is the recency signal we want).
Thumbnails are saved to .help/references/thumbs/<id>.jpg for the future editor playlist.

Runs in any order vs. transcription: it creates the sidecar if missing and merges if present, and
video_to_srt.py's write_meta preserves these fields. Idempotent — an id already carrying a `title`
is skipped unless --refresh. Needs yt-dlp (`pip install yt-dlp`).

    python enrich_meta.py [--refresh] [--only TEXT] [--no-thumbs]
"""

import argparse
import json
import subprocess
import sys
import urllib.request
from pathlib import Path

import video_to_srt as v   # reuse discover() + load_config() + paths

PYDL = [sys.executable, "-m", "yt_dlp"]
THUMBS = v.HELP / "references" / "thumbs"


def yt_meta(vid):
    url = f"https://www.youtube.com/watch?v={vid}"
    out = subprocess.run(PYDL + ["--skip-download", "--no-warnings", "--dump-single-json", "--", url],
                         capture_output=True, text=True, encoding="utf-8")
    if out.returncode != 0 or not out.stdout.strip():
        return None
    try:
        d = json.loads(out.stdout)
    except ValueError:
        return None
    ud = d.get("upload_date")  # YYYYMMDD
    date = f"{ud[0:4]}-{ud[4:6]}-{ud[6:8]}" if ud and len(ud) == 8 else None
    return {"date": date, "title": d.get("title"),
            "channel": d.get("channel") or d.get("uploader"),
            "duration": d.get("duration"), "thumbnail": d.get("thumbnail")}


def save_thumb(vid, url):
    if not url:
        return
    THUMBS.mkdir(parents=True, exist_ok=True)
    dest = THUMBS / f"{vid}.jpg"
    if dest.exists():
        return
    try:
        with urllib.request.urlopen(url, timeout=30) as r:
            dest.write_bytes(r.read())
    except Exception as e:  # network is best-effort; the URL is still saved in the sidecar
        print(f"    thumb {vid}: {e}")


def main():
    ap = argparse.ArgumentParser(description="Backfill YouTube date/title/thumbnail into meta sidecars.")
    ap.add_argument("--refresh", action="store_true", help="re-fetch even if already enriched")
    ap.add_argument("--only", metavar="TEXT", help="only ids/filenames containing TEXT")
    ap.add_argument("--no-thumbs", action="store_true", help="skip downloading thumbnail images")
    args = ap.parse_args()

    if subprocess.run(PYDL + ["--version"], capture_output=True).returncode != 0:
        sys.exit("yt-dlp not importable — `pip install yt-dlp` for this Python first.")
    cfg = v.load_config()
    found, _, _ = v.discover(cfg["videos_dir"])
    v.TRANSCRIPTS.mkdir(parents=True, exist_ok=True)

    items = sorted(found.items())
    if args.only:
        items = [(i, info) for i, info in items if args.only in i or args.only in info["path"].name]

    done = 0
    for vid, info in items:
        mpath = v.TRANSCRIPTS / f"{vid}.meta.json"
        meta = {}
        if mpath.exists():
            try:
                meta = json.loads(mpath.read_text(encoding="utf-8"))
            except ValueError:
                meta = {}
        if meta.get("title") and not args.refresh:
            continue
        ym = yt_meta(vid)
        if not ym:
            print(f"  ! {vid}: yt-dlp fetch failed")
            continue
        meta.setdefault("id", vid)            # base fields, so enrich works before stage-1 too
        meta.setdefault("type", info["type"])
        meta.setdefault("source", info["path"].name)
        meta["date"] = ym["date"] or meta.get("date") or info["date"]
        for k in ("title", "channel", "duration", "thumbnail"):
            if ym.get(k) is not None:
                meta[k] = ym[k]
        mpath.write_text(json.dumps(meta, indent=2, ensure_ascii=False), encoding="utf-8")
        if not args.no_thumbs:
            save_thumb(vid, ym.get("thumbnail"))
        done += 1
        print(f"  {vid}  {meta['date']}  {info['type']:8} {(meta.get('title') or '')[:58]}")
    print(f"\nEnriched {done} sidecar(s) -> {v.TRANSCRIPTS}")
    if not args.no_thumbs and done:
        print(f"Thumbnails -> {THUMBS}")


if __name__ == "__main__":
    main()
