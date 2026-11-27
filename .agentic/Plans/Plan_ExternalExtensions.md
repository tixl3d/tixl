# External extensions: published from other repositories, on their own schedule

Status: **design** (2026-11-27, `main` 8e6bf051c). Nothing built. Concrete mid-term form of Phase 4 in
[Plan_OptionalPackages](Plan_OptionalPackages.md).

## Goals

1. An extension can live in its own repository (example projects, community packages) and be published there
   **whenever its author wants**, independent of editor releases. Some will iterate much faster than TiXL.
2. Settings → Extensions lists a **reviewed set** of such extensions. Getting listed is one pull request; that PR
   is the contribution process.
3. One documented way to do it: a template repository with a ready workflow.

Non-goals: live install (Plan_OptionalPackages Phase 2), dependencies between extensions, editor features as
extensions.

## Premise

Almost all commits are backwards compatible for graphs: projects from three years ago still load, and the rest
are fixable in minutes. An example extension must therefore **not** be tied to an editor build or minor version.
What would tie it is shipping a compiled DLL: a DLL is bound to Core's API, which is why precompiled packages
(Mediapipe, Ndi) carry a major.minor gate (`InstalledExtensions.cs`, `ExtensionCatalog.cs`).

## What exists today (verified 2026-11-27)

- The editor compiles operator projects **from source, in-process** (`OperatorCompiler/InProcessCompiler`, Roslyn,
  no .NET SDK; ships in 4.4). That is why old user projects load: they are recompiled against the running editor,
  and `CsProjectFile` migrations upgrade old csproj formats on load.
- Installed extensions load only as **precompiled read-only packages** from `<settings>/Extensions/<name>/`
  (`OperatorPackage.json` + DLL), skipped unless built for the editor's major.minor.
- `ExtensionCatalog` reads one index per exact editor version from the tixl release
  (`…/releases/download/v<version>/extensions.json`); absolute file URLs already work; size + SHA-256 checked.
- Example projects are mostly graphs: their `.cs` files are generated slot declarations (Examples: 294 files,
  ~6k lines; a forced rebuild takes 1.3 s). Recompiling them on install costs about a second.

## Design

### D0. Repository, extension, project

- A **project** is what exists today: one csproj, its namespaces, symbols and assets.
- An **extension** is the unit of release, version, install and update. It contains **one or more projects**,
  listed in its manifest, always released and installed together.
- A **repository** holds any number of extensions; hosting does not care.

Dependencies are allowed from a project to projects of the **same extension** and to bundled packages; never to
another extension (no resolver, no version ranges between extensions). This covers both ways of sorting
projects: projects that use each other form one multi-project extension, independent ones can be separate
extensions in the same repository. Merging projects into one is never required. The dependency helper (D8)
computes the groups: they are the connected components of the "uses operators or assets of" graph.

### D1. Two kinds of extension

| | Source extension (new) | Precompiled extension (today) |
|---|---|---|
| Contents | csproj, `Symbols/` (`.cs/.t3/.t3ui`), `Assets/` | Release build output, DLL, natives |
| Compiled | by the editor on install and after an editor update, cached | in CI |
| Editor versions | any version ≥ `minTixlVersion` | exactly one major.minor |
| For | examples, showcases, graph-based tools, plain C# operators | NuGet dependencies, natives (Mediapipe, Ndi) |
| Lives | its own repo, own release cadence | the tixl monorepo, packed per release |

Source extension = "a read-only project the editor downloaded for you". A package qualifies when the in-process
compiler accepts its csproj (`EvaluatedProject.UnsupportedReasons` empty); otherwise it needs the SDK or must be
precompiled, and packing refuses it.

### D2. Editor: installing and loading source extensions

- Install unpacks into `<settings>/Extensions/<name>/` as today; a marker in the package (`TixlExtensionKind:
  source` in the nuspec, `.meta/extension.json` in the folder) selects the source path.
- Loading goes through the same path as user projects: csproj migration, in-process compile into the folder's
  `.temp/`, load. **Read-only**: not offered for editing or saving, like other read-only packages; "Copy to my
  projects" is the way to modify one.
