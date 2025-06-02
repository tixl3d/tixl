using System.Diagnostics;
using ImGuiNET;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.MagGraph.Interaction;

internal static class OutputPicking
{
    internal static void Init(GraphUiContext context)
    {
        WindowContentExtend.GetLastAndReset();
    }

    internal static void Reset(GraphUiContext context)
    {
        _hoveredOutputId = Guid.Empty;
    }

    public static void DrawAdditionOutputSelector(GraphUiContext context)
    {
        Debug.Assert(context.ActiveItem != null);

        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 5);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.One * 4);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));

        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundFull.Fade(0.7f).Rgba);
        ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundFull.Fade(0.0f).Rgba);

        ImGui.SetNextWindowPos(ImGui.GetMousePos() + new Vector2(-10, -5), ImGuiCond.Appearing);

        if (ImGui.BeginPopup("pickOutput", ImGuiWindowFlags.Popup))
        {
            CustomComponents.HintLabel("Drag output...");
            WindowContentExtend.ExtendToLastItem();

            var appearing = ImGui.IsWindowAppearing();

            // checking hover rect because window hover was inconsistent
            var hovered2 = ImRect.RectWithSize(ImGui.GetWindowPos(), ImGui.GetWindowSize()).Contains(ImGui.GetMousePos());

            var selectedWithoutRelease = !appearing
                                         && ImGui.IsMouseDown(ImGuiMouseButton.Left)
                                         && !hovered2;

            foreach (var o in context.ActiveItem.SymbolUi!.OutputUis.Values)
            {
                // Skip if not additional outputs
                {
                    var isAdditional = true;
                    foreach (var visibleOutput in context.ActiveItem.OutputLines)
                    {
                        if (visibleOutput.Id != o.Id) continue;
                        isAdditional = false;
                        break;
                    }

                    if (!isAdditional)
                        continue;
                }

                var name = o.OutputDefinition.Name;
                var labelSize = ImGui.CalcTextSize(name);
                var width = MathF.Max(200 - 4, labelSize.X + 30);
                var buttonSize = new Vector2(width, ImGui.GetFrameHeight());

                var isActive = _hoveredOutputId == o.Id;// || (_hoveredOutputId == Guid.Empty && isFirst);

                ImGui.PushStyleColor(ImGuiCol.Button, isActive ? UiColors.BackgroundActive : UiColors.BackgroundButton.Rgba);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, isActive ? UiColors.BackgroundActive : UiColors.BackgroundButton.Rgba);
                ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f));
                ImGui.Button(name);
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(2);

                if (ImGui.IsItemHovered())
                {
                    _hoveredOutputId = o.Id;
                    isActive = true;
                }

                if (isActive
                    && (ImGui.IsItemClicked(ImGuiMouseButton.Left)
                        || ImGui.IsMouseReleased(ImGuiMouseButton.Left)
                        || selectedWithoutRelease
                       )
                   )
                {
                    var outputSlot = context.ActiveItem?.Instance?.Outputs.FirstOrDefault(os => os.Id == o.Id);
                    if (outputSlot == null)
                        break;

                    var tempConnection = new MagGraphConnection
                                             {
                                                 Style = MagGraphConnection.ConnectionStyles.Unknown,
                                                 SourcePos = context.ActiveItem.PosOnCanvas,
                                                 TargetPos = default,
                                                 SourceItem = context.ActiveItem,
                                                 TargetItem = null,
                                                 SourceOutput = outputSlot,
                                                 OutputLineIndex = 0,
                                                 VisibleOutputIndex = 0,
                                                 ConnectionHash = 0,
                                                 IsTemporary = true,
                                             };
                    context.TempConnections.Add(tempConnection);
                    context.ActiveSourceItem = context.ActiveItem;
                    context.ActiveSourceOutputId = outputSlot.Id;
                    context.DraggedPrimaryOutputType = outputSlot.ValueType;
                    context.StateMachine.SetState(GraphStates.DragConnectionEnd, context);
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.EndPopup();
        }
        else
        {
            // Reset on release?
            context.StateMachine.SetState(GraphStates.Default, context);
        }

        ImGui.PopStyleVar(5);
        ImGui.PopStyleColor(2);
    }

    private static Guid _hoveredOutputId = Guid.Empty;
}