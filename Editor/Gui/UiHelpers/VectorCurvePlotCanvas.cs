#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Windows.TimeLine.Raster;

namespace T3.Editor.Gui.UiHelpers;

internal sealed class VectorCurvePlotCanvas<T>
{
    internal VectorCurvePlotCanvas(int resolution = 500)
    {
        _sampleCount = resolution;
        _graphValues = new T[_sampleCount];

        if (typeof(T) == typeof(float))
        {
            _componentCount = 1;
        }
        else if (typeof(T) == typeof(Vector2))
        {
            _componentCount = 2;
        }
        else if (typeof(T) == typeof(Vector3))
        {
            _componentCount = 3;
        }
        else if (typeof(T) == typeof(Vector4))
        {
            _componentCount = 4;
        }
        else
        {
            _componentCount = 0;
        }
        
        _graphPoints = new Vector2[_componentCount, _sampleCount];
        _lastValues = new float[_componentCount];
    }

    private readonly int _componentCount;

    public void Draw(T value)
    {
        var dl = ImGui.GetWindowDrawList();
        var min = float.PositiveInfinity;
        var max = float.NegativeInfinity;

        var newValueComponents = Utilities.GetFloatsFromVector(value);

        foreach (var vv in _graphValues)
        {
            var components = Utilities.GetFloatsFromVector(vv);
            foreach (var v in components)
            {
                if (v > max)
                    max = v;

                if (v < min)
                    min = v;
            }
        }

        var padding = (max - min) * 0.2f;
        min -= padding;
        max += padding;
        dl.PushClipRect(_canvas.WindowPos, _canvas.WindowPos + _canvas.WindowSize, true);
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
            _lastValues = newValueComponents;
        }

        _canvas.UpdateCanvas(out _);
        _raster.Draw(_canvas);
        if (!_paused)
        {
            _graphValues[_sampleOffset] = value;
            _sampleOffset = (_sampleOffset + 1) % _sampleCount;
        }
        
        var maxLength = Math.Max((int)ImGui.GetWindowSize().X, 10);
        var shownSampleCount = Math.Min(_sampleCount, maxLength);
        var startIndex = Math.Max(0,_sampleCount - shownSampleCount);

        var x = _canvas.WindowPos.X;
        var dx = (_canvas.WindowSize.X - 20) / shownSampleCount;
        for (var index = startIndex; index < _graphValues.Length; index++)
        {
            var v = _graphValues[(index + _sampleOffset) % _sampleCount];
            var components = Utilities.GetFloatsFromVector(v);
            for (var cIndex = 0; cIndex < components.Length; cIndex++)
            {
                var c = components[cIndex];
                _graphPoints[cIndex,index] = new Vector2((int)x,
                                                         (int)_canvas.TransformY(c));
            }
            x += dx;
        }
        
        for(int cIndex= 0; cIndex< _componentCount; cIndex ++)
        {
            var color = CurveComponentColors.GetCurveColor(cIndex, _componentCount);
            dl.AddPolyline(ref _graphPoints[cIndex,startIndex], shownSampleCount, color, ImDrawFlags.None, 1);
            dl.AddCircleFilled(_graphPoints[cIndex, _sampleCount - 1], 3, color);
        }

        var windowHeight = ImGui.GetWindowSize().Y / T3Ui.UiScaleFactor;
        var font = windowHeight switch
                            {
                                < 100 => Fonts.FontSmall,
                                < 200 => Fonts.FontNormal,
                                _     => Fonts.FontLarge,
                            };
            
        
        var y = _canvas.WindowSize.Y * 0.5f - font.FontSize * _componentCount / 2;

        for (var cIndex = 0; cIndex < _lastValues.Length; cIndex++)
        {
            var color = CurveComponentColors.GetValueColor(cIndex, _componentCount);
            // The readout stays legible even when the curve color is semi-transparent.
            color.Rgba.W = 1;

            var lastValue = _lastValues[cIndex];
            var valueAsString = $"{lastValue:G4}";
            dl.AddText(font,
                       font.FontSize,
                       _canvas.WindowPos
                       + new Vector2(_canvas.WindowSize.X - font.FontSize*5,
                                     y),
                       color,
                       valueAsString);
            y += font.FontSize;
        }

        dl.PopClipRect();
    }

    public void Reset(T clearValue)
    {
        for (var index = 0; index < _sampleCount; index++)
        {
            _graphValues[index] = clearValue;
        }
    }

    private readonly T[] _graphValues;
    private readonly Vector2[,] _graphPoints;
    private int _sampleOffset;
    private readonly int _sampleCount;

    private bool _paused;
    private float[] _lastValues;

    private readonly HorizontalRaster _raster = new();
    private readonly ScalableCanvas _canvas = new CurrentGraphSubCanvas { FillMode = ScalableCanvas.FillModes.FillAvailableContentRegion };
    //private const int MaxComponents = 4;
}

/// <summary>
/// Decides which theme color each component of a plotted value is drawn with. The colors themselves are
/// declared in <see cref="UiColors"/> (group "Curve plots") so that they can be edited in the theme editor;
/// this mapping is what makes the first component red, the second green, the third blue and the fourth gray.
/// </summary>
internal static class CurveComponentColors
{
    /// <summary>Color of the curve line. Values with a single component (a lone number) share one fallback color.</summary>
    internal static Color GetCurveColor(int componentIndex, int componentCount)
    {
        if (componentCount == 1)
            return UiColors.CurvePlotSingleComponent;

        return componentIndex switch
                   {
                       0 => UiColors.CurvePlotLineX,
                       1 => UiColors.CurvePlotLineY,
                       2 => UiColors.CurvePlotLineZ,
                       _ => UiColors.CurvePlotLineW,
                   };
    }

    /// <summary>Color of the numeric value printed next to the curve.</summary>
    internal static Color GetValueColor(int componentIndex, int componentCount)
    {
        if (componentCount == 1)
            return UiColors.CurvePlotSingleComponent;

        return componentIndex switch
                   {
                       0 => UiColors.CurvePlotValueX,
                       1 => UiColors.CurvePlotValueY,
                       2 => UiColors.CurvePlotValueZ,
                       _ => UiColors.CurvePlotValueW,
                   };
    }
}
