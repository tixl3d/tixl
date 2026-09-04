using System.Globalization;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Windows.TimeLine.Raster;

public sealed  class HorizontalRaster: IValueSnapAttractor
{
    public void Draw(ScalableCanvas canvas)
    {
        _canvas = canvas;
        DrawLines(canvas.Scale.Y , canvas.Scroll.Y); // ???????????
    }

    private ScalableCanvas _canvas;
        
    private static string BuildLabel(AbstractTimeRaster.Raster raster, double time)
    {
        var output = "";
        foreach (var c in raster.Label)
        {
            if (c == 'N')
            {
                output += time.ToString("G5", CultureInfo.InvariantCulture);
            }
            else
            {
                output += c;
            }
        }
        return output;
    }

        
    private IEnumerable<AbstractTimeRaster.Raster> GetRastersForScale(double scale, out float fadeFactor)
    {
        const float density = 1.0f;
        var uPerPixel = scale;
        var logScale = (float)Math.Log10(uPerPixel) + density;
        var logScaleMod = (logScale + 1000)%1.0f;
        var logScaleFloor = (float)Math.Floor(logScale);
            
        if (logScaleMod < 0.5)
        {
            fadeFactor = 1-logScaleMod*2;
            _blendRasters[0]=new AbstractTimeRaster.Raster()
                                 {
                                     Label = "N",
                                     Spacing = (float)Math.Pow(10, logScaleFloor)*50,
                                 };
            _blendRasters[1]= new AbstractTimeRaster.Raster()
                                  {
                                      Label = "N",
                                      Spacing = (float)Math.Pow(10, logScaleFloor)*10,
                                      FadeLabels = true,
                                      FadeLines = true,
                                  };
        }
        else
        {
            fadeFactor = 1-(logScaleMod - 0.5f)*2;
            _blendRasters[0]= new AbstractTimeRaster.Raster()
                                  {
                                      Label = "N",
                                      Spacing = (float)Math.Pow(10, logScaleFloor)*100,
                                  };
            _blendRasters[1]= new AbstractTimeRaster.Raster()
                                  {
                                      Label = "N",
                                      Spacing = (float)Math.Pow(10, logScaleFloor)*50,
                                      FadeLabels = true,
                                      FadeLines = true,
                                  };
        }
        return _blendRasters;
    }
        
    private readonly AbstractTimeRaster.Raster[] _blendRasters = new AbstractTimeRaster.Raster[2];
        
        
    private void DrawLines(double scale, double scroll)
    {
        if (!(Math.Abs(scale) > Epsilon))
            return;

        var drawList = ImGui.GetWindowDrawList();
        var topLeft = _canvas.WindowPos;
        var viewWidth = _canvas.WindowSize.X;
        var height = _canvas.WindowSize.Y;

        _usedPositions.Clear();

        scale = 1/scale;
        scale = -scale;

        ImGui.PushFont(Fonts.FontSmall);
        var rasters = GetRastersForScale(scale, out var fadeFactor);

        foreach (var raster in rasters)
        {
            double t = scroll%raster.Spacing;
                
            var lineAlpha = raster.FadeLines ? fadeFactor : 1;
            var lineColor = new Color(0, 0, 0, lineAlpha*0.3f);

            var textAlpha = raster.FadeLabels ? fadeFactor : 1;
            var textColor = new Color(textAlpha);

            while (t/scale < height)
            {
                var yIndex = (int)(t/scale);

                if (yIndex > 0 && yIndex < height && !_usedPositions.ContainsKey(yIndex))
                {
                    var value = scroll-t;
                    _usedPositions[yIndex] = value;

                    var y = (int)(topLeft.Y + yIndex);
                    drawList.AddRectFilled(
                                           new Vector2(topLeft.X,y ),
                                           new Vector2(topLeft.X + viewWidth, y+1), lineColor);

                    if (raster.Label != "")
                    {
                        var output = BuildLabel(raster, value);
                        var p = new Vector2(topLeft.X+ 5,y - 8f);
                        drawList.AddText(p, textColor, output);
                    }
                }

                t += raster.Spacing;
            }
        }
        ImGui.PopFont();
    }

    #region implement snap attractor
        
    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        snapResult.TryToImproveWithAnchorValueList(_usedPositions.Values);
    }
    #endregion
        
    private readonly Dictionary<int, double> _usedPositions = new();
    private const double Epsilon = 0.001f;
}