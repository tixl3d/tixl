# Writing extensions

An extension is an operator package that isn't part of the default TiXL install. Users install it from
**Settings → Extensions** with a tick and a restart. This page explains when a contribution should be an extension,
how to set one up, how to work on it, and how to split a large piece of work into pull requests that can be reviewed.
It also explains how to let an AI assistant do most of the mechanical work.

## When something should be an extension

Most operators belong in the default install. Make an extension when one of these applies:

- **Licence.** The code or a library it ships isn't compatible with TiXL's MIT licence, for example GPL code or a
  proprietary SDK runtime. TiXL's default install must stay free software so Linux distributions can package it.
- **Size.** It brings large native libraries or models, roughly anything above 20 MB.
- **Platform or hardware.** It only works on one platform, or needs hardware or a service most users don't have:
  laser controllers, a specific camera, an online account.
- **Niche.** It serves a specialised workflow, such as volumes (VDB), rigid-body physics or AI image generation.

Ableton Link, NDI and MediaPipe are the first extensions; their folders under `Operators/` are good references.

## What an extension can contain

An extension is a normal operator package: C# operators with their `.t3`/`.t3ui` files, shaders, assets such as
models or presets, native libraries, NuGet packages, and examples that show how to use it.

What it **can't** contain are changes to anything outside its own folder: `Core`, the editor, other operator
packages, the build, or CI. If your extension needs such a change, for example a new data type in `Core` or a fix in
`Lib`, that change becomes its own small pull request that is reviewed on its own merits first. The same is true for
editor features like new windows: they can't be installed as extensions.

## Where extensions live

Official extensions live in the main repository under `Operators/<Name>/`. They compile against the `Core` of the
same commit, and the release build packs them for every TiXL version, so they never fall behind.

A build from source doesn't load them by default, so they cost no startup time for anyone working on something
else. To work on one, set the environment variable `TIXL_DEBUG_EXTENSIONS` before starting TiXL, for example in the
run configuration of your IDE:

```
TIXL_DEBUG_EXTENSIONS=BepuPhysics
```

Several names are separated by `;`, and `all` loads every extension. The log names the extensions it skipped.

Extensions in their own repositories are planned, but need a published TiXL SDK package first.

## Creating an extension

1. **Copy a small extension.** `Operators/AbletonLink/` is the smallest. Copy the folder to `Operators/<Name>/`, rename
   the `.csproj` and `.csproj.DotSettings` files, and in the `.csproj` set a new `RootNamespace` and a new
   `PackageId` (a fresh GUID).
2. **Describe it** in the `.csproj`:
   ```xml
   <TixlDistribution>Optional</TixlDistribution>
   <TixlPlatforms>win-x64;linux-x64;osx-arm64</TixlPlatforms>
   <PackageLicenseExpression>MIT</PackageLicenseExpression>
   <Title>Rigid-Body Physics</Title>
   <Description>Simulate rigid bodies with BepuPhysics: bodies from points and meshes, forces, contacts and joints.</Description>
   ```
   `TixlPlatforms` lists only the platforms it really works on. `PackageLicenseExpression` is the strictest licence
   of your code and everything you ship with it, as an SPDX expression (`MIT AND Apache-2.0`, `GPL-2.0-or-later`).
3. **Add it to the solution:** `dotnet sln t3.sln add Operators/<Name>/<Name>.csproj --solution-folder Operators`.
4. **Native libraries** go into `Operators/<Name>/dependencies/`, their licence texts into `dependencies/licenses/`.
5. **NuGet packages:** add the `PackageReference`, and list the packages whose assemblies your extension must carry
   in `<TixlCopyLocalPackages>`, for example `BepuPhysics;BepuUtilities`. Don't use `CopyLocalLockFileAssemblies`:
   it copies all of TiXL's own dependencies into your extension and makes the download hundreds of megabytes larger.
6. **Operators** go into `Symbols/`. When you move existing operators, move their files unchanged: the GUIDs in
   them are how projects refer to them.
7. **Examples** that use your operators live in your extension, not in `Examples/`. The shipped examples are part of
   the default install and would show your operators as missing.

## Testing an extension

- **In the editor:** build the solution, start TiXL with `TIXL_DEBUG_EXTENSIONS=<Name>` and use your operators in a
  real project.
- **As users get it:** build the solution in Release, then pack and install it like a user would:
  ```
  python3 Installer/pack-extensions.py <folder>
  ```
  Start a Release build with `TIXL_EXTENSIONS_INDEX=<folder>` and install the extension from Settings → Extensions.
  Check that it loads and that its operators work, on every platform you listed.
- **Unit tests** go into a test project of their own, `Tests/<Name>.Tests/`, so they don't end up in the extension.

## Splitting a large contribution

A pull request is reviewed by a person who has to understand all of it. Large ones wait, and stacked ones, where each
pull request contains all the previous ones, can't be reviewed one by one at all. So:

1. **Talk first.** Describe the extension on Discord or in an issue before you open pull requests. It often turns
   out that part of it belongs in the default install, or that a planned `Core` change isn't needed.
2. **Enabling changes first, each on its own.** Fixes and additions in `Core`, the editor or `Lib` that your
   extension needs go into small pull requests against `main`, each explaining what it enables. They must not change
   the behaviour of the default install beyond what they describe.
3. **One extension per pull request.** It adds files under `Operators/<Name>/`, the line in `t3.sln`, its docs and its
   tests, and nothing else. Branch it from `main`, not from your previous pull request.
4. **Leave out** your local setup (IDE settings, your own agent or `CLAUDE.md` changes), CI changes, version numbers
   and unrelated clean-ups. If they're useful, they're separate pull requests.
5. **Keep the hand-written part reviewable.** Generated or vendored code (bindings, third-party sources) is fine, but
   say which files it is and where they come from, so the review can skip them.

Large binary files (models, native libraries above about 10 MB) stay in the repository forever. Ask before adding
them; often they can be downloaded when the extension is installed instead.

## Using an AI assistant

The repository contains instructions for AI coding assistants in `.agentic/`. The procedure for extensions is
[`.agentic/EXTENSIONS.md`](https://github.com/tixl3d/tixl/blob/main/.agentic/EXTENSIONS.md): how to sort a large branch
into enabling changes and extensions, how to build each extension, and how to check it. Start the assistant with
something like:

> Read `.agentic/AGENT_INSTRUCTIONS.md` and `.agentic/EXTENSIONS.md`. My branch `volume-physics` adds NanoVDB volume
> operators and BepuPhysics rigid-body operators. Make a plan to split it into pull requests following that guide.
> Don't change any files yet.

Check the plan before you let it continue: which changes it calls enabling, which extensions it proposes and what it
wants to leave out. Then let it work one pull request at a time, and read every diff before you push. You are the
author of the pull request, and reviewers will ask you, not the assistant.

## See also

- [Extensions](../using/Extensions.md): how users install and remove them
- [Code structure](CodeStructure.md)
- [Style conventions](StyleConventions.md)
