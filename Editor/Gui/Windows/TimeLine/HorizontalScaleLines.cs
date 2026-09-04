using System.Globalization;
using ImGuiNET;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Interaction logic for HorizontalScaleLines.xaml
/// </summary>
public class HorizontalScaleLines : IValueSnapAttractor
{
    public HorizontalScaleLines(ScalableCanvas canvas)
    {
        _canvas = canvas;
    }

    private readonly ScalableCanvas _canvas;


    public void Draw()
    {
        var drawList = ImGui.GetWindowDrawList();

        var bottom = _canvas.WindowPos.Y + _canvas.WindowSize.Y;

        var pixelsPerU = _canvas.Scale.X;
        var offset = _canvas.Scroll.X;
        _usedPositions.Clear();

        if (pixelsPerU > 0.001f)
        {
            Dictionary<int, bool> usedPositions = new Dictionary<int, bool>();

            const float DENSITY = 1.0f;
            float width = _canvas.WindowSize.X;
            float uPerPixel = 1 / pixelsPerU;
            float logScale = (float)Math.Log10(uPerPixel) + DENSITY;
            float logScaleMod = (logScale + 1000) % 1.0f;
            float logScaleFloor = (float)Math.Floor(logScale);

            var lineDefinitions = new List<LineDefinition>();

            if (logScaleMod < 0.5)
            {
                lineDefinitions.Add(new LineDefinition()
                                        {
                                            Label = "N",
                                            Spacing = (float)Math.Pow(10, logScaleFloor) * 50,
                                            LabelOpacity = 1,
                                            LineOpacity = 1
                                        });
                lineDefinitions.Add(new LineDefinition()
                                        {
                                            Label = "N",
                                            Spacing = (float)Math.Pow(10, logScaleFloor) * 10,
                                            LabelOpacity = 1 - logScaleMod * 2,
                                            LineOpacity = 1 - logScaleMod * 2
                                        });
            }
            else
            {
                lineDefinitions.Add(new LineDefinition()
                                        {
                                            Label = "N",
                                            Spacing = (float)Math.Pow(10, logScaleFloor) * 100,
                                            LabelOpacity = 1.0f,
                                            LineOpacity = 1.0f
                                        });
                lineDefinitions.Add(new LineDefinition()
                                        {
                                            Label = "N",
                                            Spacing = (float)Math.Pow(10, logScaleFloor) * 50,
                                            LabelOpacity = 1 - (logScaleMod - 0.5f) * 2,
                                            LineOpacity = 1 - (logScaleMod - 0.5f) * 2
                                        });
            }

            foreach (LineDefinition linedef in lineDefinitions)
            {
                float t = -offset % linedef.Spacing;

                while (t / uPerPixel < width)
                {
                    float u = (t / uPerPixel);
                    float posX = u;
                    int x = (int)posX;

                    if (u > 0 && u < width && !_usedPositions.ContainsKey(x))
                    {
                        _usedPositions[x] = t + offset;

                        var p1 = new Vector2(posX + _canvas.WindowPos.X, bottom - 3);
                        drawList.AddRectFilled(p1, p1 + new Vector2(1, 3), UiColors.ForegroundFull);
                        //var pen = new Pen(GetTransparentBrush(linedef.LineOpacity * 0.3) , 1);
                        //pen.Freeze();
                        //dc.DrawLine(pen, new Point(posX,0 ), new Point(posX, ActualHeight));


                        if (!string.IsNullOrEmpty(linedef.Label))
                        {
                            string output = "";
                            foreach (char c in linedef.Label)
                            {
                                if (c == 'N')
                                {
                                    output += (t + offset).ToString("G5", CultureInfo.InvariantCulture);
                                }
                                else
                                {

                                    output += c;
                                }
                            }

                            //FormattedText text= new FormattedText(output,
                            //                                        CultureInfo.GetCultureInfo("en-us"),
                            //                                        FlowDirection.LeftToRight,
                            //                                        new Typeface("Verdana"),
                            //                                        7,
                            //                                        GetTransparentLabelBrush( linedef.LabelOpacity * 0.5)
                            //                                        );
                            //text.TextAlignment = TextAlignment.Left;
                            //dc.DrawText(text, new Point(posX + 5, ActualHeight-12.0));

                            var s = ImGui.CalcTextSize(output);
                            var p2 = new Vector2(posX + _canvas.WindowPos.X - s.X * 0.5f, bottom - 16);
                            //drawList.AddRectFilled(p1, p1 + new Vector2(1, 3), Color.White);
                            drawList.AddText(p2, UiColors.Gray, output);
                        }
                    }
                    t += linedef.Spacing;
                }
            }
        }
    }


    #region implement snap attractor

    private const double SnapThreshold = 8;

    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    //SnapResult IValueSnapAttractor.CheckForSnap(double time, float scale, SnapResult.Orientations orientation)
    {
        //var TV= App.Current.MainWindow.CompositionView.XTimeView;
        //if (this.Visibility == System.Windows.Visibility.Collapsed)
        //    return null;

        foreach (var beatTime in _usedPositions.Values)
        {
            double distanceToTime = Math.Abs(snapResult.TargetValue - beatTime) * _canvas.WindowSize.X;
            if (distanceToTime < SnapThreshold)
            {
                snapResult.BestAnchorValue = beatTime;
                snapResult.BestForce = distanceToTime;
            }
        }
    }
    #endregion

    private struct LineDefinition
    {
        public String Label { get; set; }
        public float Spacing { get; set; }
        public float LineOpacity { get; set; }
        public float LabelOpacity { get; set; }
    }

    private readonly Dictionary<int, double> _usedPositions = new();
    //private List<DrawingVisual> m_Children = new List<DrawingVisual>();
    //private DrawingVisual m_DrawingVisual;
    public SnapResult CheckForSnap(double value, float canvasScale) {
        throw new NotImplementedException();
    }
}