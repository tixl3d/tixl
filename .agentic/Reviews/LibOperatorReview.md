# Lib operator review (2026-10)

Findings from a pass over all 964 operators in `Operators/Lib/Symbols`, done while rewriting their
descriptions to follow [dev.OperatorConventions](https://github.com/tixl3d/tixl/wiki/dev.OperatorConventions).
Paths are relative to `Operators/Lib/Symbols` unless they start with `Lib/Assets` / `Assets`.
Items under "Needs attention" were NOT fixed, mostly because a fix would change the output of existing graphs.

## Priority items

- **MotionBlur.hlsl** divides by `worldTFragPos.x` instead of `.w`. One-line fix, backward compatible; the edit was blocked during the pass and still needs applying.
- **Inverted ClearAfterExecution** on SetFloatVar/SetIntVar/SetStringVar/SetObjectVar/SetMatrixVar.
- **Gain/bias applied to a constant** in PerlinNoise2/PerlinNoise3; Core `MathUtils.PerlinNoise` runs `octaves - 1` octaves (Octaves=1 gives no noise).
- **Broken ops:** DrawPoints2 (parameter layout no longer matches DrawPoints.hlsl), BoundingBoxPoints (cross-group race above 512 points), Displace NormalMap vs SignedNormalMap identical, SimBlendTo F1 never blends, SimNoiseOffset rotation lookup.
- **Wrong units / math:** RepeatPolar offset (~1/32 of intended), OrbitCamera roll in radians, CameraWithRotation axes swapped, IcosahedronMesh pivot.Z uses stretch.X, AxisStepForce strength squared.
- **Licence:** KochKaleidoskope shader is CC BY-NC-SA (non-commercial).
- **Platform:** AsciiRender depends on BuildAsciiFontSorting, which is Windows-only.
- **Per-frame allocations / recompiles:** Get*Var dropdown lists, audio ops, MergeIntLists/MergeFloatLists/JoinLists LINQ, PlaybackFFT, field ops calling FlagCodeChanged() every update.
- **Misspelled op names** worth a rename with alias: MosiacTiling, ChromaticAbberation, KochKaleidoskope, GetPointLightOccclusion.

## Code fixes applied

### numbers_float

- numbers/float/process/DetectPulse.cs:~36: removed per-frame `Log.Debug($"...")` (string interpolation allocation + log spam every frame). No behavior change.
- numbers/floats/process/CompareFloatLists.cs:35: `listA.Count < index` -> `<=` (same for B). Lists of different length threw IndexOutOfRange; now missing entries count as different, as clearly intended. Only affects the previously crashing case.
- numbers/float/logic/HasValueChanged.cs:21: added `DeltaOnHit.UpdateAction += Update;`. If only DeltaOnHit was connected it never updated. Strictly less broken.
- numbers/float/process/EaseKeys.cs:~32: removed `_keyframes = _curve.GetVDefinitions().ToList();` (per-frame list allocation, result never read). No behavior change.

### numbers_anim_bool_color_data

- numbers/anim/utils/FindKeyframes.cs:42 - OpIndex clamped to `Count` (index out of range when OpIndex >= connection count); now `Count - 1`. Previously crashed, so only "less broken".
- numbers/anim/utils/SetKeyframes.cs:42 - same off-by-one clamp; same fix.
- numbers/anim/utils/FindKeyframes.cs:71 - WrapIndex mode used `(int)x % Count`, negative IndexOrTime gave negative index (crash); now `.Mod(Count)`. Identical for non-negative values.

### numbers_int_vec

- numbers/int/process/MinInt.cs:24 - missing `Ints.DirtyFlag.Clear()` after iterating the multi-input (MaxInt has it). Added; output values unchanged, only stops the multi-input from staying dirty.

### render_core

- render/_dx11/api/RtvFromTexture2d.cs:~90 - catch block set `_lastErrorMessage = null` instead of the message, so the IStatusProvider never showed failures. Now `_lastErrorMessage = message`. Status display only, no render change.
- render/_dx11/api/RtvFromTexture2d.cs:32 - array index guard `arrayIndex > maxArraySize` was off by one (index == size went on to an invalid view and threw). Changed to `>=`, which clamps to the last slice. Only affects cases that used to fail.

### render_scene

- render/scene/DrawScene.cs:35 - `foreach (var m in context.Materials)` ran without null check when UseMaterialId was set; added `&& context.Materials != null` to the condition. Only skips a would-be NullReferenceException; no behavior change otherwise.

### image_fx

- image/fx/_/_SpecularPrefilter.cs:72,86,100,138 - four early `return`s after `deviceContext.PushState(StateGroups.All)` (missing vertex/geometry/pixel shader, cubemap creation failure) never popped the state, leaking a state-stack entry and leaving device state modified. Added `Restore();` before each return. Only affects error paths; successful renders are unchanged.

### image_other

- Lib/Assets/shaders/img/fx/ToneMap.hlsl:105 (used by image/color/ToneMapping): the AgX_Punchy branch repeated `Mode < 4.5`, so it could never run and mode 5 fell through to no tonemapping. Changed to `Mode < 5.5`. Only graphs that picked AgX_Punchy change, and they now get the curve they selected instead of none (strictly less broken).
- image/use/FirstValidTexture.cs:19: `_complainedOnce |= Input.DirtyFlag.IsDirty` silenced the "No valid texture found" log whenever the input changed (inverted). It now resets the flag on input change. Only logging is affected.

### point_gen_modify

- point/generate/_DoyleSpiralRoot.cs:19 - constructor registered `B.UpdateAction` twice and never `R.UpdateAction`; R output alone never updated. Now registers R. Same Update, only adds a missing trigger.
- Lib/Assets/shaders/points/generate/BoundingBoxPoints.hlsl:61 - threads past the source count read OOB (position 0) and pulled the origin into the bounds unless count was a multiple of 512. Added `DTid.x >= sourcePointCount` to isValid (the commented-out intended line, now removed). Strictly less broken.
- Lib/Assets/shaders/points/modify/WrapPointPosition.hlsl:56 - NaN check `p.x + p.y + p.x` -> `p.z`. Only affects points with NaN z.

### point_draw_sim

- point/_cpu/TransformCPoint.cs:30 - per-frame LINQ (Select/Where/ToList) over Lists2 replaced by a for-loop counting non-null inputs; same "exactly one list" semantics, no allocations.
- point/_internal/MultiUpdatePoints.cs:10 - per-frame LINQ replaced by a loop; same result (last non-null buffer) and same warning on mismatching buffers (index now counts connected non-null inputs, as before).
- point/helper/SampleCPoints.cs (SlerpUnit) - removed two `Log.Debug(" Case A/B " + dot)` calls that fired every frame (string alloc + log spam); pure logging removal.
- point/_internal/GetParticleComponents.cs - status message referred to nonexistent "ParticleSimulation"; now "Is it used inside a ParticleSystem?". Text only.
- Assets/shaders/points/draw/DrawLinesBuildup.hlsl:111 - copy-paste bug `IsSeparator(pointBB) || pointAA.Scale.x < 0.01` -> `pointBB.Scale.x`. Only affects miter normal at stroke ends where neighbors differ; strictly less broken.

### field_geometry

- field/use/ApplyVectorField (Lib/Assets/shaders/points/_research/ApplyVectorField-template.hlsl): `p.FX2 = lerp(p.FX1, ...)` -> `lerp(p.FX2, ...)`. F2 was blended from the already-modified F1 whenever Strength < 1 or SetFx2To = Keep. Results with Strength 1 and SetFx2To != Keep are unchanged; everything else was wrong before. Only this op uses the template.
- field/use/SdfToVector.cs:71: `f{c}.w = GetSdfDistance(...)` -> `{c}GetSdfDistance(...)`. The helper is defined with the context prefix, so the unprefixed call only resolved when the context id was empty, and failed to compile otherwise. When the id is empty the generated code is identical.
- field/space/RotateField.cs:37: Globals key "pRotateXYZ" -> "pRotateAxis". Under the old key it defined the same `pRotateAxis` function as RotateAxis, so using RotateField and RotateAxis in one graph caused a shader redefinition error. Both function bodies are identical.

### mesh_particle

- mesh/_/LoadGltf.cs:36: mesh was only rebuilt when ChildIndex changed (`if(childIndexChanged)`), so changing Path alone loaded nothing; now rebuilds on path or index change and clears the stale status message. Only affects cases that were previously broken.
- mesh/draw/DrawMesh.cs:29, mesh/draw/CustomDrawMesh.cs:32: `foreach (context.Materials)` without null check when UseMaterialId is set -> NullReferenceException. Added `&& context.Materials != null`. Previously crashed, now falls back to the current material.
- Assets/shaders/3d/mesh/fx/BlendMeshToPoints.hlsl:27, fx/mesh-BlendVertices.hlsl:27, fx/mesh-UVs.hlsl:24 (BlendMeshToPoints, BlendMeshVertices, UVsViewer): bounds check `i.x > resultCount` -> `>=` (off-by-one OOB write). DX11 discarded the write, so no visible change; safer on Vulkan.
- Assets/shaders/3d/mesh/mesh-MoveToLinePoints.hlsl:53 (MoveMeshToPointLine): same off-by-one `>` -> `>=`.
- Assets/shaders/3d/mesh/mesh-FlipNormals.hlsl:26 (FlipNormals): TexCoord2 was not copied into the result vertices (undefined/zero); now copied. Only fixes lost data.

### io_flow_string_data

- flow/context/GetMatrixVar.cs:27 - `TryGetValue(variableName)` threw ArgumentNullException for a null VariableName; added `variableName != null &&`. Null name now returns identity like an undefined variable.
- string/buffers/transform/StringInsert.cs - `position.Clamp(0, maxPosition);` discarded its result, so out-of-range positions threw (caught, logged) and left the result stale. Now `position = position.Clamp(...)`. Valid positions behave the same.
- string/combine/CombineStrings.cs:17 - NRE when Separator is null (e.g. a connected null string). Now `?? string.Empty`. Same output otherwise.
- string/combine/BlendStrings.cs - the BlendSpread change was ignored by the `needsUpdate` gate, so animating only BlendSpread did nothing. Now `needsUpdate |=`. Other inputs behave the same.
- string/combine/FloatListToString.cs - the default Format `{0:0.000}` was passed to `float.ToString()`, which produced garbage like `{0:1.500}` (checked with dotnet fsi). Formats containing `{` now go through `string.Format`. Plain numeric formats (`0.00`) still use ToString. Only output that was already broken changes.
- string/list/SplitString.cs - a null Split threw an NRE, so it now uses `string.IsNullOrEmpty`. An empty input string left Count stale, so Count is now set to 0.
- string/list/StringLength.cs - NRE on a null input. Now returns 0.
- string/logic/PickStringPart.cs - `_chunks` started out null. A null InputText on the first update caused an NRE on `_chunks.Length` in later frames. It now starts as `[]`.
- string/search/SubString.cs - `str.Length` was used before the null check, causing an NRE for an unconnected or null input. The empty/null early-out now runs first.

## Needs attention

### numbers_float

- float/random/PerlinNoise (Core MathUtils.PerlinNoise): loop runs `octaves - 1` times, so Octaves=1 gives a constant output (no noise). Fixing changes every existing graph's noise; not applied.
- float/adjust/Remap.cs Modulo mode: `Fmod(v, max-min)` ignores the out-range offset (result in 0..range, not outMin..outMax). RemapFloatList does it correctly (`min + Fmod(v-min, range)`). Behavior change; not applied. Also divides by zero when RangeInMin==RangeInMax (RemapFloatList guards this).
- float/process/DetectPulse: `deltaToDamped = damped - newValue` fires when the value DROPS below its damped average; for beat detection a rise is expected. Possibly inverted sign; documented as-is.
- float/logic/HasValueChanged: PreventContinuedChanges works inverted to its name (On = every changing frame is a hit, Off = only the first frame). Documented actual behavior.
- float/adjust/Floor: `(int)` cast truncates towards zero (-2.7 -> -2) and overflows outside int range. Should be MathF.Floor; behavior change for negatives; not applied. Default Value=29 is odd.
- float/logic/TryParse: float.TryParse uses current culture (German locale parses "1.5" as 15). Should be InvariantCulture; not applied (changes results on comma locales). Documented.
- float/trigonometry/Atan2: computes Atan2(X, Y) (swapped vs. atan2(y,x) convention); documented, not changed.
- floats/process/SmoothValues: the current index is sampled twice (SampleAtIndex(index) plus windowIndex 0); window is forward-only. Default WindowSize 999999 is clamped to 10.
- floats/process/AnalyzeFloatList: AverageMean divides by list.Count including skipped non-finite entries.
- Per-frame/per-update allocations: PlaybackFFT (`.ToList()` of the FFT buffer every frame + Enum.GetNames), Compare (Enum.GetValues per update), Random/FloatHash (BigInteger per update), MergeFloatLists (LINQ Select/Where/ToList + FailOver copies each update), RemapFloatList / FloatListToIntList / IntListToFloatList (new list per update), EaseKeys (Tuple per frame).
- floats/basic/SetFloatListValue mutates the upstream input list in place (side effect on other consumers).
- floats/process/DeltaSinceLastFrame: Threshold input unused.
- float/process/Spring, AnalyzePianoMidi (PeakDecay), ComposeVec3FromList: frame-rate dependent (no dt).
- float/basic/Log: default Base=1 -> output NaN out of the box.
- float/adjust/Round: default RoundRatio=0 means no rounding at all; negative values behave oddly (C# % keeps sign).

### numbers_anim_bool_color_data

- anim/time/SetTime.cs - OffsetMode GlobalAbsolute with a connected SubTree falls into the else branch and behaves like Relative (adds NewTime). Without SubTree it permanently writes the context time. Behavior change, not fixed.
- anim/animators/AnimFloatList.cs - adjustedTime adds `offsetNumber` (the list count) to time: changing the count shifts all values; looks like a bug. Phase is added before Rate (inconsistent with AnimValue). Allocates a new List every update. Shape clamp uses `Length` instead of `Length-1`. Fixing changes output, not applied.
- bool/logic/ToggleBoolean.cs - resets its trigger inputs via SetTypedInputValue(false); with a connected input this has no effect, so it toggles every frame while the input is true. Use FlipBool for connected signals (documented).
- bool/process/DelayBoolean.cs - effective delay is FrameCount+1 frames; while the queue fills the output is false instead of the delayed value; skips updates when time does not change (paused).
- bool/process/DelayTriggerChange - mode names are counterintuitive: DelayTrue keeps true for the duration after input goes false (i.e. delays the falling edge).
- data/_obsolete/GetIteratedFloat etc. / Core StructuredListUtils.GetValueOfFieldWithType - calls `list.Type.GetFields()` every update (allocation) and negative ItemIndex gives negative index (crash). Core file, out of scope.
- data/utils/JoinLists.cs - LINQ + ToList/ToArray every update; with one list Length output is 1 instead of NumElements; with 0 lists Result keeps stale value.
- data/utils/SelectVec2FromDict.cs - caches Y key once (`_yKey`) and never refreshes when SelectX is changed by connection or dict keys change; LINQ OrderBy per lookup.
- bool/combine/Any.cs - does not clear Input.DirtyFlag (All does); may cause unnecessary re-evaluation.
- anim/time/StopWatch.cs - LastDuration is always seconds and ignores PauseWithPlayback, while Delta respects DurationIn/PauseWithPlayback.
- bool/logic/WasTrigger - reads `__TriggerA`/`__TriggerB` float vars, but nothing in Operators/Core/Editor writes those names; TriggerA/B modes appear dead.

### numbers_int_vec

- vec2/PerlinNoise2.cs, vec3/PerlinNoise3.cs ScalarNoise: `(noise*1.37+1) * 0.5f.ApplyGainAndBias(bx, by)` applies gain/bias to the constant 0.5, not to the noise value. BiasAndGain therefore only scales the output, it does not shape the distribution. Fix would change existing graphs; not applied. Check PerlinNoise (float) for the same pattern.
- ints/MergeIntLists.cs: per-frame LINQ/allocations (`Select().ToList()`, `Where().ToList()`, `Max(lambda)`, `new List<int>` copies in FailOver every frame, `?? new List<int>()` for StartIndices). Existing docs were wrong (Enabled described as list of bools, MergeMode as Override/Add); rewritten. Append without MaxSize pads gaps with -1 while MaxSize pads with 0 (inconsistent).
- vec2/process/EaseVec2Keys.cs, vec3/process/EaseVec3Keys.cs: `_keyframes = curve.GetVDefinitions().ToList()` allocates per frame per curve, and `_keyframes` appears unused for the result.
- int/process/IntsToBuffer.cs: `new int[arraySize]` every Update (per-frame heap alloc); should cache the array.
- vec2/RemapVec2.cs, vec3/RemapVec3.cs Modulo mode: `Fmod(v, outMax-outMin)` ignores outMin, so wrapping is only correct when RangeOutMin = 0. Behavior change, not applied.
- int/basic/DivInt.cs: division by 0 returns 1 (arbitrary; 0 or keeping last value would be more expected). int/basic/ModInt.cs: Mod = 0 keeps stale output; negative results from C# `%`.
- int2/process/Int2Components.cs: AspectRatio divides by Height without guard (Inf/NaN for Height 0).
- vec2/HasVec2Changed + vec3/HasVec3Changed: PreventContinuedChanges logic is confusing: when false, continued changes report true every frame bypassing MinTimeBetweenHits; when true, the time gate applies to all frames. Name suggests the opposite. Left undocumented.
- int/basic/RandomChoiceIndex: description claim "never repeat an index" only refers to consecutive values; kept wording as "without repeating the previous index" (algorithm comment supports this; not verified at buffer boundaries).
- vec2/process/SpringVec2, vec3/process/SpringVec3: local variable named `test` for Strength; Spring is frame-rate dependent (no dt).
- int/process/KeepInts: outputs the same List instance every frame (downstream may not see change), try/catch around list ops is unnecessary.

### render_core

- render/_/DefineLensFlare.cs - FxZoneMode `Off` does nothing: zone effects are always applied (Off acts like Objects). Fixing it would change existing flares that use Off with non-zero zone values. Also `new Random` and the sprite list are rebuilt every frame (Animated), a per-frame allocation. A LightIndex >= light count is passed to GetPointLight without a check. The TextureIndex input is unused (a random index is written instead).
- render/_/_LenseFlareHoopPosition.cs - same FxZoneMode/Random pattern as DefineLensFlare (copied code).
- render/_dx11/api/GetSRVProperties.cs - the `Buffer` output is never assigned.
- render/_dx11/api/OutputMergerStage.cs - BlendFactor, BlendSampleMask and DepthStencilReference are read but never applied. Rasterizer.cs - ScissorRectangles is ignored. InputAssemblerStage.cs - VertexBuffers, InputLayout and IndexBuffer are evaluated but never bound. Documented as "Currently ignored" / "Evaluated but not bound".
- render/_dx11/api/DsvFromTexture2d.cs - creates a new DepthStencilView on every texture change without disposing the old one (a leak).
- render/_dx11/api/FloatsToBuffer.cs - the size calculation assumes every Vec4Params array has exactly 4 elements (`count*4*4`). Longer arrays overflow `_uploadBuffer` and are caught as a warning. Shorter arrays leave stale data in the buffer.
- render/_/TypoGridBuffer.cs - `HighlightCharacters` null (unconnected/empty default) would throw on `IndexOf`; Height, CellSize, CellPadding and TextCycle are unused. `IndexOf` per cell is O(n*m).
- render/_/ApplyCamMatrices.cs - no null/length check on the row arrays, and the `aspect` variable is unused.
- render/_dx11/buffer/ListToBuffer.cs - uses LINQ (`Select/Where/ToList/FirstOrDefault`) in Update, so each update allocates. Division by zero if the first list has ElementSizeInBytes 0.
- render/_dx11/api/CalcDispatchCount.cs and CalcInt2DispatchCount.cs - `count / size + 1` dispatches one extra group when the count divides evenly. Harmless, but not a ceil-div. CalcInt2DispatchCount keeps its previous value when a group size is 0.
- render/_/GetLightPosition - the name is misleading because it also returns range, color, intensity and count.

### render_scene

- Lib/Assets/shaders/img/fx/MotionBlur.hlsl (render/postfx/MotionBlur): `worldTFragPos /= worldTFragPos.x;` should be `.w`. Projectively harmless, except pixels whose world x is 0 produce inf/NaN. One-line fix `.x` -> `.w` (identical results otherwise). I tried to apply it but the permission classifier blocked the edit, so it still needs doing.
- render/transform/SpreadLayout.cs: `context.ForegroundColor *= color;` sits inside the per-command loop, so command n gets color^n (it compounds). It should be `previousColor * color` for each command. Not fixed because it changes how existing graphs look when Color is not white.
- render/transform/SpreadIntoGrid.cs: `spreadYIndex = spreadIndex / gridSize.X` lacks `% gridSize.Y`, so with GridSize.Z > 1 the rows run past the grid instead of moving to the next layer. Not fixed (changes existing layouts).
- render/camera/OrbitCamera.cs: the orbit is always centered on the world origin. `eye` is never offset by CameraTargetPosition, so the target only changes where the camera looks. Also `_cameraDefinition.Roll` gets radians while CameraDefinition expects degrees, and roll is applied to `up` as well, so BlendCameras/ReuseCamera roll is wrong. Documented the current behavior.
- render/camera/CameraWithRotation.cs: WorldToCamera is built only from translation*rotation, so PositionOffset, AlsoOffsetTarget, RotationOffset and Up never affect the rendered view. The Euler axes are swapped (X rotates around Y), Roll is stored in radians in the CameraDefinition, and the default quaternion (1,1,1,1) is not normalized. Documented as-is.
- render/camera/OrthographicCamera.cs: `CameraDefinition => new()` is not implemented, so BlendCameras/ActionCamera fed from it get garbage.
- render/camera/ShiftCamera.cs: the Scale and UniformScale inputs are computed and then ignored (dead inputs).
- render/camera/ActionCamera.cs: Roll is scaled by Speed instead of RotationSpeed.
- render/camera/CamPosition.cs: the once-per-frame guard returns stale values when it is evaluated in several contexts per frame (loops, several render targets) without a CamReference.
- render/camera/BlendCameras.cs: the single-camera path never clears `_lastErrorMessage`, so a stale warning remains. On any error the Command subtree is not evaluated (the scene disappears).
- render/camera/AnaglyphCamera: an unconnected PointLight and Bloom are inside the graph. DepthOfField (Amount 0.6) is always applied and FocusDistance defaults to 0, so the default output is blurred.
- render/camera/CurrentCamMatrices.cs and render/shading/_/GetAllCameras.cs: both allocate new Vector4[] arrays every frame. GetAllCameras' `WorldToClipSpaceRows` output actually holds CameraToClipSpace (misnamed).
- render/gizmo/ConeGizmo.cs: Angle >= 180 yields tan overflow or a negative radius, although the docs/comments claim a 0-360 range.
- render/postfx/GodRays: the BlurOffset input is unused in both shaders.
- render/postfx/SSAO: NearFarRange (linearization clip planes) and NearFarClip (background fade range) have confusing, almost swapped names. The old docs described them wrong (now fixed in the docs).
- render/shading/SetPointLight: the old docs said Range is ignored, but all shaders use it (intensity / ((d/range)^decay + 1)). The docs are fixed.
- render/shading/GetPointLightOccclusion: the op name has a triple "c". Old docs referenced the non-existent [GetPointLightOcclusion], [UseRenderTarget] and [LenseFlare].
- render/sprite/_experimental/SampleSpriteAttributes: the compute shader ignores almost all inputs (Red/Green/Blue factors, Brightness, TextureMode, etc.) and overwrites Size/Pivot/UV with constants. The bounds check is commented out, so writes past spriteCount can occur (undefined on Vulkan).
- render/transform/RotateAroundAxis.cs: Axis is not normalized before CreateFromAxisAngle, so a non-unit axis distorts the result and a zero axis gives NaN.
- render/transform/SpreadLayout: ForceColorUpdate defaults to true, which invalidates the whole subgraph every frame (performance).

### image_fx

- Operators/Lib/Assets/shaders/img/fx/Displace.hlsl:101 - in the NormalMap/SignedNormalMap branch (`DisplaceMode >= 2`) the check `DisplaceMode < 0.5` can never be true, so NormalMap (2) and SignedNormalMap (3) behave the same (unsigned `rg*0.01`). It was probably meant to be `DisplaceMode < 2.5`. Not fixed because it changes the output of existing NormalMap graphs. Documented as "both modes currently behave the same".
- stylize/Dither: the `GrayScaleWeights` input goes into the cbuffer but the shader takes `ApplyGainAndBias(saturate(color), GainAndBias).x`, which is only the red channel. The `Methods` enum names are wrong: `FloydSteinberg` is actually a Bayer ordered matrix and `Diffusion` is random hash noise. The old description also claimed Floyd-Steinberg, which was wrong. Not fixed because using the weights would change existing looks.
- distort/TimeDisplace: the shader only uses DisplaceMap brightness * Displacement as the slice offset. DisplacementOffset, Twist, Shade, SampleRadius, DisplaceMapOffset, DisplaceMode and RGSS_4xAA are ignored, so either implement them or remove them from the UI. A negative Displacement gives a negative `sliceOffset`, and `(SliceIndex + sliceOffset) % ArrayLength` can then become negative (the slice index is invalid).
- stylize/AsciiRender depends on _/BuildAsciiFontSorting, which only works on Windows (System.Drawing plus a `WindowsOnlyFeature` guard). On Linux/macOS the sorting texture is never built, so AsciiRender is broken there. It needs a cross-platform image loader. FontFilePath / Filepath also have no file filter.
- distort/KochKaleidoskope: the shader header says "License Creative Commons Attribution-NonCommercial-ShareAlike 3.0" (BigWings). This conflicts with commercial use of TiXL, so check the license.
- blur/ChromaBlur: the graph outputs `float4(comp, 1)`, so the input alpha is lost. The Lab a/b channels get +0.5 / -0.5 around the blur, which has no effect at Lab ranges of about ±128 (harmless).
- stylize/Steps shader: `main = index / (StepCount - 1)` divides by zero when Count = 1.
- feedback/FluidFeedback and feedback/AdvancedFeedback: the `SampleDistance` input is not connected inside the graph (ignored).
- distort/BubbleZoom: the obsolete `Bias` input is not connected (ignored).
- _obsolete/TriangleGridTransition: `MixOriginal` is in the cbuffer but never used.
- stylize/MosiacTiling: `Stretch` maps to `Stretch2` in the cbuffer but is never used.

### image_other

- image/color/ColorGrade: the ClampResult input goes through BoolToFloat into the params buffer, but ColorGrade.hlsl has no such field and always clamps to 0.000001..1000. The parameter does nothing. Its description now says so. Either wire it up or remove it.
- image/color/RemapColor (ColorRemap.hlsl): in UseGrayScale mode `orgColor` is overwritten with the biased gray value, so DontColorAlpha outputs gray*Repeat as alpha, not the original alpha. In IndividualChannels mode alpha also gets gain/bias/Repeat. Not fixed because it would change existing results.
- image/color/ToneMapping: every mode except None forces alpha to 1. This is documented, but it may not be intended.
- image/use/KeepInTextureArray.cs: a null SourceTexture logs `Log.Warning("Invalid textures")` every frame with no instance context. WriteIndex is not clamped or wrapped the way ReadIndex is (not checked in CopyIntoArray).
- image/generate/basic/LinearGradient: OffsetMode (RelativeToImage/RelativeToSize) is not used in LinearGradient.hlsl. I could not trace whether the graph uses it. Its old description (a copy of the op description) was removed.
- image/generate/load/LoadImageFromUrl.t3ui: the Url input has `"Usage": "FilePath"`, so the editor shows a file picker for a URL.
- image/generate/load/ImageSequenceClip.t3: the graph uses the obsolete `_Time_old` and a CompareImages/Text overlay. Worth checking whether that is leftover debug content.
- image/use/UseFallbackTexture.cs: the Fallback value is cached and only re-read when its dirty flag is set. This works, but it is fragile if the fallback texture gets disposed upstream.
- image/generate/pattern/ValueRaster.hlsl: a hard-coded orange/blue tint and center-line boost are applied based on the Y value, with no parameter to control them.

### point_gen_modify

- point/generate/BoundingBoxPoints: cross-group reduction with InterlockedMin/Max then reading Bounds in the same dispatch (AllMemoryBarrierWithGroupSync does not sync groups); result index 0 written by every group, last writer wins -> racy for >512 points. Needs a second pass.
- point/modify/AttributesFromImageChannels.hlsl: strength applied twice (factors already include `* strength`, then offset/scale/F1/F2 multiplied by strength again) -> effectively Strength^2 * alpha^2. Fix changes results. `Mode` input unused by shader.
- point/combine/BlendPoints.hlsl: Pairing does not remap indices: WrapAround reads A/B at i.x without modulo (OOB -> zeros), Adjust only early-returns threads. Bound check `i.x > resultCount` off by one.
- point/transform/SnapToPoints.hlsl: pairs by index (not nearest point); F1 lerps with the raw BlendFactor param instead of computed blendFactor (likely typo, changes results).
- point/generate/RepetitionPoints.cs: Scale step is inverted: `(1 - Scale) * u + 1` -> Scale 1 = no change, Scale < 1 grows. StartW writes F1.
- point/modify/AddNoise.hlsl: RotationLookupDistance = 0 gives normalize(0) -> NaN rotations (degeneracy check tests NaN). Default is non-zero.
- point/transform/TransformFromClipSpace: name/old docs say clip space, shader multiplies by CameraToWorld (with w-divide). Either name or matrix is wrong.
- point/io/PrepareSvgLineTransition.cs: mutates the input StructuredList in place (writes F1 into upstream data); `segments` is a static shared list (not thread/instance safe); `new Random(42)` per update.
- point/modify/ClearSomePoints.hlsl: Resolution 0 -> modulo by zero; no bounds check on i.x.
- point/combine/PairPointsForLines.hlsl / PairPointsForGridWalkLine.hlsl: off-by-one bounds checks (`>` instead of `>=`), harmless on DX11 (OOB UAV writes dropped), worth checking on Vulkan.
- point/combine/SplinePoints.cs: LINQ + new StructuredList per update (cook only); warns "Need at least 2 points" but checks connected input count.

### point_draw_sim

- draw/DrawPoints2: broken. Its FloatsToBuffer layout no longer matches DrawPoints.hlsl (Color, PointSize, CutOffTransparent, FadeNearest): CutOffTransparent receives the point count (so every pixel with alpha < count is discarded -> draws nothing), FadeNearest receives EnableZWrite?0.1:-1, the FadeNearest/UseWForSize params land in unused slots, and no IntParams buffer is bound. Needs graph rewiring or marking obsolete. Doc now says so.
- sim/SimNoiseOffset.hlsl: `noiseNormal` is computed from `noiseLookup` instead of `noiseLookupNormal` (copy-paste) so RotLookupDistance has no effect and the rotation tracks the displacement noise. Fix would change existing results.
- sim/experimental/SimBlendTo.hlsl: `wB = ResultPoints[i.x].FX1` should read PointsB, so F1 never blends; PairingMethod/CountA/CountB unused; no bounds check against PointsB count. Fix changes results.
- sim/SimForceOffset: Variation (variationOffset computed but unused) and UseWForMass have no effect in the shader; old docs described them. Docs removed.
- draw/DrawRayLines: ShrinkWithDistance, TransitionProgress (OffsetU), UseWForWidth, UseWAsTexCoordV are passed but unused by the shader. WidthFactor is an int without MappedType enum.
- sim/experimental/ApplyRandomWalk: Seed, AreaCenter, AreaEdgeRange unused by SimRandomWalk.hlsl (Time is used as seed). TriggerStep is level- not edge-triggered.
- draw/DrawPointsDOF: DofShape not wired to anything; op does depth-bucket sorting but no DOF.
- draw/DrawLinesBuildup.hlsl: Color is applied twice (in imgColor and input.color) -> color squared.
- helper/CPointToCamera: SamplePos and Up unused. helper/PointToMatrix: copy of the camera op - implements ICamera/ICameraPropertiesProvider and has 10 unused camera inputs (SamplePos, FieldOfView, Roll, ...); only CamPointBuffer is used; `forward`, `_invertedMatrix`, aspect computation dead.
- _cpu/SampleSplinePoint: U*(N-2) means the last point is never reached (avoids i+1 overflow); replaces context.ObjectToWorld instead of combining; logs warnings every frame when input is missing; misnamed (linear, not spline).
- _cpu/TransformCPoint: Rotation is radians (no ToRadians, unlike TransformCPoints which uses degrees). Incremental starts from a default point at origin, not from the input.
- _experimental/PointsFromMeshData: Seed default 0 -> rng_state = i.x*0 -> all points identical; cdf search can index FaceBuffer[faceCount].
- helper/ReadPointsFromGpu (sync path): with UpdateContinuously the CPU staging buffer is disposed and recreated every frame; allocates a new StructuredList per read.
- helper/SampleCPoints: computes unused `up`, `smoothT`, `pUpA/B` every frame; F1/color not interpolated; separators not handled.
- point/_internal/AnalyzeBuffers: not used by any op; Stride not reset when nothing selected.
- draw/DrawClosedLines: LineOffset is sent to the cbuffer but no matching usage found in DrawLinesAlt.hlsl.

### field_geometry

- field/space/RepeatPolar.cs: Offset is converted with `offset / (180*PI)` instead of `offset * PI / 180`, so the rotation is about 1/32 of what was intended. Not fixed because it would change existing graphs. The doc only says "rotates the copies".
- field/space/RotateField.cs and field/generate/sdf/RotatedPlaneSDF.cs call `ShaderNode.FlagCodeChanged()` on every Update, and SdfToVector.cs does the same. Every parameter change (or an animation) recompiles the shader. RotateField could use its AdditionalParameter the way BoxSDF does. I didn't change this because I'm not sure how AdditionalParameters propagate dirty state.
- field/render/Render2dField: the shader uses only ColorField. MaxSteps, StepSize, Min/MaxDistance, Specular, Glow_, AmbientOcclusion, Background, Spec, AoDistance, Fog, LightPos and DistToColor are dead leftovers from the raymarcher (the doc says so). Remove them or hide them.
- field/adjust/SpatialDisplaceSDF.cs: it registers un-prefixed global functions (`mod289`, `permute`, `taylorInvSqrt`, `simplexNoise3D`), and its Globals key "fSimplexNoiseDisplace" has the same name as the key NoiseDisplaceSDF uses for different code. Both ops still compile together today because both variants define `fSimplexNoiseDisplace`, but this is fragile. It should use prefixed names like NoiseDisplaceSDF does.
- field/adjust/TranslateUV.cs (and every SDF generator): `p.w < 0.5` uses the root `p` instead of `p{c}`. This is consistent across all generators, so it's probably intentional (mode flag), but please confirm.
- field/combine/BlendSDFWithSDF.cs: Range = 0 divides by zero in the color lerp `smoothstep(0,1,(dM-Offset)/Range)`, although the distance path handles w <= 0. The color blend also runs only over 0..Range, not symmetrically around the mask border.
- field/generate/sdf/CapsuleLineSDF.cs: the gizmo (ITransformable.TranslationInput) moves EndPoint, not Center.
- field/space/BendField.cs / TwistField.cs: pi is typed as 3.14157892 (and as 3.141578 in RotateAxis). The error is about 1e-5 relative. Not fixed because it would change results slightly.
- field/use/RaymarchPoints / SdfReflectionLinePoints: in Raymarch mode, WriteStepCountTo writes the reflection index, not the step count.
- field/render/SampleFieldPoints.cs: WriteTo has no MappedType (enum FModes exists but isn't used), and the unused enums Shapes/Modes are leftovers.
- field/adjust/_/_SDFToColor_Old.cs: the class summary is copy-pasted from RepeatFieldAtPoints, and there is an unused CombineMethods enum. The same unused enum appears in HeightMapSdf.cs and Image2dSDF.cs.
- field/use/SDFToColor.t3 has unconnected leftover children (RandomizePoints, SelectPointsWithSDF).
- field/space/_/ExecuteRepeatFieldAtPoints: the point count is baked into the shader, and the old doc said "clamped at 50" while the code limit is 100 (doc fixed).
- Many field docs linked to nonexistent ops: [HowToRaymarchField], [PolarRepeat], [RepeatWithMirror], [RaymarchSdf], [CapsuleSDF], [SubDivisionStretch]. I removed or replaced all of them. No raymarching HowTo tutorial exists.

### mesh_particle

- particle/force/SwitchParticleForce.cs:17: Index -1 does `return` without changing output, so it keeps the previous force instead of disabling all; -2 just wraps (no "activate all"). Old docs promised both; docs now describe the real behavior. Implement or drop the feature.
- mesh/generate/IcosahedronMesh.cs:81: pivot offset uses `stretch.X * scale * pivot.Z`; should be `stretch.Z`. Changes results when Stretch.X != Stretch.Z, so not fixed.
- Assets/shaders/3d/mesh/mesh-Deform.hlsl NormalizeToSphere (DeformMesh): returns `position - Pivot` scaled but never adds Pivot back (comment "Not sure why this isn't working"); Spherize with non-zero Pivot shifts the mesh. Fix changes results.
- Assets/shaders/3d/mesh/mesh-MoveToLinePoints.hlsl TransformDirection: lerps orientationA with orientationA (copy-paste); should use orientationB. Normals currently ignore the second point. Fix changes shading.
- Assets/shaders/3d/mesh/mesh-DrawAtPoints.hlsl:140-150 (DrawMeshAtPoints atlas): uses `instanceIndex / AtlasSize.y` and `FX1 / AtlasSize.y`; should divide by AtlasSize.x. Only wrong for non-square atlases. Fix changes results.
- Assets/shaders/particles/ReconstructiveForce.hlsl: UseTargetsPosition reads `TargetPoints[gi]` instead of `[targetPointIndex]` (OOB if counts differ), `gi % targetPointCount` divides by 0 with an empty target list, and Bias is never used.
- Assets/shaders/3d/mesh/mesh-DisplaceVAT.hlsl: `DisplaceMap.GetDimensions(height, width)`: arguments swapped (returns width first), so the frame offset is 1/width. Only right for square VAT textures.
- Assets/shaders/particles/AxisStepForce.hlsl: the step velocity is multiplied by `f` twice (`direction *= ... * f` then `direction * f`), so Strength works quadratically.
- Assets/shaders/particles/SnapOrientationForce.hlsl (SnapToAnglesForce): Twist adds `PhaseAngle/360` to an angle in radians (unit mix-up). The final `lerp(v, newVelocity, 1)` is a no-op.
- mesh/generate/LoadObj.cs: the GPU cache is keyed only by path, so with UseGPUCaching a cached mesh ignores ScaleFactor/SortVertices changes. ClearGPUCache drops entries without disposing them.
- mesh/modify/RecomputeNormals.t3: RecomputeIndices feeds an `Any` node whose output is not connected, so it has no effect.
- mesh/modify/SelectVertices (mesh-SelectVertices.hlsl): Add mode computes `s + old*Strength` (Strength applied to the existing selection, not the new one); Invert ignores Strength.
- mesh/modify/SelectVerticesWithSDF: copied from the point version. WriteTo only writes when it is F1 (None/F2 do nothing; meshes have no F attributes), StrengthFactor F1/F2 both mean "existing selection", DiscardNonSelected unused.
- mesh/modify/BlendMeshToPoints & BlendMeshVertices: WrapAround pairing does not wrap (it reads by raw index, OOB when counts differ). BlendMeshToPoints UseF1AsWeight uses `1-F1` but UseF2AsWeight uses `F2`. BlendMeshVertices enum UseW1/W2AsWeight actually uses the vertex selection of A / 1-selection of B.
- mesh/_/LoadObjEdges.cs: edges are packed into 16-bit pairs (fails above 65535 vertices); the bounds check compares against the output list length instead of `mesh.Positions.Count`; skipped edges leave stale trailing points. Allocates a HashSet on every reload.
- mesh/generate/TorusMesh.cs:38: the flat-shading test pairs fillRadius with tubeSegments and tubeFill with radiusSegments; looks crossed (uncertain).
- mesh/generate/DelaunayMesh.cs: allocates several Lists per update (fine when not animated, but heavy); Log spam on invalid input.
- mesh/draw/DrawMeshWithShadow shader: shadows are attenuated by a hard-coded spotlight cone from Lights[0].range; behavior is undocumented in the UI.
- particle/force/VerletRibbonForce: the constraint pass relies on AllMemoryBarrier across thread groups (no cross-group sync), so results are racy. Labeled experimental.
- TextureDisplaceMesh shader: bitangent uses `lerp(..., 0.4)` while tangent uses 0.5 (likely a typo).

### io_flow_string_data

- flow/context/Set{Float,Int,String,Object,Matrix}Var: ClearAfterExecution is inverted. When there was no previous value, the variable is removed only if ClearAfterExecution is false. In SetMatrixVar without a SubGraph, enabling it even prevents setting the variable. Fixing this changes existing graphs, so it was not touched and the parameter is left undocumented.
- flow/context/GetFloatVar, GetStringVar, GetObjectVar: `Keys.ToList()` runs every frame, only to fill the dropdown (heap allocation per frame). GetBool/GetVec2/GetVec3 only do it when the name is dirty. GetVec2Var/GetVec3Var list all ObjectVariables, not just Vec2/Vec3 ones.
- flow/context/GetPosition.cs: PositionOffset (Vector3) has `MappedType = typeof(Spaces)`, which looks like a copy-paste error. Not removed because of the possible UI side effects.
- flow/LogMessage: with OnlyOnChanges enabled, the SubGraph is also skipped while the message is unchanged. This is surprising for a debug pass-through.
- io/audio/AudioReaction.cs: `bins2.ToList()` runs every update (allocation). In the backwards time jump branch, `_lastHitTime = timeSinceLastHit` looks wrong (probably meant `_lastEvalTime`).
- io/audio/_/AudioWaveform.cs and _/AudioFrequencies.cs: `ToList()` per update (5 lists per frame in AudioWaveform). _/GetBeatTimingDetails.cs uses reflection (`GetFields`) every frame.
- io/audio/_/GetAllSpatialAudioPlayers.cs: all slots, including the SpatialAudioPlayerIndex input, are declared `private readonly`. This needs a check that they are exposed and registered at all.
- io/audio/_/DetectBpm.cs: `(int)BufferDurationSec.GetValue(context) * 60` truncates before multiplying, so fractional seconds are lost.
- string/combine/FloatListToString: the separator is also appended after the last value.
- string/combine/BlendStrings: InputTextA/InputTextB are unused leftovers (they still have their own GroupTitles). A MaxLength change alone doesn't pass the needsUpdate gate.
- string/search/SearchAndReplace: an empty Replace returns the original, so matches cannot be deleted. Changing this would alter existing graphs.
- string/buffers/transform/StringInsert: when the insertion is as long as or longer than the original, the result is not updated (stale).
- string/transform/WrapString: the WrapAtCharacters mode logs a deprecation warning every frame and otherwise does nothing.

## Convention issues

### numbers_float

- Remap / RemapFloatList / PerlinNoise: input named `BiasAndGain` but X is passed as gain, Y as bias (ApplyGainAndBias(gain,bias)); other ops call it GainAndBias.
- Add / Sub: inputs `Input1/Input2` vs A/B in Multiply/Div.
- InvertFloat: input `A`; Sin: `Period` is a divisor (true period = 2π·Period), misleading name.
- HasValueDecreased lives in process/ while HasValueIncreased lives in logic/.
- ComposeVec3FromList: .t3 defaults IndexForY/IndexForZ = 0 although C# defaults are 1/2.
- DefineIqGradient: underscore names (A_Brightness ...), NumOfSteps.
- Accumulator: default mode PerFrame (frame-rate dependent); PerSeconds probably the better default.
- MergeFloatLists: StartIndices/MaxSize only apply in Append; no grouping.
- ValuesToTexture2: GainAndBias is only applied when Clamp is enabled (surprising coupling).
- RemapValues: description referred to non-existent "LookupValue" (fixed in docs).

### numbers_anim_bool_color_data

- AnimFloatList: param names OffsetNumber/OffsetCycle unclear (Count / TimeOffset); input order not grouped.
- AnimVec2/AnimVec3: plural Rates/Phases/Amplitudes vs singular in AnimValue (consistent between the two vec ops, fine).
- SequenceAnim: GroupTitle "Interpolation method" is on Direction instead of Interpolation.
- KeepColors: AddColorToList adds every frame while true (no edge detect), unlike other trigger-style inputs.
- BoolToInt uses ResultForTrue/False, BoolToFloat uses ForTrue/ForFalse - inconsistent naming.
- Trigger: summary previously had AKA inline; moved to AKA line.
- DateTimeInSecs: float output of Unix seconds loses precision (~128 s steps). Consider double or offset.

### numbers_int_vec

- vec2/GridPosition: input `A` (Vector2) is unused.
- Generic names Input1/Input2/A/B/F on many math ops (AddInt, SubInts, AddVec2/3, DotVec*, LerpVec3 `F`, BlendVector3 `F`); SubInts says nothing about order.
- vec3/ScaleVector3 uses `ScaleUniform` while ScaleVector2/DivideVector2 use `UniformScale`.
- Naming inconsistency across ops: AddInt/SubInts/MultiplyInt/MultiplyInts/SumInts/DivInt; ScaleVector2 vs AddVec2; Vec2Magnitude lives in vec3 folder; MulMatrix lives in vec3 folder.
- int/basic/AddInt.cs: Result/Input attributes look commented out in grep output (check that file is well-formed; it builds presumably).
- vec2/Vec2JitterOffset: input `Seed` actually is a sequence step/frame index; a name like `Index` would fit better.
- vec3/EulerToAxisAngle: radians in/out while RotateVector3 and most TiXL rotations use degrees.
- vec3/HasVec3Changed vs vec2/HasVec2Changed: Vec2 has no Mode input or DeltaOnHit output.
- ints/MergeIntLists: input `Enabled` actually means "use MergeMode"; misleading name.
- int/logic/CountInt: TriggerReset sits first in input order; parameter order is scattered (Modulo, OnlyCountChanges between triggers).
- int2/process/MaxInt2: starts at 0, so negative Int2 are clamped to 0.
- int/process/MaxInt, MinInt: no connections outputs int.MinValue/int.MaxValue.

### render_core

- DustParticles: input `Texture_` (trailing underscore); FadingSlideShow `FolderWithImages` (should be a path name such as `FolderPath`), `TriggerUpdate`.
- TransformMatrix: `Rotation_PitchYawRoll` / `Rotation_Quaternion` use underscores. Text/TextOutlines: `EnableZTest` vs DrawScreenQuad `EnableDepthTest`.
- DefineLensFlare: `MattBoxZone` (typo for MatteBox; _VisLenseFxZone uses `MatteBoxZone`). The "Lense" spelling in LenseFlareHoop/_LenseFlareHoopPosition/_VisLenseFxZone/LenseFlareSetup is inconsistent with DefineLensFlare/DrawLensShimmer.
- _ImageFxShaderSetup2 uses `GenerateMipmaps`, _ImageFxShaderSetupStatic uses `GenerateMips`. PrefixSum input is named `InputList2`. ExecuteBufferUpdate output is named `Output2`.
- PickSDXVector4: the name references SharpDX.
- DrawScreenQuad/DrawScreenQuadAdvanced: EnableDepthTest and EnableDepthWrite have identical descriptions (left unchanged).

### render_scene

- AnaglyphCamera.EyeOffet: typo (EyeOffset).
- Shear.Translation: misleading name for the shear amounts.
- CameraWithRotation.RotationOffset2: unclear name next to RotationOffset.
- DrawLineGrid.BlendMod, DrawLensFlares.BlendMod, DrawPointSpritesShaded.BlendMod, _DrawLenseFlare_Old.BlendMod: truncated names (BlendMode).
- DrawLensFlares.Texture_ and DrawPointSprites.Texture_: trailing underscore.
- DrawPointSpritesShaded.GPoints vs DrawPointSprites.Points, and SpriteBuffer vs Sprites: inconsistent naming between the sibling ops.
- DrawPointSprites.ScaleFactor: an int enum selecting F1/F2, which the name hides.
- GetPointLightOccclusion: triple "c" in the op name. "Lense" spelling in LenseFlareSetup/LenseFlareSetupAdvanced/_DrawLenseFlare_Old vs DrawLensFlares/DefineLensFlare. ChromaticAbberation (other group) is also misspelled.
- OrbitCamera: about 20 inputs with mixed ordering; Seed/RotationOffset sit in odd groups.
- ActionCamera: FOV is a rate (change per second), not an absolute value as the name suggests.
- Several gizmo/helper ops in `_` folders (DrawQuad, DrawLensFlares, GetTextureFromContext, SetContextTexture, GetAllCameras) are user-facing in practice. Consider moving them or marking them clearly.
- TextSprites.Filepath vs the standard FilePath.

### image_fx

- stylize/MosiacTiling: op name typo (should be MosaicTiling). Many descriptions linked [MosaicTiling], which does not exist, so the links now use the real name.
- stylize/ChromaticAbberation: op name typo (should be ChromaticAberration). Other ops linked the non-existent [ChromaticAberration], so the links now point to the real name.
- distort/KochKaleidoskope: spelling (Kaleidoscope). The shader file is already named KochKaleidoscope.hlsl.
- distort/FieldToImage is an SDF-field renderer and does not belong in image/fx/distort (field/render would fit better).
- stylize/DetectEdges: `Contrast` is really an additive offset/threshold, not a contrast.
- glitch/RgbTV: `ImageBrightess` (typo) and `Buldge` (typo, should be Bulge).
- feedback/AfterGlow: `ContrastOffset2` is an unclear name. It also still uses the obsolete `_BlobOld`, so consider marking AfterGlow obsolete in favor of AfterGlow2.
- blur/Blur: `Opacity` actually multiplies the RGB (brightness gain) and does not change alpha, so the name is misleading.
- stylize/Pixelate: `TileAmount` should probably be `TileCount`. The extra group is titled "Extra feature".
- glitch/SubdivisionStretch: `Use4xMSAA` is actually RGSS supersampling. Displace and TimeDisplace call the same thing `RGSS_4xAA`, so pick one name.
- feedback/SimpleLiquid2: input names `FX_RG_Velocity`, `FX_B_AddRemoveMass`, `OnClick_AddRemoveMass` and `MouseClick_Force` break PascalCase.
- stylize/ColorPhysarum is still marked "work in progress" and has 21 ungrouped inputs.
- distort/RadialRepeat: `CenterPosition` should be `Center`, and it uses `Zoom` where other ops use Scale.
- _/_trippleImageFxSetup: typo "tripple".

### image_other

- Typos in parameter names: RowSwift (FraserGrid, ZollnerPattern; should be RowShift), ForgroundRatio (RyojiPattern1/2), Constrast (Rings), Offsetimage (MirrorRepeat).
- Rings has underscore-prefixed inputs (_Segments, _Twist, _Thickness, _Ratio, _FillRatio, _HighlightRatio). FraserGrid and ZollnerPattern use RAffects_BarWidth-style names.
- CompareImages: the input named IntensityRange actually sets the size of the level graph. Texture2d/Texture2d2 should be ImageA/ImageB. It uses Rotate here but Rotation in ImageLevels.
- Rotation vs Rotate naming varies across generators (TransformImage/ImageLevels use Rotation; NGon/Blob/SinForm use Rotate).
- NGonGradient/RadialGradient use BiasAndGain while the rest of the group uses GainAndBias.
- CombineMaterialChannels2 is the older op and CombineMaterialChannels the newer one, which is confusing. Its description previously claimed it was the newer version (now fixed).
- _KeepPreviousFrame_Old1 sits in image/use, not in an _obsolete folder.
- LoadImage FileFilter lacks formats such as webp/hdr/exr. I did not check which formats the loader supports.
- ConvertFormat's ScaleFactor is labelled experimental and crops instead of resampling.

### point_gen_modify

- PointTrailFast: `AddSeperatorThreshold` misspelled. SetPointAttributes: Fx1/Fx2/SetExtend vs F1/F2/Scale naming. SamplePointsByCameraDistance: `WForDistance` actually scales F1.
- BlendPoints/SnapToPoints: inputs `PointsA_`/`PointsB_` trailing underscores. PairPointsFor*: `GPoints`/`GTargets`, `SetWTo01` (writes F1).
- TransformSomePoints: `TestParam`, `LengthFactor` look like leftovers; no group structure.
- AttributesFromImageChannels / LinearSamplePointAttributes / SamplePointAttributes_v1: many ungrouped params; op name for v1 replacement unclear.
- PointColorWithField uses a shader under shaders/points/_research.
- SnapPointsToGrid uses shader under _internal; Amount used both as strength and threshold.

### point_draw_sim

- Inconsistent names across point draw ops: BlendMod vs BlendMode, EnableTest/EnableDepthWrite vs EnableZTest/EnableZWrite, UsePointsScale vs UsePointScale, Size vs LineWidth vs PointSize, UseWForSize/UseWForWidth/UseWAsWeight still say "W" though the attribute is F1.
- draw/legacy/_DrawVaryingQuads: input named "Sdfgh"; _DrawQuads: "AltasMode" typo.
- sim/experimental/SimPointMeshCollisions: "Bouncyness", "ClampAccelleration" typos.
- _cpu/TransformCPoint: input "Lists2"; _cpu/RepeatAtCPoints: "DestinationsPoints".
- draw/DrawConnectionLines: debug input TestIndex exposed; IsEnabled/Reset in middle of list; no groups on 14 inputs.
- draw/DrawMeshAtPoints2, DrawBillboards: DrawMeshAtPoints2 default ColorVariationMode/ScaleDistribution=3 (UseW) differs from DrawBillboards (0).
- sim/SimCentricalOffset / SimDirectionalOffset: unused private Modes enum in SimCentricalOffset.cs.
- helper/LoadObjAsPoints: Mode AllVertices is listed but not implemented.

### field_geometry

- SpatialDisplaceSDF: `vScale` is not PascalCase, and `SamplePos` is an abbreviation.
- RaymarchField: `NormalSamplingD` is truncated. FractalSDF: `Minrad`.
- Render2dField: `Glow_` and the dead parameters (see above).
- ApplyVectorField: `SetFx1To`/`SetFx2To` vs. `WriteDistanceTo` elsewhere; `StrengthFactor` is really "StrengthSource".
- SubDivPattern3d: the name says 3D, but the pattern only uses X/Y. `UseAspectForSplit` and `ColorMode` are ints instead of bool/enum.
- PushPullSDF: `StepScale` vs. `StepFactor` (BendField/TwistField/NoiseDisplaceSDF) for the same concept.
- CombineSDF/StairCombineSDF: the generic `K` name. In StairCombineSDF, `Steps` doubles as the groove width.
- RotateAxis vs. RotateField vs. TransformField overlap. RotateField has no gizmo or ITransformable.
- TorusSDF has "Shader Varations" (typo) as a group title.
- Geometry op docs use multi-word AKA terms (e.g. "extrude text"). Left as is, except TextToCurves.

### mesh_particle

- particle/force/SnapToAnglesForce: `AngleCount` is the angle step in degrees (default 45), not a count. Misleading name.
- mesh/generate/ExtrudeCurves: `UseExtend` actually toggles scaling by point Scale (shader UseStretch); `UseWAsWidth` is obsolete and unused but still visible; output slot is named `Output2`.
- mesh/draw/DrawMeshCelShading: `EdgeThresold` typo; Shading is a Gradient while other draw ops use Shading as a Default/Flat enum.
- mesh/modify/TransformMeshUVs: `Uniformscale` casing; TransformMesh uses `UniformScale`. MeshProjectUV/TransformMeshUVs use Translate/Rotate; TransformMesh uses Translation/Rotation.
- mesh/draw: Culling vs CullMode naming differs between DrawMesh/DrawMeshAtPoints/DrawMeshChunksAtPoints. DrawMeshChunksAtPoints uses UseF1ForSize/UseStretch while DrawMeshAtPoints uses ScaleFactor/UsePointScale.
- mesh/modify/CollapseVertices: GridSize is fed into the shader's `Strength`, `Extend` into the noise threshold; names don't describe the effect. Unused enum `Modes`.
- mesh/modify/ColorVerticesWithField: StrengthFactor passed to the shader but unused; three unused enums in .cs.
- mesh/modify/DeformMesh: ShowPivots (group "Obsolete") is unconnected. Same for DisplaceMeshNoise.UseWAsWeight, MeshVolumeForce.ApplyColorOnCollision, RandomJumpForce.AmountFromVelocity.
- mesh/generate/GridRepeatMesh & RadialRepeatMesh: RadialRepeatMesh contains an unused DrawMesh child; OffsetCenter in GridRepeatMesh maps to GridPoints.Pivot (name mismatch).
- particle/force/_/RandomJumpForce and _/VolumeForce live in `_` but are user-facing forces; consider moving VolumeForce to an obsolete location.

### io_flow_string_data

- string/combine/BlendStrings: InputTextA/B should be removed or hidden. Blend + BlendSpread + Scramble + ScrambleSeed + MaxLength + Characters lack structure.
- string/random/BuildRandomString: 14 inputs with partial grouping. The names Insert/InsertString and Scramble/ScrambleRatio are ambiguous.
- io/audio/AudioReaction: the input named `Output` (output mode) and `InputBand` (which picks the data type, not a band) are misleading.
- io/audio/AudioCompressor: `MakeupGainDb` puts the unit in the name. Other ops use plain names.
- flow/context/GetIntVar uses `FallbackValue` while all sibling Get*Var ops use `FallbackDefault`. `LogUpdates`/`LogLevel` differ between GetIntVar and SetIntVar.
- string/list/ZipStringList: StringsOne/StringsTwo are non-standard names (InputA/InputB?).
- flow/BlendScenes: the op description says "float index" but the parameter is BlendFraction.
- data/object/PickObject: Input is listed before Index, while the other Pick* ops follow the same order. OK, no change.

## Undocumented / uncertain

### numbers_float

- Ease/EaseKeys: Interpolation enum values not described individually.
- FailOver mode of MergeFloatLists described roughly ("first list whose content changed recently"); exact switching rules are intricate.
- DampAngle, Damp, DampFloatList, PickFloatList, FloatListToIntList, IntListToFloatList left unchanged (already adequate).
- Ease Duration documented as bars (LocalFxTime) – based on TiXL time being in bars; verify.

### numbers_anim_bool_color_data

- SequenceAnim: modes (UpdateMode, OutputMode, RecordingMode, Interpolation, Direction) kept existing texts only; not verified in depth.
- TriggerAnim / AnimVec2 / AnimVec3: shape/mode params left undocumented (shared AnimMath, not verified).
- AbletonLinkSync, SetPlaybackSpeed, SetBpm, ForwardBeatTaps params left as they were.
- WasTrigger TriggerA/B: source of variables unknown (see above).
- OKLChToColor UseGamma: exact effect not verified, left empty.

### numbers_int_vec

- SpringVec2/SpringVec3 Strength and Tension: no clear units, left without docs.
- EaseVec2/3, EaseVec2/3Keys Interpolation/Direction enums: left to the enum names.
- HasVec2Changed/HasVec3Changed PreventContinuedChanges (see above).
- PerlinNoise2/3 BiasAndGain: existing description kept although it does not match the implementation bug above.
- TransformVec3 matrix convention (transposes before transform) not described in detail.

### render_core

- LenseFlareHoop: no description. Not used anywhere in Lib, and the shader parameters (Width, AspectRatio, Noise, Segments, SegmentWidth) were not verified.
- DrawAsSplitView Mode (RepeatView/SliceView), Stretch; RepeatWithMotionBlur Strength/FadeAlpha/Passes; FadingSlideShow params (IndexAndFraction, FadeType, ScaleMode are plain ints with unknown mapping): left without parameter docs.
- ComputeImageDifference: whether the Difference output is normalized in the graph was not verified (the docs only describe the shader).
- _ImageFxShaderSetup2/_ImageFxShaderSetupStatic params, TextGrid DisplaceTexture/DisplaceAmount/OverrideScale, DrawScreenQuadAdvanced DepthBuffer usage: not documented.
- Removed links to non-existent ops: [SizeToDispatchCount], [_ImageFxShaderSetup], [AString], [RandomString]. Fixed [DefineLenseFlare]/[DrawLenseShimmer] links. ReprojectToUV/ShadowPlane: removed links to their own Example op.
- PrefixSum: the old description (spatial hash map NN search) was wrong. It is now described as a uint prefix scan, based on the prefix-scan.hlsl shader.

### render_scene

- DrawLineGrid.Orientation: the plane mapping per index is unclear (PickVector3 of the xy/xz/yz rotations). Documented only generically. DrawLineGrid.Segments: default 0,0 feeds LinePoints Count; effect unclear, left undocumented.
- _DrawPointInfo.Show: meaning of the int unclear, left undocumented.
- LoadGltfScene.CombineBuffer: described as "merges meshes into one combined buffer with chunks". Its exact downstream requirements were not verified.
- LenseFlareSetupAdvanced element params (FlareSprites, Star, Digital, ...): the existing texts were kept but not verified per child.
- ProjectLight: AmbientColor, SurfaceIntensity, RayIntensity, RaysDecay, ShadowForm, ShadowBias and ShadowResolution were left without docs.
- SetEnvironment.Fallback mapping (0 studio, 1 St. Peters, 2 black) is inferred from the order of PickTexture inputs.
- SampleSpriteAttributes params: not documented (mostly no effect).

### image_fx

- Params of RgbTV, GlitchQuick, SortPixelGlitch, ColorPhysarum, FakeLight, ScreenCloseUp, AdvancedFeedback2 (OKLab range/twirl params), SimpleLiquid/SimpleLiquid2 (physics params), KochKaleidoskope (Offset/Angle/Shade*), RadialRepeat Zoom, GlitchDisplace Mode/OverridePoints, AsciiRender/SubdivisionStretch ColorMode/TextureFx, FieldToImage OutputFormat: these are built from deep subgraphs or the shader mapping was unclear, so they were left at existing/empty text.
- DirectionalBlur RefinementPass/RefinementSamples/RefineSizeFactor: these run as a second graph pass and were not verified.
- FakeLight inputs (HeightMap, LightMap, MidColor, HighlightColor, Direction): the graph mapping to the shader was not traced.

### image_other

- Sketch: OnionSkinRange and SyncToKeyframes (implementation in point/_experimental/_SketchImpl.cs, not traced far enough).
- LinearGradient: OffsetMode.
- WaveForm: OverrideSize (removed my guess). VectorScopePosition and Opacity were left as they were.
- NumberPattern, Rings, RyojiPattern1/2, FraserGrid, ShardNoise, Grain (Brightness, Exponent), RemoveStaticBackground (the Training* rate parameters beyond the general note) and the CustomPixelShader TemplateFile/Offset parameters: no parameter docs added.
- MunchingSquares2: I assume ShadowColor maps to the cbuffer "Black" and HighlightColor to "White" by order (not verified in the .t3).

### point_gen_modify

- APoint: no .cs found; AddSeparator/Selected/Extend left undocumented.
- PointTrail: direction of WriteTrailOrderTo (newest=1 or 0) not verified; IsEnabled behavior not verified.
- SortPoints SortingSpeed, CustomPointShader TimeScale (kept existing text), SetAttributesWithPointFields params, PairPointsForSplines params, PointInfoLines, LineTextPoints, DataPoint* (existing docs kept), SelectPoints, RandomizePoints, MapPointAttributes, TransformWithImage, PointAttributeFromNoise, DoyleSpiralPoints2 params, LineCPoints/LinePoints params: not reviewed in depth, left as is.
- No HowTo tutorial links added (not verified).

### point_draw_sim

- draw/DrawConnectionLines params (ScatterLookUp, LineGradient, ColorOverLifetime, CellSize details) - only summary written.
- draw/DrawMeshAtPoints2 params - left (mirrors DrawBillboards but not verified against mesh shader).
- sim/SamplePointSimAttributes, sim/SimDisplacePoints2d, sim/experimental/GrowStrains, SimFollowMeshSurface, SimPointMeshCollisions, _experimental/ReflectionLines, NumberLinePoints, TraceContourLines params - not verified, left.
- draw/DrawClosedLines LineOffset, draw/DrawRayLines unused params, SimNoiseOffset RotLookupDistance - deliberately left/no doc.

### field_geometry

- RaymarchField UVMapping: the enum names (Local_YZ) don't obviously match the shader defines (MAPPING_XY/XZ). I documented it generically.
- JonBakerSDFLoader ListSelect: I didn't verify the order of the three JSON files in PickString.
- ColorFromField: kept the note that the built-in mesh shaders don't show vertex colors yet. Not verified.
- RaymarchPoints vs. SdfReflectionLinePoints: their shaders differ in detail, and I documented both only at a high level.
- CappedTorusSDF Fill: values derived from the formula (about 0.93 is empty, 3.22 is closed). The mapping is unintuitive.

### mesh_particle

- IcosahedronMesh TexCoord/TexCoord2 UV modes (Faces, Unwrapped, Atlas, FacesSub, GridFacesSub): not verified per mode.
- CustomForce.NormalSamplingDistance, CustomDrawMesh/CustomVertexShader A-D/Image/Gradient: generic, left as is.
- VisualizeMesh.ShowVerticeSelection: rendering path not traced; existing (empty) left empty.
- MeshVolumeForce/_/VolumeForce volume parameters (VolumeShape etc.) left without docs.
- ParticleSystem LifeTime/MaxParticleCount/Speed/Drag kept existing text (not re-verified in detail).

### io_flow_string_data

- flow/context/*Var ClearAfterExecution (inverted logic, see above).
- flow/BlendScenes params (graph op, not analysed in depth).
- flow/skillQuest/_ValueQuizGraph: the description ("draws the value history graph") comes from its inputs and outputs only.
- io/audio/_/DetectBpm: only LowestBpm/HighestBpm documented. LowerLimit/UpperLimit/BufferDurationSec/LockItFactor are unclear.
- io/audio/_/DataRecording, _/DetectBeatOffset, _/GetAllSpatialAudioPlayers params; _obsolete/* params.
- string/datetime/TimeToString: the exact output format (graph of FloatToString/Modulo) was not verified, so the description stays generic.
- string/random/AnimRandomString Rate: units not verified.

## Stats

### numbers_float

73 ops reviewed, 67 .t3ui touched; op descriptions: 4 new (ColorsToList, AnalyzeFloatList, CombineFloatLists, + CurvesToTexture had only a tip), ~40 rewritten/normalized, ~12 fixed-as-wrong (Sigmoid, SmoothStep, Floor, Accumulator, Atan2, AnalyzePianoMidi, DeltaSinceLastFrame, RemapValues, FreezeValue, MergeFloatLists LTP/HTP, RemapFloatList modes, ColorListToInts/SumRange broken links); param descriptions ~95 added, ~20 changed, ~12 removed. 4 code fixes.

### numbers_anim_bool_color_data

Ops touched: 74 of 83 .t3ui. Op descriptions: 13 new, ~55 rewritten/normalized (labels, Tooll->TiXL, bold removed, broken links [SetCommandTime], [Time2], [LFO], [HasValueIIncreased], [AnimatingStuff], [DelayTrigger], [FeedbackEffect] fixed), 4 fixed as wrong (Xor, HasTimeChanged, Time, PickColorFromImage/WasTrigger). Param descriptions: ~45 added/changed, ~20 removed (restating name). Code fixes: 3 (2 files).

### numbers_int_vec

84 ops touched; op descriptions: 7 written new (IntsToList, PickIntFromList, SetIntListValue, DivideVector2, Vec2Magnitude, EaseVec2/EaseVec3 and Keys variants had only links, counted as rewritten), ~70 rewritten/normalized, 6 fixed-as-wrong (MergeIntLists, MaxInt2, Int2Components "4 integers", DampVec3 "float", FloatToInt "floored", Vector4Components "three values", plus dead [AColor] links in PickColor/RgbaToColor); param descriptions ~45 added, ~8 changed, ~25 removed (restating names).

### render_core

Ops touched: 80 of 87 (not touched: LenseFlareHoop, FadingSlideShow, DrawAsSplitView, RepeatWithMotionBlur, ListToBuffer, PickBlendMode, plus the descriptions of GpuMeasure/DrawScreenQuad apart from params). Op descriptions: 26 new, 38 rewritten/normalized, 4 fixed as wrong (PrefixSum, DefineLensFlare FxZoneScale param, RequestedResolution, Text links). Param descriptions: ~75 added, 6 changed, 7 removed. Code fixes: 2 (RtvFromTexture2d).

### render_scene

87 ops in the list, 86 .t3ui touched (TXAA left unchanged; skinning op descriptions kept except SkeletonFromPoints, only their params were added). Op descriptions: about 22 written new, about 52 rewritten/normalized, 6 fixed as wrong (SSAO params, SetPointLight Range, GetPointLightOcclusion outputs/links, SetEnvironment texture input, DrawPointSprites orientation, LenseFlareSetup RandomSeed). Param descriptions: about 230 added, about 35 changed, about 12 removed (trivial restatements). Code fixes: 1 (DrawScene null guard). 1 shader fix blocked by permissions (MotionBlur.hlsl).

### image_fx

53 ops touched. Op descriptions: 7 written new (BuildAsciiFontSorting, _multiImageFxSetup, Displace, FieldToImage, ChromaBlur was minimal, etc.), 44 rewritten or normalized (summary line, AKA, See also, fixed non-existent links), 4 fixed as wrong (Dither "Floyd-Steinberg", Glow "Offset parameter", SimpleLiquid2 FX channel layout, ChromaticDistortion Colorize). Param descriptions: about 150 added, about 25 changed (incl. wrong ones: Blur Opacity/Offset, ChromaticDistortion Colorize, DistortAndShade ShadeColor, MosiacTiling FxTextures, AfterGlow2 BlurAmount), about 3 removed (restating ones, e.g. Sharpen Strength). 3 tutorial links added (HowToUseImageEffects).

### image_other

78 ops touched. Op descriptions: 5 new (MunchingSquares2, MandelbrotFractal, ValueRaster, FirstValidTexture, KeepPreviousFrame), about 70 rewritten or normalized (labels, AKA, See also, broken links such as [WaveForm2], [Adjust Color], [UseRenderTarget], [PlayVideoClip], [B8G8R8A8_UNorm] and the junk "Tesdf" code block in CustomPixelShader), 4 fixed as wrong (Sketch referenced PageIndex/ShowAs inputs that don't exist; TileableNoise was a copy of FractalNoise; CombineMaterialChannels2 claimed to be the newer version; RemapColor's note about gradient alpha controlling intensity). Parameter descriptions: about 150 added, about 25 changed/corrected (e.g. LinearGradient.OffsetMode copy, UseTextureReference wiring), about 20 removed as restating the name.

### point_gen_modify

Ops touched: 58 of 71. Op descriptions: new 6 (ResampleLinePoints, _RandomizePoints_Legacy1, _DoyleSpiralRoot, _ExecuteCombineBuffers, PairPointsForGridWalkLines, TransformSomePoints), rewritten/normalized ~40, fixed-as-wrong 9 (AddNoise, AttributesFromImageChannels, PointTrail, PointTrailFast, PrepareSvgLineTransition, TransformFromClipSpace, BoundingBoxPoints, BoundPoints, BlendPoints mode names) plus 4 broken links fixed (SamplePointAttributes, SetAttrFromImageRgba, MoveMeshToPointLine, CollectSpawnPoints/SvgLineTransitionExample removed). Params: ~70 added, ~8 changed, ~20 removed. Code fixes: 3.

### point_draw_sim

62 ops touched; op descriptions: 11 new, 31 rewritten/normalized, 18 fixed-as-wrong (wrong attribute/size claims, broken links like [CombineBuffer], [KeepPoints], [SimForwardMovement], [DrawRaypoints], [ParticleSimulation], [CollectSpawnPoints]); params: ~120 added, ~15 changed, ~20 removed. 4 C# fixes + 1 shader fix.

### field_geometry

90 ops touched. Op descriptions: 22 written new (incl. 7 internal helpers), 50 rewritten, 4 fixed as wrong (CappedTorusSDF "AKA cube", TextToCurves pivot claim, RepeatFieldAtPoints limit, VoronoiFracture duplicate paragraph), rest kept. Param descriptions: about 200 added/changed, about 25 removed (name-restating). 3 code fixes.

### mesh_particle

74 ops touched (all in list). Op descriptions: 4 written new (DrawMeshChunksAtPoints, GridRepeatMesh, RadialRepeatMesh, RandomJumpForce), ~55 rewritten/normalized, 3 fixed as wrong (LoadGltf "no function", SwitchParticleForce -1/-2, MeshFacesPoints FX1/radius). Param descriptions: ~330 added, ~60 changed (incl. wrong ones: FragmentField chips SphereField/SdfToColor, DeformMesh gizmo crosses, RepeatMeshAtPoints TexCoord2Factor, WrapMode Border), ~15 removed (restating name). Code fixes: 3 .cs, 5 shaders.

### io_flow_string_data

123 ops reviewed, ~105 .t3ui files touched, 9 .cs fixes. Op descriptions: 22 new, ~55 rewritten or normalized (labels, Tips:, See also:, Obsolete./Internal helper. prefixes, T3 -> TiXL, broken [..] chips), 6 fixed as wrong (GetStringVar "float", SetStringVar "float dictionary", SetFloatVar [ContextFloat], StringLength "float", FilesInFolder/SplitString [PickFromStringList], PlaySpatialAudioSample [..] pseudo-links). Param descriptions: ~150 added, ~15 changed (incl. PlaySpatialAudioSample Seek "seconds" -> normalized, FloatToString examples), ~25 restating-only removed.
