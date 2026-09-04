#nullable enable
using ImGuiNET;
using T3.Editor.App;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using T3.SystemUi;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// If active renders a small input field above a symbolChildUi. Handles its state 
/// </summary>
internal static class RenamingOperator
{
    public static void OpenForChildUi(SymbolUi.Child symbolChildUi)
    {
        _nextFocusedInstanceId = symbolChildUi.SymbolChild.Id;
    }

    private static Guid _nextFocusedInstanceId = Guid.Empty;

    /// <param name="opWidthOnCanvas">Width of the renamed node in canvas units. Falls back to the childUi size if not given.</param>
    public static void Draw(ProjectView projectView, float opWidthOnCanvas = 0)
    {
        var justOpened = false;

        var renameTriggered = _nextFocusedInstanceId != Guid.Empty;
            
        if (_focusedInstanceId == Guid.Empty)
        {
            if ((renameTriggered || ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows) || ImGui.IsWindowFocused()) 
                && !ImGui.IsAnyItemActive() 
                && !ImGui.IsAnyItemFocused() 
                && (renameTriggered || ImGui.IsKeyPressed(Key.Return.ToImGuiKey())) // TODO: Should be keyboard action 
                && string.IsNullOrEmpty(FrameStats.Current.OpenedPopUpName))
            {
                var selectedInstances = projectView.NodeSelection.GetSelectedNodes<SymbolUi.Child>().ToList();
                if (_nextFocusedInstanceId != Guid.Empty)
                {
                    _focusedInstanceId = _nextFocusedInstanceId;
                    _nextFocusedInstanceId = Guid.Empty;
                    justOpened = true;
                    ImGui.SetKeyboardFocusHere();

                }
                else if (selectedInstances.Count == 1)
                {
                    _focusedInstanceId = selectedInstances[0].SymbolChild.Id;
                    justOpened = true;
                    ImGui.SetKeyboardFocusHere();
                }
            }
        }


        if (_focusedInstanceId == Guid.Empty)
            return;

        var parentSymbolUi = projectView.CompositionInstance?.GetSymbolUi();
        if (parentSymbolUi == null || !parentSymbolUi.ChildUis.TryGetValue(_focusedInstanceId, out var symbolChildUi))
        {
            Log.Error("canceling rename overlay of no longer valid selection");
            _focusedInstanceId = Guid.Empty;
            return;
        }

        var symbolChild = symbolChildUi.SymbolChild;

        var canvas = projectView.GraphView.Canvas;
        var positionInScreen = canvas.TransformPosition(symbolChildUi.PosOnCanvas);

        ImGui.SetCursorScreenPos(positionInScreen + Vector2.One);

        var text = symbolChild.Name;
        var fieldWidth = ComputeFieldWidth(text, positionInScreen.X, opWidthOnCanvas > 0 ? opWidthOnCanvas : symbolChildUi.Size.X, canvas);

        if (CustomComponents.DrawInputFieldWithPlaceholder("Untitled",
                                                           ref text,
                                                           fieldWidth,
                                                           false,
                                                           ImGuiInputTextFlags.AutoSelectAll))
        {
            symbolChild.Name = text;
            parentSymbolUi.FlagAsModified();
            
        }
            
        if (!justOpened && (ImGui.IsItemDeactivated() || ImGui.IsKeyPressed(Key.Return.ToImGuiKey())))
        {
            _focusedInstanceId = Guid.Empty;
        }
    }

    public static bool IsOpen => _focusedInstanceId != Guid.Empty;

    /// <summary>
    /// Grows with the edited name so long names stay readable, but never gets narrower than the node
    /// and stops at the right window edge (unless the node itself already reaches beyond it).
    /// </summary>
    private static float ComputeFieldWidth(string text, float positionInScreenX, float opWidthOnCanvas, ScalableCanvas canvas)
    {
        var opWidthOnScreen = canvas.TransformDirection(new Vector2(opWidthOnCanvas, 0)).X;
        var minWidth = MathF.Max(MinFieldWidth * T3Ui.UiScaleFactor, opWidthOnScreen);

        var distanceToRightEdge = ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - positionInScreenX - RightEdgePadding * T3Ui.UiScaleFactor;
        var maxWidth = MathF.Max(minWidth, distanceToRightEdge);

        var textWidth = string.IsNullOrEmpty(text) ? 0 : ImGui.CalcTextSize(text).X;
        var requiredWidth = textWidth + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetFontSize();

        // The helper reserves this gutter for form layouts, so add it back to get the requested field width.
        return Math.Clamp(requiredWidth, minWidth, maxWidth) + FormInputs.ParameterSpacing;
    }

    private const float MinFieldWidth = 120;
    private const float RightEdgePadding = 8;

    private static Guid _focusedInstanceId;
}