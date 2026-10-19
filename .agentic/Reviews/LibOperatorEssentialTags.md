# Essential tag proposal for Lib operators

Essential operators rank 1.5x higher in search and are what the graph operator browser suggests per namespace.
This proposal is derived from how often an operator appears in the HowTo tutorials and in `*Example` / HandsOn projects in `Operators/Examples`.
It is a starting point for curation, not a decision. Edit the **Decision** column (`yes` / `no`), then the tags can be applied in one pass.

- **add**: not Essential yet, used in 2+ HowTos, or 1 HowTo and 3+ examples, or 6+ examples. Internal, Obsolete, NeedsFix, Experimental and Research ops are excluded.
- **review**: Essential today but used in no HowTo and at most one example. May still be essential; low tutorial coverage is also a signal for the tutorials.

## Current Essential count per namespace

| Namespace | Essential |
|---|---|
| field | 22 |
| flow | 3 |
| image | 55 |
| io | 3 |
| mesh | 20 |
| numbers | 61 |
| particle | 7 |
| point | 46 |
| render | 27 |
| string | 14 |

## field

| Proposal | Operator | Folder | Current tags | HowTos | Examples | Summary | Decision |
|---|---|---|---|---|---|---|---|
| review | ApplyVectorField | field/use | Essential | 0 | 1 | Orients points along the vectors of a field, and can write the vector length or distance i | |
| review | PushPullSDF | field/adjust | Essential | 0 | 1 | Makes the incoming SDF volume thinner or thicker by adding a constant to its distance. | |
| review | ReflectField | field/space | Essential | 0 | 1 | Mirrors the incoming field at a plane, so the part in front of the plane is copied to the  | |
| review | HeightMapSdf | field/generate/sdf | Essential | 0 | 0 | Generates a terrain-like SDF from a height map texture. | |
| review | InvertSDF | field/adjust | Essential | 0 | 0 | Inverts the incoming SDF by negating its distance, swapping inside and outside. | |
| review | RepeatField3 | field/space | Essential | 0 | 0 | Repeats the incoming field infinitely along all three axes. | |
| review | RotateField | field/space | Essential | 0 | 0 | Rotates the incoming field around the X, Y and Z axes. | |

## flow

| Proposal | Operator | Folder | Current tags | HowTos | Examples | Summary | Decision |
|---|---|---|---|---|---|---|---|
| add | Execute | flow | Advanced | 5 | 62 | Executes the connected commands and then restores the graphics context. It can be used for | |
| add | GetFloatVar | flow/context | Advanced | 1 | 10 | Reads a float variable from the evaluation context. | |
| add | Once | flow | Advanced | 0 | 13 | Outputs true for a single update when the Trigger input changed, otherwise false. | |
| review | TimeClip | flow | Essential | 0 | 0 | Creates a time clip bar within the DopeView of the Timeline, similar to how video editing  | |

## image

