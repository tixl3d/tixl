# Dependency health: not getting stuck again

**Status:** Snapshot taken 2026-10-04. No work scheduled — this records what we are exposed to and two
decisions already made: stay on ImGui.NET 1.91.6, and migrate the FFmpeg bindings later. Both are deliberately
deferred behind the Linux/macOS port and the installer.
**Belongs to:** [Plan_CrossPlatformV5](Plan_CrossPlatformV5.md) in spirit — SharpDX is the case that
motivated it.

SharpDX was abandoned in 2018 and its types reached ~150 files, so replacing it became a multi-month facade
project rather than a package swap. The lesson is not "pick livelier packages" — it is that **cost of
replacement is set by blast radius, not by the dependency's health.** A dead library behind a seam is an
annoyance; a live one whose types are everywhere is still a hostage situation waiting to happen.

## Snapshot

Age of the newest published version of each third-party package, 2026-10-04. Age alone is not a verdict: a
small, finished library can sit still for years and be fine. Read it together with the blast radius.

| Age | Package | What it does | Blast radius |
|---|---|---|---|
| 12.8 y | Rug.Osc 1.2.5 | OSC input | one operator family |
| 8.1 y | **SharpDX 4.2.0** | D3D11 | ~150 files — being replaced |
| 6.0 y | DirectShowLib.Standard | video device input | Windows-only, one op |
| 5.7 y | unsplasharp.api | stock images | one op |
| 5.5 y | LibTessDotNet | tessellation | few ops |
| 4.4 y | NDILibDotNet6 | NDI | Windows-only, few ops |
| 4.4 y | CommandLineParser | CLI args | Editor/Player startup |
| 4.3 y | StbImageWriteSharp | image write | narrow |
| 3.9 y | Palink.ArtNet | DMX | one op |
| 2.5 y | **Sdcb.FFmpeg 7.0.0** | FFmpeg bindings | VideoServices, ~3.9k LOC |
| 1.7 y | **ImGui.NET 1.91.6.1** | the entire UI | everything in Editor |
| 1.6 y | Delaunator | triangulation | one op |
| <1 y | Vortice.Vulkan, ppy.SDL3-CS, SkiaSharp, Svg.Skia, Sentry, OnnxRuntime, SharpGLTF, OpenCvSharp4, StbImageSharp, FFmpeg.LGPL, TqkLibrary natives | | |

Re-run it; the script reads the csproj files and asks nuget.org, so it needs no build and no SDK. It also
prints the pinned version beside the newest one, which is the second signal worth reading — `ManagedBass`,
`Vortice.Vulkan`, `ppy.SDL3-CS` and `SkiaSharp` are all healthy upstream but a few releases behind here.

```
python3 .agentic/Plans/dependency-age.py        # third-party only
python3 .agentic/Plans/dependency-age.py --all  # including Microsoft.*, System.*, test packages
```

## What the snapshot says

- **The dormant ones are mostly harmless.** Rug.Osc, ArtNet, DirectShowLib, unsplasharp and friends are
  small, finished, and each sits behind one operator. If one breaks, the blast radius is that operator.
- **Two are worth watching because of reach, not age.** `ImGui.NET` is every pixel of the editor, and it has
  been quiet for ~1.7 years while Dear ImGui upstream has not. `Sdcb.FFmpeg` is the one we have already
  decided about, below.
- **The FFmpeg *native* packages are alive** — `FFmpeg.LGPL` publishes daily, `TqkLibrary` is at 9.0.2. We
  pin old versions of both deliberately, because the *bindings* are stuck at FFmpeg 7.0. nuget.org packages
  are immutable (owners may unlist, never delete, and an exact-version restore still resolves), so a pinned
  version does not rot. Hosting our own build would buy little against that.

## Decision: FFmpeg bindings (2026-10-04)

Migrate `Sdcb.FFmpeg` → `FFmpeg.AutoGen`, in two steps, when the port and installer are done. **Not a fork.**

| | Sdcb.FFmpeg | FFmpeg.AutoGen |
|---|---|---|
| License | LGPL-3.0 | MIT |
| Last push | 2025-03-24 | 2026-08-22 |
| Tracks | FFmpeg 7.0 | FFmpeg 9 |

Sdcb also has a real defect: it declares FFmpeg's `uint8_t *data[4]` / `int linesize[4]` parameters as
by-value structs where the C signature decays them to pointers. The Windows x64 convention hides the
mismatch; on Linux `av_image_copy_to_buffer` receives a garbage picture height — a different one each run.
FFmpeg.AutoGen declares the same parameters by reference, which is correct. `SoftwareFrameConverter`
works around it today; see the remark there.

