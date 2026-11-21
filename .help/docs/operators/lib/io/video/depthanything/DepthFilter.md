# DepthFilter

*in [Lib.io.video.depthanything](README.md)*

Smooths a depth map with an edge-aware bilateral filter.

Reduces noise in estimated depth while keeping depth edges sharp.

AKA: bilateral filter, depth smoothing, denoise

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **DepthTexture** (Texture2D) | The depth map to filter. |
| **SigmaSpace** (Single) | Spatial extent of the filter. |
| **SigmaColor** (Single) | Depth difference over which neighbors still contribute. Smaller values keep edges sharper. |
| **Enabled** (Boolean) | Passes the depth through unchanged when disabled. |

## Outputs

| Name | Type |
|---|---|
| **FilteredDepth** | T3.Core.DataTypes.Texture2D |


