using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.TimeLine.Raster;

public abstract class AbstractTimeRaster : IValueSnapAttractor
{
    internal abstract void Draw(TimeLineCanvas timeLineCanvas, float unitsPerSeconds, float verticalLabelAlign=1);
    protected abstract string BuildLabel(Raster raster, double timeInSeconds);
        
    protected double UnitsPerSecond { get; set; } = 1;
    protected abstract void InitScaleRanges(float density);
    
    
    protected virtual bool TryGetRastersForScale(double invertedScale, [NotNullWhen(true)] out Raster[]? rasters, out float fadeFactor)
    {
        var density = UserSettings.Config.TimeRasterDensity * 0.02f;
        
        foreach (var range in ScaleRanges)
        {
            if (range.ScaleMax < invertedScale / density) 
                continue;

            fadeFactor = 1 - (float) MathUtils.RemapAndClamp(invertedScale, 
                range.ScaleMin * density,
                range.ScaleMax * density, 
                0, 
                1);
            
            rasters = range.Rasters;
            return true;

        }

        fadeFactor = 0;
        rasters = null;
        return false;
    }

    private void InitializeTimeScaleDefinitions(float density)
    {
        if (ScaleRanges != null && Math.Abs(density - _density) < 0.0001) 
            return;
        
        InitScaleRanges(density);
        _density = density;
    }

    private float _density = 1;
    
    protected void DrawTimeTicks(double scale, double scroll, ScalableCanvas canvas, float verticalLabelAlign = 1)
    {
        if (!(scale > Epsilon))
            return;

        InitializeTimeScaleDefinitions(UserSettings.Config.TimeRasterDensity);
        
        var drawList = ImGui.GetWindowDrawList();
        var topLeft = canvas.WindowPos + new Vector2(ImGui.GetScrollX(), ImGui.GetScrollY());
        var viewHeight = canvas.WindowSize.Y;
        var width = canvas.WindowSize.X;

        _usedPositions.Clear();

            
        var invertedScale = 1 / scale;

        if(!TryGetRastersForScale(invertedScale, out var rasters, out var fadeFactor))
            return;
        
        ImGui.PushFont(Fonts.FontSmall);

        // Debug string 
        //ImGui.GetForegroundDrawList().AddText(topLeft, Color.Green, $"{UserSettings.Config.TimeRasterDensity /scale:0.00000}");
            
        foreach (var raster in rasters)
        {
            double t = -scroll % raster.Spacing;

            var lineAlpha = raster.FadeLines ? fadeFactor : 1;
            var lineColor = UiColors.GridLines.Fade(lineAlpha * 0.4f);

            var textAlpha = raster.FadeLabels ? fadeFactor : 1;
            var textColor = UiColors.Text.Fade(textAlpha);

            while (t / invertedScale < width)
            {
                var xIndex = (int)(t / invertedScale);

                if (xIndex > 0 && xIndex < width && !_usedPositions.ContainsKey(xIndex))
                {
                    var timeInUnits = t + scroll;

                    _usedPositions[xIndex] = timeInUnits / UnitsPerSecond;


                    drawList.AddRectFilled(
                                           new Vector2(topLeft.X + xIndex, topLeft.Y),
                                           new Vector2(topLeft.X + xIndex + 1, topLeft.Y + viewHeight), lineColor);

                    if (raster.Label != "")
                    {
                        var output = BuildLabel(raster, timeInUnits);
                        // Get exact text size
                        var textSize = ImGui.CalcTextSize(output);
                        var p = topLeft + new Vector2(xIndex + 1, (viewHeight  - textSize.Y) * verticalLabelAlign );
                        drawList.AddText(p, textColor, output);
                    }
                }

                t += raster.Spacing;
            }
        }

        ImGui.PopFont();
    }

    #region implement snap attractor

    public virtual void CheckForSnap(ref SnapResult snapResult)
    {
        snapResult.TryToImproveWithAnchorValueList(_usedPositions.Values);
    }
    #endregion

    private readonly Dictionary<int, double> _usedPositions = new();
    protected ScaleRange[] ScaleRanges;
    private const double Epsilon = 0.00001f;
    
    protected record struct ScaleRange(double ScaleMin, double ScaleMax, Raster[] Rasters);
    public record struct Raster(string Label, double Spacing, bool FadeLabels=false, bool FadeLines=false);
}