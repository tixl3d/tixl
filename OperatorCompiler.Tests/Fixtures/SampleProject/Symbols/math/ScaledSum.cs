namespace fixture.SampleProject.math;

/// <summary>Exercises constructs whose code generation differs between compiler versions or settings.</summary>
[Guid("5e0c3c8a-63f4-4b5d-9a2e-0f6b7c1d2e3f")]
internal sealed class ScaledSum : Instance<ScaledSum>
{
    [Output(Guid = "8a1b2c3d-4e5f-4a6b-9c7d-0e1f2a3b4c5d")]
    public readonly Slot<float> Result = new();

    public ScaledSum()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var factor = Factor.GetValue(context);
        int[] weights = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        Span<float> scaled = stackalloc float[weights.Length];
        for (var index = 0; index < weights.Length; index++)
            scaled[index] = weights[index] * factor;

        var labels = weights.Where(w => w % 2 == 0).Select(w => $"w{w}").ToList();
        var mode = labels.Count switch
                       {
                           0 => Mode.Empty,
                           < 6 => Mode.Few,
                           _ => Mode.Many,
                       };

        Result.Value = Sum(scaled) + (int)mode + Count(labels, l => l.Length > 2);
        return;

        static float Sum(ReadOnlySpan<float> values)
        {
            var total = 0f;
            foreach (var value in values)
                total += value;
            return total;
        }
    }

    private static int Count<T>(List<T> items, Func<T, bool> predicate) => items.Count(predicate);

    private enum Mode
    {
        Empty,
        Few,
        Many,
    }

    [Input(Guid = "c4d5e6f7-0819-4a2b-8c3d-4e5f6a7b8c9d")]
    public readonly InputSlot<float> Factor = new();
}
