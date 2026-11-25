#nullable enable
using System.Reflection;
using ImGuiNET;
using T3.Editor.Gui.Styling;
using Color = T3.Core.DataTypes.Vector.Color;

namespace T3.Editor.Gui.TableView;

/// <summary>
/// How tables of numbers show a value: zeros faded, non-finite values flagged, and values that changed since the
/// previous frame tinted orange when they grew and blue when they shrank, stronger for larger relative changes.
/// </summary>
internal static class TableValueStyle
{
    /// <param name="previous">The value in the previous frame; pass <paramref name="value"/> if there is none.</param>
    public static Color GetColor(float value, float previous)
    {
        if (!float.IsFinite(value))
            return UiColors.StatusAttention;

        if (HasChanged(value, previous))
        {
            var relativeChange = MathF.Abs(value - previous) / MathF.Max(MathF.Abs(previous), MinMagnitudeForRelativeChange);
            var strength = Math.Clamp(relativeChange / RelativeChangeForFullTint, MinTintStrength, 1f);
            return Color.Mix(UiColors.Text, value > previous ? UiColors.StatusAnimated : UiColors.StatusAutomated, strength);
        }

        return value == 0 ? UiColors.Text.Fade(0.1f) : UiColors.Text;
    }

    /** Shows the previous value and the change in percent. Allocates, so only while hovered. */
    public static void DrawTooltipIfHovered(string label, float value, float previous)
    {
        if (!ImGuiNET.ImGui.IsItemHovered())
            return;

        var message = !HasChanged(value, previous)
                          ? $"{label}: {value:G7}"
                          : previous != 0
                              ? $"{label}: {value:G7}\nprevious: {previous:G7} ({(value - previous) / MathF.Abs(previous):+0.##%;-0.##%})"
                              : $"{label}: {value:G7}\nprevious: {previous:G7}";
        CustomComponents.TooltipForLastItem(message, useHoverDelay: false);
    }

    /// <summary>One label per float of <paramref name="type"/>'s fields, e.g. "Position.x", ".y", ".z", "F1" for a point.</summary>
    public static string[] GetFloatColumnLabels(Type type) => GetFloatColumnLayout(type).Labels;

    /// <summary>Per float column of <paramref name="type"/>: whether it is the first component of a color field.</summary>
    public static bool[] GetColorColumnStarts(Type type) => GetFloatColumnLayout(type).StartsColor;

    public static bool IsColorField(FieldInfo field)
    {
        return field.FieldType == typeof(Vector4) && field.Name.Contains("Color", StringComparison.OrdinalIgnoreCase);
    }

    /** A narrow strip at the left edge of a cell previewing a color value; HDR components are clamped. */
    public static void DrawColorSwatch(Vector2 cellTopLeft, float height, Vector4 color)
    {
        var width = MathF.Max(1, MathF.Round(ColorSwatchWidth * T3Ui.UiScaleFactor));
        var min = new Vector2(MathF.Floor(cellTopLeft.X), MathF.Floor(cellTopLeft.Y));
        var max = min + new Vector2(width, MathF.Round(height));
        ImGui.GetWindowDrawList().AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(Vector4.Clamp(color, Vector4.Zero, Vector4.One)));
    }

    private static FloatColumnLayout GetFloatColumnLayout(Type type)
    {
        if (_layoutsForTypes.TryGetValue(type, out var layout))
            return layout;

        var labels = new List<string>();
        var startsColor = new List<bool>();
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (TableList.TypeComponents.TryGetValue(field.FieldType, out var components))
            {
                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    labels.Add(componentIndex == 0 ? field.Name + components[componentIndex] : components[componentIndex]);
                    startsColor.Add(componentIndex == 0 && IsColorField(field));
                }
            }
            else
            {
                labels.Add(field.Name);
                startsColor.Add(false);
            }
        }

        layout = new FloatColumnLayout(labels.ToArray(), startsColor.ToArray());
        _layoutsForTypes[type] = layout;
        return layout;
    }

    private static bool HasChanged(float value, float previous)
    {
        return BitConverter.SingleToInt32Bits(value) != BitConverter.SingleToInt32Bits(previous);
    }

    /** Keeps the relative change of values near zero finite. */
    private const float MinMagnitudeForRelativeChange = 0.001f;
    private const float RelativeChangeForFullTint = 0.1f;
    private const float MinTintStrength = 0.35f;
    private const float ColorSwatchWidth = 5;

    private sealed record FloatColumnLayout(string[] Labels, bool[] StartsColor);

    private static readonly Dictionary<Type, FloatColumnLayout> _layoutsForTypes = new();
}
