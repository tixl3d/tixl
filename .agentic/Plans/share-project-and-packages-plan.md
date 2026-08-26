# Share Project, Missing-Symbol Placeholders & Import — Plan

Distilled from design discussion, 2026-08-25. Scope: TiXL user-project sharing as a
self-contained artifact, graceful handling of missing symbols, and replace-style import.
NuGet feed distribution is a later phase built on the same artifact.

---

## 1. Decisions

**Artifact & scope**
- The share artifact is a **content-only `.nupkg`** (zip + manifest). Same artifact later
  serves feed publishing — no second format, no converter.
- **Source-only**: no binaries shipped. Packages compile at editor startup exactly like
  user projects. `<IncludeBuildOutput>false</IncludeBuildOutput>`, explicit `Content Include`
  for `**/*.cs`, `*.t3`, `*.t3ui`, resources; `<NoWarn>NU5128</NoWarn>`.
- Closure walk **excludes read-only packages** (Lib etc.). External symbols become
  dependency entries in the manifest, never vendored copies (symbol GUIDs are global —
  vendoring creates ambiguous resolution).
- **Cross-user-project references**: detect and error cleanly in v1. Rare; unsupported for now.

**Export options**
- Tree-shake unused operators / exclude unreferenced or large assets: **opt-in, off by default**.
- Reachability is under-approximate in a live tool (temporarily disconnected ops,
  GUIDs referenced from C# not the graph). A bloated export is annoying; a gutted one is
  an unreproducible bug report.
- Toggle labels show the computed gain: "Tree shake unused operators (12 symbols)",
  "Exclude unreferenced assets (3 files, 1.5 GB)". Self-documenting; zero-gain toggles
  need no thought.

**Import semantics**
- **Replace, not merge.** Delete the *entire* local project directory of that name, then
  unpack fresh. Not the intersection with incoming symbols — otherwise sender-deleted
  symbols survive as orphans with dangling references.
- Name collisions: username namespaces make them rare → simple "name taken, pick another"
  dialog. No automatic remapping in v1.
- Before deletion: scan for **inbound references** from the user's other projects into the
  target package's GUIDs. If found, report concretely ("3 of your operators use symbols
  from this project") and allow cancel.
- **Force a project-level backup** on import (v4 backups are change-triggered; import must
  always trigger one).
- Import-as-copy (side-by-side versions, forking examples) is deferred. It requires the
  GUID remapper — same primitive as "fork example into my project" — build it once,
  properly, when examples/packages land.

**Missing-symbol placeholders**
- An unresolved symbol GUID at load becomes a **placeholder child**, not a dropped one.
  Fixes an existing failure mode (Lib removals / failed compiles silently destroying graphs),
  and is what makes partially-satisfied imports legible.
- **Round-trip is the invariant**: load-with-missing → save must be lossless. If lossy,
  the feature is worse than deletion (silent, delayed corruption).
- **Parameters are kept as opaque JSON**: stash the child's input subtree unparsed,
  re-emit verbatim on save. No type/converter needed. On resurrection, run the blob
  through the normal converter.
- **Slots inferred from connections**: a referenced input/output GUID exists; type inferred
  from the resolved other end where available.
- Placeholders participate in graph model + UI, **never in evaluation** (skip instantiation;
  downstream evaluates as unconnected).
- **Resurrection**: diff real slot GUIDs against inferred; reconnect by GUID; drop only
  genuinely changed slots (existing slot-diff behavior, now correctly scoped).
- Status is **derived state, rendered by the UI** — distinct node style. No renaming to
  "params missing", no auto-inserted annotations (both mutate/serialize the user's project
  to signal a temporary condition, and leave cleanup debt after resurrection).
- **Missing-symbols panel**: aggregated across the project, searchable, grouped by owning
  package where the `.t3` records it, navigable to usages. This is the repair workflow and
  the import-diagnosis surface ("4 missing symbols, all from `bob.effects` — install v1.2?").

**Sequencing**
Export → Placeholders → Import. Import without placeholders must hard-reject unsatisfied
packages; placeholders relax that. Either ship them first, or ship import v1 with a
full-satisfaction requirement — decide up front, don't discover mid-build.

---

## 2. Pre-code checks (do these before Phase 1)

- [x] **C1 — Closure walk separability.** Answered 2026-08-26: `PlayerExporter`'s walk is
      *instance/slot-based* (starts at a live output, mixes in soundtrack + shared-asset player
      concerns) and not reusable. Share export got its own *symbol-level* walk instead
      (`ProjectPackageExporter`) — simpler, no refactor of `PlayerExporter` needed.
- [ ] **C2 — SymbolJson tolerance.** Can the read path stash an unparsed input subtree
      (opaque token) instead of failing/dropping? Determines the parameter-retention design.
- [ ] **C3 — Load-missing vs. compile-failure paths.** Does `EditorSymbolPackage` share a
      code path between "type missing at load" and "types unloaded after failed compile"?
      Placeholders should trigger on load only — compile failure keeps existing handling,
      or every failed build churns the graph through placeholder/resurrect cycles.
- [ ] **C4 — Resource addressing.** Confirm package-relative resource resolution and HLSL
      includes survive an unpack into a different project folder (should, given the
      package-relative resolver — verify with a shader-heavy project).
- [ ] **C5 — Community.** Float export on Discord before building. Highest-demand, least
      controversial piece; check nobody is mid-refactor in `PlayerExporter`.

---

## 3. Phase 1 — Share Project (export)

Context-menu action on the project list. Self-contained; useful the day it lands.

### Tasks (implemented 2026-08-26 in `Editor/UiModel/Exporting/ProjectPackageExporter.cs`,
`Editor/Gui/Graph/Dialogs/ShareProjectDialog.cs`; entry: project context menu in Hub)
- [x] Symbol-level closure walk (new, not extracted from PlayerExporter — see C1); stops at
      read-only/built-in package boundaries, records external packages as (identity, version)
      dependency entries in the nuspec.
