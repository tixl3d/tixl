#nullable enable
using T3.Core.Resource;
using T3.Graphics;

namespace T3.Editor.Gui.UiHelpers.Enums;

/// <summary>
/// Texture formats keep their DXGI names, which projects store. This lists the usual picks first with a readable
/// label and disables what the graphics backend can't create.
/// </summary>
internal sealed class FormatOptionHints : IEnumOptionHints
{
    public Type EnumType => typeof(Format);
    public string ImportantGroupTitle => "Common";

    public EnumOptionHint GetHint(string valueName, object value)
    {
        var format = (Format)value;
        var backend = ResourceManager.Device?.Backend;

        if (backend != null && format != Format.Unknown && !IsUsable(backend, format))
            return new EnumOptionHint(OptionRelevance.Unavailable, Tooltip: $"Not supported by {backend.BackendDescription}.");

        if (_importantFormats.TryGetValue(format, out var important))
            return new EnumOptionHint(OptionRelevance.Important, important.Label, important.Tooltip);

        if (format == Format.Unknown || backend == null)
            return default;

        return backend.Supports(format, TextureUsage.RenderTarget)
                   ? default
                   : new EnumOptionHint(OptionRelevance.Normal, Tooltip: "Only usable as a depth buffer.");
    }

    /** Ops pick a format for an image they render into and then sample, or for a depth buffer they sample. */
    private static bool IsUsable(IGraphicsBackend backend, Format format)
    {
        return backend.Supports(format, TextureUsage.Sampled | TextureUsage.RenderTarget)
               || backend.Supports(format, TextureUsage.Sampled | TextureUsage.DepthStencil);
    }

    private static readonly Dictionary<Format, (string Label, string Tooltip)> _importantFormats
        = new()
              {
                  [Format.R32G32B32A32_Float] = ("RGBA 32-bit float", "Full float precision for data like positions or simulation state. Twice the memory of 16-bit float."),
                  [Format.R16G16B16A16_Float] = ("RGBA 16-bit float", "HDR color with alpha. The usual choice for intermediate images and anything brighter than 1."),
                  [Format.R11G11B10_Float] = ("RGB 11/11/10-bit float", "HDR color without alpha at half the memory of 16-bit float."),
                  [Format.R8G8B8A8_UNorm] = ("RGBA 8-bit", "Standard color with 8 bits per channel. Smallest and fastest, but smooth gradients band and values are clamped to 0..1."),
                  [Format.R32_Float] = ("R 32-bit float", "A single channel with full precision, for depth or data."),
                  [Format.R16_Float] = ("R 16-bit float", "A single channel, for masks or height maps."),
              };
}
