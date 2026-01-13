namespace Mediapipe.Extension;

public static class ImageFormatExtension
{
    /// <returns>
    ///     The number of channels for a <paramref name="format" />.
    ///     If channels don't make sense in the <paramref name="format" />, returns <c>0</c>.
    /// </returns>
    /// <remarks>
    ///     Unlike the original implementation, this API won't signal SIGABRT.
    /// </remarks>
    public static int NumberOfChannels(this ImageFormat.Types.Format format)
    {
        return format switch
        {
            ImageFormat.Types.Format.Srgb or ImageFormat.Types.Format.Srgb48 => 3,
            ImageFormat.Types.Format.Srgba or ImageFormat.Types.Format.Srgba64 or ImageFormat.Types.Format.Sbgra => 4,
            ImageFormat.Types.Format.Gray8 or ImageFormat.Types.Format.Gray16 => 1,
            ImageFormat.Types.Format.Vec32F1 => 1,
            ImageFormat.Types.Format.Vec32F2 => 2,
            ImageFormat.Types.Format.Vec32F4 => 4,
            ImageFormat.Types.Format.Lab8 => 3,
            _ => 0
        };
    }

    /// <returns>
    ///     The depth of each channel in bytes for a <paramref name="format" />.
    ///     If channels don't make sense in the <paramref name="format" />, returns <c>0</c>.
    /// </returns>
    /// <remarks>
    ///     Unlike the original implementation, this API won't signal SIGABRT.
    /// </remarks>
    public static int ByteDepth(this ImageFormat.Types.Format format)
    {
        return format switch
        {
            ImageFormat.Types.Format.Srgb or ImageFormat.Types.Format.Srgba or ImageFormat.Types.Format.Sbgra => 1,
            ImageFormat.Types.Format.Srgb48 or ImageFormat.Types.Format.Srgba64 => 2,
            ImageFormat.Types.Format.Gray8 => 1,
            ImageFormat.Types.Format.Gray16 => 2,
            ImageFormat.Types.Format.Vec32F1 or ImageFormat.Types.Format.Vec32F2
                or ImageFormat.Types.Format.Vec32F4 => 4,
            ImageFormat.Types.Format.Lab8 => 1,
            _ => 0
        };
    }

    /// <returns>
    ///     The channel size for a <paramref name="format" />.
    ///     If channels don't make sense in the <paramref name="format" />, returns <c>0</c>.
    /// </returns>
    /// <remarks>
    ///     Unlike the original implementation, this API won't signal SIGABRT.
    /// </remarks>
    public static int ChannelSize(this ImageFormat.Types.Format format)
    {
        return format switch
        {
            ImageFormat.Types.Format.Srgb or ImageFormat.Types.Format.Srgba
                or ImageFormat.Types.Format.Sbgra => sizeof(byte),
            ImageFormat.Types.Format.Srgb48 or ImageFormat.Types.Format.Srgba64 => sizeof(ushort),
            ImageFormat.Types.Format.Gray8 => sizeof(byte),
            ImageFormat.Types.Format.Gray16 => sizeof(ushort),
            ImageFormat.Types.Format.Vec32F1 or ImageFormat.Types.Format.Vec32F2 or ImageFormat.Types.Format.Vec32F4 =>
                sizeof(float), // sizeof float may be wrong since it's platform-dependent, but we assume that it's constant across all supported platforms.
            ImageFormat.Types.Format.Lab8 => sizeof(byte),
            _ => 0
        };
    }
}