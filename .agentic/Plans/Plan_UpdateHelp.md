# Migrate documentation to `.help/` and publish via MkDocs

**Date:** 2026-04-18
**Status:** In progress. Seed pages moved from the legacy GitHub wiki (`../t3.wiki/`) into `.help/` and trimmed; site deployment and image migration are still open.

## Goal

Split documentation along a clear line:

- **`.help/` — user-facing.** Installation, UI, how-tos, FAQs, custom-shader / custom-operator authoring, live performance, advanced features. Single source of truth, ships with the code, publishes to `tixl3d.github.io`. Wiki pages that have been migrated here get a redirect banner pointing at the new URL.
- **GitHub wiki — developer-facing.** Building TiXL from source, coding conventions, CI, integration tests, renderdoc, git workflow, release process, ad-hoc design discussions. Stays editable on the wiki; no migration, no redirect banners for these pages.

The line: **if a user making motion graphics or live visuals would want to read it, it goes into `.help/`. If only someone opening `t3.sln` would want to read it, it stays on the wiki.**

## Current state (after this pass)

- `.help/README.md` — index of migrated pages only; links to not-yet-migrated topics removed.
- `.help/STYLE.md` — writing guide for contributors and agents.
- 20 pages migrated under `general/`, `setup/`, `ui/`, `advanced/`. Filenames have had the `help.` / `dev.` / `help.ui.` prefixes stripped.
- Internal cross-links pointing at renamed files have been fixed inline where the target page exists in `.help/`.
- Per-page issues (typos, outdated instructions, missing images, sections that need rewriting) are catalogued below.

## Work plan

### 1. MkDocs + mike publishing

Pick **MkDocs Material** + the **mike** plugin for versioning.

Site lives at `https://tixl.app/help/`. The `/help/` prefix is set via `site_url`; mike inserts the version segment after it (`/help/v4.2/…`, `/help/latest/…`, `/help/main-dev/…`).

1. Add `mkdocs.yml` at the repo root:
   - `docs_dir: .help`
   - `site_name: TiXL Documentation`
   - `site_url: https://tixl.app/help/`
   - `use_directory_urls: true`
   - Theme: `material`, navigation tabs, light/dark toggle, instant navigation, and **`navigation.footer`** (gives every page auto "Previous / Next" links at the bottom, following the nav order).
   - **No explicit `nav:` tree** — ordering is driven by `mkdocs-awesome-nav` reading `.nav.yml` files. Root-level order lives in `.help/.nav.yml`; each section folder has its own `.nav.yml` overriding the ordering for that section and declaring its display title.
   - Plugins: `search`, `mike`, `awesome-nav`.
   - `extra.version.provider: mike` (exposes the version selector in the header).
   - `hooks:` points at `scripts/docs/op_autolinks.py` (Section 4c).
2. Add `requirements-docs.txt`: `mkdocs-material`, `mike`, `mkdocs-awesome-nav`, `pymdown-extensions`.
3. Add `.github/workflows/docs.yml`:
   - Triggered on push to `main` and on tags matching `v*`.
   - On `main` push: `mike deploy --push --update-aliases main-dev` (no alias swap with `latest`).
   - On stable tag (e.g. `v4.2.0`): `mike deploy --push <major>.<minor> --update-aliases latest`.
   - On pre-release tag (e.g. `v4.3.0-alpha.1`): `mike deploy --push <major>.<minor> --update-aliases alpha`.
   - Version selector shows `v4.1`, `v4.2 (latest)`, `v4.3 (alpha)`, `main-dev`.
