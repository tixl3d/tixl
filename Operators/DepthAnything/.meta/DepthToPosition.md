# DepthToPosition Operator

## Overview
Unprojects a depth map into camera-space positions on the GPU.

## Inputs
- **Texture2D DepthTexture** - Source depth data (R32_Float)
- **float FieldOfView** - Vertical field of view in degrees (default: 50)
- **float DepthScale** - Scales the distance of every point (default: 1)

## Outputs
- **Texture2D PositionTexture** - RGBA32F; xyz is the camera-space position (x right, y up, z forward), w is 1 where the depth was usable

## Use Cases
- Point-cloud style displacement
- Relighting and fog from real geometry
- Feeding particle emitters from a camera image
