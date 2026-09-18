# Output vs. Binding — Concrete Examples (2026-07-29)

Companion to data-model §2.5. The abstract framing kept confusing us, so: worked examples over a
matrix of **complexity** (single output → Spout mix → multi-machine) × **situation** (steady state →
device change → venue change).

## The two records, in plain words

- **Output** (in `<venue>.setup.json`, shared, travels with the project):
  *"A picture I produce."* Name, canvas resolution (usually auto), and — attached to it via
  mappings — all corner pins and calibration.
- **Binding** (in `outputs.machine.json`, per computer, gitignored):
  *"Where that picture goes on THIS computer tonight."* Display connector, Spout stream, NDI sender.

**One-sentence rule: the Output is the part that must survive the cable being pulled.** Everything
confusing about the split resolves against that sentence — calibration hangs on the output because
calibration must survive replugging, renaming streams, and switching machines.

Concretely, the sketch's example is stored as:

```
barn.setup.json (shared)                 outputs.machine.json (this laptop)
  outputs:                                 bindings:
    { id:A…, name:"Projector 1",             { output:A…, display:"Display 2" }
      resolution: auto }                     { output:B…, spout:"Spout1" }
    { id:B…, name:"Spout 1",
      resolution: 1280×720 }
  surfaces:
    { name:"Wood Wall",
      mappings:[ {output:A…, quad:…} ] }   (nothing about walls or quads in here —
    …                                       another machine can bind the same setup)
```

## Where calibration & effects live (vocabulary guard)

The 2026-07-29 revision moved *transport* to the binding — it did **not** move calibration. The old
"Output vs. Device" pair redistributed: the device's *identity* (the physical projector, its pose and
calibration) merged **into** the Output; only the *plug* moved out to the binding. Test any storage
question against the cable-pull rule — calibration on the binding side would die on replug.

| Data | Home | Why |
|---|---|---|
| Corner pin, warp, mask, per-surface color | Surface→Output mapping (venue file, §2.3 stack) | how *each surface* lands in the canvas |
| Projector pose/lens, calibration points | Output (venue file, `ProjectorCamera`) | venue physics — survives replug and machine swap |
| Whole-canvas trim (global gamma/brightness, future) | Output (venue file) | belongs to the picture as a whole |
| Transport choice | Binding (machine file) | tonight's plug, this machine |
| Per-plug trim (future, only if output mirroring lands) | Output→Binding edge (machine file) | a preview monitor wants different gamma than the projector — the only data that varies per plug |

## UI rule (from ui-restructuring B.3)

