using ImGuiNET;
using T3.Graphics.Compat;
using T3.Graphics;
using System.Numerics;
using StbImageSharp;
using T3.Editor.Gui;
using T3.Editor.Gui.Styling;
using Device = T3.Graphics.Compat.Device;

namespace T3.Editor.UiContentDrawing;

internal static class FontAtlasGenerator
{
    private static string GetIconPathForScale(float scale, out float iconScaleFactor)
    {
        iconScaleFactor = float.NaN;
        var bestDist = float.PositiveInfinity;

        foreach (var x in Icons.IconFilePathForResolutions.Keys)
        {
            var dist = MathF.Abs(x - scale); // This should probably be logarithmic

            if (!(dist < bestDist))
                continue;

            bestDist = dist;
            iconScaleFactor = x;
        }

        return Icons.IconFilePathForResolutions[iconScaleFactor];
    }

    internal static unsafe void CreateFontAtlasWithIcons(Device device,
                                                         IntPtr imguiContext,
                                                         out ShaderResourceView fontTextureView,
                                                         out SamplerState imGuiSampler)
    {
        imGuiSampler = null;
        fontTextureView = null;
        var scaleFactor = T3Ui.UiScaleFactor;

        var previousContext = ImGui.GetCurrentContext();
        ImGui.SetCurrentContext(imguiContext);
        ImGuiIOPtr io = ImGui.GetIO();
        
        var iconFilePath = GetIconPathForScale(scaleFactor, out var iconScaleFactor);

        Icons.IconFont = io.Fonts.AddFontDefault();
        Icons.FontSize = 15 * iconScaleFactor;
        
        var glyphIds
            = Icons.CustomIcons
                   .Select(iconSource
                               => io.Fonts.AddCustomRectFontGlyph(Icons.IconFont,
                                                                  iconSource.Char,
                                                                  (int)(iconSource.SourceArea.GetWidth() * iconScaleFactor),
                                                                  (int)(iconSource.SourceArea.GetHeight() * iconScaleFactor),
                                                                  iconSource.SourceArea.GetWidth() * iconScaleFactor)).ToArray();

        io.Fonts.Build();

        // Get pointer to texture data, must happen after font build
        io.Fonts.GetTexDataAsRGBA32(out IntPtr atlasPixels, out var atlasWidth, out var atlasHeight, out _);

        // Decoded as RGBA - byte for byte what ImGui's RGBA32 atlas holds, so icons copy straight across.
        ImageResult icons;
        using (var stream = System.IO.File.OpenRead(iconFilePath))
        {
            icons = ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        }

        // Copy the data into the font atlas texture
        for (int i = 0; i < glyphIds.Length; i++)
        {
            var glyphId = glyphIds[i];
            var icon = Icons.CustomIcons[i];

            int sx = (int)(icon.SourceArea.GetWidth() * iconScaleFactor);
            int sy = (int)(icon.SourceArea.GetHeight() * iconScaleFactor);
            int px = (int)(icon.SourceArea.Min.X * iconScaleFactor);
            int py = (int)(icon.SourceArea.Min.Y * iconScaleFactor);

            uint[] iconContent = new uint[sx * sy];
            CopyRegion(icons, px, py, sx, sy, iconContent);

            var rect = io.Fonts.GetCustomRectByIndex(glyphId);
            for (int y = 0, s = 0; y < rect.Height; y++)
            {
                uint* p = (uint*)atlasPixels + (rect.Y + y) * atlasWidth + rect.X;
                for (int x = rect.Width; x > 0; x--)
                {
                    *p++ = iconContent[s];
                    s++;
                }
            }
        }

        // Upload texture
        const IntPtr fontAtlasId = (IntPtr)1;
        io.Fonts.SetTexID(fontAtlasId);
        var box = new DataBox(atlasPixels, atlasWidth * 4, 0);

        try
        {
            // Upload texture to graphics system
            var textureDesc = new Texture2DDescription()
                                  {
                                      Width = atlasWidth,
                                      Height = atlasHeight,
                                      MipLevels = 1,
                                      ArraySize = 1,
                                      Format = Format.R8G8B8A8_UNorm,
                                      SampleDescription = new SampleDescription() { Count = 1, Quality = 0 },
                                      Usage = ResourceUsage.Default,
                                      BindFlags = BindFlags.ShaderResource,
                                      CpuAccessFlags = CpuAccessFlags.None
                                  };

            Texture2D texture = new Texture2D(device, textureDesc, [box]);
            texture.DebugName = "FImgui Font Atlas";

            fontTextureView = new ShaderResourceView(device, texture);
            texture.Dispose();

            // Store our identifier
            io.Fonts.TexID = (IntPtr)fontTextureView.ImGuiTextureId;

            var samplerDesc = new SamplerStateDescription()
                                  {
                                      Filter = Filter.MinMagMipLinear,
                                      AddressU = TextureAddressMode.Wrap,
                                      AddressV = TextureAddressMode.Wrap,
                                      AddressW = TextureAddressMode.Wrap,
                                      MipLodBias = 0.0f,
                                      ComparisonFunction = Comparison.Always,
                                      MinimumLod = 0.0f,
                                      // Every image ImGui draws goes through this sampler, so it must reach the mip chain:
                                      // clamped to level 0, a photo shrunk onto a card is a moiré of its finest pixels.
                                      MaximumLod = float.MaxValue,
                                  };
            imGuiSampler = new SamplerState(device, samplerDesc);
        }
        catch (Exception e)
        {
            Log.Error("Failed to create fonts texture: " + e.Message);
        }

        if (previousContext != IntPtr.Zero)
            ImGui.SetCurrentContext(previousContext);
    }

    /// <summary>Copies one icon out of the decoded atlas, row by row, as packed RGBA pixels.</summary>
    private static void CopyRegion(ImageResult image, int x, int y, int width, int height, uint[] target)
    {
        const int bytesPerPixel = 4;
        for (var row = 0; row < height; row++)
        {
            var source = ((y + row) * image.Width + x) * bytesPerPixel;
            System.Buffer.BlockCopy(image.Data, source, target, row * width * bytesPerPixel, width * bytesPerPixel);
        }
    }
}
