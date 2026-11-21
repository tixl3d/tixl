# DepthToNormal Operator

## Overview
Converts depth maps to normal maps using Sobel edge detection. Essential for depth-based lighting effects.

## Inputs
- **Texture2D DepthTexture** - Source depth data (R32_Float)
- **float Strength** - Normal intensity (default: 1.0)
- **bool Invert** - Flip Z component (default: false)

## Outputs
- **Texture2D NormalTexture** - RGB normal map (tangent space)

## Use Cases
- Depth-based lighting
- Surface reconstruction
- Material effects based on geometry
