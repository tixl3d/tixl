#nullable enable
using System.Diagnostics;
using System.Text;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Skills.Data;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Graph.Dialogs;

internal static class EditTourPointsPopup
{
    private static bool _isOpen;

    internal static void ShowNextFrame()
    {
        _isOpen = true;
    }

    private static string GetSelectionMarkdown()
    {
        if (_compositionUi == null)
            return string.Empty;

        var sb = new StringBuilder();

        var hasCurrentCompositionTour = _compositionUi.TourPoints.Count > 0;
        if (hasCurrentCompositionTour)
        {
            TourDataMarkdownExport.WriteSymbolTourMarkdown(_compositionUi, sb);
        }
        else
        {
            var orderedChildUis = _selectedChildIds
                                 .Select(id => _compositionUi.ChildUis.TryGetValue(id, out var childUi) ? childUi : null)
                                 .Where(childUi => childUi != null)
                                 .OrderBy(childUi => childUi!.PosOnCanvas.Y);

            foreach (var childUi in orderedChildUis)
            {
                if (childUi == null)
                    continue;

                TourDataMarkdownExport.WriteSymbolTourMarkdown(childUi.SymbolChild.Symbol.GetSymbolUi(), sb);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    internal static ChangeSymbol.SymbolModificationResults Draw(Symbol operatorSymbol, ProjectView projectView)
    {
        var result = ChangeSymbol.SymbolModificationResults.Nothing;

        if (!_isOpen)
            return ChangeSymbol.SymbolModificationResults.Nothing;

        ImGui.SetNextWindowSize(new Vector2(500, 500) * T3Ui.UiScaleFactor, ImGuiCond.Once);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1);
        if (ImGui.Begin("Edit tour points", ref _isOpen))
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.WindowBackground.Fade(0.8f).Rgba);

            ImGui.BeginChild("Inner", Vector2.Zero, false, ImGuiWindowFlags.NoMove);
            {
                _compositionUi = operatorSymbol.GetSymbolUi();

                if (CustomComponents.IconButton(Icon.CopyToClipboard, Vector2.Zero))
                {
                    ImGui.SetClipboardText(GetSelectionMarkdown());
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    {
                        ImGui.PushFont(Fonts.Code);
                        ImGui.PushTextWrapPos(400);
                        ImGui.TextWrapped(GetSelectionMarkdown());
                        ImGui.PopTextWrapPos();
                        ImGui.PopFont();
                    }
                    ImGui.EndTooltip();
                }

                ImGui.SameLine();
                if (CustomComponents.IconButton(Icon.PasteFromClipboard, Vector2.Zero))
                {
                    TourDataMarkdownExport.TryPasteTourData(_compositionUi, projectView);
                }

                // Handle selection
                _selectedChildIds.Clear();
                _firstSelectedChildId = Guid.Empty;
                ImGui.Indent(20);
                FormInputs.AddVerticalSpace(20);

                foreach (var c in projectView.NodeSelection.GetSelectedChildUis())
                {
                    if (_firstSelectedChildId == Guid.Empty)
                        _firstSelectedChildId = c.Id;

                    _selectedChildIds.Add(c.Id);
                }

                var modified = false;
                _completedDragging = false;

                // List...
                if (!_isDragging && _listOrderWhileDragging.Count != _compositionUi.TourPoints.Count)
                {
                    _listOrderWhileDragging.Clear();
                    for (var index = 0; index < _compositionUi.TourPoints.Count; index++)
                    {
                        _listOrderWhileDragging.Add(index);
                    }
                }

                for (var index = 0; index < _compositionUi.TourPoints.Count; index++)
                {
                    var tourPoint = _compositionUi.TourPoints[index];

                    ImGui.PushID(tourPoint.Id.GetHashCode());
                    // Draw floating + button...
                    {
                        var keepCursorPos = ImGui.GetCursorPos();
                        var h = ImGui.GetFrameHeight();
                        ImGui.SetCursorPos(keepCursorPos - new Vector2(h, h * 0.5f));

                        if (CustomComponents.TransparentIconButton(Icon.Plus, Vector2.Zero))
                        {
                            InsertNewTourPoint(index);
                        }

                        ImGui.SetCursorPos(keepCursorPos);
                    }

                    modified |= DrawItem(tourPoint, index);
                    FormInputs.AddVerticalSpace(2);

                    ImGui.PopID();
                }

                if (_completedDragging)
                {
                    _isDragging = false;
                    _listOrderWhileDragging.Clear();
                    modified = true;
                }

                if (CustomComponents.DisablableButton("Add Tour Point", CanAdd))
                {
                    InsertNewTourPoint(_compositionUi.TourPoints.Count);
                }

                if (modified)
                    _compositionUi.FlagAsModified();
            }

            ImGui.EndChild();
            ImGui.PopStyleColor();
        }

        ImGui.PopStyleColor();

        ImGui.End();
        ImGui.PopStyleVar();
        return result;
    }

