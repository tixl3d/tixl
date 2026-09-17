#nullable enable
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace T3.Editor.App.DebugProtocol;

/// <summary>
/// Captures the editor window's client area - the complete UI, unlike output-texture screenshots.
/// </summary>
internal static class WindowCapture
{
    /// <param name="cropInClientPixels">Optional region, e.g. a single ImGui window.</param>
    internal static bool TrySaveClientArea(IntPtr windowHandle, string path, Rectangle? cropInClientPixels, out string error)
    {
        error = string.Empty;
        if (!GetClientRect(windowHandle, out var clientRect) || clientRect.Right <= 0 || clientRect.Bottom <= 0)
        {
            error = "The editor window has no client area (is it minimized?)";
            return false;
        }

        try
        {
            using var bitmap = new Bitmap(clientRect.Right, clientRect.Bottom, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                var deviceContext = graphics.GetHdc();
                try
                {
                    // Full-content rendering is required to get the swap chain's pixels instead of a black frame
                    if (!PrintWindow(windowHandle, deviceContext, ClientOnly | RenderFullContent))
                    {
                        error = "PrintWindow failed";
                        return false;
                    }
                }
                finally
                {
                    graphics.ReleaseHdc(deviceContext);
                }
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var format = path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? ImageFormat.Jpeg : ImageFormat.Png;
            if (cropInClientPixels is not { } crop)
            {
                bitmap.Save(path, format);
                return true;
            }

            crop.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            if (crop.Width <= 0 || crop.Height <= 0)
            {
                error = "The requested region is outside the editor window";
                return false;
            }

            using var cropped = bitmap.Clone(crop, bitmap.PixelFormat);
            cropped.Save(path, format);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr windowHandle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr windowHandle, IntPtr deviceContext, uint flags);

    private const uint ClientOnly = 0x1;
    private const uint RenderFullContent = 0x2;
}
