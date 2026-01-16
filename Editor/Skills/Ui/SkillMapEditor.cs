#nullable enable

using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Skills.Data;
using T3.Editor.Skills.Training;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Skills.Ui;

internal static class SkillMapEditor
{
    internal static void ShowNextFrame()
    {
        _isOpen = true;
    }

    internal static void Draw()
    {
        if (!_isOpen)
            return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1);
        ImGui.SetNextWindowSize(new Vector2(500, 500) * T3Ui.UiScaleFactor, ImGuiCond.Once);
        if (ImGui.Begin("Edit skill map", ref _isOpen))
        {
            // ImGui.BeginChild("LevelList", new Vector2(120 * T3Ui.UiScaleFactor, 0));
            // {
            //     ImGui.Indent(10);
            //
            //     for (var index = 0; index < SkillMapData.Data.Topics.Count; index++)
            //     {
            //         var t = SkillMapData.Data.Topics[index];
            //         ImGui.PushID(index);
            //
            //         if (ImGui.Selectable($"{t.Title}", _selectedTopics.Contains(t)))
            //         {
            //             _selectedTopics.Clear();
            //             _selectedTopics.Add(t);
            //         }
            //
            //         ImGui.PopID();
            //     }
            //
            //     ImGui.Unindent(10);
            //     FormInputs.AddVerticalSpace();
            // }
            // ImGui.EndChild();
            //
            // ImGui.SameLine();
            ImGui.BeginChild("Inner", new Vector2(-200, 0), false, ImGuiWindowFlags.NoMove);
            {
                ImGui.SameLine();

                if (ImGui.Button("Save"))
                {
                    SkillMapData.Save();
                }

                if (ImGui.IsWindowAppearing())
                {
                    if (_selectedTopics.Count == 0)
                    {
                        var tmp = new HashSet<QuestTopic>(SkillMapData.Data.Topics);
                        _canvas.FocusTopics(tmp);
                    }
                }

                DrawInteractiveMap();
            }

            ImGui.EndChild();
            ImGui.SameLine();
            ImGui.BeginChild("SidePanel", new Vector2(200, 0));
            {
                DrawSidebar();
            }
            ImGui.EndChild();
        }

        ImGui.PopStyleColor();

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private static void DrawInteractiveMap()
    {
        var isAnyItemHovered = _canvas.DrawContent(HandleTopicInteraction2, out var mouseCell, _selectedTopics);

        if (_state == States.DraggingItems)
        {
            if (_draggedTopic == null || ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _draggedTopic = null;
                _state = States.Default;
            }
            else
            {
                var draggedCell = _draggedTopic.Cell;
                var dX = mouseCell.X - draggedCell.X;
                var dY = mouseCell.Y - draggedCell.Y;

                var movedSomewhat = dX != 0 || dY != 0;

                if (movedSomewhat)
                {
                    var moveCellDelta = new HexCanvas.Cell(dX, dY);
                    var isBlocked = false;
                    foreach (var t in _selectedTopics)
                    {
                        var newCell = t.Cell + moveCellDelta;

                        if (_blockedCellIds.Contains(newCell.GetHashCode()))
                        {
                            isBlocked = true;
                            break;
                        }
                    }

                    if (!isBlocked)
                    {
                        foreach (var t in _selectedTopics)
                        {
                            t.Cell += moveCellDelta;
                        }
                    }
                }
            }
        }

        var dl = ImGui.GetWindowDrawList();
        if (!isAnyItemHovered && ImGui.IsWindowHovered() && !ImGui.IsMouseDown(ImGuiMouseButton.Right))
        {
            HandleFenceSelection();
            if (_fence.State != SelectionFence.States.Updated)
                DrawHoveredEmptyCell(dl, mouseCell);
        }
    }

    private static void HandleFenceSelection()
    {
        var shouldBeActive =
            ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup)
            && _state == States.Default;

        if (!shouldBeActive)
        {
            _fence.Reset();
            return;
        }

        switch (_fence.UpdateAndDraw(out var selectMode))
        {
            case SelectionFence.States.PressedButNotMoved:
                if (selectMode == SelectionFence.SelectModes.Replace)
                    _selectedTopics.Clear();
                break;

            case SelectionFence.States.Updated:
                HandleSelectionFenceUpdate(_fence.BoundsUnclamped, selectMode);

                break;

            case SelectionFence.States.CompletedAsClick:
                // A hack to prevent clearing selection when opening parameter popup
                if (ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopup))
                    break;

                _selectedTopics.Clear();
                break;
            case SelectionFence.States.Inactive:
                break;
            case SelectionFence.States.CompletedAsArea:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void HandleSelectionFenceUpdate(ImRect bounds, SelectionFence.SelectModes selectMode)
    {
        if (selectMode == SelectionFence.SelectModes.Replace)
        {
            _selectedTopics.Clear();
        }

        foreach (var topic in SkillMapData.Data.Topics)
        {
            var centerOnScreen = _canvas.ScreenPosFromCell(topic.Cell);
            if (!bounds.Contains(centerOnScreen))
                continue;

            if (selectMode == SelectionFence.SelectModes.Remove)
            {
                _selectedTopics.Remove(topic);
            }
            else
            {
                _selectedTopics.Add(topic);
            }
        }
    }

