# External extensions: build and publish from other repositories

Status: **design** (2026-11-27, `main` 8e6bf051c). Nothing built. Concrete mid-term form of Phase 4 in
[Plan_OptionalPackages](Plan_OptionalPackages.md); uses GitHub releases instead of nuget.org (D4 stays the
long-term option, entries below carry URL + hash so moving the files later changes nothing for the editor).

## Goals

1. An extension can live in its own repository (pixtur's example projects, community packages) and be built and
   published there, on its own schedule, without touching the tixl repo or its release run.
2. A tixl release lists a **reviewed set** of such extensions next to the official ones in Settings → Extensions.
   Getting listed is one pull request against this repo; that PR is the contribution process.
3. There is one documented way to do it: a reusable workflow plus a template repository, so contributors copy
   instead of invent.

Non-goals: live install (Phase 2 of Plan_OptionalPackages), nuget.org hosting, dependencies between extensions,
editor features as extensions.

## What exists today (verified 2026-11-27)

- `Installer/pack-extensions.py` packs every `Operators/*/*.csproj` with `TixlDistribution=Optional` into
  `TiXL.<Name>.<TiXL version>.nupkg` plus one `extensions.json`; only `windows-build.yml` runs it and attaches the
  files to the tixl release.
- `ExtensionCatalog` reads `github.com/tixl3d/tixl/releases/download/v<exact version>/extensions.json`
  (`TIXL_EXTENSIONS_INDEX` overrides). File URLs resolve with `new Uri(indexUri, entry.File)`, so **absolute URLs
  to another repo's release already work**. Size and SHA-256 are checked on download.
- Installing checks only that the package's `EditorVersion` matches the editor's **major.minor**.
- `IndexEntry` has no version, author or source; the shown version is the index's `tixlVersion`.
- User projects already build outside this repo: their csproj references `$(T3_ASSEMBLY_PATH)/Core.dll`,
  `Graphics.dll`, `Graphics.Compat.dll`, `Logging.dll`. The Linux release tarball is the full Release editor
  folder (those DLLs at the root, Lib under `Operators/`), so an extracted tarball is a usable SDK today.
- Releases come several times a day (4.4.0.13–15 on 2026-11-26). **External packages cannot be rebuilt per
  release**; they are built once per major.minor and every tixl release re-lists them.

## Design

### D1. Build: reusable workflow, SDK = the released tarball

`tixl3d/tixl/.github/workflows/build-extension.yml` (`on: workflow_call`). Inputs: `project` (csproj path),
`tixl-version` (e.g. `4.4.0.15`; the newest release of the wanted minor). Steps:

1. Download and unpack `tixl-<version>-linux-x64.tar.gz` from the tixl release; `T3_ASSEMBLY_PATH` = its root.
2. `dotnet build <project> -c Release`.
3. Fetch `Installer/pack-extensions.py` at the tag `v<tixl-version>` and pack the project (see D2).
4. Attach `.nupkg` + a one-entry `extension.json` to the calling repo's release (tag = package version).

Extension repos call it from a ~10-line workflow (`workflow_dispatch` for "rebuild the examples once in a while",
`release` for tagged versions). Linux runners are enough: the operator DLL is `net10.0` AnyCPU. Packages with
natives keep them per RID in `dependencies/` as today; building natives stays the extension's own business.

Later: replace the tarball download with a `TiXL.Sdk` reference package (Plan_OptionalPackages Phase 4).

### D2. Packing outside the repo

`pack-extensions.py` gains `--project <csproj>` (pack exactly this one, wherever it is) next to today's scan.
In that mode:

- ID comes from a new csproj property `TixlExtensionId` (e.g. `pixtur.Examples`); the `TiXL.` prefix is
  reserved for official packages and refused here.
- Version is the csproj `VersionPrefix`, not the TiXL version; the built-for TiXL major.minor is read from the
  build's `OperatorPackage.json` and written to the entry as `tixlVersion`.
- Entry gains `version`, `author` (`Authors` property), `source` (repository URL, `RepositoryUrl`).
- Verify before relying on it: the editor's csproj migrations (`CsProjectFile`) keep unknown properties when they
  rewrite a user project. They use `GetOrAddProperty`/`SetOrAddProperty`, so they should.

### D3. Listing: a reviewed list in this repo, merged into each release's index

`Installer/external-extensions.json` (later the seed of `packages/catalog.json` from Plan_OptionalPackages D5):

```json
{ "id": "pixtur.Examples", "tixlVersion": "4.4", "tier": "community",
  "url": "https://github.com/pixtur/T3Projects/releases/download/v1.3.0/pixtur.Examples.1.3.0.nupkg",
  "size": 312000000, "sha256": "…", "title": "…", "description": "…", "licence": "MIT",
  "platforms": [], "author": "pixtur", "source": "https://github.com/pixtur/T3Projects" }
```

- The entry pins **URL + SHA-256**: a re-uploaded release asset with other content fails the download check,
  so what the reviewer approved is what users get. Updating an extension is a PR that changes URL + hash; the
  extension repo's workflow prints the ready-to-paste entry.
- `windows-build.yml`, after `pack-extensions.py`: for each entry whose `tixlVersion` matches this release's
  major.minor, download, verify size + hash, run the check from D4, append to `extensions.json` with the absolute
  URL. The files stay in the extension's repo (no re-upload, no 2 GB-per-asset or release-size concern).
- **An entry that fails is left out of that release's index with a warning; it does not fail the release.** A
  third party must not be able to block a tixl release.
- When the minor changes (4.4 → 4.5), entries for 4.4 drop out automatically until their repo rebuilds against
  4.5 and updates the PR. A scheduled job may open an issue listing entries without a current-minor build.

### D4. Compatibility check in CI

A small console tool (`Installer/CheckExtension`, run with `dotnet run`) loads the package DLL into a
`MetadataLoadContext` together with the release's Core, Graphics, Graphics.Compat, Logging and Lib, and fails
if any referenced type or member does not resolve. That is the real risk of building once per minor: a Core API
change inside a minor. It also warns (does not fail) when a `.t3` child `SymbolId` is found neither in the
package nor in a bundled package, i.e. graphs that would load with missing operators.

The editor integration tests (TCP bridge) need a GPU and are not used here.

### D5. Editor

Small and backwards compatible:

- `IndexEntry`: optional `version`, `tixlVersion`, `author`, `source`, `tier`. Show the entry's version instead of
  the index's when present; show author and a source link; mark `community` entries.
- Name derivation already keeps the full ID for non-`TiXL.` packages (`<settings>/Extensions/pixtur.Examples/`).
- Install from File stays the path for unlisted packages.

### D6. Trust

Installing runs the extension's code with the user's rights. Listing is a reviewed PR (code, licence, size,
what the package contains), the hash pin makes the review stick, and `community` entries are labelled as not
maintained by the TiXL team. External DLLs are not code-signed; Windows does not check signatures on loaded
assemblies, so this affects only reputation, not loading.

### D7. Licence

The packed entry's `licence` comes from `PackageLicenseExpression` as today. The reviewer checks it against the
package's NuGet references and natives; `check-licences.py` can be pointed at the package csproj in the reusable
workflow so the contributor sees problems before the PR.

## Migration of the examples

1. **Stays bundled in this repo:** `Examples/Symbols/Lib` (133 `*Example` operator help graphs, found by name
   through `ExampleSymbolLinking`), `templates`, `howto`, `tutorial` and the assets they use.
2. **First external extension (proves the pipeline):** the showcase graphs in `Examples/Symbols/user` with
   `Assets/user` (~220 MB, 122 MB zstd), moved to an official-tier repo, e.g. `tixl3d/tixl-showcase`. Keeps the
   GUIDs (`git mv`, then history-preserving split); installer shrinks by ~120 MB.
3. **pixtur's projects** (`pixtur/T3Projects`): Archive ↔ Wake use each other's operators (8 / 2), and
   Video_44_Highlights uses 3 of Video_OutputSetup. Extensions cannot depend on each other, so these become **one
   package** `pixtur.Examples` (one csproj, symbols merged, GUIDs unchanged, namespaces change to
   `pixtur.Examples.*`), or the links are cut first. Playground (16 GB) and `_agentTests` stay out.
   `Examples/Assets/user/wake` (41 MB) may duplicate `Wake/Assets`; dedupe while moving.

Developers keep opening these as user projects in a source build; nothing changes for Debug work.

## Phases

1. **Tixl side** (~2 days): `pack-extensions.py --project`; `build-extension.yml`; `external-extensions.json`
   + merge step in `windows-build.yml`; `IndexEntry` fields and display. Test with `TIXL_EXTENSIONS_INDEX`
   pointing at a locally merged index.
2. **Compatibility check** (~1 day): `CheckExtension` tool, wired into the merge step.
3. **Showcase repo** (~1 day): split, first listing PR, remove from bundled Examples.
4. **pixtur.Examples** (~1–2 days): merge the dependent projects, workflow, listing PR.
5. **Contributor docs + template** (~1 day): `tixl3d/extension-template` (csproj with the properties above,
   10-line workflow, README); a "Publishing from your own repository" section in
   `.help/docs/contributing/WritingExtensions.md`; the agent procedure in `.agentic/EXTENSIONS.md`.

## Open questions

1. **Where the list lives:** this repo (recommended: reviewed like code, versioned with the release that ships
   it) or a separate `tixl3d/extensions` repo (community PRs never touch the main repo).
2. **Listing without a release:** with the index attached per release, a new listing appears with the next
   release. Given the release cadence that seems fine; otherwise the editor would also read the list from `main`
   (Plan_OptionalPackages D5).
3. **Bundling on request:** "shipped with releases" here means *listed* in the release's index. Putting an
   external package into the installer itself would mean the release run fetches and bundles it, and the
   Bundled licence allowlist applies. Recommend keeping that for packages moved into this repo.
