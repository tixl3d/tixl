---
name: analyze-videos
description: Extract a comprehensive operator-mention analysis from each new video transcript. Reads the SRTs in .help/.tmp/video-transcripts/ (produced by video_to_srt.py), and for each one without an analysis yet, writes references/video-analysis/<id>.md — every meaningful operator discussion with its timestamp, depth, and a one-line note. Feeds the editor's deep-link index; never touches the wiki, the YouTube descriptions, or git. Use when the user wants to analyze transcribed videos, build the operator mention index, or invokes /analyze-videos.
---

# analyze-videos

STAGE 2 of the video → docs pipeline. `video_to_srt.py` produced the transcripts; this skill turns
each into a **comprehensive operator-mention analysis** — the committed source the editor reads to
deep-link operators into the videos. `analysis_to_index.py` then builds the index from these.

This is **not** a summary or a chapter list. Your wiki pages and YouTube descriptions are the
concise, human-curated view and **you keep writing those by hand** — this skill never generates or
touches them. The analysis is the *exhaustive* machine view: every operator that gets meaningfully
discussed, at its timestamp. Full design: `.agentic/DOCUMENTATION_ECOSYSTEM.md`.

## Hard invariants

- **Never `git add` / commit / push.** Write files; the user reviews and commits.
- **Don't touch the wiki repo or the YouTube `.txt` files.** Out of scope.
- **Be exhaustive, not selective.** The whole point is to capture operators the curated chapters
  *skip* — passing mentions count, with `depth: passing`. Aim for *dozens* per long video.
- **Bracket only confirmed operators.** The transcript is raw ASR and speakers talk in generics
  ("noise", "gradient", "value"). Bracket a name **only if it's in the operator vocabulary**
  (Step 2): pick the specific operator the context implies (`"fractal noise"` → `[FractalNoise]`),
  and leave a vague mention unbracketed (describe it in prose) rather than inventing a leaf name.
  Flag any you're unsure of in the hand-off.

## Step 1 — find the work

For each `*.srt` in `.help/.tmp/video-transcripts/` that has **no** matching
`references/video-analysis/<id>.md` yet, this is a video to analyze. Read its sidecar
`<id>.meta.json` (written by `video_to_srt.py`) for `type` and `date`. Process each in turn; an SRT
that already has an analysis is skipped (idempotent).

## Step 2 — refresh the operator vocabulary

Bracketing is only useful if names match real operators, so give the extractors the canonical leaf
names. Dump them from the generated operator index to a scratch file (do this once per batch — the
op set only changes when operators are added):

```bash
python - <<'PY'
import json
from pathlib import Path
idx = json.loads(Path(".help/docs/operators/index.json").read_text(encoding="utf-8"))
names = sorted(idx.get("by_shortname", {}))
out = Path(".help/.tmp/op-vocabulary.txt")
out.parent.mkdir(parents=True, exist_ok=True)
out.write_text("\n".join(names) + "\n", encoding="utf-8")
print(f"{len(names)} operator names -> {out}")
PY
```

`.help/.tmp/` is git-ignored, so this is regenerated on demand.

## Step 3 — extract (per video)

The SRT is large (~90k tokens for a 4-hour video) — **spawn a subagent** to read it end to end in
sequential chunks and return a comprehensive, deduplicated list of operator mentions. In the prompt:

- Have it **read `.help/.tmp/op-vocabulary.txt` first** — that's the *closed set* of real TiXL
  operators (PascalCase leaf names, e.g. `RadialGradient`, `ParticleSystem`, `DrawPoints`).
- The ASR mishears names and speakers use generics. Bracket a name **only if it's in that
  vocabulary**; map the spoken word to the specific operator the context implies (`"fractal noise"`
  → `[FractalNoise]`, `"the gradient"` along a line → `[LinearGradient]`). If a mention is too vague
  to pin to a real operator, leave it unbracketed rather than inventing one. Acronyms follow TiXL
  casing: `Ik`, `Sdf`, `Obj`, `2d` (`IkChain`, `LoadObj`, `Image2dSDF`).
- Capture **every** operator that's meaningfully named or demonstrated. For each mention it returns:
  `t` (M:SS or H:MM:SS), the operator name(s), a **depth** (`passing` | `explained` | `in-depth`),
  and a one-line *what you'll find* note. Also a 1–2 sentence overall summary, a clean title, and a
  list of any names it was unsure about.

## Step 4 — write `references/video-analysis/<id>.md`

```markdown
---
video: <id>
type: <from the .meta.json: meetup | tutorial | release | …>
date: <from the .meta.json, if any>
title: <clean title>
duration: <H:MM:SS, the last transcript timestamp>
---

<1–2 sentence summary>

## Mentions
- <M:SS> [<Op>] · <depth> — <one-line note>
- <M:SS> [<OpA>] [<OpB>] · <depth> — <note>     (a moment can name more than one)
…
```

Rules:
- **Bracket every operator** as `[OpName]` — that's the explicit marker `analysis_to_index.py`
  resolves against the operator index (plain text is ignored, so unbracketed = not indexed). The
  resolver matches case-insensitively, so a stray `IKChain` still finds `IkChain`.
- One line per moment; keep them in time order; `depth` is one of `passing` / `explained` /
  `in-depth`. The note is the "is it worth clicking?" text. Notes use an em/en dash (`—`/`–`) after
  the depth, never a hyphen (the parser splits on the dash).
- Be comprehensive — a 4-hour meet-up should yield many dozens of lines, not ~30.

## Step 5 — refresh the index and hand off

- Run `python .help/scripts/analysis_to_index.py` to rebuild `videos.json` + `mentions.json`.
- Report per video: the analysis written and a count of mentions, plus any operator names you were
  unsure about (so the user can fix a bracket). Nothing is committed — the user reviews
  `references/video-analysis/*` and `references/indices/*` and commits.

## Notes

- Batch: refresh the vocabulary (Step 2) once, then loop Steps 3–4 over every new SRT, then run
  Step 5 once at the end.
- If a `.meta.json` is missing (hand-placed SRT), ask the user for the `type`, default `video`.