- The compile output is cached and keyed by editor version, so the cost (about 1–3 s per package) is paid once
  after install or after an editor update, not on every start.
- A failed compile skips only that extension, logs the first errors, and marks it "needs an update" on the
  Extensions page. Operators of Lib the graphs reference but the editor no longer has stay lossless placeholders,
  as for any old project.
- Updates: the Extensions page compares the installed version with the listed one and offers "Update".

### D3. Listing: a reviewed list, independent of editor releases

`extensions/registry.json` in this repo (or `tixl3d/extensions`, open question 1), fetched by the editor over
HTTPS and cached; the release ships a snapshot as offline fallback. The editor takes a **list of registry URLs**
(primary plus mirrors, e.g. a Codeberg mirror of the registry repo), tried in order, so the registry is not tied
to one host. It replaces the per-release
index for external extensions; official precompiled packages keep the per-release index.

Two ways to list, both via reviewed PR:

```json
{ "id": "pixtur.Examples", "tier": "community", "kind": "source",
  "source": "https://github.com/pixtur/T3Projects", "author": "pixtur", "licence": "MIT",
  "track": "https://example.org/tixl/pixtur.Examples/latest/extension.json" }
```

- **`track: <url>`** (trusted publishers: tixl3d showcase, maintainers): the editor reads the `extension.json`
  at that HTTPS URL (version, `minTixlVersion`, file URL, size, SHA-256). Publishing a new version means replacing
  that file, **no PR and no TiXL release**. Trust is in whoever controls the URL. Any static host works; no forge
  API is used. On GitHub the URL can be `…/releases/latest/download/extension.json`.
- **`pin`** (community default): the entry holds `version`, `url`, `size`, `sha256`, `minTixlVersion`. Updating
  is a PR that changes them; the hash makes the review stick. The template workflow prints the ready entry.

`minTixlVersion` hides versions that use operators or features an older editor lacks. A known-bad combination
gets `maxTixlVersion` in the entry; no other compatibility bookkeeping.

### D4. Publishing: a plain script, CI wrappers optional

**Packing reuses Share Project.** `ProjectPackageExporter` already writes a source-only `<PackageId>.<Version>.nupkg`
(project files in their normal layout, dependencies on shared packages recorded with their versions, cross-project
references reported). A source extension is that file plus `extension.json`. Needed: export several projects into
one package (the cross-project check then allows references between them), and a headless entry point (editor
command line or debug-bridge command) so CI and scripts can run it.

Everything a publisher needs is `tixl-publish-extension` (a script, no forge dependency): check against a TiXL
release (download the Linux tarball), run the export, update `extension.json` (D7). Uploading is the publisher's choice of host. Release-asset storage on forges and object storage
have different per-file and quota limits; a ~500 MB package can exceed what a forge accepts, so object storage
(S3-compatible buckets, a plain web server) is the safe fallback. The TiXL tarball used for the check is the
same file on every mirror; the script takes its URL as an argument.

CI wrappers around the script, maintained in the template repository:

- GitHub Actions: the reusable workflow below.
- Forgejo Actions (Codeberg, self-hosted Forgejo/Gitea): largely the same YAML; verify the reusable-workflow call
  and the release upload action before claiming support.
- Woodpecker (Codeberg CI) and GitLab CI: a short pipeline that runs the script; added when someone needs it.

GitHub template:

`tixl3d/extension-template` with a ~10-line workflow calling `tixl3d/tixl/.github/workflows/publish-extension.yml`
(`workflow_call`, triggered by `workflow_dispatch` or a tag):

1. Download the latest TiXL Linux tarball; build the project with it as `T3_ASSEMBLY_PATH`. This is a **check**,
   not the shipped artifact: it catches compile errors and runs a missing-operator scan (every `.t3` child
   `SymbolId` must exist in the package or in a bundled package of that release).
2. Pack source with the headless Share Project export into `<id>.<version>.nupkg` and add the version to
   `extension.json` (D7).
3. Attach both to the repo's release.

A scheduled run of step 1 against each new TiXL release gives authors early warning of the rare breaking
change ("your extension no longer compiles with 4.5.0.3"), without forcing a republish when nothing broke.

