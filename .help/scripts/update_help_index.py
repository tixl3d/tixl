#!/usr/bin/env python3
"""
update_help_index.py — mechanical half of the meet-up -> docs pipeline.

Two phases, both deterministic (no LLM, no git):
  1. transcribe — for each meet-up recording without a transcript yet, extract audio
                  and run whisper.cpp in resumable 30-min chunks, then stitch a
                  timestamped .srt (+ .txt) into .help/.tmp/video-transcripts/.
  2. index      — rebuild the reference index from the wiki's meet-up notes into
                  .help/references/indices/{videos.json,backlinks.json}, refreshing the
                  wiki review artifacts in .help/.tmp/wiki-out/.

The LLM step (transcript -> summary, wiki page, YouTube text) is a separate Claude Code
skill. This script only writes files; it never stages or commits. Machine paths come from
update_help_index.local.json (git-ignored). See .agentic/DOCUMENTATION_ECOSYSTEM.md.

Usage:
    python update_help_index.py [--skip-transcribe] [--skip-index]
"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

import meetup_references as mr  # sibling module: the wiki-notes parser

HERE = Path(__file__).resolve().parent   # .help/scripts
HELP = HERE.parent                       # .help
REPO = HELP.parent                       # repo root

TRANSCRIPTS = HELP / ".tmp" / "video-transcripts"
WIKI_OUT    = HELP / ".tmp" / "wiki-out"
INDICES     = HELP / "references" / "indices"
OP_INDEX    = HELP / "docs" / "operators" / "index.json"
CONFIG      = HERE / "update_help_index.local.json"

CONFIG_TEMPLATE = {
    "videos_dir":    "C:/path/to/meetup/recordings",
    "wiki_repo":     "C:/path/to/t3.wiki",
    "ffmpeg":        "ffmpeg",
    "ffprobe":       "ffprobe",
    "whisper_cli":   "C:/path/to/whisper-cli.exe",
    "whisper_model": "C:/path/to/ggml-base.en.bin",
    "threads": 20,
    "chunk_seconds": 1800,
    "chunk_timeout_seconds": 900,
}

DATE_RE = re.compile(r"(\d{4}-\d{2}-\d{2})")
SRT_TIME_RE = re.compile(r"(\d\d:\d\d:\d\d,\d+)\s*-->\s*(\d\d:\d\d:\d\d,\d+)")


def load_config():
    if not CONFIG.exists():
        CONFIG.write_text(json.dumps(CONFIG_TEMPLATE, indent=2), encoding="utf-8")
        sys.exit(f"Wrote a config template to {CONFIG}\nEdit the paths, then re-run.")
    cfg = dict(CONFIG_TEMPLATE)
    cfg.update(json.loads(CONFIG.read_text(encoding="utf-8")))
    return cfg


# ---------- SRT helpers ----------

def srt_time_to_sec(s):
    hms, ms = s.split(",")
    h, m, sec = hms.split(":")
    return int(h) * 3600 + int(m) * 60 + int(sec) + int(ms) / 1000.0


def sec_to_srt_time(t):
    ms = int(round((t - int(t)) * 1000)); t = int(t)
    return f"{t // 3600:02d}:{(t % 3600) // 60:02d}:{t % 60:02d},{ms:03d}"


def read_srt(path):
    text = path.read_text(encoding="utf-8", errors="replace").strip()
    rows = []
    for block in re.split(r"\n\s*\n", text):
        lines = [l for l in block.splitlines() if l.strip()]
        ti = next((i for i, l in enumerate(lines) if "-->" in l), None)
        if ti is None:
            continue
        m = SRT_TIME_RE.search(lines[ti])
        if not m:
            continue
        rows.append((srt_time_to_sec(m.group(1)), srt_time_to_sec(m.group(2)),
                     " ".join(lines[ti + 1:]).strip()))
    return rows


# ---------- transcription ----------

def video_duration(cfg, mp4):
    out = subprocess.run([cfg["ffprobe"], "-v", "error", "-show_entries", "format=duration",
                          "-of", "default=nw=1:nk=1", str(mp4)], capture_output=True, text=True)
    try:
        return float(out.stdout.strip())
    except ValueError:
        return 0.0


def transcribe_one(cfg, mp4, date):
    srt_out = TRANSCRIPTS / f"{date}.srt"
    if srt_out.exists():
        return "skip"
    dur = video_duration(cfg, mp4)
    if dur <= 0:
        print(f"  ! {date}: could not read duration; skipping")
        return "error"
    chunk = int(cfg["chunk_seconds"])
    n = int(dur // chunk) + 1
    chunks_dir = TRANSCRIPTS / f".{date}-chunks"
    chunks_dir.mkdir(parents=True, exist_ok=True)
    print(f"  {date}: {dur / 3600:.2f} h -> {n} chunk(s)")
    for i in range(n):
        cof = chunks_dir / f"c{i:02d}.srt"
        if cof.exists():
            continue
        start = i * chunk
        cwav = chunks_dir / f"c{i:02d}.wav"
        subprocess.run([cfg["ffmpeg"], "-loglevel", "error", "-ss", str(start), "-t", str(chunk),
                        "-i", str(mp4), "-vn", "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le",
                        "-y", str(cwav)], check=False)
        print(f"    chunk c{i:02d} (start {start // 60} min) transcribing...")
        try:
            subprocess.run([cfg["whisper_cli"], "-m", cfg["whisper_model"], "-f", str(cwav),
                            "-osrt", "-of", str(chunks_dir / f"c{i:02d}"),
                            "-t", str(cfg["threads"]), "-np"],
                           check=False, timeout=int(cfg["chunk_timeout_seconds"]))
        except subprocess.TimeoutExpired:
            print(f"    chunk c{i:02d} watchdog timeout - leave for a re-run")
        if cwav.exists() and cof.exists():
            try:
                cwav.unlink()
            except OSError:
                pass

    parts = [chunks_dir / f"c{i:02d}.srt" for i in range(n)]
    if not all(p.exists() for p in parts):
        missing = [i for i, p in enumerate(parts) if not p.exists()]
        print(f"  {date}: PARTIAL - chunks {missing} missing; re-run to finish")
        return "partial"

    rows = []
    for i, p in enumerate(parts):
        for s, e, t in read_srt(p):
            rows.append((s + i * chunk, e + i * chunk, t))
    out_lines = []
    for idx, (s, e, t) in enumerate(rows, 1):
        out_lines += [str(idx), f"{sec_to_srt_time(s)} --> {sec_to_srt_time(e)}", t, ""]
    srt_out.write_text("\n".join(out_lines), encoding="utf-8")
    (TRANSCRIPTS / f"{date}.txt").write_text("\n".join(t for _, _, t in rows), encoding="utf-8")
    print(f"  {date}: done -> {srt_out.name}")
    return "done"


def run_transcription(cfg):
    vids = Path(cfg["videos_dir"])
    if not vids.is_dir():
        print(f"  videos_dir not found: {vids} - skipping transcription")
        return {}
    TRANSCRIPTS.mkdir(parents=True, exist_ok=True)
    results = {}
    for mp4 in sorted(vids.glob("*.mp4")):
        m = DATE_RE.search(mp4.name)
        if m:
            results[m.group(1)] = transcribe_one(cfg, mp4, m.group(1))
    return results


# ---------- index ----------

def run_index(cfg):
    wiki = Path(cfg["wiki_repo"])
    if not wiki.is_dir():
        print(f"  wiki_repo not found: {wiki} - skipping index")
        return None
    WIKI_OUT.mkdir(parents=True, exist_ok=True)
    INDICES.mkdir(parents=True, exist_ok=True)
    pages, stubs, backlinks, _mism, _undecl, _ambig = mr.build(wiki, OP_INDEX, WIKI_OUT)
    ext = json.loads((WIKI_OUT / "external-references.json").read_text(encoding="utf-8"))
    videos_json = {"schemaVersion": ext["schemaVersion"], "emojiLegend": ext["emojiLegend"],
                   "videos": ext["videos"]}
    (INDICES / "videos.json").write_text(
        json.dumps(videos_json, ensure_ascii=False, indent=2), encoding="utf-8")
    (INDICES / "mentions.json").write_text(
        json.dumps(ext["mentions"], ensure_ascii=False, indent=2), encoding="utf-8")
    return {"pages": len(pages), "stubs": len(stubs),
            "refs": len(ext["mentions"]),
            "segments": sum(len(v["segments"]) for v in ext["videos"])}


def main():
    ap = argparse.ArgumentParser(description="Mechanical half of the meet-up -> docs pipeline.")
    ap.add_argument("--skip-transcribe", action="store_true", help="don't transcribe new captures")
    ap.add_argument("--skip-index", action="store_true", help="don't rebuild the reference index")
    args = ap.parse_args()
    cfg = load_config()

    print("== update_help_index ==")
    tr = {}
    if not args.skip_transcribe:
        print("- transcribe")
        tr = run_transcription(cfg)
    idx = None
    if not args.skip_index:
        print("- index")
        idx = run_index(cfg)

    print("\n== summary ==")
    for d, r in sorted(tr.items()):
        print(f"  transcript {d}: {r}")
    if idx:
        print(f"  index: {idx['pages']} pages, {idx['segments']} segments, "
              f"{idx['refs']} referenced entities ({idx['stubs']} stubs skipped)")
        print(f"  -> .help/references/indices/videos.json + mentions.json")
    print("  (nothing committed - review with `git status`)")


if __name__ == "__main__":
    main()
