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
- **Bracket only confirmed names.** The transcript is raw ASR and speakers talk in generics
  ("noise", "the timeline"). Bracket a name **only if it's in a Step 2 vocabulary** — operators as
  bare `[FractalNoise]`, UI components/concepts as `[ui:DopeSheet]`. Pick the specific entry the
  context implies; leave a vague mention unbracketed (describe it in prose) rather than inventing a
  name. Flag any you're unsure of in the hand-off.

## Step 1 — find the work

For each `*.srt` in `.help/.tmp/video-transcripts/` that has **no** matching
`references/video-analysis/<id>.md` yet, this is a video to analyze. Read its sidecar
`<id>.meta.json` (written by `video_to_srt.py`) for `type` and `date`. Process each in turn; an SRT
that already has an analysis is skipped (idempotent).

## Step 2 — refresh the vocabularies

Bracketing only helps if names resolve. Dump both the operator leaf names and the UI-topic
vocabulary (preferred term + synonyms) to scratch files the extractors read (once per batch):

```bash
python - <<'PY'
import json
from pathlib import Path
tmp = Path(".help/.tmp"); tmp.mkdir(parents=True, exist_ok=True)

ops = sorted(json.loads(Path(".help/docs/operators/index.json").read_text(encoding="utf-8"))
             .get("by_shortname", {}))
(tmp / "op-vocabulary.txt").write_text("\n".join(ops) + "\n", encoding="utf-8")

topics = json.loads(Path(".help/references/indices/topics.json").read_text(encoding="utf-8"))["topics"]
lines = [f"ui:{tid}  —  " + "; ".join([t["term"]] + t["synonyms"]) for tid, t in sorted(topics.items())]
(tmp / "topic-vocabulary.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
print(f"{len(ops)} operators, {len(lines)} UI topics")
PY
```

`.help/.tmp/` is git-ignored, so these regenerate on demand. The UI topics come from the
hand-authored registry `.help/references/topics/ui-topics.md` (compiled into `topics.json` by
Step 5) — edit that file to add a topic or synonym.

## Step 3 — extract (per video)

The SRT is large (~90k tokens for a 4-hour video) — **spawn a subagent** to read it end to end in
sequential chunks and return a comprehensive, deduplicated list of operator mentions. In the prompt:

- Have it **read `.help/.tmp/op-vocabulary.txt` and `.help/.tmp/topic-vocabulary.txt` first** — the
  closed sets of real TiXL operators (PascalCase leaf names, e.g. `RadialGradient`, `DrawPoints`)
  and UI components/concepts (each line `ui:<Id>  —  term; synonyms`).
- The ASR mishears names and speakers use generics. Bracket a name **only if it's in a vocabulary**:
  - **Operators** → bare `[FractalNoise]`; map the spoken word to the specific op the context
    implies (`"fractal noise"` → `[FractalNoise]`, `"the gradient"` along a line →
    `[LinearGradient]`). Acronyms follow TiXL casing: `Ik`, `Sdf`, `Obj`, `2d`.
  - **UI concepts** → `[ui:<Id>]`, mapping any synonym to its id (`"dope sheet area"` →
    `[ui:DopeSheet]`, `"performance window"` → `[ui:PerformanceMonitor]`). One moment can name an
    operator *and* a UI topic. The topic vocabulary lists ids already shown as `ui:…` — write
    `[ui:DopeSheet]`, **not** `[ui:ui:DopeSheet]`.
  - Too vague to map to either? Leave it unbracketed (describe it in prose) rather than inventing a
    name.
- Capture **every** operator *and UI component/concept* meaningfully named or demonstrated. For each,
  return a **segment**: `start→end` (M:SS or H:MM:SS — the span where it's actually discussed, not a
  single point), the marker(s), a **depth** (`passing` | `explained` | `in-depth`), a **style** (below),
  a **confidence** `N%`, and a user-facing **note**. Also a 1–2 sentence overall summary, a clean
  title, and any names it was unsure of.
- **style — how structured/trustworthy the moment is** (a separate axis from depth — it's always one
  of the four values below, **never** a depth word like `explained`; infer it from the language):
  - `scripted` — a prepared, polished walkthrough (one narrator presenting, no fumbling — the tutorials).
  - `answer` — a direct reply to a posed question.
  - `discussion` — open back-and-forth, opinions and trade-offs weighed.
  - `experiment` — live trial-and-error, figuring it out, hitting and fixing snags.
  Most→least reliable: `scripted` > `answer` > `discussion` > `experiment` — it feeds relevancy ranking.
- **confidence `N%`** — how sure you are this segment is correctly identified *and* genuinely useful to
  someone stuck on that operator/topic: weigh ASR clarity, on-topic-ness, and how reliable the
  explanation is. A clean scripted demo of the right op ~90%; a garbled or barely-there aside ~50%.
- **One segment per distinct moment; never overlap.** If the same operator comes up at several points,
  give each its own non-overlapping span — don't let one segment's `end` run past the next's `start`,
  and don't emit two segments for the same point.
- **Note voice — user-facing, inviting, "what you'll learn"**, not a transcript paraphrase. Frame the
  takeaway; reference operators in `[brackets]`; one sentence, can run a touch longer than a label:
  - *"recommended as a heavier procedural test"* → **How `[FractalNoise]` can be used for performance tests.**
  - *"animated by connecting its phase to time…"* → **How to prevent caching by connecting a time operator.**
  - *"resolution increased to 4K…"* → **Example of how large resolutions like 4K quickly demonstrate fill-rate limits.**

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
- <start>→<end> [<Op>] · <depth> · <style> · <conf>% — <user-facing note>
- <start>→<end> [ui:<Id>] · <depth> · <style> · <conf>% — <note>            (a UI component or concept)
- <start>→<end> [<Op>] [ui:<Id>] · <depth> · <style> · <conf>% — <note>     (more than one marker is fine)
…
```

Rules:
- **`<start>→<end>`** is the segment span (the index stores it as `startSecond` + `duration`). Use the
  arrow `→` (a plain hyphen also parses); a single timestamp with no `→<end>` is a zero-length point.
- **`· <depth> · <style> · <conf>%`** are `·`-separated tokens between the markers and the note dash —
  one depth (`passing`/`explained`/`in-depth`), one style (`scripted`/`answer`/`discussion`/`experiment`),
  and a confidence percentage.
- **Markers before the dash are indexed; the note is display-only.** `[OpName]` / `[ui:Id]` in the
  marker position resolve into the index (case-insensitively — a stray `IKChain` finds `IkChain`, a
  bare `[Timeline]` resolves to `ui:Timeline`). `[Op]` links *inside the note* feed the help UI's
  auto-linker and are **not** counted as mentions — so use them freely for readability.
- The note dash is an em/en dash (`—`/`–`), never a hyphen (the hyphen is the range separator).
- Keep segments in time order and non-overlapping. Be comprehensive — a 4-hour meet-up yields many
  dozens of segments, not ~30.

## Step 5 — refresh the index and hand off

- Run `python .help/scripts/analysis_to_index.py` to rebuild `videos.json` + `mentions.json`.
- Report per video: the analysis written and a count of mentions, plus any operator names you were
  unsure about (so the user can fix a bracket). Nothing is committed — the user reviews
  `references/video-analysis/*` and `references/indices/*` and commits.

## Notes

- Batch: refresh the vocabulary (Step 2) once, then loop Steps 3–4 over every new SRT, then run
  Step 5 once at the end.
- If a `.meta.json` is missing (hand-placed SRT), ask the user for the `type`, default `video`.
