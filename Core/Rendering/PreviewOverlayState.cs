#nullable enable
using System;
using T3.Core.DataTypes;

namespace T3.Core.Rendering;

/// <summary>What an output window's preview draws on top of the shown value, toggled per window in its toolbar.</summary>
[Flags]
public enum PreviewOverlays
{
    None = 0,
    Wireframe = 1,
    Points = 2,
    PointIndices = 4,
    Selection = 8,
    Hover = 16,
    Normals = 32,
}

/// <summary>
/// What the editor asks of a preview op like [_OutputPreviewGeometry]: which overlays to draw and which elements are
/// selected or hovered in the output window's table. The output window sets it on
/// <see cref="T3.Core.Operator.EvaluationContext.PreviewOverlay"/> before the preview renders; preview graphs read it
/// with [GetPreviewState]. Like <see cref="RenderPassState"/> it is typed engine state rather than context variables.
/// </summary>
public struct PreviewOverlayState
{
    /// <summary>Shown when nothing set the state, e.g. a preview op evaluated outside an output window.</summary>
    public const PreviewOverlays Defaults = PreviewOverlays.Wireframe | PreviewOverlays.Points | PreviewOverlays.Selection | PreviewOverlays.Hover;

    /// <summary>False until an output window sets the state; <see cref="Shows"/> then uses <see cref="Defaults"/>.</summary>
    public bool IsSet;

    public PreviewOverlays Overlays;

    /// <summary>The table's selected elements as a small geometry (points, edges as curves, primitives), or null.</summary>
    public Geometry? Selected;

    /// <summary>The element under the mouse in the table, like <see cref="Selected"/>, or null.</summary>
    public Geometry? Hovered;

    public readonly bool Shows(PreviewOverlays overlay) => ((IsSet ? Overlays : Defaults) & overlay) != 0;
}
