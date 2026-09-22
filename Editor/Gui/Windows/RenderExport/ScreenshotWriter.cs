#nullable enable

using T3.Graphics.Compat;
using System.IO;
using StbImageWriteSharp;
using T3.Core.DataTypes;
using T3.Core.Resource;

namespace T3.Editor.Gui.Windows.RenderExport;

internal static class ScreenshotWriter
{
    internal enum FileFormats
    {
        Png,
        Jpg,
    }

    private static TextureBgraReadAccess? _textureBgraReadAccess;
    internal static string? LastFilename { get; private set; }

    //private static TextureBgraReadAccess? _textureBgraReadAccess;
    private static int _lastUpdateFrame = 0;

    internal static void ClearQueue()
    {
        _textureBgraReadAccess?.ClearQueue();
    }

    internal static void Update()
    {
        // Once per UI frame. Counted in UI frames rather than playback frames, which stand still while no
        // project is focused and would hold a capture of the editor's own window forever.
        var frame = ImGuiNET.ImGui.GetFrameCount();
        if (frame == _lastUpdateFrame)
            return;

        _textureBgraReadAccess?.Update();
        _lastUpdateFrame = frame;
    }

    internal static bool InitiateConvertAndReadBack2(T3.Core.DataTypes.Texture2D gpuTexture, TextureBgraReadAccess.OnReadComplete saveSampleAfterReadback)
    {
        if (_textureBgraReadAccess == null)
            _textureBgraReadAccess = new TextureBgraReadAccess();
        
        _useFormats = FileFormats.Png;
        _forceOpaque = false;
        return _textureBgraReadAccess.InitiateConvertAndReadBack(gpuTexture, saveSampleAfterReadback);        
    }

    /// <param name="opaque">Write alpha as fully opaque — for a UI back-buffer capture, whose alpha is
    /// render residue that makes the PNG see-through in viewers.</param>
    internal static bool StartSavingToFile(T3.Core.DataTypes.Texture2D gpuTexture, string filepath, FileFormats format, Action<string?>? onComplete = null, bool logErrors=true, bool opaque = false)
    {
        _textureBgraReadAccess ??= new TextureBgraReadAccess();
        _useFormats = format;
        _forceOpaque = opaque;
        
        return _textureBgraReadAccess.InitiateConvertAndReadBack(gpuTexture, request => 
                                                                             {
                                                                                 OnReadComplete(request, logErrors);
                                                                                 onComplete?.Invoke(request.Filepath);
                                                                             }, filepath);        
    }
    
    
    private static void OnReadComplete(TextureBgraReadAccess.ReadRequestItem request, bool logErrors)
    {
        var immediateContext = ResourceManager.Device.ImmediateContext;
        if (request.CpuAccessTexture.IsDisposed)
        {
            if(logErrors)
                Log.Debug("ScreenshotWriter: Texture was disposed before readback was complete");
            return;
        }
        
        var dataBox = immediateContext.MapSubresource(request.CpuAccessTexture,
                                                      0,
                                                      0,
                                                      T3.Graphics.Compat.MapMode.Read,
                                                      T3.Graphics.Compat.MapFlags.None,
                                                      out var imageStream);
        using var dataStream = imageStream;

        var width = request.CpuAccessTexture.Description.Width;
        var height = request.CpuAccessTexture.Description.Height;

        // Rows are copied out one by one: the mapped row pitch is padded and can exceed width * 4. JPEG has no
        // alpha, so it gets three bytes per pixel.
        var isPng = _useFormats == FileFormats.Png;
        var bytesPerPixel = isPng ? 4 : 3;
        var pixels = new byte[width * height * bytesPerPixel];
        var row = new byte[width * 4];

        try
        {
            for (var y = 0; y < height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(dataBox.DataPointer + y * dataBox.RowPitch, row, 0, row.Length);
                var target = y * width * bytesPerPixel;
                for (var x = 0; x < width; x++)
                {
                    var source = x * 4;
                    pixels[target++] = row[source];
                    pixels[target++] = row[source + 1];
                    pixels[target++] = row[source + 2];
                    if (isPng)
                        pixels[target++] = _forceOpaque ? (byte)255 : row[source + 3];
                }
            }

            using var file = File.Create(request.Filepath);
            var writer = new ImageWriter();
            if (isPng)
            {
                writer.WritePng(pixels, width, height, ColorComponents.RedGreenBlueAlpha, file);
            }
            else
            {
                writer.WriteJpg(pixels, width, height, ColorComponents.RedGreenBlue, file, JpegQuality);
            }

            LastFilename = request.Filepath;
        }
        catch (Exception e)
        {
            if (logErrors)
                Log.Warning("Failed to export image: " + e.Message);
        }
    }

    private const int JpegQuality = 90;

    /// <summary>
    /// Save the requested format for later use by callback.
    /// This is not ideal, but beats the alternative to moving file formats to
    /// <see cref="_textureBgraReadAccess"/> in core.
    /// </summary>
    private static FileFormats _useFormats = FileFormats.Png;
    private static bool _forceOpaque;


}