IDs: the `TiXL.` prefix is reserved for official packages; external ones use `<owner>.<Name>`.

### D5. Trust

Installing runs the extension's code (its `.cs` compiles into the editor process). Listing is a reviewed PR; a
a `track` entry is reserved for publishers who are trusted with that. Community entries are
labelled as not maintained by the TiXL team. Source extensions are easier to review than DLLs: the code is in the
package.

### D6. Licence

`PackageLicenseExpression` from the csproj goes into `extension.json` and is shown. Source extensions have no NuGet
references (the in-process compiler would refuse them), so the licence is the author's own plus their assets.

### D7. Versioning and update check

**Version numbers.** The package version is the csproj `VersionPrefix` (`major.minor.patch`), set by the author;
the editor does not bump it. Publishing refuses a version that already exists. Meaning, for users whose own graphs
use operators of the extension (symbols are referenced by GUID only, never by version):

- **major**: a symbol, input or output was removed (graphs that use it get placeholders);
- **minor**: symbols, inputs or outputs were added;
- **patch**: anything else (graph content, assets, fixes).

The publish check enforces this: it compares symbol, input and output GUIDs with the previous published version
and refuses a removal without a major bump. For pure example packs this rarely triggers.

**Compatibility with the editor** is one number per version, `minTixlVersion` (default: the csproj `EditorVersion`
at publish time; the author may lower it). `maxTixlVersion` exists only as a registry override for a known break.

**`extension.json` is the version history**, newest first, append only; files are immutable and carry the version
in their name:

```json
{ "id": "pixtur.Examples", "versions": [
  { "version": "1.3.0", "minTixlVersion": "4.4.0", "url": "…/pixtur.Examples.1.3.0.nupkg", "size": 512000000,
    "sha256": "…", "published": "2026-12-02", "notes": "Wake 2026 set" },
  { "version": "1.2.1", "minTixlVersion": "4.4.0", "url": "…", "size": 498000000, "sha256": "…",
    "published": "2026-11-28", "yanked": "broken asset paths, use 1.3.0" } ] }
```

The editor picks the newest version that is not yanked and whose `minTixlVersion` is not newer than itself, so
an older editor keeps getting the last version that works for it. Pinned registry entries use the same `versions`
list inline; a new version is a PR adding one element.

**When the editor checks:**

- When Settings → Extensions opens, and at startup in the background at most once per 24 h.
- Conditional requests (`ETag` / `If-Modified-Since`) against the registry URLs and each tracked
  `extension.json`, results cached in the settings folder, so an idle check costs a few small requests.
- Setting "Check for extension updates" (default on). Off: only when the page is opened. Its tooltip names the hosts
  that are contacted, since tracked extensions may live on hosts other than the registry.
- Offline or failing hosts: the cached state is shown with its age; never an error dialog.

**How updates show and apply:**

- The Extensions page shows `1.2.1 → 1.3.0`, the download size and `notes`; "Update" and "Update All". A small
  badge on the Extensions settings entry when updates exist; no modal, no notification at startup.
- Applying uses the existing pending mechanism: download, check size + SHA-256, stage in `.pending`, apply on
  restart. The previous version is kept in `.previous/<name>` for one-click "Roll Back".
- No automatic updates by default (packages can be hundreds of MB). Opt-in per extension.
- Installed version yanked: the page recommends the newest good version.
- After an editor update, source extensions recompile (D2). If one fails and a newer compatible version exists,
  the "needs an update" state offers it directly.

**Projects that use extension operators.** Share Project already records dependency versions. Opening such a
project with the extension missing or older offers "Install" / "Update" wherever unresolved operators are reported
(Plan_OptionalPackages Phase 3).

Official precompiled packages keep their per-release index: they update together with the editor.

### D8. Editor helpers for authors

Built on what exists: the Share Project analysis (`ProjectPackageExporter.TryAnalyze`: cross-project references,
unreferenced assets, dependency versions), `ChangeSymbolNamespaceCommand` (moves symbols to another project, undoable),
the namespace drag source in the symbol library, and the hub's project list (which already has an Archived section).