    private static void DrawSidebar()
    {
        if (_selectedTopics.Count != 1)
            return;

        var topic = _selectedTopics.First();

        if (ImGui.IsKeyDown(ImGuiKey.A) && !ImGui.IsAnyItemActive())
        {
            _state = States.LinkingItems;
        }

        var isSelectingUnlocked = _state == States.LinkingItems;

        if (CustomComponents.ToggleIconButton(ref isSelectingUnlocked, Icon.ConnectedOutput, Vector2.Zero))
        {
            _state = isSelectingUnlocked ? States.LinkingItems : States.Default;
        }

        ImGui.Indent(5);
        var autoFocus = false;
        if (_focusTopicNameInput)
        {
            autoFocus = true;
            _focusTopicNameInput = false;
        }

        FormInputs.DrawFieldSetHeader("Topic");
        ImGui.PushID(topic.Id.GetHashCode());
        FormInputs.AddStringInput("##Topic", ref topic.Title, autoFocus: autoFocus);
        FormInputs.AddVerticalSpace();

        if (FormInputs.AddEnumDropdown(ref topic.TopicType, "##Type"))
        {
        }

        FormInputs.DrawFieldSetHeader("Requires");
        if (FormInputs.AddEnumDropdown(ref topic.Requirement, "##Requirements"))
        {
        }

        
        FormInputs.DrawFieldSetHeader("Namespace");
        FormInputs.AddStringInput("##NameSpace", ref topic.Namespace);

        FormInputs.DrawFieldSetHeader("Description");
        topic.Description ??= string.Empty;
        CustomComponents.DrawMultilineTextEdit(ref topic.Description);

        ImGui.PopID();
    }

    private static void DrawHoveredEmptyCell(ImDrawListPtr dl, HexCanvas.Cell cell)
    {
        var hoverCenter = _canvas.ScreenPosFromCell(cell);
        _dampedHoverCanvasPos = MathUtils.Lerp(_dampedHoverCanvasPos, hoverCenter, 0.5f);

        dl.AddNgonRotated(_dampedHoverCanvasPos, _canvas.HexRadiusOnScreen, UiColors.ForegroundFull.Fade(0.1f), false);

        var activeTopic = _selectedTopics.Count == 0 ? null : _selectedTopics.First();

        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            var newTopic = new QuestTopic
                               {
                                   Id = Guid.NewGuid(),
                                   MapCoordinate = new Vector2(cell.X, cell.Y),
                                   Title = "New topic" + SkillMapData.Data.Topics.Count(),
                                   ZoneId = activeTopic?.ZoneId ?? Guid.Empty,
                                   TopicType = _lastType,
                                   ProgressionState = activeTopic?.ProgressionState ?? QuestTopic.ProgressStates.Locked,
                                   Requirement = activeTopic?.Requirement ?? QuestTopic.Requirements.AllInputPaths,
                               };

            _selectedTopics.Clear();
            _selectedTopics.Add(newTopic);
            _focusTopicNameInput = true;
        }
        else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _selectedTopics.Clear();
        }
    }

    private static void HandleTopicInteraction2(QuestTopic topic, bool isSelected)
    {
        switch (_state)
        {
            case States.Default:
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    _state = States.HoldingItem;

                break;

            case States.HoldingItem:
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    if (!ImGui.GetIO().KeyShift)
                    {
                        _selectedTopics.Clear();
                    }

                    _selectedTopics.Add(topic);
                    _lastType = topic.TopicType;
                }

                // Start Dragging
                if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    if (!_selectedTopics.Contains(topic))
                    {
                        _selectedTopics.Clear();
                        _selectedTopics.Add(topic);
                    }

                    _state = States.DraggingItems;
                    _canvas.InverseTransformPositionFloat(ImGui.GetMousePos());
                    _draggedTopic = topic;

                    // Initialize blocked cells to avoid collisions
                    _blockedCellIds.Clear();
                    foreach (var t in SkillMapData.Data.Topics)
                    {
                        if (_selectedTopics.Contains(t))
                            continue;

                        _blockedCellIds.Add(new HexCanvas.Cell(t.MapCoordinate).GetHashCode());
                    }
                }

                break;

            case States.LinkingItems:
                if (_selectedTopics.Count != 1 || isSelected)
                {
                    _state = States.Default;
                    break;
                }

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    var activeTopic = _selectedTopics.First();
                    if (!activeTopic.UnlocksTopics.Remove(topic.Id))
                    {
                        activeTopic.UnlocksTopics.Add(topic.Id);
                    }

                    if (!ImGui.GetIO().KeyShift)
                    {
                        _state = States.Default;
                    }
                }

                break;
        }
    }

    private enum States
    {
        Default,
        HoldingItem,
        LinkingItems,
        DraggingItems,
    }

    private static States _state;

    private static Vector2 _dampedHoverCanvasPos;
    private static readonly HashSet<int> _blockedCellIds = new(64);
    private static QuestTopic? _draggedTopic;
    private static bool _isOpen;
    private static readonly HashSet<QuestTopic> _selectedTopics = new();
    private static bool _focusTopicNameInput;
    private static QuestTopic.TopicTypes _lastType = QuestTopic.TopicTypes.Numbers;
    private static readonly SelectionFence _fence = new();
    private static readonly SkillMapCanvas _canvas = new();
}