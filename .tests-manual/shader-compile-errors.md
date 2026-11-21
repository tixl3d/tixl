---
id: shader-compile-errors
title: Shader Compile Error Messages
scope: shaders
tags: [shaders, migration]
added: 2026-11-02
added-in-version: 4.4
prerequisites:
  - A project with a [CustomPixelShader] connected to an output.
related-help:
  - ../.help/docs/advanced/UpgradingToVulkan.md
---

Covers the message a user sees when a shader written for an earlier TiXL version does not compile.

## Step: Undefined identifier

**Action:**
Add `float3 d = rotate_vector(float3(1,0,0), float4(0,0,0,1));` as a line of the shader code.

**Expected:**
- The error names `rotate_vector` and a line number, and suggests `qRotateVec3`.

## Step: Error below the code field

**Action:**
Keep the `rotate_vector` line from the previous step and look at the parameter window of [CustomPixelShader].

**Expected:**
- The ShaderCode field has an error border, and below it the error with the line number counted within the field
  (the line you typed, not a line of the full shader), followed by the `qRotateVec3` hint.
- An error in AdditionalCode shows below that field instead.
- Fixing the line removes the border and the message.

## Step: Vector size mismatch

**Action:**
Replace that line with `c.rgb *= ImageA.Sample(Sampler, uv);`.

**Expected:**
- The error names the `*=` operator and both vector types, and says that vector sizes must match.

## Step: Fixing the shader

**Action:**
Change the line to `c.rgb *= ImageA.Sample(Sampler, uv).rgb;`.

**Expected:**
- The error disappears and the operator renders.
