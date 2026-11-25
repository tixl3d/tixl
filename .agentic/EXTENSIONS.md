# Extensions: Procedure for Agents

How to turn operator work into **extensions** (optional operator packages, installed by users from Settings →
Extensions) and how to split a large contribution into pull requests that can be reviewed. Agent-neutral; the human
version is `.help/docs/contributing/WritingExtensions.md`. Read `AGENT_INSTRUCTIONS.md` first; its rules still apply.

## What an extension is (facts to rely on)

- An operator package under `Operators/<Name>/` whose `.csproj` says `<TixlDistribution>Optional</TixlDistribution>`.
  Release builds don't bundle it; `Installer/pack-extensions.py` packs it into `TiXL.<Name>.<version>.nupkg` for the
  GitHub release, and the editor installs it into `<settings>/Extensions/<Name>/`.
- A source (Debug) build skips it unless `TIXL_DEBUG_EXTENSIONS` names it (`;`-separated, or `all`). Its operators then
  count as "expected missing": graphs keep them, the log mentions them only at debug level.
- It can contain operators (`.cs`/`.t3`/`.t3ui` in `Symbols/`), helpers (`Utils/`, `internal`), shaders and assets
  (`Assets/`), native libraries (`dependencies/`), NuGet packages and examples.
- It can't change anything outside its folder. `Core`, `Editor`, other packages (including `Lib` and `Examples`),
  build files and CI are out of reach, and editor features (windows, menus) can't be extensions at all.
- References: `Operators/AbletonLink/` (smallest, native library + GPL), `Operators/Ndi/` (NuGet + native runtime),
  `Operators/Mediapipe/` (models, NuGet with natives).

## Procedure for splitting a large contribution

Work in this order and **stop for the user's confirmation after steps 1 and 2.**

### 1. Inventory

```
git diff --stat main...<branch>
git diff --name-status main...<branch>
```

Sort every changed file into one of these groups and show the result as a table (path pattern, group, reason):

| Group | Contents | Goes into |
|---|---|---|
| A enabling | changes in `Core/`, `Editor/`, `Lib`, other packages, build files that the extensions need | one small PR each, against `main` |
| B extension | operators, shaders, assets, natives, examples of one domain | one PR per extension |
| C docs | `.help/` pages and operator descriptions for B | the PR of the extension they describe |
| D independent | fixes or features unrelated to the extensions | their own PRs, or left out |
| E drop | local setup: IDE files, the contributor's own `CLAUDE.md`/agent edits, CI workflow edits, version bumps, generated help indices, reformatting | not submitted |

When a group A change is large or touches public API in `Core`, flag it: it needs a discussion with the maintainers
before any code is moved.

### 2. Decide the extensions

- **One domain per extension:** "NanoVDB volumes" and "BepuPhysics rigid bodies" are two extensions even if they
  arrived together.
- **Extension, not default install,** when the licence isn't MIT-compatible, the download is large (about 20 MB+),
  it works on one platform or needs special hardware or a service, or the domain is niche. Otherwise propose the
  default install (`Lib` or a bundled package) and say why.
- **No dependencies between extensions.** If two share code, the shared part is group A, or the two are one extension.
- **Bundled code never uses an extension.** No C# in `Lib`/`Examples`/other bundled packages may reference an
  extension's types. Graphs may use extension operators only inside the extension itself.

List per extension: name, operators, assets and natives with sizes, NuGet packages with licences, platforms, the
resulting `PackageLicenseExpression`, and which group A PRs it depends on.

### 3. Create each extension

On a branch from `main` (`git switch -c ext/<name> main`), take the files from the contribution branch with
`git checkout <branch> -- <paths>`, not by cherry-picking commits of a stacked branch.

1. Copy `Operators/AbletonLink/` to `Operators/<Name>/`. Rename `AbletonLink.csproj` and `.csproj.DotSettings`.
   Remove the copied operator, native library and licence files.
2. In the `.csproj`: new `RootNamespace`, a new GUID as `PackageId`, `VersionPrefix` 1.0.0, then
   `TixlDistribution` = `Optional`, `TixlPlatforms` (only platforms that work), `PackageLicenseExpression` (SPDX,
   the strictest of code, natives and NuGet packages), `Title`, `Description` (one sentence, what it does).
3. Operators into `Symbols/` with unchanged `.cs`/`.t3`/`.t3ui` files. **Never regenerate GUIDs**: projects refer to
   operators, inputs and outputs by them. The namespace may stay as it was.
4. Native libraries into `dependencies/`, licence texts into `dependencies/licenses/`. For a GPL or LGPL binary also
   write a notice naming the exact source (repository and commit), like `AbletonLinkDLL-SOURCE.md`.
5. NuGet packages: `PackageReference`, plus `<TixlCopyLocalPackages>` naming each package whose assemblies or natives
   the extension must carry. Never `CopyLocalLockFileAssemblies` (it copies all of the editor's dependencies).
6. Examples that use the extension's operators go into the extension's own `Symbols/`.
7. `dotnet sln t3.sln add Operators/<Name>/<Name>.csproj --solution-folder Operators`.
8. Tests into `Tests/<Name>.Tests/`, never inside `Operators/<Name>/` (the package compiles every `.cs` in its
   folder).
9. Large binaries (above about 10 MB) stay in git forever: list them for the user and ask before adding them.

### 4. Verify each pull request

```
dotnet build t3.sln -c Debug -p:EnableWindowsTargeting=true      # no new warnings
dotnet build t3.sln -c Release -p:EnableWindowsTargeting=true
python3 Installer/check-licences.py                              # bundled packages unaffected
python3 Installer/pack-extensions.py /tmp/ext                    # the extension packs
dotnet test <Tests/<Name>.Tests>                                 # when it has tests
```

Then in the editor (see `DEBUG_PROTOCOL.md`; experiments only in `_agentTests`):
- Source build with `TIXL_DEBUG_EXTENSIONS=<Name>`: the package loads, its operators instantiate and render.
- Source build without it: the log line names it as skipped, and nothing else warns about it.
- Release build with `TIXL_EXTENSIONS_INDEX=/tmp/ext`: Settings → Extensions lists it; installing loads it.

For group A pull requests: the default install behaves as before, verified with the steps above for the areas they
touch.

### 5. Pull requests

- Every PR is based on `main` and contains one group A change or one extension. A PR that needs another names it in
  its description instead of containing it.
- The description says what it adds, what it depends on, which platforms were tested and how, and which files are
  generated or vendored (with their origin).
- Don't push or open PRs unless the user asks; prepare branches and descriptions and let the user decide.

## Rules and their reasons

- **Extensions don't change shared code.** Reviewers can merge an extension knowing it can't break anything outside
  its folder; that guarantee is what makes large extensions reviewable at all.
- **No `CopyLocalLockFileAssemblies`.** Mediapipe shrank from 171 MB to 107 MB by switching to
  `TixlCopyLocalPackages`; the default copies the editor's OnnxRuntime and more into every package.
- **Keep GUIDs.** A changed GUID silently disconnects every project that used the operator.
- **Honest licence metadata.** `PackageLicenseExpression` and the files in `dependencies/licenses/` are what users see
  before installing, and what decides whether an extension can be published.
- **Examples inside the extension.** Bundled examples using extension operators show them as missing to everyone
  who hasn't installed the extension.
