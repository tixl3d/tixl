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
- **purpose — what this clip *gives* the reader** (a separate axis again; pick the single best fit). It lets
  the help UI group an operator's references into sections and is written so the prose makes it obvious
  *without* naming the tag:
  - `Example` — a concrete setup/wiring to learn from (often pairs the op with others).
  - `Concept` — what it is or how it works under the hood.
  - `Parameters` — what its knobs/attributes do (a "walks every knob" tutorial is the prime case — here
    listing the parameters covered *is* the value, not a dump).
  - `Performance` — cost, speed, optimization behavior.
  - `Comparison` — when to pick it over a sibling operator.
  - `Gotcha` — a pitfall, ordering rule, constraint, or bug to avoid.
  - `Tip` — a shortcut or practical technique.
  `Example`, `Comparison`, `Parameters` rank a touch higher — they're what someone stuck on an op wants first.
- **confidence `N%`** — how sure you are this segment is correctly identified *and* genuinely useful to
  someone stuck on that operator/topic: weigh ASR clarity, on-topic-ness, and how reliable the
  explanation is. A clean scripted demo of the right op ~90%; a garbled or barely-there aside ~50%.
- **One segment per distinct moment; never overlap.** If the same operator comes up at several points,
  give each its own non-overlapping span — don't let one segment's `end` run past the next's `start`,
  and don't emit two segments for the same point.
