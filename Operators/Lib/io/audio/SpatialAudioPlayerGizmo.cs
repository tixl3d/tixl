namespace Lib.io.audio;

/// <summary>
/// A debug visualization composite operator for SpatialAudioPlayer.
/// The composite graph uses the input slots directly for visualization.
/// When embedded in SpatialAudioPlayer, the inputs are wired from the parent's inputs.
/// </summary>
[Guid("b26e6624-4017-46b5-970d-1525d90fafad")]
internal sealed class SpatialAudioPlayerGizmo : Instance<SpatialAudioPlayerGizmo>
{
    [Output(Guid = "4075a57f-ed26-46e0-baff-88809ac8f07d")]
    public readonly Slot<Command> Output = new();
    /// <summary>
    /// The context key used to retrieve SpatialAudioGizmoData from the EvaluationContext.
    /// Reserved for future use - currently the composite graph reads from input slots directly.
    /// </summary>
    [Input(Guid = "102ed409-f50c-4769-89e7-24f96993dfbf")]
    public readonly InputSlot<string> ContextKey = new();
    /// <summary>Position of the audio source in 3D space.</summary>
    [Input(Guid = "d68cfc97-8bf8-4b03-8b2d-9dba9175f36a")]
    public readonly InputSlot<Vector3> SourcePosition = new();
    /// <summary>Rotation of the audio source (for directional cone).</summary>
    [Input(Guid = "a7c899ee-28f4-444e-b111-4ea98a9ee5f8")]
    public readonly InputSlot<Vector3> SourceRotation = new();
    /// <summary>Position of the listener in 3D space.</summary>
    [Input(Guid = "1c7f4f37-e109-42f5-bf22-7149a946120d")]
    public readonly InputSlot<Vector3> ListenerPosition = new();
    /// <summary>Rotation/orientation of the listener.</summary>
    [Input(Guid = "8ec0be50-c509-432d-b2e1-d8822b90000b")]
    public readonly InputSlot<Vector3> ListenerRotation = new();
    /// <summary>Minimum distance for audio falloff.</summary>
    [Input(Guid = "d24e8381-df53-4afb-9573-e83c4ded337d")]
    public readonly InputSlot<float> MinDistance = new();
    /// <summary>Maximum distance for audio falloff.</summary>
    [Input(Guid = "708c414d-1017-4bc7-b650-265e8ed09613")]
    public readonly InputSlot<float> MaxDistance = new();
    /// <summary>Inner cone angle in degrees (full volume zone).</summary>
    [Input(Guid = "d1b4973a-a717-49da-af49-b3359bd6b609")]
    public readonly InputSlot<float> InnerConeAngle = new();
    /// <summary>Outer cone angle in degrees (attenuated volume zone).</summary>
    [Input(Guid = "cafd2a8d-ffa1-4441-b66a-001e383343ad")]
    public readonly InputSlot<float> OuterConeAngle = new();
    /// <summary>Length of the cone visualization. Typically matches MaxDistance for accurate representation.</summary>
    [Input(Guid = "73ab9ccd-bcec-4d28-9961-d05db79bd1e6")]
    public readonly InputSlot<float> ConeLength = new();
    /// <summary>Color used for the gizmo visualization.</summary>
    [Input(Guid = "5f6b0e74-e280-487f-8e61-bc3af7787254")]
    public readonly InputSlot<Vector4> Color = new();
    /// <summary>Controls the visibility of the gizmo.</summary>
    [Input(Guid = "92de2db7-49a5-49e1-bf7e-803c48ccb62c")]
    public readonly InputSlot<GizmoVisibility> Visibility = new();
}
