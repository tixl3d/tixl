#!/usr/bin/env python3
"""
video_to_srt.py — STAGE 1 of the video -> docs pipeline.

Scans a video library and transcribes each clip with whisper.cpp into one timestamped .srt
per video, keyed by its YouTube id:

    <videos_dir>/<type>/<name>__<id>.mp4  ->  .help/.tmp/video-transcripts/<id>.srt
                                          (+ <id>.meta.json: id, type, date, source)

Conventions:
  - The YouTube id is appended after a double underscore: `…__<id>.mp4` (a space before the
    extension is tolerated). Files with no `__<id>` are skipped with a warning — the id is the
    key for everything downstream, so a video without one can't be processed.
  - The immediate sub-folder is the `type` (meetups/ tutorials/ releases/ -> meetup, tutorial,
    release; a file directly in the root -> "video").
  - Duplicate ids (same video in two files) are transcribed once.
  - Resumable: a video whose <id>.srt already exists is skipped; whisper runs in 30-min chunks,
    each on a watchdog, so a killed run loses nothing.

Run it (e.g. overnight):  python video_to_srt.py   [--dry-run] [--only TEXT]
Stages after this: /analyze-videos (skill) -> analysis_to_index.py.
Machine paths come from video_to_srt.local.json (git-ignored).
"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
HELP = HERE.parent
TRANSCRIPTS = HELP / ".tmp" / "video-transcripts"
CONFIG = HERE / "video_to_srt.local.json"

CONFIG_TEMPLATE = {
    "videos_dir": "C:/path/to/Videos/_tixl",
    "ffmpeg": "ffmpeg", "ffprobe": "ffprobe",
    "whisper_cli": "C:/path/to/whisper-cli.exe",
    "whisper_model": "C:/path/to/ggml-base.en.bin",
    "threads": 20, "chunk_seconds": 1800, "chunk_timeout_seconds": 900,
}

ID_RE = re.compile(r"__([A-Za-z0-9_-]{11})\s*\.[^.]+$")
DATE_RE = re.compile(r"(\d{4}-\d{2}-\d{2})")
SRT_TIME_RE = re.compile(r"(\d\d:\d\d:\d\d,\d+)\s*-->\s*(\d\d:\d\d:\d\d,\d+)")


def load_config():
    if not CONFIG.exists():
        CONFIG.write_text(json.dumps(CONFIG_TEMPLATE, indent=2), encoding="utf-8")
        sys.exit(f"Wrote a config template to {CONFIG}\nEdit the paths, then re-run.")
    cfg = dict(CONFIG_TEMPLATE)
    cfg.update(json.loads(CONFIG.read_text(encoding="utf-8")))
    return cfg


# ---------- discovery ----------

def discover(videos_dir):
    """Return ({id: {type,date,path}}, duplicates, skipped) for *.mp4 under videos_dir."""
    root = Path(videos_dir)
    found, dups, skipped = {}, [], []
    for mp4 in sorted(root.rglob("*.mp4")):
        m = ID_RE.search(mp4.name)
        if not m:
            skipped.append(mp4.name)
            continue
        vid = m.group(1)
        rel = mp4.parent.relative_to(root)
        vtype = rel.parts[0] if rel.parts else "video"
        if vtype.endswith("s"):
            vtype = vtype[:-1]            # meetups -> meetup
        if vid in found:
            dups.append((vid, mp4.name))
            continue
        dm = DATE_RE.search(mp4.name)
        found[vid] = {"type": vtype, "date": dm.group(1) if dm else None, "path": mp4}
    return found, dups, skipped


# ---------- srt helpers / transcription (proven chunked + watchdog) ----------

def srt_time_to_sec(s):
    hms, ms = s.split(",")
    h, m, sec = hms.split(":")
    return int(h) * 3600 + int(m) * 60 + int(sec) + int(ms) / 1000.0


def sec_to_srt_time(t):
    ms = int(round((t - int(t)) * 1000)); t = int(t)
    return f"{t // 3600:02d}:{(t % 3600) // 60:02d}:{t % 60:02d},{ms:03d}"


def read_srt(path):
    rows = []
    for block in re.split(r"\n\s*\n", path.read_text(encoding="utf-8", errors="replace").strip()):
        lines = [l for l in block.splitlines() if l.strip()]
        ti = next((i for i, l in enumerate(lines) if "-->" in l), None)
        if ti is None:
            continue
        mm = SRT_TIME_RE.search(lines[ti])
        if mm:
            rows.append((srt_time_to_sec(mm.group(1)), srt_time_to_sec(mm.group(2)),
                         " ".join(lines[ti + 1:]).strip()))
    return rows


def video_duration(cfg, mp4):
    out = subprocess.run([cfg["ffprobe"], "-v", "error", "-show_entries", "format=duration",
                          "-of", "default=nw=1:nk=1", str(mp4)], capture_output=True, text=True)
    try:
        return float(out.stdout.strip())
    except ValueError:
        return 0.0


def transcribe(cfg, mp4, vid):
    srt_out = TRANSCRIPTS / f"{vid}.srt"
    if srt_out.exists():
        return "skip"
    dur = video_duration(cfg, mp4)
    if dur <= 0:
        print(f"  ! {vid}: can't read duration; skipping")
        return "error"
    chunk = int(cfg["chunk_seconds"])
    n = int(dur // chunk) + 1
    cdir = TRANSCRIPTS / f".{vid}-chunks"
    cdir.mkdir(parents=True, exist_ok=True)
    print(f"  {vid}: {dur / 3600:.2f} h -> {n} chunk(s)")
    for i in range(n):
        cof = cdir / f"c{i:02d}.srt"
        if cof.exists():
            continue
        cwav = cdir / f"c{i:02d}.wav"
        subprocess.run([cfg["ffmpeg"], "-loglevel", "error", "-ss", str(i * chunk), "-t", str(chunk),
                        "-i", str(mp4), "-vn", "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le",
                        "-y", str(cwav)], check=False)
        print(f"    chunk c{i:02d} ({i * chunk // 60} min) ...")
        try:
            subprocess.run([cfg["whisper_cli"], "-m", cfg["whisper_model"], "-f", str(cwav),
                            "-osrt", "-of", str(cdir / f"c{i:02d}"), "-t", str(cfg["threads"]), "-np"],
                           check=False, timeout=int(cfg["chunk_timeout_seconds"]))
        except subprocess.TimeoutExpired:
            print(f"    chunk c{i:02d} watchdog timeout - leave for a re-run")
        if cwav.exists() and cof.exists():
            try:
                cwav.unlink()
            except OSError:
                pass

    parts = [cdir / f"c{i:02d}.srt" for i in range(n)]
    if not all(p.exists() for p in parts):
        miss = [i for i, p in enumerate(parts) if not p.exists()]
        print(f"  {vid}: PARTIAL - chunks {miss} missing; re-run to finish")
        return "partial"
    rows = []
    for i, p in enumerate(parts):
        for s, e, t in read_srt(p):
            rows.append((s + i * chunk, e + i * chunk, t))
    out_lines = []
    for idx, (s, e, t) in enumerate(rows, 1):
        out_lines += [str(idx), f"{sec_to_srt_time(s)} --> {sec_to_srt_time(e)}", t, ""]
    srt_out.write_text("\n".join(out_lines), encoding="utf-8")
    print(f"  {vid}: done -> {vid}.srt")
    return "done"


def write_meta(vid, info):
    meta = {"id": vid, "type": info["type"], "date": info["date"], "source": info["path"].name}
    (TRANSCRIPTS / f"{vid}.meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")


def main():
    ap = argparse.ArgumentParser(description="Transcribe a video library to per-id .srt files.")
    ap.add_argument("--dry-run", action="store_true", help="list what would be transcribed; do nothing")
    ap.add_argument("--only", metavar="TEXT", help="only videos whose id or source name contains TEXT")
    args = ap.parse_args()
    cfg = load_config()

    if not Path(cfg["videos_dir"]).is_dir():
        sys.exit(f"videos_dir not found: {cfg['videos_dir']} (edit {CONFIG.name})")
    TRANSCRIPTS.mkdir(parents=True, exist_ok=True)

    found, dups, skipped = discover(cfg["videos_dir"])
    if skipped:
        print(f"Skipped {len(skipped)} file(s) with no `__<id>`:")
        for n in skipped:
            print(f"  - {n}")
    if dups:
        print(f"Ignored {len(dups)} duplicate-id file(s) (same video):")
        for vid, n in dups:
            print(f"  - {vid}: {n}")

    items = sorted(found.items(), key=lambda kv: (kv[1]["date"] or "", kv[0]))
    if args.only:
        items = [(v, i) for v, i in items if args.only in v or args.only in i["path"].name]

    print(f"\n{len(items)} video(s) to consider:")
    results = {}
    for vid, info in items:
        done = (TRANSCRIPTS / f"{vid}.srt").exists()
        tag = "[have srt]" if done else "[needs srt]"
        print(f"  {tag} {info['type']:9} {info['date'] or '         '}  {vid}  ({info['path'].name})")
        if args.dry_run:
            continue
        results[vid] = transcribe(cfg, info["path"], vid)
        write_meta(vid, info)

    if not args.dry_run:
        print("\n== summary ==")
        for vid, r in results.items():
            print(f"  {vid}: {r}")
        print("  (nothing committed; transcripts are in .help/.tmp/video-transcripts/)")


if __name__ == "__main__":
    main()
