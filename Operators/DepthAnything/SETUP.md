# DepthAnything Operator - Integration Guide

## Quick Start

### 1. Add to Solution

Add the project reference to `t3.sln`:

```xml
<ProjectReference Include="Operators\DepthAnything\DepthAnything.csproj" />
```

### 2. Download Models

Run `python Scripts/download_models.py` (or `Scripts/download_models.ps1`) to fetch the Depth Anything 3 models into `Assets/`.
Pass file names to fetch only some, for example `python Scripts/download_models.py da3-small.onnx`.
See `README.md` for the file names each model selection looks for.

### 3. Build and Run

```bash
# Build the solution
dotnet build t3.sln

# Or build from Visual Studio
```

## Operator Location in Editor

After building, operators will appear in the node browser under:

```
Lib > io > video > depthanything >
├── DepthAnything          (Main depth estimation)
├── DepthToNormal          (Depth → Normal conversion)
├── DepthToPosition        (Depth → camera-space positions)
├── DepthToPoints          (Depth → point buffer for live splats)
├── DepthParallax          (Shift an image by its depth)
├── DepthFilter            (Bilateral filtering)
└── DepthThreshold         (Depth-based masking)
```

## Typical Workflow

```
Texture2D Input
    │
    ├──> [DepthAnything] ──> DepthTexture (R32_Float)
    │                           │
    │                           ├──> [DepthFilter] ──> Filtered Depth
    │                           │
    │                           ├──> [DepthToNormal] ──> Normal Map
    │                           │
    │                           └──> [DepthThreshold] ──> Binary Mask
    │
    └──> Original (passthrough)
```

## Performance Tips

1. **Use a Small model** with CUDA for real-time applications
2. **Enable EnhanceContrast** for better depth separation
3. **Use bilateral filter** to reduce noise before normal generation
4. **Cache results** if input doesn't change every frame

## Troubleshooting

### Model not found error
- Ensure `.onnx` files are in `Assets/` folder
- Check file names match exactly (case-sensitive)
- Verify files are not corrupted

### Low depth quality
- Try larger model size (Base or Large)
- Enhance contrast for better separation
- Use depth filter to reduce noise

### Performance issues
- Reduce input resolution before depth estimation
- Use Small model instead of Base/Large
- Disable debug mode in production

## Dependencies

All dependencies are managed via NuGet:
- Microsoft.ML.OnnxRuntime.Gpu (1.24.4; the CPU-only package on macOS)
- SharpDX (via TiXL Core)

The model files in `Assets/` are not tracked in git — `Scripts/download_models.py` fetches them
(also see `Assets/.gitignore`).

## License

- This code: MIT
- ONNX Runtime: MIT