- **Note voice — teach the operator, not the video.** The reader wants to understand *how to use this
  operator* or *what it's good for*, and is deciding whether this clip is worth their time. Write **one
  self-contained sentence carrying the transferable lesson** — it must make sense to someone who has
  never seen this video. The note is the whole payoff of the deep-link; a weak note wastes the segment.
  - **Frame it as HOW or WHAT.** "How to …", "What … is for", "Why you'd …". Describe the technique or
    purpose, not the on-screen steps.
  - **Drop the scene.** No reference to *this* video's subject — not "the sprinkles", "the trail
    source", "the monster", "the donut", "the intro text". Those are meaningless out of context.
    Generalise to the operator's role ("scattering instances over a surface", "recording past positions
    to draw trails").
  - **No bare parameter dumps.** "cell vs. bounds mode; count/center params" names knobs without saying
    what they *achieve*. Mention a parameter only alongside what it lets you do.
  - **No cryptic shorthand.** If parsing the sentence needs the video ("randomizing the F2 texture-coord
    per copy so repeated meshes dance out of sync"), rewrite it plainly or drop the segment.
  - **The click test:** if the note only makes sense *because* you watched the clip, it has failed.
  - **Don't restate the operator's own NAME.** The note hangs off the operator's marker, so the reader
    already sees it. Open on the verb or the technique, not `"How [ThatOp] …"`. (Reference *other*
    operators in `[brackets]` freely — those aren't redundant.)
  - **Don't restate the operator's DESCRIPTION.** The op's one-line doc sits right above the reference
    list in the help UI. Skip the definition and carry only the *delta* this clip adds — the non-obvious
    step, the number, the pairing, the caveat. (`Example`/`Gotcha`/`Performance`/`Comparison` notes are
    inherently delta-focused, which is why the purpose tag and this rule reinforce each other.)
  - One sentence, may run a touch longer than a label.

  Before → after, for `[GridPoints]` (its doc already says "lays out a grid of points" — don't repeat that):
  - ✗ *"grid points for sprinkles; cell vs. bounds mode; count/center params"* — scene-specific + param dump
  - ✗ *"How [GridPoints] lays out a regular grid of points across a surface…"* — restates name + description
  - ✓ **Cell-spacing vs. bounds sizing — which mode to reach for when you want fixed gaps between instances versus filling a set volume.** · purpose: `Parameters`

  More deltas (name + definition dropped, purpose in italics):
  - `[FastBlur]` → **Up to 10× faster than [Blur] at large radii, with better quality.** *(Performance)*
  - `[MeshVolumeForce]` → **The non-obvious catch: draw the particles before the mesh, or they're hidden inside the solid.** *(Gotcha)*
  - `[IkChain]` → **Feed it a [LinePoints] bone chain and a target; zero the chain's pivot to anchor the root.** *(Example)*

- **Keep the note about THIS operator — not about TiXL.** The lesson must be specific to the bracketed
  operator's own job: what it does, how to drive it, what it's good for. A takeaway that would read the
  same for any operator is a general-TiXL lesson, not an operator reference.
  - General mechanics — stacking / evaluation order, the buffer-copy render model, pull-based caching,
    how the graph snaps, what the Z-buffer does — belong to a **`ui:` topic** (`[ui:Graph]`,
    `[ui:EvaluationContext]`, …), not to an operator. Tag the moment there instead, or drop the marker.
  - If an operator is only *illustrating* a general concept, either re-frame the note to what the clip
    shows about **that operator specifically**, or move the segment to the topic it actually teaches.
  - **Swap test:** mentally substitute a different, unrelated operator into the sentence. If it's still
    just as true, the note is too general — fix or drop it.

  For `[SetMaterial]`:
  - ✓ specific: **How a material operator gives an SDF or mesh its surface look, glossiness moving between shiny and frosted.**
  - ✗ general (really a stacking lesson → belongs on `[ui:Graph]`): *"Why operator order matters in a stack: the last one wins, so set a default then override."*
  - ✗ general (true of every image effect → drop, or `[ui:EvaluationContext]`): *"Why image effects allocate a fresh buffer rather than overwriting the original."*

- **Favor depth — fewer, deeper notes beat many shallow ones.** A clip that genuinely *uses and explains*
  an operator is worth far more than a name-drop. Spend real care on the `explained`/`in-depth` segments;
  for a bare `passing` aside a short honest note is fine (*"briefly named while wiring a particle setup"*) —
  don't inflate it into a lesson it doesn't deliver.

## Step 4 — write `references/video-analysis/<id>.md`

```markdown
---
video: <id>
type: <from the .meta.json: meetup | tutorial | release | …>
date: <from the .meta.json, if any>
title: <clean title>
duration: <H:MM:SS, the last transcript timestamp>
focusesOn: [<Op>], [ui:<Id>]   # OPTIONAL — only when this video IS the dedicated tutorial for those (see below)
---

<1–2 sentence summary — for a focus tutorial this becomes the reference's display note, so write it as a strong standalone "what you'll learn">

## Mentions
- <start>→<end> [<Op>] · <depth> · <style> · <purpose> · <conf>% — <user-facing note>
- <start>→<end> [ui:<Id>] · <depth> · <style> · <purpose> · <conf>% — <note>          (a UI component or concept)
- <start>→<end> [<Op>] [ui:<Id>] · <depth> · <style> · <purpose> · <conf>% — <note>   (more than one marker is fine)
…
```

**Focus tutorials (`focusesOn`).** If a video's whole point is to teach one or more operators/topics, list
them in `focusesOn`. For each focus key the index collapses that video's moments into a **single** reference,
boosts it (×5) so it leads as "the tutorial", labels it with the **summary** above and the **full video
length**, and **drops the video's incidental mentions of other ops**. So a focus tutorial needs only:
- a strong standalone **summary** (it becomes the reference's note), and
- **one** mention line per focus op carrying its `· depth · style · purpose ·` axes — the line's own note is
  superseded by the summary, so keep it short, and don't author the incidental non-focus mentions (they're dropped).

Only set `focusesOn [X]` if the body actually brackets `[X]` somewhere — otherwise the whole video silently
vanishes from `X` (the drop-incidental rule finds nothing to keep). A symbol's curated link is a strong
signal that the linked video is that operator's focus tutorial.

Rules:
- **`<start>→<end>`** is the segment span (the index stores it as `startSecond` + `duration`). Use the
  arrow `→` (a plain hyphen also parses); a single timestamp with no `→<end>` is a zero-length point.
- **`· <depth> · <style> · <purpose> · <conf>%`** are `·`-separated tokens between the markers and the note
  dash — one depth (`passing`/`explained`/`in-depth`), one style (`scripted`/`answer`/`discussion`/
  `experiment`), one purpose (`Example`/`Concept`/`Parameters`/`Performance`/`Comparison`/`Gotcha`/`Tip`),
  and a confidence percentage. The parser scans for each by name (order is forgiving), so a legacy line
  missing the purpose token still parses — it just gets no purpose.
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
