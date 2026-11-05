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
        BlendTo(intended, deltaTime * cameraMoveFriction * 60);
    }

    /// <summary>
    /// Eases towards <paramref name="intended"/> in angles rather than points: yaw takes the shorter way, pitch and
    /// distance blend linearly. Blending the points would cut straight through the pole while orbiting near it, which
    /// flips the view by 180°. The end that moved less (the target while orbiting or dollying, the position while
    /// looking around) is blended as a point and the other one rebuilt from the blended direction.
    /// </summary>
    public void BlendTo(CameraSetup intended, float factor)
    {
        factor = Math.Clamp(factor, 0, 1);
        var view = Target - Position;
        var intendedView = intended.Target - intended.Position;
        var distance = view.Length();
        var intendedDistance = intendedView.Length();
        if (distance < MinDistance || intendedDistance < MinDistance)
        {
            Position = Vector3.Lerp(Position, intended.Position, factor);
            Target = Vector3.Lerp(Target, intended.Target, factor);
            return;
        }

        GetYawPitch(view / distance, out var yaw, out var pitch);
        GetYawPitch(intendedView / intendedDistance, out var intendedYaw, out var intendedPitch);

        var yawDelta = MathF.IEEERemainder(intendedYaw - yaw, MathF.Tau);
        var direction = FromYawPitch(yaw + yawDelta * factor, pitch + (intendedPitch - pitch) * factor);
        var blendedDistance = distance + (intendedDistance - distance) * factor;

        var targetMoved = Vector3.DistanceSquared(Target, intended.Target);
        var positionMoved = Vector3.DistanceSquared(Position, intended.Position);
        if (targetMoved <= positionMoved)
        {
            Target = Vector3.Lerp(Target, intended.Target, factor);
            Position = Target - direction * blendedDistance;
        }
        else
        {
            Position = Vector3.Lerp(Position, intended.Position, factor);
            Target = Position + direction * blendedDistance;
        }
    }

    /// <summary>
    /// Turns the view around the camera position. Positive yaw turns left, positive pitch looks up. Yaw is around the
    /// world's up axis; pitch stops just short of straight up or down instead of flipping over, so turning sideways
    /// there spins the image like a roll.
    /// </summary>
    public void LookAround(float yawLeftRad, float pitchUpRad)
    {
        var view = Target - Position;
        var distance = view.Length();
        if (distance < MinDistance)
            return;

        Target = Position + RotateDirection(view / distance, yawLeftRad, pitchUpRad) * distance;
    }

    /// <summary>
    /// Moves the camera around its target, keeping the distance. The view direction turns like <see cref="LookAround"/>:
    /// positive yaw turns it left, positive pitch up, with the same stop at the poles.
    /// </summary>
    public void Orbit(float yawLeftRad, float pitchUpRad)
    {
        var view = Target - Position;
        var distance = view.Length();
        if (distance < MinDistance)
            return;

        Position = Target - RotateDirection(view / distance, yawLeftRad, pitchUpRad) * distance;
    }

    /// <summary>
    /// Applies yaw and pitch to a normalized direction in world space. The pitch limit only blocks moving further, so
    /// a camera already looking straight down doesn't snap.
    /// </summary>
    private static Vector3 RotateDirection(Vector3 direction, float yawLeftRad, float pitchUpRad)
    {
        GetYawPitch(direction, out var yaw, out var pitch);

        var newPitch = pitch + pitchUpRad;
        if (pitchUpRad > 0)
        {
            newPitch = MathF.Min(newPitch, MathF.Max(pitch, MaxPitchRad));
        }
        else
        {
            newPitch = MathF.Max(newPitch, MathF.Min(pitch, -MaxPitchRad));
        }

        return FromYawPitch(yaw + yawLeftRad, newPitch);
    }

    /** Yaw 0 looks down -Z and grows turning left; pitch grows looking up. Straight up or down has no yaw and gives 0. */
    private static void GetYawPitch(Vector3 direction, out float yaw, out float pitch)
    {
        var horizontal = MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
        pitch = MathF.Atan2(direction.Y, horizontal);
        yaw = horizontal > MinDistance ? MathF.Atan2(-direction.X, -direction.Z) : 0;
    }

    private static Vector3 FromYawPitch(float yaw, float pitch)
    {
        var cosPitch = MathF.Cos(pitch);
        return new Vector3(-MathF.Sin(yaw) * cosPitch, MathF.Sin(pitch), -MathF.Cos(yaw) * cosPitch);
    }

    /** Position and target can't express looking exactly straight up or down, so the stop is a hair before. */
    private const float MaxPitchRad = 89.9f * MathF.PI / 180;

    private const float MinDistance = 0.00001f;
}