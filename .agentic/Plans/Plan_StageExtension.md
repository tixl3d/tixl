# Output Setup — Stage Extension: Lighting, Lasers, 3D Models (design notes, 2026-07-29)

Scope extension decided 2026-07-29: the Setup grows from "projection output routing" toward a **venue
description** — stage lighting fixtures (moving heads, LED strips, lasers), 3D models of the physical
stage/room, and 3D-anchored reference points linked to reference images. **Status: direction-setting
notes**, sequenced after the [`ui-restructuring-plan.md`](output-setup/ui-restructuring-plan.md)
phases; one model decision (§5, reference points) affects
[`camera-calibration.md`](output-setup/camera-calibration.md) and should be settled before that plan
builds annotations.

## 1. Governing principle (unchanged)

**Setup = physical description + patch + routing. Graph = content + behavior.** A fixture's placement,
address, and beam geometry live in the setup (re-done per venue, venue-swappable); what it *does* over
time is op-driven. This is the same boundary that made surfaces work, applied to lights.

## 2. As-built foundation: the point-buffer light flow (2026-07-29)

TiXL **already has** an Art-Net/DMX fixture flow, and it validates the §1 boundary: ops emit **point
buffers** (position, orientation, color, FX1, FX2) that are **matched by index** to fixture types and
DMX data layouts. This works unusually well because points are TiXL's lingua franca — every point
feature/effect (noise, repel, curl, sim…) becomes a light-control tool for free. Consequences:

- **Points are the control carrier, period.** Moving heads are *not* an alien parameter model after
  all — position/orientation drive pan/tilt, color drives color, FX map to fixture channels. The
  stage extension adds *placement, patch, and previz* around this flow; it does not replace it.
- **The patch is the part that belongs in the Setup.** Index→fixture/address matching is venue
  knowledge (which fixture hangs where, on which universe/address) — today it lives op-side, which
  means it dies with the venue swap. Migrating the patch (fixture list, layouts, addresses) into the
  setup while ops keep emitting pure point buffers is the core model move of this extension (open
  question §10.1: migration path and how index-matching maps to setup fixture ids).
- **LED strips reconcile into the same flow:** a strip's sampling geometry (setup-side placement)
  *generates* points — sampled from content space or along the stage-space path — that feed the same
  point→layout mapping. One output path for all pixel-mapped and parameter fixtures, two point
  sources (procedural op chains vs. content sampling).

## 3. Fixture families — different sources, one carrier

| Family | Point source | Pipeline fit |
|---|---|---|
| **LED strips / pixel-mapped fixtures** | sampling geometry (setup-side polyline/grid, N pixels) sampling content, or procedural points | Content → Slice → sampling geometry → points → patch → Art-Net/sACN. MadMapper's LED mapping is the UX reference; the transport is the as-built flow. |
| **Moving heads / wash, spots** | procedural point buffers (as-built) | Setup adds placement + patch + GDTF type; ops keep emitting points, matched to setup fixtures instead of op-side layouts. |
| **Lasers** | ILDA / galvo point streams | Own protocol, own safety regime (§9). Placement + zones in setup; content op-side. **Last in line.** |

New output kinds (additive, string-discriminated like the planned NDI/Spout): `ArtNet`/`sACN`
universes — these are *network node* targets, not display bindings; the machine-binding story differs
from displays (an Art-Net node has an IP, not a connector — per-venue rather than per-machine, though
which machine *sends* is a machine fact for multi-machine setups).

## 4. Standards — adopt, don't invent

- **GDTF** (General Device Type Format, DIN SPEC 15800) for fixture definitions — geometry, channels,
  beam data, wheels. Thousands of fixtures exist in the public GDTF share; hand-rolling a fixture
  format means maintaining a fixture library forever. Non-negotiable recommendation.
- **MVR** (My Virtual Rig) for rig exchange — import a stage plot (fixtures + trusses + positions)
  from Vectorworks/grandMA/Depence instead of re-placing everything by hand; export ours. MVR bundles
  GDTF + scene, so the two adoptions are one decision.
- **Art-Net / sACN (E1.31)** for DMX transport; **ILDA** for lasers (later).
- 3D models: **glTF** as the primary import (plus OBJ tolerance). MVR scenes carry their own geometry.
- **BlenderDMX alignment reinforces all of the above** (§7): it consumes GDTF/MVR and listens to
  Art-Net/sACN — aligning with the standards *is* aligning with the previz bridge.

## 5. 3D stage models & reference points (the model-level decision)

- `Setup.StageModels[]` (additive): `{ Id, Name, AssetPath, Pose, Scale }` — room/stage geometry as
  context. Roles: previz backdrop in the perspective/stage view, snapping target for surface &
  fixture placement, and *calibration geometry* (known 3D shape for camera/projector solves).
- **Promote reference points to a setup-level entity** — `Setup.ReferencePoints[]`, each a named
  survey point with an **observation list**:
  - position on/in a stage model (3D, meters),
  - position(s) in reference images (2D px, per image),
  - later: camera detections (from calibration captures).

  One point, many observations = the **correspondence hub** across all spaces. Posing a reference
  photo against the model, PnP-solving a camera, or solving a projector against real geometry are all
  "consume the observation list" operations. This supersedes the binding-local point-annotation shape
  sketched in `camera-calibration.md` §4.1 — build the entity setup-level from the start; the
  reference/straighten canvas and the 3D views edit *observations* of the same point. (Line
  annotations stay binding-local — they serve rectification, not correspondence.)

