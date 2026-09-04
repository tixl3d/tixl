#nullable enable
using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Icon = T3.Editor.Gui.Styling.Icon;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// A helper that decides which graph element to show.
/// This is used by <see cref="OutputWindow"/> and eventually in <see cref="ParameterWindow"/>.
/// </summary>
internal sealed class ViewSelectionPinning
{
    public void DrawPinning(Action? drawExtraMenuItems = null)
    {
        if (!TryGetPinnedOrSelectedInstance(out var pinnedOrSelectedInstance, out var canvas))
        {
            Unpin();
            return;
        }

        var nodeSelection = canvas.NodeSelection;

        // Keep pinned if pinned operator changed
        var oneSelected = nodeSelection.Selection.Count == 1;
        var selectedOp = nodeSelection.GetFirstSelectedInstance();
        var isPinnedToSelected = pinnedOrSelectedInstance == selectedOp;

        // FIXME: This is a hack and will only work with a single output window...
        nodeSelection.PinnedIds.Clear();
        if (_isPinned)
            nodeSelection.PinnedIds.Add(pinnedOrSelectedInstance.SymbolChildId);
        var iconSize = new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight());
        if (CustomComponents.IconButton(Icon.Pin,
                                        iconSize,
                                        _isPinned ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default
                                       ))
        {
            if (_isPinned)
            {
                _isPinned = false;
            }
            else
            {
                PinInstance(pinnedOrSelectedInstance, canvas);
            }
        }

        CustomComponents.TooltipForLastItem("Pin output to active operator.",
                                            UserActions.PinToOutputWindow.ListShortcuts());

