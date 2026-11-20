#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using Color = T3.Core.DataTypes.Vector.Color;

namespace T3.Editor.Gui.OutputUi.Geometry;

/// <summary>
/// Label/value lines and the attribute list shown for CPU geometry. Owners fill it when the geometry changes, so
/// drawing stays allocation-free.
/// </summary>
internal sealed class GeometryStatsPanel
{
    public string Headline { get; private set; } = string.Empty;

    public void Clear(string headline)
    {
        Headline = headline;
        _lineCount = 0;
        _attributeCount = 0;
    }

    public void AddLine(string label, string value, Color color)
    {
        if (_lineCount == _labels.Length)
            return;

        _labels[_lineCount] = label;
        _values[_lineCount] = value;
        _valueColors[_lineCount] = color;
        _lineCount++;
    }

    public void SetAttributes(GeometryAttributes attributes)
    {
        _attributeCount = 0;
        foreach (var attribute in attributes)
        {
            if (_attributeCount == _attributeNames.Length)
                break;

            _attributeNames[_attributeCount] = attribute.Name;
            _attributeInfos[_attributeCount] = $"{TypeName(attribute)} per {attribute.Domain}";
            _attributeCount++;
        }
    }

    /// <param name="showHeadline">False when the headline is already shown elsewhere, e.g. next to the mode button.</param>
    public void Draw(bool showHeadline)
    {
        if (showHeadline)
        {
            CustomComponents.StylizedText(Headline, Fonts.FontLarge, UiColors.Text);
            FormInputs.AddVerticalSpace(3);
        }

        for (var i = 0; i < _lineCount; i++)
        {
            CustomComponents.StylizedText(_labels[i], Fonts.FontSmall, UiColors.TextMuted);
            ImGui.SameLine(LabelWidth * T3Ui.UiScaleFactor);
            CustomComponents.StylizedText(_values[i], Fonts.FontNormal, _valueColors[i]);
        }

        if (_attributeCount == 0)
            return;

        FormInputs.AddVerticalSpace(8);
        CustomComponents.StylizedText("Attributes", Fonts.FontSmall, UiColors.TextMuted);
        for (var i = 0; i < _attributeCount; i++)
        {
            CustomComponents.StylizedText(_attributeNames[i], Fonts.FontNormal, UiColors.Text);
            ImGui.SameLine(LabelWidth * T3Ui.UiScaleFactor);
            CustomComponents.StylizedText(_attributeInfos[i], Fonts.FontSmall, UiColors.TextMuted);
        }
    }

    public static string FormatBounds(Vector3 min, Vector3 max)
    {
        return $"({min.X:0.###}, {min.Y:0.###}, {min.Z:0.###}) .. ({max.X:0.###}, {max.Y:0.###}, {max.Z:0.###})";
    }

    public static string FormatSize(Vector3 size)
    {
        return $"{size.X:0.###} x {size.Y:0.###} x {size.Z:0.###}";
    }

    private static string TypeName(GeometryAttribute attribute)
    {
        return attribute switch
                   {
                       GeometryAttribute<float>   => "float",
                       GeometryAttribute<int>     => "int",
                       GeometryAttribute<Vector2> => "vec2",
                       GeometryAttribute<Vector3> => "vec3",
                       GeometryAttribute<Vector4> => "vec4",
                       _                          => "?",
                   };
    }

    private const float LabelWidth = 90;
    private const int MaxLines = 8;
    private const int MaxAttributes = 32;

    private readonly string[] _labels = new string[MaxLines];
    private readonly string[] _values = new string[MaxLines];
    private readonly Color[] _valueColors = new Color[MaxLines];
    private int _lineCount;
    private readonly string[] _attributeNames = new string[MaxAttributes];
    private readonly string[] _attributeInfos = new string[MaxAttributes];
    private int _attributeCount;
}
