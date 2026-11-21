# Depth Estimation Basic Example

This example demonstrates the basic workflow for depth estimation using the DepthAnything operator.

## Composition Structure

```
[Video Input] → [DepthAnything] → [Depth Output]
                   ↓
              [DepthTexture] → [DepthToNormal] → [Normal Output]
              [DepthTexture] → [DepthFilter] → [Filtered Depth]
              [DepthTexture] → [DepthThreshold] → [Binary Mask]
```

## Node Configuration

### DepthAnything
- **InputTexture**: Connect to video source
- **Enabled**: true
- **ModelSize**: V3 Small
- **OutputFormat**: Rainbow
- **EnhanceContrast**: true
- **InvertDepth**: false
- **Debug**: false

### DepthToNormal
- **DepthTexture**: Connect from DepthAnything.DepthTexture
- **Strength**: 1.0
- **Invert**: false

### DepthFilter
- **DepthTexture**: Connect from DepthAnything.DepthTexture
- **SigmaSpace**: 1.0
- **SigmaColor**: 0.1
- **Enabled**: true

### DepthThreshold
- **DepthTexture**: Connect from DepthAnything.DepthTexture
- **MinThreshold**: 0.0
- **MaxThreshold**: 0.5
- **Invert**: false
- **UseNormalized**: true

## Expected Results

- **DepthTexture**: Raw depth values (R32_Float format)
- **NormalizedDepthTexture**: Colorized depth visualization
- **NormalTexture**: Surface normals for lighting
- **MaskTexture**: Binary mask based on depth range

## Performance Tips

1. Use Small model for real-time applications
2. Reduce input resolution for faster processing
3. Enable EnhanceContrast for better depth separation
4. Use bilateral filter to reduce noise

## Use Cases

- Background removal (use DepthThreshold with appropriate range)
- Depth-based lighting effects (use normals from DepthToNormal)
- Point cloud generation (use raw DepthTexture)
- Depth-based video effects
