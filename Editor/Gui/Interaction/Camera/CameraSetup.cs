using T3.Core.Operator.Interfaces;
using T3.Core.Utils.Geometry;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Interaction.Camera;

/// <summary>
/// Describes a complete setup of a camera position and rotation. This
/// is used for blending between cameras or smoothing camera interactions. 
/// </summary>
/// <remarks>Setup is a slightly misleading name. PositionAndOrientation would
/// clearer but is too long.</remarks>
public sealed class CameraSetup
{
    public Vector3 Position = new(0, 0, GraphicsMath.DefaultCameraDistance);
    public Vector3 Target;

    public void Reset()
    {
        Position = new Vector3(0, 0, GraphicsMath.DefaultCameraDistance);
        Target = Vector3.Zero;
        UserSettings.Config.CameraSpeed = 1;
    }

    public bool MatchesSetup(CameraSetup other)
    {
        return Vector3.Distance(other.Position, Position) < CameraInteractionParameters.StopDistanceThreshold
               && Vector3.Distance(other.Target, Target) < CameraInteractionParameters.StopDistanceThreshold;
    }

    public void SetTo(CameraSetup other)
    {
        Position = other.Position;
        Target = other.Target;
    }

    public void SetTo(ICamera cameraInstance)
    {
        Position = cameraInstance.CameraPosition;
        Target = cameraInstance.CameraTarget;
    }

    public bool Matches(ICamera cameraInstance)
    {
        return Vector3.Distance(cameraInstance.CameraPosition, Position) < CameraInteractionParameters.StopDistanceThreshold
               && Vector3.Distance(cameraInstance.CameraTarget, Target) < CameraInteractionParameters.StopDistanceThreshold;
    }

    public void BlendTo(CameraSetup intended, float cameraMoveFriction, float deltaTime)
    {
        var f = deltaTime * cameraMoveFriction * 60;
        Position = Vector3.Lerp(Position, intended.Position, f);
        Target = Vector3.Lerp(Target, intended.Target, f);
    }
    
    public void BlendTo(CameraSetup intended, float factor)
    {
        //var f = deltaTime * cameraMoveFriction * 60;
        Position = Vector3.Lerp(Position, intended.Position, factor);
        Target = Vector3.Lerp(Target, intended.Target, factor);
    }
}