While an output ↔ binding pair is boring (1:1, nothing hanging on the output's identity), the
outliner draws **one merged pill** and the user sees *one* concept: `Local / Display 2`. The split
becomes visible only when reality forces it — the scenarios below show exactly when.

## Scenario matrix

| | steady state | device change | venue change |
|---|---|---|---|
| **single display** | S1 | S2 (new display), S3 (unplugged) | S6 |
| **display + Spout** | S1b | S4 (Spout offline) | S6 |
| **multi-machine** | S5 | S5b (client drops) | S7 |

---

### S1 — Baseline, single display (the flow-1 user)
User binds their content to `Local / Display 2`. Behind the scenes two records are created (output
auto-named `@ Display 2` + binding); the user sees **one pill**. They never learn the word "output"
and lose nothing.

### S1b — The sketch's state (display + Spout, calibrated)
Two outputs. `Projector 1` carries Wood Wall's corner pins — identity now matters, so it shows as
its own pill with an edge to `Local / Display 2`. The Spout output is boring… except it was
auto-named after its stream, producing `Spout 1 → Spout "Spout1"`. With role naming it would read
`Preview → Local / Spout "Spout1"` — the duplication feeling in the sketch is a *naming* artifact,
not a model one.

### S2 — A new display appears (projector plugged in)
`LOCAL BINDINGS` gains a dimmed row: `Local / Display 3 (3840×2160)` — the column is a live
**inventory of plugs**, so it just grows. Nothing else changes. Dragging a surface (or content) onto
the new row creates output + binding in one gesture (S1 pattern). With auto resolution, anything
routed there now renders 4K — no other edit.

### S3 — Display unplugged mid-session (why Output exists, visibly)
The plug vanishes from the inventory — but "the picture" must not: `Projector 1` (with all its
calibration) stays, its merged pill **splits**, and it shows *binding missing* (`StatusAttention`,
readiness entry). Replug the display → name-first matching rebinds automatically → pill merges
again. Nothing was lost, nothing to redo. *This scenario is the whole reason the entity exists;
every design question about outputs can be tested against it.*

### S4 — Spout receiver goes offline
Nothing happens in TiXL — a Spout *sender* doesn't die when receivers leave. The binding stays
valid; at most the output card can show a muted "no receivers" hint (transport diagnostics are
binding-level, not output-level). Contrast with S3: the *plug* is still there, only the far end left.

### S5 — Multi-machine, steady state
The shared setup lists 4 outputs. Each machine's own file claims some of them:
master binds none (edit/preview only), ClientA binds Out1–2 to its DP-1/2, ClientB binds Out3–4.
Same setup file everywhere — because outputs are machine-neutral names, per the one-sentence rule.
Outliner on the master: machine groups `ClientA / DP-1…`, remote-claimed outputs tagged, not alarmed.

### S5b — A client drops out
ClientB's heartbeat dies → its group dims, Out3–4 show *machine missing* (readiness). The outputs —
and their calibration — are untouched; an understudy machine claiming Out3–4 (future) inherits them
by Guid. S3's logic at fleet scale.

### S6 — Venue change (single machine)
`Duplicate "Barn" → "Gallery"`. Outputs copy **with their Guids** (venue-swap invariant), so the
machine file's bindings still point at them; display *names* differ at the new venue → name-first
match fails → readiness lists "rebind Projector 1". Corner pins copy but are stale → recalibrate
(readiness). Content/routing untouched. The venue file describes the venue; the machine file follows.

### S7 — Venue change, touring rig
Same as S6 per machine, driven from the master's readiness panel (flows doc, flow 8): each client's
claims re-checked, remote calibration per output. The setup file that toured is the same one from S5.

---

## Virtual displays (2026-07-29, audio-abstraction precedent)

Pre-4.2, projects saved the concrete WASAPI device as a string — examples couldn't run elsewhere;
4.2 fixed it with a virtual **"Default Audio Input" device in the device list**, mapped once in
machine settings. Displays get the identical treatment — no new binding concept, just **displays
that always exist**:

- Virtual displays: `Editor Display` (the display containing the editor window's top-left corner)
  and `2nd Display` (first non-editor display; overridable per machine in machine-wide settings —
  like audio, not per-project). Example projects ship bound to `2nd Display` and run anywhere,
  zero clicks.
- A binding still just points at a display; virtual names are intercepted by the resolution layer
  before OS enumeration (internal marker, not the literal string — a physical display with a
  colliding name can't shadow them).
- UI: ordinary rows in LOCAL BINDINGS, styled distinct (dashed/marked), draggable like any plug.
  Auto-created outputs (S1 / flow 1) default to `2nd Display`; label shows the current resolution:
  `Local / 2nd Display (→ Display 2)`.
- **Resolution timing:** virtual → concrete resolves at presentation start or display-topology
  change, then sticks for the session — moving the editor window mid-show never flips outputs.
  Readiness shows the resolved device.
- **Virtual for dev/examples; venues pin concrete.** Production context (show-file lock / readiness)
  flags virtual-display bindings: "bound to a virtual display — pin it?".
- Degradations: single display → `2nd Display` falls back to a windowed preview on primary, hinted;
  3+ displays → deterministic default, per-machine override.

## Open questions

1. **Mirroring:** one output → two bindings on the same machine (operator monitor showing a
   projector's feed). Allow n bindings per output per machine, or keep 1:1 + a dedicated preview
   feature? (Leaning: allow the list — the model already is a list — and treat UI as later.)
2. **Rebind policy detail:** name-first, index-fallback exists; when both fail *and* exactly one new
   display appeared (S2+S3 combined: projector swapped), offer one-click "rebind to the new display?"
   instead of silent adoption.
