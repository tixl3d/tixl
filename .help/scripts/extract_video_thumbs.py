#!/usr/bin/env python3
"""
extract_video_thumbs.py — STAGE 4 (optional): per-reference preview thumbnails.

For every reference in the curated index, grabs ONE representative frame from the MIDDLE of that reference's
time range. The help UI loads `<videoId>_<startSecond>.jpg` and shows it in the row tooltip, so a user sees
the actual moment a reference points at rather than the video's generic cover image.

Source videos are found recursively under the library (the same `videos_dir` video_to_srt.py uses), named
`<title>__<id>.<ext>` anywhere beneath it. Frames land in `.help/references/thumbs/` (committed and shipped
alongside the indices), next to the per-video cover thumbnails, where the editor's VideoThumbnails resolver finds them.

Usage:
    python .help/scripts/extract_video_thumbs.py [--videos DIR] [--all] [--width 240] [--webp]
"""

import argparse
import json
import subprocess
from pathlib import Path

HERE = Path(__file__).resolve().parent
HELP = HERE.parent
INDICES = HELP / "references" / "indices"
OUT = HELP / "references" / "thumbs"
CONFIG = HERE / "video_to_srt.local.json"

VIDEO_EXTS = (".mp4", ".webm", ".mkv", ".mov", ".m4v")
SPAN_CAP = 45          # never spread frames across more than this many seconds, so they stay legible
MIN_SPAN = 8           # for a near-instant mention, still spread frames a little


def videos_dir(arg):
    if arg:
        return Path(arg)
    if CONFIG.exists():
        d = json.loads(CONFIG.read_text(encoding="utf-8")).get("videos_dir")
        if d:
            return Path(d)
    raise SystemExit("no --videos given and no videos_dir in video_to_srt.local.json")


META = HELP / ".tmp" / "video-transcripts"


def index_sources(root: Path, needed_ids):
    """Map videoId -> source file. Files are named `<title>__<id>.<ext>`, so resolve via each video's
    .meta.json `source` field, falling back to an id-substring match on the filename."""
    by_name = {}
    for p in root.rglob("*"):
        if p.suffix.lower() in VIDEO_EXTS:
            by_name[p.name] = p

    sources = {}
    for vid in needed_ids:
        meta = META / f"{vid}.meta.json"
        src = json.loads(meta.read_text(encoding="utf-8")).get("source") if meta.exists() else None
        if src and src in by_name:
            sources[vid] = by_name[src]
            continue
        for name, p in by_name.items():
            if vid in name:
                sources[vid] = p
                break
    return sources


def references(curated: bool):
    name = "mentions.json" if curated else "mentions.full.json"
    data = json.loads((INDICES / name).read_text(encoding="utf-8"))
    seen = set()
    for segs in data.values():
        for s in segs:
            key = (s["video"], s["startSecond"])
            if key not in seen:
                seen.add(key)
                yield s["video"], s["startSecond"], s.get("duration") or 0


def build(root: Path, curated: bool, width: int, ext: str):
    OUT.mkdir(parents=True, exist_ok=True)
    refs = list(references(curated))
    sources = index_sources(root, {vid for vid, _, _ in refs})
    made = skipped = missing = 0
    missing_videos = set()
    for vid, start, duration in refs:
        out = OUT / f"{vid}_{start}.{ext}"
        if out.exists():
            skipped += 1
            continue
        src = sources.get(vid)
        if not src:
            missing += 1
            missing_videos.add(vid)
            continue
        span = max(MIN_SPAN, min(duration or MIN_SPAN, SPAN_CAP))
        mid = start + span // 2   # one representative frame from the middle of the reference
        cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
               "-ss", str(mid), "-i", str(src), "-frames:v", "1",
               "-vf", f"scale={width}:-2", "-q:v", "4", str(out)]
        if subprocess.run(cmd).returncode == 0 and out.exists():
            made += 1
        else:
            print(f"  ! ffmpeg failed for {vid} @{start}s")

    print(f"{made} frames written, {skipped} already present, {missing} skipped (no source video)")
    if missing_videos:
        print("  missing source videos: " + ", ".join(sorted(missing_videos)[:20])
              + (" …" if len(missing_videos) > 20 else ""))
    print(f"-> {OUT}  (width={width}, .{ext}; nothing committed)")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--videos", type=str, default=None, help="library root (default: videos_dir from config)")
    ap.add_argument("--all", action="store_true", help="all references (default: only curated mentions.json)")
    ap.add_argument("--width", type=int, default=240)
    ap.add_argument("--webp", action="store_true", help="emit .webp instead of .jpg (smaller, but the editor decodes jpg)")
    args = ap.parse_args()
    build(videos_dir(args.videos), curated=not args.all, width=args.width, ext="webp" if args.webp else "jpg")


if __name__ == "__main__":
    main()
