# Documentation & Discovery Ecosystem

TiXL v4.2's theme is **documentation & discovery**. This is the source of truth for how the
help surfaces relate and how the meet-up → docs pipeline works. Agent-neutral; referenced
from `CLAUDE.md` and `AGENT_INSTRUCTIONS.md`.

## Help surfaces, by depth

| Tier | Surface | Feel |
| --- | --- | --- |
| 0 glance | inline tooltips, operator *first line* | omnipresent, 1 line |
| 1 context | ToolTip panel (issue #102), full op/param description | on-demand, in place |
| 2 guided task | HowTos, Welcome intro | "show me once" |
| 3 deep practice | SkillQuest, Guided Feature Tours | "let me try it" |
| 4 reference | the manual (`.help/docs/`, help.tixl.app) | "explain everything" |
| — archive | meet-up / tutorial videos (YouTube) | "watch the demo" |

Rule: authored cross-links point **one tier deeper** ("learn more"); the upward backlinks
("referenced from") are **derived** from the index, never hand-authored.

## Cross-reference model

- One shared reference **index** is the spine; every surface reads it.
- Address scheme `type:id`. **Operators are unprefixed** (`[LinearGradient]`, resolved by the
  existing `op_autolinks.py`); other surfaces use `tour:` `quest:` `manual:` `video:`.
- **Internal** targets (repo-resident) are link-checked at build (`mkdocs strict`). **External**
  targets (videos, arbitrary URLs) are soft — a dead YouTube link must never break a build.
- **UI components carry stable ids.** The `ui:` namespace (Parameter Window, Performance
  Window, …) resolves against `[HelpUiID("PerformanceWindow")]` attributes on Editor component
  classes, collected by reflection into `references/indices/components.json` — the UI analogue of
  the operator doc index. This formalises today's ad-hoc `DocumentationButton.Draw("<docId>", …)`
  string literals into one declared, validatable id, so `ui:` mentions/links are checked
  like operator names. Decouples the help-id from the class name. **Keep it curated** — roughly
  30–100 across the whole app (major windows, panels, a few distinctive controls), not every
  widget; an id is a cross-reference commitment, added only for things that actually get
  referenced. (Editor-side; also the enabler for the #102 tooltip to light up on UI, not just
  operators.)

## Folder layout

```
.help/
  .src/                          # committed — HAND-AUTHORED source (video scripts, drafts)
  .tmp/                          # gitignored — regenerable, safe to delete
    audio/                       #   *.wav  (whisper input, ~0.5 GB each)
    video-transcripts/           #   *.srt / *.txt  (raw ASR)
    youtube/                     #   *.txt  (generated descriptions, to paste)
    summaries/meetup/<date>.md   #   LLM extraction — staging for the (future) mentions enrichment
  references/                    # committed — the durable, distilled layer
    videos.map.json              #   date -> YouTube id (recorded once after upload)
    indices/
      videos.json                #   video/meet-up chapters + segment references
      skillquest.json            #   quest entries + refs (TBD)
      mentions.json              #   "type:id" reference -> [video moments] for the editor
  scripts/
    update_help_index.py         #   the mechanical entry point (below)
    update_help_index.local.json #   gitignored — machine paths
    op_autolinks.py              #   existing MkDocs auto-linker
  docs/                          # the published MkDocs tree (unchanged)
    operators/index.json         #   existing operator doc index
```

Generated **wiki pages do not live here** — they are written into the *wiki repo* working copy
(`tixl3d/tixl.wiki`, cloned parallel, path from `update_help_index.local.json`), files only.

## Index schema

`videos.json` — `{ emojiLegend, videos: [ { id, type, source:{type,id}, date, title, url,
thumbnail, durationLabel, sourcePage, segments: [ { t, tLabel, durationSec?, category, emoji,
text, url, ops[], issues[], notes[], section? } ] } ] }`

`mentions.json` — `{ "<type>:<id>": [ { source:{type,id}, url, date, tLabel, category,
durationMin, title, summary } ], ... }` — keys are typed references
(`op:Lib.io.video.VideoDeviceInput`, `ui:PerformanceWindow`, …), so non-operator
entities slot in beside operators. Merged across all sources so the editor tooltip loads one file. `category` (emoji-derived) + `durationMin` (gap to the next chapter) qualify each
mention — a 26-min op-reference vs a 1-min chat. `source.type` makes the platform explicit
(future Vimeo / local). `summary` (plain "what you'll find" text) is filled by the LLM
enrichment skill, not the mechanical pass.

Emoji → category legend (extensible; unknown emojis are flagged, not dropped): `chat 💬🗯️ ·
tip 💡 · op-reference 📘 · ui 🖱️ · highlight 🌟 · feature 🆕 · update 🛠️ · background 🧠📊 · planned 🎯 ·
showcase ✨🍿 · walkthrough 📝 · warning ⚠️ · question ❓` (pending labels: 🔢 🐛 🙋).

## The meet-up → docs pipeline

Two halves, by nature of the work:

### Mechanical (deterministic) — `update_help_index.py`
- Scans `Videos\_tixl\meetups\*.mp4`. For any capture without a transcript: ffmpeg → 16 kHz
  mono WAV → whisper.cpp (`ggml-base.en`) in **30-minute chunks**, each on a watchdog.
  **Resumable** (skips chunks already done) — a killed run loses nothing. ~30 min per 4-h video
  at ~9× realtime.
- Backfills the index from existing hand-written wiki notes (tolerant parser, ~5 line formats).
- Merges per-source indices into `mentions.json`.
- Idempotent. Reads machine paths from `update_help_index.local.json`. **Writes files only —
  never `git add` / `commit` / `push`.**
- **Runs locally, foreground.** Do *not* launch transcription as a chat/session background task —
  those die silently on long runs (observed repeatedly).

### Judgment (LLM) — the `describe-meetup` skill
- Reads the new transcript(s); produces the distilled summary (summary + chapters + highlights +
  operators) → `references/summaries/meetup/<date>.md`.
- Chapter **granularity** matters: enough chapters that each topic's duration (gap to the next)
  is meaningful. The skill assigns every chapter a `category` (incl. `🖱️ ui` for
  panels/controls/drag behaviors) and fills each operator mention's plain-text `summary` in
  `mentions.json` by reading the transcript around that timestamp.
- **Resolves the YouTube id** from `references/videos.map.json`; if the date is missing it asks
  for the video URL (e.g. `https://youtu.be/qavgcL72F1Y`), extracts the id, and records it — so
  you never hand-edit JSON. (Existing wiki pages are backfilled by reading the id already
  embedded in them.) The id is needed only at generation, not for transcription.
- Generates: the **wiki page** (`<wiki>/meetup.<date>.md`, canonical format, deep-linked
  chapters, ops auto-linkable), the **YouTube description** (`.tmp/youtube/<date>.txt`, plain
  chapters + links footer), and the **index delta** (`videos.json` + `mentions.json`).
- **Every chapter keeps its emoji category** on both surfaces (`📘 operator`, `🍿 showcase`,
  `🧠 deep dive`, …) — it aids triage on multi-hour videos and keeps the wiki and YouTube
  consistent. The YouTube description leads with a one-line legend.
- The YouTube `.txt` is fully copy-paste: **line 1 is a ≤100-char title** in the form
  `TiXL Meetup <date> / <topics>` — identical to the wiki page H1, so one title serves both
  surfaces — then a blank line, then the body (summary, legend, chapters, links). Line 1 → the
  title field, the rest → the description field.
- The description carries a **wiki backlink** — `More details … on the TiXL wiki:
  https://github.com/tixl3d/tixl/wiki/meetup.<date>` (URL constructable from the date) — the
  video→wiki half of the cross-link; the wiki page links back via its thumbnail and chapters.
- Human reviews. Nothing committed.

## The full process

**One-time setup:** clone the wiki parallel (`../tixl.wiki`); create
`update_help_index.local.json` (wiki path, videos dir, whisper toolkit + model, ffmpeg);
ensure ffmpeg + the whisper.cpp build are present.

**Per capture:**
1. Drop the recording in `Videos\_tixl\meetups\` as `YYYY-MM-DD ….mp4`; upload to YouTube
   (unlisted is fine) and keep its URL — the skill asks for it in step 3 and records the id.
2. `python .help/scripts/update_help_index.py` → transcribes new captures (~30 min each),
   refreshes the indices. Wait.
3. In a Claude session, run the skill → drafts the summary, wiki page, YouTube description, and
   index delta.
4. Paste `.help/.tmp/youtube/<date>.txt` into the YouTube video description.
5. Review changed files (`git status` / `diff`) in **both** the TiXL repo and the wiki repo.
6. Rebuild + test in the Editor — the operator tooltip now shows "discussed in-depth at <date> →
   <timestamp>".
7. Commit (you), in both repos.

## Guardrails

- Scripts never touch the git index or remotes — they write files; you commit.
- Raw transcripts and WAVs are regenerable → `.help/.tmp/` (gitignored). Distilled summaries are
  kept.
- ASR is rough (names/timestamps drift) — a human pass is required before anything is published.
- The reference index also covers SkillQuest, operators, and manual pages; the meet-up pipeline is
  one feeder. Cross-reference maintenance across them is an LLM-assisted, human-reviewed sweep.

## Status

- **Built:** `update_help_index.py` (resumable transcription + index from wiki notes); the
  wiki-notes parser (`meetup_references.py`); the **`describe-meetup`** skill (transcript →
  summary + wiki page + YouTube text); the typed `op:`/`ui:` index with category + duration
  qualifiers.
- **Next:** the editor tooltip consumer (#102); `[HelpUiID]` + `components.json` (makes `ui:`
  resolvable); the `mentions.json` `summary`/`ui:` merge step; the SkillQuest feeder.
