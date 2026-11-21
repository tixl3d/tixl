# Background Removal Using Depth

This example shows how to use depth estimation to remove or replace backgrounds in video content.

## Composition Structure

```
[Video Input] → [DepthAnything] → [DepthThreshold] → [Mask]
                                              ↓
[Video Input] → [MaskTexture] → [Blend Modes] → [Output]
```

## Node Configuration

### Step 1: Depth Estimation
**DepthAnything**
- **InputTexture**: Connect to video source
- **ModelSize**: V3 Small (for real-time)
- **OutputFormat**: Grayscale
- **EnhanceContrast**: true
- **InvertDepth**: false (foreground = closer)

### Step 2: Create Foreground Mask
**DepthThreshold**
- **DepthTexture**: Connect from DepthAnything.DepthTexture
- **MinThreshold**: 0.0 (near objects)
- **MaxThreshold**: 0.3 (adjust based on scene)
- **Invert**: false
- **UseNormalized**: true

### Step 3: Apply Mask
**Blend/Composite**
- Connect original video to Source A
- Connect DepthThreshold.MaskTexture to Mask input
- Set blend mode to "Mask" or "Alpha Blend"

### Alternative: Chroma Key Style
1. Use Color output format from DepthAnything
2. Adjust threshold to isolate subject
3. Use the mask to blend with background video or color

## Fine-Tuning Tips

### Finding the Right Threshold
1. Enable Debug mode on DepthAnything to see depth values
2. Use MinDepth and MaxDepth outputs to gauge range
3. Start with narrow range and expand gradually

### Dealing with Depth Edge Artifacts
1. Apply bilateral filter (DepthFilter) before thresholding
2. Use slightly feathered edges in blending
3. Consider using depth-based soft masking

### Multiple Subjects at Different Depths
1. Create multiple DepthThreshold nodes
2. Each with different ranges
3. Combine masks with logic operators

## Advanced Techniques

### Depth-Based Gradient Masking
```
[DepthTexture] → [Remap Range] → [Gradient Mask]
```
- Creates soft transitions instead of hard cuts
- Useful for natural depth-of-field effects

### Normal-Aware Blending
```
[DepthTexture] → [DepthToNormal] → [Normal-Aware Blend]
```
- Uses surface normals for better edge detection
- Reduces halo effects on complex geometries

## Performance Considerations

- V3 Small: ~6 ms per frame on TensorRT (fp16) and ~30 ms on CUDA - suitable for live video
- For offline processing, use V3 Base for better quality
- Consider lower input resolution (720p instead of 1080p)

## Common Issues and Solutions

| Issue | Cause | Solution |
|-------|-------|----------|
| Subject is cut off | Threshold too narrow | Increase MaxThreshold |
| Background included | Threshold too wide | Decrease MaxThreshold |
| Noisy edges | Raw depth data | Add DepthFilter before threshold |
| Poor subject separation | Low depth contrast | Enable EnhanceContrast |
