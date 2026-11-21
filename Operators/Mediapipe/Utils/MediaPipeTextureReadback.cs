namespace Lib.io.video.mediapipe;

/// <summary>
/// The one guard every operator's texture readback goes through.
/// <para>
/// The downsample that feeds the inference buffer walks the mapped staging texture four bytes at a time
/// and takes the first three as R, G and B. That is only valid for an 8-bit four-channel texture: on a
/// half-float or 16-bit surface the values are garbage, and on anything narrower the walk reads past the
/// end of the mapped region - a native access violation from several threads at once, which no managed
/// handler can catch. Cameras, video inputs and render targets in TiXL all deliver BGRA/RGBA8, so refusing
/// the rest with one clear message is both safe and honest; silently producing nonsense landmarks is not.
/// </para>
/// </summary>
internal static class MediaPipeTextureReadback
{
    /// <summary>True for the 8-bit four-channel formats the inference path can interpret.</summary>
    public static bool IsSupportedColorFormat(T3.Graphics.Format format)
    {
        return format is T3.Graphics.Format.R8G8B8A8_Typeless
                       or T3.Graphics.Format.R8G8B8A8_UNorm
                       or T3.Graphics.Format.R8G8B8A8_UNorm_SRgb
                       or T3.Graphics.Format.B8G8R8A8_Typeless
                       or T3.Graphics.Format.B8G8R8A8_UNorm
                       or T3.Graphics.Format.B8G8R8A8_UNorm_SRgb
                       or T3.Graphics.Format.B8G8R8X8_Typeless
                       or T3.Graphics.Format.B8G8R8X8_UNorm
                       or T3.Graphics.Format.B8G8R8X8_UNorm_SRgb;
    }

    /// <summary>
    /// Convenience wrapper for the operators: returns false (and reports once per distinct format) when
    /// the input cannot be interpreted. Repeating the message every frame would drown the log for a
    /// condition that only changes when the upstream texture changes.
    /// </summary>
    public static bool CheckFormat(T3.Graphics.Format format, ref T3.Graphics.Format? warnedFormat,
                                   Action<string> warn, string label)
    {
        if (IsSupportedColorFormat(format))
            return true;

        if (warnedFormat != format)
        {
            warnedFormat = format;
            warn($"[{label}] Input format {format} is not an 8-bit RGBA/BGRA texture, which these operators "
                 + "need - the image is passed through without inference.");
        }

        return false;
    }

    /// <summary>
    /// Copies the texture into a staging copy and maps it for reading. Returns false instead of throwing:
    /// a texture that is still bound for writing, has mismatched dimensions or comes from another device
    /// makes <c>CopyResource</c> fail, and that must degrade to "no result this frame" rather than escape
    /// into the evaluation loop. The caller must unmap when this returns true.
    /// </summary>
    public static bool TryMap(T3.Graphics.Compat.DeviceContext context, T3.Graphics.Compat.Texture2D source,
                              T3.Graphics.Compat.Texture2D staging, out T3.Graphics.Compat.DataBox dataBox,
                              out string error)
    {
        try
        {
            context.CopyResource(source, staging);
            dataBox = context.MapSubresource(staging, 0, T3.Graphics.Compat.MapMode.Read, T3.Graphics.Compat.MapFlags.None);
            error = string.Empty;
            return true;
        }
        catch (Exception e)
        {
            dataBox = default;
            error = e.Message;
            return false;
        }
    }

    /// <summary>
    /// True when the driver's row pitch can hold a full row of RGBA8 pixels. A smaller pitch would make
    /// the row walk overrun the mapping.
    /// </summary>
    public static bool RowPitchFits(T3.Graphics.Compat.DataBox dataBox, int width)
        => dataBox.DataPointer != IntPtr.Zero && dataBox.RowPitch >= width * 4;
}
