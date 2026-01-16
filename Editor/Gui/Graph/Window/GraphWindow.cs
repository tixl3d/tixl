#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Editor.Gui.Dialogs;
using T3.Editor.Gui.Hub;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.TransformGizmos;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using MagGraphView = T3.Editor.Gui.MagGraph.Ui.MagGraphView;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Window;

internal sealed class GraphWindow : Windows.Window
{
    #region Window implementation --------------------
    public GraphWindow()
    {
        Config.Title = LayoutHandling.GraphPrefix + _instanceNumber;
        Config.Visible = true;

        AllowMultipleInstances = true;
        MayNotCloseLastInstance = true;
        Config.Visible = true;
        WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        _instanceNumber++;
        GraphWindowInstances.Add(this);
    }

    private readonly int _instanceNumber;

    internal override IReadOnlyList<Gui.Windows.Window> GetInstances() => GraphWindowInstances;
    internal static readonly List<GraphWindow> GraphWindowInstances = [];

    public void SetWindowToNormal()
    {
        WindowFlags &= ~(ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoMove |
                         ImGuiWindowFlags.NoResize);
    }

    protected override void AddAnotherInstance()
    {
        // ReSharper disable once ObjectCreationAsStatement
        new GraphWindow();
    }
    #endregion

    #region Handling project view ----------------------
    public ProjectView? ProjectView { get; private set; }

    /// <summary>
    /// Initialize <see cref="ProjectView"/> for loading a project 
    /// </summary>
    internal bool TrySetToProject(OpenedProject project, bool tryRestoreViewArea = true)
    {
        if (!project.Package.HasHome)
        {
            Log.Error("Project has no home.");
            return false;
        }

        ProjectView = UserSettings.Config.GraphStyle == UserSettings.GraphStyles.Magnetic
                          ? MagGraphView.CreateWithComponents(project)
                          : Legacy.GraphView.CreateWithComponents(project);

        ProjectView.SetAsFocused();
        // ProjectView = MagGraphCanvas.CreateWithComponents(project);
        // ProjectView = Legacy.GraphCanvas.CreateWithComponents(project);
        ProjectView.OnCompositionChanged += CompositionChangedHandler;

        var rootInstance = ProjectView.RootInstance;
        var rootSymbolChildId = rootInstance.SymbolChildId;
        var rootPath = rootInstance.InstancePath;
        var startPath = rootPath;
        if (project.Package.OverrideHomeGuid == Guid.Empty)
        {
            var opId = UserSettings.GetLastOpenOpForWindow(Config.Title);
            if (opId != Guid.Empty && opId != rootSymbolChildId)
            {
                if (rootInstance.SymbolChild.SearchForChild(opId, out _, out var path))
                {
                    startPath = path;
                }
            }
        }

        const ScalableCanvas.Transition transition = ScalableCanvas.Transition.JumpIn;
        if (!ProjectView.TrySetCompositionOp(startPath, transition, tryRestoreViewArea: tryRestoreViewArea)
            && !ProjectView.TrySetCompositionOp(rootPath, transition, tryRestoreViewArea: tryRestoreViewArea))
        {
            Log.Warning("Can't set composition op");
            return false;
        }

        _focusOnNextFrame = true;
        return true;
    }

    /** Called when view is closed (e.g. when jumping to hub) or changed */
    public void CloseView()
    {
        if (ProjectView != null)
            ProjectView.OnCompositionChanged -= CompositionChangedHandler;

        _reinitHubView = true;
        ProjectView = null;
    }

    /** Called when windows is closed */
    protected override void Close()
    {
        if (ProjectView == null)
            return;

        ProjectView.OnCompositionChanged -= CompositionChangedHandler;

        ProjectView.Close();
        GraphWindowInstances.Remove(this);
    }

    private bool _reinitHubView = true;
    private bool _focusOnNextFrame;

    // private void FocusRequested()
    // {
    //     TakeFocus();
    //     _focusOnNextFrame = true;
    // }

    private void CompositionChangedHandler(ProjectView _, Guid instanceId)
    {
        UserSettings.SaveLastViewedOpForWindow(Config.Title, instanceId);
    }
    #endregion

