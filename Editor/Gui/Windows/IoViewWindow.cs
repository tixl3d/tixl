using T3.Core.DataTypes.DataSet;
using T3.Editor.Gui.OutputUi;

// ReSharper disable PossibleMultipleEnumeration

namespace T3.Editor.Gui.Windows;

[HelpUiID("IoView")]
internal sealed class IoViewWindow : Window
{
    internal IoViewWindow()
    {
        Config.Title = "IO Events";
    }

    protected override void DrawContent()
    {
        _canvas.Draw(DataRecording.ActiveRecordingSet);
    }

    internal override List<Window> GetInstances()
    {
        return new List<Window>();
    }

    private readonly DataSetViewCanvas _canvas = new();
}