1. **Dependency check** (first, it decides the grouping). Per project or selection, lists everything outside
   "this extension + bundled packages": operators of other editable projects, of optional extensions (Ndi,
   Mediapipe), missing operators, and asset addresses of other packages (`pixtur.Wake:footage/…`). Shows the
   project-to-project graph and the suggested extension groups. Actions per finding: *Move operator into this
   project*, *Copy asset into this project and relink*, *Add project to this extension*. Also lists unreferenced
   assets (already computed) as a size saving.

   **Asset references** get the same treatment, from file/directory string inputs (the `StringInputUi.Usage` the
   stub `SymbolAnalysis.LogInvalidAssetReference` already filters on) resolved through `AssetRegistry`:
   *own / same extension / bundled* (fine), *other project* (copy and relink), *optional extension* (declare or
   replace), *absolute path* (`ExternalAssetsPackage`; not portable, copy into the project), *missing* (address does
   not resolve), and *bundled but leaving the bundle* (e.g. `Examples:user/…` once the showcase moves out). Copy
   actions show the size and warn above the host's per-file limit (GitHub: 100 MB). Addresses built in C# are
   invisible to this scan, as for the unreferenced-asset analysis. Shown as a badge in the symbol library next to
   "has invalid references", like the symbol check.

   First run on `tixl-examples-pixtur` (offline scan, 2026-11-27): all findings in `PlaygroundArchive2026`:
   7 addresses into `pixtur.Playground` (images, an `.obj`, a 112 MB `.mov`), one into the no longer existing
   `pixtur.Stammtisch`, one absolute Windows Dropbox path; 15 into bundled Examples (fonts, hdri, meshes, logo:
   all in parts that stay bundled) and Lib.
2. **Extensions in the project list.** Editable projects that belong to an extension get the extension's name and
   version as a badge (reuse the existing badge/marker look), plus a marker when they changed since the last release.
   Installed, read-only extensions get their own section like Archived, with the update badge from D7.
3. **Extension settings and "Release…".** A project context-menu entry in the hub: id, title, description,
   licence, member projects, `minTixlVersion`, version with *major / minor / patch* buttons where the suggested one
   comes from the symbol-interface diff (D7). Stored in the csproj of the extension's first project (properties
   from D2) plus a small manifest for the member list. *Release…* runs the dependency check, the compile check and
   the diff, then writes `<id>.<version>.nupkg` and the updated `extension.json` into a release folder and shows
   the registry snippet. **Uploading stays outside the editor** at first (host-neutral, D4); an optional
   configured upload command can follow.
4. **Moving namespaces between projects with their assets.** Dragging a namespace onto another project in the
   symbol library moves its symbols (existing command) **and** the assets only they use, rewriting asset
   addresses (`AssetRegistry.UpdateMovedAsset` handles moves inside a project today). Assets also used elsewhere:
   ask (copy or keep shared). One undoable command; refuses if a target project would gain a dependency the D0
   rules forbid. This is also how two projects are merged when an author wants that.

   **Move dialog** (requested 2026-11-27). Dropping a namespace on another project opens *Move Operators…*
   instead of moving at once. It lists the operators that move and, when some assets are used **only** by them,
   a dropdown for those assets:
   - *Move assets to a subfolder* (default when a whole project moves): keeps the source layout below a new folder
     named after the moved namespace or project (`Assets/Wake/footage/…`). This is the "bundle a project into a
     project collection" case: several projects end up side by side in one collection without their asset folders
     being lumped together or colliding.
   - *Move assets*: same relative paths in the target's Assets folder (default for a single namespace). Name
     collisions with different content are listed and block this option.
   - *Don't move assets*: addresses keep pointing at the source package; the asset check (helper 1) then reports
     them as *other package* if the source isn't shipped.
   Assets also used by operators that stay are listed separately (copy, or keep and reference). The dialog shows
   counts and sizes; the whole move, including file moves and address rewrites in every symbol that references
   the assets, is one undoable command.

5. **Manage Assets dialog** (later). Opened from the asset badge's *Resolve…* entry and from the Asset Library.
   Lists asset uses as a tree by project and namespace; every asset shows its state (ok, other package, absolute
   path, outside the assets folder, missing) and can be selected. Selected assets can be copied or moved into a
   project with their addresses rewritten (the same command as 4), relinked to another file, or removed from the
   inputs that use them.

