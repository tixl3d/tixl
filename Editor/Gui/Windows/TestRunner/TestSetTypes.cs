#nullable enable

namespace T3.Editor.Gui.Windows.TestRunner;

/// <summary>
/// In-memory representation of a parsed manual test set file. All fields are
/// derived from <c>.tests-manual/*.md</c>; nothing is persisted from the
/// runner side. See <c>.tests-manual/README.md</c> for the source format.
/// </summary>
internal sealed class TestSet
{
    public required string Id;
    public required string Title;
    public required string Scope;
    public required IReadOnlyList<string> Tags;
    public required IReadOnlyList<string> Prerequisites;
    public required IReadOnlyList<string> RelatedHelp;
    public required string Intro;
    public required IReadOnlyList<TestStep> Steps;
    public required string SourcePath;

    /// <summary>Non-fatal parse warnings — surfaced in the Pick UI.</summary>
    public required IReadOnlyList<string> ParseWarnings;
}

internal sealed class TestStep
{
    public required string Title;
    public required string? Context;
    public required IReadOnlyList<string> ActionBullets;
    public required IReadOnlyList<string> ExpectedBullets;
}

internal enum Outcome
{
    Pending,
    Pass,
    Fail,
    Other,
    Skipped,
}

internal sealed class StepResult
{
    public required string SetId;
    public required int StepIndex;
    public Outcome Outcome;
    public string Comment = string.Empty;
    public DateTime TimestampUtc;
}

/// <summary>
/// One walk-through of a chosen subset of <see cref="TestSet"/>s. Captures
/// every step's outcome and comment in a flat list so the Summary view can
/// regroup by set without rewalking the source.
/// </summary>
internal sealed class RunReport
{
    public DateTime StartedUtc;
    public DateTime FinishedUtc;
    public required IReadOnlyList<TestSet> Sets;
    public required List<StepResult> Results;
}
