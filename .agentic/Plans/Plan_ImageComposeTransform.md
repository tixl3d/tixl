# Image compose transform (context-carried, respected by all texture ops)

**Status:** Drafted 2026-06-04. Deferred — a large, cross-cutting initiative extracted from the video work
([`Plan_VideoClipPlayer.md`](Plan_VideoClipPlayer.md)), which introduces the same `ImageComposeTransform`
context field **minimally** (only the video ops consume it). This plan is about extending that to **every
texture-consuming operator**, so a context transform is honored graph-wide.

## Idea

Carry an **`ImageComposeTransform`** on `EvaluationContext` with a **neutral, meaningful default** (identity
UV transform, color/tint = 1, opacity = 1, normal blend, default sampler). Texture-consuming ops **read it
and apply it when they sample / render**, exactly as Scene `[Transform]` carries the world matrix for 3D. A
single **`TransformImage`** op manipulates it for its subgraph, so it affects every image/texture op in its
subtree with no per-op wiring.

## Why it's worth it

- **UV transforms are essentially free.** Offset / scale / rotate / mirror applied at *sample time* is just a
  coordinate transform inside the shader the op already runs — no extra pass, no intermediate render target
  (unlike chaining a dedicated transform op, which costs a full-screen blit). Tint / opacity / blend fold
  into the same sample.
- **One mechanism, graph-wide.** `TransformImage` (like `[Transform]`) transforms a whole image subgraph;
  ops no longer each need their own offset/scale/rotate inputs.
- **Consistent with the 3D side** — the same mental model as Scene `[Transform]`.

## Safe by default (the property that makes this tractable)

Because the context default is **neutral (identity / one)** and only a `TransformImage` in a subtree changes
it, **existing projects are unaffected** until someone introduces a `TransformImage`. There is no behavioral
migration of current graphs — adoption is purely additive: an un-migrated op ignores the (neutral) context
as today; a migrated op honors it only when non-neutral. This is what lets a 100+-op rollout proceed
incrementally without a flag day.

## Scope / cost

The bulk of the work is **~100+ texture-consuming operators** — their HLSL samplers plus the C# constant
wiring. Each must thread the context transform into its shader and apply it at sample time.

## Strategy

- **Shared helper, not per-op reinvention.** An HLSL include (e.g. `SampleWithComposeTransform(...)`) + a C#
  helper that packs the context `ImageComposeTransform` into a constant buffer. Ops adopt by switching their
  sampling call to the helper — minimal per-op change, uniform behavior, one place to get the math right.
- **Define the participation rule.** Which ops respect it: texture *sampling / drawing* ops (filters, blits,
  draws, generators that read textures). Pure-data / non-spatial ops do not. Document the rule so adoption is
  mechanical, not case-by-case judgement.
- **Reconcile with ops' own transform inputs.** Many image ops already expose offset/scale/rotate. Convention
  to settle: the context transform is the *ambient* transform inherited from ancestors and **composes with**
  the op's own local inputs (ambient ∘ local), matching how `[Transform]` composes down the scene tree.
- **Incremental, by category.** Phase 1: the field + the shared helper + the video ops (overlaps the video
  plan). Phase 2+: migrate texture ops in batches (generators → filters → renderers), each batch testable in
  isolation; un-migrated ops keep working by the safe-by-default property.

## Risks

- **Visual regressions** if an op applies the transform in the wrong space or order — mitigated by the
  neutral default (no transform ⇒ no change) plus per-batch visual tests.
- **Constant-buffer layout consistency** across many ops — the shared helper standardizes it.
- **Hot-path discipline** — the apply must stay allocation-free and inside the existing sample (no extra
  pass), per the engine's per-frame rules.

## Relationship to the video work

[`Plan_VideoClipPlayer.md`](Plan_VideoClipPlayer.md) introduces the `ImageComposeTransform` context field and
its **first consumers** (the `VideoClipPlayer` blit + `VideoClip` + `TransformImage`). It is **not blocked**
on this plan — video compositing needs only its own ops to honor the field. This plan generalizes consumption
to the remaining texture ops afterward, turning "free UV transforms" on everywhere.
