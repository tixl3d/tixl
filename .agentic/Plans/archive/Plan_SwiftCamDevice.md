# Plan: SwiftCam Device Operator

**Date:** 2026-04-27
**Status:** Ready to start — alignment complete, awaiting kickoff session.
**Hardware:** Swift Imaging camera (USB) available for live testing on the development machine.

## Motivation

The existing [VideoDeviceInput.cs](../../Operators/Lib/io/video/VideoDeviceInput.cs) covers webcams via DirectShow + OpenCvSharp. That path is the wrong shape for a Swift Imaging camera, which is a scientific USB sensor with:

- Hardware ROI, multiple bit depths (8/10/12/14/16), MONO/RAW formats not exposed cleanly through DirectShow.
- A vendor SDK (`swiftcam.dll`) that gives direct frame pointers via push-mode callback — the per-frame `Mat` allocation, BGR→BGRA conversion and software resize in `VideoDeviceInput` are pure overhead in this context.
- Stable scientific use cases (microscopy, low-light) where exposure/gain control and predictable latency matter more than auto-tuned color.

The Swift camera should be treated as a *deterministic sensor*, not a webcam. This plan covers a dedicated `SwiftCamDevice` operator that uses the vendor SDK directly and exposes the result as a `Texture2D` for downstream operators.

## Goal (Phase 1, this PR)

A `SwiftCamDevice` operator that:

1. Enumerates and opens a Swift camera by name (dropdown).
2. Streams via `Swiftcam_StartPushModeV4` — callback writes a frame pointer; render thread does direct `UpdateSubresource` into an `R8_UNorm` (MONO8) or `R16_UNorm` (RAW16) texture.
3. Exposes inputs for `Active`, `DeviceName`, `ResolutionIndex`, `BitDepth`, `ExposureTimeUs`, `AnalogGainPercent`, `Roi`, `Reconnect`.
4. Exposes outputs `Texture`, `Resolution`, `Status`, `UpdateCount` — same shape as `VideoDeviceInput` so it drops into existing graphs.
5. Reports a friendly `Status` message if `swiftcam.dll` isn't installed, without crashing the editor.

Everything else from the multi-stage SDK integration plan (dark-frame subtraction, temporal accumulation, tone mapping, auto-exposure UI) is **out of scope** for this PR — see "Future phases" below.

## Locked-in architecture decisions

| Decision | Choice | Rationale |
|---|---|---|
| Operator name | `SwiftCamDevice` | Brand name, mirrors `VideoDeviceInput` shape |
| Location | `Operators/Lib/io/video/SwiftCamDevice.cs` | Next to existing video-input ops |
| Vendor wrapper | Copy `swiftcam.cs` verbatim into `Operators/Lib/io/video/swiftcam/Swiftcam.cs`, keep `internal` visibility | Same assembly as the operator; minimises drift from vendor source |
| Capture mode | `StartPushModeV4` (raw `IntPtr` callback) | Lowest-latency, no managed copy per frame |
| Pixel formats stage 1 | MONO8 → `R8_UNorm`, RAW16 → `R16_UNorm` | Covers preview + scientific workflows; conversion to displayable RGBA is a downstream operator's job |
| ROI | `Swiftcam_put_Roi` with stop → configure → start | Header explicitly forbids changing inside callback |
| DLL deployment | NOT committed to repo. User installs Swift Imaging SDK separately. Wrapper file (Swiftcam.cs) IS committed with vendor attribution | No license file shipped with SDK → can't redistribute under MIT. Wrapper is user-authored P/Invoke, same legal status as defining `[DllImport]`s manually |
| Missing-DLL handling | Lazy SDK probe in `Update`, cached `_sdkAvailable` bool, friendly Status message with install link, retry on `Reconnect` | `[DllImport]` resolves lazily — keeps failure scoped to this op only |

## Phase 1 file plan

```
Operators/Lib/io/video/SwiftCamDevice.cs            -- the operator (~400 lines)
Operators/Lib/io/video/swiftcam/Swiftcam.cs         -- vendor C# wrapper, copied verbatim from SDK
Operators/Lib/io/video/swiftcam/Swiftcam.NOTICE.md  -- attribution + "vendor wrapper, do not edit" note
.help/using/SwiftCamSetup.md                        -- user install steps and first-use walkthrough
.tests-manual/SwiftCamDevice/SwiftCamDevice.md      -- manual test set
.gitignore                                          -- add `**/swiftcam.dll` (paranoia: prevent accidental commit of vendor binary into a build folder)
```

No edits expected in `Editor/`, `Core/`, or other operator projects. Stage 1 is self-contained inside `Operators/Lib/io/video/`.

## Phase 1 implementation steps

Step 1–2 are clean, independent, and worth committing as one. Steps 3–8 belong in a single second commit because they only become useful together.

