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
    public void DrawPinning()
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
                                        _isPinned ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed
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
                                            isPinnedToSelected ? CustomComponents.ButtonStates.Disabled : CustomComponents.ButtonStates.Normal
                                           )
                && !isPinnedToSelected
                && oneSelected)
            {
                PinSelectionToView(canvas);
            }

            CustomComponents.TooltipForLastItem(selectedOp != null
                                                    ? $"Pin output to selected {selectedOp.Symbol.Name}."
                                                    : $"Select an operator and click to update pinning.",
                                                UserActions.PinToOutputWindow.ListShortcuts());
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(200*T3Ui.UiScaleFactor);
        var suffix = _isPinned ? " (pinned)" : " (selected)";

        if (TryGetPinnedEvaluationInstance(canvas.Structure, out var pinnedEvaluationInstance))
        {
            suffix += " -> " + pinnedEvaluationInstance.Symbol.Name + " (Final)";
        }

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);

        var symbolName = pinnedOrSelectedInstance.Symbol.Name;
        var symbolChildName = pinnedOrSelectedInstance.SymbolChild?.Name;
        if (!string.IsNullOrEmpty(symbolChildName))
        {
            symbolName = $"\"{symbolChildName}\" {symbolName}";
        }
        
        if (ImGui.BeginCombo("##pinning", symbolName + suffix))
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 6));
            if (_isPinned)
            {
                if (ImGui.MenuItem("Unpin view"))
                {
                    Unpin();
                }

                if (_pinnedProjectView != null)
                {
                    var instanceSelectedInGraph = _pinnedProjectView!.NodeSelection.GetFirstSelectedInstance();
                    if (instanceSelectedInGraph != pinnedOrSelectedInstance)
                    {
                        if (ImGui.MenuItem("Pin Selection to View", UserActions.PinToOutputWindow.ListShortcuts()))
                        {
                            PinSelectionToView(canvas);
                        }
                    }
                }
            }
            else
            {
                if (ImGui.MenuItem("Pin Selection to View", UserActions.PinToOutputWindow.ListShortcuts()))
                {
                    PinSelectionToView(canvas);
                }
            }

            if (pinnedEvaluationInstance != null)
            {
                if (ImGui.MenuItem("Unpin start operator"))
                {
                    _pinnedEvaluationInstancePath = [];
                }
            }
            else
            {
                if (ImGui.MenuItem("Pin as start operator"))
                {
                    PinSelectionAsEvaluationStart(nodeSelection.GetFirstSelectedInstance());
                }
            }

            if (ProjectView.Focused != null)
            {
                if (ImGui.MenuItem("Show in Graph"))
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
                if (ImGui.BeginMenu("Show Output..."))
                {
                    var isDefaultOutput = _selectedOutputId == Guid.Empty;
                    
                    for (var outputIndex = 0; outputIndex < pinnedOrSelectedInstance.Outputs.Count; outputIndex++)
                    {
                        var output = pinnedOrSelectedInstance.Outputs[outputIndex];
                        var isSelected = outputIndex == 0 && isDefaultOutput
                                         || output.Id == _selectedOutputId;
                        if (ImGui.MenuItem(output.ToString(), null, isSelected))
                        {
                            _selectedOutputId = outputIndex == 0 ? Guid.Empty : output.Id;
                        }
                    }

                    ImGui.EndMenu();
                }
            }

            ImGui.Separator();
            ImGui.MenuItem("Show hovered outputs", false);
            ImGui.PopStyleVar();
            ImGui.EndCombo();
        }

        ImGui.PopStyleColor();
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

        if (!_pinnedProjectView!.GraphView.Destroyed)
        {
            instance = _pinnedProjectView.Structure.GetInstanceFromIdPath(_pinnedInstancePath);
            components = _pinnedProjectView;
            return instance != null;
        }

        Unpin();
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

    public void PinInstance(Instance? instance, ProjectView canvas)
    {
        _pinnedInstancePath = instance != null ? instance.InstancePath : [];
        _pinnedProjectView = canvas;
        _isPinned = true;
    }

    private void Unpin()
    {
        _isPinned = false;
        _pinnedProjectView = null;
        _pinnedInstancePath = [];
    }

    public bool TryGetPinnedEvaluationInstance(Structure structure, [NotNullWhen(true)] out Instance? instance)
    {
        instance = structure.GetInstanceFromIdPath(_pinnedEvaluationInstancePath);
        return instance != null;
    }

    private bool _isPinned;
    private Guid _selectedOutputId; // Empty if default
    private ProjectView? _pinnedProjectView;
    private IReadOnlyList<Guid> _pinnedInstancePath = Array.Empty<Guid>();
    private IReadOnlyList<Guid> _pinnedEvaluationInstancePath = Array.Empty<Guid>();

    public ISlot? GetPinnedOrDefaultOutput(IReadOnlyList<ISlot> outputs)
    {
        if (outputs.Count == 0)
            return null;

        if (_selectedOutputId == Guid.Empty)
            return outputs[0];

        foreach (var o in outputs)
        {
            if (o.Id == _selectedOutputId)
                return o;
        }
        
        return outputs[0];
    }
}