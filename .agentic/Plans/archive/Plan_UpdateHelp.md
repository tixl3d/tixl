# Migrate documentation to `.help/` and publish via MkDocs

**Date:** 2026-04-18
**Status:** Site is live at [help.tixl.app](https://help.tixl.app/). Content migrated, IA set, MkDocs + Vercel pipeline shipped. Operator-reference exporter populates `.help/docs/operators/`; the MkDocs auto-linker rewrites `[OperatorName]` refs to relative `.md` paths. Most images are local under `.help/docs/images/`. Remaining work is editorial (imported-page clean-up, stray external image URLs, missing image fixes inline) and the wiki-side banner/sidebar cutover.

## Goal

Split documentation along a clear line:

- **`.help/` — user-facing.** Installation, UI, how-tos, FAQs, custom-shader / custom-operator authoring, live performance, advanced features. Single source of truth, ships with the code, publishes to `help.tixl.app`. Wiki pages that have been migrated here get a redirect banner pointing at the new URL.
- **GitHub wiki — developer-facing.** Building TiXL from source, coding conventions, CI, integration tests, renderdoc, git workflow, release process, ad-hoc design discussions. Stays editable on the wiki; no migration, no redirect banners for these pages.

The line: **if a user making motion graphics or live visuals would want to read it, it goes into `.help/`. If only someone opening `t3.sln` would want to read it, it stays on the wiki.**

## Current state

- **Live site:** `https://help.tixl.app/` on Vercel, building from `main` on every push.
- **Layout:**
  ```
  .help/
  ├── README.md              contributor orientation (not published)
  ├── mkdocs.yml             site config
  ├── requirements-docs.txt  Python deps
  ├── vercel.json            install / build / output for Vercel
  ├── docs/                  mkdocs docs_dir — all markdown
  │   ├── index.md  STYLE.md  .pages
  │   ├── getting-started/  install/  using/  advanced/  contributing/  operators/
  ├── site/                  build output (gitignored, inside .help/)
  ├── .venv/                 build venv (Vercel + local; gitignored)
  └── .src/                  raw source material for future pages (not published)
  ```
- **IA:** five top-level sections (`getting-started`, `install`, `using`, `advanced`, `contributing`) plus `operators` placeholder. Each section has a `README.md` listing pages present *and* topics still to write. Root-level + per-section `.pages` files drive ordering via the `awesome-pages` plugin.
- **Pages migrated (36 total):** all user-facing `help.*` pages from the wiki — verbatim copies with banners on the wiki originals. Not yet edited for STYLE.md.
- **Wiki banners:** each migrated wiki page starts with a `<!-- TIXL_MOVED_BANNER -->` block pointing at the GitHub source path. Script is idempotent; the next pass rewrites them to `help.tixl.app/<section>/<page>/` URLs.
- **Operator reference:** placeholder `docs/operators/index.md` in place; exporter retarget still pending (Section 4c).

## Work plan

### 1. MkDocs on Vercel (help.tixl.app) ✅ shipped

Live at **https://help.tixl.app/** on Vercel, project name `tixl-help`, deploying from `main` on every push.

**What's in the repo:**

- [`.help/mkdocs.yml`](../../.help/mkdocs.yml) — Material theme, edit-on-GitHub action, `navigation.footer` (auto Previous / Next), `awesome-pages` plugin reading `.pages` files for ordering. `docs_dir: docs`, `site_dir: site` (both inside `.help/`). `strict: false` for now; re-enable when editorial pass is clean. The operator auto-linker hook is commented out, waiting on the exporter retarget.
- [`.help/requirements-docs.txt`](../../.help/requirements-docs.txt) — `mkdocs-material>=9.5`, `mkdocs-awesome-pages-plugin>=2.9`, `pymdown-extensions>=10.0`. No `mike`.
- [`.help/vercel.json`](../../.help/vercel.json) — venv-based build, required because Vercel's Python is externally-managed (PEP 668) under `uv`:
  ```json
  {
    "installCommand": "python3 -m venv .venv && .venv/bin/pip install -r requirements-docs.txt",
    "buildCommand": ".venv/bin/mkdocs build",
    "outputDirectory": "site",
    "framework": null
  }
  ```
- Vercel project **Root Directory** = `.help`. All paths in `vercel.json` are relative to that.
- `.gitignore` excludes `.help/site/` and `.help/.venv/`.

**Local preview:**

```bash
pip install -r .help/requirements-docs.txt
mkdocs serve -f .help/mkdocs.yml
```

Opens at `http://127.0.0.1:8000/`.

**Versioning — deferred.** `mike` versions docs on a `gh-pages` branch, which Vercel doesn't speak. When versioning actually matters, options are:

- Move docs to GitHub Pages + `mike` natively, or
- Hand-roll per-version subdirectories via a build script that commits to a `docs-publish` branch Vercel serves.

Until then, `help.tixl.app` serves current `main`. External links that need immortality can pin to a git commit via "Edit on GitHub".

**Optional follow-up — unify onto `tixl.app/help/...`.** Add a `vercel.json` rewrite on the Figma (`tixl.app`) project:

```json
{ "rewrites": [{ "source": "/help/:path*", "destination": "https://help.tixl.app/:path*" }] }
```

Then update `site_url` in `mkdocs.yml` back to `https://tixl.app/help/`. Cost to switch: trivial.

**Known surface quirks** (not blockers, future polish):

- MkDocs Material prints a 2.0 announcement banner in terminals; cosmetic only.
- ✅ `strict: true` flipped 2026-04-18 — broken links / missing images now fail the Vercel build instead of sliding through as warnings.

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
2. **Update banners once the docs site is live.** ✅ *Done (2026-04-18).* 35 banners rewritten to `help.tixl.app/<section>/<page>/` by [`.help/scripts/wiki/update_banners.py`](../../.help/scripts/wiki/update_banners.py), which parses Section 5's mapping table as its source of truth. Idempotent — safe to re-run after any table update. Push to the `t3.wiki` remote pending.
3. **Wiki sidebar update.** ✅ *Done (2026-04-18).* `_Sidebar.md` now splits into a user-docs group (all links → `help.tixl.app`) and a developer-docs group (wiki-relative `dev.*` links). Push to the `t3.wiki` remote pending.
4. **No release-time sync from `.help/` back to the wiki.** The docs site replaces the wiki copy entirely — the banner carries anyone landing on an old URL forward. No second source to keep consistent.
5. **No wiki lockdown.** Developer pages still need to be editable. Restrict nothing; rely on the banner + sidebar to steer edits.

### 3. Images

**Status (2026-04-18):** No longer a dedicated phase. The bulk of local images have been copied into a central `.help/docs/images/` tree (e.g. `images/MosaicEffect/`, `images/MigrateFromT3/`, `images/timeline/`). Any remaining missing-image warnings are folded into the editorial pass (Section 6) — fix them inline on the page you're reviewing rather than batching.

Residual items to handle opportunistically:

- **External URLs still in pages** (`github.com/user-attachments/…`, `user-images.githubusercontent.com/…`, `github.com/tixl3d/tixl/assets/…`). These render today; only risk is GitHub rotating them later. Localise if you're editing the page anyway; don't open PRs just for this.
- **Compression.** Once the image tree stabilises, run `oxipng`/`pngquant` over PNGs and `gifsicle -O3` over GIFs in `.help/docs/images/`. One-shot cleanup, not blocking anything.
- **Path convention.** Existing pages use site-absolute `/images/...` paths. These work while the site serves from the domain root (`help.tixl.app`), but will break if it's ever rewritten to a sub-path (e.g. `tixl.app/help/*` per Section 1's optional follow-up). If that flip happens, sweep to relative `../images/...` — trivial script pass.
- **Target dimensions** when adding new images: ≤1600 px wide; screenshots ≤500 KB, animations ≤2 MB.

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
- ✅ `(wip)-Your-first-C#-operator.md` — merged into `advanced/WritingCodeOps.md` (2026-04-18). Added the "TiXL *is* the SDK" framing up top and a second "Combine existing operators into a new type" walkthrough alongside the existing Duplicate-based path. Dropped the canned-JSON network snippet and the 2024 screenshot URLs. Wiki copy deleted.

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