Shipped first (2026-11-27): the symbol and asset checks in the symbol library (`SymbolAnalysis`: shipped-package
rule, asset categories; magenta document badge with tooltip; *Invalid asset references* filter; scan on first
open). Then the badge became a menu (left or right click): *Select Operators in Graph* (opens an instance in the focused
project, adding one to the current composition if needed, and selects the affected children), *List in Console*
(one warning per issue, linked to the child instance when the project uses the symbol) and *Select Assets in Asset
Library* (`AssetLibrary.ShowAndSelect`: clears search and type filters, selects, opens folders, scrolls). A
*Resolve…* entry is reserved for the Manage Assets dialog (5).

Order: 1 and 2 first (small, useful immediately while sorting projects), then 3, then 4 (largest: file moves,
asset relinking, undo across projects), then 5.

## Migration of the examples

1. **Stays bundled:** `Examples/Symbols/Lib` (133 `*Example` operator help graphs, found by name through
   `ExampleSymbolLinking`), `templates`, `howto`, `tutorial` and their assets.
2. **First external extension:** the showcase graphs `Examples/Symbols/user` + `Assets/user` (~220 MB, 122 MB
   zstd) into an official repo, e.g. `tixl3d/tixl-showcase`, with a `track` entry. Installer shrinks by ~120 MB.
   GUIDs unchanged (`git mv`, history-preserving split).
3. **pixtur's projects** from `pixtur/T3Projects` into one new repository, projects unchanged (D0). Archive ↔ Wake
   use each other's operators (8 / 2) and Video_44_Highlights uses 3 of Video_OutputSetup, so those form two
   multi-project extensions (or one, if preferred); ArtNet, DronesOfFury, ProjDesyAR, Tixl42_Release and VJCasual
   are independent. Playground (16 GB) and `_agentTests` stay out. Dedupe `Examples/Assets/user/wake` (41 MB)
   against `Wake/Assets`.

Developers keep opening these repos as user projects in a source build; nothing changes for Debug work.

## Phases

1. **Spike** (½ day): unpack a showcase project into a fresh settings folder and load it as a read-only project
   through the in-process compiler in a Release editor. Confirms the generated csproj compiles without the SDK and
   measures first-load compile time.
2. **Editor** (~3–4 days): source-extension install and load path (D2), registry fetch with cache and mirrors,
   `track` resolution, version selection, update check, Update / Update All / Roll Back, "needs an update" state
   (D7).
3. **Publishing** (~2 days): multi-project + headless Share Project export, `tixl-publish-extension` (compile
   check, missing-operator scan, symbol-interface diff, `extension.json` update), CI wrappers, template repo.
4. **Author helpers 1 + 2** (~2 days): dependency check, project-list badges (D8).
5. **Showcase repo** (~1 day), then **pixtur's repository** (~1 day; no merging needed).
6. **Author helpers 3 + 4** (~3–5 days): extension settings and Release…, namespace moves with assets (D8).
7. **Docs** (~1 day): "Publishing from your own repository" in `.help/docs/contributing/WritingExtensions.md`,
   the agent procedure in `.agentic/EXTENSIONS.md`, release notes, a manual test set for install/update.

## Open questions

1. **Host-neutral from the start (decided 2026-11-27):** some users don't want GitHub/Microsoft in the loop.
   Pinned entries are host-neutral already (HTTPS + SHA-256). Open: whether the official registry also gets a
   non-GitHub mirror (Codeberg) from day one.
2. **Where the registry lives:** this repo (reviewed like code) or `tixl3d/extensions` (community PRs never touch
   the main repo, and listing changes don't show up in the editor's history). Recommend `tixl3d/extensions`,
   since listings no longer move with releases.
3. **Who gets `track`:** only tixl3d-owned repos, or also named community maintainers.
4. **Precompiled community packages** (NuGet, natives): out of scope here; they need a build per minor and fit
   the nuget.org route of Plan_OptionalPackages D4.
