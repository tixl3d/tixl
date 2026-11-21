---
id: depth-anything-3
title: Depth Anything 3 Depth Chain
scope: depth-anything
tags: [ai, video]
added: 2026-11-01
added-in-version: 4.4
prerequisites:
  - A scratch project, not one that is shared over a collaboration session.
  - An image or webcam texture, and at least one DA3 model in Operators/DepthAnything/Assets (run Scripts/download_models.py da3-small.onnx).
---

Verifies the Depth Anything 3 model support, the GPU input path and the depth operators that consume its outputs.

## Step: Estimating depth with a V3 model

**Action:**
Connect an image to `[DepthAnything]`, leave `ModelSizeParam` on "V3 Small", set `ExecutionProviderParam` to CUDA or DirectML, and view `NormalizedDepthTexture`.

**Expected:**
- `Status` reads "Ready" with the provider and model file name, then "Processing".
- Near objects are bright and far objects dark.
- `InferenceMs` and `LatencyMs` show values, with `LatencyMs` not much larger than `InferenceMs` plus a frame or two.

## Step: Without a model installed

**Action:**
Rename the DA3 model files in Assets so none is found, restart TiXL and create a new `[DepthAnything]`.

**Expected:**
- `Status` reads "Model not found: da3-small-fp16.onnx" and the operator shows an error state until a model is installed from the model store.

## Step: Any input texture format

**Action:**
Feed `[DepthAnything]` from a render output with a float format (for example the result of `[RenderTarget]` at 16 bit float) instead of a 8 bit image.

**Expected:**
- Depth is estimated, with no "Unsupported input format" warning.

## Step: Smoothing

**Action:**
With live or animated input, compare `Smoothing` at 0 and at 0.5 on `NormalizedDepthTexture`.

**Expected:**
- At 0.5 the brightness of flat regions flickers noticeably less, and fast motion trails slightly.

## Step: Confidence, sky and field of view

**Action:**
Select a model that predicts them (V3 Base gives confidence, V3 Metric Large gives sky) and view `ConfidenceTexture` or `SkyTexture`. Read `VerticalFov`.

**Expected:**
- Outputs the model does not predict stay empty. `ConfidenceTexture` holds the model's raw confidence; V3 Base
  returns values just above 1 with a narrow range, so the preview looks almost flat rather than like a mask.
- `VerticalFov` is a plausible camera angle (typically 30 to 90 degrees) for V3 Base and 0 for models without camera output.

## Step: GPU depth operators

**Action:**
Connect `DepthTexture` to `[DepthFilter]`, `[DepthToNormal]` and `[DepthToPosition]` and view each output.

**Expected:**
- `[DepthFilter]` smooths noise while silhouettes stay sharp.
- `[DepthToNormal]` shows a lit-looking normal map with no border artifacts.
- `[DepthToPosition]` outputs a float texture whose blue channel grows with distance.
- Frame rate stays steady while the chain runs.

## Step: Depth parallax

**Action:**
Connect the image, `DepthTexture`, `MinDepth` and `MaxDepth` to `[DepthParallax]` and animate `Offset` back and forth.

**Expected:**
- Near objects shift more than far ones, and no black holes open at depth edges.
- `FocalPlane` moves the depth at which the image stays still.