        if (_isPinned)
        {
            ImGui.SameLine();
            if (CustomComponents.IconButton(Icon.PlayOutput,
                                            iconSize,
                                            isPinnedToSelected ? CustomComponents.ButtonStates.Disabled : CustomComponents.ButtonStates.Emphasized
                                           )
                && !isPinnedToSelected
                && oneSelected)
            {
                PinSelectionToView(canvas);
            }

            if (ImGui.IsItemHovered())
            {
                CustomComponents.TooltipForLastItem(selectedOp != null
                                                        ? $"Pin output to selected {selectedOp.Symbol.Name}."
                                                        : $"Select an operator and click to update pinning.",
                                                    UserActions.PinToOutputWindow.ListShortcuts());
            }
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(200 * T3Ui.UiScaleFactor);
        var suffix = _isPinned ? " (pinned)" : " (selected)";

        if (TryGetPinnedEvaluationInstance(canvas.Structure, out var pinnedEvaluationInstance))
        {
            suffix += " -> " + pinnedEvaluationInstance.Symbol.Name + " (Final)";
        }

        var symbolName = pinnedOrSelectedInstance.Symbol.Name;
        var symbolChildName = pinnedOrSelectedInstance.SymbolChild?.Name;
        if (!string.IsNullOrEmpty(symbolChildName))
        {
            symbolName = $@"""{symbolChildName}"" {symbolName}";
        }

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        var open = ImGui.BeginCombo("##pinning", symbolName + suffix);
        ImGui.PopStyleColor();

        if (open)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 6));
            if (_isPinned)
            {
                if (CustomComponents.DrawMenuItem(_unpinViewId, "Unpin View", reserveIconColumn: false))
                {
                    Unpin();
                }

                if (_pinnedProjectView != null)
                {
                    var instanceSelectedInGraph = _pinnedProjectView!.NodeSelection.GetFirstSelectedInstance();
                    if (instanceSelectedInGraph != pinnedOrSelectedInstance)
                    {
                        if (CustomComponents.DrawMenuItem(_pinSelectionToViewId, "Pin Selection to View",
                                                          UserActions.PinToOutputWindow.ListShortcuts(), reserveIconColumn: false))
                        {
                            PinSelectionToView(canvas);
                        }
                    }
                }
            }
            else
            {
                if (CustomComponents.DrawMenuItem(_pinSelectionToViewId, "Pin Selection to View",
                                                  UserActions.PinToOutputWindow.ListShortcuts(), reserveIconColumn: false))
                {
                    PinSelectionToView(canvas);
                }
            }

            if (pinnedEvaluationInstance != null)
            {
                if (CustomComponents.DrawMenuItem(_unpinStartOperatorId, "Unpin Start Operator", reserveIconColumn: false))
                {
                    _pinnedEvaluationInstancePath = [];
                }
            }
            else
            {
                if (CustomComponents.DrawMenuItem(_pinAsStartOperatorId, "Pin as Start Operator", reserveIconColumn: false))
                {
                    PinSelectionAsEvaluationStart(nodeSelection.GetFirstSelectedInstance());
                }
            }

            if (ProjectView.Focused != null)
            {
                if (CustomComponents.DrawMenuItem(_showInGraphId, "Show in Graph", reserveIconColumn: false))
                {
                    var parentInstance = pinnedOrSelectedInstance.Parent;
                    var parentSymbolUi = parentInstance?.GetSymbolUi();
                    if (parentSymbolUi == null)
                        return;

                    var instanceChildUi = parentSymbolUi.ChildUis[pinnedOrSelectedInstance.SymbolChildId];
                    nodeSelection.SetSelection(instanceChildUi, pinnedOrSelectedInstance);
                    FitViewToSelectionHandling.FitViewToSelection();
                }
            }

            if (pinnedOrSelectedInstance.Outputs.Count > 1)
            {
                if (CustomComponents.DrawSubMenu(_showOutputId, "Show Output"))
                {
                    var isDefaultOutput = _selectedOutputId == Guid.Empty;

                    for (var outputIndex = 0; outputIndex < pinnedOrSelectedInstance.Outputs.Count; outputIndex++)
                    {
                        var output = pinnedOrSelectedInstance.Outputs[outputIndex];
                        var isSelected = outputIndex == 0 && isDefaultOutput
                                         || output.Id == _selectedOutputId;
                        
                        if(CustomComponents.DrawMenuItem(_showOutputItemBaseId + outputIndex,
                                                       output.ToString(),
                                                       isChecked:isSelected,
                                                       reserveIconColumn: false
                                                       ))
                        {
                            _selectedOutputId = outputIndex == 0 ? Guid.Empty : output.Id;
                        }
                    }

                    ImGui.EndMenu();
                }
            }

            if (drawExtraMenuItems != null)
            {
                CustomComponents.SeparatorLine();
                drawExtraMenuItems();
            }

            CustomComponents.SeparatorLine();
            CustomComponents.DrawMenuItem(_showHoveredOutputsId, "Show Hovered Outputs", isEnabled: false, reserveIconColumn: false);
            ImGui.PopStyleVar();
            ImGui.EndCombo();
        }

        ImGui.SameLine();
    }

    private void PinSelectionToView(ProjectView canvas)
    {
        var firstSelectedInstance = canvas.NodeSelection.GetFirstSelectedInstance();
        PinInstance(firstSelectedInstance, canvas);
        //_pinnedEvaluationInstancePath = null;
    }

    private void PinSelectionAsEvaluationStart(Instance? instance)
    {
        _pinnedEvaluationInstancePath = instance != null
                                            ? instance.InstancePath
                                            : [];
    }

    public bool IsPinned => _isPinned;

    public bool TryGetPinnedOrSelectedInstance([NotNullWhen(true)] out Instance? instance, [NotNullWhen(true)] out ProjectView? components)
    {
        var focusedComponents = ProjectView.Focused;

        if (!_isPinned)
        {
            if (focusedComponents == null)
            {
                components = null;
                instance = null;
                return false;
            }

            components = focusedComponents;
            instance = focusedComponents.NodeSelection.GetFirstSelectedInstance();
            return instance != null;
        }

        // Try the pinned project view first
        if (_pinnedProjectView != null && !_pinnedProjectView.GraphView.Destroyed)
        {
            instance = _pinnedProjectView.Structure.GetInstanceFromIdPath(_pinnedInstancePath);
            components = _pinnedProjectView;
            if (instance != null)
                return true;
        }

        // Pinned project view is stale (e.g. after project close/reopen) — try resolving
        // the saved instance path against the current focused project view
        if (focusedComponents != null && _pinnedInstancePath.Count > 0)
        {
            instance = focusedComponents.Structure.GetInstanceFromIdPath(_pinnedInstancePath);
            if (instance != null)
            {
                _pinnedProjectView = focusedComponents;
                components = focusedComponents;
                return true;
            }

            // Instance not found yet — might still be loading. Keep the pin data
            // and fall through to selection for now.
        }

        // Don't Unpin() here — the instance path may resolve on a later frame
        // after the project finishes loading. Fall back to selection.
        if (focusedComponents != null)
        {
            components = focusedComponents;
            instance = focusedComponents.NodeSelection.GetFirstSelectedInstance();
            return instance != null;
        }

        components = null;
        instance = null;
        return false;
    }

    public void PinInstance(Instance? instance, ProjectView? projectView=null, bool unpinIfAlreadyPinned = false)
    {
        projectView??= ProjectView.Focused;
        var path = instance != null ? instance.InstancePath : [];
        var alreadyPinned = path.SequenceEqual(_pinnedInstancePath);
        if (alreadyPinned && unpinIfAlreadyPinned)
        {
            Unpin();
            return;
        }

        _pinnedInstancePath = instance != null ? instance.InstancePath : [];
        _pinnedEvaluationInstancePath = _pinnedInstancePath;
        _pinnedProjectView = projectView;
        _isPinned = true;
    }

    public void Unpin()
    {
        _isPinned = false;
        _pinnedProjectView = null;
        _pinnedInstancePath = [];
        _pinnedEvaluationInstancePath = [];
    }

    public bool TryGetPinnedEvaluationInstance(Structure structure, [NotNullWhen(true)] out Instance? instance)
    {
        instance = structure.GetInstanceFromIdPath(_pinnedEvaluationInstancePath);
        return instance != null;
    }

    internal void SaveStateTo(Output.OutputWindowState state)
    {
        state.IsPinned = _isPinned;
        state.PinnedOutputId = _selectedOutputId;
        state.PinnedInstancePath = _isPinned ? _pinnedInstancePath.ToArray() : [];
    }

    internal void LoadStateFrom(Output.OutputWindowState state)
    {
        _isPinned = state.IsPinned;
        _selectedOutputId = state.PinnedOutputId;
        _pinnedInstancePath = state.PinnedInstancePath;
        _pinnedProjectView = UiModel.ProjectHandling.ProjectView.Focused;
        _pinnedEvaluationInstancePath = state.PinnedInstancePath; // Same path for now
    }

    private bool _isPinned;
    private Guid _selectedOutputId; // Empty if default
    private ProjectView? _pinnedProjectView;
    private IReadOnlyList<Guid> _pinnedInstancePath = [];
    private IReadOnlyList<Guid> _pinnedEvaluationInstancePath = [];

    public ISlot? GetPinnedOrDefaultOutput(List<ISlot> outputs)
    {
        if (outputs.Count == 0)
            return null;

        if (_selectedOutputId == Guid.Empty)
            return outputs[0];

        for (var index = 0; index < outputs.Count; index++)
        {
            var o = outputs[index];
            if (o.Id == _selectedOutputId)
                return o;
        }

        return outputs[0];
    }

    private static readonly int _unpinViewId = nameof(_unpinViewId).GetHashCode();
    private static readonly int _pinSelectionToViewId = nameof(_pinSelectionToViewId).GetHashCode();
    private static readonly int _unpinStartOperatorId = nameof(_unpinStartOperatorId).GetHashCode();
    private static readonly int _pinAsStartOperatorId = nameof(_pinAsStartOperatorId).GetHashCode();
    private static readonly int _showInGraphId = nameof(_showInGraphId).GetHashCode();
    private static readonly int _showOutputId = nameof(_showOutputId).GetHashCode();
    private static readonly int _showOutputItemBaseId = nameof(_showOutputItemBaseId).GetHashCode();
    private static readonly int _showHoveredOutputsId = nameof(_showHoveredOutputsId).GetHashCode();
}