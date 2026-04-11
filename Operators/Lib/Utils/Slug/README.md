# Slug Utils

Utility helpers for runtime or offline `.slug` generation in `Lib`.

## Folder Layout

- `Generation/`
  - `SlugFontManager.cs`
    - On-the-fly compile manager with cache invalidation and `TryCompile` / `TryCompileToFile` APIs.
  - `SlugGenerationSettings.cs`
    - High-level options (`BandCount`, `FullRange`, `Whitelist`, `CharsetFilePath`, validation, progress callback).
  - `SlugGenerationResult.cs`
    - Result payload (bytes + timing report + effective options/whitelist).
- `Charset/`
  - `SlugCharsetUtils.cs`
    - Helpers for charset parsing/validation using `SlugSharp.Core.CharsetFileParser`.
- `Shaders/`
  - `SlugShaderPaths.cs`
    - Canonical shader references + absolute path resolver.

## Quick Usage

```csharp
using Lib.Utils.Slug;

var manager = new SlugFontManager();

if (!manager.TryCompile("fonts/SpaceMono-Regular.ttf",
                        settings: null,
                        out var result,
                        out var error))
{
    Log.Error(error);
    return;
}

var slugBytes = result.Bytes;
```

## Generate With In-Memory Charset (Variable)

Example with explicit C# chars (readable):

```csharp
using Lib.Utils.Slug;

var manager = new SlugFontManager();
var settings = new SlugGenerationSettings
{
    BandCount = 16,
    FullRange = false,
    Whitelist = new HashSet<int>
                {
                    (int)' ',
                    (int)'A', (int)'B', (int)'C',
                    (int)'a', (int)'b', (int)'c',
                    0x4E2D, // U+4E2D
                },
    ValidateOutput = true,
};

if (!manager.TryCompile("fonts/SpaceMono-Regular.ttf", settings, out var result, out var error))
{
    Log.Error(error);
    return;
}

File.WriteAllBytes("SpaceMono.slug", result.Bytes);
```

## Generate With Charset File (`eascii.txt`)

An `eascii.txt` file is available at:

- `Operators/Lib/Utils/Slug/eascii.txt`

Example:

```csharp
using Lib.Utils.Slug;

var manager = new SlugFontManager();
var settings = new SlugGenerationSettings
{
    FullRange = false,
    CharsetFilePath = "Operators/Lib/Utils/Slug/eascii.txt",
    ValidateCharsetFile = true,
    ValidateOutput = true,
};

if (!manager.TryCompileToFile("fonts/SpaceMono-Regular.ttf",
                              "out/SpaceMono-eascii.slug",
                              settings,
                              out var result,
                              out var error))
{
    Log.Error(error);
    return;
}

Log.Debug($"Generated {result.ByteCount} bytes in {result.Report?.TotalMilliseconds} ms");
```

Example `eascii.txt` content with ranges:

```txt
# Latin uppercase
A-Z

# Latin lowercase
a-z

# Digits
0-9

# Extra symbols
! ? . , : ; - _ + = / \\ @ # $ % & * ( )
```

## Merge Behavior (Whitelist + CharsetFile)

When `FullRange = false`:

- If only `Whitelist` is set, it is used.
- If only `CharsetFilePath` is set, file code points are used.
- If both are set, both sets are merged (deduplicated).

When `FullRange = true`, whitelist filtering is ignored.

## Progress Callback (Optional)

```csharp
using Lib.Utils.Slug;

var settings = new SlugGenerationSettings
{
    ProgressInterval = 100,
    ProgressCallback = update =>
    {
        Log.Debug($"Slug stage={update.Stage} t={update.ElapsedMilliseconds}ms emitted={update.EmittedGlyphCount}");
    }
};
```

## Cache Control

`SlugFontManager` caches by:

- font absolute path
- font last write time (UTC)
- band count
- full-range flag
- effective whitelist signature

Helpers:

- `manager.InvalidateCacheForFont(fontPath)`
- `manager.ClearCache()`
- `manager.CacheEntryCount`

## Shader Integration Note

The Slug rendering path depends on both shaders in:

- `Operators/Lib/Assets/shaders/slug/SlugVertexShader.hlsl`
- `Operators/Lib/Assets/shaders/slug/SlugPixelShader.hlsl`

Use:

- `SlugShaderPaths.RelativeVertexShaderPath`
- `SlugShaderPaths.RelativePixelShaderPath`
- `SlugShaderPaths.TryResolveAbsoluteShaderPaths(...)`

to avoid hardcoded string duplication in operators.

## Namespace

All classes stay in the same namespace:

- `Lib.Utils.Slug`

So existing call sites can keep the same `using` statement even after folder reorganization.
