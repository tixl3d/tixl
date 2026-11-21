# DepthParallax Operator

## Overview
Shifts an image by its depth map, as if the camera moved sideways. Runs on the GPU.

## Inputs
- **Texture2D ImageTexture** - The image to shift
- **Texture2D DepthTexture** - The matching depth map
- **float MinDepth, MaxDepth** - Depth range, usually wired from DepthAnything
- **Vector2 Offset** - Camera move as a fraction of the image
- **float FocalPlane** - Depth that stays still, 0 (far) to 1 (near)
- **int Steps** - Search steps per pixel

## Outputs
- **Texture2D ResultTexture** - RGBA16F shifted image

## Use Cases
- 3D photo and 2.5D video effects
- Depth-driven camera moves on stills
