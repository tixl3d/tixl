using System.Text.RegularExpressions;

namespace fixture.SampleProject.math;

/// <summary>Uses a [GeneratedRegex], which only compiles with the SDK's source generators.</summary>
[Guid("0f1e2d3c-4b5a-4968-8776-a5b4c3d2e1f0")]
internal sealed partial class MatchesPattern : Instance<MatchesPattern>
{
    [Output(Guid = "1a2b3c4d-5e6f-4071-8293-a4b5c6d7e8f9")]
    public readonly Slot<bool> Result = new();

    public MatchesPattern()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var text = Text.GetValue(context);
        Result.Value = text != null && Identifier().IsMatch(text);
    }

    [GeneratedRegex(@"^@[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    [Input(Guid = "9f8e7d6c-5b4a-4392-8170-6f5e4d3c2b1a")]
    public readonly InputSlot<string> Text = new();
}