    private static readonly Dictionary<int, float> _lastItemHeights = new();

    private static bool DrawItem(TourPoint tourPoint, int index)
    {
        Debug.Assert(_compositionUi != null);
        var modified = false;

        var isSelected = _selectedChildIds.Contains(tourPoint.ChildId) && tourPoint.Style == TourPoint.Styles.InfoFor;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, isSelected
                                                   ? Color.Mix(UiColors.BackgroundButton, UiColors.BackgroundActive, 0.1f).Rgba
                                                   : UiColors.BackgroundButton);

        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 5);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5, 5)); // inner spacing

        if (!_lastItemHeights.TryGetValue(index, out var height))
        {
            height = 0;
        }

        ImGui.BeginChild("item", new Vector2(-5, height), true);
        {
            FormInputs.AddVerticalSpace(3);
            var padding = 10;
            ImGui.PushFont(Fonts.FontSmall);
            {
                ImGui.Indent(6);
                ImGui.SetNextItemWidth(10);
                ImGui.AlignTextToFramePadding();
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);

                var dragIndex = _isDragging
                                    ? _listOrderWhileDragging[index]
                                    : index;

                ImGui.Button($"{dragIndex + 1}.");
                ImGui.PopStyleColor(2);

                if (ImGui.IsItemActive())
                {
                    _isDragging = true;
                    var itemMin = ImGui.GetWindowPos();
                    var itemMax = ImGui.GetWindowPos() + ImGui.GetWindowSize();
                    ImGui.GetForegroundDrawList().AddRect(itemMin, itemMax, Color.Gray);

                    var mouseY = ImGui.GetMousePos().Y;
                    var halfHeight = ImGui.GetWindowSize().Y / 2;
                    var indexDelta = 0;
                    if (mouseY < itemMin.Y - halfHeight && index > 0)
                    {
                        indexDelta = -1;
                    }
                    else if (mouseY > itemMax.Y + halfHeight && index < _compositionUi.TourPoints.Count - 1)
                    {
                        indexDelta = 1;
                    }

                    if (indexDelta != 0)
                    {
                        var newIndex = index + indexDelta;
                        if (newIndex >= 0 && index < _compositionUi.TourPoints.Count && newIndex < _compositionUi.TourPoints.Count)
                        {
                            (_compositionUi.TourPoints[newIndex], _compositionUi.TourPoints[index]) =
                                (_compositionUi.TourPoints[index], _compositionUi.TourPoints[newIndex]);
                            (_listOrderWhileDragging[newIndex], _listOrderWhileDragging[index]) =
                                (_listOrderWhileDragging[index], _listOrderWhileDragging[newIndex]);
                        }
                    }
                }

                if (ImGui.IsItemDeactivated())
                {
                    _completedDragging = true;
                }

                // TourPoint type
                ImGui.SameLine(0, padding);

                ImGui.SetNextItemWidth(120);
                modified |= FormInputs.DrawEnumDropdown(ref tourPoint.Style, "style");

                // Highlight child
                if (_compositionUi.ChildUis.TryGetValue(tourPoint.ChildId, out var childUi))
                {
                    ImGui.SameLine(0, padding);
                    ImGui.TextUnformatted("on");
                    ImGui.SameLine(0, padding);
                    if (ImGui.Button(childUi.SymbolChild.ReadableName))
                    {
                        var comp = ProjectView.Focused!.CompositionInstance;
                        if (comp != null && comp.Children.TryGetChildInstance(tourPoint.ChildId, out var instance))
                        {
                            ProjectView.Focused.GraphView.OpenAndFocusInstance(instance.InstancePath);
                        }
                    }

                    if (ImGui.IsItemHovered())
                    {
                        FrameStats.AddHoveredId(tourPoint.ChildId);
                    }
                }
                else
                {
                    CustomComponents.TooltipForLastItem("" + tourPoint.ChildId);
                }

                if (!CanAdd && tourPoint.ChildId != _firstSelectedChildId)
                {
                    ImGui.SameLine();
                    if (CustomComponents.IconButton(Icon.Cross, Vector2.Zero))
                    {
                        tourPoint.ChildId = Guid.Empty;
                        tourPoint.InputId = Guid.Empty;
                        modified = true;
                    }
                }
                else if (CanAdd && tourPoint.ChildId != _firstSelectedChildId)
                {
                    ImGui.SameLine();
                    if (CustomComponents.IconButton(Icon.ApplySelection, Vector2.Zero))
                    {
                        tourPoint.ChildId = _firstSelectedChildId;
                        tourPoint.InputId = Guid.Empty;
                        modified = true;
                    }
                }

                if (tourPoint.ChildId != Guid.Empty)
                {
                    ImGui.SameLine(0, 10);
                    ImGui.SetNextItemWidth(100);
                    ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundButton.Rgba);
                    ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, UiColors.BackgroundButton.Rgba);
                    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4));
                    if (_compositionUi.ChildUis.TryGetValue(tourPoint.ChildId, out var symbolUi))
                    {
                        var label = "select input";
                        if (tourPoint.InputId != Guid.Empty)
                        {
                            var inputDefinition = symbolUi.SymbolChild.Symbol.InputDefinitions.FirstOrDefault(i => i.Id == tourPoint.InputId);
                            if (inputDefinition != null)
                                label = inputDefinition.Name;
                        }

                        if (ImGui.BeginCombo("##test", label))
                        {
                            if (ImGui.Selectable("--none--", tourPoint.InputId == Guid.Empty))
                            {
                                tourPoint.InputId = Guid.Empty;
                                modified = true;
                            }

                            foreach (var i in symbolUi.SymbolChild.Symbol.InputDefinitions)
                            {
                                if (ImGui.Selectable(i.Name, i.Id == tourPoint.InputId))
                                {
                                    tourPoint.InputId = i.Id;
                                    modified = true;
                                }
                            }
                            ImGui.EndCombo();
                        }

                    }

                    ImGui.PopStyleColor(2);
                    ImGui.PopStyleVar();
                }
            }
            ImGui.PopFont();

            var x = ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight();
            ImGui.SameLine(x);

            if (CustomComponents.TransparentIconButton(Icon.Trash, Vector2.Zero, CustomComponents.ButtonStates.Dimmed))
            {
                _compositionUi.TourPoints.Remove(tourPoint);
                modified = true;
            }

            if (_editedTourPointId == tourPoint.Id)
            {
                if (_shouldFocusInput)
                {
                    ImGui.SetKeyboardFocusHere();
                    _shouldFocusInput = false;
                }

                if (ImGui.InputTextMultiline("##Text", ref tourPoint.Description, 16384, new Vector2(-10, 60) * T3Ui.UiScaleFactor))
                {
                    modified = true;
                }

                if (ImGui.IsItemDeactivated())
                {
                    _editedTourPointId = Guid.Empty;
                }
            }
            else
            {
                var text = string.IsNullOrEmpty(tourPoint.Description) ? "Add description..." : tourPoint.Description;

                ImGui.TextWrapped(text);
                if (ImGui.IsItemClicked())
                {
                    _editedTourPointId = tourPoint.Id;
                    _shouldFocusInput = true;
                }
            }

            ImGui.Unindent(10);

            // Activate tour point
            if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                TourInteraction.SetProgressIndex(_compositionUi.Symbol.Id, index);
            }

            FormInputs.AddVerticalSpace();

            _lastItemHeights[index] = ImGui.GetCursorPosY();
        }
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();

        return modified;
    }

    private static void InsertNewTourPoint(int index)
    {
        _compositionUi?.TourPoints.Insert(index, new TourPoint
                                                     {
                                                         Description = string.Empty,
                                                         Id = Guid.NewGuid(),
                                                         ChildId = _firstSelectedChildId,
                                                         Style = TourPoint.Styles.Info
                                                     });
    }

    private static bool CanAdd => _selectedChildIds.Count == 1;

    private static bool _shouldFocusInput;
    private static bool _isDragging;
    private static readonly List<int> _listOrderWhileDragging = [];
    private static bool _completedDragging;

    private static Guid _editedTourPointId;
    private static SymbolUi? _compositionUi;
    private static Guid _firstSelectedChildId;
    private static readonly HashSet<Guid> _selectedChildIds = [];
}