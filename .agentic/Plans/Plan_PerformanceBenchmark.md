# Performance Test Suite

**Status (2026-11-24):** phases 1 and 2 built, uncommitted; tested on Linux/Vulkan (Radeon 8060S, Debug build) only.
Manual test set: [`benchmark-suite`](../../.tests-manual/benchmark-suite.md). See [Built](#built-2026-11-24) for what
differs from the design below.

**Goal:** a built-in test suite that shows, per area of the engine, how the duration grows with load on this
machine, and where an authored **Reference** amount lands. It answers "what can this machine do?" for users, and
"did this release or platform get slower?" for us — first for the notebook's Windows and Linux partitions and a
Mac.

Related: [Plan_FrameTiming](Plan_FrameTiming.md) §7 (measurement layers; this is layer 2, "built-in benchmark"),
archived `Plan_OpGraphProfiling` (per-op timing; shares the GPU query pool), [BISECT_PROTOCOL](../BISECT_PROTOCOL.md).

## The idea

Three categories, one test each to start. Every test sweeps **one** parameter over a log range (10 … 10M) and measures
the duration of each step. The result is a small graph per test:

```
 Buffers & Compute Shaders            7.2ms │ Reference
  50ms ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄/┄┄
  16ms ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄/┄┄┄┄
   8ms ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄●┄┄┄┄┄
                                         ____/│
   1ms ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄____-----┄┄┄┄┄┄│┄┄┄┄┄┄┄┄┄
       _________________-----'                │
 0.1ms ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄│┄┄┄┄┄┄┄┄┄
       10    100    1K    10K   100K         500K 1M   10M
```

- **x:** the swept parameter, log scale (10, 100, 1K … as in the sketch), 1-2-5 steps per decade. The tick labels
  carry the scale, so it reads as "amount", not as a logarithm.
- **y:** duration in ms, **log scale from 0.1 to 50 ms**, labelled at 0.1, 1, 8 (120 Hz), 16 (60 Hz) and 50 ms. Like
  the x axis, the labels carry the scale. A log axis keeps the low end readable: on a linear 0–50 ms axis everything
  under ~1 ms is one flat line, and two machines can't be told apart there. 50 ms is the end of realtime (20 fps), so
  the sweep stops at the first step above it; that step is still drawn, clipped at the top edge. Steps under 0.1 ms
  sit on the bottom edge.
- **Reference:** a vertical line at an authored amount of this test's parameter (e.g. 500K for the buffer test),
  labelled with the duration measured there on this machine. It is the test's headline number, coloured by the
  budget it fits (green under 8 ms, yellow under 16, red above).
- **Spread:** each sweep runs twice; the two passes are drawn as a band around the curve (see Measuring one step).
- **Other runs** (another OS, machine or release) overlay as faded curves, which is the cross-platform comparison.

The knee of the curve is the useful information: where a machine leaves the linear range (cache, bandwidth,
memory) shows up as a bend, which one number per scene cannot show.

**What the suite does not measure:** whole frames. A machine can do well in every test and still stutter, because
of how frames reach the display (present mode, compositor, vsync pacing). That is
[Plan_FrameTiming](Plan_FrameTiming.md)'s subject; the suite must not be read as "this machine runs smoothly".

### The Reference

Each test has one authored **Reference**: an amount of its swept parameter that a typical scene uses (500K buffer
elements, 1K draw calls, 20 full-screen layers). It is a fixed number in the manifest, the same on every machine;
the graph shows only what this machine measures at that amount. Nothing is calibrated against other hardware.
The tooltip says what the amount stands for, e.g. "a dense particle scene".

## Tests

One test per category to start, enough to prove the measuring method, the stop rule and the graph on every platform.
Every test is a composition with one swept input (see Architecture). References are first guesses.

| Category | Test | Swept parameter | Measures | Reference (first guess) |
|---|---|---|---|---|
| CPU | Graph evaluation | ops evaluated per frame ([Loop] around a small math chain) | operator update overhead, slot and dirty-flag cost | 2K |
| Buffers & Compute | Point processing | points through a fixed compute chain (transform, noise, sort-free) | compute dispatch throughput, buffer bandwidth | 500K |
| Draw & Fillrate | Fillrate / overdraw | full-screen layers with blending at the fixed resolution | pixel throughput, bandwidth | 20 |

CPU tests plot CPU time. GPU tests plot GPU time, with CPU time as a thin second line, because a GPU test that
turns CPU-bound (draw calls) is itself a finding.

## Architecture

### Tests are compositions; the suite is a manifest

- **Each test is a symbol** in a `Benchmarks` folder of the Examples package (later its own package): normal ops,
  one int/float input named `Load`, and a `Command` or `Texture2D` output. Measuring real operators is the point:
  a C#-only microbenchmark would miss what users pay for. Anyone can open a test and see what it does.
- **A suite manifest** (`Benchmarks/suite.json`, versioned) lists per test: symbol id, category, title, unit label,
  sweep range, the Reference with its one-line description, and which time is plotted (CPU or GPU). The manifest
  version goes into every result, because changing a test or its Reference breaks comparisons with older runs.

### Measuring one step

- The runner instantiates the test outside any project view, through its own `EvaluationContext` at a fixed
  resolution (1920×1080), the way the debug bridge's `probePull` does. The output window and window size play no part.
- Per step: set `Load`, then **warm up** until shaders and pipelines exist (at least 10 frames, and until two
  consecutive 10-frame medians agree within 5 %), then measure **30 frames** and keep the median and p90.
- **CPU time:** `Stopwatch` around the test output's `Update` (evaluation and command recording).
- **Step time, the main measurement:** wall-clock time from the start of `Update` until the GPU has finished the
  test's commands. The runner submits the test's work on its own and **waits for it** (Vulkan: a fence; D3D11:
  `Flush` and an event query) before the editor records its UI, so the UI is excluded. It works the same on every
  backend and needs no GPU timer support. Stalling the editor for a frame doesn't matter during a run. This needs a
  "submit and wait" call on the backend API. GPU tests plot step time, CPU tests plot CPU time.
- **GPU timestamps, secondary:** a timestamp pair around the same span, from a pooled, backend-neutral timestamp
  API read a few frames late (`GpuFrameTimer` does this for the whole frame already; `GpuMeasure` gets rewritten
  on it, as `Plan_OpGraphProfiling` Part 4 planned). Recorded next to the step time, not plotted: on Apple GPUs
  (MoltenVK) timestamps are only taken at pass boundaries and may be coarse, which is unverified. Step time minus
  GPU time shows the submission overhead.
- **Vsync off and the GPU kept busy** for the whole run. A nearly idle GPU clocks down, which makes the first,
  small steps look slow. If needed, a filler load keeps clocks up.
- **Two passes per sweep, in opposite directions:** small to large, then large to small. A laptop that heats up
  slows down over a minute of load, and a GPU that just idled starts slow; running the passes in opposite directions
  exposes both as a gap between them instead of a bent curve. The graph draws the band between the passes and
  plots their mean; steps where they differ by more than 10 % are marked. Keep runs short (≈40 s for three tests).
- **Stop conditions:** a step above 50 ms ends the sweep, and so does GPU memory above 80 % of the budget or any
  device error. Windows resets the GPU after 2 s in one submit (TDR); the 50 ms ceiling stays far below that.
- One full run: 3 tests × 2 passes × 13–19 steps × ~0.4 s ≈ 40 seconds.

### Results

- A run is a JSON file in the settings folder (`Benchmarks/<date>-<label>.json`): system info (GPU name, vendor,
  driver, backend, OS, TiXL version, build configuration, validation/debug layer, display scale, power source:
  AC or battery), manifest version, and per test the step values with step, CPU and GPU-timestamp median and p90
  for both passes, plus the stop reason.
- **Test fingerprint:** a hash of each test's composition and of every symbol it uses, nested ones included, with
  their packages' versions. Tests are built from real operators, so editing an operator changes the test without
  touching the manifest. "Compare…" marks tests whose fingerprint differs from the loaded run: the difference may
  come from the operator, not from the engine or the platform.
- Results from other machines or OSes are copied over as files and loaded with "Compare…".

### Benchmark window

- `Editor/Gui/Windows/Analyze/BenchmarkWindow.cs`: category sections with a grid of small graphs, as in the
  sketch, drawn with the ImGui draw list in the style of `MetricGraphView` (`UiColors`, every size × `UiScaleFactor`).
- Toolbar: Run all / run category, progress with the test and step being measured, Cancel.
- Hovering a point shows the amount, CPU and GPU time. Clicking a graph opens the test composition.
- "Compare…" loads other result files and overlays them as faded curves with a legend; the reference line shows
  every run's duration at the Reference.
- "Copy summary" puts a small Markdown table (duration at the Reference per test) on the clipboard for bug reports.

### Debug builds

The suite runs in Debug builds as well as Release, so it can be used while developing:

- No `#if DEBUG` / Release-only code paths in the runner, the tests or the window.
- Debug builds run managed code without JIT optimizations, and the Vulkan validation layer or the D3D11 debug
  layer adds CPU cost to every draw and dispatch. The result records the build configuration and whether a
  validation or debug layer was active; the window shows a "Debug build" badge, and "Compare…" labels runs from
  a different configuration. GPU times are mostly unaffected; CPU times are only comparable within one
  configuration.
- Per-frame debug logging stays off during a run, so it does not show up in the CPU curves.

## Phases

**Phase 1 — tests and runner.** The three test compositions and the manifest; a "submit and wait" call on the
backend API (Vulkan fence, D3D11 event query) for step time; a backend-neutral GPU timestamp pool (also replacing
`GpuMeasure`'s D3D11-only queries); `BenchmarkRunner` (two passes, warm-up, stop conditions); system info, test
fingerprints and the result JSON. Check the step times against the timestamps on all three systems, the Mac
especially, before building the window.

**Phase 2 — Benchmark window.** Graphs, compare overlay, copy summary.

**Phase 3 — baselines.** One saved result per release and platform, to compare against when a release or a backend
change might have made something slower.

## Built (2026-11-24)

Code: `Editor/Gui/Windows/Analyze/Benchmark/` (suite, runner, result, window), tests in
`Operators/Examples/Symbols/benchmarks/` (`BenchGraphEvaluation`, `BenchPointProcessing`, `BenchFillrate`),
`IGraphicsBackend.WaitForGpu` and `DriverDescription`, debug-bridge `runBenchmark` / `getBenchmarkStatus`.

Differences from the design above:

- **The suite is C# (`BenchmarkSuite`), not a JSON manifest.** Same content (symbol id, category, unit, range,
  Reference and its description, CPU or step time) and a `Version` constant; no file to locate or parse.
- **No timestamp pool.** Each measured frame waits for the GPU anyway, so the timestamp pair is read right after
  the wait. On Vulkan that needed `FlushAndWait` to mark the waited frame complete; before, query results only
  became readable once the frame slot was reused. `[GpuMeasure]` is unchanged.
- **Tests re-evaluate through a frame variable.** `InvalidateGraph` only propagates real changes, so a test with
  a fixed Load returned cached results. The runner sets the float variable `BenchmarkFrame` every frame and each
  test reads it with [GetFloatVar] (always dirty), like an animated scene. The CPU test needs none: [Loop] reruns
  its subgraph itself.
- **Setting Load forces invalidation.** `InputSlot.SetTypedInputValue` is ignored when the slot was already
  visited in the current invalidation pass.
- **Unsteady** means the passes differ by more than 10 % *and* 0.05 ms; at a few microseconds 10 % is timer noise.
- **Colours:** green under 8 ms, white under 16 ms, `StatusWarning` above; the theme has no yellow with that meaning.
- **Not built:** clicking a graph to open its test composition; the "keep the GPU busy" filler (not needed so far).
- **History for regression checks (added on request):** each run stores the editor's git commit (from its
  InformationalVersion) and an optional note. The window lists the runs of the same label and GPU with the change
  per test against the run before; the bridge's `getBenchmarkStatus` returns that comparison and
  `getBenchmarkHistory` the list. `AGENT_INSTRUCTIONS.md` tells agents when to run it. Changes count above 10 % and
  0.05 ms; repeated Debug runs on the 8060S varied up to 6 % (Fillrate).