- ✅ `dev.WikiConventions.md` — superseded by `.help/STYLE.md`. Deleted 2026-04-18.
- ✅ `dev.ContributingToTheWiki.md` — superseded by `.help/STYLE.md`. Deleted 2026-04-18.
- ✅ `dev.DocumentationPush.md` — superseded by this plan. Deleted 2026-04-18.

All three are staged in the `t3.wiki` checkout; push pending alongside the banner/sidebar updates.

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

Site root: `https://help.tixl.app/`.

Operator pages: `https://help.tixl.app/ops/lib/field/adjust/PushPullSDF/`. No version prefix in v1 (mike is deferred per Section 1). When versioning lands, per-version URLs (`help.tixl.app/v4.2/ops/…`) will be the immutable ones for external links.

Rules for forming the path:

- Namespace segments lowercase (`Lib.field.adjust` → `lib/field/adjust`).
- Operator name keeps its PascalCase (`PushPullSDF`).
- Everything hyphen-free; the generator already guarantees identifier-safe names.

#### Retarget the exporter

**Status (2026-04-18):** Done. [`ExportWikiDocumentation.cs`](../../Editor/Gui/UiHelpers/Wiki/ExportWikiDocumentation.cs) now writes `.help/docs/operators/` with the nested layout, per-namespace `README.md` indices, and an `index.json` sidecar. Editor builds green. Still pending: the user runs **Documentation → Export as WIKI** to populate the folder from the live symbol graph, commits the output, and we flip the placeholder `operators/index.md` into final form (already updated to link into the generated tree).

