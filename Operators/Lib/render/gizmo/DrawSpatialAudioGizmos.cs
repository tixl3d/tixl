namespace Lib.render.gizmo;

/// <summary>
/// Draws gizmos for all spatial audio players in the current composition.
/// Iterates through all SpatialAudioPlayer instances and visualizes their source positions,
/// listener positions, attenuation ranges, and directional cones.
/// </summary>
[Guid("b53e6425-f40a-449c-846c-b4e0b8306a43")]
internal sealed class DrawSpatialAudioGizmos : Instance<DrawSpatialAudioGizmos>
{
    [Output(Guid = "fef51122-2c71-4b67-9d24-feb02ceb03f7")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "9c733ca8-0f77-4664-9680-609f262ee4a8")]
    public readonly InputSlot<GizmoVisibility> Visibility = new();
}