- **Not tested yet:** the Compare overlay and Copy summary (need clicks), D3D11 `WaitForGpu`, Windows and macOS.

- **JIT warm-up per test.** Each test first runs ~1.5 s at its Reference. Without it the Release CPU test measured
  0.46 ms going up and 0.11 ms coming down at the Reference: .NET re-compiles hot code with optimizations after a
  while. The two passes caught it.
- **Fingerprints hash operator source**, not compiled IL, so Debug and Release runs of the same tests match.

First results (Radeon 8060S, Linux, Vulkan), duration at the Reference:

| Test | Release | Debug | Range |
|---|---|---|---|
| CPU, 500 iterations | 0.12 ms | 0.31 ms | Release reaches 100K iterations below 50 ms |
| Buffers & Compute, 500K points | 0.77 ms | 0.77 ms | 10M points: 12.5 ms |
| Draw & Fillrate, 20 layers | 3.0 ms | 3.0 ms | 500 layers: 71 ms |

## Decided (2026-11-24)

- The marker is called **Reference**: an authored, fixed amount per test, shown with this machine's duration.
  There is no "average machine" and no calibration.
- Both axes log with plain labels: x the amount (10 … 10M), y 0.1–50 ms. The sweep stops above 50 ms.
- One test per category to start.
- Step time (wait for the GPU) is the main measurement; GPU timestamps are recorded next to it.
- Each sweep runs twice, small to large and back; the graph shows the spread.

## Out of scope for now

- More categories (Textures & Memory, Video) and more tests per category (candidates: draw-command recording, CPU
  geometry, particle simulation, upload/readback, draw calls, instanced geometry, image FX chain).
- Running the suite in the Player.