Forking Sdcb would mean owning a header-driven code generator to fix something another project already got
right, and the result would stay LGPL-3.0 inside an MIT codebase.

**Step 1** — swap to FFmpeg.AutoGen **7.1.1**, same native libraries, no behaviour change. FFmpeg.AutoGen is
raw P/Invoke with no object model, so the wrapper types VideoServices leans on (`Frame`, `FormatContext`,
`CodecContext`, `Codec`, `MediaStream`, `Packet`, `VideoFrameConverter`, `MediaDictionary`, `CodecResult`)
have to be written here. `VideoServices.Tests` and the visual suite are the safety net.

**Step 2** — bump to 9.x separately. Linux can then use the distro's FFmpeg with no bundled natives at all
(Arch already ships 9.0.1); Windows moves to a current build.

Two steps, because in one migration a binding bug and a codec change look identical.

## Looked at more closely (2026-10-04)

**Rug.Osc — leave it.** OSC 1.0 has not changed since 2002, so "it works" is a real argument here in a way it
never was for SharpDX, which wrapped a platform that kept moving. There is also nowhere better to go: every
.NET OSC library is dormant (OscCore 2019 and Unity-shaped, CoreOSC 2019 one release, Bespoke.Osc 2021 one
release, SharpOSC not on nuget.org). Usage is six files and already behind `IoServices/OscConnectionManager`.
The one thing that is *not* true is "we can always fork it": bitbucket.org/rugcode/rug.osc is 404 since
Bitbucket dropped Mercurial. Re-publications exist (`mgamache/Rug.Osc.Core`, a netstandard port). Cheap
insurance is to vendor the source, not to migrate. Risks to keep in mind are runtime/TFM and trimming, and
that it parses untrusted UDP - a malformed packet must not take the editor down.

**ImGui.NET — the largest single-vendor exposure in the repo.** 379 files, **7,538 call sites**, 199 distinct
`ImGui.*` members. SharpDX was ~150 files.

Its version tracking stopped at **2025-01-06 (ImGui 1.91.6)**, right at the 1.92 break (dynamic font atlas,
`ImTextureID` → `ImTextureRef`). Later commits on the repo are a MonoGame sample and a dependabot bump, so
"the repo is active" does not mean the binding is being maintained - and a contributed PR does not help while
nobody is cutting releases. Dear ImGui itself is the healthiest project in this file (76k stars, pushed
daily), so the gap only widens.

**macOS is not blocked**: `runtimes/osx/native/libcimgui.dylib` in 1.91.6.1 is a universal binary carrying
x86_64 and arm64.

**Hexa.NET.ImGui is the leading alternative** (revised 2026-10-04, after asking the maintainer). Its newest
stable is 2.2.9 from 2025-10-10, which looks stale until you see what is in it: it binds **Dear ImGui
1.92.2b** - past the break ImGui.NET is stuck before. `3.1.0-experimental` (2026-07-31) binds 1.92.9b and adds
net10.0. Quiet *after* completing a major migration is a different signal from quiet *at* one.

- Consumed as ordinary `PackageReference`s. Natives ship inside the package across 9 RIDs (including
  `osx-arm64` and `linux-arm64`), so none of the FFmpeg-style native wrangling applies.
- `Hexa.NET.ImGui.Backends.SDL3` exists and matches the SDL3 path TiXL is already on.
- ~150k downloads; one transitive dependency, `HexaGen.Runtime`, from the same author.
- Licence: the maintainer has committed to keeping it MIT. Note the file is MIT *plus a naming clause*, and
  it is that clause - not MIT - that makes GitHub and nuget report NOASSERTION. Harmless for consuming the
  package; it will keep tripping licence scanners.

**Decision (2026-10-04): stay on 1.91.6, port first.** It is MIT, it works, and macOS is covered, so nothing
forces a move. Revisit only when something in 1.92+ is actually needed.

If that day comes, evaluate **Hexa.NET.ImGui** first (above) - it is already past 1.92 and needs no native
build pipeline. **Forking ImGui.NET** is the fallback: MIT, and a fork inherits a working generator and
native CI across five RIDs rather than starting from scratch, tracking an upstream that is thriving (unlike
SharpDX, where the layer underneath was fading too). Either way, move because 1.92+ is needed, not as
insurance - insurance means taking on migration cost, or maintenance, to stand still.

Note the asymmetry that matters: 199 distinct members is a *small* surface to wrap, but 7,538 call sites is a
large mechanical edit. A seam is affordable; it is just not cheap.

## The habit worth keeping

When a third-party type starts appearing in files that have no business knowing about it, that is the moment
to put a seam in — not when the package dies. `Core/Video/VideoPlayback.cs` already does this for FFmpeg,
which is why the migration above is bounded to one project instead of being another facade project.
