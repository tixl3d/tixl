#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.OpUis;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.Variations;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui;

internal static class ParameterPopUp
{
    public static void HandleOpenParameterPopUp(SymbolUi.Child childUi, Instance instance, OpUi.CustomUiResult customUiResult, ImRect nodeScreenRect)
    {
        var activatedWithLeftMouse = ImGui.IsItemHovered()
                                     && ImGui.IsMouseReleased(ImGuiMouseButton.Left)
                                     && ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 0).Length() < UserSettings.Config.ClickThreshold
                                     && !ParameterWindow.IsAnyInstanceVisible()
                                     && !ImGui.GetIO().KeyShift; // allow double click to open

        var activatedWithMiddleMouse = ImGui.IsItemHovered()
                                       && ImGui.IsMouseReleased(ImGuiMouseButton.Middle)
                                       && ImGui.GetMouseDragDelta(ImGuiMouseButton.Middle, 0).Length() < UserSettings.Config.ClickThreshold;

        var activationRequested = NodeIdRequestedForParameterWindowActivation == instance.SymbolChildId
                                  && !ParameterWindow.IsAnyInstanceVisible();

        if ((activatedWithLeftMouse || activatedWithMiddleMouse || activationRequested)
            && (customUiResult & OpUi.CustomUiResult.PreventOpenParameterPopUp) == 0
           )
        {
            Open(instance);
        }
    }

    public static void Open(Instance instance)
    {
        _selectedInstance = instance;

        NodeIdRequestedForParameterWindowActivation = Guid.Empty;

        _isOpen = true;
        _lastRequiredHeight = 0;
        _focusDelayCount = 3;
    }

    private static Vector2 DefaultWindowSize => new Vector2(310, 250) * T3Ui.UiScaleFactor;

    private static void Close()
    {
        _isOpen = false;
    }

    public static void DrawParameterPopUp(ProjectView graphWindow)
    {
        if (!_isOpen || _selectedInstance == null || graphWindow.CompositionInstance == null)
            return;

        var symbolUi = _selectedInstance.GetSymbolUi();
        var compositionSymbolUi = graphWindow.CompositionInstance.GetSymbolUi();
        if (!compositionSymbolUi.ChildUis.TryGetValue(_selectedInstance.SymbolChildId, out var symbolChildUi))
        {
            Close();
            return;
        }

        if (!graphWindow.NodeSelection.IsAnythingSelected())
        {
            Close();
            return;
        }

        var nodeScreenRect = graphWindow.GraphView.Canvas.TransformRect(ImRect.RectWithSize(symbolChildUi.PosOnCanvas, symbolChildUi.Size));
        var horizontalOffset = 25 * graphWindow.GraphView.Canvas.Scale.X;
        var screenPos = new Vector2(nodeScreenRect.Min.X + horizontalOffset, nodeScreenRect.Max.Y + 5);
        var height = _lastRequiredHeight.Clamp(MinHeight, DefaultWindowSize.Y);
        ImGui.SetNextWindowPos(screenPos);

        var preventTabbingIntoUnfocusedStringInputs = ImGui.IsAnyItemActive() ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoNavInputs;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.One * 2);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 2);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 2);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4);
        if (ImGui.BeginChild("Popup",
                             new Vector2(DefaultWindowSize.X, height),
                             true,
                             preventTabbingIntoUnfocusedStringInputs
                             | ImGuiWindowFlags.NoScrollWithMouse
                             | ImGuiWindowFlags.NoScrollbar))
        {
            if (ImGui.IsKeyDown(ImGuiKey.Escape))
            {
                Close();
            }
            FrameStats.Current.OpenedPopUpName = ParameterPopUpName;
            ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetWindowPos(),
                                                    ImGui.GetWindowPos() + ImGui.GetWindowSize(),
                                                    UiColors.BackgroundFull);

            FormInputs.SetIndent(20);

            if (_focusDelayCount >= 0)
            {
                ImGui.SetWindowFocus();
                _focusDelayCount--;
            }
            else if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows))
            {
                Close();
            }
            
            FrameStats.Current.OpenedPopupHovered = ImGui.IsItemHovered();


            // Toolbar
            {
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2,4) * T3Ui.UiScaleFactor);
                ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);
                ImGui.SetCursorPos( new Vector2(5,5));
                CustomComponents.AddSegmentedIconButton(ref _viewMode, _modeIcons);
                ImGui.SameLine();
                
                var spaceBetweenViewIconsAndActions = ImGui.GetContentRegionAvail().X 
                                                      - 3 * (ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X);
                
                // Bypass
                {
                    if (symbolChildUi.SymbolChild.IsBypassable())
                    {
                        ImGui.SameLine(0, spaceBetweenViewIconsAndActions);
                        var isBypassed = symbolChildUi.SymbolChild.IsBypassed;
                        
                        if (CustomComponents.ToggleTwoIconsButton(ref isBypassed, 
                                                                  Icon.OperatorBypassOff,
                                                                  Icon.OperatorBypassOn, 
                                                                  CustomComponents.ButtonStates.NeedsAttention, 
                                                                  CustomComponents.ButtonStates.Dimmed))
                        {
                            UndoRedoStack.AddAndExecute(isBypassed
                                                            ? new ChangeInstanceBypassedCommand(symbolChildUi.SymbolChild, true)
                                                            : new ChangeInstanceBypassedCommand(symbolChildUi.SymbolChild, false));
                        }
                        ImGui.SameLine();
                    }
                }
                
                // Disable
                {
                    ImGui.SameLine();
                    var isDisabled = symbolChildUi.SymbolChild.IsDisabled;
                    if (CustomComponents.ToggleIconButton(ref isDisabled, 
                                                          Icon.OperatorDisabled, 
                                                          Vector2.Zero, 
                                                          CustomComponents.ButtonStates.NeedsAttention))
                    {
                        UndoRedoStack.AddAndExecute(isDisabled
                                                        ? new ChangeInstanceIsDisabledCommand(symbolChildUi, true)
                                                        : new ChangeInstanceIsDisabledCommand(symbolChildUi, false));
                    }
                }
                
                // Pin to background
                {
                    ImGui.SameLine();
                    var isPinned = _selectedInstance == graphWindow.GraphImageBackground.OutputInstance;
                    if (CustomComponents.ToggleIconButton(ref isPinned, Icon.PlayOutput, Vector2.Zero))
                    {
                        if (isPinned)
                            graphWindow.SetBackgroundOutput(_selectedInstance);
                    }
                }
                
                ImGui.PopStyleVar();
                ImGui.PopStyleColor();
            }

            // Content
            switch (_viewMode)
            {
                case ViewModes.Parameters:
                    
                    ImGui.BeginChild("Scrolling", new Vector2(DefaultWindowSize.X, height - 20 ), false);
                    CustomComponents.HandleDragScrolling(_parameterPopUpReference);
                    ImGui.PushFont(Fonts.FontSmall);
                    ParameterWindow.DrawParameters(_selectedInstance, symbolUi, symbolChildUi, compositionSymbolUi, hideNonEssentials: true);
                    FormInputs.AddVerticalSpace();
                    ImGui.PopFont();
                    _lastRequiredHeight = ImGui.GetCursorPosY() + ImGui.GetFrameHeight() + 0;
                    ImGui.Dummy(new Vector2(10,10));
                    ImGui.EndChild();
                    break;
                
                case ViewModes.Presets:
                    var allWindows = WindowManager.GetAllWindows();
                    foreach (var w in allWindows)
                    {
                        if (w is not VariationsWindow variationsWindow)
                            continue;

                        ImGui.BeginChild("Scrolling", DefaultWindowSize, false, 
                                         ImGuiWindowFlags.NoScrollWithMouse|
                                         ImGuiWindowFlags.NoScrollbar |
                                         ImGuiWindowFlags.NoBackground);
                        variationsWindow.DrawWindowContent(hideHeader: true);
                        _lastRequiredHeight = DefaultWindowSize.Y;
                        ImGui.GetWindowDrawList().AddRect(ImGui.GetWindowPos()+ Vector2.One, ImGui.GetWindowPos() + ImGui.GetWindowSize() - new Vector2(2,27), UiColors.WindowBackground);

                        ImGui.EndChild();
                        break;
                    }

                    break;
                case ViewModes.Help:
                    ImGui.BeginChild("Scrolling", 
                                     new Vector2(DefaultWindowSize.X, 
                                                 height), 
                                     false,
                                     ImGuiWindowFlags.NoBackground
                                    );
                    FormInputs.AddVerticalSpace();
                    OperatorHelp.DrawHelp(symbolUi);
                    FormInputs.AddVerticalSpace();
                    _lastRequiredHeight = ImGui.GetCursorPosY() + ImGui.GetFrameHeight();
                    // Draw a small border to separate from graph
                    ImGui.GetWindowDrawList().AddRect(ImGui.GetWindowPos()+ Vector2.One, ImGui.GetWindowPos() + ImGui.GetWindowSize() - new Vector2(2,2), UiColors.WindowBackground);
                    ImGui.EndChild();
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            //
        }


        
        ImGui.EndChild();
        ImGui.PopStyleVar(3);
    }

    private enum ViewModes
    {
        Parameters,
        Presets,
        Help,
    }

    private static readonly List<Icon> _modeIcons = new()
                                                        {
                                                            Icon.ViewList,
                                                            Icon.Presets,
                                                            Icon.HelpOutline
                                                        };

    private static float _lastRequiredHeight;
    private const float MaxHeight = 280;
    private const float MinHeight = 50;

    private static bool _isOpen;
    private static int _focusDelayCount;
    private static object _parameterPopUpReference = new();

    private static ViewModes _viewMode = ViewModes.Parameters;
    private static Instance? _selectedInstance;
    private const string ParameterPopUpName = "parameterContextPopup";
    public static Guid NodeIdRequestedForParameterWindowActivation;
}