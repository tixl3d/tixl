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

    private void DrawOneInstance()
    {
        UpdateBeforeDraw();

        if (!Config.Visible)
            return;

        if (!_wasVisible)
        {
            ImGui.SetNextWindowSize(WindowSizeOverride * T3Ui.UiScaleFactor);
            _wasVisible = true;
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
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,
                WindowPaddingOverride.X > -1
                    ? WindowPaddingOverride
                    : T3Style.WindowPaddingForWindows
            );

            _wasDockedLastFrame = ImGui.IsWindowDocked();

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