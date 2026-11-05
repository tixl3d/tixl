#nullable enable
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The silhouettes a person on the Board can be drawn with: every PNG below the editor's props folder, scanned once
/// on first use. A file named <c>man-pointing-200cm.png</c> reads as "Man Pointing", with the whole image 2 m high.
/// The images are white on transparent, so that a tint sets their color.
/// </summary>
internal static class PersonImages
{
    internal sealed class Image(string relativePath, string label, float heightInMeters, float aspect)
    {
        public readonly string RelativePath = relativePath;
        public readonly string Label = label;
        public readonly float HeightInMeters = heightInMeters;

        /// <summary>Width over height, read from the file so bounds are known before the texture has loaded.</summary>
        public readonly float Aspect = aspect;

        public Resource<Texture2D> Texture => _texture ??= ResourceManager.CreateTextureResource(Path.Combine(Folder, RelativePath), null);

        private Resource<Texture2D>? _texture;
    }

    /// <summary>The image a person is drawn with: the one it stores, or the first one if that is gone or it stores none.</summary>
    public static Image? For(Prop prop)
    {
        var images = All;
        for (var index = 0; index < images.Count; index++)
        {
            if (images[index].RelativePath == prop.ImagePath)
                return images[index];
        }

        return images.Count > 0 ? images[0] : null;
    }

    /// <summary>The "Add Person" submenu, one entry per image.</summary>
    /// <param name="boardPosition">Where the person lands, in board metres; see <see cref="SetupActions.AddProp"/>.</param>
    public static void DrawAddMenu(int id, SetupEntitySelection selection, Vector2? boardPosition = null)
    {
        var images = All;
        if (!CustomComponents.DrawSubMenu(id, "Add Person", isEnabled: images.Count > 0))
            return;

        for (var index = 0; index < images.Count; index++)
        {
            if (CustomComponents.DrawMenuItem(index, images[index].Label, reserveIconColumn: false))
                SetupActions.AddProp(selection, images[index], boardPosition);
        }

        ImGui.EndMenu();
    }

    private static List<Image> Scan()
    {
        var images = new List<Image>();
        if (!Directory.Exists(Folder))
            return images;

        foreach (var path in Directory.EnumerateFiles(Folder, "*.png", SearchOption.AllDirectories))
        {
            if (!TryReadPngAspect(path, out var aspect))
            {
                Log.Warning($"Skipping person image '{path}': it isn't a readable PNG.");
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(path);
            var height = DefaultHeightInMeters;
            var lastDash = name.LastIndexOf('-');
            if (lastDash > 0 && name.EndsWith("cm", StringComparison.Ordinal) && int.TryParse(name[(lastDash + 1)..^2], out var centimeters) && centimeters > 0)
            {
                height = centimeters / 100f;
                name = name[..lastDash];
            }

            var label = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('-', ' '));
            images.Add(new Image(Path.GetRelativePath(Folder, path).Replace('\\', '/'), label, height, aspect));
        }

        images.Sort(static (a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        return images;
    }

    /** A PNG's size sits at a fixed place in its header, in the IHDR chunk that has to come first. */
    private static bool TryReadPngAspect(string path, out float aspect)
    {
        aspect = 0;
        Span<byte> header = stackalloc byte[24];
        try
        {
            using var stream = File.OpenRead(path);
            stream.ReadExactly(header);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (!header[..8].SequenceEqual(_pngSignature))
            return false;

        var width = BinaryPrimitives.ReadInt32BigEndian(header[16..]);
        var height = BinaryPrimitives.ReadInt32BigEndian(header[20..]);
        if (width <= 0 || height <= 0)
            return false;

        aspect = width / (float)height;
        return true;
    }

    private static readonly byte[] _pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private const float DefaultHeightInMeters = 1.7f;
    private static string Folder => Path.Combine(SharedResources.EditorResourcesDirectory, "images/props");
    private static List<Image> All => _all ??= Scan();
    private static List<Image>? _all;
}
