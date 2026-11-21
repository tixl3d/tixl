#nullable enable
using System.Numerics;
using T3.Core.DataTypes.Vector;

namespace T3.Core.Output;

/// <summary>
/// The scan field as a calibration target. A laser output has no pixel canvas, so its
/// <see cref="CalibrationPoint.OutputPixel"/> values are positions in a square field image: the
/// whole ILDA field (-32767..32767 on both axes) maps onto <see cref="Resolution"/>², x right and
/// y up - i.e. pixel y grows downwards like on every other canvas. The scanner camera is solved
/// with a square lens aspect, which is exactly what [LaserCamera] uses for a laser output (the
/// scan field is square).
/// </summary>
public static class LaserScannerField
{
    /// <summary>Field image resolution the solver works in. Only the ratio matters, and it is square.</summary>
    public const int Resolution = 4096;

    public static Int2 PixelResolution => new(Resolution, Resolution);

    /// <summary>Field position in -1..1 (ILDA field normalised, y up) to a field-image pixel.</summary>
    public static Vector2 PixelFromField(float x, float y)
    {
        return new Vector2((x * 0.5f + 0.5f) * Resolution,
                           (0.5f - y * 0.5f) * Resolution);
    }

    /// <summary>Field-image pixel back to -1..1 with y up.</summary>
    public static Vector2 FieldFromPixel(Vector2 pixel)
    {
        return new Vector2(pixel.X / Resolution * 2f - 1f,
                           1f - pixel.Y / Resolution * 2f);
    }
}
