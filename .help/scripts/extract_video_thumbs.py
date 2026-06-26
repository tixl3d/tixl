#!/usr/bin/env python3
"""
extract_video_thumbs.py — STAGE 4 (optional): per-reference hover-preview filmstrips.

For every reference in the curated mentions index, samples a handful of frames spanning that
reference's time range from the source video and tiles them into ONE horizontal sprite image. The
help UI loads `<videoId>_<startSecond>.<ext>` and cycles through the frames on hover, so a user can
glance at what a clip actually shows before clicking.

Input  : source videos named `<videoId>.<ext>` in --videos (the originals are NOT in the repo).
Output : .help/references/thumbs/<videoId>_<startSecond>.<ext>  (one wide sprite per reference)

Naming is the contract the editor relies on — do not change it without updating VideoThumbnails.

Usage:
    python .help/scripts/extract_video_thumbs.py --videos D:/tixl-videos [--all] [--frames 6] [--width 240] [--jpg]

  --all     thumbnail every reference in mentions.full.json (default: only the curated mentions.json)
  --jpg     emit .jpg sprites (engine already loads jpg; webp is ~30% smaller but needs a webp loader)
"""

import argparse
import json
import subprocess
from pathlib import Path

HERE = Path(__file__).resolve().parent
HELP = HERE.parent
INDICES = HELP / "references" / "indices"
OUT = HELP / "references" / "thumbs"

VIDEO_EXTS = (".mp4", ".webm", ".mkv", ".mov", ".m4v")
SPAN_CAP = 45          # seconds — never spread the frames across more than this, so they stay legible
MIN_SPAN = 6           # seconds — for a near-instant mention, still spread frames a little


def find_source(videos_dir: Path, vid: str):
    for ext in VIDEO_EXTS:
        p = videos_dir / f"{vid}{ext}"
        if p.exists():
            return p
    return None


def references(curated: bool):
    name = "mentions.json" if curated else "mentions.full.json"
    data = json.loads((INDICES / name).read_text(encoding="utf-8"))
    seen = set()
    for segs in data.values():
        for s in segs:                       # one sprite per (video, startSecond) — refs often share a moment
            key = (s["video"], s["startSecond"])
            if key not in seen:
                seen.add(key)
                yield s["video"], s["startSecond"], s.get("duration") or 0


def build(videos_dir: Path, curated: bool, frames: int, width: int, ext: str):
    OUT.mkdir(parents=True, exist_ok=True)
    made = skipped = missing = 0
    missing_videos = set()
    for vid, start, duration in references(curated):
        out = OUT / f"{vid}_{start}.{ext}"
        if out.exists():
            skipped += 1
            continue
        src = find_source(videos_dir, vid)
        if not src:
            missing += 1
            missing_videos.add(vid)
            continue
        span = max(MIN_SPAN, min(duration or MIN_SPAN, SPAN_CAP))
        # fps = frames/span gives ~`frames` evenly spaced grabs across the reference; tile into one row.
        vf = f"fps={frames}/{span},scale={width}:-2,tile={frames}x1"
        cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
               "-ss", str(start), "-i", str(src), "-t", str(span),
               "-vf", vf, "-frames:v", "1", str(out)]
        if subprocess.run(cmd).returncode == 0 and out.exists():
            made += 1
        else:
            print(f"  ! ffmpeg failed for {vid} @{start}s")

    print(f"{made} sprites written, {skipped} already present, {missing} skipped (no source video)")
    if missing_videos:
        print("  missing source videos: " + ", ".join(sorted(missing_videos)[:20])
              + (" …" if len(missing_videos) > 20 else ""))
    print(f"-> {OUT}  (frames={frames}, width={width}, .{ext}; nothing committed)")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--videos", required=True, type=Path, help="folder of <videoId>.<ext> source videos")
    ap.add_argument("--all", action="store_true", help="all references (default: only curated mentions.json)")
    ap.add_argument("--frames", type=int, default=6)
    ap.add_argument("--width", type=int, default=240)
    ap.add_argument("--jpg", action="store_true", help="emit .jpg instead of .webp")
    args = ap.parse_args()
    build(args.videos, curated=not args.all, frames=args.frames, width=args.width,
          ext="jpg" if args.jpg else "webp")


if __name__ == "__main__":
    main()
