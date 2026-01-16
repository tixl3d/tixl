using ImGuiNET;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Windows.TimeLine.Raster;

namespace T3.Editor.Gui.UiHelpers;

internal sealed class CurvePlotCanvas
{
    internal CurvePlotCanvas(int resolution = 500)
    {
        _sampleCount = resolution;
        _graphValues = new float[_sampleCount];
        _graphPoints = new Vector2[_sampleCount];
    }

    internal void Draw(float value)
    {
        var dl = ImGui.GetWindowDrawList();
        var min = float.PositiveInfinity;
        var max = float.NegativeInfinity;

        foreach (var v in _graphValues)
        {
            if (v > max)
                max = v;

            if (v < min)
                min = v;
        }

        var padding = (max - min) * 0.2f;
        if (padding < 0.001f)
            padding = 0.05f;
        min -= padding;
        max += padding;

            
        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _paused = !_paused;
        }

        if (!ImGui.IsWindowFocused())
        {
            _paused = false;
        }

        if (!_paused)
        {
            _canvas.SetScopeToCanvasArea(new ImRect(0, min, 1, max), flipY: true);
            _lastValue = value;
        }
            
        _canvas.UpdateCanvas(out _);
        dl.PushClipRect(_canvas.WindowPos, _canvas.WindowPos + _canvas.WindowSize, true);
            
        _raster.Draw(_canvas);

        if (!_paused && float.IsFinite(value) && !float.IsInfinity(value) && !float.IsNaN(value))
        {
            _graphValues[_sampleOffset] = value;
            _sampleOffset = (_sampleOffset + 1) % _sampleCount;
        }

        var maxLength = Math.Max((int)ImGui.GetWindowSize().X, 10);
        var shownSampleCount = Math.Min(_sampleCount, maxLength);
        var startIndex = Math.Max(0,_sampleCount - shownSampleCount);
        
        var x = +_canvas.WindowPos.X;
        var dx = (_canvas.WindowSize.X - 20) / shownSampleCount;
        for (var index = startIndex; index < _graphValues.Length; index++)
        {
            var v = _graphValues[(index + _sampleOffset) % _sampleCount];
            _graphPoints[index] = new Vector2(
                                              (int)x,
                                              (int)_canvas.TransformY(v));
            x += dx;
        }
            
        dl.AddPolyline(ref _graphPoints[startIndex], 
                       shownSampleCount , 
                       UiColors.Text.Fade(0.3f), 
                       ImDrawFlags.None, 
                       1);
        dl.AddCircleFilled(_graphPoints[_sampleCount - 1], 3, UiColors.Gray);

        var windowHeight = ImGui.GetWindowSize().Y / T3Ui.UiScaleFactor;
        var font = windowHeight switch
                       {
                           < 100 => Fonts.FontSmall,
                           < 200 => Fonts.FontNormal,
                           _     => Fonts.FontLarge,
                       };
        
        var valueAsString = $"{_lastValue:G5}";
        dl.AddText(font,
                   font.FontSize,
                   _canvas.WindowPos
                   + new Vector2(_canvas.WindowSize.X - font.FontSize*5,
                                 _canvas.WindowSize.Y * 0.5f - font.FontSize / 2),
                   UiColors.Text,
                   valueAsString);
            
        dl.PopClipRect();
    }

    public void Reset(float clearValue = 0)
    {
        for (var index = 0; index < _sampleCount; index++)
        {
            _graphValues[index] = clearValue;
        }
    }

    private readonly float[] _graphValues;
    private readonly Vector2[] _graphPoints;
    private int _sampleOffset;
    private readonly int _sampleCount;

    private bool _paused;
    private float _lastValue;

    private readonly HorizontalRaster _raster = new();
    private readonly ScalableCanvas _canvas = new CurrentGraphSubCanvas { FillMode = ScalableCanvas.FillModes.FillAvailableContentRegion };
}