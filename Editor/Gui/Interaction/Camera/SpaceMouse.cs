#nullable enable
using T3.Editor.App;
using T3.Editor.Gui.UiHelpers;
using T3.IoServices;

namespace T3.Editor.Gui.Interaction.Camera;

/// <summary>
/// Gathers update event information from SpaceMouse and apply it to a <see cref="CameraSetup"/> while the SpaceMouse
/// is set to the camera. On Windows the device is read through Win32 raw input here, which also feeds
/// <see cref="SpaceMouseInput"/> for operators. Elsewhere <see cref="SpaceMouseInput"/> reads it and this applies its
/// current deflection over time.
/// </summary>
public sealed partial class SpaceMouse : ICameraManipulator, IWin32MessageHandler
{
    private readonly SpaceMouseDevice? _spaceMouseDevice;

    public SpaceMouse(IntPtr windowHandle)
    {
        _spaceMouseDevice = new(windowHandle);
    }

    /** Without a window handle the device is read by <see cref="SpaceMouseInput"/>. */
    public SpaceMouse()
    {
    }

    public void ManipulateCamera(CameraSetup intendedSetup, double frameTime)
    {
        if (!_initialized && _spaceMouseDevice != null)
        {
            _spaceMouseDevice.MotionEvent += SpaceMouseMotionHandler;
            _spaceMouseDevice.ButtonEvent += SpaceMouseButtonHandler;
        }

        _initialized = true;

        // Several cameras can be updated per frame; the deflection is applied once
        if (_spaceMouseDevice == null && frameTime != _lastPollTime)
        {
            var reportsInFrame = (float)Math.Clamp(frameTime - _lastPollTime, 0, MaxPollGapSec) * NominalReportsPerSecond;
            _lastPollTime = frameTime;
            var translation = SpaceMouseInput.Translation;
            var rotation = SpaceMouseInput.Rotation;
            if (IsSteeringCamera && (translation != Vector3.Zero || rotation != Vector3.Zero))
                AddMotion(translation * reportsInFrame, rotation * reportsInFrame);
        }

        var wasEvaluatedThisFrame = Math.Abs(frameTime - _lastUpdateTime) < 0.0001f;
        var stillMoving = _dampedRotation.LengthSquared() + _dampedTranslation.LengthSquared() > 0.001f;
            
        if (_eventCount == 0 && !stillMoving)
            return;

        if (!wasEvaluatedThisFrame)
        {
            var tooLong = Math.Abs(frameTime - _lastUpdateTime) > 0.5f;
            if (!tooLong)
            {
                // Older settings files may hold values the settings field no longer allows
                var damping = Math.Clamp(UserSettings.Config.SpaceMouseDamping, 0, Windows.SettingsWindow.MaxSpaceMouseDamping);
                _dampedRotation = Vector3.Lerp(_rotationSum, _dampedRotation, damping);
                _dampedTranslation = Vector3.Lerp(_translationSum, _dampedTranslation, damping);
            }
            else
            {
                _dampedRotation = Vector3.Zero;
                _dampedTranslation= Vector3.Zero;
            }
            _lastUpdateTime = frameTime;
        }
        _eventCount = 0;
        _translationSum = new Vector3(0, 0, 0);
        _rotationSum = new Vector3(0, 0, 0);

            
        var viewDir = intendedSetup.Target - intendedSetup.Position;
        var upDir = new Vector3(0, 1, 0);

        // Not normalized: moving sideways scales with the distance to the target, as tuned
        var sideDir = Vector3.Cross(upDir, viewDir);

        var viewDirLength = viewDir.Length();
        viewDir /= viewDirLength;

        float translationVelocity = _dampedTranslation.Length() / 2000.0f;
        var direction = _dampedTranslation;
        if(direction.LengthSquared() > 0.0001f)
            direction = Vector3.Normalize(direction);

        if (translationVelocity <  CameraInteractionParameters.MaxMoveVelocity)
            direction *= translationVelocity;
        else
            direction *= CameraInteractionParameters.MaxMoveVelocity;

        var moveDir = direction.X * sideDir - direction.Y * viewDir - direction.Z * upDir;
        intendedSetup.Position += moveDir;
        intendedSetup.Target += moveDir;

        // Tilting and twisting the cap turn the view; the pitch stops at the poles
        intendedSetup.LookAround(-_dampedRotation.Y / RawUnitsPerRadian, _dampedRotation.X / RawUnitsPerRadian);
    }

    private static bool IsSteeringCamera => UserSettings.Config.SpaceMouseUsage == UserSettings.InputDeviceUsages.Editor;

    private void SpaceMouseButtonHandler(object sender, SpaceMouseDevice.ButtonEventArgs e)
    {
        var pressed = e.ButtonMask?.Pressed ?? 0;
        SpaceMouseInput.SetButtonsFromRawInput((pressed & 1) != 0, (pressed & 2) != 0);
    }

    /** Raw input sums every report into the camera move, as it was tuned on Windows. */
    private void SpaceMouseMotionHandler(object sender, SpaceMouseDevice.MotionEventArgs e)
    {
        Vector3? translation = e.TranslationVector != null
                                   ? new Vector3(e.TranslationVector.X, e.TranslationVector.Y, e.TranslationVector.Z)
                                   : null;
        Vector3? rotation = e.RotationVector != null
                                ? new Vector3(e.RotationVector.X, e.RotationVector.Y, e.RotationVector.Z)
                                : null;
        SpaceMouseInput.SetFromRawInput(translation, rotation);

        if (IsSteeringCamera)
            AddMotion(translation ?? Vector3.Zero, rotation ?? Vector3.Zero);
    }

    /** Takes axes in HID order and orientation (X, Y, Z and Rx, Ry, Rz) from either platform. */
    private void AddMotion(Vector3 translation, Vector3 rotation)
    {
        var moveSpeed = UserSettings.Config.SpaceMouseMoveSpeedFactor;
        _translationSum.X -= translation.X * moveSpeed;
        _translationSum.Y += translation.Y * moveSpeed;
        _translationSum.Z += translation.Z * moveSpeed;

        // Swap axes from HID orientation to matching coordinates
        var rotationSpeed = UserSettings.Config.SpaceMouseRotationSpeedFactor;
        _rotationSum.X += rotation.X * rotationSpeed * 0.5f;
        _rotationSum.Y += rotation.Z * rotationSpeed;
        _rotationSum.Z += rotation.Y * rotationSpeed;
        _eventCount++;
    }
        
    private int _eventCount;
    private Vector3 _translationSum;
    private Vector3 _rotationSum;

    private Vector3 _dampedTranslation;
    private Vector3 _dampedRotation;

    private double _lastUpdateTime;
    private double _lastPollTime;
    private bool _initialized;

    /** Raw input reports arrive at about this rate, so a held deflection counts as that many reports per second. */
    private const float NominalReportsPerSecond = 125;

    private const double MaxPollGapSec = 0.1;

    /** Summed raw rotation (about 350 per report at full deflection) per radian of turn. */
    private const float RawUnitsPerRadian = 8000;

    public void ProcessMessage(int message, IntPtr wParam, IntPtr lParam) => _spaceMouseDevice?.ProcessMessage(message, wParam, lParam);
}