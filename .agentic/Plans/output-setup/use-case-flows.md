# Output Setup — Use-Case Click Flows (divergent exploration, 2026-07-29)

Nine end-to-end flows across the long-term scope, from trivial to touring rig. Coarse granularity;
**the point is mining potentials and gaps, not specification** — each flow ends with the ideas it
surfaced (💡). Cross-cutting harvest at the end. Assumes the target design: flow outliner strip,
properties in the Parameter window, unified canvas ([`ui-restructuring-plan.md`](ui-restructuring-plan.md)),
plus the long-term docs ([`camera-calibration.md`](camera-calibration.md),
[`multi-machine.md`](multi-machine.md), [`Plan_StageExtension.md`](../Plan_StageExtension.md)).

**Flow priority (2026-08-29):** the first *concrete* click-flow spec (drafted separately by pixtur,
in progress) is **live-first** — projector plugged in and lit, the flow you'd demo. Prep-at-home /
venue-swap flows (Flow 5 territory) come after; the Setup↔MachineConfig split already carries them,
so live-first creates no design debt. Two behaviors settled with it: the **first `SendToOutput`
auto-connects full-frame to the first output** (zero-config path — Flow 1's premise made explicit),
and the auto-created routing must be **visible** (flow edge, not silent magic), or users never form
the routing model that multi-output setups need.

---

## Flow 1 — "Just show my stuff on the 2nd display" (~8 steps)

*Bedroom, laptop + HDMI monitor. User knows ops, has never seen the output system.*

- New project; patches some visuals.
- Adds `SendToOutput` after the final op (or: right-click op → "Send to output").
- Output window opens/focuses, shows the content full-frame — a default setup with one Default
  output was created silently.
- Toolbar: clicks the binding dropdown → sees `Local / Display 2 (1920×1080)`.
- Picks it; display 2 goes live windowed.
- Clicks fullscreen toggle.
- Keeps patching; output follows.
- Saves. Done — **zero setup concepts encountered** (no surface, slice, or outliner needed).

💡 **Potentials**
- The `Machine / Display` label (multi-machine §4) already earns its keep here — "Local / Display 2"
  is self-explanatory in a way "Display 2" + separate output concept never was.
- Auto-created implicit setup must be *upgradeable in place* — when this user later opens Edit Setup,
  their world (1 content, 1 output, bound) is already correctly represented. No "convert project?"
  moment, ever.
- Idea: `SendToOutput` created with an unbound default output could pulse the binding dropdown once —
  the single affordance that matters next.

## Flow 2 — Sofa projector, keystone by eye (~14 steps)

*Living room, cheap beamer at an angle onto the wall.*

- As Flow 1, but the picture lands trapezoid-distorted on the wall.
- Opens Edit Setup mode (toolbar); flow strip appears: content → output, no surface yet.
- Clicks `+` in SURFACES; a surface appears, auto-mapped full-frame to the output, auto-fed by the
  content — the picture doesn't change (safe default).
- Canvas: drags the four corner handles until the picture sits square on the wall, by eye.
- Nudges one corner with arrow keys for the last centimeter — or picks up a **game controller**:
  shoulder buttons cycle the corners, thumb stick nudges, all while standing at the wall.
- Notices content stretched; opens Parameter window → surface card → sets Size to the wall's real
  proportions (`2.4m × 1.35m`); content re-fits.
- Toggles the raster overlay to verify straightness; toggles off.
- Collapses the strip; fullscreen; watches a movie of rotating cubes.

💡 **Potentials**
- **"+ Surface" must be a non-event visually** — inherit the current full-frame routing so adding
  structure never breaks the picture. (Already the plan's instinct; the flow confirms it's critical.)
- Arrow-key nudge on selected corner (with shift = coarse) — tiny, essential for by-eye work.
- **Game-controller calibration** (see harvest §H) — two analog sticks make headless by-eye
  calibration a two-hands-at-the-wall activity instead of a walk-back-and-forth loop.
- Aspect guard: subtle indicator when corner-pin distortion exceeds ~n% — catches "why does it look
  squished" before the user knows to ask.
- Idea: press-and-hold a corner handle → magnifier inset around the physical corner (using output
  self-view), for one-person calibration at distance.
- **2026-08-31:** the first keystone no longer *requires* the surface — the direct pipe's **patch
  quad** (data-model §2.5) covers corner-pin-by-eye. Flow 2's "+ Surface" now enters at "sets Size
  to the wall's real proportions" — the first *meters* feature — and the patch quad promotes
  verbatim.

## Flow 3 — Gallery window from a phone photo (~22 steps)

*Shop-window installation; projector mounted overhead at a steep angle; artist has 20 minutes.*

- Flow 2 through surface creation.
- Takes a phone photo of the (blank) shop window from across the street.
- Drags the photo into the output window → becomes a Reference Image; reference canvas opens.
- Traces 4 lines along the window frame edges (straight in reality); types the real width of one
  edge (`1.8m`).
- Clicks **Straighten** — keystone solved; clicks **Apply lengths** — surface gets true size/aspect.
- Canvas morphs to the straightened view; content already fits.
- Adds a second, smaller surface for the side panel; drags its corners roughly; snaps its edge
  level with the main surface (guide line appears).
- Content: splits the texture — drags a slice rect over the left half for the window, right half
  for the side panel; drags slice → surface in the strip to connect the second one.
- Walks outside, checks alignment; nudges one corner via laptop.
- Sets output to autostart fullscreen on this display; saves; leaves.

💡 **Potentials**
- Phone→editor photo transfer is a real-world friction point. Idea: "Import from phone" QR code that
  opens a local-network upload page. Disproportionate delight for on-site work — but honestly a
  **heavy feature** (an embedded web server + a maintained mobile surface); if built, scope it to
  upload-only first and treat any remote-control ambitions (harvest §G) as separate.
- Length entry belongs *on the line annotation* (inline field at the line), not in a dialog.
- "Autostart fullscreen on project open" is an installation-mode essential — belongs on the output
  card, and a headless/kiosk player mode is the natural extension.
- The straighten flow is already the strongest teaching moment in the system — the Flow 2 user who
  finds photos can self-upgrade. Keep the two entry points (by-eye vs photo) visibly parallel.

## Flow 4 — Venue swap: same show, next city (~12 steps)

*The Flow 3 installation travels to a second gallery.*

- Opens project; Setup dropdown → **Duplicate "Gallery A" → "Gallery B"**.
- A readiness panel lists what's venue-bound: 2 surfaces (calibration stale), 1 output binding
  (missing display), reference image (wrong venue).
- Binds the output to the new projector (`Local / Display 2`).
- New phone photo → replace reference image → re-trace → Straighten/Apply.
- Second surface: corners by eye (it's small); snap to level.
- Readiness panel all green.
- Test run, save, done. Content, slices, routing: untouched — **only physics was redone.**

💡 **Potentials**
- **The readiness/pre-flight panel is the idea this flow demands** (see harvest §A) — venue swap is
  exactly "a checklist of what's no longer true."
- "Replace reference image, keep annotations as starting points" — annotations re-anchor onto a new
  photo instead of starting from zero (patch-match assist once cameras exist).
- Setup dropdown could show per-setup readiness badges — pick the venue *and see its health* in one
  place.

## Flow 5 — Club night: blended wall + LED strips (~40 steps)

*Two projectors side-by-side on one wide wall, soft-edge overlap; four LED strips behind the bar on
an Art-Net node. One VJ, one afternoon.*

- New project; content patched; Edit Setup; two outputs created, bound to both projectors.
- Fires the built-in **test pattern** (per-output color + grid + output name) — confirms which
  projector is which without walking over.
- Creates one wide surface (`6m × 2.5m`); maps it to Output A (corner pin left half by eye), adds
  second mapping to Output B (right half).
- Overlap region shows double-bright; opens the mapping edge in the strip → Parameter window shows
  the mapping card → enables **soft-edge mask**; drags the feather width until the seam vanishes.
- Repeats feather tune with the blend test pattern (gradient bars across the seam).
- The wall isn't quite flat — enables a **warp lattice** (2×3) on Output B's mapping and pulls two
  lattice points until the grid lines meet across the seam.
- Content: one slice across the full wall; checks motion continuity across the seam.
- LED strips: `+` in FIXTURES → "LED strip (pixel-mapped)"; draws the strip polyline along the
  bar geometry on the canvas; sets pixel count (144) and pitch from the hardware label.
- Duplicates ×3; arranges; assigns each to the Art-Net node (`Bar-Node / Universe 1–2`,
  auto-addressed sequentially).
- **Identify chase**: strip flashes its index sequence — discovers strip 3 is wired backwards;
  clicks "reverse direction" on its card.
- Sampling: strips sample the wall's content space (option A) — bar continues the wall visuals;
  previews; switches strip 4 to procedural points (op-driven) for a strobe accent.
- Patches point FX in the graph (noise on color, beat-driven); strips react.
- The projectors don't match: Output B runs warmer and dimmer — adds a **color correction** on its
  mapping (gain + temperature) until a white test field matches across the seam.
- Show prep: snapshot a few looks; MIDI-maps blend of two content sends; runs an hour.
- Doors. During the show: one projector nudged by a guest — grabs the master, **rotate/pan
  quick-fix** on Output B's mapping (all corners together), 20 seconds, fixed.

💡 **Potentials**
- **Test patterns and identify-flash are infrastructure, not polish** — every flow past trivial
  needs "which physical thing is this entity": outputs (name+grid), fixtures (chase), surfaces
  (outline flash). One consistent "Identify" verb everywhere (right-click → Identify).
- **The mapping modifier stack (warp, mask, color) is what "advanced" runs on** (harvest §I) —
  this flow alone touches all three; without them it dead-ends at "almost right."
- Auto-addressing with visual confirmation beats address spreadsheets; "reverse direction" and
  "serpentine" toggles are mandatory strip-reality features.
- The rotate/pan quick-fix (already in long-term-features) earns "show-night panic button" status:
  big hit area, works on all corners of a mapping at once, coarse by default.
- Blend feather wants its *own* test pattern; pattern choice should follow the tool in use.

## Flow 6 — Facade mapping with webcam calibration (~35 steps)

*Small festival, building facade, one projector, one calibrated webcam, tripods.*

- Project; content; output bound.
- Reference: daylight photo of the facade; traces point annotations on 8 distinct features
  (window corners, ledge ends); enters two known lengths.
- Camera: `+` Camera → intrinsics wizard (holds checkerboard, live coverage feedback, ~2 min).
- Evening. Camera on tripod → **Pose camera**: patch-matching finds 6 of 8 reference points in
  the live frame (2 occluded); PnP solves pose; residual shown green.
- **Calibrate projector**: structured-light run (~20 s of patterns); solve completes; surfaces
  snap to the facade's features — corner pins done, no hand-dragging at all.
- Reviews in the unified canvas: output view vs camera view overlay toggle; one balcony region
  slightly off — cycles to its corners with the game controller and nudges them from the audience
  position (manual edits layer *over* the solve).
- Builds region structure on the now-accurate surface (windows as sub-regions, snapped); routes
  different slices per region.
- **Neutralize colors**: the facade's sandstone tints everything warm — runs the reference-based
  color solve (projects grey ramps, camera compares against neutral) → per-mapping correction
  brings projected greys back to neutral.
- Content polish; walks the audience area; happy.
- Next evening: **Verify calibration** (one button): camera recaptures, 1 surface drifted
  (projector settled on its scaffold) → accepts suggested correction.
- Show. Camera stays mounted; drift check re-run at each doors-open.

💡 **Potentials**
- "Manual edits layer over the solve" is the trust-preserving rule: a solve is a *starting point the
  user can always overrule*, and re-solving asks before discarding manual deltas.
- Camera-view overlay on the unified canvas (see what the camera sees, with entities projected into
  it) is the debugging view for everything CV — worth designing as a first-class view, not a dialog.
- Occlusion tolerance (6 of 8 points) must be the normal path, with per-point match confidence shown.
- One-button **Verify calibration** with per-surface green/amber/red is the drift feature's whole UI.
- **Reverse color grading from reference/camera** (harvest §J) — surface tint and projector color
  drift are solvable with the same camera that solves geometry.

## Flow 7 — Theatre: 3D stage, moving heads, BlenderDMX previz (~50 steps)

*Mid-size theatre production. Set designer has a Vectorworks plot. Lighting: 12 moving heads, 8 LED
bars. Projection: 2 projectors on the set. Previz needed weeks before stage access.*

- Project; **Import MVR** from the set designer → stage model, trusses, 12 fixtures with GDTF
  types and positions appear in the stage view.
- Reviews import diff panel ("12 fixtures, 2 unknown GDTF types → placeholder"); fetches the 2
  types from GDTF Share in-app.
- Adds the 8 LED bars manually (GDTF picker → drag onto truss positions in the stage view; snap
  to truss).
- Patch: opens patch list; auto-address per universe; conflicts flagged; drags two fixtures to
  another universe.
- Projection: places 2 projector outputs in the stage view aiming at the set piece; creates
  surfaces on the set-piece faces of the 3D model (click face → surface from face).
- Choreography: point-buffer ops target fixture groups (heads follow a curl-noise path, color
  from a gradient sampled by position); scrubs the timeline; values flow.
- **Previz**: clicks "Previz in Blender" → MVR export + connection hints; BlenderDMX imports the
  rig; TiXL's Art-Net loops back locally; Eevee shows beams live against the stage model.
- Iterates choreography watching Blender; director signs off on look 3; renders a Cycles still
  for the production meeting.
- Stage access day: venue-duplicates the setup; real positions differ from plot — re-poses 3
  trusses (readiness panel flagged them via reference photos of the build); moving-head calibration:
  aims 3 known points per head → pose refined.
- Projector calibration via webcam (Flow 6 path) against the *stage model* geometry.
- **Brightness balance**: the two projectors throw at very different distances — accepts the
  suggested per-mapping gain from the solved poses (distance + pixel density), then trims by eye.
- Dry run with previz side-by-side (real stage vs Blender) to spot dead pixels/beam clipping.
- Show file locked; operator gets the readiness panel as their pre-show ritual.

💡 **Potentials**
- **MVR import diff** (new/moved/unknown) rather than silent import — plots get revised weekly;
  re-import must be a merge, not a replace. This is the fixture analog of reference-point re-anchoring.
- "Surface from model face" (click a face of the stage model → surface with correct size and pose) —
  probably the single biggest time-saver 3D models enable for projection.
- Fixture-group targeting for point buffers ("these 12 points → group Heads") is where the patch
  (§Plan_StageExtension) meets the graph — the binding UI for it deserves early design attention.
- "Previz in Blender" as a *product verb* (one button: export + launch + connect) — the bridge only
  feels free if it's one click, not a 6-step recipe in the docs.
- Moving-head pose refinement by aiming at known points = the fixture version of projector
  calibration; same reference points, same math family. Plan the solver interface to serve both.
- **Photometric compensation from solved poses** (harvest §J) — once geometry is solved, throw
  distance and px/m per surface are *known*; brightness equalization becomes a computable suggestion.

## Flow 8 — Touring multi-machine rig (~60 steps)

*Arena support act. Master laptop + 2 render clients. 4 projectors (2 per client), LED backwall on a
node, camera on FOH. New venue every night.*

- Shop prep: project built; setup with 4 outputs, wall/backdrop surfaces, LED backwall fixture;
  all outputs bound to `Local` for bench testing.
- Enrolls clients: runs TiXL Player on Client A/B → each shows an enroll code; master adds them;
  they appear in the outliner's machine groups (`ClientA / DP-1…`).
- Drags outputs onto machines in the outliner: Out 1–2 → ClientA, Out 3–4 → ClientB; backwall
  node claimed by ClientA (it's on that switch).
- **Deploy**: one button; per-machine progress; clients report ready; heartbeats green.
- Bench run: master clock + events stream; walks displays with identify patterns; measures
  chain latency per client with the camera (flash pattern) — ClientB is 1 frame behind → offset
  compensated automatically.
- Determinism check flags one surface spanning ClientA/ClientB with feedback-based content →
  re-routes that content via NDI (per the seam rule); flag clears.
- Venue night 1: duplicate setup ("Tour" → "Arena Graz"); readiness panel drives the evening:
  bind real projectors per client, camera-calibrate each (patterns display on *remote* outputs,
  solves run on master), backwall node IP updated.
- Soundcheck: latency re-measured; snapshot looks; show file locked.
- Show: mid-set, ClientB heartbeat goes amber (GPU thermal); master pre-arms understudy…
  (long-term) or drops ClientB's outputs to reduced content; recovers; note logged.
- Venue night 2 (repeat of the venue block, now ~20 minutes): duplicate from *Graz* (closest
  venue), readiness panel shorter; done before dinner.
- End of tour: setups per venue archived with the project; next tour starts from the best one.

💡 **Potentials**
- Enroll-code pairing (not config files) is the fleet UX bar set by every modern device ecosystem.
- **The readiness panel becomes the operator's main screen** in touring reality — pre-show ritual,
  not a dialog. It should be printable/exportable (production managers ask).
- Latency auto-compensation per machine (measure → offset presents) turns the camera timing feature
  from diagnostic into an actual fix.
- The determinism seam check (multi-machine §6) works best as a *live lint* in the outliner —
  warning badge on the edge that crosses machines, with "route via NDI" as the offered fix.
- Venue-to-venue duplication ("start from closest venue") suggests setups want lightweight lineage
  metadata (duplicated-from, date) — nearly free, helps tours.

## Flow 9 — Output packing: TV wall behind a split matrix (~18 steps, added 2026-08-31)

*Particle sim rendered high-res; shown on an arrangement of old TVs driven by a 4×4 HDMI split
matrix. The output is one 4K display; the physical layout is irregular.*

- Send op exists, direct-piped to `2nd Display (4K)` (Flow 1 state).
- Output card context menu → **Split 4×4** — sixteen **patches** tile the canvas (data-model §2.5);
  each maps to one matrix input.
- Photographs the (dark) TV arrangement; drags the photo in → **reference jig**: a `Render`-off
  surface with the straightened photo as backdrop (data-model §2.10).
- Traces each TV as a region over the photo; the **content footprint** defaults to a content-aspect
  rect around the traced bounding box, nudged to taste.
- **Generate slices from regions** — sixteen source slices, bezel-correct, named after their
  regions, linked (dashed derivation wires on the board).
- Routes slices → patches with **Identify**: click a patch, that TV shows its number, drop the
  matching slice on it. Sixteen screens in ~a minute instead of cable archaeology.
- Checks the wall; one TV was re-hung — nudges its traced region, **Update slices from regions**;
  done.

💡 **Potentials**
- **The jig is the venue-portability story for pixel installs**: next venue = new photo, retrace,
  regenerate — routing and patches survive untouched.
- Matrix **presets** (`4×4 @ 480p`) next to the surface size presets — same discoverability pattern.
- Aspect-mismatch readiness hint per route (slice 16:9 → patch 4:3) — the packing twin of the
  corner-pin aspect guard.
- Patch overlap = free PIP; painter's order = list order (reorder UI only when a flow demands it).
- Identify gets its strongest justification yet (harvest §B) — here it's not a convenience, it *is*
  the cabling workflow.

---

## Cross-cutting harvest

**A. The readiness / pre-flight panel** (flows 4, 7, 8; implied in all): one aggregated view of
"what's not true anymore" — unbound outputs, stale calibration, missing devices/machines, drifted
cameras, determinism seams, unknown GDTF types. Every long-term feature *feeds* it; venue swap and
show-night are *driven* by it. Likely the highest-leverage single UI in the long-term scope, and it
can start tiny (unbound outputs + unbound content) inside the current branch's model.

**B. Identify, everywhere** (flows 5, 7, 8): one verb — flash/outline the physical counterpart of
any entity (output pattern, fixture chase, surface outline, machine's displays). Context menu +
readiness panel integration.

**C. Test patterns as a system** (flows 5, 6): pattern follows tool — output-identify grid, blend
gradient, calibration structured light, strip index chase, grey ramps for color solves. One
generator, tool-aware selection.

**D. Progressive disclosure ladder holds** (flows 1→3): display → output → surface → slice →
reference → camera → stage — each concept appears only when its problem appears, and each flow's end
state is a valid starting point for the next flow. This is the acceptance test for the whole design:
*no flow forces a concept the scenario doesn't need.*

**E. Solves are starting points** (flows 6, 7): every automated result (straighten, camera solve,
MVR import, auto-address, photometric suggestions) must be manually overridable, and re-running must
merge/ask, never clobber. One consistent rule across CV, import, and auto-layout.

**F. Physical-world friction is the real competitor** (flows 3, 5, 6): phone photo import, walking
distance to the wall, which-cable-is-which, wired-backwards strips. Features that eliminate a *walk*
(magnifier on corner, identify, controller-in-hand nudging) may beat features that add capability.

**G. A phone/tablet remote** keeps almost surfacing (corner nudge at distance, readiness check from
FOH, photo import). Not planned anywhere — noted as a recurring pull, not a commitment. The QR photo
upload (flow 3) is the smallest honest slice of it; anything more is a product decision.

**H. Game-controller calibration** (flows 2, 6): a stock gamepad as the headless calibration tool —
shoulder buttons / d-pad cycle anchor points (corners, lattice points, annotation endpoints), one
analog stick for coarse, the other for fine nudging, triggers to switch surface/mapping. Cheap
hardware, zero UI chrome, works standing at the wall or in the audience position. Fits the existing
sub-element selection plane (`SelectionTarget` cycling is exactly "next anchor point") — likely a
surprisingly thin feature over the Phase C canvas, and TiXL's op-side gamepad input suggests device
handling exists to borrow.

**I. Mapping modifier stack is the "advanced" backbone** (flows 5, 6, 7): warp lattices,
sub-division refinement, masks/feather, color correction — the already-planned `OutputMapping`
growth (data-model §2.3). The flows confirm the stack is *sequenced right after* the restructuring:
flow 5 dead-ends without masks + warp + color, and the Parameter-window move (Phase A) exists
precisely to give this stack room. Modifier UI should read as a stack of collapsible sections on the
mapping card, each with its matching test pattern (§C).

**J. Photometric compensation** (flows 6, 7 — advanced, camera-enabled):
- **Brightness from geometry**: solved projector poses give throw distance, incidence angle, and
  px/m per surface → compute relative luminance and *suggest* per-mapping gain so multi-projector
  setups match (then trim by eye; §E applies). No camera needed once geometry is solved.
- **Reverse color grade from reference images**: project grey ramps / color fields, capture with the
  camera, solve a per-mapping correction (matrix or small LUT) toward neutral greys — compensating
  both projector color drift and tinted physical surfaces (sandstone, painted walls). Honest limits:
  a webcam gives *relative* neutrality, not colorimetry — aim for "matched and neutral-ish," not
  calibrated color. Storage-wise this is the color member of the mapping stack (§I), solver-written
  like every other solve (§E).
