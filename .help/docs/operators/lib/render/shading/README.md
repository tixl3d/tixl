# Lib.render.shading

## Operators

- [**DefineMaterials**](DefineMaterials.md) — Defines materials for later use when drawing content or scenes.
- [**Equirectangle**](Equirectangle.md) — Renders the input scene as an image with depth with equirectangular mapping (for 360/VR/fulldome video)
- [**GetPointLightOccclusion**](GetPointLightOccclusion.md) — Returns a float list with the visibility of the current point lights.
- [**IntToWrapmode**](IntToWrapmode.md) — Picks a wrap mode with an index.
- [**LenseFlareSetup**](LenseFlareSetup.md) — Pre-made complex light flare setups with various styles.
- [**LenseFlareSetupAdvanced**](LenseFlareSetupAdvanced.md) — Same as [LenseFlareSetup] but all elements can be tweaked separately.
- [**PointLight**](PointLight.md) — The standard point light with transform gizmo.
- [**SetEnvironment**](SetEnvironment.md) — Sets the image-based lighting (IBL) for the current RenderTarget. This texture can then be used by drawing operators for physically based rendering (PBR) further left in the graph.
- [**SetFog**](SetFog.md) — Adds fog to the incoming scene.
- [**SetMaterial**](SetMaterial.md) — Sets the Physically Based Rendering (PBR) Material for the current RenderTarget which is then used by [DrawMesh] and other PBR rendering operators. Each of the material properties can be controlled by a color and/or by connecting a texture input by using a [LoadImage] operator.
- [**SetPointLight**](SetPointLight.md) — Adds a point light into the scene which illuminates geometry using a combination of "Color", "Intensity" and "Decay".
- [**SetRequestedResolution**](SetRequestedResolution.md) — Set the requested resolution (similar to the Resolution drop-down of the output windows).
- [**SetShadow**](SetShadow.md) — Renders a shadow pass for a directional light source.
- [**TextureToCubeMap**](TextureToCubeMap.md) — Converts a 2d texture ([loaded with [LoadImage]) into a cube map that can then be used by [SetEnvironment] for PBR image-based lighting.
- [**UseMaterial**](UseMaterial.md) — Applies the connected material definition to the context and it will be used by Draw operators further down (left) in the graph.

---

*Auto-generated from the operator library.*
