# Lib.image.use

## Operators

- [**Blend**](Blend.md) — Blends two images.
- [**BlendImages**](BlendImages.md) — Blends the connected input images with cross-fading and using a float index.
- [**BlendWithMask**](BlendWithMask.md) — Blends two images by the brightness of a 3rd mask image.
- [**Combine3Images**](Combine3Images.md) — A node to combine 3 input images into the RGBA channels of a new one.
- [**CombineMaterialChannels**](CombineMaterialChannels.md) — Combines roughness, metallic, and ambient occlusion texture maps that are loaded with [LoadImage] into a single texture for [SetMaterial] to create PBR materials.
- [**CombineMaterialChannels2**](CombineMaterialChannels2.md) — Combines roughness, metallic, and ambient occlusion texture maps into a single texture for [SetMaterial].
- [**CustomPixelShader**](CustomPixelShader.md) — Creates a custom shader from a source parameter. This can be useful for prototyping.
- [**DepthBufferAsGrayScale**](DepthBufferAsGrayScale.md) — Converts the provided depth buffer into a grayscale texture.
- [**FirstValidTexture**](FirstValidTexture.md)
- [**Fxaa**](Fxaa.md) — Fast approXimate Anti-Aliasing is a post-FX, use it to improve SDF / RayMarching.
- [**KeepInTextureArray**](KeepInTextureArray.md) — Pick a single "slice" from a TextureArray
- [**KeepPreviousFrame**](KeepPreviousFrame.md)
- [**NormalMap**](NormalMap.md) — Converts the brightness of an image into a normal map that can be used with [SetMaterial].
- [**PickTexture**](PickTexture.md) — Picks one of the connected textures. Only the selected connection will be updated.
- [**RenderWithMotionBlur**](RenderWithMotionBlur.md) — This will render multiple instances of the incoming op each pass with slightly offset local time.
- [**SwapTextures**](SwapTextures.md) — A helper that swaps texture buffers. This can be useful for implementing buffer buffering in compute shader effects.
- [**UseFallbackTexture**](UseFallbackTexture.md) — Automatically replaces a non-loadable texture with a predefined backup.
- [**UseTextureReference**](UseTextureReference.md) — Uses a reference to a [RenderTarget] to implement feedback effects.

---

*Auto-generated from the operator library.*
