# ContextVariables

Context variables pass values down a graph without wiring them through every operator in between. This page explains how to set and read them, the render pass state that TiXL's own operators use the same way, and the variable names some built-in operators expect.

## How context variables work

Every evaluation carries a context. Operators that render or evaluate a subgraph hand that context down, so a value set further right in the graph is visible to everything evaluated inside it.

Use the set and get operators of the matching type:

- [SetFloatVar] / [GetFloatVar], [SetIntVar] / [GetIntVar], [SetBoolVar] / [GetBoolVar]
- [SetVec2Var] / [GetVec2Var], [SetVec3Var] / [GetVec3Var], [SetMatrixVar] / [GetMatrixVar]
- [SetStringVar] / [GetStringVar], [SetObjectVar] / [GetObjectVar]

Each type has its own set of names, so a float called `size` and an int called `size` are two different variables.

### Scoped or for the rest of the frame

A setter works in one of two modes, depending on whether its **SubGraph** input is connected:

- **SubGraph connected:** the value applies only while that subgraph is evaluated. Afterwards the previous value is restored. Use this to give one branch of a graph its own value.
- **SubGraph unconnected:** the value stays set for everything evaluated later in the same frame. A common pattern is a [Group] or [Execute] that runs several setters before the operators that read them.

All variables are cleared at the start of each frame. A getter that runs before its setter in a frame, or reads a name nobody set, returns its **FallbackValue**.

### Loops

[Loop] sets its index (int and float) and its progress (0 to 1) as variables for each iteration. The names are inputs of the loop, `i` and `f` by default. Read them with [GetIntVar] or [GetFloatVar] inside the looped subgraph.

## When to use them, and when not

Context variables are good for values that many operators deep inside a subgraph need, like a loop index, a color theme or a time offset. They keep the graph readable when the alternative is the same connection drawn to twenty operators.

They are a poor fit when the relation matters: a connection shows where a value comes from, a variable name does not. A typo in a name fails silently, and the getter returns its fallback. Prefer a connection when only one or two operators need the value.

Getters re-evaluate every frame, and so does everything that depends on them. This is cheap for a few getters, but a getter feeding an expensive image chain keeps that chain from being cached.

## Render pass state

Some values describe how the current pass is rendered rather than what the graph wants to compute. TiXL keeps these as typed render pass state instead of named variables. Read them with [GetRenderPassState] and pick the value from its dropdown:

| Value | Meaning |
|---|---|
| VelocityRequested | 1 if the surrounding [RenderTarget] renders a velocity buffer. |
| RepeatPass | Index of the current motion blur subframe ([RepeatFrame]); 0 for the frame itself. |
| CubeFace | Face rendered by [RenderToCubeMap], 0 to 5. |
| IsShadowPass | 1 while [SetShadow] renders its shadow map. |
| SubframeCount | Number of subframes [RepeatFrame] renders this frame; 0 outside. |

[SetRenderPassState] sets a value for its subgraph, or for the rest of the frame when its SubGraph is not connected, in the same way as the variable setters. TiXL's own operators use it, e.g. [SetShadow] marks the shadow pass and [TXAA] publishes its camera jitter. You will rarely need it in your own graphs.

Damping and spring operators ([Damp], [DampVec3], [Spring] and their variants) read the render pass to advance in smaller steps during motion blur subframes. That is why damped motion blurs correctly in [RenderWithMotionBlur] and keeps its speed at any pass count.

## Variable names used by built-in operators

A few built-in operators read or write named variables by convention. You can set or read them yourself with the matching set and get operators.

| Name | Type | Set by | Read by | Meaning |
|---|---|---|---|---|
| `SpeedFactorA`, `SpeedFactorB` | float | [SetSpeedFactors] | animators like [AnimValue], [AnimInt], [AnimVec3] with their speed factor set to A or B | Global speed multipliers, e.g. to change the tempo of all animations in a VJ setup at once. 1 if not set. |
| `__TriggerA`, `__TriggerB` | float | your graph (see the VJ examples) | [WasTrigger] with Trigger set to A or B | Shared trigger inputs, so one control drives many [WasTrigger] operators. |
| `_normalizedTime` | float | [TimeClip] | your graph | Progress through the time clip, from 0 at its start to 1 at its end. |
| `_PlayModeProgress` | float | skill quest levels | the editor | A level reports its progress to the play mode UI. |
| `__MotionBlurPass` | int | older graphs | [Loop] | Legacy. A [Loop] whose index variable has this name marks its iterations as motion blur subframes. New graphs use [RepeatFrame]. |
| `CubeFace` | int | [RenderToCubeMap] | your graph | Legacy. The face index is also available as CubeFace in [GetRenderPassState]. |

Names starting with an underscore are meant to be read, not set, unless the table says otherwise.

## For C# operator authors

In C#, variables are dictionaries on the context, e.g. `context.FloatVariables` and `context.IntVariables`. Render pass state is the typed field `context.RenderPass`:

```csharp
// Skip state updates in repeated evaluations of the same frame (further cube faces, older motion blur graphs).
if (context.RenderPass.IsRepeatOfFrame)
    return;

// Or advance by this evaluation's share of the frame (motion blur subframes):
var stepSec = context.RenderPass.GetStepDurationSec(Playback.LastFrameDuration);
```

An operator that renders its own pass saves the whole struct, changes its member and restores it afterwards:

```csharp
var keepRenderPass = context.RenderPass;
context.RenderPass.IsShadowPass = true;
SubGraph.GetValue(context);
context.RenderPass = keepRenderPass;
```

Add new engine state to `RenderPassState` rather than inventing another variable name. Names in strings are easy to mistype, hard to find and invisible in the operator's inputs.

## See also

- [Writing C# operators](WritingCodeOps.md)
- [Creating new operators](CreatingNewOps.md)
