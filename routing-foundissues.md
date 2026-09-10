# Issues found while planning connection routing

Scope: unrelated observations during planning and implementation against `d969679f6000d18a635333b59450c52cc681e23a`. These issues were left unchanged. Reproduce independently before changing these paths; do not expand routing work into a general connection/layout cleanup.

## 1. Duplicate connections can resolve to the first multi-input ordinal

- Source: `Core/Operator/Symbol.cs`, `GetMultiInputIndexFor` (around lines 143–147); `Core/Operator/Symbol.ConnectionSubClasses.cs`, value-equality operator (around line 133).
- Observation: lookup uses `FindIndex(cc => cc == con)`. Connections with identical endpoint values compare equal, so later identical occurrences can resolve to the first occurrence's position.
- Potential impact: operations using that lookup can remove or restore the wrong occurrence/order in a multi-input.
- Routing boundary: identify occurrences by explicit target ordinal from the graph/layout and authoritative connection enumeration. Leave the Core helper unchanged.
- Follow-up reproduction: connect the same output twice to one multi-input, with a different source between them, then exercise an operation using this lookup on the later occurrence.

## 2. Multi-input replacement snapshots a different edge from the deleted ordinal

- Source: `Editor/Gui/MagGraph/Interaction/InputSnapper.cs`, around lines 73–77.
- Observation: the replacement path snapshots `FirstOrDefault` for the target input, but passes `BestInputMatch.MultiInputIndex` to deletion.
- Potential impact: replacing a non-first occurrence can undo by restoring the first occurrence's source at the replaced position.
- Routing boundary: build merge/cut commands directly from complete occurrence snapshots. Do not reuse this replacement path for the new stroke operation or refactor the existing snapper as part of routing.
- Follow-up reproduction: use three distinct sources in one multi-input, replace the second through socket snapping, then undo and compare the source sequence.

## 3. Connection deletion throws after its parent symbol is unloaded

- Source: `Editor/UiModel/Commands/Graph/DeleteConnectionCommand.cs`, `Do` and `Undo` (around lines 20 and 31).
- Observation: missing registry resolution throws. The add-child and add-connection commands instead have defensive missing-symbol handling.
- Potential impact: an undo entry outliving its project's registered symbols can raise an exception.
- Routing boundary: the new routing-specific composite resolves its parent and required definitions defensively before invoking existing subcommands. Do not change shared command behavior in this feature.
- Follow-up reproduction: retain a deletion undo entry across a package/project unload and exercise its resolution path in an isolated test.

## 4. A snapped-layout condition compares coordinates from different axes

- Source: `Editor/Gui/MagGraph/Model/MagGraphLayout.cs`, `UpdateConnectionLayout`, the `AdditionalOutToMainInputSnappedVertical` branch.
- Observation: one condition compares `sourceMax.X` with `targetMin.Y`. This appears to be a mixed-axis typo; the intended geometry still needs confirmation.
- Potential impact: a secondary-output connection can receive an unexpected snapped style at certain node positions.
- Routing boundary: reroute-involved wires take their own small nonsnapped geometry branch. Do not alter the existing condition while adding it.
- Follow-up reproduction: move a secondary-output source and its target independently in X/Y while observing when this style is selected.

## 5. Hidden-input tooltip index guard allows an index equal to Count

- Source: `Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawNode.cs`, around lines 365–367.
- Observation: the guard is `inputIndex <= item.Instance.Inputs.Count`, followed by indexing `Inputs[inputIndex]`. Equality is outside the valid index range.
- Potential impact: if UI/input counts diverge, for example around a reload, this tooltip path can index beyond the runtime input list. Static inspection establishes the unsafe boundary, not a reproduced user-visible failure.
- Routing boundary: compact anchors use their validated one-input/one-output contract and do not need this tooltip path. Leave the unrelated guard unchanged.
- Follow-up reproduction: exercise the hidden-input tooltip with mismatched UI/runtime input counts in an isolated reload test.

## 6. Repository SDK version is not a valid SDK feature-band version

- Source: `global.json`, `sdk.version` is `10.0.0`.
- Observation: installed .NET SDK 10.0.302 reports that SDK feature bands start at 100, for example `10.0.100`.
- Routing boundary: leave the repository SDK configuration unchanged. Verification builds run from the workspace parent with an explicit project path, using the installed .NET 10 SDK.
- Follow-up: independently choose the repository's intended minimum SDK feature band and verify existing developer/CI environments before changing it.

## 7. Double slots use float-only value controls

- Source: `Editor/UiModel/UiRegistration.cs`, `RegisterIOType(typeof(double), ...)`; `Editor/Gui/InputUi/VectorInputs/FloatInputUi.cs`; `Editor/Gui/OutputUi/FloatOutputUi.cs`.
- Observation: the double registration constructs `FloatInputUi` and `FloatOutputUi`, whose typed implementations expect `float`. The output control explicitly returns when the slot is not `Slot<float>`.
- Potential impact: a double input/output may not offer working value editing or display. This also affects the new ordinary `RerouteDouble` value controls.
- Routing boundary: graph socket compatibility uses the runtime slot's actual `ValueType`; double forwarding and serialization passed the runtime checks. Do not change the shared type UI registration as part of routing.
- Follow-up: reproduce parameter editing and output value display on an existing double operator, then address the registration/control mismatch separately.

## 8. Existing cube screenshot acceptance check fails in the verification session

- Source: `Tests/Editor.IntegrationTests/GraphAcceptanceTests.cs`, existing `BuildRenderRecolorUndo_RoundTrips` test.
- Observation: on the Debug editor launched at 1280 by 720, its first screenshot was 860 bytes, below the test's 2,000-byte content threshold. The test stopped there; it did not exercise recoloring or its final undo assertions. The debug log query reported no error entries.
- Routing boundary: the separate reroute forwarding/reload/connection-undo test passed in the same run. No screenshot threshold, rendering code, or existing test behavior was changed.
- Follow-up: investigate the captured output and window/scene state independently. The cause has not been established or reproduced on an unchanged baseline, so this is not claimed as a confirmed pre-existing product defect.
