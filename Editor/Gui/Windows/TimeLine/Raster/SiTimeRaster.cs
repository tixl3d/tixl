#nullable enable

using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.TimeLine.Raster;

/// <summary>
/// A <see cref="AbstractTimeRaster"/> that displays Seconds, Minutes, Hours, etc. 
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed class SiTimeRaster : AbstractTimeRaster
{
    internal override void Draw(TimeLineCanvas timeLineCanvas, float unitsPerSeconds, float verticalLabelAlign = 1)
    {
        var scale = timeLineCanvas.Scale.X * timeLineCanvas.Playback.Bpm / 120f;
        var scroll = timeLineCanvas.Scroll.X / timeLineCanvas.Playback.Bpm * 120f;
        DrawTimeTicks(scale, scroll, timeLineCanvas,verticalLabelAlign);
    }

    private static string Format(double t, double spacing, int modulo)
    {
        var l = (int) (t / spacing + 0.0001f) % modulo;
        if (t < 0)
        {
            l--;
        }

        return "" + l;
    }

    protected override string BuildLabel(Raster raster, double beatTime)
    {
        var time = beatTime / BarsToSecs;
        var output = "";
        foreach (var c in raster.Label)
        {
            output += c switch
            {
                'Y' => Format(time, everyYear, 9999),
                'D' => Format(time, everyDay, 365),
                'H' => Format(time, everyHour, 24),
                'M' => Format(time, everyMinute, 60),
                'S' => Format(time, everySec, 60),
                'F' => Format(time, everySec / 60, 60),
                'T' => "." + Format(time, every100Ms, 10),
                _ => c
            };
        }

        return output;
    }

    public override void CheckForSnap(ref SnapResult snapResult)
    {
        if (ImGui.GetIO().KeyAlt)
            base.CheckForSnap(ref snapResult);
    }
    
    private const float BarsToSecs = 0.5f;

    private const float everyYear = 365 * 24 * 60 * 60;
    private const float every10Days = 10 * 24 * 60 * 60;
    private const float everyDay = 24 * 60 * 60;
    private const float every4Hours = 4 * 60 * 60;
    private const float everyHour = 60 * 60;
    private const float every5Minute = 5 * 60;
    private const float everyMinute = 1 * 60;
    private const float every15Sec = 15;
    private const float everySec = 1;
    private const float every100Ms = 1 / 10f;
    private const float every10Ms = 1 / 100f;

    
    
    protected override void InitScaleRanges(float density)
    {
        density *= 2f * 0.02f * 0.75f;
        const bool FadeLabels = true;
        const bool FadeLines = true;

        ScaleRanges =
        [
            new ScaleRange(0, 0.0002 / density, [
                new Raster("Hh", everyHour * BarsToSecs),
                new Raster("Mm", everyMinute * BarsToSecs),
                new Raster("Ss", everySec * BarsToSecs),
                new Raster("T", every100Ms * BarsToSecs),
                new Raster("", every10Ms * BarsToSecs, FadeLabels, FadeLines)
            ]),

            new ScaleRange(0.0002 / density, 0.0010 / density, [
                new Raster("Hh", everyHour * BarsToSecs),
                new Raster("Mm", everyMinute * BarsToSecs),
                new Raster("Ss", everySec * BarsToSecs),
                new Raster("T", every100Ms * BarsToSecs, FadeLabels)
            ]),
            new ScaleRange(0.0010 / density, 0.005 / density, [
                new Raster("Hh", everyHour * BarsToSecs),
                new Raster("Mm", everyMinute * BarsToSecs),
                new Raster("Ss", everySec * BarsToSecs),
                new Raster("", every100Ms * BarsToSecs, FadeLabels, FadeLines)
            ]),
            new ScaleRange(0.005 / density, 0.01 / density, [
                new Raster("Hh", everyHour * BarsToSecs),
                new Raster("Mm", everyMinute * BarsToSecs),
                new Raster("Ss", every15Sec * BarsToSecs),
                new Raster("Ss", everySec * BarsToSecs, FadeLabels)
            ]),
            new ScaleRange(0.01 / density, 0.03 / density, [
                new Raster("Hh", everyHour * BarsToSecs),
                new Raster("Mm", everyMinute * BarsToSecs),
                new Raster("Ss", every15Sec * BarsToSecs),
                new Raster("", everySec * BarsToSecs, FadeLabels)
            ]),
            new ScaleRange(0.03 / density, 0.1 / density, [
                new Raster("Hh", everyHour * BarsToSecs),
                new Raster("Mm", everyMinute * BarsToSecs),
                new Raster("Ss", every15Sec * BarsToSecs, FadeLabels),
                new Raster("", everySec * BarsToSecs, FadeLabels, FadeLines)
            ]),
            new ScaleRange(0.1 / density, 0.5 / density, [
                new Raster("Hh", everyHour * BarsToSecs),
                new Raster("Mm", everyMinute * BarsToSecs),
                new Raster("", every15Sec * BarsToSecs, FadeLabels, FadeLines)
            ]),
            new ScaleRange(0.5 / density, 1 / density, [
                new Raster("Hh", everyHour * BarsToSecs),
                new Raster("Mm", every5Minute * BarsToSecs, FadeLabels, FadeLines),
                new Raster("Mm", everyMinute * BarsToSecs, FadeLabels)
            ]),
            new ScaleRange(1 / density, 5 / density, [
                new Raster("Hh", everyHour * BarsToSecs),
                new Raster("Mm", every5Minute * BarsToSecs, FadeLabels),
                new Raster("", everyMinute * BarsToSecs, FadeLabels, FadeLines)
            ]),
            new ScaleRange(5 / density, 20 / density, [
                new Raster("Hh", every4Hours * BarsToSecs),
                new Raster("Hh", everyHour * BarsToSecs, FadeLabels)
            ]),

            new ScaleRange(20 / density, 100 / density, [
                new Raster("Dd", everyDay * BarsToSecs),
                new Raster("Hh", every4Hours * BarsToSecs, FadeLabels, FadeLines)
            ]),

            new ScaleRange(100 / density, 500 / density, [
                new Raster("Dd", every10Days * BarsToSecs),
                new Raster("Dd", everyDay * BarsToSecs, FadeLabels)
            ]),

            new ScaleRange(500 / density, 750 / density, [
                new Raster("Dd", every10Days * BarsToSecs),
                new Raster("", everyDay * BarsToSecs, FadeLabels, FadeLines)
            ]),
            new ScaleRange(750 / density, 2000 / density, [
                new Raster("Dd", every10Days * BarsToSecs)
            ]),
            new ScaleRange(2000 / density, 40000 / density, [
                new Raster("Dd", every10Days * BarsToSecs, true)
            ]),
            new ScaleRange(2000 / density, 400000 / density, [
                new Raster("Dd", every10Days *10 * BarsToSecs )
            ])
        ];
    }
}