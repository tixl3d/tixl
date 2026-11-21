# DepthThreshold

*in [Lib.io.video.depthanything](README.md)*

Creates a mask from the depth range between two thresholds.

Useful for separating foreground from background.

AKA: depth mask, depth key, background removal

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **DepthTexture** (Texture2D) | The depth map to threshold. |
| **MinThreshold** (Single) | Lower bound of the depth range that is kept. |
| **MaxThreshold** (Single) | Upper bound of the depth range that is kept. |
| **Invert** (Boolean) | Flips the mask. |
| **UseNormalized** (Boolean) | Interprets the thresholds relative to the depth range of the frame instead of as absolute values. |

## Outputs

| Name | Type |
|---|---|
| **MaskTexture** | T3.Core.DataTypes.Texture2D |
| **PixelRatio** | System.Single |


