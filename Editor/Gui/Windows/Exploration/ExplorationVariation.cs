using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;
using Vector2 = System.Numerics.Vector2;

using Vector4 = System.Numerics.Vector4;

namespace T3.Editor.Gui.Windows.Exploration;

internal sealed class ExplorationVariation
{
    public GridCell GridCell;
    public bool ThumbnailNeedsUpdate;
    public string Title;
    public bool IsLiked;

    private ExplorationVariation(Dictionary<VariationParameter, InputValue> valuesForParameters)
    {
        ValuesForParameters = valuesForParameters;
        ThumbnailNeedsUpdate = true;
    }

    public ExplorationVariation Clone(Structure structure)
    {
        var newVariation = new ExplorationVariation(new Dictionary<VariationParameter, InputValue>(ValuesForParameters));
        newVariation.UpdateUndoCommand(structure);
        return newVariation;
    }

    public void KeepCurrentAndApplyNewValues(Structure structure)
    {
        _changeCommand = CreateChangeCommand(structure);
        _changeCommand.Do();
    }

    public void ApplyPermanently()
    {
        if (_changeCommand == null)
            return;

        UndoRedoStack.AddAndExecute(_changeCommand);
    }

    public void RestoreValues()
    {
        _changeCommand?.Undo();
    }

    // private void InvalidateParameters()
    // {
    //     foreach (var param in ValuesForParameters.Keys)
    //     {
    //         param.InputSlot.DirtyFlag.ForceInvalidate();
    //     }
    // }

    public void UpdateUndoCommand(Structure structure)
    {
        _changeCommand = CreateChangeCommand(structure);
    }

    public static ExplorationVariation Mix(IEnumerable<VariationParameter> variationParameters,
                                           IReadOnlyCollection<Tuple<ExplorationVariation, float>> neighboursAndWeights, float scatter,
                                           GridCell cell = new())
    {
        // Collect neighbours
        var valuesForParameters = new Dictionary<VariationParameter, InputValue>();
        if (neighboursAndWeights.Count == 0)
        {
            foreach (var param in variationParameters)
            {
                valuesForParameters.Add(param, param.InputSlot.Input.Value);
            }

            return new ExplorationVariation(valuesForParameters)
                       {
                           GridCell = cell,
                       };
        }

        foreach (var param in variationParameters)
        {
            if (param.Type == typeof(float))
            {
                var value = 0f;
                var sumWeight = 0f;
                foreach (var (neighbourVariation, weight) in neighboursAndWeights)
                {
                    if (!neighbourVariation.ValuesForParameters.TryGetValue(param, out var matchingParam))
                        matchingParam = param.InputSlot.Input.Value;

                    if (matchingParam is InputValue<float> floatInput)
                    {
                        value += floatInput.Value * weight;
                        sumWeight += weight;
                    }
                }

                value *= 1f / sumWeight;
                value += _random.NextFloat(-scatter, scatter) * param.ParameterScale * param.ScatterStrength;
                valuesForParameters.Add(param, new InputValue<float>(value));
            }

            if (param.Type == typeof(Vector2))
            {
                var value = Vector2.Zero;
                var sumWeight = 0f;
                foreach (var (neighbourVariation, weight) in neighboursAndWeights)
                {
                    if (!neighbourVariation.ValuesForParameters.TryGetValue(param, out var matchingParam))
                        matchingParam = param.InputSlot.Input.Value;

                    if (matchingParam is InputValue<Vector2> typedInput)
                    {
                        value += typedInput.Value * weight;
                        sumWeight += weight;
                    }
                }

                value *= 1f / sumWeight;
                value += new Vector2(
                                     _random.NextFloat(-scatter, scatter),
                                     _random.NextFloat(-scatter, scatter)
                                    ) * param.ParameterScale * param.ScatterStrength;

                valuesForParameters.Add(param, new InputValue<Vector2>(value));
            }

            if (param.Type == typeof(Vector3))
            {
                var value = Vector3.Zero;
                var sumWeight = 0f;
                foreach (var (neighbourVariation, weight) in neighboursAndWeights)
                {
                    if (!neighbourVariation.ValuesForParameters.TryGetValue(param, out var matchingParam))
                        matchingParam = param.InputSlot.Input.Value;

                    if (matchingParam is InputValue<Vector3> typedInput)
                    {
                        value += typedInput.Value * weight;
                        sumWeight += weight;
                    }
                }

                value *= 1f / sumWeight;
                value += new Vector3(
                                     _random.NextFloat(-scatter, scatter),
                                     _random.NextFloat(-scatter, scatter),
                                     _random.NextFloat(-scatter, scatter)
                                    ) * param.ParameterScale * param.ScatterStrength;

                valuesForParameters.Add(param, new InputValue<Vector3>(value));
            }

            if (param.Type == typeof(Vector4))
            {
                var value = Vector4.Zero;
                var sumWeight = 0f;
                foreach (var (neighbourVariation, weight) in neighboursAndWeights)
                {
                    if (!neighbourVariation.ValuesForParameters.TryGetValue(param, out var matchingParam))
                        matchingParam = param.InputSlot.Input.Value;

                    if (matchingParam is InputValue<Vector4> typedInput)
                    {
                        value += typedInput.Value * weight;
                        sumWeight += weight;
                    }
                }

                value *= 1f / sumWeight;
                value += new Vector4(
                                     _random.NextFloat(-scatter, scatter),
                                     _random.NextFloat(-scatter, scatter),
                                     _random.NextFloat(-scatter, scatter),
                                     _random.NextFloat(-scatter, scatter)
                                    ) * param.ParameterScale * param.ScatterStrength;

                valuesForParameters.Add(param, new InputValue<Vector4>(value));
            }
        }

        return new ExplorationVariation(valuesForParameters)
                   {
                       GridCell = cell,
                   };
    }

    private MacroCommand CreateChangeCommand(Structure structure)
    {
        var commands = new List<ICommand>();

        foreach (var (param, value) in ValuesForParameters)
        {
            try
            {
                var instance = structure.GetInstanceFromIdPath(param.InstanceIdPath);
                var newCommand = new ChangeInputValueCommand(instance.Parent.Symbol, param.TargetChild.Id, param.Input, value);
                commands.Add(newCommand);
            }
            catch (Exception)
            {
                Log.Warning("Skipping no longer valid variation parameter");
            }
        }

        return new MacroCommand("Set Preset Values", commands);
    }

    public sealed class VariationParameter
    {
        public IReadOnlyList<Guid> InstanceIdPath = Array.Empty<Guid>();
        public SymbolUi.Child TargetChild;
        public IInputSlot InputSlot { get; set; }
        public Symbol.Child.Input Input;
        public Type Type;
        public float ScatterStrength = 1;
        public float ParameterScale = 1;
        public float ParameterMin = float.NegativeInfinity;
        public float ParameterMax = float.PositiveInfinity;
        public bool ParameterClampMin = false;
        public bool ParameterClampMax = false;
        
        //public bool IsHidden;
    }


    public readonly Dictionary<VariationParameter, InputValue> ValuesForParameters;

    private static readonly Random _random = new();
    private ICommand _changeCommand;
}