- [x] Detect cross-user-project references → listed in dialog, export disabled.
- [x] Nupkg emission: nuspec (id = RootNamespace, version from csproj, dependency block,
      packageType `TixlProject`), OPC parts (`_rels`, `[Content_Types].xml`), files packed in
      project-relative layout at archive root so hand-unzip yields a working project folder.
- [x] Export dialog: destination folder, both opt-in toggles with computed gains
      (tree-shake reachability from home symbol; unreferenced-asset scan incl. fnt→png and
      hlsl includes). Zero-gain toggles are hidden.
- [x] Ignore list `ProjectPackageExporter.ExcludedSubdirectories` (bin, obj, .git, Export) —
      public so import reuses it.
- Manual test set: `.tests-manual/share-project-export.md`

### Acceptance
- Export a real project → unzip **by hand** into a clean install's project folder →
  editor compiles and opens it, shaders and resources resolve.
- Dependency block correctly lists read-only packages actually used.
- Cross-project reference case errors with an actionable message.

---

## 4. Phase 2 — Missing-symbol placeholders

### Tasks
- [ ] Tolerant load in `SymbolJson`: unresolved symbol GUID → placeholder child retaining
      GUID, name, position, connections (both directions), and the opaque input subtree.
- [ ] Verbatim re-emit on save.
- [ ] **Round-trip test: load project with missing symbols, save, byte-compare `.t3`/`.t3ui`.**
      Gate everything else on this passing.
- [ ] Slot inference from referencing connections (type from resolved end where present).
- [ ] Skip instantiation; ensure downstream evaluation treats placeholder outputs as
      unconnected without special-casing every consumer.
- [ ] Resurrection path: slot diff, reconnect by GUID, feed stored input JSON through the
      normal converter, drop only changed slots.
- [ ] Decide propagation: does "contains placeholders" bubble up the composition tree?
      (UI question mostly — far cheaper to design in now than retrofit.)
- [ ] Distinct node rendering for placeholder state (derived, nothing persisted).
- [ ] Missing-symbols panel: aggregate, search, group by owning package, navigate to usages.

### Acceptance
- Removing a Lib symbol, then opening + saving + reopening a project that used it, loses
  nothing; restoring the symbol restores connections and parameters silently.
- Panel lists all missing symbols with counts and navigation.
- Failed project compile does **not** route through the placeholder path (per C3).

---

## 5. Phase 3 — Import

Drop nupkg/zip onto editor (or file picker). Mostly mechanics once 1 + 2 exist.

### Tasks
- [ ] Read nuspec; validate structure.
- [ ] Name-collision check → rename dialog.
- [ ] Dependency check against installed packages; unsatisfied entries are allowed and
      will surface as placeholders (or hard-require satisfaction if shipping before Phase 2).
- [ ] Inbound-reference scan into target GUIDs → concrete warning, cancel option.
- [ ] Force backup → delete target project directory → unpack → regenerate csproj →
      compile → load.
- [ ] Post-import report: what landed, what's missing (link to panel).

### Acceptance
- v1 → edit → v2 reimport replaces cleanly, including sender-deleted symbols (no orphans).
- Import with a missing Lib dependency loads with placeholders and a clear diagnosis.
- Inbound-reference warning fires when (and only when) the user's own projects reference
  the package.
- Backup exists and restores after every import.

---

## 6. Phase 4 (later) — Feed distribution

Same artifact, registry behind it. Sketch only:
- Consumption: shadow csproj + `dotnet restore` → `project.assets.json` → materialize into a
  TiXL-managed root → `ProjectReference`s regenerated at startup (machine-specific paths are
  derived state from a manifest, never committed).
- Build cache keyed by **(package version, Core version)** — immutable versions ⇒ compile
  each package once, ever.
- Core API: semver discipline; breaking changes rare but must bump major.
- Large payloads (ML models, native deps): 250 MB nupkg cap ⇒ post-install fetch with hash
  verification; map `runtimes/<rid>/native/` into the `dependencies/` resolver; packaging
  buys *optional install*, not crash isolation — be explicit about that.
- Source packages compiled at startup = code execution before user interaction: curated
  feed + first-install confirmation, decided deliberately.
- Publish UX (API keys, one-button push) is where the remaining months live.

---

## 7. Effort sense (rough)

| Phase | Estimate | Dominant risk |
|---|---|---|
| 1 Export | days–2 wks | C1: closure walk entangled with flattening |
| 2 Placeholders | ~2–3 wks | round-trip fidelity; resurrection edge cases |
| 3 Import | ~1 wk | mostly mechanics; UX polish |
| 4 Feed | months, part-time | API stability commitment, publish UX, ML payloads |

Estimates assume familiarity with `ProjectSetup` / `SymbolJson`; the checks in §2 are what
keep them honest.
