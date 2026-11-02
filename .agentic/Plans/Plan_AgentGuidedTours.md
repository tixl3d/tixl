# Agent-Guided Tours (learning-focused MCP experiment)

**Status:** Proposed experiment, 2026-11-02. Nothing implemented.
**Scope:** Editor (`TourInteraction`, `ParameterWindow`, `DebugServer`), plus a new stdio sidecar in the last phase.
**Not in scope:** Graph edits by the agent, keyframes, combine-into-symbol, operator generation, verifying that a user finished a step.

## Why

Users asked for an MCP integration. Generating graphs is a large, risky surface, while learning questions need
almost none of it: "How do I animate the camera?", "What does this graph do?", "How do I make this black?"

The idea: the agent answers **inside the editor**, as a step-by-step tour over a real graph, highlighting ops and
parameters, using the interaction SkillQuest already taught users. A tour writes nothing to the project, so the
agent can teach with no graph-editing tools. The user makes the change themselves, which is also how they learn it.

## Hypothesis and exit criteria

The bet: a tour on a real graph teaches better than a chat answer, and an agent can write good ones from our existing
descriptions and examples.

Phase 2 tests this **before any MCP code exists**. Continue to the sidecar only if:

- At least 8 of the 10 evaluation prompts give a tour the maintainer would accept with light edits.
- No tour states a confident falsehood about what an op or parameter does.
- A tour feels better than the same answer in chat, by the maintainer's judgement.

If these fail, stop. Keep whichever of `searchSymbols` / `describeSymbol` / temporary tours proved useful for
development sessions, and remove the rest.

## What exists

- **Tour data:** `TourPoint` has `Description`, `ChildId`, `InputId` and `Style`. Saved per symbol in `.t3ui`; 123 tours,
  121 of them in Skills.
- **Tour playback:** `TourInteraction.Draw` runs for any composition with tour points, from `GraphWindow`. Markdown
  text with clickable `[Operator]` links. `SkillTraining.DrawLevelHeader` returns early outside a level.
- **Bridge:** 48 methods on the main thread (`DebugServer`). `getGraphState` already accepts a `compositionId`, so it can
  read a graph that is not open.
- **Example links:** `ExampleSymbolLinking` maps an op to `<Name>Example(s)` symbols, by naming convention.
- **Knowledge, Lib:** 873 of 971 operators have a description; 2,962 of 5,780 input entries do (653 operators have at
  least one).
- **Knowledge, Examples:** 8 `howto` graphs (`HowToAnimate`, `HowToUseParticles`, ...) that appear to carry many
  annotations, 2 `tutorial` graphs, and 129 per-operator examples under `Symbols/Lib`.

## What is missing

- `TourPoint.InputId` is saved and editable, but playback never reads it. Only the op gets a marker.
- Tours exist only on `SymbolUi`, which is saved. There is no temporary tour.
- The marker does not scroll the graph, so a target can be off-screen.
- The bridge has no tour methods, no symbol search and no symbol description. Annotations are not exposed.
- Opening an example that is not part of the current project has no direct path. `TrySetCompositionOp` takes a path
  through existing instances.

## Decisions

1. **Temporary tours live in memory only.** They never touch `SymbolUi.TourPoints`, never call `FlagAsModified`, and never
   appear in undo. An authored tour on the same symbol is hidden while the temporary one runs and returns when it ends.
2. **Target by GUID, not by the tour Markdown format.** The agent has ids from `getGraphState`. The Markdown import skips
   ops in collapsed sections, and it counts duplicates in a different order than the export.
3. **Validate the whole tour up front.** `showTour` rejects the tour if any op or input does not exist, naming the failing
   step. A made-up op must become an error for the agent, not a step with no marker.
4. **Read knowledge from the running editor, not from a generated index.** A tour needs the editor anyway. This also gives
   one source of truth that matches the running version, and includes the user's own operators.
5. **One composition per tour.** A step can only target a child of the open composition. Multi-level tours can come later.
6. **No tours during a SkillQuest level.** `showTour` returns an error while a level is active.
7. **Short steps.** 2-3 sentences, since the panel is 500 px wide in `FontLarge`. Enforced by guidance in the tool
   description, not by the server.
8. **The sidecar is an allowlist.** It exposes only the tools below. `shutdown`, `reload`, `setInput`, `addOp` and paths
   chosen by the caller are not reachable through it.

## Phases

### Phase 1: Temporary tours over the bridge (about 2 days)

`TourInteraction`:

- Add a temporary tour (`SymbolId`, title, points, progress) separate from `_symbolTourProgress`.
- Resolve the active points in one helper, used by `Draw` and `RenderTourPointDetails`. `IsTourBlockingHints` keeps reading
  authored tours.
- When a temporary tour ends, drop it instead of showing `Restart Tour`. Add a close button.
- On step change, if the target is outside the visible area, frame it using the logic behind `focusGraphView`.
- A target deleted during the tour shows the text without a marker.

`DebugServer`:

```json
{"method":"showTour","compositionId":"<optional, defaults to focused>","title":"Animating the camera",
 "points":[{"style":"InfoFor","childId":"<guid>","inputName":"Position","description":"..."}]}
{"method":"getTourState"}   // active, symbolId, stepIndex, stepCount, finished
{"method":"clearTour"}
```