1. ✅ `WikiOperatorsFolder` in `ExportWikiDocumentation.cs` changes from `t3.wiki/operators/` to `.help/docs/operators/` (constant renamed to `HelpOperatorsFolder`).
2. ✅ Switch to a **nested folder layout** matching the URL:
   ```
   .help/docs/operators/
     lib/
       field/
         adjust/
           PushPullSDF.md
       image/
         adjust/
           AdjustColors.md
   ```
   Implemented by `NamespaceToRelDir`: split `symbol.Namespace` on `.`, lowercase all segments, `mkdir -p` the chain, write `{SymbolName}.md` at the leaf.
3. ✅ Rewrote the back-link to `*in [Lib.field.adjust](README.md)*` — resolves to the namespace index in the same folder.
4. ✅ Generated namespace index pages (`.help/docs/operators/lib/README.md`, `.help/docs/operators/lib/field/README.md`, …) listing sub-namespaces and the operators at each level with their short descriptions. Replaces the monolithic `lib.md` TOC.
5. ✅ Emit `.help/docs/operators/index.json` used by the auto-linker:
   ```json
   {
     "by_fullpath": {
       "Lib.field.adjust.PushPullSDF": {
         "url": "/ops/lib/field/adjust/PushPullSDF/",
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
- `t3.wiki/operators/` can either stay as a frozen snapshot of its last auto-generated state (with banners pointing at `help.tixl.app/ops/…`) or be deleted wholesale — they're generated artifacts, not authored content.
- Pick whichever feels safer; I'd lean on deleting once the new site is live and picking any stragglers up via banners on referring pages.

#### Auto-linking operator references in prose

**Status (2026-04-18):** Done. Hook lives at [`.help/scripts/docs/op_autolinks.py`](../../.help/scripts/docs/op_autolinks.py) and is registered in [`mkdocs.yml`](../../.help/mkdocs.yml). Silent no-op until `index.json` exists, so the Vercel build keeps succeeding even before the exporter has been run.

Help pages already write `[AdjustColors]` or `[AudioReaction]` inline. Today these render as literal text. The hook turns them into links.

1. ✅ MkDocs hook at `.help/scripts/docs/op_autolinks.py`, registered in `mkdocs.yml` under `hooks:`.
2. The hook loads `.help/operators/index.json` once at build start and runs in `on_page_markdown`:
   - Pattern: `\[([A-Za-z][A-Za-z0-9]*)\]` (bracketed PascalCase word) — but **only if the bracketed text is not already an explicit link** (not followed by `(`).
   - Resolve:
     - Bracket content matches a full path (e.g. `[Lib.image.color.AdjustColors]`) → link directly to that operator.
     - Dotted-but-partial path (`[lib.image.color.AdjustColors]` — lowercase prefix tolerated) → normalize to PascalCase `Lib.…` and resolve.
     - Short name with exactly one match in `by_shortname` → link to that operator.
     - Short name with >1 match → leave text as-is AND print a build warning `Ambiguous operator reference '[Value]' in .help/.../Foo.md; candidates: Lib.numbers.float.basic.Value, Lib.numbers.int.basic.Value. Qualify with the namespace.`
     - No match → leave text as-is (it's likely a prose reference, not an op).
   - Replace with a link to the `url` field from `index.json` (e.g. `[AdjustColors](/ops/lib/image/color/AdjustColors/)`). The URL is already site-absolute. If versioning is later re-enabled via mike, it injects the version prefix at build time without the hook caring.
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

Format below. URL column will point at rendered pages (`/<section>/<page>/`) once the docs site is live; for now, banners use the GitHub source link.

| Legacy wiki page | `.help/` source | Rendered URL (once live) |
|---|---|---|
| `help.AddingFonts` | `advanced/AddingFonts.md` | `/advanced/AddingFonts/` |
| `help.ArtnetAndDMX` | `using/ArtnetAndDMX.md` | `/using/ArtnetAndDMX/` |
| `help.Backups` | `using/Backups.md` | `/using/Backups/` |
| `help.Concepts` | `getting-started/Concepts.md` | `/getting-started/Concepts/` |
| `help.ConvertSDFs` | `advanced/ConvertSDFs.md` | `/advanced/ConvertSDFs/` |
| `help.CreatingNewOps` | `advanced/CreatingNewOps.md` | `/advanced/CreatingNewOps/` |
| `help.ExportExecutables` | `using/ExportExecutables.md` | `/using/ExportExecutables/` |
| `help.ExportVideos` | `using/ExportVideos.md` | `/using/ExportVideos/` |
| `help.FAQ` | `using/FAQ.md` | `/using/FAQ/` |
| `help.FaqBuildingContent` | `using/FaqBuildingContent.md` | `/using/FaqBuildingContent/` |
| `help.FaqDevOps` | `advanced/FaqDevOps.md` | `/advanced/FaqDevOps/` |
| `help.HowTixlWorks` | `getting-started/HowTixlWorks.md` | `/getting-started/HowTixlWorks/` |
| `help.Installation` | `install/Installation.md` | `/install/Installation/` |
| `help.InstallationT3` | *(not migrated — stays on wiki)* | — |
| `help.InstallDev` | `install/InstallDev.md` | `/install/InstallDev/` |
| `help.InstallLinux` | `install/InstallLinux.md` | `/install/InstallLinux/` |
| `help.InstallMacOS` | `install/InstallMacOS.md` | `/install/InstallMacOS/` |
| `help.Introduction` | `getting-started/Introduction.md` | `/getting-started/Introduction/` |
| `help.KeyboardShortcuts` | `using/KeyboardShortcuts.md` | `/using/KeyboardShortcuts/` |
| `help.LivePerformances` | `using/LivePerformances.md` | `/using/LivePerformances/` |
| `help.OSC` | `using/OSC.md` | `/using/OSC/` |
| `help.OptimizingRenderingPerformance` | `using/OptimizingRenderingPerformance.md` | `/using/OptimizingRenderingPerformance/` |
| `help.PresetsAndSnapshots` | `using/PresetsAndSnapshots.md` | `/using/PresetsAndSnapshots/` |
| `help.RealtimeRendering` | `using/RealtimeRendering.md` | `/using/RealtimeRendering/` |
| `help.RemoveStaticBackground` | `using/RemoveStaticBackground.md` | `/using/RemoveStaticBackground/` |
| `help.ReportBugs` | `getting-started/ReportBugs.md` | `/getting-started/ReportBugs/` |
| `help.ShaderDevelopmentExample` | `advanced/ShaderDevelopmentExample.md` | `/advanced/ShaderDevelopmentExample/` |
| `help.SharingExampleProjects` | `using/SharingExampleProjects.md` | `/using/SharingExampleProjects/` |
| `help.SkillQuest` | `getting-started/SkillQuest.md` | `/getting-started/SkillQuest/` |
| `help.SvgLineFonts` | `advanced/SvgLineFonts.md` | `/advanced/SvgLineFonts/` |
| `help.TixlChanges` | `getting-started/MigratingFromTooll3.md` | `/getting-started/MigratingFromTooll3/` |
| `help.ui.TimeLine` | `using/Timeline.md` | `/using/Timeline/` |
| `help.UsingCustomShaders` | `advanced/UsingCustomShaders.md` | `/advanced/UsingCustomShaders/` |
| `help.VideoTutorials` | `getting-started/VideoTutorials.md` | `/getting-started/VideoTutorials/` |
| `dev.WritingCodeOps` | `advanced/WritingCodeOps.md` | `/advanced/WritingCodeOps/` |
| `Installation` | `install/Installation.md` | `/install/Installation/` |

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

**Done:**

1. ✅ Transition banners prepended to all 36 migrated wiki pages, pointing at the GitHub source. Section 2.1.
2. ✅ All user-facing `help.*` pages copied into `.help/docs/`. Section 4.
3. ✅ IA reorg: `getting-started` / `install` / `using` / `advanced` / `contributing` / `operators`. Section 4 note.
4. ✅ MkDocs Material + awesome-pages plugin + `.pages` ordering files. Section 1.
5. ✅ `mkdocs.yml`, `requirements-docs.txt`, `vercel.json` in place; root directory kept clean (all docs infrastructure under `.help/`).
6. ✅ **Vercel deploy live at `help.tixl.app`.** Section 1 (shipped).

**Next session, in priority order:**

1. ✅ **Wiki banners updated** — 35 migrated pages now point at `help.tixl.app/<section>/<page>/` via [`.help/scripts/wiki/update_banners.py`](../../.help/scripts/wiki/update_banners.py), driven by the Section 5 mapping table. Idempotent (verified with a second run). Still needs a push to the `t3.wiki` remote. Section 2.2.
2. ✅ **Wiki `_Sidebar.md` split** — user docs now link to `help.tixl.app`; developer docs stay editable on the wiki. Still needs a push. Section 2.3.
3. **Editorial pass on migrated pages** per STYLE.md — voice, relative links, `help.X` wiki links → proper paths, operator-brackets convention, obsolete-content pruning. Each cleared page gets one fewer warning; flip `strict: true` in `mkdocs.yml` once the count hits zero. Section 6.
4. ✅ **Retarget the operator exporter** — [ExportWikiDocumentation.cs](../../Editor/Gui/UiHelpers/Wiki/ExportWikiDocumentation.cs) writes `.help/docs/operators/` with the nested layout plus `index.json`. User runs **Documentation → Export as WIKI** once to populate the folder, then commits.
5. ✅ **Enable the operator auto-linker hook** — registered in `mkdocs.yml`, sourced from [`scripts/docs/op_autolinks.py`](../../.help/scripts/docs/op_autolinks.py). Section 4c.
6. ✅ **Merged `(wip)-Your-first-C#-operator.md`** into `advanced/WritingCodeOps.md` (2026-04-18). Wiki copy deleted; push pending with the other wiki changes. Section 4.
7. ✅ **Pruned obsolete wiki pages** (2026-04-18) — `dev.WikiConventions`, `dev.ContributingToTheWiki`, `dev.DocumentationPush` deleted; push pending. Section 4a.

(Image handling — previously Section 3 as a dedicated phase — is now folded into the editorial pass. See Section 3 for the residual items.)

**Later, when it matters:**

- Versioning via `mike` or hand-rolled per-version subdirs. Section 1 "Versioning — deferred".
- Optional `vercel.json` rewrite on the Figma `tixl.app` project to expose docs at `tixl.app/help/*` in addition to `help.tixl.app`. Section 1 "Optional follow-up".
- Batch image compression (`oxipng`/`pngquant`/`gifsicle`) over `.help/docs/images/`. Section 3 residual.
- Content sources: turn `.help/.src/` scripts (ShaderGraph, Making-of Ashborn, release notes, v3.9 video) into proper pages; extract selected meet-up segments via Whisper. Discussed in chat, not yet in a dedicated section.
