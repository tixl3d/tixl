# Output Setup — Multi-Machine / Render Clients (design notes, 2026-07-29)

Boygroup-style distributed rendering (vvvv analogy): a master streams state; render clients run the
same project + setup and render only the outputs they own. **Status: long-term** — nothing built now,
but the current work carries cheap enablers (§6) and one UI decision lands early (§4).

## 1. The contract already exists

The venue/machine/project storage split *is* the distribution contract:

- **Shared artifacts:** the project (graph) and `.setup.json` (venue) — identical on every machine.
- **Per-machine fact:** `outputs.machine.json` bindings — "which outputs do I own."

A render client = Player + compositor loading the shared artifacts and rendering its bound subset.
All setup entities resolve by Guid on every machine. What's missing is only the **master's view of the
fleet**: no machine knows the others' claims today.

## 2. Machine model (additive)

- `MachineConfig` gains a stable **machine name/id** (one field — the classic cheap-now field).
- At runtime, clients **heartbeat** to the master: identity + claimed outputs + health. The fleet
  picture is runtime state, not setup data — venue files stay machine-agnostic.
- Later: disguise-style understudy (hot spare mirroring a client, takes over its outputs) is a
  natural extension of the same claims model. Noted, not planned.

## 3. Stages (each independently useful)

1. **Pixel streaming** — the planned NDI/Spout output kinds (data-model §2.5): master renders
   everything, dumb receivers display. No sync problem (one renderer); bandwidth/latency-limited.
   Covers "one extra projector on a second box" and genuinely-shared stateful content forever.
2. **Boygrouping proper** — Player as render client. State channel decomposes into:
   - *Clock:* master playback time/BPM broadcast; clients smooth toward it (PTP makes this trivial, §5).
   - *Events:* parameter changes, snapshot/preset activation, forwarded MIDI/OSC — the IO-data
     event-stream abstraction pointed at a socket instead of a file.
   - *Deployment:* project + setup copied ahead of time. **Never stream assets live** (vvvv's asset-
     drift lesson).
   - Gate: `OutputManager` compositor extraction (refactoring-plan P2.4) — a render client is exactly
     Player + compositor.
3. **Editing-time integration** — the killer feature: **remote calibration**. Master pushes setup
   edits live (all calibration flows through commands/`SetupSnapshotCommand` — a natural serialization
   point); the far wall moves while you drag. Also: fleet UI (§4), health states, and pattern display
   on remote outputs for [`camera-calibration.md`](camera-calibration.md) phase 2.

## 4. Flow-outliner integration — machines resolve the Output/Device ambiguity

Decided for the outliner (feeds `ui-restructuring-plan.md` B.3): the rightmost column is
**machine-grouped bindings** ("LOCAL BINDINGS" on a single machine), and bindings carry the
*transport* — displays **and** Spout/NDI streams are peer binding kinds (data-model §2.5, revised
2026-07-29). Binding targets read as **`Machine / Target`** — e.g. `Local / Display 2`,
`Local / Spout "Spout1"` — instead of a bare display name, which resolves the "Output vs. device
binding" ambiguity in one stroke:

- Single-machine setups show one implicit **Local** group — the label alone explains the concept.
- Multi-machine setups list each known machine as a group (from heartbeats; offline machines from the
  last-seen cache, dimmed); outputs bound on *other* machines render faded with the machine tag
  rather than "unbound".
- `StatusAttention` distinguishes *truly unbound* (no machine claims it) from *remote* (someone else
  claims it).
- The bind menu (`DrawOutputBindingMenu` successor) offers `Local / …` targets always; remote targets
  only in stage 3 (binding a remote display = a remote edit).

## 5. Sync primitives (OS-side, current state 2026)

- **Clock:** PTP client ships in w32time (`ptpprov.dll`, Win10 1809+) — sub-ms LAN sync with software
  timestamps. Assume "synchronized system clock" as a given; don't build clock sync into the protocol.
- **Presentation:** Win11 composition-swapchain API (`IPresentationManager`) supports present-at-time
  + actual-display feedback → PTP clock + scheduled presents = frame-accurate alignment without
  genlock hardware. Candidate for the *client's* present path only; the editor keeps its DXGI loop.
- **Hard limit:** vblank-level alignment across machines (soft-edge spanning a machine seam) still
  needs hardware sync (Quadro Sync class). Software gets to ±1 refresh.
- **Latency constant:** measured by camera-calibration phase 5.

## 6. Determinism — a routing guideline, not an engineering goal

Stateful content (particles, feedback, unseeded randomness) diverges across machines. It only
*matters* when the same content spans a machine seam. So:

- Detectable from setup data: a surface whose mappings land on outputs claimed by different machines,
  or slices of one source split across machines.
- UI warns at the seam: "this surface spans machines — content must be deterministic or streamed
  (NDI)". Practical guidance: keep blended/overlapping projectors on one machine.
- No whole-graph determinism requirement, ever.

## 7. Enablers in current work

1. **Keep P2.4 alive** — no more editor state accreting into `OutputManager`; compositor extraction
   before Player output support.
2. **Machine name/id field** in `MachineConfig` whenever that file is next touched.
3. **Contract guard (review rule):** everything a client needs must stay derivable from
   *project + setup + machine bindings* — no editor-session-only state in the render path.
4. **NDI/Spout kinds** when §2.5 is picked up → stage 1 falls out.
5. Outliner machine grouping (§4) ships with the flow outliner — it's a labeling/grouping choice,
   not distributed infrastructure.