1. **Drop in the vendor wrapper.**
   - Copy `c:\Users\pixtur\dev\research\swift\Swift Imaging 3.0 SDK\dotnet\swiftcam.cs` → `Operators/Lib/io/video/swiftcam/Swiftcam.cs`.
   - Add a brief `Swiftcam.NOTICE.md` next to it: source path, vendor name, "do not modify — replace wholesale on SDK update".
   - Verify it compiles in isolation: `dotnet build Operators/Lib/Lib.csproj`.
   - Expected pitfalls: namespace mismatch (file is global namespace, may need to wrap in `namespace Lib.io.video.swiftcam;`), or `using` clauses missing for `System.Runtime.InteropServices`. Both are five-minute fixes.

2. **Add `**/swiftcam.dll` to `.gitignore`.** Belt-and-braces against a future `dotnet publish` or developer drop committing the binary.

3. **Skeleton operator file.** Create `SwiftCamDevice.cs` with:
   - Class declaration, `[Guid]` attribute (generate new GUID), inheriting `Instance<SwiftCamDevice>` and `ICustomDropdownHolder`.
   - All input/output slots declared.
   - Empty `Update(EvaluationContext context)` — no SDK calls yet, just sets `Status` to "stub".
   - Build green.

4. **Lazy SDK probe + missing-DLL UX.**
   - `private bool? _sdkAvailable;` field.
   - First action in `Update`: if null, try `Swiftcam.EnumV2()` inside `try { } catch (DllNotFoundException) catch (BadImageFormatException)`. Cache result.
   - On false: set `Status = "swiftcam.dll not found. Install Swift Imaging SDK from <vendor URL>, then trigger Reconnect."`, return.
   - On `Reconnect` true: clear `_sdkAvailable` so next frame re-probes.
   - Same try/catch in `ICustomDropdownHolder.GetOptionsForInput` so the dropdown yields a single "swiftcam.dll not found" entry instead of throwing into ImGui.

5. **Device enumeration + dropdown.**
   - Static cache `_devices` populated by `Swiftcam.EnumV2()`. Refresh on `Reconnect`.
   - `ICustomDropdownHolder.GetOptionsForInput(InputDeviceName.Id)` yields each `DeviceV2.displayname`.
   - `HandleResultForInput` writes the chosen displayname back to `InputDeviceName`.

6. **Capture lifecycle.**
   - `Open` resolves displayname → device id → `Swiftcam.Open(camId)` → store the `Swiftcam` handle.
   - Configure ROI, bit depth, pixel format BEFORE `StartPushModeV4` (header constraint).
   - `StartPushModeV4(DataCallback, EventCallback)`. The data callback runs on the SDK thread.
   - In the callback: hold a lock for ~1 µs, copy `(IntPtr pData, w, h, stride, seq)` into a single `LatestFrame` struct field. Do NOT touch GPU, do NOT call SDK methods (header forbids).
   - Lifecycle transitions on `Active`, device change, ROI change, bit-depth change, `Reconnect`: stop → reconfigure → start.

7. **Render-thread GPU upload.**
   - Inside `Update`, after the lazy probe and lifecycle bookkeeping: lock briefly, snapshot `LatestFrame`, release.
   - Allocate/recreate `_gpuTexture` if `(w, h, format)` changed. Format = `R8_UNorm` or `R16_UNorm` based on bit depth.
   - `device.ImmediateContext.UpdateSubresource(new DataBox(ptr, stride, 0), _gpuTexture)`.
   - Bump `UpdateCount`, set `Resolution`, set `Texture.Value = _gpuTexture`.
   - Performance check: zero allocations on the per-frame happy path (no LINQ, no `new`, no closures). Same rule as elsewhere.

8. **Exposure / gain inputs.**
   - On change: `Swiftcam.put_ExpoTime(microseconds)`, `Swiftcam.put_ExpoAGain(percent)`. Safe to call mid-stream per the header.
   - Detect change with cached previous values to avoid hammering the SDK with redundant sets every frame.

9. **Dispose.** Stop capture, close device, dispose texture. Mirror `VideoDeviceInput.Dispose`.

10. **Build + smoke test (live hardware).**
    - `dotnet build Operators/Lib`.
    - Hot-reload editor, drop a `SwiftCamDevice` op into a test composition, wire texture into a `Display` or similar.
    - Verify: device shows in dropdown, frames flow, exposure slider has visible effect, ROI shrinks the texture, toggling `Active` cleanly stops/starts.
    - Add `Log.Debug` probes liberally during this step per `.claude/CLAUDE.md` — strip them before commit.

11. **Documentation.** Write `.help/using/SwiftCamSetup.md`: where to download the SDK, what gets installed, what to expect on first use, troubleshooting the missing-DLL case.

12. **Manual test set.** Add `.tests-manual/SwiftCamDevice/SwiftCamDevice.md` (see "Manual test set" section below).

## Future phases (not this PR)

Each becomes a separate operator that consumes a `Texture2D` — TiXL's compositional shape. Listed roughly in priority order:

- **`SubtractDarkFrame`** — takes 2 textures (live + dark), outputs the difference. User captures the dark frame manually with the lens cap on; we just subtract.
- **`AccumulateFrames`** — gated temporal mean with motion-rejection. Compute shader: `if (abs(current - mean) < threshold) mean = lerp(mean, current, alpha) else mean = current`. Outputs the running mean texture.
- **`NormalizeBitDepth`** — converts `R16_UNorm` to `R8_UNorm` or RGBA8 with a tone-mapping curve and configurable black/white points. Needed because most TiXL display ops expect 8-bit RGBA.
- **`SwiftCamControlUI`** — optional dedicated UI window for white balance, auto-exposure target, cooling/TEC, trigger modes — pulled out of the main operator to keep `SwiftCamDevice` lean.

## Risks and unknowns

- **Vendor wrapper namespace.** `swiftcam.cs` is in the global namespace as shipped. If wrapping in `namespace Lib.io.video.swiftcam;` causes friction (reflection, marshalling-by-name, etc.), fall back to leaving it global and adding `using static Lib.io.video.swiftcam.Swiftcam;` where needed. Test early.
- **Wrapper size.** 4,771 lines covers the entire SDK — auto-focus, GPS, lens motors, etc. We could trim to the ~30 functions we use, but every trim creates a divergence point on the next SDK update. Recommend keeping it verbatim for now, costing ~150 KB compiled. Revisit if the operator project assembly size becomes an issue.
- **Disconnect detection.** The SDK reports disconnects via `eEVENT.DISCONNECTED` to the event callback. Need to verify the callback is called on a thread we can synchronise with; if not, set a `_disconnected = true` flag and clean up on render thread.
- **DLL search order.** `[DllImport("swiftcam.dll")]` uses Windows search order. The SDK installer typically puts it in `System32` (auto-found) or under `%ProgramFiles%\Swift Imaging\` (NOT auto-found). May need a `NativeLibrary.SetDllImportResolver` in a static ctor that probes `%ProgramFiles%\Swift Imaging\` and the SDK's typical install path. Confirm during step 10 — if Windows finds it without help, skip the resolver to keep code simple.
- **Pull mode vs push mode.** Vendor's WinForms sample uses pull-with-callback, not push V4. Push V4 is documented and exposed but less battle-tested in their samples. If push V4 misbehaves, fall back to pull mode (one extra `PullImage` call per event, but functionally equivalent). Decision point: end of step 7.
- **Performance constraints.** Per `.agentic/AGENT_INSTRUCTIONS.md`, `Update` runs every frame — must stay allocation-free. The lock-protected `LatestFrame` struct copy in step 7 is the hottest path; verify with a profile if anything seems sluggish.

## Manual test set

`.tests-manual/SwiftCamDevice/SwiftCamDevice.md`, following the format in [`.tests-manual/README.md`](../../.tests-manual/README.md):

1. **DLL missing happy path.** Without the SDK installed: drop `SwiftCamDevice` into a graph → expect `Status` shows install instructions, no crash, dropdown shows "swiftcam.dll not found".
2. **First-time use.** With SDK + camera installed: drop `SwiftCamDevice`, pick device from dropdown, toggle `Active` → live texture appears.
3. **Exposure control.** Move `ExposureTimeUs` slider → image brightness changes accordingly.
4. **Gain control.** Move `AnalogGainPercent` → noise/brightness changes accordingly.
5. **ROI.** Set `Roi` to a sub-region → output texture resolution shrinks, content is the cropped sensor region.
6. **Bit depth switch.** Toggle 8 ↔ 16 bit → texture format changes (verify via `Resolution`/`Texture` channel inspection).
7. **Reconnect.** Yank USB cable → `Status` reflects disconnect. Plug back in, trigger `Reconnect` → resumes.
8. **Two operators.** Drop a second `SwiftCamDevice` on the same camera → expect graceful "device busy" status (no crash, no zombie threads).

## Documentation

`.help/using/SwiftCamSetup.md` covers:

- What the Swift Imaging camera is and which models are supported (link to vendor product page).
- Where to download `swiftcam.dll` (vendor URL) and what their installer does.
- Verifying the install (a single sentence: "Drop the SwiftCamDevice op into a graph; if the dropdown shows your camera's name, you're set.").
- Troubleshooting the missing-DLL message.
- Pointer to the manual test set for self-validation.

Per `.help/STYLE.md` conventions; cross-link from the existing video-input page if there is one.

## Acceptance criteria

- `dotnet build Operators/Lib` clean, no warnings introduced.
- All eight manual test steps pass with hardware connected.
- DLL-missing test step passes without hardware connected.
- No `Log.Debug` probes left in the committed code.
- `.help/using/SwiftCamSetup.md` published under `.help/`.
- No new allocations in the per-frame path (verified by reading the code, profile if uncertain).
