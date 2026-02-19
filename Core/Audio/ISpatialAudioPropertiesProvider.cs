using T3.Core.Operator;

namespace T3.Core.Audio;

/// <summary>
/// Interface for operators that provide spatial audio properties.
/// Used by universal gizmo operators to discover and visualize all spatial audio sources.
/// </summary>
public interface ISpatialAudioPropertiesProvider
{
    /// <summary>Position of the audio source in 3D space.</summary>
    Vector3 SourcePosition { get; }
    
    /// <summary>Rotation of the audio source (for directional cone), in Euler angles (degrees).</summary>
    Vector3 SourceRotation { get; }
    
    /// <summary>Position of the listener in 3D space.</summary>
    Vector3 ListenerPosition { get; }
    
    /// <summary>Rotation/orientation of the listener, in Euler angles (degrees).</summary>
    Vector3 ListenerRotation { get; }
    
    /// <summary>Minimum distance for audio falloff.</summary>
    float MinDistance { get; }
    
    /// <summary>Maximum distance for audio falloff.</summary>
    float MaxDistance { get; }
    
    /// <summary>Inner cone angle in degrees (full volume zone).</summary>
    float InnerConeAngle { get; }
    
    /// <summary>Outer cone angle in degrees (attenuated volume zone).</summary>
    float OuterConeAngle { get; }
    
    /// <summary>Gizmo visibility setting for the spatial audio source.</summary>
    GizmoVisibility GizmoVisibility { get; }
}
