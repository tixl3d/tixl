#nullable enable
using T3.Core.Audio;
using T3.Core.Utils;
// ReSharper disable InconsistentNaming

namespace Lib.io.audio.@_;

/// <summary>
/// Retrieves all spatial audio players in the current composition and provides their properties.
/// Used by DrawSpatialAudioGizmos to visualize all spatial audio sources.
/// </summary>
[Guid("91f249cf-a398-4261-8654-22637a1b9c12")]
internal sealed class GetAllSpatialAudioPlayers : Instance<GetAllSpatialAudioPlayers>
{
    [Output(Guid = "38381654-017c-4856-ae28-7dbc9f6b0fc8")]
    private readonly Slot<Vector3> SourcePosition = new();

    [Output(Guid = "619b45b5-4749-431b-b6a0-5edce7d263f5")]
    private readonly Slot<Vector3> SourceRotation = new();

    [Output(Guid = "bb6aeccb-bc35-46a1-ade2-0f089ec8c92c")]
    private readonly Slot<Vector3> ListenerPosition = new();

    [Output(Guid = "3d983835-8f8d-4e4b-abfe-7955b01a1581")]
    private readonly Slot<Vector3> ListenerRotation = new();

    [Output(Guid = "859295a6-f928-4443-9eca-2e800f275235")]
    private readonly Slot<float> MinDistance = new();

    [Output(Guid = "16de1964-9ae7-42e1-8ee5-6872b9c529a5")]
    private readonly Slot<float> MaxDistance = new();

    [Output(Guid = "fdde0e02-f7bb-4e7c-a542-0140894c8d0a")]
    private readonly Slot<float> InnerConeAngle = new();

    [Output(Guid = "fab6f132-2d5a-42ff-83cd-e35d64bf19e4")]
    private readonly Slot<float> OuterConeAngle = new();

    [Output(Guid = "b7e0d227-ceb8-4cf6-a3fa-fd956ad86869")]
    private readonly Slot<int> FramesSinceLastUpdate = new();

    [Output(Guid = "45415e90-2a4d-400c-90a3-63426c091b7e")]
    private readonly Slot<int> SpatialAudioPlayerCount = new();
    
    [Output(Guid = "4c4eb902-9d1e-41a9-9794-7d9a10b3a5a1")]
    private readonly Slot<GizmoVisibility> GizmoVisibility = new();

    public GetAllSpatialAudioPlayers()
    {
        SpatialAudioPlayerCount.UpdateAction += Update;
        SourcePosition.UpdateAction += Update;
        SourceRotation.UpdateAction += Update;
        ListenerPosition.UpdateAction += Update;
        ListenerRotation.UpdateAction += Update;
        MinDistance.UpdateAction += Update;
        MaxDistance.UpdateAction += Update;
        InnerConeAngle.UpdateAction += Update;
        OuterConeAngle.UpdateAction += Update;
        FramesSinceLastUpdate.UpdateAction += Update;
        GizmoVisibility.UpdateAction += Update;
    }

    private readonly List<ISpatialAudioPropertiesProvider> _spatialAudioInstances = new();

    private void Update(EvaluationContext context)
    {
        if (Parent?.Parent == null)
        {
            Log.Warning("Can't find composition", this);
            return;
        }

        _spatialAudioInstances.Clear();
        foreach (var child in Parent.Parent.Children.Values)
        {
            if (child is not ISpatialAudioPropertiesProvider spatialAudio)
                continue;

            _spatialAudioInstances.Add(spatialAudio);
        }

        SpatialAudioPlayerCount.Value = _spatialAudioInstances.Count;

        var index = SpatialAudioPlayerIndex.GetValue(context).Clamp(0, 10000);

        if (_spatialAudioInstances.Count == 0)
        {
            Log.Debug("No spatial audio players found", this);
            return;
        }

        var spatialPlayer = _spatialAudioInstances[index.Mod(_spatialAudioInstances.Count)];

        if (spatialPlayer is Instance instance && instance.Outputs.Count > 0)
        {
            var firstOutput = instance.Outputs[0];
            FramesSinceLastUpdate.Value = firstOutput.DirtyFlag.FramesSinceLastUpdate;
        }
        else
        {
            FramesSinceLastUpdate.Value = 999999;
        }

        SourcePosition.Value = spatialPlayer.SourcePosition;
        SourceRotation.Value = spatialPlayer.SourceRotation;
        ListenerPosition.Value = spatialPlayer.ListenerPosition;
        ListenerRotation.Value = spatialPlayer.ListenerRotation;
        MinDistance.Value = spatialPlayer.MinDistance;
        MaxDistance.Value = spatialPlayer.MaxDistance;
        InnerConeAngle.Value = spatialPlayer.InnerConeAngle;
        OuterConeAngle.Value = spatialPlayer.OuterConeAngle;
        GizmoVisibility.Value = spatialPlayer.GizmoVisibility;

        // Prevent double evaluation when accessing multiple outputs
        SpatialAudioPlayerCount.DirtyFlag.Clear();
        SourcePosition.DirtyFlag.Clear();
        SourceRotation.DirtyFlag.Clear();
        ListenerPosition.DirtyFlag.Clear();
        ListenerRotation.DirtyFlag.Clear();
        MinDistance.DirtyFlag.Clear();
        MaxDistance.DirtyFlag.Clear();
        InnerConeAngle.DirtyFlag.Clear();
        OuterConeAngle.DirtyFlag.Clear();
        FramesSinceLastUpdate.DirtyFlag.Clear();
        GizmoVisibility.DirtyFlag.Clear();
    }

    [Input(Guid = "5c07c8a2-b1da-42c1-addf-8ef5877784d7")]
    private readonly InputSlot<int> SpatialAudioPlayerIndex = new();
}
