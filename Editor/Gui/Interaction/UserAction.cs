#nullable enable

namespace T3.Editor.Gui.Interaction;

/// <summary>
/// UserAction represents single atomic commands that can be mapped to keyboard shortcuts
/// This enum list order will be the same as displayed in the shortcut editor. 
/// </summary>
internal enum UserActions
{
    None,
    TapBeatSync,
    TapBeatSyncMeasure,
    // General
    Undo,
    Redo,
    Save,
    FocusSelection,
    DeleteSelection,
    CopyToClipboard,
    PasteFromClipboard,
    PasteValues,
    New,

    // Playback
    PlaybackForward,
    PlaybackForwardHalfSpeed,
    PlaybackBackwards,
    PlaybackNextFrame,
    PlaybackPreviousFrame,
    PlaybackStop,
    PlaybackToggle,
    PlaybackJumpBack,
    PlaybackJumpToNextKeyframe,
    PlaybackJumpToPreviousKeyframe,
    PlaybackJumpToEndTime,
    PlaybackJumpToStartTime,

    // Timeline
    SetStartTime,
    SetEndTime,
    InsertKeyframe,
    InsertKeyframeWithIncrement,
    ToggleAnimationPinning,
    SplitSelectedOrHoveredClips,

    // Graph
    PinToOutputWindow,
    DisplayImageAsBackground,
    ClearBackgroundImage,
    Duplicate,
    LayoutSelection,
    ToggleDisabled,
    ToggleBypassed,
    AddAnnotation,
    AddComment,
    RenameChild,
    ToggleSnapshotControl,

    // Navigation
    ScrollLeft,
    ScrollRight,
    ScrollUp,
    ScrollDown,
    ZoomIn,
    ZoomOut,

    AlignSelectionLeft,
    
    NavigateBackwards,
    NavigateForward,
    SelectToAbove,
    SelectToRight,
    SelectToBelow,
    SelectToLeft,
    SearchGraph,
    OpenOperator,
    CloseOperator,

    //camera controls
    CameraLeft,
    CameraRight,
    CameraForward,
    CameraBackward,
    CameraUp,
    CameraDown,
    CameraReset,
    CameraFocusSelection,

    // Layout and window management
    ToggleAllUiElements,
    ToggleVariationsWindow,
    ToggleFullscreen,
    ToggleFocusMode,

    // Layouts (0-9)
    LoadLayout0, LoadLayout1, LoadLayout2, LoadLayout3, LoadLayout4,
    LoadLayout5, LoadLayout6, LoadLayout7, LoadLayout8, LoadLayout9,
    SaveLayout0, SaveLayout1, SaveLayout2, SaveLayout3, SaveLayout4,
    SaveLayout5, SaveLayout6, SaveLayout7, SaveLayout8, SaveLayout9,

    // Bookmarks (0-9)
    LoadBookmark0, LoadBookmark1, LoadBookmark2, LoadBookmark3, LoadBookmark4,
    LoadBookmark5, LoadBookmark6, LoadBookmark7, LoadBookmark8, LoadBookmark9,
    SaveBookmark0, SaveBookmark1, SaveBookmark2, SaveBookmark3, SaveBookmark4,
    SaveBookmark5, SaveBookmark6, SaveBookmark7, SaveBookmark8, SaveBookmark9,
    
    // Misc
    RenderScreenshot,
    RenderAnimation,
    
    // ReSharper disable once InconsistentNaming
    __Count,     
}

internal static class UserActionRegistry
{
    private static readonly HashSet<UserActions> _deferredActions = new();

    public static bool WasActionQueued(UserActions action)
    {
        return _deferredActions.Remove(action);
    }

    public static void QueueAction(UserActions action)
    {
        _deferredActions.Add(action);
    }
}