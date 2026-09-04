namespace T3.Editor.UiModel.Selection;

internal static class FitViewToSelectionHandling
{
    /// <summary>
    /// This is called at the beginning of each frame.
    /// 
    /// For some events we have to use a frame delay mechanism so ui-elements can
    /// respond to updates in a controlled manner (I.e. when rendering the next frame) 
    /// </summary>
    internal static void ProcessNewFrame()
    {
        FitViewToSelectionRequested = _fitViewToSelectionTriggered;
        _fitViewToSelectionTriggered = false;
    }

    internal static void FitViewToSelection()
    {
        _fitViewToSelectionTriggered = true;
    }

    internal static bool FitViewToSelectionRequested { get; private set; }
    private static bool _fitViewToSelectionTriggered = false;        
}