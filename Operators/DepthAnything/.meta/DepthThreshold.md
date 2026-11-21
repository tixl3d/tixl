# DepthThreshold Operator

## Overview
Creates binary masks from depth data using threshold ranges. Useful for foreground/background separation.

## Inputs
- **Texture2D DepthTexture** - Source depth data
- **float MinThreshold** - Lower bound (0-1 or absolute)
- **float MaxThreshold** - Upper bound (0-1 or absolute)
- **bool Invert** - Flip mask output
- **bool UseNormalized** - Use relative (0-1) or absolute thresholds

## Outputs
- **Texture2D MaskTexture** - Binary mask (R8)
- **float PixelRatio** - Percentage of pixels in range

## Use Cases
- Background removal
- Depth-based masking
- Object segmentation
- Depth-based video effects
