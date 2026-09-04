#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Snapping;

namespace T3.Editor.Gui.Windows.TimeLine.Raster;

/// <summary>
/// A time raster (vertical lines) that calculate required labels and spacing logarithmically. 
/// </summary>
internal sealed class StandardValueRaster : AbstractTimeRaster
{
    internal override void Draw(TimeLineCanvas timeLineCanvas, float unitsPerSeconds, float verticalLabelAlign = 1)
    {
        UnitsPerSecond = unitsPerSeconds / timeLineCanvas.Playback.Bpm * 60f * 4f;

        var scale = timeLineCanvas.Scale.X / UnitsPerSecond;
        var scroll = timeLineCanvas.Scroll.X * UnitsPerSecond;
        DrawTimeTicks(scale, scroll, timeLineCanvas,verticalLabelAlign);
    }

    /// <summary>
    /// Normally time rasters don't snap by default.
    /// When <see cref="StandardValueRaster"/> is used inside curve editors,
    /// default snapping is enabled.  
    /// </summary>
    public bool EnableSnapping = false;

    public void Draw(ScalableCanvas canvas)
    {
        var unitInSecs = 1;

        var scale = canvas.Scale.X / unitInSecs;
        var scroll = canvas.Scroll.X * canvas.Scale.X;
        DrawTimeTicks(scale, scroll / scale, canvas);
    }

    protected override string BuildLabel(Raster raster, double timeInUnits)
    {
        var output = "";
        foreach (var c in raster.Label)
        {
            if (c == 'N')
            {
                output += timeInUnits.ToString("G5", CultureInfo.InvariantCulture);
            }
            else
            {
                output += c;
            }
        }

        return output;
    }
    
    protected override void InitScaleRanges(float density) { }

    protected override bool TryGetRastersForScale(double scale, [NotNullWhen(true)] out Raster[]? rasters,
        out float fadeFactor)
    {
        const float density = 1.0f;
        var logScale = (float) Math.Log10(scale) + density;
        var logScaleMod = (logScale + 1000) % 1.0f;
        var logScaleFloor = (float) Math.Floor(logScale);

        if (logScaleMod < 0.5)
        {
            fadeFactor = 1 - logScaleMod * 2;
            _blendRasters[0] = new Raster(Label: "N", Spacing: (float) Math.Pow(10, logScaleFloor) * 50);
            _blendRasters[1] = new Raster(Label: "N", Spacing: (float) Math.Pow(10, logScaleFloor) * 10, true, true);
        }
        else
        {
            fadeFactor = 1 - (logScaleMod - 0.5f) * 2;
            _blendRasters[0] = new Raster(Label: "N", Spacing: (float) Math.Pow(10, logScaleFloor) * 100);
            _blendRasters[1] = new Raster(Label: "N", Spacing: (float) Math.Pow(10, logScaleFloor) * 50, true, true);
        }

        rasters = _blendRasters;
        return true;
    }

    public override void CheckForSnap(ref SnapResult snapResult)
    {
        if (EnableSnapping)
            base.CheckForSnap(ref snapResult);
    }

    private readonly Raster[] _blendRasters = new Raster[2];
}