using System.Text;

namespace T3.Editor.Gui.Windows.TimeLine.Raster;

/// <summary>
/// A <see cref="AbstractTimeRaster"/> that displays Bars, Beats and ticks 
/// </summary>
internal sealed class BeatTimeRaster : AbstractTimeRaster
{
    internal override void Draw(TimeLineCanvas timeLineCanvas, float unitsPerSeconds, float verticalLabelAlign = 1)
    {
        var scale = timeLineCanvas.Scale.X;
        var scroll = timeLineCanvas.Scroll.X;
        DrawTimeTicks(scale, scroll, timeLineCanvas,verticalLabelAlign);
    }

    protected override string BuildLabel(Raster raster, double timeInBars)
    {
        _stringBuilder.Clear();

        foreach (var c in raster.Label)
        {
            switch (c)
            {
                // bars
                case 'b':
                    _stringBuilder.Append($"{(int)timeInBars}");
                    break; 
                // beats
                case '.':
                    _stringBuilder.Append($".{(int)(timeInBars * 4) % 4}");
                    break; 
                // ticks
                case ':':
                    _stringBuilder.Append($":{(int)(timeInBars * 16) % 4}");
                    break; 
                default:
                    _stringBuilder.Append(c);
                    break;
            }
        }

        return _stringBuilder.ToString();
    }

    
    protected override void InitScaleRanges(float density)
    {
        density *= 0.02f;
        const float everyTick = 1 / 240f * 60 / 4;
        const float everyBeat = 1 / 240f * 60;
        const float everyBar = 4 / 240f * 60;
        const float everyMeasure = 16 / 240f * 60;
        const float everyPhrase = 64 / 240f * 60;
        const float every10Phrases = 10 * 64 / 240f * 60;
        
        ScaleRanges =
        [
            new ScaleRange(ScaleMin: 0, ScaleMax: 0.001 / density, Rasters:
            [
                new Raster("b", everyBar, FadeLabels: false, FadeLines: false),
                new Raster("b.", everyBeat, FadeLabels: false, FadeLines: false),
                new Raster(":", Spacing: everyTick, FadeLabels: true, FadeLines: false)
            ]),
            new ScaleRange(ScaleMin: 0.001 / density, ScaleMax: 0.002 / density, Rasters:
            [
                new Raster("b", Spacing: everyBar, FadeLabels: false, FadeLines: false),
                new Raster("b.", Spacing: everyBeat, FadeLabels: false, FadeLines: false),
                new Raster("", Spacing: everyTick, FadeLabels: false, FadeLines: true)
            ]),
            new ScaleRange(ScaleMin: 0.002 / density, ScaleMax: 0.005 / density, Rasters:
            [
                new Raster("b", Spacing: everyBar, FadeLabels: false, FadeLines: false),
                new Raster("b.", Spacing: everyBeat, FadeLabels: true, FadeLines: false)
            ]),
            new ScaleRange(ScaleMin: 0.005 / density, ScaleMax: 0.007 / density, Rasters:
            [
                new Raster("b", Spacing: everyBar, FadeLabels: false, FadeLines: false),
                new Raster("", Spacing: everyBeat, FadeLabels: true, FadeLines: false)
            ]),
            new ScaleRange(ScaleMin: 0.007 / density, ScaleMax: 0.01 / density, Rasters:
            [
                new Raster("b", Spacing: everyBar, FadeLabels: false, FadeLines: false),
                new Raster("", Spacing: everyBeat, FadeLabels: true, FadeLines: true)
            ]),
            new ScaleRange(ScaleMin: 0.01 / density, ScaleMax: 0.012 / density, Rasters:
            [
                new Raster("b", Spacing: everyBar, FadeLabels: false, FadeLines: false)
            ]),
            new ScaleRange(ScaleMin: 0.012 / density, ScaleMax: 0.03 / density, Rasters:
            [
                new Raster("b", Spacing: everyMeasure, FadeLabels: false, FadeLines: false),
                new Raster("b", Spacing: everyBar, FadeLabels: true, FadeLines: false)
            ]),
            new ScaleRange(ScaleMin: 0.03 / density, ScaleMax: 0.06 / density, Rasters:
            [
                new Raster("b", Spacing: everyMeasure, FadeLabels: false, FadeLines: false),
                new Raster("", Spacing: everyBar, FadeLabels: true, FadeLines: true)
            ]),
            new ScaleRange(ScaleMin: 0.06 / density, ScaleMax: 0.1 / density, Rasters:
            [
                new Raster("b", Spacing: everyMeasure, FadeLabels: false, FadeLines: false)
            ]),

            new ScaleRange(ScaleMin: 0.1 / density, ScaleMax: 0.15 / density, Rasters:
            [
                new Raster("b", Spacing: everyPhrase, FadeLabels: false, FadeLines: false),
                new Raster("b", Spacing: everyMeasure, FadeLabels: true, FadeLines: false)
            ]),
            new ScaleRange(ScaleMin: 0.15 / density, ScaleMax: 0.3 / density, Rasters:
            [
                new Raster("b", Spacing: everyPhrase, FadeLabels: false, FadeLines: false),
                new Raster("", Spacing: everyMeasure, FadeLabels: true, FadeLines: true)
            ]),
            new ScaleRange(ScaleMin: 0.3 / density, ScaleMax: 0.5 / density, Rasters:
            [
                new Raster("b", Spacing: every10Phrases, FadeLabels: false, FadeLines: false),
                new Raster("b", Spacing: everyPhrase, FadeLabels: true, FadeLines: false)
            ]),
            new ScaleRange(ScaleMin: 0.5 / density, ScaleMax: 1.2 / density, Rasters:
            [
                new Raster("b", Spacing: every10Phrases, FadeLabels: false, FadeLines: false),
                new Raster("", Spacing: everyPhrase, FadeLabels: true, FadeLines: true)
            ]),
            new ScaleRange(ScaleMin: 1.2 / density, ScaleMax: 2 / density, Rasters:
            [
                new Raster("b", Spacing: every10Phrases, FadeLabels: true, FadeLines: false)
            ]),
            new ScaleRange(ScaleMin: 2 / density, ScaleMax: 999 / density, Rasters:
            [
                new Raster("", Spacing: every10Phrases, FadeLabels: false, FadeLines: false)
            ])
        ];
    }
    
    private static readonly StringBuilder _stringBuilder = new(20);
}