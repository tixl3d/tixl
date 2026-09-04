namespace T3.Editor.Gui.Interaction.Camera;

public static class CameraInteractionParameters
{
    public const float RenderWindowHeight = 450; // TODO: this should be derived from output window size
    public const float StopDistanceThreshold = 0.0001f;
    public const float RotateMouseSensitivity = 0.001f;
    public const float OrbitHorizontalDamping = 0.2f;
    public const float CameraMoveDamping = 0.12f;
    public const float CameraDamping = 0.5f;
    public const float ZoomSpeed = 10f;
    public const float MaxMoveVelocity = 1;
    public const float CameraAcceleration = 1;
}