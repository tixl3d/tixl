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

    /// <summary>When the set was first added (from the `added` frontmatter). <see cref="DateTime.MinValue"/> if absent — sorts oldest.</summary>
    public required DateTime Added;

    /// <summary>TiXL <c>major.minor</c> the set first shipped in (from `added-in-version`). Empty if absent.</summary>
    public required string AddedInVersion;
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
    /// <summary>Optional one-line context. Will be folded into the Action body
    /// at render time so the user reads a single, narrative instruction.</summary>
    public required string? Context;
    /// <summary>Raw markdown for the Action body — prose, bullets, or both.</summary>
    public required string ActionMarkdown;
    /// <summary>Raw markdown for the Expected Results body.</summary>
    public required string ExpectedMarkdown;
}

internal enum Outcome
{
    Pending,
    Pass,
    Fail,
    Other,
    Skipped,
}

/// <summary>Display order for the test-set list. Order matches the runner's sort dropdown.</summary>
internal enum TestSetSort
{
    RecentlyAdded,
    Alphabetical,
    ByScope,
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
    /// <summary>Stable id for this run — used in the exported JSON and as a key when sharing results.</summary>
    public Guid Id = Guid.NewGuid();
    public DateTime StartedUtc;
    public DateTime FinishedUtc;
    public required IReadOnlyList<TestSet> Sets;
    public required List<StepResult> Results;
}