| Proposal | Operator | Folder | Current tags | HowTos | Examples | Summary | Decision |
|---|---|---|---|---|---|---|---|
| add | Bloom | image/fx/blur | - | 2 | 14 | Adds a glow around bright areas of the image. A more versatile and faster version of [Glow | |
| add | ToneMapping | image/color | Advanced | 2 | 5 | Converts high dynamic range (HDR) colors into the displayable 0 to 1 range while preservin | |
| add | CombineMaterialChannels | image/use | Advanced | 1 | 3 | Combines roughness, metallic and ambient occlusion maps into a single texture for PBR mate | |
| add | ConvertFormat | image/color | Advanced | 1 | 3 | Copies a texture into another texture format. | |
| review | AdvancedFeedback | image/fx/feedback | Essential | 0 | 1 | A more versatile but harder to control version of [FluidFeedback] that also amplifies edge | |
| review | BlendImages | image/use | Essential | 0 | 1 | Cross-fades between the connected images using a float index. | |
| review | StarGlowStreaks | image/fx/stylize | Essential | 0 | 1 | Renders star-shaped glow streaks from bright areas of the incoming image. | |
| review | Tint | image/color | Essential | 0 | 1 | Maps the dark and bright tones of an image to two colors. | |
| review | AdvancedFeedback2 | image/fx/feedback | Essential | 0 | 0 | An improved feedback effect that adjusts the feedback buffer in OKLab color space. | |
| review | AfterGlow2 | image/fx/feedback | Essential | 0 | 0 | Creates an afterglow effect that leaves fading, blurred trails behind moving images. | |
| review | KeyColor | image/color | Essential | 0 | 0 | Keys out a color or a range of colors in an image, for instance for blue screen or green s | |
| review | LoadImageFromUrl | image/generate/load | Essential | 0 | 0 | Loads an image from a URL as a texture. | |
| review | MosiacTiling | image/fx/stylize | Essential | 0 | 0 | Subdivides the incoming image into recursive square tiles, with smaller tiles where the im | |
| review | PolarCoordinates | image/fx/distort | Essential | 0 | 0 | Converts an image between rectangular and polar coordinates. In other words, it wraps a st | |
| review | RgbTV | image/fx/glitch | Essential | 0 | 0 | Creates a vintage CRT TV effect with RGB pixel pattern, noise, distortion and glitches. | |
| review | VoronoiCells | image/fx/stylize | Essential | 0 | 0 | Creates a Voronoi cell pattern where each cell is filled with the color of the incoming im | |

## mesh

| Proposal | Operator | Folder | Current tags | HowTos | Examples | Summary | Decision |
|---|---|---|---|---|---|---|---|
| add | SplitMeshVertices | mesh/modify | Advanced | 1 | 4 | Gives every face of a mesh its own three vertices, so faces no longer share vertices. | |
| review | MeshProjectUV | mesh/modify | Essential | 0 | 1 | Overwrites the UV coordinates of a mesh with a planar projection. | |
| review | TextureDisplaceMesh | mesh/modify | Essential | 0 | 1 | Displaces the vertices of a mesh by a texture that is projected onto it from the front. | |
| review | VisualizeMesh | mesh/_ | Essential | 0 | 1 | Draws debug overlays for a mesh: wireframe, vertex indices, tangent space axes and vertex  | |

## numbers

| Proposal | Operator | Folder | Current tags | HowTos | Examples | Summary | Decision |
|---|---|---|---|---|---|---|---|
| add | PerlinNoise3 | numbers/vec3 | - | 7 | 32 | Generates three continuously and randomly changing values based on Perlin noise. | |
| add | PerlinNoise2 | numbers/vec2 | - | 2 | 6 | Generates two continuously and randomly changing values based on Perlin noise. | |
| add | SampleCurve | numbers/curve | Advanced | 2 | 0 | Samples an editable curve at position U and outputs the value. | |
| add | Compare | numbers/float/logic | Advanced | 1 | 4 | Compares two float values, outputting a boolean. | |
| add | AnimInt | numbers/anim/animators | - | 1 | 3 | Generates a beat-synced integer that counts up in steps. | |
| add | RgbaToColor | numbers/vec4 | - | 1 | 3 | Creates a color from red, green, blue and alpha values. | |
| add | Vec2ToVec3 | numbers/vec2 | - | 1 | 3 | Combines a Vector2 and a float into a Vector3. | |
| add | Vector3Components | numbers/vec3 | - | 1 | 3 | Splits a Vector3 into its X, Y and Z values. | |
| add | Ease | numbers/float/process | - | 0 | 9 | Creates smooth eased transitions whenever the input value changes. | |
| add | Vector2Components | numbers/vec2 | - | 0 | 8 | Splits a Vector2 into its X and Y values. | |
| add | AddVec3 | numbers/vec3 | - | 0 | 7 | Adds two Vector3 values. | |
| add | ScaleVector3 | numbers/vec3 | - | 0 | 7 | Multiplies two Vector3 values component-wise and by a uniform factor. | |
| review | Abs | numbers/float/adjust | Essential | 0 | 1 | Outputs the absolute value of a number. | |
| review | Floor | numbers/float/adjust | Essential | 0 | 1 | Removes the fractional part of a value. | |
| review | HasValueChanged | numbers/float/logic | Essential, Advanced | 0 | 1 | Tests whether the incoming value has changed by more than a threshold since the last frame | |
| review | PickFloat | numbers/float/logic | Essential | 0 | 1 | Picks one of the connected float inputs by index. | |
| review | PickInt | numbers/int/logic | Essential | 0 | 1 | Picks one of the connected integer inputs by Index. | |
| review | SetTime | numbers/anim/time | Essential | 0 | 1 | Overrides the animation time of a sub-command graph. | |
| review | SpringVec2 | numbers/vec2/process | Essential | 0 | 1 | Follows the incoming Vector2 with a springy motion that can overshoot. | |
| review | Sub | numbers/float/basic | Essential | 0 | 1 | Subtracts Input2 from Input1. | |
| review | SubInts | numbers/int/basic | Essential | 0 | 1 | Subtracts Input2 from Input1. | |
| review | AddInt2 | numbers/int2/basic | Essential | 0 | 0 | Adds two Int2 values component-wise. | |
| review | Atan2 | numbers/float/trigonometry | Essential, Advanced | 0 | 0 | Returns the angle of a 2D vector in radians. | |
| review | BlendValues | numbers/float/process | Essential | 0 | 0 | Blends between a number of connected values with a float index. | |
| review | ClampInt | numbers/int/process | Essential | 0 | 0 | Limits an integer to the range between Min and Max. | |
| review | Cos | numbers/float/trigonometry | Essential, Advanced | 0 | 0 | Computes the cosine of the input in radians. | |
| review | FreezeValue | numbers/float/process | Essential | 0 | 0 | Holds the input value depending on the Freeze input. | |
| review | KeepBoolean | numbers/bool/process | Essential | 0 | 0 | Holds a boolean value depending on the Freeze input. | |
| review | Log | numbers/float/basic | Essential | 0 | 0 | Computes the logarithm of Value to the given Base. | |
| review | RemapValues | numbers/float/process | Essential | 0 | 0 | Maps an input value to the Y component of the Vector2 pair whose X component is closest to | |
| review | ScaleResolution | numbers/int2/process | Essential | 0 | 0 | Scales an Int2 resolution by separate X and Y factors. | |
| review | ScaleSize | numbers/int2/process | Essential | 0 | 0 | Scales an Int2 resolution by a uniform factor and an optional per-axis Stretch. | |
| review | Sigmoid | numbers/float/adjust | Essential | 0 | 0 | Applies the logistic sigmoid function 1/(1+e^(Stretch*Value)). | |

## particle

| Proposal | Operator | Folder | Current tags | HowTos | Examples | Summary | Decision |
|---|---|---|---|---|---|---|---|
| review | FollowMeshSurfaceForce | particle/force | Essential | 0 | 1 | Keeps particles close to the surface of a mesh and lets them flow along it. | |
| review | ReconstructiveForce | particle/force | Essential | 0 | 1 | Pulls particles back towards their matching target points inside a spherical volume. | |
| review | FieldVolumeForce | particle/force | Essential | 0 | 0 | Uses a signed distance field as a volume that particles collide with, are attracted to or  | |

## point

| Proposal | Operator | Folder | Current tags | HowTos | Examples | Summary | Decision |
|---|---|---|---|---|---|---|---|
| add | SimNoiseOffset | point/sim | Advanced | 1 | 5 | Adds Perlin or curl noise movement to a simulation buffer. | |
| add | ResampleLinePoints | point/modify | Advanced | 1 | 3 | Creates a new buffer with a given number of points evenly interpolated along the connected | |
| review | LoadSvg | point/io | Essential | 0 | 1 | Loads an SVG file as a list of points so it can be rendered as points or lines. | |
| review | PolarTransformPoints | point/transform | Essential | 0 | 1 | Reprojects points from a rectangular space to a radial coordinate system. | |
| review | SnapPointsToGrid | point/transform | Essential | 0 | 1 | Moves points towards the center of their grid cell, i.e., snaps them onto a grid. | |
| review | DrawMovingPoints | point/draw | Essential | 0 | 0 | Draws a point buffer as camera-facing sprites that stretch along their movement on screen. | |
| review | ReorientLinePoints | point/transform | Essential, Advanced | 0 | 0 | Aligns the orientation of line points so that their z-axis points along the line. | |
| review | TransformWithImage | point/modify | Essential | 0 | 0 | Allows modifying various point attributes from an image. | |

## render

| Proposal | Operator | Folder | Current tags | HowTos | Examples | Summary | Decision |
|---|---|---|---|---|---|---|---|
| add | ListToBuffer | render/_dx11/buffer | Advanced | 1 | 7 | Sends the incoming structured list as StructuredBuffers to the GPU. | |
| add | SpreadLayout | render/transform | Advanced | 0 | 12 | Distributes the connected draw commands along a line and applies a common transform and co | |
| review | GodRays | render/postfx | Essential, Experimental | 0 | 1 | Draws light rays (god rays) from a light position, using the depth buffer to let scene obj | |
| review | MotionBlur | render/postfx | Essential | 0 | 1 | Applies a post-processing motion blur caused by camera movement to a rendered image. | |
| review | DrawLensFlares | render/shading/_ | Essential | 0 | 0 | Draws the lens flare sprites defined by one or more [DefineLensFlare] operators. | |
| review | FadingSlideShow | render/basic | Essential | 0 | 0 | Blends smoothly between image files read from a directory. | |
| review | LenseFlareSetupAdvanced | render/shading | Essential | 0 | 0 | Draws a pre-made lens flare like [LenseFlareSetup], but with separate intensity controls f | |
| review | RepeatWithMotionBlur | render/utils | Essential | 0 | 0 | Repeats the connected subgraph with offset local time. | |

## string

| Proposal | Operator | Folder | Current tags | HowTos | Examples | Summary | Decision |
|---|---|---|---|---|---|---|---|
| add | MockStrings | string/random | Advanced | 1 | 4 | Returns example text of different categories, such as colors, names, lorem ipsum or prime  | |
| review | BlendStrings | string/combine | Essential, Advanced | 0 | 1 | Blends between connected strings with a scrambling animation similar to an airport flight  | |
| review | PickString | string/logic | Essential | 0 | 1 | Picks one of the connected strings by index. | |
| review | SearchAndReplace | string/search | Essential | 0 | 1 | Searches for text within a string and replaces all matches. | |
| review | IndexOf | string/search | Essential | 0 | 0 | Searches the original string for a pattern and returns the position of its first occurrenc | |
| review | StringLength | string/list | Essential | 0 | 0 | Returns the number of characters in a string as an int. | |
| review | SubString | string/search | Essential | 0 | 0 | Returns a part of the connected string. | |
| review | WrapString | string/transform | Essential | 0 | 0 | Wraps a string at a column width. | |

## Existing Internal / Obsolete tags that contradict the description

These tags were set before this pass and were left unchanged. Some look wrong (e.g. user-facing ops tagged Internal, which hides them in search at 0.3x).

| Operator | Folder | Current tags | Issue | Decision |
|---|---|---|---|---|
| Render2dField | field/render | NeedsFix, Obsolete | tagged Obsolete, description does not say so | |
| ResetSubtreeTrigger | flow | Advanced, Internal, Experimental | tagged Internal, description reads as user-facing | |
| ExecuteRawBufferUpdate | flow/context | Internal, Obsolete | tagged Obsolete, description does not say so | |
| GetForegroundColor | flow/context | Advanced, Internal | tagged Internal, description reads as user-facing | |
| DepthBufferAsGrayScale | image/use | Advanced, Internal | tagged Internal, description reads as user-facing | |
| GetFrameSpeedFactor | numbers/anim/time | Internal | tagged Internal, description reads as user-facing | |
| WasTrigger | numbers/bool/logic | Advanced, Internal, Experimental, Research | tagged Internal, description reads as user-facing | |
| GradientsToTexture | numbers/color | Internal | tagged Internal, description reads as user-facing | |
| CurvesToTexture | numbers/curve | Advanced, Internal | tagged Internal, description reads as user-facing | |
| Lerp | numbers/float/process | Obsolete | tagged Obsolete, description does not say so | |
| MakeResolution | numbers/int2/process | Obsolete | tagged Obsolete, description does not say so | |
| FieldDistanceForce | particle/force | Research, Obsolete | tagged Obsolete, description does not say so | |
| DrawPoints2 | point/draw | NeedsFix, Obsolete | tagged Obsolete, description does not say so | |
| CPointToCamera | point/helper | Internal | tagged Internal, description reads as user-facing | |
| SampleCPoints | point/helper | Internal | tagged Internal, description reads as user-facing | |
| DefineLensFlare | render/_ | Internal | tagged Internal, description reads as user-facing | |
| DrawLensShimmer | render/_ | Advanced, Internal | tagged Internal, description reads as user-facing | |
| LenseFlareHoop | render/_ | Obsolete | tagged Obsolete, description does not say so | |
| _ComputeBRDFLookup | render/_ | Internal, Obsolete | tagged Obsolete, description does not say so | |
| _ComputeDepthToLinear | render/_ | Internal, Obsolete | tagged Obsolete, description does not say so | |
| _DepthOfField | render/_ | Internal, Obsolete | tagged Obsolete, description does not say so | |
| _VisLenseFxZone | render/_ | Internal, Obsolete | tagged Obsolete, description does not say so | |
| FloatsToBuffer | render/_dx11/api | Internal | tagged Internal, description reads as user-facing | |
| GenerateMips | render/_dx11/api | Advanced, Internal | tagged Internal, description reads as user-facing | |
| SrvFromStructuredBuffer | render/_dx11/api | Advanced, Internal, Obsolete | tagged Internal, description reads as user-facing | |
| TimeConstBuffer | render/_dx11/api | Advanced, Internal, Obsolete | tagged Internal, description reads as user-facing | |
| UavFromStructuredBuffer | render/_dx11/api | Advanced, Internal, Obsolete | tagged Internal, description reads as user-facing | |
| Viewport | render/_dx11/api | Advanced, Internal | tagged Internal, description reads as user-facing | |
| IsBufferDirty | render/_dx11/buffer | Advanced, Internal | tagged Internal, description reads as user-facing | |
| Texture3dComponents | render/_dx11/buffer | Internal | tagged Internal, description reads as user-facing | |
| ExecuteTextureUpdate | render/_dx11/fxsetup | Advanced, Internal | tagged Internal, description reads as user-facing | |
| PickBlendMode | render/_dx11/fxsetup | Advanced, Internal | tagged Internal, description reads as user-facing | |
| ShowTexture3d | render/_dx11/fxsetup | Internal, Obsolete | tagged Obsolete, description does not say so | |
| DrawScreenQuad | render/basic | Advanced, Internal | tagged Internal, description reads as user-facing | |
| VisibleGizmos | render/gizmo | Advanced, Internal | tagged Internal, description reads as user-facing | |
| IntToWrapmode | render/shading | Advanced, Internal | tagged Internal, description reads as user-facing | |
| SetPointLight | render/shading | Internal | tagged Internal, description reads as user-facing | |
| GetTextureFromContext | render/shading/_ | Internal | tagged Internal, description reads as user-facing | |
| SetContextTexture | render/shading/_ | Advanced, Internal | tagged Internal, description reads as user-facing | |
| _DrawLenseFlare_Old | render/shading/_ | Internal, Obsolete | tagged Internal, description reads as user-facing | |
| ConvertEquirectangle | render/utils | Internal | tagged Internal, description reads as user-facing | |
