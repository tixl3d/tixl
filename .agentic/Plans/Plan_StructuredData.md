# Plan: Structured data connections (trees, tables, queries)

Status: **Design only, nothing implemented or prototyped.** Written 2026-10-30. Sizes and speed claims below
are estimates until Phase 0 has run.

## Goal

A connection type that holds typed, structured data and can be read by query ops: nested values like JSON or
Max `dict`, and tables like TouchDesigner DATs. It should be easy to use in a graph, safe across hot reload,
and fast enough for large lists.

This matters more long term than the performance tests that prompted it. Those tests are the first consumer
(see [Performance tests](#performance-tests-first-consumer)), not the reason for the design.

## Existing options

| Option | Keep for | Why it can't be the general solution |
| --- | --- | --- |
| `Dict<float>` | Nothing new; retire it | Float-only. `ReadFromJson` doesn't appear to match what `ToJson` writes, and both are marked "unverified". |
| `DataSet` | Recordings, values over time | Each sample is a `DataEvent` plus a boxed value (roughly 60 bytes vs 4 for a float). Non-temporal data has to pretend it has a time. |
| `<object>` slots | Stable types owned by `Core` (camera references) | Invisible to the graph. Op-owned classes such as `_SketchImpl.OutPages` break on hot reload. |
| `StructuredList<T>` | Large numeric data, GPU upload | `where T : unmanaged` rules out strings, and the schema is a compiled type users can't define in a graph. |
| `JToken` as slot type | Parsing and writing only | Allocates per value, is mutable, can't hold a `StructuredList`. |

What users asked for, and what to take from each:

| Reference | Take | Leave |
| --- | --- | --- |
| Max `dict` | Nested keys, path access | Mutable global dictionaries shared by name |
| JSON | Data model, exchange format | Parsed trees as the in-memory form of large data |
| JavaScript | Easy `a.b[3].c` access | A full language: allocations, and project files could run code |
| TouchDesigner DATs | Tables, seeing the data on the operator | Everything being strings |

## Key finding: geometry attributes are already the table model

`Core/DataTypes/Geometry/GeometryAttributes.cs` already has most of what a table needs:

- `GeometryAttribute<T>` is a named, dense `T[]`: a typed column.
- One `AttributeDomain` is a table: all `Face` attributes have one value per face. `MeshGeometry` is topology
  plus several related tables.
- `SourcePoint` is effectively a foreign key: it exists so per-point data can be joined back later.
- `Attributes.Add()` shares unmodified buffers between geometries.
- `ColorFacesFromAttribute` fills its dropdown from what's actually on the input (`ICustomDropdownHolder`).

So the new type should share the column implementation rather than add a parallel one.

`Plan_ProceduralGeometry.md` owns the geometry side. It already says CPU attributes live in
`GeometryAttributes`, and that `Point[]` is a projection format. This plan must not contradict that.

## Design

Three layers:

| Layer | What it adds | Used by |
| --- | --- | --- |
| **Column** | Named, typed values | Everything below |
| **Table** | Columns with the same row count | Geometry domains, CSV, performance samples |
| **Tree** | Maps, lists and scalars, with tables as leaves | JSON, API responses, test results |

Sketch only, names are not final:

```csharp
public abstract class DataColumn { Name, ValueType, Count, Resize }
public sealed class DataColumn<T> : DataColumn where T : unmanaged { public T[] Values; }
public sealed class StringColumn : DataColumn { public string[] Values; }

public readonly struct DataValue
{
    // Null, Bool, Number, String, List, Map, Table, Column, Structured
    public readonly DataKind Kind;
    private readonly double _number;  // Reading a number doesn't box.
    private readonly object _reference;
}
```

### Decisions

- **Tables are columnar.** 100k rows with three fields are three arrays, not 100k dictionaries. This avoids the
  per-item overhead of one `DataChannel` per list item.
- **`StructuredList` becomes a leaf, by reference, with no conversion.** It stays GPU-uploadable and can have a
  label column next to it. It is array-of-structs and columns are struct-of-arrays, so neither replaces the
  other. Conversion is explicit, and packing for the GPU happens late, as in `GeometryMeshCompiler`.
- **Strings in GPU data** are an `int` index into a `StringColumn`. `StructuredList` does not gain reference types.
- **The type lives in `Core`.** The schema is data, not a class, so reloading a package can't invalidate it.
  That is the justification for the `Core` addition.
- **Follow the geometry mutation rules, not strict immutability:**
  - Ops must not mutate their inputs. This is a convention, as for `MeshGeometry`.
  - Per-frame producers reuse their output instance and reallocate only when sizes change.
  - Provide a `Version` counter, because with reused instances reference identity can't signal change
    (see the comment on `MeshGeometry.Version`).
  - Building a new root is fine for small data that changes rarely, such as an API response.
- **`MeshGeometry` keeps its typed slot.** Topology has invariants a generic tree can't protect. The data value
  gets a read-only, no-copy view instead: a path like `faces/IsCut` returns the same array.
- **`DataSet` stays the time-series model.** A channel can be exposed as a table with time and value columns.
  Its `JObject` metadata stays for now; convert at the boundary.
- **Newtonsoft stays at the boundary** for parsing and writing.

### Queries

- Paths like `a/b[3]/c`, plus `items[*]/name` to collect a column. A small subset of JSONPath, not an
  expression language.
- Ops parse a path once and cache it, like `DataClipSampling.TryResolveChannel`, so reads are allocation-free.
- Loops need an `Index` input, because building path strings every frame allocates.
- Completion comes from the actual value, extending `DataClipSampling.GetChannelOptions`. This also works for
  API and file data that no compiled type describes. The limit is that it needs an evaluated input.
- Numeric conversion: `TryGet<float>` doesn't find an `int` attribute today, so `ColorFacesFromAttribute` can't
  index with one. Query ops must convert, or report the mismatch.
- A dynamic type moves errors from connection time to runtime, so ops report "path not found" or
  "expected number, got string" through `IStatusProvider`.

First ops: `ParseJson`, `ToJson`, `LoadCsv`, `GetFloat`, `GetString`, `GetColumn`, `SetValue`, `BuildTable`.

### Viewer

A tree and table output UI matters as much as the type, since seeing the data is much of why DATs feel good.
`Editor/Gui/OutputUi/MeshGeometryOutputUi.cs` is the starting point: it already lists attributes and draws a
clipped part table, with row strings formatted once per change so drawing stays allocation-free.

## Phases

### Phase 0: Benchmark

Compare columns against `JToken` trees for memory and read speed, with about 100k rows and per-frame reads.
Fix the format only after this. Keep the benchmark out of the shipped code.

### Phase 1: Extract the column

Extract `DataColumn` / `DataColumn<T>` from `GeometryAttribute<T>`, and add a table type that checks row counts
(today "Length must equal the element count" is only a comment). `GeometryAttribute` becomes a column plus an
`AttributeDomain`. No behaviour change.

`GeometryAttributes` has no serialization of its own, so this shouldn't affect saved files, but its users haven't
been traced yet: `Operators/Lib/Symbols/geometry/`, `GeometryPartSubset`, `GeometryMeshCompiler`, `CurveGeometry`,
`MeshGeometryOutputUi`. Do this while the system is young.

### Phase 2: Spreadsheet viewer on geometry

Show attribute values per domain, generalizing `MeshGeometryOutputUi`. Useful on its own before any JSON support
exists, and it tests the column and table design against real data.

### Phase 3: Data value and JSON

- `DataValue`, tree kinds, `ParseJson` / `ToJson`.
- Register the type in `Core/Model/SymbolPackage.TypeRegistration.cs` and `Editor/UiModel/UiRegistration.cs`.
- Check whether it needs `IOutputData`, as `Dict<T>` implements it.
- Tree and table output UI.
- Run the "Interface stability" audit from `AGENT_INSTRUCTIONS.md` before the format ships.

### Phase 4: Query ops

`GetFloat`, `GetString`, `GetColumn`, `SetValue`, `BuildTable`, plus path completion and status messages. Validate
against three real cases: the performance results, the `UnsplashAPI` response, and a CSV file.

### Phase 5: Performance tests

See below.

### Phase 6: Migrations

- **`Dict<float>`:** more widely used than its experimental status suggests. Live producers are `OscInput`,
  `Gamepad`, `FaceDetection`, `ObjectDetection`, `FaceLandmarkDetection`, `WebSocketClient`, `WebSocketServer`,
  `FreeDInput` and `PosiStageInput`. Several update every frame, so the replacement must support reusing the output
  instance. Old projects depend on the registered type, so keep the registration and the `Select*FromDict` ops as
  obsolete instead of deleting them.
- **Op-owned `<object>` data:** move cases such as `_SketchImpl` where it fits. Camera references stay.
- **`DataSet` adapter:** channel to table.

### Each user-visible phase

Update `.help/`, add a set in `.tests-manual/`, and write operator descriptions per the conventions.

## Performance tests (first consumer)

Compare rendering performance across machines and backends, with one test per bottleneck. Each test sweeps an
integer variable R on a log scale:

| Test | R | Workload |
| --- | --- | --- |
| Fill rate | 1 to 1000 | Overlapping images, 1920x1080, 16-bit RGBA |
| Shader | 100 to 7680 | Raymarching a complex scene at R × R/1.7778 |
| Compute and buffers | 10 to 10M | `RadialPoints` → `Randomize` → `Transform` → `Pick`, no drawing |
| MSAA lines | 1 to 10M | A line spiral with R points |

The original draft had `3960` and `R/1.6666`. Those are corrected above so the range reaches the 8K tick and
matches the 16:9 ticks.

**Result graph:** X axis is R, with ticks on powers of ten or named resolutions (180p, 360p, 720p, HD, 4K, 8K). Y axis
is duration in ms on a log scale from 1 to 100, so 8, 16, 32 and 100ms fall at about 45, 60, 75 and 100% of the
height. The graph is a white line, with threshold lines labelled on the left and one vertical line at the expected R
chosen by the test's designer. The duration at expected R is the headline number. The drawing is built with ops
(loops and line drawing) by the maintainer.

**Measurement:** record three values per step, because without vsync a frame takes roughly `max(CPU, GPU)`, so the
low end of a frame-time curve may only be measuring the editor.

| Value | How | Tells you |
| --- | --- | --- |
| GPU ms | Timestamp queries around the test subgraph | Main number for most tests |
| CPU ms | Stopwatch around the same evaluation | Graph and driver overhead |
| Frame ms | Existing frame timing | Sanity check; comparable across backends |

Follow the ring buffer in `Editor/Gui/Windows/Analyze/GpuFrameTimer.cs`, not `GpuMeasure`, which flushes on read,
measures only every few frames and smooths the value. Tag each query with its R, since results arrive frames later.

**Traps:**

- **Caching:** the graph only re-evaluates dirty ops, so the compute test could measure almost nothing after the
  first frame. Invalidate the test subgraph every frame, ideally through an input that actually changes.
- **Overdraw:** the fill-rate test needs blending on and depth off. Its 1000-draw `Loop` will probably be limited by
  CPU and driver cost, which the CPU value will show.
- **GPU timeout:** a heavy step can exceed the roughly 2s Windows GPU timeout and lose the device. Stop the sweep once
  a step exceeds the cap and let the curve end there. This is a must-have.
- **Clock speeds:** GPUs clock down under light load and laptops throttle. Use a time budget per step with a minimum
  frame count, report the median, and treat disjoint results as invalid.
- **Warm-up:** must cover queued frames from the previous R as well as buffer resizes. Discard frames until two
  consecutive ones agree within about 20%, with an upper limit.

**Also needed:**

- Export JSON with GPU name, driver, backend, TiXL version, build configuration and all samples. Comparing machines
  is the point.
- Run in the Player too, which removes editor UI overhead. Record which one was used.
- Measure expected R directly and for longer, rather than interpolating it.
- Store raw values. Expected R is its own field, not the last tick. Tick positions and normalized coordinates are
  computed by the drawing ops.

**Driver:** `ExecutePerformanceTest`, as its own state machine with one step per frame, because
`Operators/examples/Symbols/testing/ExecuteTests.cs` runs everything in a `while` loop. R reaches the subgraph
through a context variable, like the `_TestFrame` pattern there.

**Result shape:**

```json
{
  "device": { "gpu": "...", "backend": "dx11" },
  "tests": {
    "compute": {
      "expectedR": 300000,
      "samples": { "r": [10, 100, 1000], "gpuMs": [0.2, 0.3, 0.5] }
    }
  }
}
```

`samples` is a table, and `tests/compute/samples/gpuMs` returns a float column without copying it.

**Rollout:** build the compute test end to end, then run it several times on one machine. If the expected-R value
varies by more than about 5%, fix that before adding the other tests, because small differences between machines
won't mean anything until then.

**If the tests come first:** `DataSet` is the fallback. Channel path is `["Compute", "GpuMs"]`, `Time` is R, and machine
info goes in metadata. It needs a new `TimeUnits` value, a public `DataChannel` constructor (it is `internal`, visible
only to `Core.Tests` and `IoServices`), and ops that take a `DataSet` rather than a `DataClip`. It would also show how
results evolve over time.

## Open questions

1. **Moving geometry attributes onto the shared column:** recommended and assumed above, not yet confirmed by the
   maintainer. If rejected, Phase 1 builds a separate column and Phase 2 moves after Phase 3.
2. **Convention vs enforced read-only access** for inputs. Assumed: convention, matching `MeshGeometry`.
3. **Naming:** `DataValue` and `DataColumn` sit close to `DataSet` and `DataClip`, and "attribute" is already overloaded.
4. **Package placement** of the query ops. `Sample*FromDataClip` lives in `Operators/Io`; keeping heavy subsystems out
   of Lib's reload path suggests checking this first.
5. **Authoring in the graph:** a JSON-literal op with editable text is likely, and would need the input UI.

## Risks

- **Scope creep:** this could easily become a database. Check every addition against the real cases in Phase 4.
- **Runtime type errors** in graphs, as described under Queries.
- **Two tree formats** while `DataSet` metadata stays on `JObject`.
