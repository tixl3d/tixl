# DepthFilter Operator

## Overview
Applies bilateral filtering to depth maps for edge-aware smoothing. Reduces noise while preserving depth edges.

## Inputs
- **Texture2D DepthTexture** - Source depth data
- **float SigmaSpace** - Spatial filter radius (default: 1.0)
- **float SigmaColor** - Depth similarity threshold (default: 0.1)
- **bool Enabled** - Toggle filtering on/off

## Outputs
- **Texture2D FilteredDepth** - Smoothed depth output

## Use Cases
- Noise reduction in depth maps
- Pre-processing before normal generation
- Smoothing while preserving edges
