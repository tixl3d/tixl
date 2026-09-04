using ImGuiNET;
using T3.Core.Utils;

namespace T3.Editor.Gui.UiHelpers;

public class CurvePlot
{
    public CurvePlot(string label = "", float width = float.NaN, int resolution = 500)
    {
        _sampleCount = resolution;
        _graphValues = new float[_sampleCount];
        _label = label;
        _width = width;
    }

    public void Draw(float value)
    {
        _graphValues[_sampleOffset] = value;
        if (Damping)
        {
            _dampedValue = MathUtils.Lerp(_dampedValue, value, 0.01f);
            value = _dampedValue;
        }
            

        _sampleOffset = (_sampleOffset + 1) % _sampleCount;
        if (_width > 0)
            ImGui.SetNextItemWidth(120);

        if (float.IsNaN(MinValue))
        {
            ImGui.PlotLines($"{value:##.##} {_label}", ref _graphValues[0], _sampleCount, _sampleOffset);
        }
        else
        {
            ImGui.PlotLines($"{value:##.##} {_label}", ref _graphValues[0], _sampleCount, _sampleOffset, "", 
                            scale_min: MinValue,
                            scale_max: MaxValue);
        }
    }

    public void Reset(float clearValue = 0)
    {
        for (var index = 0; index < _sampleCount; index++)
        {
            _graphValues[index] = clearValue;
        }
    }

    public float MinValue = float.NaN;
    public float MaxValue = 100;
    private readonly float[] _graphValues;
    private int _sampleOffset;
    private readonly int _sampleCount;
    private readonly string _label;
    private readonly float _width;
    public  bool Damping;
    private float _dampedValue;
}