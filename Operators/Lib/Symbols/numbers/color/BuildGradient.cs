namespace Lib.numbers.color;

[Guid("f835b87a-05e6-4bde-931c-ca43524d4467")]
internal sealed class BuildGradient : Instance<BuildGradient>
{
    [Output(Guid = "a148ef69-0891-45b1-9ade-e8875f616f2f")]
    public readonly Slot<Gradient> OutGradient = new();

    public BuildGradient()
    {
        OutGradient.UpdateAction += Update;
        OutGradient.Value = _gradient;
    }

    
    private void Update(EvaluationContext context)
    {
        _gradient.Steps.Clear();
        var colors = Colors.GetValue(context) ?? [];
        var positions = Positions.GetValue(context) ?? [];

        // Fall back to normalized steps
        if (positions.Count == 0)
        {
            if (_normalizedPositions.Count != colors.Count)
            {
                _normalizedPositions.Clear();
                if (colors.Count == 1)
                {
                    _normalizedPositions.Add(0);
                }
                else
                {
                    for (var index = 0; index < colors.Count; index++)
                    {
                        _normalizedPositions.Add((float)index / (colors.Count-1));
                    }
                }
            }

            positions = _normalizedPositions;
        }
        

        var minCount = Math.Min(colors.Count, positions.Count);
        if (minCount == 0)
        {
            return;
        }

        for (var index = 0; index < minCount; index++)
        {
            //var slots = colors[index];
            var pos = positions[index];
            var color = colors[index];

            _gradient.Steps.Add(new Gradient.Step
                                    {
                                        NormalizedPosition = pos,
                                        Color = color,
                                        Id = IntToGuid(index)
                                    });
        }

        _gradient.SortHandles();
        _gradient.Interpolation = (Gradient.Interpolations)Interpolation.GetValue(context);
    }

    private readonly Gradient _gradient = new();
    private readonly List<float> _normalizedPositions = [];

    private static Guid IntToGuid(int value)
    {
        BitConverter.GetBytes(value).CopyTo(_bytes, 0);
        return new Guid(_bytes);
    }

    private static readonly byte[] _bytes = new byte[16];

    [Input(Guid = "C9132144-EA18-4B9C-8C82-1A7CD8F02CE9")]
    public readonly InputSlot<List<Vector4>> Colors = new();

    [Input(Guid = "E1DF93CB-9CE5-4941-B56B-EE5B2F0AFF8D")]
    public readonly InputSlot<List<float>> Positions = new();

    [Input(Guid = "918c99f2-2197-467d-a991-2f69ef81440c", MappedType = typeof(Gradient.Interpolations))]
    public readonly InputSlot<int> Interpolation = new();
}