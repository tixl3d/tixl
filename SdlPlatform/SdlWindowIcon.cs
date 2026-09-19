using System.Buffers.Binary;
using SDL;
using T3.Core.Logging;
using static SDL.SDL3;

namespace T3.SdlPlatform;

/// <summary>
/// Sets a window icon from a Windows .ico file. Reads the largest uncompressed 32-bit frame, which needs no
/// image decoder; PNG-compressed frames are skipped.
/// </summary>
public static unsafe class SdlWindowIcon
{
    public static void TrySet(SDL_Window* window, string icoPath)
    {
        try
        {
            if (!TryReadLargestBgraFrame(File.ReadAllBytes(icoPath), out var width, out var height, out var bgraTopDown))
            {
                Log.Warning($"No uncompressed 32-bit frame in {icoPath}");
                return;
            }

            var surface = SDL_CreateSurface(width, height, SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888);
            if (surface == null)
                return;

            // ARGB8888 is a packed little-endian format: its bytes in memory are B, G, R, A like the .ico's.
            var rowBytes = width * 4;
            for (var y = 0; y < height; y++)
            {
                bgraTopDown.AsSpan(y * rowBytes, rowBytes).CopyTo(new Span<byte>((byte*)surface->pixels + y * surface->pitch, rowBytes));
            }

            SDL_SetWindowIcon(window, surface);
            SDL_DestroySurface(surface);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to set window icon from {icoPath}: {e.Message}");
        }
    }

    private static bool TryReadLargestBgraFrame(byte[] ico, out int width, out int height, out byte[] bgraTopDown)
    {
        width = height = 0;
        bgraTopDown = [];

        var frameCount = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        var bestOffset = -1;
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var entry = ico.AsSpan(6 + frameIndex * 16, 16);
            var frameWidth = entry[0] == 0 ? 256 : entry[0];
            var bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            var isBitmap = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(offset)) == BitmapInfoHeaderSize;
            if (bitsPerPixel != 32 || !isBitmap || frameWidth <= width)
                continue;

            width = frameWidth;
            height = entry[1] == 0 ? 256 : entry[1];
            bestOffset = offset;
        }

        if (bestOffset < 0)
            return false;

        // The bitmap stores rows bottom-up after its header, followed by a 1-bit mask that 32-bit frames ignore.
        var pixelStart = bestOffset + BitmapInfoHeaderSize;
        var rowBytes = width * 4;
        bgraTopDown = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            ico.AsSpan(pixelStart + (height - 1 - y) * rowBytes, rowBytes).CopyTo(bgraTopDown.AsSpan(y * rowBytes));
        }

        return true;
    }

    private const int BitmapInfoHeaderSize = 40;
}
