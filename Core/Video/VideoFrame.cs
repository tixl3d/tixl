using SharpDX.DXGI;
using System;
using System.Runtime.InteropServices;

namespace T3.Core.Video;

public struct VideoFrame : IDisposable
{
    public int Width;
    public int Height;
    public int StrideInBytes;
    public IntPtr Data;
    public Format Format;
    public bool IsOwned; //tells if data is owned

    public int TotalSize => Height * StrideInBytes;

    public static VideoFrame Reference(int width, int height, int stride, IntPtr data, Format format)
    {
        VideoFrame result = new VideoFrame();
        result.Width = width;
        result.Height = height;
        result.StrideInBytes = stride;
        result.Data = data;
        result.Format = format;
        result.IsOwned = false;
        return result;
    }

    public static VideoFrame Owned(int width, int height, int stride, Format format)
    {
        VideoFrame result = new VideoFrame();
        result.Width = width;
        result.Height = height;
        result.StrideInBytes = stride;
        result.Data = Marshal.AllocHGlobal(stride * height);
        result.Format = format;
        result.IsOwned = true;
        return result;
    }

    public void Dispose()
    {
        if (this.Data != IntPtr.Zero && this.IsOwned)
        {
            Marshal.FreeHGlobal(this.Data);
            this.Data = IntPtr.Zero;
        }
    }
}