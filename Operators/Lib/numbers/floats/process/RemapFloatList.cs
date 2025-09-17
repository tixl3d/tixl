using T3.Core.Utils;

namespace Lib.numbers.floats.process;

[Guid("f4e26a76-f8c4-4889-b1dc-c690cd078296")]
public sealed class RemapFloatList : Instance<RemapFloatList>
{
    [Output(Guid = "b6ed9d22-e8c4-458e-9bdc-0d65eca208c5")]
    public readonly Slot<List<float>> Result = new();

    public RemapFloatList()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var inputList = FloatList.GetValue(context);
        if (inputList == null || inputList.Count == 0)
        {
            Result.Value = new List<float>(); // Return an empty list if input is null or empty
            return;
        }

        var inMin = RangeInMin.GetValue(context);
        var inMax = RangeInMax.GetValue(context);
        var outMin = RangeOutMin.GetValue(context);
        var outMax = RangeOutMax.GetValue(context);
        var biasAndGain = BiasAndGain.GetValue(context);
        var mode = (Modes)Mode.GetValue(context);

        var resultList = new List<float>(inputList.Count);
        var inRange = inMax - inMin;

        // Avoid division by zero if the input range is invalid
        if (Math.Abs(inRange) < 0.00001f)
        {
            // Fill the list with the lower bound of the output range
            for (var i = 0; i < inputList.Count; i++)
            {
                resultList.Add(outMin);
            }
            Result.Value = resultList;
            return;
        }

        foreach (var value in inputList)
        {
            var normalized = (value - inMin) / inRange;
            if (normalized > 0 && normalized < 1)
            {
                normalized = normalized.ApplyGainAndBias(biasAndGain.X, biasAndGain.Y);
            }

            var v = normalized * (outMax - outMin) + outMin;

            switch (mode)
            {
                case Modes.Clamped:
                    {
                        var min = Math.Min(outMin, outMax);
                        var max = Math.Max(outMin, outMax);
                        v = v.Clamp(min, max);
                        break;
                    }
                case Modes.Modulo:
                    {
                        var min = Math.Min(outMin, outMax);
                        var max = Math.Max(outMin, outMax);
                        var modRange = max - min;
                        if (Math.Abs(modRange) > 0.00001f)
                        {
                            v = min + MathUtils.Fmod(v - min, modRange);
                        }
                        else
                        {
                            v = min;
                        }
                        break;
                    }
            }
            resultList.Add(v);
        }
        Result.Value = resultList;
    }

    private enum Modes
    {
        Normal,
        Clamped,
        Modulo,
    }

    [Input(Guid = "fdc8c3ea-30cb-4f31-9469-435a87b34028")]
    public readonly InputSlot<List<float>> FloatList = new();

    [Input(Guid = "1bcb6cd5-bd78-4d9a-8944-c34d33126a7b")]
    public readonly InputSlot<float> RangeInMin = new();

    [Input(Guid = "fb98e84f-7fcf-4228-b440-665d622cb2a6")]
    public readonly InputSlot<float> RangeInMax = new();

    [Input(Guid = "7a1029cc-d0cb-4e77-88e6-485b6994c620")]
    public readonly InputSlot<float> RangeOutMin = new();

    [Input(Guid = "b518b98c-9f9b-43bb-ab1a-e26d82e6290f")]
    public readonly InputSlot<float> RangeOutMax = new();

    [Input(Guid = "80eed18c-5453-4088-9e96-e5d42f9776c3")]
    public readonly InputSlot<Vector2> BiasAndGain = new();

    [Input(Guid = "023147fa-fae5-446c-b24a-88b0cb6bc27b", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();
}