## 6. UI integration (forward notes for the restructuring phases)

- **Flow outliner:** fixtures appear in the flow only where they consume content — pixel-mapped
  fixtures sit with SURFACES (or a FIXTURES group beside them) with edges Slice → strip → universe.
  Parameter-driven fixtures are *not* flow rows (no content edge); they live in the stage/canvas views
  and a patch list. Universes join OUTPUTS; network nodes join the machine-grouped DEVICES column.
- **Unified canvas / stage view:** stage models render as context geometry; fixtures are placeable
  true-scale items (kind-colored, per the board language). Beam previz (cones, gobo projection) is a
  stage-view rendering feature — genuinely later, and never a blocker for control output working.
- **Parameter window:** fixture cards (patch, placement, strip pixel count/pitch) follow the Phase A
  pattern unchanged.

## 7. Previz strategy: the BlenderDMX bridge (decided direction, 2026-07-29)

Don't build native beam previz until TiXL rendering reaches parity — **bridge to
[BlenderDMX](https://blenderdmx.eu/)** (open-source, Eevee/Cycles) instead. The bridge is nearly free
because BlenderDMX already speaks our chosen standards:

- **Rig sync:** TiXL exports **MVR** (fixtures + GDTF types + positions + stage geometry) →
  drag-drop import in BlenderDMX. Later, **MVR-xchange** (which BlenderDMX supports) upgrades this to
  live rig sync instead of file export.
- **Control:** BlenderDMX listens to **Art-Net/sACN** — TiXL's existing output *is* the control
  bridge. Point-buffer flow → patch → universes → Eevee beams live, with zero Blender-specific code.
- What TiXL builds: MVR export (rides on the §4 GDTF/MVR adoption) and nothing else. What we get:
  photoreal previz (Cycles), volumetric beams (Eevee), and DMX-recording-to-keyframes on day one of
  fixture support.
- Native stage-view beam previz remains the long-term goal (one integrated tool), but it stops being
  a blocker for *anything* — it graduates from phase to "parity project", pulled by real need.

## 8. Phasing (each shippable, ordered by pipeline proximity)

1. **LED pixel mapping** — sampling-geometry entity + strip editor on the canvas + Art-Net/sACN
   output kinds in the setup; strips generate points into the as-built point→layout flow (§2).
2. **Stage model import + reference points** — glTF import, `StageModels`, `ReferencePoints` with
   image/model observations, snapping. Immediately improves projection workflows (place surfaces on
   real geometry) before any lighting exists; unblocks model-based calibration.
3. **Fixture placement + patch (GDTF/MVR)** — GDTF reader, fixture entities, patch migrates from
   op-side index-matching into the setup (§2); MVR import/**export** — export is the previz bridge
   (§7), so BlenderDMX previz ships *with this phase*, not after it.
4. **Beam previz, native** — parity project, demand-pulled (§7).
5. **Lasers** (ILDA, zones, safety interlocks).

## 9. Risks

- **Scope gravity, named honestly:** phases 3–5 border the feature set of dedicated previz suites
  (Capture, Depence, grandMA 3D). The defensible ambition for TiXL is *placement, patch, pixel
  mapping, and calibration in one venue file* — previz is outsourced to BlenderDMX (§7) until parity
  is genuinely demanded. Re-read this line when tempted.
- **DMX timing:** universes tick at ~44 Hz, decoupled from render rate — output scheduling needs its
  own cadence (same lesson as audio), and multi-machine sends belong to the machine that claims the
  node (`multi-machine.md` claims model extends to universes).
- **Laser safety is a liability, not a feature gap** — zones, interlocks, and "editor never emits
  ILDA without explicit arming" are prerequisites, which is why lasers are last.
- **Fixture library debt** is fully avoided only by GDTF (§3) — resist any "quick custom fixture
  json" shortcut that becomes a permanent parallel format.

## 10. Open questions

1. **Patch migration** (§2): how op-side index-matching maps onto setup fixture ids — index ranges
   claimed per fixture group? explicit point-index→fixture bindings? — and the back-compat path for
   existing projects using the op-side layouts.
2. Sampling geometry shape: polyline-with-pitch only, or grid/arbitrary-point sets from day one
   (leaning: polyline + grid; arbitrary points additive later).
3. Do pixel-mapped fixtures sample in *content* space (like slices) or *stage* space (project the
   strip onto a surface's content) — likely both, chosen per strip; decide with the strip editor.
4. Where the patch list lives in the UI (outliner shelf vs parameter-window list vs stage view side
   panel).
5. MVR import fidelity targets (fixtures + positions first; trusses/geometry as display-only context).
6. Whether FX1/FX2 point-channel semantics should be formalized (named channel map per fixture type)
   when the patch moves setup-side, so GDTF channel functions can bind to them declaratively.
