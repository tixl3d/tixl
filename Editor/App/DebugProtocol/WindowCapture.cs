#nullable enable
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using StbImageWriteSharp;

namespace T3.Editor.App.DebugProtocol;

/// <summary>
/// Captures the editor window's client area - the complete UI, unlike output-texture screenshots. Windows only:
/// it asks the window to paint itself into a bitmap.
/// </summary>
internal static class WindowCapture
{
    /// <param name="cropInClientPixels">Optional region, e.g. a single ImGui window.</param>
    internal static bool TrySaveClientArea(IntPtr windowHandle, string path, Rectangle? cropInClientPixels, out string error)
    {
        error = string.Empty;
        if (!OperatingSystem.IsWindows())
        {
            error = "Window capture is only available on Windows";
            return false;
        }

        if (!GetClientRect(windowHandle, out var clientRect) || clientRect.Right <= 0 || clientRect.Bottom <= 0)
        {
            error = "The editor window has no client area (is it minimized?)";
            return false;
        }

        var area = new Rectangle(0, 0, clientRect.Right, clientRect.Bottom);
        if (cropInClientPixels is { } crop)
        {
            crop.Intersect(area);
            if (crop.Width <= 0 || crop.Height <= 0)
            {
                error = "The requested region is outside the editor window";
                return false;
            }

            area = crop;
        }

        try
        {
            if (!TryCaptureBgra(windowHandle, clientRect.Right, clientRect.Bottom, out var bgra, out error))
                return false;

            var rgba = CropToRgba(bgra, clientRect.Right, area);

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using var stream = File.Create(path);
            var writer = new ImageWriter();
            if (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                writer.WriteJpg(rgba, area.Width, area.Height, ColorComponents.RedGreenBlueAlpha, stream, JpegQuality);
            }
            else
            {
                writer.WritePng(rgba, area.Width, area.Height, ColorComponents.RedGreenBlueAlpha, stream);
            }

            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    /// <summary>The whole client area as top-down BGRA rows, painted into a DIB by the window itself.</summary>
    private static bool TryCaptureBgra(IntPtr windowHandle, int width, int height, out byte[] bgra, out string error)
    {
        bgra = [];
        error = string.Empty;

        var screenContext = GetDC(IntPtr.Zero);
        var memoryContext = CreateCompatibleDC(screenContext);
        var header = new BitmapInfoHeader
                         {
                             Size = Marshal.SizeOf<BitmapInfoHeader>(),
                             Width = width,
                             Height = -height, // negative: top-down rows
                             Planes = 1,
                             BitCount = 32,
                         };
        var bitmap = CreateDIBSection(screenContext, ref header, 0, out var bits, IntPtr.Zero, 0);
        var previous = SelectObject(memoryContext, bitmap);

        try
        {
            // Full-content rendering is required to get the swap chain's pixels instead of a black frame
            if (bitmap == IntPtr.Zero || !PrintWindow(windowHandle, memoryContext, ClientOnly | RenderFullContent))
            {
                error = "PrintWindow failed";
                return false;
            }

            bgra = new byte[width * height * 4];
            Marshal.Copy(bits, bgra, 0, bgra.Length);
            return true;
        }
        finally
        {
            SelectObject(memoryContext, previous);
            DeleteObject(bitmap);
            DeleteDC(memoryContext);
            ReleaseDC(IntPtr.Zero, screenContext);
        }
    }

    /// <summary>
    /// Cuts <paramref name="area"/> out of BGRA rows and swaps it to RGBA. Alpha is forced opaque: a DIB painted
    /// by GDI leaves it undefined.
    /// </summary>
    private static byte[] CropToRgba(byte[] bgra, int sourceWidth, Rectangle area)
    {
        var rgba = new byte[area.Width * area.Height * 4];
        for (var y = 0; y < area.Height; y++)
        {
            var source = ((area.Y + y) * sourceWidth + area.X) * 4;
            var target = y * area.Width * 4;
            for (var x = 0; x < area.Width; x++, source += 4, target += 4)
            {
                rgba[target] = bgra[source + 2];
                rgba[target + 1] = bgra[source + 1];
                rgba[target + 2] = bgra[source];
                rgba[target + 3] = 255;
            }
        }

        return rgba;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr windowHandle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr windowHandle, IntPtr deviceContext, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr deviceContext, ref BitmapInfoHeader header, uint usage, out IntPtr bits,
                                                  IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr gdiObject);

    private const uint ClientOnly = 0x1;
    private const uint RenderFullContent = 0x2;
    private const int JpegQuality = 90;
}