Document all three in `DEBUG_PROTOCOL.md` and add an integration test in `Tests/Editor.IntegrationTests` covering a valid tour,
a rejected tour, no `.t3ui` change, and the authored tour returning.

### Phase 2: Knowledge methods and the evaluation (about 2 days + maintainer time)

Additive bridge methods:

- `searchSymbols` (`query`, `maxCount`): token match over name, namespace, summary and the `AKA:` line. Returns name, id,
  package, first description line, example ids. The agent does the semantic work, so this stays simple.
- `describeSymbol` (`symbolId` or `symbolName`): full description, inputs (name, type, default, description, group), outputs,
  example ids.
- `getGraphState`: add annotations (title, text, area), since the `howto` graphs teach through them.

Then the evaluation, driving the bridge from an agent CLI with a short instruction file and no MCP. Run the 10 prompts below
in `_agentTests` and in example graphs, and judge against the exit criteria.

Provisional prompts, to be replaced by the maintainer's ranking:

1. What does this selected operator do, and which parameters matter?
2. Explain this graph to me step by step.
3. What does this example do, and which three parameters should I play with first?
4. Which part of this graph is responsible for the colour shift?
5. How do I make this black?
6. How can I animate the camera?
7. How do I make something react to the beat of my music?
8. How do I get glow only on the bright parts of my image?
9. Why is this scene dark?
10. What does this error in the log mean, and which operator caused it?

### Phase 3: Parameter highlighting (about 1-2 days; do only if Phase 2 passes)

- A step with an input selects its op, so the Parameter Window shows it.
- `ParameterWindow.DrawParameters` compares each `inputSlot.Id` against the highlighted input, draws a frame around the row in the
  marker's colour (`BackgroundActive`), and scrolls to it once per step.
- The row may be hidden: `skipIfDefault` inside a closed group, or `hideNonEssentials`. Force it visible while highlighted.
- This also applies to authored tours, which already store `InputId`, so SkillQuest gains it. Check existing levels for
  highlights that were never seen and now appear. `LevelFeedback` tracks tips per op and input, and may have reusable parts.
- The per-frame cost is one Guid compare per parameter row, with no allocations.

Phase 2 evaluates without this. If the tours are good with op markers alone, this phase is a polish decision.

### Phase 4: MCP sidecar (about 3-4 days; do only if Phase 2 passes)

A small self-contained stdio program, e.g. `Tools/TiXL.Mcp/`, reusing `Tests/TiXL.DebugClient` moved to a shared location. It uses
the official C# MCP SDK (check its current state first).

| Tool | Maps to |
| --- | --- |
| `get_context` | `getContext` |
| `read_graph` | `getGraphState` |
| `search_operators` | `searchSymbols` |
| `describe_operator` | `describeSymbol` |
| `open_example` | see open question 1 |
| `read_log` | `getLogTail` |
| `look_at_output` | `screenshot`, sidecar-chosen temp path, returned as an image |
| `show_tour`, `get_tour_state`, `clear_tour` | the Phase 1 methods |

- Tool descriptions carry what the agent must know: select to evaluate, pump frames, tour style rules. The sidecar handles
  the select/pump/screenshot sequence inside `look_at_output`.
- Requests get timeouts with messages an agent can act on, because a modal dialog blocks the bridge.
- A clear error when the bridge is off, telling the user where to enable it (Settings → Diagnostics → Debug Bridge).
- Ship as **experimental**: no compatibility promise, version reported from `ProtocolVersion`.
- Add a `.help/` page, a release-notes entry and a `.tests-manual/` set.

## Open questions

1. **How do we open an example?** `TrySetCompositionOp` needs an instance path, and example symbols are not instantiated in the
   user's project. Options: open the Examples package as a project, or a read-only preview composition. Adding an instance to the
   user's graph breaks the "writes nothing" promise, so it is out. Resolve this first in Phase 2, because how-to answers depend on
   it. Fallback: tours on the open graph only, with chat text naming the example.
2. **Do the help pages ship?** `.help/embedded` is only 48 KB of panel docs; the full `.help/` and the video analyses are not
   known to be in a release. Phase 2 uses operators and examples only. Add a docs tool only if the evaluation shows gaps.
3. **Can tours point at things other than ops?** A tour on the timeline or output window would suit "How do I export?". Out of scope
   until the basic experiment works.

## Risks

- **Wrong explanations look authoritative.** A tour in the editor's own UI reads like official content. Label it as AI-generated in the
  panel header.
- **Diagnosis prompts (9, 10) may fail while explanation succeeds.** That is an acceptable outcome: drop diagnosis, keep explanation.
- **Project content is untrusted.** Descriptions and annotations from shared projects reach the agent and could carry instructions. With
  an allowlist of no write tools the damage is limited to a misleading tour.
- **Public API pressure.** Once users rely on the sidecar, bridge changes can break it. The sidecar's small tool list is what insulates
  the bridge, so keep bridge methods out of its public surface.
- **Selection side effects.** Selecting an op changes what is evaluated and displayed. This is expected in a tour, but the user's
  selection is not restored afterwards.

## Effort

Roughly 8-10 working days in total, with about 4 before the go/no-go decision. These are estimates from reading the code; nothing has been
prototyped.
