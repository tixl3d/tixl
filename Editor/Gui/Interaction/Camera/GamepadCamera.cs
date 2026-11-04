#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Interaction.Gamepad;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Interaction.Camera;

/// <summary>
/// Flies the camera of the output window (or the graph background) while a game controller steers it: the left
/// stick moves sideways and forward, the right stick looks around from the camera's position, the triggers move
/// down (left) and up (right). Speeds follow the distance to the camera target and the camera speed setting, like
/// the keyboard's WASD, so the same deflection feels alike at any scale.
/// </summary>
internal sealed class GamepadCamera : ICameraManipulator
{
    public void ManipulateCamera(CameraSetup intendedSetup, double frameTime)
    {
        if (!GamepadControl.IsSteeringCamera)
            return;

        var move = GamepadControl.CameraMoveStick;
        var look = GamepadControl.CameraLookStick;
        var lift = GamepadControl.CameraLift;
        if (move == Vector2.Zero && look == Vector2.Zero && MathF.Abs(lift) < LiftDeadZone)
            return;

        var viewDir = intendedSetup.Target - intendedSetup.Position;
        var viewDistance = viewDir.Length();
        if (viewDistance < MinViewDistance)
            return;

        viewDir /= viewDistance;
        var up = Vector3.UnitY;
        var left = Vector3.Normalize(Vector3.Cross(up, viewDir));
        var deltaTime = ImGui.GetIO().DeltaTime;

        // Looking straight up or down would flip the view, as with the SpaceMouse
        var pitchInput = look.Y;
        if (viewDir.Y > MaxPitchDot)
            pitchInput = MathF.Min(pitchInput, 0);
        else if (viewDir.Y < -MaxPitchDot)
            pitchInput = MathF.Max(pitchInput, 0);

        var yaw = -Squared(look.X) * YawRadiansPerSecond * deltaTime;
        var pitch = Squared(pitchInput) * PitchRadiansPerSecond * deltaTime;
        var rotation = Matrix4x4.Multiply(Matrix4x4.CreateFromAxisAngle(left, -pitch), Matrix4x4.CreateFromAxisAngle(up, yaw));
        var newViewDir = Vector3.Normalize(Vector3.TransformNormal(viewDir, rotation));

        var speed = viewDistance * ViewDistancesPerSecond * UserSettings.Config.CameraSpeed * deltaTime;
        var offset = (newViewDir * Squared(move.Y) - left * Squared(move.X) + up * Squared(lift)) * speed;

        intendedSetup.Position += offset;
        intendedSetup.Target = intendedSetup.Position + newViewDir * viewDistance;
    }

    /** Keeps the sign, so a light touch moves gently and full deflection quickly. */
    private static float Squared(float value) => value * MathF.Abs(value);

    private const float ViewDistancesPerSecond = 1f;
    private const float YawRadiansPerSecond = 1.5f;
    private const float PitchRadiansPerSecond = 1.0f;
    private const float MaxPitchDot = 0.95f;
    private const float LiftDeadZone = 0.05f;
    private const float MinViewDistance = 0.0001f;
}
