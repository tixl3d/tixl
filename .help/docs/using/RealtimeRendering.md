# Real-time Rendering for Artists

This page collects technical concepts and terminology that might sound intimidating. To be clear, you will not need to know or even understand any of the things mentioned here. But as you become more and more experienced, your ambitions will increase, and then knowing these concepts will help you get the most out of your project.

## Mip-maps

You will notice that many operators provide a setting to [Generate Mipmaps](https://en.wikipedia.org/wiki/Mipmap). The idea behind this is that, with generated and stored smaller and smaller copies of our image, each one has half the resolution. For a 1024px image, this results in 10 levels (512, 256, 128, 64, 32, 16, 8, 4, 2, 1).

Scaling down is normally done with a very good filter. This means when looking at an object from a large distance, the GPU can show this scaled-down version to avoid flickering.

Adding mip-maps will increase the memory impact of your texture by roughly 30%. It will also take considerable time to generate these images. They are automatically generated for all images loaded from files.

In most situations, you can ignore this overhead, but if you want (or need) to optimize performance, this change can be significant.

Here are some rules of thumb for when to use mip-maps:

- For textures used by billboards or sprites
- For textures (especially normal maps) used for PBR-rendering
- For textures that can be cached (e.g., with [Once]+[RenderTarget])

Avoid when:
- Texture is animated
- For chained image effects
- For post-render effects

## Multi-Sampling Anti-Aliasing (MSAA)

## Z-Buffer

The Z-buffer, also known as the **depth buffer**, is a crucial concept in 3D rendering. 

In simple terms, the Z-buffer stores information about the depth or distance of each pixel from the viewer's perspective in a 3D scene. The "Z" in Z-buffer stands for the "depth" axis in the 3D coordinate system, in TOOLL3 pointing away from the camera..

Here's a breakdown of how the Z-buffer works:

1. **Depth Information**: Each pixel in the Z-buffer stores a value representing its distance from the viewer along the Z-axis. The depth value is typically measured as a linear distance or as a value in a normalized range (e.g., between 0 and 1).
2. When **rendering** a 3D scene, objects are drawn onto the screen one at a time. The Z-buffer is used to keep track of the depth of each pixel as objects are drawn.
3. **Depth Testing**: Before drawing a pixel on the screen, the Z-buffer is consulted to check whether the new pixel is closer to the viewer than the existing pixel at that position. If the new pixel is closer, it is drawn, and its depth value is updated in the Z-buffer. If it's farther away, it is discarded.
4. Hidden Surface Removal: The Z-buffer helps in automatically handling hidden surfaces, ensuring that only the closest visible surfaces are rendered. This process is known as depth buffering or z-buffering.

In TiXL you enable depth buffer in  [RenderTarget]. it will then manage all the necessary internal steps. If enabled, you can enable DepthTest or DepthWrite with many draw operators like [DrawMesh] [DrawPoints], etc. Without a depth buffer (or depth testing) the last element draw will always be on top.

Some advanced side notes: Depth-buffers are especially complicated in conjunction with MSAA because it will require the depth buffer to be copied in a second pass. 

## Texture Formats

DirectX is provides an [overwhelming list of texture formats](https://learn.microsoft.com/en-us/windows/win32/api/dxgiformat/ne-dxgiformat-dxgi_format). 99% of the will only by relevant in fringe situations. But many operators provide a format dropdown with this list. There are three parts that are relevant for each format:

1. The  **color channels** and their order (e.g. RGBA has for channels)
2. The **resolution** of each channel (e.g. 8 bit  for 256 colors and 16 for 65536, 32bit for 4 billion)
3. The **encoding of the value range**. This is a [complicated topic](https://learn.microsoft.com/en-us/windows/win32/api/dxgiformat/ne-dxgiformat-dxgi_format), but you should know that with float precision the resolution is higher in the lower ranges and gets sparse for very large numbers. These keyword might be relevant:
   - FLOAT - the default used by TiXL
   - UNORM - the value range is limited to 0...1 

When working with TiXL these formats might be relevant:

- `R16G16B16A16_FLOAT` the default. Good compromise for HDR rendering.
- `R32G32B32A32_FLOAT` in situations where high quality output is required.
- `R8G8B8A8_UNorm` the default format when loading 8bit images.
- `G8G8R8A8_UNorm` Look carefully! The G and R channels are swapped here. Sadly this format is used by many videos and can help to speed up [rendering videos](ExportVideos.md).


You can see the current texture format in the output window:

![Alt text](/images/image.png)


## Cube Maps
Todo

## Blend Modes

## The Render Pipeline
Todo

## Shaders
Todo

### HLSL (DirectX's Shader Language)

### Compute Shaders

### Pixel (or Fragment) Shaders

### Vertex Shaders

## Physically Based Rendering (PBR)

## Ray Marching

## Mesh Rendering


## Bias-Functions

Many of TiXL’s operators have a Gain or a BiasAndGain parameter. This concept is incredibly powerful for procedural content, so we should cover it here. Both are so called "mapping function": they match an incoming value -- let's say a brightness -- to a new value. If the mapping function is a straight diagonal line, the function is neutral and has no effect. If it is curved up in the middle, the image gets brighter. Photoshop Curve adjustment is a good example of a mapping curve.

_Bias_ and _Gain_ are two subsets of mapping curves that are defined for values between 0 and 1 (the so called normalized range). _Gain_ (sometimes called bias) pushes values towards the upper left corner or lower left corner resulting in brighter or darker images. 

_Schlick bias_ is another special function that creates curves that look like an S. It increases or reduces contrast similar like you would do with adjustment curves in Photoshop.

TiXL’s _GainAndBias_ combines both curves into a single function with two parameters. Here are some examples what you can do with that:

- Adjust brightness and contrast of images
- Define a smooth threshold where black jumps to white
- Make animations smoother, snappier or with an ease in or easy out slope.
- Adjust a uniform random distribution to allow changes to mostly occur at the center (e.g. most grains of sand have roughly the same size but some are very small and some are very large)

It takes a while to get the hang of if. But once you get hooked, you will start to see mapping curves everywhere in nature and will never want to go back to a linear distribution.

___

## DirectX Specific

The following are advanced DirectX specific concepts, that are called different on other APIs like Vulkan.

## Shader Resource Views (SRV)

## Unordered Access Views (UAV)

___

## TiXL Specific Concepts

### Graph Rendering (Directed Acyclic Graphs)

### Evaluation Context

### Caching