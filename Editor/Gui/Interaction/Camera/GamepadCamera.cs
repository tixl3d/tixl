#nullable enable
using ImGuiNET;
using T3.Editor.Gui.UiHelpers;
using T3.IoServices;

namespace T3.Editor.Gui.Interaction.Camera;

/// <summary>
/// Flies the camera of the output window (or the graph background) with the first game controller while the
/// controller is set to the camera (menu bar icon or Settings → Input Devices): the left stick moves sideways and
/// forward, the right stick looks around from the camera's position, the triggers move down (left) and up (right).
/// The sticks are smoothed over <see cref="UserSettings.ConfigData.GamepadCameraSmoothing"/>, so moves start and
/// stop gently. Speeds follow the distance to the camera target and the camera speed setting, like WASD.
/// </summary>
internal sealed class GamepadCamera : ICameraManipulator
{
    public void ManipulateCamera(CameraSetup intendedSetup, double frameTime)
    {
        // Several cameras can be updated per frame; the smoothing advances once
        if (frameTime != _lastFrameTime)
        {
            _lastFrameTime = frameTime;
            UpdateSmoothedInput();
        }

        if (_move == Vector2.Zero && _look == Vector2.Zero && _lift == 0)
            return;

        var viewDir = intendedSetup.Target - intendedSetup.Position;
        var viewDistance = viewDir.Length();
        if (viewDistance < MinViewDistance)
            return;

        viewDir /= viewDistance;
        var up = Vector3.UnitY;
        var left = Vector3.Normalize(Vector3.Cross(up, viewDir));
        var deltaTime = ImGui.GetIO().DeltaTime;
        var config = UserSettings.Config;

        // Looking straight up or down would flip the view, as with the SpaceMouse
        var pitchInput = config.GamepadCameraInvertLook ? -_look.Y : _look.Y;
        if (viewDir.Y > MaxPitchDot)
        {
            pitchInput = MathF.Min(pitchInput, 0);
        }
        else if (viewDir.Y < -MaxPitchDot)
        {
            pitchInput = MathF.Max(pitchInput, 0);
        }

        var lookSpeed = config.GamepadCameraLookSpeed * deltaTime;
        var yaw = -_look.X * YawRadiansPerSecond * lookSpeed;
        var pitch = pitchInput * PitchRadiansPerSecond * lookSpeed;
        var rotation = Matrix4x4.Multiply(Matrix4x4.CreateFromAxisAngle(left, -pitch), Matrix4x4.CreateFromAxisAngle(up, yaw));
        var newViewDir = Vector3.Normalize(Vector3.TransformNormal(viewDir, rotation));

        var speed = viewDistance * ViewDistancesPerSecond * config.CameraSpeed * config.GamepadCameraMoveSpeed * deltaTime;
        var offset = (newViewDir * _move.Y - left * _move.X + up * _lift) * speed;

        intendedSetup.Position += offset;
        intendedSetup.Target = intendedSetup.Position + newViewDir * viewDistance;
    }

    /// <summary>
    /// Shapes the sticks (squared, keeping the sign, so a light touch moves gently) and eases the result towards
    /// them. Without a controller steering the camera everything eases back to rest.
    /// </summary>
    private void UpdateSmoothedInput()
    {
        var targetMove = Vector2.Zero;
        var targetLook = Vector2.Zero;
        var targetLift = 0f;
        if (UserSettings.Config.GamepadUsage == UserSettings.GamepadUsages.Editor
            && SdlGamepads.TryGetState(0, out var state))
        {
            targetMove = Squared(state.LeftThumb);
            targetLook = Squared(state.RightThumb);
            var lift = state.RightTrigger - state.LeftTrigger;
            targetLift = MathF.Abs(lift) < LiftDeadZone ? 0 : lift * MathF.Abs(lift);
        }

        var smoothingSec = UserSettings.Config.GamepadCameraSmoothing;
        var blend = smoothingSec <= 0 ? 1 : 1 - MathF.Exp(-ImGui.GetIO().DeltaTime / smoothingSec);
        _move = SnapToRest(Vector2.Lerp(_move, targetMove, blend));
        _look = SnapToRest(Vector2.Lerp(_look, targetLook, blend));
        _lift += (targetLift - _lift) * blend;
        if (MathF.Abs(_lift) < RestThreshold)
            _lift = 0;
    }

    private static Vector2 Squared(Vector2 stick) => new(stick.X * MathF.Abs(stick.X), stick.Y * MathF.Abs(stick.Y));

    /** Ends the easing once it no longer moves anything visible, so the camera can settle. */
    private static Vector2 SnapToRest(Vector2 value) => value.LengthSquared() < RestThreshold * RestThreshold ? Vector2.Zero : value;

    private const float ViewDistancesPerSecond = 1f;
    private const float YawRadiansPerSecond = 1.5f;
    private const float PitchRadiansPerSecond = 1.0f;
    private const float MaxPitchDot = 0.95f;
    private const float LiftDeadZone = 0.05f;
    private const float MinViewDistance = 0.0001f;
    private const float RestThreshold = 0.001f;

    private double _lastFrameTime = double.NaN;
    private Vector2 _move;
    private Vector2 _look;
    private float _lift;
}
