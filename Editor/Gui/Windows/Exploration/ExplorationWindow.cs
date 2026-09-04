using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.InputUi.VectorInputs;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

using Vector4 = System.Numerics.Vector4;

namespace T3.Editor.Gui.Windows.Exploration;

/// <summary>
/// Renders the <see cref="ExplorationWindow"/>
/// </summary>
internal sealed class ExplorationWindow : Window
{
    public ExplorationWindow()
    {
        _variationCanvas = new ExploreVariationCanvas(this);
        Config.Title = "Variation Explorer";
        Config.Visible = true;
    }

    private Guid _compositionSymbolId;

    private bool CheckFavoriteMatchesNodeSelection(ExplorationVariation variation, NodeSelection nodeSelection)
    {
        var match = true;
        foreach (var param in variation.ValuesForParameters.Keys)
        {
            if (!nodeSelection.GetSelectedChildUis().Contains(param.TargetChild))
            {
                match = false;
            }
        }

        return match;
    }

    protected override void DrawContent()
    {
        ImGui.BeginChild("params", new Vector2(200, -1));
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(4,4));

            var currentGraphCanvas = ProjectView.Focused;

            if (currentGraphCanvas != null)
            {
                DrawSidePanelContent(currentGraphCanvas.NodeSelection, currentGraphCanvas.Structure);
            }