4. DNS / CNAME: point `tixl.app` at the `gh-pages` branch, or — if the marketing site already owns `tixl.app` — publish docs at `/help/` via the same Pages deploy or a reverse proxy. Decide early so URLs in generated content (Section 4c's `index.json`) match.
5. Add a top-of-page "Edit on GitHub" link generated from `repo_url` + page path so contributions route to the repo, not the wiki.

Deliverable: a push to `main` rebuilds `main-dev` on the public site within ~60 s; a release tag promotes a new versioned build and optionally updates `latest`.

### 2. Prevent drift on migrated pages (wiki stays otherwise)

Only pages that have moved to `.help/` get retired — developer pages (`dev.*`, and any `help.*` we explicitly decide to keep on the wiki) stay editable there. No redirect banners on untouched pages.

1. **Transition banner on migrated pages.** ✅ *Done.* 36 wiki pages (all migrated `help.*` plus `dev.WritingCodeOps`, `Installation.md`, `(wip)-Your-first-C#-operator.md`) now start with:
   ```md
   <!-- TIXL_MOVED_BANNER -->
   > ⚠ **This page has moved.** It is now maintained in the main repo at [`.help/<path>.md`](https://github.com/.../.help/<path>.md).
   > Edits made on the wiki will be lost — please edit the source in the repo instead.
   > Once the docs site is live, this banner will be updated to link to the rendered page.
   ```
   The `TIXL_MOVED_BANNER` HTML-comment marker lets the banner-update script (see below) find and rewrite each banner in place.
2. **Update banners once the docs site is live.** Rewrite each banner's URL from the GitHub source link (`github.com/tixl3d/tixl/blob/main/.help/...`) to the rendered page (`tixl.app/help/latest/<section>/<page>/`). Reuse the same script that created the banners; the mapping table in Section 5 is the single source of truth. The HTML-comment marker makes this idempotent.
3. **Wiki sidebar update.** Edit `_Sidebar.md` to split the nav into "User docs (on `tixl.app/help/`)" and "Developer docs (here)", with the user-docs links pointing at the rendered site once it's live.
4. **No release-time sync from `.help/` back to the wiki.** The docs site replaces the wiki copy entirely — the banner carries anyone landing on an old URL forward. No second source to keep consistent.
5. **No wiki lockdown.** Developer pages still need to be editable. Restrict nothing; rely on the banner + sidebar to steer edits.

### 3. Fetch images

Most migrated pages reference images that currently point at `https://github.com/user-attachments/...` or the wiki's `images/` folder. Either path is fragile.

1. Inventory: scan `.help/**/*.md` for image references and produce three buckets:
   - GitHub `user-attachments` URLs → fetch, commit under `.help/<section>/images/` (or `.help/ui/images/timeline/`, co-located with the page).
   - `https://user-images.githubusercontent.com/...` — same treatment.
   - References already like `images/xxx.png` but with no local file yet → search `../t3.wiki/images/` and copy.
2. Compress: run every fetched PNG through `oxipng` / `pngquant` and every GIF through `gifsicle -O3`.
3. Rewrite: replace URLs in the markdown with relative paths, add descriptive alt text per STYLE.md.
4. Target dimensions: ≤1600 px wide; screenshots ≤500 KB, animations ≤2 MB.

**Currently unresolved image references** (local paths that need fetched images):

- `ui/TimeLine.md` — `images/timeline/anim-8.gif`, `image-9.png`, `anim-9.gif`, `anim-4.gif`, `image-7.png`, `anim-6.gif`, `image-8.png`, `anim-10.gif`. Sources in `../t3.wiki/images/`.
- `advanced/ConvertSDFs.md` — `images/convert-sdfs/image.png`, `anim.gif`, `anim-1.gif`. Likely under `../t3.wiki/help.ConvertSDFs/`.
- `setup/InstallLinux.md` — keeps several `github.com/user-attachments` URLs (works today, but should be localized).
- `setup/InstallMacOS.md` — 20+ `github.com/user-attachments` URLs.
- `setup/InstallDev.md` — one `github.com/user-attachments` URL.
- `general/LivePerformances.md` — two `user-images.githubusercontent.com` URLs.
- `general/AddingFonts.md` — two `github.com/user-attachments` URLs.
- `general/Introduction.md` — YouTube thumbnail (external, fine).
- `advanced/AddingFonts.md` — same as `general/AddingFonts.md` (see "Duplicates", below).
- `ui/PresetsAndSnapshots.md` — four `user-images.githubusercontent.com` URLs.
- `advanced/OSC.md` — several `github.com/tixl3d/tixl/assets/...` URLs.
- `advanced/SvgLineFonts.md` — one `user-images.githubusercontent.com` URL.

### 4. Migrate remaining user-facing pages

**Status: done (copies landed verbatim; banners added to wiki originals).** Content still needs a review/editorial pass per STYLE.md — the copy step was mechanical.

**Structural reorg (2026-04-18).** The `general/` / `setup/` / `ui/` split has been replaced with an audience-led hierarchy: `getting-started/`, `install/`, `using/`, `advanced/`, `contributing/`. Each section has a `README.md` that lists its pages plus a "Still to write" list. `.src/` holds raw source material (tutorial scripts, release-note drafts) used as input for future pages; it's excluded from the published site. Wiki banners were re-written to the new paths via the mapping-table script (Section 5). The rendered URLs in the mapping table have been updated accordingly.

- ✅ `help.Concepts.md` → `general/Concepts.md` *(page self-marks "needs work"; editorial pass due)*
- ✅ `help.VideoTutorials.md` → `general/VideoTutorials.md`
- ✅ `help.ExportVideos.md` → `ui/ExportVideos.md`
- ✅ `help.ExportExecutables.md` → `advanced/ExportExecutables.md`
- ✅ `help.KeyboardShortcuts.md` → `ui/KeyboardShortcuts.md`
- ✅ `help.CreatingNewOps.md` → `advanced/CreatingNewOps.md` *(merge candidate with `WritingCodeOps.md`; keep separate for now, resolve in editorial pass)*
- ✅ `help.ArtnetAndDMX.md` → `advanced/ArtnetAndDMX.md`
- ✅ `help.RealtimeRendering.md` → `advanced/RealtimeRendering.md`
- ✅ `help.ReportBugs.md` → `general/ReportBugs.md`
- ✅ `help.TixlChanges.md` → `general/MigratingFromTooll3.md` *(renamed during migration — Tooll3 is legacy; the page is a migration aid, not v3 documentation)*
- ✅ `help.SharingExampleProjects.md` → `general/SharingExampleProjects.md`
- ❌ `help.InstallationT3.md` — **not migrated.** Tooll3 install instructions belong on the wiki as historical reference, not in the TiXL help. Wiki copy's banner has been stripped so it stays a standalone legacy page.
- ✅ `help.RemoveStaticBackground.md` → `advanced/RemoveStaticBackground.md`
- ✅ `help.ShaderDevelopmentExample.md` → `advanced/ShaderDevelopmentExample.md`
- ✅ `help.SkillQuest.md` → `general/SkillQuest.md`
- ✅ `help.FaqDevOps.md` → `advanced/FaqDevOps.md`. Cross-link from `advanced/WritingCodeOps.md`; fold small duplicates in the latter as part of the editorial pass.
- `(wip)-Your-first-C#-operator.md` — bannered with "being merged into WritingCodeOps". **Still to do: perform the merge, then delete the wiki copy.**

Follow-up for every migrated page:

- Apply STYLE.md (voice, headings, relative links, operator-brackets convention).
- Fix internal `help.X` / `dev.X` links to relative paths or `[OperatorName]` where appropriate.
- Localize or re-link images (Section 3).
- Prune obsolete content (e.g. Tooll3-era instructions where TiXL v4 obsoleted them).
- Log per-page issues under Section 6.

### 4a. Stay on the wiki (developer-facing)

These stay editable on the wiki. They get **no redirect banners** and **no migration to `.help/`**. Keep the wiki sidebar listing them under a "Developer docs" heading.

- `dev.UsingDev.md`
- `dev.StandAloneBuilds.md`
- `dev.TixlVsTooll3.md`
- `dev.DevelopingOperators.md`
- `dev.IntegrationTests.md`
- `dev.UsingRenderDoc.md`
- `dev.WorkingWithGit.md`
- `dev.DebuggingPlayer.md`
- `dev.ContextVariables.md`
- `dev.UpdatingHomeTemplate.md`
- `dev.Contributing.md`
- `dev.ManualTestingPlan.md`
- `dev.CodingConventions.md`
- `dev.OperatorConventions.md`
- `dev.ChangingFilePathFormat.md`
- `dev.ProposedBreakingChangesForMain.md`
- `dev.VisualStudioCodeSetup.md`
- `dev.TransformGizmos.md`
- `dev.AudioRoadmap.md`
- `dev.IdeasForOperators.md`
- `dev.TixlReleaseIssues.md`

**Delete from the wiki** (obsolete or superseded):

- `dev.WikiConventions.md` — superseded by `.help/STYLE.md`.
- `dev.ContributingToTheWiki.md` — superseded by `.help/STYLE.md`.
- `dev.DocumentationPush.md` — superseded by this plan.

### 4b. Do not migrate, do not keep

- `meetup.*.md` — meet-up notes; host on the forum/Discord instead.
- `update.*.md`, `ReleaseNotes.*.md` — release notes; these belong in `CHANGELOG.md` in the main repo or on the Releases page.
- `UserTests.DeadMau5.md`, `MainDevNotes.md` — internal notes; archive or delete.
- `lib/` — covered by the operator reference pipeline (Section 4c).
- `operators/` — covered by the operator reference pipeline (Section 4c).

### 4c. Operator reference — integrate with the help pipeline

TiXL ships a generator ([Editor/Gui/UiHelpers/Wiki/ExportWikiDocumentation.cs](../../Editor/Gui/UiHelpers/Wiki/ExportWikiDocumentation.cs)) that walks every `Lib.*` `SymbolUi` and writes a markdown file per operator (662 pages today). The ground truth is the `SymbolUi.Description` plus input/output metadata on the symbol — edited in-editor, living next to the code. We make this the primary operator surface on the new site.

Existing external links into the wiki's `operators/` folder are believed to be minimal; we accept breaking them in exchange for a cleaner URL scheme that mike can version-alias going forward.

#### Target URL shape

Site root: `https://tixl.app/help/`.

Operator pages (via mike): `https://tixl.app/help/latest/ops/lib/field/adjust/PushPullSDF/` — with `latest` resolving to the current stable (`v4.2` or whichever). Per-version URLs (`…/help/v4.2/ops/…`) are the immutable ones that external links should use.

Rules for forming the path:

- Namespace segments lowercase (`Lib.field.adjust` → `lib/field/adjust`).
- Operator name keeps its PascalCase (`PushPullSDF`).
- Everything hyphen-free; the generator already guarantees identifier-safe names.

#### Retarget the exporter

1. `WikiOperatorsFolder` in `ExportWikiDocumentation.cs` changes from `t3.wiki/operators/` to `.help/operators/`.
2. Switch to a **nested folder layout** matching the URL:
   ```
   .help/operators/
     lib/
       field/
         adjust/
           PushPullSDF.md
       image/
         adjust/
           AdjustColors.md
   ```
   Implementation in the exporter: split `symbol.Namespace` on `.`, lowercase all segments, `mkdir -p` the chain, write `{SymbolName}.md` at the leaf.
3. Rewrite the "in [Lib.field.adjust](lib)" back-link to a relative link at the enclosing index (e.g. `../` to the namespace's `README.md`, or a MkDocs section index auto-created by the `awesome-nav` plugin).
4. Generate namespace index pages (`.help/operators/lib/README.md`, `.help/operators/lib/field/README.md`, …) listing the operators in that namespace with their short descriptions. Replaces the monolithic `lib.md` TOC that the old exporter wrote.
5. Emit `.help/operators/index.json` used by the auto-linker:
   ```json
   {
     "by_fullpath": {
       "Lib.field.adjust.PushPullSDF": {
         "url": "/help/ops/lib/field/adjust/PushPullSDF/",
         "summary": "Makes the incoming SDF volumes thicker or thinner..."
       }
     },
     "by_shortname": {
       "PushPullSDF": ["Lib.field.adjust.PushPullSDF"],
       "Value": ["Lib.numbers.float.basic.Value", "Lib.numbers.int.basic.Value"]
     }
   }
   ```
   `url` is site-absolute but version-agnostic — mike injects the version prefix (`/help/v4.2/ops/…`) at build time. Arrays hold all matches so the linker can flag ambiguity.

#### Wiki cutover

- No release-time mirror of `operators/` back to the wiki.
- `t3.wiki/operators/` can either stay as a frozen snapshot of its last auto-generated state (with banners pointing at `tixl.app/help/latest/ops/…`) or be deleted wholesale — they're generated artifacts, not authored content.
- Pick whichever feels safer; I'd lean on deleting once the new site is live and picking any stragglers up via banners on referring pages.

#### Auto-linking operator references in prose

Help pages already write `[AdjustColors]` or `[AudioReaction]` inline. Today these render as literal text. Make them into links.

1. Adopt a small MkDocs hook (preferred over a published plugin for deployment simplicity). Hooks live in `scripts/docs/op_autolinks.py`, registered in `mkdocs.yml` as `hooks: [scripts/docs/op_autolinks.py]`.
2. The hook loads `.help/operators/index.json` once at build start and runs in `on_page_markdown`:
   - Pattern: `\[([A-Za-z][A-Za-z0-9]*)\]` (bracketed PascalCase word) — but **only if the bracketed text is not already an explicit link** (not followed by `(`).
   - Resolve:
     - Bracket content matches a full path (e.g. `[Lib.image.color.AdjustColors]`) → link directly to that operator.
     - Dotted-but-partial path (`[lib.image.color.AdjustColors]` — lowercase prefix tolerated) → normalize to PascalCase `Lib.…` and resolve.
     - Short name with exactly one match in `by_shortname` → link to that operator.
     - Short name with >1 match → leave text as-is AND print a build warning `Ambiguous operator reference '[Value]' in .help/.../Foo.md; candidates: Lib.numbers.float.basic.Value, Lib.numbers.int.basic.Value. Qualify with the namespace.`
     - No match → leave text as-is (it's likely a prose reference, not an op).
   - Replace with a link to the `url` field from `index.json` (e.g. `[AdjustColors](/help/ops/lib/image/color/AdjustColors/)`). The URL is already site-absolute and version-agnostic; mike injects the version prefix at build time, so cross-version links work.
3. Optionally, on hover, show the operator summary via a MkDocs Material tooltip. Supported natively by Material's admonition extension; simplest is just a title attribute via HTML.
4. **Don't** link every mention — only bracketed ones. That keeps authoring control with the writer while making the common case zero-friction.

#### Content the exporter should grow into

The current output is serviceable (title, description, parameter table, output table). Low-hanging improvements tracked separately from this plan:

- Link parameter types to the type-system explanation page where useful.
- Link "Default example" / "Example operator" to an example project or screenshot when `SymbolUi.Examples` is populated.
- Emit a lightweight search-index sidecar that MkDocs `search` can consume alongside page content (operator names, parameter names, aliases).
- Render a "See also" section from the existing "usage" / "related" metadata if we add those fields to `SymbolUi`.

#### Ownership and update cadence

The exporter runs from inside TiXL (it needs the full symbol/UI graph). Options for when it runs:

- **Menu action** (today): author clicks a menu item, the files regenerate, they commit. Simple and good enough for now.
- **Pre-release step** (future): a CI job that boots TiXL in headless mode to regenerate `.help/operators/` — removes the "forgot to re-export" risk. Requires headless-startup work.

For this iteration we keep the menu action; add a note in `.help/STYLE.md` that the operator docs are generated, not hand-edited.

#### Writer-facing rules (to add to `.help/STYLE.md`)

- Refer to operators with brackets: `[AdjustColors]`, not `AdjustColors` or `the AdjustColors op`. The brackets tell the tooling "link this".
- Prefer the short name. Qualify with the namespace only when a short name is ambiguous (the build log tells you when).
- Don't hand-edit files under `.help/operators/` — they are regenerated from code.

### 5. Wiki-page → new-docs mapping (for redirect banners)

Produce a table that maps each **migrated** wiki filename to its new URL. Dev pages listed in Section 4a are not in this table; they get no banner.

Format below. URL column will point at rendered pages (`/help/latest/<section>/<page>/`) once the docs site is live; for now, banners use the GitHub source link.

| Legacy wiki page | `.help/` source | Rendered URL (once live) |
|---|---|---|
| `help.AddingFonts` | `advanced/AddingFonts.md` | `/help/latest/advanced/AddingFonts/` |
| `help.ArtnetAndDMX` | `using/ArtnetAndDMX.md` | `/help/latest/using/ArtnetAndDMX/` |
| `help.Backups` | `using/Backups.md` | `/help/latest/using/Backups/` |
| `help.Concepts` | `getting-started/Concepts.md` | `/help/latest/getting-started/Concepts/` |
| `help.ConvertSDFs` | `advanced/ConvertSDFs.md` | `/help/latest/advanced/ConvertSDFs/` |
| `help.CreatingNewOps` | `advanced/CreatingNewOps.md` | `/help/latest/advanced/CreatingNewOps/` |
| `help.ExportExecutables` | `using/ExportExecutables.md` | `/help/latest/using/ExportExecutables/` |
| `help.ExportVideos` | `using/ExportVideos.md` | `/help/latest/using/ExportVideos/` |
| `help.FAQ` | `using/FAQ.md` | `/help/latest/using/FAQ/` |
| `help.FaqBuildingContent` | `using/FaqBuildingContent.md` | `/help/latest/using/FaqBuildingContent/` |
| `help.FaqDevOps` | `advanced/FaqDevOps.md` | `/help/latest/advanced/FaqDevOps/` |
| `help.HowTixlWorks` | `getting-started/HowTixlWorks.md` | `/help/latest/getting-started/HowTixlWorks/` |
| `help.Installation` | `install/Installation.md` | `/help/latest/install/Installation/` |
| `help.InstallationT3` | *(not migrated — stays on wiki)* | — |
| `help.InstallDev` | `install/InstallDev.md` | `/help/latest/install/InstallDev/` |
| `help.InstallLinux` | `install/InstallLinux.md` | `/help/latest/install/InstallLinux/` |
| `help.InstallMacOS` | `install/InstallMacOS.md` | `/help/latest/install/InstallMacOS/` |
| `help.Introduction` | `getting-started/Introduction.md` | `/help/latest/getting-started/Introduction/` |
| `help.KeyboardShortcuts` | `using/KeyboardShortcuts.md` | `/help/latest/using/KeyboardShortcuts/` |
| `help.LivePerformances` | `using/LivePerformances.md` | `/help/latest/using/LivePerformances/` |
| `help.OSC` | `using/OSC.md` | `/help/latest/using/OSC/` |
| `help.OptimizingRenderingPerformance` | `using/OptimizingRenderingPerformance.md` | `/help/latest/using/OptimizingRenderingPerformance/` |
| `help.PresetsAndSnapshots` | `using/PresetsAndSnapshots.md` | `/help/latest/using/PresetsAndSnapshots/` |
| `help.RealtimeRendering` | `using/RealtimeRendering.md` | `/help/latest/using/RealtimeRendering/` |
| `help.RemoveStaticBackground` | `using/RemoveStaticBackground.md` | `/help/latest/using/RemoveStaticBackground/` |
| `help.ReportBugs` | `getting-started/ReportBugs.md` | `/help/latest/getting-started/ReportBugs/` |
| `help.ShaderDevelopmentExample` | `advanced/ShaderDevelopmentExample.md` | `/help/latest/advanced/ShaderDevelopmentExample/` |
| `help.SharingExampleProjects` | `using/SharingExampleProjects.md` | `/help/latest/using/SharingExampleProjects/` |
| `help.SkillQuest` | `getting-started/SkillQuest.md` | `/help/latest/getting-started/SkillQuest/` |
| `help.SvgLineFonts` | `advanced/SvgLineFonts.md` | `/help/latest/advanced/SvgLineFonts/` |
| `help.TixlChanges` | `getting-started/MigratingFromTooll3.md` | `/help/latest/getting-started/MigratingFromTooll3/` |
| `help.ui.TimeLine` | `using/Timeline.md` | `/help/latest/using/Timeline/` |
| `help.UsingCustomShaders` | `advanced/UsingCustomShaders.md` | `/help/latest/advanced/UsingCustomShaders/` |
| `help.VideoTutorials` | `getting-started/VideoTutorials.md` | `/help/latest/getting-started/VideoTutorials/` |
| `dev.WritingCodeOps` | `advanced/WritingCodeOps.md` | `/help/latest/advanced/WritingCodeOps/` |
| `Installation` | `install/Installation.md` | `/help/latest/install/Installation/` |

The banner-writer script (and a later URL-update script) takes this table as input. Banners already in place use the GitHub-source column; the URL-update pass will rewrite them to the rendered URL column once mike is publishing.

### 6. Per-page findings

These are issues the migration-review pass noted but did not fix inline. Each bullet is a small follow-up.

#### `general/FAQ.md`
- Rewrote intro to drop "Please contribute…" preamble, standardized on second person, fixed heading case. Content is otherwise okay.
- References two sections that aren't migrated yet: "Export Executables" and "Export Videos". Update links once Section 4 lands.
- The "Is there a standalone version?" section used to talk about .NET 4.7.1 and .NET 5 — removed, current install is bundled.

#### `general/Introduction.md`
- Extensive prose transcribed from a video walkthrough. Keeps tangential anecdotes ("I prefer working in full-screen mode…").
- Rewrite as a guided tour that references the actual current UI. Remove the video-transcript artifacts.
- Uses version "3.5" — update to current.
- Mentions Dark Mode / F12 focus mode; verify shortcuts still match current build.
- Trailing blank lines (line 190 onward).

#### `general/HowTixlWorks.md`
- Accurate conceptually but has typos: "sound more complicated", "information flows from left to right" (flow is from leaf inputs to output).
- The "Render Context" link is to a non-existent page — either write that page or reword.
- Mentions `[RandomCamera]`, `[Layer2d]` — operator names to verify still exist.

#### `general/FaqBuildingContent.md`
- Reads like a Q&A dump with author-voice ("❔") interjections. Decide whether to keep the format or rewrite as regular FAQ.
- The Particles question is orphaned — either move to a Particles page or expand.

#### `general/LivePerformances.md`
- Long — consider splitting into `LivePerformances.md` (setup) and `LiveTipsAndTricks.md` (WAKE example, MidiPipe, rtpMIDI).
- "Future Features" section reads as marketing/roadmap — either move to the community site or prune.
- The WAKE case study has fragile external image URLs.

#### `general/Backups.md`
- Short and clear. Mentions both Tooll3 and TiXL paths — fine as transitional info, but re-check whether the Tooll3 path is still relevant or should be dropped.

#### `general/AddingFonts.md` (duplicate of `advanced/AddingFonts.md`)
- **Duplicates were already deduped** at migration; only `advanced/AddingFonts.md` remains.
- **Decide the home:** users who just want to add a font aren't necessarily "advanced". Consider moving to `general/` and linking to the MSDF deep-dive. Currently in `advanced/`.

#### `setup/Installation.md`
- Rewritten; removed the broken `help.InstallationT3` link (page not migrated).
- Tooll3 install guide still needed — add as part of Section 4.

#### `setup/InstallDev.md`
- Mentions ".NET 4.7.1" in the Visual Studio section — this looks wrong given .NET 9 is the current target. Verify and fix.
- SDK version referenced as "v9.0.203 as of 2025-09-20" — replace with a link to the current `global.json` / tixl.props, not a hard-coded version.
- "B: Using a terminal window" vs "A: Using git-scm-software" are redundant — collapse.
- Last line still had `dev.UsingDev` / `dev.WritingCodeOps` links; `UsingDev` hasn't been migrated yet, linked form cleaned up.

#### `setup/InstallLinux.md`
- Title header standardized.
- "Legacy Tooll3 instructions" section below the divider — consider moving to a separate page or dropping.
- External image URLs; see Section 3.

#### `setup/InstallMacOS.md`
- Mixes Tooll3 and TiXL instructions in one document, which reads confused. Split into "Install TiXL" (primary) and a small "Install Tooll3 (legacy)" page.
- All images are external `github.com/user-attachments` URLs.
- Typo: "wintricks" should be "winetricks".

#### `ui/TimeLine.md`
- All image references updated to `images/timeline/*` paths — files not yet present. Fetch per Section 3.
- Does not yet document the new `SelectionRangeIndicator`, `SelectionArea`, or TimeWarp handles landed in 2026-04 (see `Plan_TimelineSelectionUI.md`). **Add these sections as the next content pass**, then include screenshots.
- Filename case: consider renaming `TimeLine.md` → `Timeline.md` (single word is the standard spelling everywhere in the UI).

#### `ui/PresetsAndSnapshots.md`
- Relatively clean. External image URLs need localization.
- Mentions APC Mini-specific controls — consider extracting to a dedicated controllers page when that section grows.

#### `advanced/UsingCustomShaders.md`
- Two `TODO: Add more details` markers. Either fill them in or delete the empty sections.
- Table of quaternion helpers is useful — consider extracting to an HLSL helpers reference.

#### `advanced/WritingCodeOps.md`
- Broken intro link `help.InstallDev.md` — fixed to `../setup/InstallDev.md`.
- Uses `private void Update(EvaluationContext context)` as if it's the Op base method — verify against current Operator template.
- "Install Fork" is a weak instruction — either link git-fork.com or rephrase.
- "I'm sure, Visual Studio could work, but I'm not sure how to set it up" — rewrite with concrete instructions or drop.
- `operators/Lib.numbers.float.basic.Modulo.md` link assumes the operator reference site is live; update once MkDocs is set up.

#### `advanced/OptimizingRenderingPerformance.md`
- Rewritten for tone, fixed "neglatable" typo. Broken `T3UserInterface#output-window` and `#appplication-header` fragment links removed — reword if/when the UI tour page ships.
- GTX 2070 reference is one person's machine — consider replacing with a relative metric ("~100× overdraw at 1080p").

#### `advanced/ConvertSDFs.md`
- Inline GLSL→HLSL walkthrough is useful but cramped. Consider splitting "Convert GLSL to HLSL" and "Using CustomSDF parameters" into separate H2s.
- Image refs rewired to `images/convert-sdfs/`. Files still need fetching.

#### `advanced/OSC.md`
- Section "ZigOSC (iOS app)" is empty — fill in or drop.
- Screenshots are external `github.com/tixl3d/tixl/assets/...` URLs. Localize.

#### `advanced/AddingFonts.md`
- Good content but very long. Split "Background information" (why MSDF) from "Converting fonts" (practical steps).
- "TODO" items implied in sections 18-20. Verify legacy method steps still work.
- External image URLs.

#### `advanced/SvgLineFonts.md`
- "Things I have tried…" section reads as personal notes. Either prune or reframe as "Known limitations".
- "In the long term" bullet is a placeholder — drop.

#### `README.md`
- Rewritten as an index of migrated pages. Add the missing-pages section once Section 4 lands.

### 7. Agent hooks

Add a line to `.claude/CLAUDE.md` under "Project Conventions":

> **User-facing changes update docs.** When you ship a UI or behavior change a user would notice, update the matching page under `.help/`. If a suitable page doesn't exist, either add one or flag it in `.agentic/Plans/Plan_UpdateHelp.md`. Docs follow `.help/STYLE.md`.

### 8. Order of operations

1. ✅ **Done.** Transition banners prepended to all 36 migrated wiki pages, pointing at the GitHub source. Section 2.1.
2. ✅ **Done.** All user-facing `help.*` pages copied into `.help/`. Section 4.
3. **Next:** update the wiki's `_Sidebar.md` to split into "User docs (on tixl.app/help/)" and "Developer docs". Section 2.3.
4. **Next:** set up MkDocs + mike locally and preview `.help/` rendered. Section 1 steps 1–3. Retarget operator exporter (Section 4c); run once to populate `.help/operators/`.
5. **Then:** fetch and localize images. Section 3.
6. **Then:** ship the GitHub Action and publish to `tixl.app/help/`. Section 1 step 3.
7. **Then:** **update wiki banners from GitHub-source URLs to rendered URLs** using the mapping table in Section 5. Drive via the banner script (the `TIXL_MOVED_BANNER` HTML-comment marker makes this idempotent). Section 2.2.
8. **Then:** editorial pass on each migrated page per STYLE.md — voice, structure, relative links, operator-brackets convention, obsolete-content pruning, image localization. Log per-page issues under Section 6.
9. **Then:** merge `(wip)-Your-first-C#-operator.md` into `WritingCodeOps.md`, delete wiki copy.
10. **Last:** review Section 4a/4b — delete obsolete wiki pages (`dev.WikiConventions`, `dev.ContributingToTheWiki`, `dev.DocumentationPush`), keep the rest editable.
