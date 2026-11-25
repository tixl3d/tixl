#nullable enable
using T3.Core.DataTypes.Vector;

namespace T3.Editor.Gui.Styling;

/// <summary>
/// Colors for the components of vector values (X, Y, Z, W), derived from the themeable
/// <see cref="UiColors.AxisColorX"/>, <see cref="UiColors.AxisColorY"/> and <see cref="UiColors.AxisColorZ"/>.
/// Resolved on every call so theme changes apply immediately.
/// </summary>
internal static class VectorComponentColors
{
    /// <summary>Curve color for single-component values and for the W component.</summary>
    internal static Color GrayCurveColor => UiColors.ForegroundFull.Fade(0.3f);

    /// <summary>Translucent line color for curves and plots. Indices beyond W wrap around to X.</summary>
    internal static Color GetCurveColor(int componentIndex)
    {
        return (componentIndex % 4) switch
                   {
                       0 => UiColors.AxisColorX.Fade(0.4f),
                       1 => UiColors.AxisColorY.Fade(0.4f),
                       // Blue reads darker than red and green on dark backgrounds.
                       2 => UiColors.AxisColorZ.Fade(0.6f),
                       _ => GrayCurveColor,
                   };
    }

    /// <summary>Opaque color lightened towards the foreground, so it stays legible as text.</summary>
    internal static Color GetTextColor(int componentIndex)
    {
        return (componentIndex % 4) switch
                   {
                       0 => Lighten(UiColors.AxisColorX),
                       1 => Lighten(UiColors.AxisColorY),
                       2 => Lighten(UiColors.AxisColorZ),
                       _ => UiColors.Gray,
                   };
    }

    private static Color Lighten(Color axisColor) => Color.Mix(axisColor, UiColors.ForegroundFull, 0.4f);
}
