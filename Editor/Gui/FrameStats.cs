using ImGuiNET;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui;

/// <summary>
/// A helper class that collects information duration the processing of a frame,
/// so they can be used in the next.   
/// </summary>
internal static class FrameStats
{
    internal static void CompleteFrame()
    {
        (Current, Last) = (Last, Current);
        Current.Clear();
        UpdatePulses();

        WindowLayoutChanged = HasChanged(ref _lastWindowLayoutCounter, LayoutHandling.ChangeCounter);
        SelectionChanged = ProjectView.Focused != null && 
                           HasChanged(ref _lastSelectionCounter, ProjectView.Focused.NodeSelection.ChangeCounter);
    }

    public static bool WindowLayoutChanged;
    public static bool SelectionChanged;

    private static bool HasChanged(ref int counter, int newCounter)
    {
        if (counter == newCounter)
            return false;

        counter = newCounter;
        return true;
    }

    internal static void AddHoveredId(Guid id)
    {
        Current.HoveredIds.Add(id);
    }

    internal static bool IsIdHovered(Guid id)
    {
        return Last.HoveredIds.Contains(id);
    }

    /// <summary>
    /// Flags an item for a cross-highlight flash, wherever it happens to be drawn. Lets one place (a hovered
    /// list row, a search hit, a validation warning) draw attention to an item elsewhere without the two
    /// sharing anything but its <see cref="Guid"/>. Read the amount with <see cref="GetPulse"/> and use it to
    /// mix a highlight color toward the item's own — <c>lerp(itemColor, highlight, GetPulse(id))</c>.
    /// <para>The amount is animated, not oscillating: a fresh pulse <b>flashes</b> to <see cref="PulseFlash"/>
    /// and settles to <see cref="PulseHold"/>; calling it every frame (a live hover) holds there; once the
    /// calls stop it fades to 0 within ~0.5 s. No explicit clear.</para>
    /// </summary>
    internal static void PulseItemWithId(Guid id)
    {
        _pulsedThisFrame.Add(id);
    }

    /// <summary>The highlight-mix amount in [0, <see cref="PulseFlash"/>] for <paramref name="id"/>; 0 if it isn't pulsing.</summary>
    internal static float GetPulse(Guid id)
    {
        return _pulseValues.GetValueOrDefault(id);
    }

    private const float PulseFlash = 0.8f;      // the initial flash
    private const float PulseHold = 0.4f;       // steady level while it keeps being pulsed
    private const float PulseRatePerSecond = 0.8f; // covers the 0.4 flash→hold drop (and hold→0 fade) in ~0.5 s

    private static readonly Dictionary<Guid, float> _pulseValues = new();
    private static HashSet<Guid> _pulsedThisFrame = [];
    private static HashSet<Guid> _pulsedLastFrame = [];
    private static readonly List<Guid> _pulseKeys = [];

    // Advances every live pulse once per frame toward its target: PulseHold while still being pulsed, 0 once
    // it stops. A pulse that wasn't active last frame flashes to PulseFlash first, then eases down.
    private static void UpdatePulses()
    {
        var step = Math.Clamp(ImGui.GetIO().DeltaTime, 0f, 0.1f) * PulseRatePerSecond;

        foreach (var id in _pulsedThisFrame)
        {
            if (!_pulsedLastFrame.Contains(id))
                _pulseValues[id] = PulseFlash; // rising edge
            else if (!_pulseValues.ContainsKey(id))
                _pulseValues[id] = PulseHold;
        }

        _pulseKeys.Clear();
        _pulseKeys.AddRange(_pulseValues.Keys);
        foreach (var id in _pulseKeys)
        {
            var target = _pulsedThisFrame.Contains(id) ? PulseHold : 0f;
            var v = _pulseValues[id];
            v = v > target ? MathF.Max(target, v - step) : MathF.Min(target, v + step);
            if (v <= 0.001f && target <= 0f)
                _pulseValues.Remove(id);
            else
                _pulseValues[id] = v;
        }

        (_pulsedLastFrame, _pulsedThisFrame) = (_pulsedThisFrame, _pulsedLastFrame);
        _pulsedThisFrame.Clear();
    }

    internal sealed class Stats
    {
        internal bool HasKeyframesBeforeCurrentTime;
        internal bool HasKeyframesAfterCurrentTime;
        internal bool HasAnimatedParameters => HasKeyframesBeforeCurrentTime || HasKeyframesAfterCurrentTime;
        internal bool IsItemContextMenuOpen;
        internal bool IsModalDialogOpen;
        internal bool OpenedPopupCapturedMouse;
        internal bool OpenedPopupHovered;
        internal bool UiColorsChanged;
        internal bool SomethingWithTooltipHovered;
        internal bool UndoRedoTriggered;
        internal readonly HashSet<Guid> HoveredIds = [];
            
        /// <summary>
        /// This is reset on Frame start and can be useful for allow context menu to stay open even if a
        /// later context menu would also be opened. There is probably some ImGui magic to do this probably. 
        /// </summary>
        internal string OpenedPopUpName;

        internal void Clear()
        {
            HasKeyframesBeforeCurrentTime = false;
            HasKeyframesAfterCurrentTime = false;
            IsItemContextMenuOpen = false;
            IsModalDialogOpen = false;
            UiColorsChanged = false;
            OpenedPopUpName = string.Empty;
            OpenedPopupCapturedMouse = false;
            OpenedPopupHovered = false;
            SomethingWithTooltipHovered = false;
            UndoRedoTriggered = false;
            HoveredIds.Clear();
        }
    }

    internal static Stats Current = new();
    internal static Stats Last = new();
    
    private static int _lastSelectionCounter;
    private static int _lastWindowLayoutCounter;
}