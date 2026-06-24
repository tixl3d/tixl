---
name: write-topic-docs
description: Draft the embedded help text for ui: topics whose doc is still empty. For each topic it gathers the transcript passages where the maintainer explains it (via the ui: deep-link index), the implementing class, and any existing .help/ page, then distills a short user-facing doc into .help/references/topics/ui-topics.md. Use after the videos are transcribed and analyzed (stage 2), when the user wants to write or fill in the ui topic docs.
---

# write-topic-docs

STAGE 4 (authoring) of the docs pipeline. The `ui:` topic registry
(`.help/references/topics/ui-topics.md`) holds metadata + synonyms + class pointers; the **doc body**
of each topic is the help shown on hover / in the help panel. This skill fills the `_TODO_` bodies —
sourced primarily from the maintainer's **own video explanations**, located via the `ui:` deep-link
index and fact-checked against the implementing class.

**Run only after stage 2** (`/analyze-videos`) has populated `mentions.json` with `[ui:Id]` mentions —
without them there are no transcript passages to distill from. Full design: `.agentic/DOCUMENTATION_ECOSYSTEM.md`.

## Hard invariants

- **Never `git add` / commit.** Write the docs; the user reviews and commits.
- **User-facing voice** (`.help/docs/STYLE.md`): plain English, second person, no marketing tone.
- **Never name the C# class in the prose.** The class is your fact-check, not content — STYLE.md
  keeps implementation detail out of `.help/`. Operators go in `[Brackets]` (the docs build
  auto-links them); bold a UI element on first mention.
- **Ground every claim in a source.** Distill the transcript passages + class behaviour; don't invent
  features. If a topic has no usable video coverage and the class is unclear, leave `_TODO_` and flag it.

## Step 1 — find the work

`python .help/scripts/topic_doc_sources.py` (no arg) lists every topic whose doc is still empty and
prints each one's source bundle. Target one with `python .help/scripts/topic_doc_sources.py ui:Timeline`.

## Step 2 — read the source bundle (per topic)

The helper prints, for the topic: its `classes`, any existing `.help/` page, and the top transcript
passages where it's discussed (ranked in-depth > explained > passing, most recent first) — each with
the video, timestamp and deep-link URL. That bundle is the raw material; prefer recent, in-depth
passages (a lone `passing` mention is usually too thin to write from).

## Step 3 — draft (per topic)

Spawn a subagent with the bundle (or draft directly for a single topic). Produce:

- **First line:** one sentence — what it is and when you'd reach for it. This is the hover text, so it
  must stand alone.
- a blank line, then **2–4 sentences**: where to find it, the key actions/parameters, one practical
  tip. Second person; bold a UI element on first mention; bracket operators.

Open the `classes` file(s) only to verify terminology and behaviour — keep the code detail out of the
prose. If a fuller `.help/` page exists, keep the embedded doc short and let that page carry detail.

## Step 4 — write + refresh

- In `.help/references/topics/ui-topics.md`, replace that topic's `_TODO_` with the drafted body,
  matching the `## <Term>` block so it lands on the right topic.
- After the batch, run `python .help/scripts/analysis_to_index.py` to fold the docs into `topics.json`.
- Report which topics were written and which stayed `_TODO_` for lack of sources. Nothing is committed
  — the user reviews `ui-topics.md` and commits.

## Notes

- Batch: loop Steps 2–3 over the empty-doc topics, write each as you go, run Step 4 once at the end.
- A meet-up demo distilled into a doc: per STYLE.md, the deep-link URL already records which video and
  moment it came from, so the cross-reference is preserved automatically.