    #region Drawing ---------------------------------------
    protected override void DrawContent()
    {
        if (ProjectView == null)
        {
            ProjectHub.Draw(this, _reinitHubView);
            _reinitHubView = false;
            return;
        }

        if (FitViewToSelectionHandling.FitViewToSelectionRequested)
            GraphCanvas?.FocusViewToSelection();

        if (ProjectView.InstView == null)
            return;

        var windowContentHeight = (int)ImGui.GetWindowHeight();

        if (UserSettings.Config.ShowTimeline)
        {
            ProjectView.TimeLineCanvas.FoldingHeight.DrawSplit(out windowContentHeight);
        }

        //ImageBackgroundFading.HandleImageBackgroundFading(ProjectView.GraphImageBackground, out var backgroundImageOpacity);
        var backgroundImageOpacity = 1f;
        ImGui.BeginChild("##graphbackground", new Vector2(0, windowContentHeight), false,
                         ImGuiWindowFlags.NoScrollbar
                         | ImGuiWindowFlags.NoMove
                         | ImGuiWindowFlags.NoScrollWithMouse
                         | ImGuiWindowFlags.NoDecoration
                         | ImGuiWindowFlags.NoTitleBar
                         | ImGuiWindowFlags.NoBackground
                         | ImGuiWindowFlags.ChildWindow);
        {
        }
        ImGui.EndChild();
        ImGui.SetCursorPos(Vector2.Zero);

        ImGui.BeginChild("##graph", new Vector2(0, windowContentHeight), false,
                         ImGuiWindowFlags.NoScrollbar
                         | ImGuiWindowFlags.NoMove
                         | ImGuiWindowFlags.NoScrollWithMouse
                         | ImGuiWindowFlags.NoDecoration
                         | ImGuiWindowFlags.NoTitleBar
                         | ImGuiWindowFlags.NoBackground
                         | ImGuiWindowFlags.ChildWindow);
        {
            // ImageBackground
            ProjectView.GraphImageBackground.Draw(backgroundImageOpacity);

            var graphHiddenWhileInteractiveWithBackground = ProjectView.GraphImageBackground.IsActive && TransformGizmoHandling.IsDragging;
            if (!graphHiddenWhileInteractiveWithBackground)
            {
                var drawList = ImGui.GetWindowDrawList();
                DrawGraphContent(drawList);
            }
        }
        ImGui.EndChild();

        if (ProjectView == null)
            return;

        ProjectView.CheckDisposal();

        if (UserSettings.Config.ShowTimeline)
        {
            const int splitterWidth = 3;
            var availableRestHeight = ImGui.GetContentRegionAvail().Y;
            if (availableRestHeight <= splitterWidth)
            {
                //Log.Warning($"skipping rending of timeline because layout is inconsistent: only {availableRestHeight}px left.");
            }
            else
            {
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + splitterWidth - 1); // leave gap for splitter
                ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0);

                ImGui.BeginChild("##timeline", Vector2.Zero, false,
                                 ImGuiWindowFlags.NoResize
                                 | ImGuiWindowFlags.NoBackground
                                );
                {
                    if (ProjectView.CompositionInstance != null)
                        ProjectView.TimeLineCanvas.Draw(ProjectView.CompositionInstance, Playback.Current);
                }
                ImGui.EndChild();
                ImGui.PopStyleVar(1);
            }
        }

        if (UserSettings.Config.ShowMiniMap && GraphCanvas != null)
            UiElements.DrawMiniMap(ProjectView.InstView, GraphCanvas.Canvas);
    }

    private void DrawGraphContent(ImDrawListPtr drawList)
    {
        if (_focusOnNextFrame)
        {
            ImGui.SetWindowFocus();
            _focusOnNextFrame = false;
        }

        ImGui.SetScrollX(0);

        drawList.ChannelsSplit(2);

        // Draw Foreground content first
        drawList.ChannelsSetCurrent(1);
        {
            if (UserSettings.Config.ShowTitleAndDescription)
                GraphTitleAndBreadCrumbs.Draw(ProjectView);

            TourInteraction.Draw(ProjectView);

            // Breadcrumbs may have requested close...
            if (ProjectView == null)
                return;

            UiElements.DrawProjectControlToolbar(ProjectView);
        }

        // Draw content
        drawList.ChannelsSetCurrent(0);
        {
            ImageBackgroundFading.HandleGraphFading(ProjectView.GraphImageBackground, drawList, out var graphOpacity);

            var isGraphHidden = graphOpacity <= 0;
            if (!isGraphHidden && GraphCanvas != null)
            {
                GraphCanvas.BeginDraw(ProjectView.GraphImageBackground.IsActive,
                                      ProjectView.GraphImageBackground.HasInteractionFocus);

                GraphBookmarkNavigation.HandleForCanvas(ProjectView);

                ImGui.BeginGroup();
                ImGui.SetScrollY(0);

                if (!UserSettings.Config.FocusMode)
                    CustomComponents.DrawWindowFocusFrame();

                if (ImGui.IsWindowFocused())
                {
                    ProjectView?.SetAsFocused();
                }

                GraphCanvas.DrawGraph(drawList, graphOpacity);

                ImGui.EndGroup();

                if (ProjectView != null)
                    ParameterPopUp.DrawParameterPopUp(ProjectView);
            }
        }
        drawList.ChannelsMerge();

        // if (ProjectView?.InstView != null)
        //     _editDescriptionDialog.Draw(ProjectView.InstView.Symbol);
        //
    }
    #endregion

    private IGraphView? GraphCanvas => ProjectView?.GraphView;
    // private static readonly EditSymbolDescriptionDialog _editDescriptionDialog = new();
    // private static readonly EditTourPointsDialog _editTourPointsDialog = new();
}