            ImGuiUtils.ResetCursorForExtentCheck();
            ImGui.PopStyleVar();
            ImGui.EndChild();
        }

        ImGui.SameLine();
        ImGui.BeginChild("canvas", new Vector2(-1, -1), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar);
        {
            _variationCanvas.Draw(ProjectView.Focused?.Structure);
        }
        ImGui.EndChild();
    }

    private static float _strengthBeforeDrag = 0;
        
    private void DrawSidePanelContent(NodeSelection nodeSelection, Structure structure)
    {
        // List selected operators and parameters
        ImGui.DragFloat("Scatter", ref _variationCanvas.Scatter, 0.1f, 0, 100);
        _compositionSymbolId = nodeSelection.GetSelectionSymbolChildId() ?? Guid.Empty;

        var selectedSymbolUiChilds = nodeSelection.GetSelectedChildUis();

        // Remove no longer selected parameters
        var symbolChildUis = selectedSymbolUiChilds as SymbolUi.Child[] ?? selectedSymbolUiChilds.ToArray();
        for (var index = VariationParameters.Count - 1; index >= 0; index--)
        {
            if (!symbolChildUis.Contains(VariationParameters[index].TargetChild))
            {
                VariationParameters.RemoveAt(index);
            }
        }

        foreach (var symbolChildUi in symbolChildUis)
        {
            ImGui.Indent(5);
            ImGui.PushFont(Fonts.FontBold);
            ImGui.Selectable(symbolChildUi.SymbolChild.ReadableName);
            ImGui.PopFont();
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Gray.Rgba);
            ImGui.PushID(symbolChildUi.Id.GetHashCode());
                
            var keepX = ImGui.GetCursorPosX();
            foreach (var input in symbolChildUi.SymbolChild.Inputs.Values)
            {
                ImGui.PushID(input.Id.GetHashCode());
                var p = input.DefaultValue;

                // TODO: check if input is connected
                if (p.ValueType == typeof(float)
                    || p.ValueType == typeof(Vector2)
                    || p.ValueType == typeof(Vector3)
                    || p.ValueType == typeof(Vector4)
                   )
                {
                    var matchingParam =
                        VariationParameters.FirstOrDefault(variationParam =>
                                                               input == variationParam.Input && symbolChildUi.Id == variationParam.TargetChild.Id);
                    var selected = matchingParam != null;

                    if (matchingParam != null)
                    {
                        var keep = ImGui.GetCursorPos();
                        var formattedStrength = $"×{matchingParam.ScatterStrength:F1}";
                        var size = ImGui.CalcTextSize(formattedStrength);
                        ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X- size.X - 5);
                        ImGui.TextUnformatted(formattedStrength);
                        ImGui.SetCursorPos(keep);
                        ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X- 50);
                        ImGui.InvisibleButton("ScatterStrengthFactor", new Vector2(50, ImGui.GetTextLineHeight()));
                        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
                        {
                            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
                        }
                        if (ImGui.IsItemActivated())
                        {
                            _strengthBeforeDrag = matchingParam.ScatterStrength;
                        }
                        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                        {
                            matchingParam.ScatterStrength = (_strengthBeforeDrag + ImGui.GetMouseDragDelta().X * 0.02f).Clamp(0, 100f);
                        }
                        if (ImGui.IsItemDeactivated())
                        {
                            _variationCanvas.ClearVariations();
                        }
                        ImGui.SameLine();
                        ImGui.SetCursorPosX(keepX);
                    }
                        
                    if (ImGui.Selectable(input.Name, selected))
                    {
                        if (selected)
                        {
                            VariationParameters.Remove(matchingParam);
                        }
                        else
                        {
                            var instance = nodeSelection.GetInstanceForChildUi(symbolChildUi);
                            var inputSlot = instance.Inputs.Single(input2 => input2.Id == input.Id);
                                
                            //var xxx = symbolChildUi.SymbolChild.Symbol
                            var scale = 1f;
                            var min = float.NegativeInfinity;
                            var max = float.PositiveInfinity;
                            var clampMin = false;
                            var clampMax = false;

                            var symbolUi = symbolChildUi.SymbolChild.Symbol.GetSymbolUi();
                            var inputUi = symbolUi.InputUis[input.Id];
                            switch (inputUi)
                            {
                                case FloatInputUi floatInputUi:
                                    scale = floatInputUi.Scale;
                                    min = floatInputUi.Min;
                                    max = floatInputUi.Max;
                                    clampMin = floatInputUi.ClampMin;
                                    clampMax = floatInputUi.ClampMax;
                                    break;
                                case Vector2InputUi float2InputUi:
                                    scale = float2InputUi.Scale;
                                    min = float2InputUi.Min;
                                    max = float2InputUi.Max;
                                    clampMin = float2InputUi.ClampMin;
                                    clampMax = float2InputUi.ClampMax;
                                    break;
                                case Vector3InputUi float3InputUi:
                                    scale = float3InputUi.Scale;
                                    min = float3InputUi.Min;
                                    max = float3InputUi.Max;
                                    clampMin = float3InputUi.ClampMin;
                                    clampMax = float3InputUi.ClampMax;
                                    break;
                                case Vector4InputUi float4InputUi:
                                    scale = 0.02f; // Reasonable default for color variations
                                    break;
                            }

                            VariationParameters.Add(new ExplorationVariation.VariationParameter()
                                                        {
                                                            TargetChild = symbolChildUi,
                                                            Input = input,
                                                            InstanceIdPath = instance.InstancePath,
                                                            Type = p.ValueType,
                                                            InputSlot = inputSlot,
                                                            ParameterScale = scale,
                                                            ParameterMin = min,
                                                            ParameterMax = max,
                                                            ParameterClampMin = clampMin,
                                                            ParameterClampMax = clampMax,
                                                        });
                        }

                        _variationCanvas.ClearVariations();
                    }
                }
                ImGui.PopID();
            }

            ImGui.PopID();

            ImGui.Dummy(Spacing);
            ImGui.PopStyleColor();
            ImGui.Unindent(5);
        }

        // List Snapshots
        if (_compositionSymbolId != Guid.Empty && _variationsForSymbols.TryGetValue(_compositionSymbolId, out var savedForComposition))
        {
            ImGui.Separator();
                
            // Header
            ImGui.PushFont(Fonts.FontBold);
            var itemWidth = ImGui.GetContentRegionAvail().X - 16;
            ImGui.TextUnformatted("Snapshots");
            ImGui.PopFont();
            ImGui.SameLine(itemWidth);
                
            if (CustomComponents.IconButton(Icon.Trash, new Vector2(16, 16)))
            {
                Log.Debug("Not implemented");
            }
            CustomComponents.TooltipForLastItem("Remove not liked snapshots");

            ExplorationVariation deleteAfterIteration = null;
            foreach (var variation in savedForComposition)
            {
                var isMatching = CheckFavoriteMatchesNodeSelection(variation, nodeSelection);
                ImGui.PushStyleColor(ImGuiCol.Text, isMatching ? UiColors.Gray.Rgba : NonMatchingVarationsColor);
                ImGui.PushID(variation.GetHashCode());
                {
                    var isSelected = _blendedVariations.Contains(variation);
                    if (CustomComponents.IconButton(isSelected ? Icon.ChevronRight : Icon.Pin, new Vector2(16, 16)))
                    {
                        if (isSelected)
                        {
                            _blendedVariations.Remove(variation);
                        }
                        else
                        {
                            _blendedVariations.Add(variation);
                        }

                        LayoutBlendedVariations();
                    }

                    ImGui.SameLine();

                    if (ImGui.Selectable(variation.Title, false, 0, new Vector2(ImGui.GetWindowWidth() - 32, 0)))
                    {
                        SelectVariation(variation, nodeSelection, structure);
                    }

                    if (ImGui.IsItemHovered())
                    {
                        if (_lastHoveredVariation == null)
                        {
                            variation.KeepCurrentAndApplyNewValues(structure);
                            _lastHoveredVariation = variation;
                        }
                        else if (_lastHoveredVariation != variation)
                        {
                            _lastHoveredVariation.RestoreValues();
                            variation.KeepCurrentAndApplyNewValues(structure);
                            _lastHoveredVariation = variation;
                        }

                        // Hover relevant operators
                        foreach (var param in _lastHoveredVariation.ValuesForParameters.Keys)
                        {
                            FrameStats.AddHoveredId(param.TargetChild.Id);
                        }
                    }
                    else
                    {
                        var wasHoveredBefore = _lastHoveredVariation == variation;
                        if (wasHoveredBefore)
                        {
                            _lastHoveredVariation.RestoreValues();
                            _lastHoveredVariation = null;
                        }
                    }

                    ImGui.SameLine();

                    // Delete button
                    if (CustomComponents.IconButton(variation.IsLiked ? Icon.Heart : Icon.HeartOutlined, new Vector2(16, 16)))
                    {
                        variation.IsLiked = !variation.IsLiked;
                        //deleteAfterIteration = variation;
                    }
                }

                ImGui.PopStyleColor();
                ImGui.PopID();
            }

            if (deleteAfterIteration != null)
            {
                savedForComposition.Remove(deleteAfterIteration);
            }
        }
    }

    private void LayoutBlendedVariations()
    {
        _variationCanvas.ClearVariations();
        _variationCanvas.ResetView();

        // Merge parameter list
        var parameters = new List<ExplorationVariation.VariationParameter>();
        foreach (var variation in _blendedVariations)
        {
            foreach (var param in variation.ValuesForParameters.Keys)
            {
                if (!parameters.Contains(param))
                    parameters.Add(param);
            }
        }

        const int steps = 7;
        if (_blendedVariations.Count == 2)
        {
            for (int stepY = 0; stepY <= steps; stepY++)
            {
                var ty = (float)stepY / steps;
                var inputVariationsAndWeights = new List<Tuple<ExplorationVariation, float>>()
                                                    {
                                                        new(_blendedVariations[0], 1 - ty),
                                                        new(_blendedVariations[1], ty),
                                                    };
                var newVariation = ExplorationVariation.Mix(parameters, inputVariationsAndWeights, 0, GridCell.Center + new GridCell(0, stepY - steps / 2));
                _variationCanvas.AddVariationToGrid(newVariation);
            }
        }
        else if (_blendedVariations.Count == 3)
        {
            for (int stepY = 0; stepY <= steps; stepY++)
            {
                for (int stepX = 0; stepX <= steps; stepX++)
                {
                    var tx = (float)stepX / steps;
                    var ty = (float)stepY / steps;
                    var inputVariationsAndWeights = new List<Tuple<ExplorationVariation, float>>()
                                                        {
                                                            new(_blendedVariations[0], (1 - tx) * (1 - ty)),
                                                            new(_blendedVariations[1], (1 - tx) * (ty)),
                                                            new(_blendedVariations[2], (tx) * (ty)),
                                                        };

                    var gridCell = GridCell.Center + new GridCell(stepX - steps / 2, stepY - steps / 2);
                    var newVariation = ExplorationVariation.Mix(parameters, inputVariationsAndWeights, 0, gridCell);
                    _variationCanvas.AddVariationToGrid(newVariation);
                }
            }
        }
    }

    private void SelectVariation(ExplorationVariation variation, NodeSelection nodeSelection, Structure structure)
    {
        variation.ApplyPermanently();
        variation.UpdateUndoCommand(structure);

        // Select variation
        nodeSelection.Clear();
        VariationParameters.Clear();

        var alreadyAdded = new HashSet<SymbolUi.Child>();
        foreach (var param in variation.ValuesForParameters.Keys.Distinct())
        {
            VariationParameters.Add(param);
            if (!alreadyAdded.Contains(param.TargetChild))
            {
                nodeSelection.AddSelection(param.TargetChild, structure.GetInstanceFromIdPath(param.InstanceIdPath));
                alreadyAdded.Add(param.TargetChild);
            }
        }

        _variationCanvas.ClearVariations();
    }

    private List<ExplorationVariation> _blendedVariations = new();

    internal override List<Window> GetInstances()
    {
        return new List<Window>();
    }

    public void SaveVariation(ExplorationVariation variation)
    {
        _savedVariationIndex++;
        variation.Title = GetRandomTitle();
        if (_variationsForSymbols.TryGetValue(_compositionSymbolId, out var list))
        {
            list.Add(variation);
        }
        else
        {
            _variationsForSymbols[_compositionSymbolId] = new List<ExplorationVariation> { variation };
        }
    }

    private string GetRandomTitle()
    {
        return RandomNames[_random.Next(RandomNames.Length)] + " " + RandomNames[_random.Next(RandomNames.Length)];
    }

    private static Random _random = new();

    private static string[] RandomNames =
        {
            "Ace", "Age", "Ego", "Aid", "Aim", "Air", "Ape", "Barf", "Ass", "Axe", "Bad", "Big", "Boa", "Bro", "Bug", "Bum", "Cat", "Cow", "Cult", "Dog",
            "Duck", "Eel", "Egg", "Eye", "Funk", "Fix", "Fox", "Fun", "Gut", "Hack", "Freak", "Cyber", "Dope", "Hip", "Bit",
            "Mega", "Bomb", "Hot", "Jump", "Cult", "Ice", "Mad", "Mix", "Mud", "Off", "Ohm", "Oil", "One", "Two", "Four", "Five", "Six", "Pet", "Pig",
            "Poo", "Pop", "Pot", "Pub", "Raw", "Red", "Green", "Pink", "Black", "White", "Orange", "Fine", "Fog", "Sad", "Sea", "Sex", "Shy", "Sin", "Sir",
            "Ska", "Toy", "Tea", "Retro", "Monkey", "Top", "Wet", "Zoo"
        };

    public IOutputUi OutputUi { get; set; }

    private readonly Dictionary<Guid, List<ExplorationVariation>> _variationsForSymbols = new();
    private ExplorationVariation _lastHoveredVariation;
    private readonly ExploreVariationCanvas _variationCanvas;
    private static readonly Vector2 Spacing = new(1, 5);
    private static readonly Color NonMatchingVarationsColor = new(0.3f);
    private static int _savedVariationIndex = 1;
    internal readonly List<ExplorationVariation.VariationParameter> VariationParameters = new();
}