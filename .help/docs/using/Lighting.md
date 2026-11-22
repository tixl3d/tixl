# Lighting

This page explains how lights work in TiXL: which light types exist, how a light reaches the objects it
illuminates, how to aim lights with the gizmo, and which draw operators respond to which lights. Read it when a
scene looks too dark, too flat, or when a light seems to have no effect.

## Light types

TiXL has three kinds of lights. Each comes as a pair: an operator with a gizmo for placing it in the Output
window, and a `Set…` operator without a gizmo for when the values come from somewhere else.

| Light | With gizmo | Without gizmo | Use it for |
|---|---|---|---|
| Point | [PointLight] | [SetPointLight] | Bulbs, glowing objects, fill light. Shines in all directions. |
| Spot | [SpotLight] | [SetSpotLight] | Stage lights, torches, a pool of light on the floor. Shines in a cone. |
| Directional | [DirectionalLight] | [SetDirectionalLight] | Sunlight or moonlight. Parallel light from one direction. |

### Point lights

A point light sits at **Position** and shines equally in every direction. Its brightness falls off with distance:

```
brightness = Intensity / ((distance / Range) ^ Decay + 1)
```

- **Range** is the distance at which the light has dropped to half its intensity.
- **Decay** controls how fast it fades beyond that. 2 is close to real light; lower values reach further.

### Spot lights

A spot light shines from **Position** toward **Target** in a cone.

- **ConeAngle** is the full opening angle in degrees.
- **Softness** ([SetSpotLight] only) is the part of the cone, from its edge inwards, over which the light fades
  out. 0 gives a hard edge, 1 fades all the way from the center. [SpotLight] uses a slightly soft edge.
- **Range** and **Decay** work as for point lights.

### Directional lights

A directional light lights every surface from the same direction with the same strength, no matter how far
away it is. Only the direction from **Position** to **Target** matters. Position just decides where the arrow
gizmo is drawn, so put it somewhere you can see it.

## How a light reaches your objects

A light operator affects only the operators connected to its **Command** input, and everything below them.
Draw operators evaluated anywhere else don't see it. This lets you light parts of a scene separately:

- To light a whole scene, put the lights directly above the camera, so everything drawn through the camera is
  inside them.
- To light one object only, put a light right above that object's draw operator.
- Lights stack. Chaining several light operators adds them all to the subgraph below the last one.

Up to **8 lights** can be active at the same time, in any mix of types. A ninth light is ignored and a warning
appears in the console.

### When there are no lights

A scene without any light operator is not dark. TiXL then uses two default point lights, one above and one
below, so that newly drawn meshes are visible. As soon as you add your own light, the defaults are gone.

Every scene is also lit by an environment map, which adds soft light and reflections from all directions. A
default one is used until you add [SetEnvironment]. If a scene still looks lit after you disabled all lights,
this is why. Lower **Exposure** in [SetEnvironment] for a darker look.

## Placing and aiming lights

Select a light operator with a gizmo while the Output window shows your scene:

- With the **Move** gizmo you drag the light's Position. The Target stays where it is, so a spot or directional
  light keeps pointing at the same spot while you move it.
- With the **Rotate** gizmo you swing the Target around the light. The colored rings rotate around the world
  axes, the outer ring rotates around your view direction. Hold `Ctrl` to snap to 15 degree steps.

Each drag is one undo step. To point a light at a moving object, connect the object's position to the light's
Target, for example from a [Locator] or a [Vector3Gizmo].

**ShowGizmo** decides when the gizmo is drawn: always, never, only while the operator is selected, or following
the gizmo toggle of the Output window. **GizmoSize** only changes the drawing.

## Which draw operators respond to which lights

Draw operators fall into two groups.

**Physically based (PBR) shading** handles all light types correctly:

- [DrawMesh], [DrawScene], [CustomDrawMesh], [DrawMeshAtPoints]
- [DrawPointsShaded], [DrawLinesShaded]
- [RaymarchField]

**Stylized and specialized shading** has its own lighting and only knows point lights for now:

- [DrawMeshCelShading], [DrawMeshHatched], [DrawMeshWithShadow]
- [DrawMeshAtPoints2], [DrawMeshChunksAtPoints]
- [DrawTubes], [DrawRibbons], [DrawPointSpritesShaded]

These treat a spot light as a point light at its position, without the cone. A directional light reaches them as
a very distant point light at half its intensity, which looks similar to sunlight but not identical. If a
stylized draw needs exact spot or sun lighting, use a point light placed accordingly.

Unlit operators such as [DrawMeshUnlit] or [DrawPoints] ignore lights completely.

## Materials

How a surface responds to light is set with [SetMaterial]: base color, emissive color, roughness, metalness and
specular. Texture maps can replace the base color and emissive color, add surface detail through a normal map,
and drive roughness, metalness and occlusion from one combined map. Put it above the draw operators it should apply to, in the
same way as lights. Rough surfaces spread a light's highlight into a soft sheen, smooth and metallic ones show
a small, bright highlight and reflect the environment.

Emissive color is added on top of the lighting, so emissive parts glow even in a dark scene. They don't light
other objects, though. For that, add a point light next to them.

## Reflections and materials

[ScreenSpaceReflections] adds reflections of what is visible on screen. By default it treats the whole scene as one
material, set by its own Roughness and Reflectivity. Turn on **WithMaterialBuffer** in [RenderScene] and it uses
each surface's [SetMaterial] instead: rough surfaces get blurred and weaker reflections, very rough ones none, and
metals reflect in their base color. Only physically based draw operators write the material; everything else keeps
the effect's parameters.

## Shadows

[SetShadow] renders a shadow map from a light position toward a target. Only [DrawMeshWithShadow] receives it,
and the shadow is independent of the light operators: place it to match the light you want to cast shadows. For
soft contact shadows on any geometry, [ScreenSpaceLighting] and ambient occlusion often work better.

## Lights and blend modes

Draw operators with the blend mode Normal or None write their surfaces into the normal and velocity buffers of
the [RenderTarget], which screen space reflections, ambient occlusion and motion blur rely on. Additive,
multiply and the other blend modes leave those buffers alone, so glowing particles drawn on top of a scene
don't disturb the effects underneath.

## Performance

Every light costs a little time per pixel for every lit surface. With 8 lights this is rarely a problem on
current graphics cards. Spot lights cost slightly more than point lights, directional lights slightly less.
If a scene gets slow, check the number of pixels drawn first (resolution, multi-sampling, overlapping
transparent layers) before removing lights. See [Optimizing rendering performance](OptimizingRenderingPerformance.md).

## See also

- [Real-time rendering for artists](RealtimeRendering.md)
- [Procedural geometry](ProceduralGeometry.md)
