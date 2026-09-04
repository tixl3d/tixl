using T3.Core.Animation;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.TimeLine.Raster;

/// <summary>
/// A small helper class that switches the drawing of time rasters depending on the
/// setting in <see cref="Playback"/> 
/// </summary>
internal sealed class TimeRasterSwitcher:IValueSnapAttractor
{
    public void Draw(TimeLineCanvas timelineCanvas)
    {
        var unitsPerSeconds = 1f;
        if(UserSettings.Config.TimeDisplayMode != TimeFormat.TimeDisplayModes.Bars)
        {
            switch (UserSettings.Config.TimeDisplayMode)
            {
                case TimeFormat.TimeDisplayModes.F30:
                    unitsPerSeconds = 30;
                    _standardRaster.EnableSnapping = true;
                    break;
                case TimeFormat.TimeDisplayModes.F60:
                    unitsPerSeconds = 60;
                    _standardRaster.EnableSnapping = true;
                    break;
            }
        }
        ActiveRaster?.Draw(timelineCanvas, unitsPerSeconds,0);
    }
    
    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        ActiveRaster.CheckForSnap(ref snapResult);
    }

    private AbstractTimeRaster ActiveRaster
    {
        get
        {
            switch (UserSettings.Config.TimeDisplayMode)
            {
                case TimeFormat.TimeDisplayModes.Bars:
                    return _beatRaster;
                case TimeFormat.TimeDisplayModes.Secs:
                    return _siTimeRaster;
                default:
                    return _standardRaster;
            }
        }
    }
        
    private readonly StandardValueRaster _standardRaster = new();
    private readonly BeatTimeRaster _beatRaster = new();
    private readonly SiTimeRaster _siTimeRaster = new();
}