using T3.Core.DataTypes.Vector;

namespace T3.Editor.Gui.Styling;

/// <summary>
/// Provides <see cref="ColorVariation"/>s of <see cref="Color"/>s that are used to 
/// represent different UI-elements (e.g. <see cref="ConnectionLine"/>  and Operators) for a <see cref="Type"/>.
/// </summary>
internal static class ColorVariations
{
    public static ColorVariation OperatorBackground = new(brightnessFactor: 0.5f, saturationFactor: 0.7f, opacityFactor: 1);
    public static ColorVariation OperatorLabel = new(brightnessFactor: 1.3f, saturationFactor: 0.4f, opacityFactor: 1);
    public static ColorVariation OperatorBackgroundIdle = new(brightnessFactor: 0.71f, saturationFactor: 1f, opacityFactor: 0.3f);
    public static ColorVariation OperatorBackgroundHover = new(brightnessFactor: 0.6f, saturationFactor: 1, opacityFactor: 1);
    public static ColorVariation OutputNodes = new(brightnessFactor: 0.35f, saturationFactor: 0.7f, opacityFactor: 0.4f);
    public static ColorVariation Highlight = new(brightnessFactor: 1.2f, saturationFactor: 1.2f, opacityFactor: 1);
    public static ColorVariation ConnectionLines = new(brightnessFactor: 1f, saturationFactor: 1, opacityFactor: 0.8f);
    public static ColorVariation OperatorOutline = new(brightnessFactor: 0.1f, saturationFactor: 0.7f, opacityFactor: 0.5f);
    public static ColorVariation SectionBackground = new(brightnessFactor: 0.12f, saturationFactor: 1f, opacityFactor: 0.7f);
    public static ColorVariation SectionOutline = new(brightnessFactor: 1f, saturationFactor: 0.0f, opacityFactor: 0.0f);
}