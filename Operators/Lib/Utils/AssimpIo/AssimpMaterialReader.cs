#nullable enable
using System.Threading;
using StbImageSharp;
using AssimpMaterial = Assimp.Material;
using AssimpScene = Assimp.Scene;
using TextureSlot = Assimp.TextureSlot;
using TextureType = Assimp.TextureType;

namespace Lib.Utils.AssimpIo;

/// <summary>Decoded RGBA8 pixels, ready to become a texture on the render thread.</summary>
internal sealed class AssimpTextureImage(int width, int height, byte[] rgba)
{
    public readonly int Width = width;
    public readonly int Height = height;
    public readonly byte[] Rgba = rgba;
}

/// <summary>
/// A material in the terms of TiXL's <c>PbrMaterial</c>. Roughness, metallic and occlusion are baked
/// into one map in the RMO channel layout the PBR shaders expect (R roughness, G metallic, B
/// occlusion); the shaders add <see cref="Roughness"/> and <see cref="Metallic"/> on top of it.
/// </summary>
internal sealed class AssimpMaterialData
{
    public string Name = string.Empty;
    public Vector4 BaseColor = Vector4.One;
    public Vector4 EmissiveColor = new(0, 0, 0, 1);
    public float Roughness = DefaultRoughness;
    public float Metallic;

    public AssimpTextureImage? BaseColorMap;
    public AssimpTextureImage? EmissiveMap;
    public AssimpTextureImage? NormalMap;
    public AssimpTextureImage? RoughnessMetallicOcclusionMap;

    /// <summary>Releases the decoded pixels after their upload; they can be large and are not needed again.</summary>
    public void DropMaps()
    {
        BaseColorMap = null;
        EmissiveMap = null;
        NormalMap = null;
        RoughnessMetallicOcclusionMap = null;
    }

    public const float DefaultRoughness = 0.5f;
}

/// <summary>
/// Reads Assimp materials and decodes their textures, from embedded data or from files next to the
/// model. Runs on the import worker; decoding failures become warnings and fall back to no map.
/// </summary>
internal sealed class AssimpMaterialReader(AssimpScene scene, string modelPath, List<string> warnings, CancellationToken token)
{
    public AssimpMaterialData Read(AssimpMaterial material)
    {
        token.ThrowIfCancellationRequested();

        var data = new AssimpMaterialData { Name = material.HasName ? material.Name : string.Empty };

        data.BaseColor = TryGetColor(material, "$clr.base", out var baseColor)
                             ? baseColor
                             : material.HasColorDiffuse ? SanitizeColor(material.ColorDiffuse) : Vector4.One;

        if (material.HasOpacity)
        {
            var opacity = float.IsFinite(material.Opacity) ? material.Opacity : 1;
            data.BaseColor = new Vector4(data.BaseColor.X, data.BaseColor.Y, data.BaseColor.Z, data.BaseColor.W * opacity);
        }

        if (material.HasColorEmissive)
            data.EmissiveColor = SanitizeColor(material.ColorEmissive) with { W = 1 };

        float? roughnessFactor = TryGetFloat(material, "$mat.roughnessFactor", out var roughness) ? roughness : null;
        float? metallicFactor = TryGetFloat(material, "$mat.metallicFactor", out var metallic) ? metallic : null;

        data.BaseColorMap = TryLoad(material, TextureType.BaseColor) ?? TryLoad(material, TextureType.Diffuse);
        data.EmissiveMap = TryLoad(material, TextureType.EmissionColor) ?? TryLoad(material, TextureType.Emissive);
        data.NormalMap = TryLoad(material, TextureType.NormalCamera) ?? TryLoad(material, TextureType.Normals);

        // A material without emissive color but with an emissive map would hide the map
        if (data.EmissiveMap != null && data.EmissiveColor.X + data.EmissiveColor.Y + data.EmissiveColor.Z <= 0)
            data.EmissiveColor = Vector4.One;

        data.RoughnessMetallicOcclusionMap = BuildRoughnessMetallicOcclusion(material, roughnessFactor, metallicFactor);
        if (data.RoughnessMetallicOcclusionMap != null)
        {
            // Factors are baked into the map
            data.Roughness = 0;
            data.Metallic = 0;
        }
        else
        {
            data.Roughness = roughnessFactor ?? AssimpMaterialData.DefaultRoughness;
            data.Metallic = metallicFactor ?? 0;
        }

        return data;
    }

