#nullable enable
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.UiModel.Selection;

/// <summary>
/// Coordinates the editor's selection systems (the graph's <see cref="NodeSelection"/>, the output setup's
/// entity selection) around one rule: there is never more than one selected thing, and that thing is
/// what the Parameter window inspects. A system claims the inspection at pick time; claiming clears the
/// other system's selection, so the graph visibly deselects when a setup entity takes over and vice versa.
/// </summary>
internal static class GlobalSelectionHandling
{
    public enum InspectionTargets
    {
        None,
        GraphNode,
        SetupEntity,
    }

    /// <summary>Which selection system the Parameter window currently shows. <see cref="InspectionTargets.None"/>
    /// reads like <see cref="InspectionTargets.GraphNode"/> — the window falls back to the graph's composition.</summary>
    public static InspectionTargets InspectionTarget { get; private set; }

    /// <summary>Called by a selection system when the user picks in it. Always clears the other system's
    /// selection, even on a repeated claim — a mirrored entity row may have appeared in between.</summary>
    public static void ClaimInspection(InspectionTargets owner)
    {
        // The slot is set before the other side clears, so that clear's release finds a different owner and stays a no-op.
        InspectionTarget = owner;
        switch (owner)
        {
            case InspectionTargets.SetupEntity:
                ProjectView.Focused?.NodeSelection.Clear();
                break;
            case InspectionTargets.GraphNode:
                OutputSetupHandling.EntitySelection.Clear();
                break;
        }
    }

    /// <summary>Called by a selection system when it has nothing to inspect anymore. Only the current owner can release.</summary>
    public static void ReleaseInspection(InspectionTargets owner)
    {
        if (InspectionTarget == owner)
            InspectionTarget = InspectionTargets.None;
    }
}
