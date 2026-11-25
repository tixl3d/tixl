#nullable enable
using T3.Core.DataTypes;

namespace T3.Core.Rendering;

/// <summary>
/// The buffers and camera rendered together with a color image, carried by <see cref="Texture2D.Bundle"/> of that image.
/// Post effects take what they need from it unless the buffer is connected explicitly, so a rendered scene can be wired
/// with a single connection. Owned and updated by the render op; consumers only read it.
/// </summary>
public sealed class RenderBundle
{
    public Texture2D? Depth;
    public Texture2D? Normal;
    public Texture2D? Velocity;

    /// <summary>Unlit surface color (rgb) and occlusion (a) of PBR draws.</summary>
    public Texture2D? BaseColor;

    /// <summary>Roughness, metalness, specular (rgb) and relight coverage (a) of PBR draws.</summary>
    public Texture2D? Material;

    /// <summary>The camera that drew the image: the innermost camera inside the render op (see <see cref="RenderPassState.RecordCamera"/>).</summary>
    public ICameraPropertiesProvider? Camera;

    /// <summary>Short suffix for captions, e.g. "+DNVM" for depth, normal, velocity and material; empty without buffers.</summary>
    public string Label => Labels[(Depth != null ? 1 : 0) | (Normal != null ? 2 : 0) | (Velocity != null ? 4 : 0)
                                  | (Material != null ? 8 : 0)];

    private static readonly string[] Labels =
        [
            "", " +D", " +N", " +DN", " +V", " +DV", " +NV", " +DNV",
            " +M", " +DM", " +NM", " +DNM", " +VM", " +DVM", " +NVM", " +DNVM",
        ];
}
