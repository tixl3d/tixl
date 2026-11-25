namespace fixture.BrokenProject;

/// <summary>Fails to compile in several ways, so both compilers can be compared on their errors.</summary>
[Guid("3c4d5e6f-7081-4a92-b3c4-d5e6f708192a")]
internal sealed class Broken : Instance<Broken>
{
    [Output(Guid = "4d5e6f70-8192-4a3b-c4d5-e6f708192a3b")]
    public readonly Slot<float> Result = new();

    public Broken()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var helper = new MissingHelper();
        Result.Value = undefinedValue + helper.Value;
        string text = 42;
    }
}
