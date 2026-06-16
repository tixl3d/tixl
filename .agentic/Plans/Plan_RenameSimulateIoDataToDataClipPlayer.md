# Rename [SimulateIoData] → [DataClipPlayer] and add AutoCollect

Ticket: #1078 — https://github.com/tixl3d/tixl/issues/1078
Size: —   Milestone: v4.2

## Problem
`[SimulateIoData]` should be renamed to `[DataClipPlayer]` for naming consistency with
`[AudioClipPlayer]`, and gain an `AutoCollect` input that pulls in sibling data clips automatically —
mirroring how `[AudioClipPlayer]` auto-collects sibling audio clips.

## Affected code
- Operator: `Operators/Lib/io/data/SimulateIoData.cs` — `class SimulateIoData : Instance<SimulateIoData>`,
  symbol GUID `7e1c4a82-9c5d-4f8e-bd6f-1a2b3c4d5e60`, namespace `Lib.io.data`. Current slots: output
  `Execute`, inputs `Clips` (MultiInput<DataClip?>), `Enabled` (bool).
- Companion metadata: `SimulateIoData.t3`, `SimulateIoData.t3ui` (GUID-keyed; no hard-coded name field —
  the display name derives from the class name via reflection, `Symbol.Name => InstanceType.Name`).
- AutoCollect reference: `Operators/Lib/io/audio/AudioClipPlayer.cs:94` —
  `[Input(Guid="301f0b3a-…")] public readonly InputSlot<bool> AutoCollect = new();` and its sibling-scan
  logic at lines ~46-69 (rebuilds the sibling list only on Parent/composition-version change; dedups via
  a `_seenChildIds` HashSet; default `true` in the .t3).

## Proposed approach
1. Rename the class to `DataClipPlayer` (and rename the .cs/.t3/.t3ui files to match), **keeping the symbol
   GUID unchanged** so existing user projects resolve it. The GUID comments in .t3/.t3ui regenerate.
2. Add an `AutoCollect` bool input (new GUID, default `true`), modelled on AudioClipPlayer: when set, scan
   the parent composition for sibling data-clip providers and drive them alongside the wired `Clips`.
3. Decide the provider contract — AudioClipPlayer collects an `IAudioClipProvider`; there may be no
   data-clip equivalent yet. Either introduce an `IDataClipProvider` interface (and have the clip ops
   implement it) or scan for the relevant operator/slot type directly. This is the real design work.
4. Update doc/test references: `.help/docs/using/Recording.md`, `.tests-manual/` data-clip sets.
5. Optional back-compat: a `_SimulateIoDataObsolete` shim keeping the old GUID is **not** needed if the
   GUID is preserved on the renamed class (rename ≠ new GUID); only add a shim if the GUID has to change.

## Risks / side-effects
- This is an operator rename + input-set change — the explicit "break user projects" category. The GUID-
  preserving rename is safe for graph resolution, but the **new input slot** changes the op's interface;
  confirm old projects load with the added input defaulting cleanly.
- AutoCollect's sibling scan must stay allocation-light and only rebuild on change (copy AudioClipPlayer's
  caching), or it becomes a per-frame cost.

## Open questions
- Is there (or should there be) an `IDataClipProvider` abstraction, or does AutoCollect scan by concrete
  type? This determines the bulk of the work.
- Keep file names as `SimulateIoData.*` or rename to `DataClipPlayer.*`? (Convention prefers matching.)
- Any string-name lookups of "SimulateIoData" anywhere (search/snippets) that need updating?
