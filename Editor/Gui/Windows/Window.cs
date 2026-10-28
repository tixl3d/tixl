#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// A base class that unifies how windows are rendered and persisted
/// </summary>
internal abstract class Window
{
    internal bool AllowMultipleInstances = false;
    protected bool MayNotCloseLastInstance = false;

    protected ImGuiWindowFlags WindowFlags;
    protected Vector2 WindowPaddingOverride = new Vector2(-1);

    /// <summary>Initial window size (unscaled). Multiplied by <see cref="T3Ui.UiScaleFactor"/> when the window first appears.</summary>
    protected Vector2 WindowSizeOverride = new Vector2(550, 450);

    /// <summary>Center the window in the main viewport whenever it appears. Re-centers on every reopen
    /// (used by the version-welcome popup so it lands centered regardless of where it was left).</summary>
    protected bool CenterOnAppearing;

    /// <summary>Override the window's inner-content backdrop (ImGui ChildBg). Null inherits the shared
    /// Panel Background; set it to give a window its own themeable background (e.g. the graph canvas).</summary>
    protected virtual Color? InnerBackgroundColor => null;
    protected string? MenuTitle;


    internal abstract IReadOnlyList<Window> GetInstances();

    protected string WindowDisplayTitle => Config.Title;

    public void Draw()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.WindowBackground.Rgba);
        if (AllowMultipleInstances)
        {
            DrawAllInstances();
        }
        else
        {
            
            DrawOneInstance();
        }
        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Focuses the window on its next Begin. Unlike calling <c>ImGui.SetWindowFocus()</c> inside
    /// <see cref="DrawContent"/>, this also surfaces a window that is currently an unselected dock
    /// tab — there Begin returns false and the content never draws. The request repeats for a few
    /// frames because on a window's creation frame the focus can be consumed before its docking
    /// is resolved.
    /// </summary>
    internal virtual void RequestWindowFocus()
    {
        _focusRequestFramesLeft = 20;
    }

    /** The window holding the keyboard focus, or the last one that did. */
    internal static Window? FocusedWindow { get; private set; }

    /// <summary>
    /// Like <see cref="RequestWindowFocus"/>, but waits until a layout change has settled. Windows that appear with
    /// the new layout grab the focus on their first frame.
    /// </summary>
    internal void RequestFocusAfterLayoutChange()
    {
        _windowToFocusAfterLayoutChange = this;
        _layoutChangeFocusFrame = ImGui.GetFrameCount() + LayoutChangeSettleFrames;
    }

    private void DrawOneInstance()
    {
        UpdateBeforeDraw();

        if (!Config.Visible)
            return;

        if (!_wasVisible)
        {
            var size = WindowSizeOverride * T3Ui.UiScaleFactor;
            ImGui.SetNextWindowSize(size);

            if (CenterOnAppearing)
            {
                var viewport = ImGui.GetMainViewport();
                ImGui.SetNextWindowPos(viewport.GetCenter() - size * 0.5f);
            }

            _wasVisible = true;
        }

        if (_windowToFocusAfterLayoutChange == this && ImGui.GetFrameCount() >= _layoutChangeFocusFrame)
        {
            _windowToFocusAfterLayoutChange = null;
            RequestWindowFocus();
        }

        if (_focusRequestFramesLeft > 0)
        {
            ImGui.SetNextWindowFocus();
        }

        var borderWidthForFloatingWindows = _wasDockedLastFrame ? 0 : 2;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, borderWidthForFloatingWindows);

        var mayNotClose = MayNotCloseLastInstance && GetVisibleInstanceCount() == 1;

        // Draw output window
        var isVisible = mayNotClose
            ? ImGui.Begin(WindowDisplayTitle, WindowFlags)
            : ImGui.Begin(WindowDisplayTitle, ref Config.Visible, WindowFlags);

        if (isVisible)
        {
            _focusRequestFramesLeft = 0;
        }
        else if (_focusRequestFramesLeft > 0)
        {
            _focusRequestFramesLeft--;
        }

        if (isVisible)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,
                WindowPaddingOverride.X > -1
                    ? WindowPaddingOverride
                    : T3Style.WindowPaddingForWindows
            );

            _wasDockedLastFrame = ImGui.IsWindowDocked();

            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
                FocusedWindow = this;

            // Prevent window header from becoming invisible 
            var windowPos = ImGui.GetWindowPos();
            if (windowPos.X <= 0) windowPos.X = 0;
            if (windowPos.Y <= 0) windowPos.Y = 0;
            ImGui.SetWindowPos(windowPos);

            var preventMouseScrolling = T3Ui.MouseWheelFieldWasHoveredLastFrame
                ? ImGuiWindowFlags.NoScrollWithMouse
                : ImGuiWindowFlags.None;

            // Draw child to prevent imgui window dragging
            {
                // Windows that want a distinct content backdrop (e.g. the graph editor canvas) override
                // InnerBackgroundColor; the rest inherit the shared Panel Background (style ChildBg).
                var innerBackground = InnerBackgroundColor;
                if (innerBackground.HasValue)
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, innerBackground.Value.Rgba);

                ImGui.BeginChild("inner", ImGui.GetContentRegionAvail(),
                    ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysUseWindowPadding,
                    ImGuiWindowFlags.NoMove | preventMouseScrolling | WindowFlags);

                var idBefore = ImGui.GetID(0);

                DrawContent();

                var idAfter = ImGui.GetID(0);
                if (idBefore != idAfter)
                    Log.Warning($"Inconsistent ImGui-ID after rendering {this}  {idBefore} != {idAfter}");

                ImGui.EndChild();

                if (innerBackground.HasValue)
                    ImGui.PopStyleColor();
            }

            ImGui.PopStyleVar(); // WindowPadding
        }

        // End must be called unconditionally after Begin, even when Begin
        // returns false (collapsed/hidden). ImGui 1.91 enforces this.
        ImGui.End();

        if (!Config.Visible)
        {
            // Re-arm the appearing logic so a re-opened window snaps back to its centered position.
            if (CenterOnAppearing)
                _wasVisible = false;

            Close();
        }

        // if (hideFrameBorder)
        //     ImGui.PopStyleVar();

        ImGui.PopStyleVar();
    }

    private int GetVisibleInstanceCount()
    {
        var count = 0;
        foreach (var x in GetInstances())
        {
            if (x.Config.Visible)
                count++;
        }

        return count;
    }

    private bool _wasVisible;
    private int _focusRequestFramesLeft;

    /** The new ImGui layout is loaded on the next frame, and its dock nodes and tabs take another frame or two. */
    private const int LayoutChangeSettleFrames = 3;

    private static Window? _windowToFocusAfterLayoutChange;
    private static int _layoutChangeFocusFrame;

    internal void DrawMenuItemToggle()
    {
        if (AllowMultipleInstances)
        {
            var menuTitle = string.IsNullOrEmpty(MenuTitle)
                ? $"New {Config.Title}"
                : MenuTitle;
            if (CustomComponents.DrawMenuItem(menuTitle.GetHashCode(), Icon.None, menuTitle, reserveIconColumn: false))
            {
                AddAnotherInstance();
            }
        }
        else
        {
            var menuTitle = string.IsNullOrEmpty(MenuTitle)
                ? Config.Title
                : MenuTitle;

            if (CustomComponents.DrawMenuItem(menuTitle.GetHashCode(), Icon.None, menuTitle, isChecked: Config.Visible, reserveIconColumn: false,
                                              state: Config.Visible ? CustomComponents.ButtonStates.Emphasized : CustomComponents.ButtonStates.Default))
            {
                Config.Visible = !Config.Visible;
            }

            if (!Config.Visible)
                Close();
        }
    }

    protected abstract void DrawContent();

    private void UpdateBeforeDraw()
    {
    }

    readonly List<Window> _instancesToDraw = [];

    private void DrawAllInstances()
    {
        _instancesToDraw.Clear();
        IReadOnlyList<Window> instances = GetInstances();

        //This replaces the ToArray but is GC free, replace foreach as in eventually adds an enumerator allocation
        for (int i = 0; i < instances.Count; i++)
        {
            _instancesToDraw.Add(instances[i]);
        }

        for (int i = 0; i < _instancesToDraw.Count; i++)
        {
            _instancesToDraw[i].DrawOneInstance();
        }
    }

    protected virtual void Close()
    {
    }

    /// <summary>
    /// Returns the lowest-numbered title not yet taken by an instance. A shared title would
    /// make two instances draw into the same ImGui window.
    /// </summary>
    protected static string GetUnusedInstanceTitle(string prefix, IReadOnlyList<Window> instances)
    {
        for (var number = 0;; number++)
        {
            var title = prefix + number;
            var isTaken = false;
            for (var i = 0; i < instances.Count; i++)
            {
                if (instances[i].Config.Title != title)
                    continue;

                isTaken = true;
                break;
            }

            if (!isTaken)
                return title;
        }
    }

    protected virtual void AddAnotherInstance()
    {
    }

    internal sealed class WindowConfig
    {
        // Public for json-serialization
        public string Title = ""; // This property name is unfortunate because it's used for serialization
        public bool Visible;
    }

    internal WindowConfig Config = new();


    /** We need to set border width before drawing, but only know if docked after :/ */
    private bool _wasDockedLastFrame;
}