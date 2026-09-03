#nullable enable
namespace T3.Core.Operator.Interfaces;

/// <summary>
/// Lets an operator report the progress of a long-running computation so the UI
/// can render a progress bar on its graph node. Return true only while progress
/// should actually be shown (e.g. a background job has been running long enough
/// that flashing a bar for trivial updates is avoided).
/// </summary>
public interface IProgressProvider
{
    bool TryGetProgress(out float progress);
}