    /// <summary>
    /// glTF stores roughness in G and metallic in B of one map; other formats use separate grayscale
    /// maps. Occlusion comes from the ambient occlusion slot or glTF's occlusion (exposed as light map).
    /// Factors missing from the file scale a present map by 1 and an absent one to the defaults.
    /// </summary>
    private AssimpTextureImage? BuildRoughnessMetallicOcclusion(AssimpMaterial material, float? roughnessFactorInFile, float? metallicFactorInFile)
    {
        var combined = TryLoad(material, TextureType.GltfMetallicRoughness);
        var roughnessMap = combined == null ? TryLoad(material, TextureType.Roughness) : null;
        var metallicMap = combined == null ? TryLoad(material, TextureType.Metalness) : null;
        var occlusionMap = TryLoad(material, TextureType.AmbientOcclusion) ?? TryLoad(material, TextureType.Lightmap);

        if (combined == null && roughnessMap == null && metallicMap == null && occlusionMap == null)
            return null;

        var roughnessFactor = roughnessFactorInFile ?? (combined != null || roughnessMap != null ? 1 : AssimpMaterialData.DefaultRoughness);
        var metallicFactor = metallicFactorInFile ?? (combined != null || metallicMap != null ? 1 : 0);

        var width = 1;
        var height = 1;
        foreach (var image in (ReadOnlySpan<AssimpTextureImage?>)[combined, roughnessMap, metallicMap, occlusionMap])
        {
            if (image == null)
                continue;

            width = Math.Max(width, image.Width);
            height = Math.Max(height, image.Height);
        }

        if (!TryReserveTextureBytes((long)width * height * 4))
        {
            warnings.Add("Combined roughness/metallic/occlusion map exceeds the texture memory budget, skipped");
            return null;
        }

        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                float roughnessValue;
                float metallicValue;
                if (combined != null)
                {
                    roughnessValue = Sample(combined, x, y, width, height, 1);
                    metallicValue = Sample(combined, x, y, width, height, 2);
                }
                else
                {
                    roughnessValue = roughnessMap != null ? Sample(roughnessMap, x, y, width, height, 0) : 1;
                    metallicValue = metallicMap != null ? Sample(metallicMap, x, y, width, height, 0) : 1;
                }

                var occlusion = occlusionMap != null ? Sample(occlusionMap, x, y, width, height, 0) : 1;

                var offset = (y * width + x) * 4;
                rgba[offset] = ToByte(roughnessValue * roughnessFactor);
                rgba[offset + 1] = ToByte(metallicValue * metallicFactor);
                rgba[offset + 2] = ToByte(occlusion);
                rgba[offset + 3] = 255;
            }
        }

        return new AssimpTextureImage(width, height, rgba);
    }

    /** Nearest-neighbour lookup, so maps of different resolution can be combined. */
    private static float Sample(AssimpTextureImage image, int x, int y, int width, int height, int channel)
    {
        var sourceX = x * image.Width / width;
        var sourceY = y * image.Height / height;
        return image.Rgba[(sourceY * image.Width + sourceX) * 4 + channel] / 255f;
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);

    private AssimpTextureImage? TryLoad(AssimpMaterial material, TextureType type)
    {
        if (material.GetMaterialTextureCount(type) == 0)
            return null;

        if (!material.GetMaterialTexture(type, 0, out TextureSlot slot) || string.IsNullOrWhiteSpace(slot.FilePath))
            return null;

        var key = slot.FilePath;
        if (_imagesByKey.TryGetValue(key, out var cached))
            return cached;

        token.ThrowIfCancellationRequested();
        var image = TryDecode(slot.FilePath);
        _imagesByKey[key] = image;
        return image;
    }

    private AssimpTextureImage? TryDecode(string texturePath)
    {
        try
        {
            var embedded = scene.GetEmbeddedTexture(texturePath);
            if (embedded != null)
            {
                if (embedded.HasCompressedData)
                    return Decode(embedded.CompressedData, texturePath);

                if (embedded.HasNonCompressedData && embedded.Width > 0 && embedded.Height > 0)
                {
                    if (embedded.Width > MaxTextureSize || embedded.Height > MaxTextureSize)
                    {
                        warnings.Add($"Texture {texturePath} exceeds {MaxTextureSize}px, skipped");
                        return null;
                    }

                    return FromTexels(embedded);
                }
            }

            if (texturePath.StartsWith('*'))
            {
                warnings.Add($"Embedded texture {texturePath} is missing");
                return null;
            }

            var filePath = ResolveTextureFile(texturePath);
            if (filePath == null)
            {
                warnings.Add($"Texture not found: {texturePath}");
                return null;
            }

            var length = new FileInfo(filePath).Length;
            if (length > MaxTextureFileBytes)
            {
                warnings.Add($"Texture too large, skipped: {texturePath}");
                return null;
            }

            return Decode(File.ReadAllBytes(filePath), texturePath);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            warnings.Add($"Failed to read texture {texturePath}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Texture paths in model files are relative to the model, sometimes absolute paths of the
    /// author's machine, and often written with Windows separators. Falls back to the bare file name
    /// next to the model, which is how such files are usually shipped.
    /// </summary>
    private string? ResolveTextureFile(string texturePath)
    {
        var modelFolder = Path.GetDirectoryName(modelPath) ?? string.Empty;
        var normalized = texturePath.Replace('\\', '/');

        if (Path.IsPathRooted(normalized) && File.Exists(normalized))
            return normalized;

        var relative = Path.Combine(modelFolder, normalized.TrimStart('/'));
        if (File.Exists(relative))
            return relative;

        var fileName = Path.GetFileName(normalized);
        var sibling = Path.Combine(modelFolder, fileName);
        if (File.Exists(sibling))
            return sibling;

        var inTexturesFolder = Path.Combine(modelFolder, "textures", fileName);
        return File.Exists(inTexturesFolder) ? inTexturesFolder : null;
    }

    private AssimpTextureImage? Decode(byte[] encoded, string texturePath)
    {
        // Read the header first: a tiny file can declare huge dimensions, and decoding would allocate
        // the full image before any size or budget check could run.
        using (var infoStream = new MemoryStream(encoded, writable: false))
        {
            var info = ImageInfo.FromStream(infoStream);
            if (info is not { Width: > 0, Height: > 0 })
            {
                warnings.Add($"Texture {texturePath} has an unsupported format, skipped");
                return null;
            }

            if (info.Value.Width > MaxTextureSize || info.Value.Height > MaxTextureSize)
            {
                warnings.Add($"Texture {texturePath} exceeds {MaxTextureSize}px, skipped");
                return null;
            }

            if (!TryReserveTextureBytes((long)info.Value.Width * info.Value.Height * 4))
            {
                warnings.Add($"Texture memory budget exceeded, skipped: {texturePath}");
                return null;
            }
        }

        var image = ImageResult.FromMemory(encoded, ColorComponents.RedGreenBlueAlpha);
        if (image.Width <= 0 || image.Height <= 0)
            return null;

        if (image.Data.Length != (long)image.Width * image.Height * 4)
        {
            warnings.Add($"Texture {texturePath} decoded to an unexpected size, skipped");
            return null;
        }

        return new AssimpTextureImage(image.Width, image.Height, image.Data);
    }

    private AssimpTextureImage? FromTexels(Assimp.EmbeddedTexture embedded)
    {
        var texels = embedded.NonCompressedData;
        var byteCount = (long)embedded.Width * embedded.Height * 4;
        if (!TryReserveTextureBytes(byteCount))
        {
            warnings.Add("Embedded texture exceeds the texture memory budget, skipped");
            return null;
        }

        var rgba = new byte[embedded.Width * embedded.Height * 4];
        var count = Math.Min(texels.Length, embedded.Width * embedded.Height);
        for (var index = 0; index < count; index++)
        {
            var texel = texels[index];
            rgba[index * 4] = texel.R;
            rgba[index * 4 + 1] = texel.G;
            rgba[index * 4 + 2] = texel.B;
            rgba[index * 4 + 3] = texel.A;
        }

        return new AssimpTextureImage(embedded.Width, embedded.Height, rgba);
    }

    private static bool TryGetColor(AssimpMaterial material, string key, out Vector4 color)
    {
        color = Vector4.One;
        if (!material.HasNonTextureProperty(key))
            return false;

        var values = material.GetNonTextureProperty(key).GetFloatArrayValue();
        if (values == null || values.Length < 3)
            return false;

        color = new Vector4(Sanitize(values[0]),
                            Sanitize(values[1]),
                            Sanitize(values[2]),
                            values.Length > 3 ? Sanitize(values[3], 1) : 1);
        return true;
    }

    private static Vector4 SanitizeColor(Vector4 color)
    {
        return new Vector4(Sanitize(color.X), Sanitize(color.Y), Sanitize(color.Z), Sanitize(color.W, 1));
    }

    private static float Sanitize(float value, float fallback = 0) => float.IsFinite(value) ? value : fallback;

    private static bool TryGetFloat(AssimpMaterial material, string key, out float value)
    {
        value = 0;
        if (!material.HasNonTextureProperty(key))
            return false;

        value = material.GetNonTextureProperty(key).GetFloatValue();
        return float.IsFinite(value);
    }

    /// <summary>Reserves budget before allocating, so a file with many large textures can't exhaust memory.</summary>
    private bool TryReserveTextureBytes(long byteCount)
    {
        if (byteCount > MaxDecodedTextureBytes - _decodedBytes)
            return false;

        _decodedBytes += byteCount;
        return true;
    }

    private const int MaxTextureSize = 16384;
    private const long MaxTextureFileBytes = 512L * 1024 * 1024;

    /** Decoded pixels retained for one import. Maps stay on the CPU until the render thread uploads them. */
    private const long MaxDecodedTextureBytes = 1L * 1024 * 1024 * 1024;

    private readonly Dictionary<string, AssimpTextureImage?> _imagesByKey = new(StringComparer.Ordinal);
    private long _decodedBytes;
}
