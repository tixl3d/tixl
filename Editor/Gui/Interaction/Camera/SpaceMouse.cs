#if PLATFORM_WINDOWS
using System.Windows.Forms;
using T3.Editor.App;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Interaction.Camera;

/// <summary>
/// Gathers update event information from SpaceMouse and apply it to a <see cref="CameraSetup"/>. 
/// </summary>
public sealed partial class SpaceMouse : ICameraManipulator, IWindowsFormsMessageHandler
{
    private readonly SpaceMouseDevice _spaceMouseDevice;
    public SpaceMouse(IntPtr windowHandle)
    {
        _spaceMouseDevice = new(windowHandle);
    }
        
    public void ManipulateCamera(CameraSetup intendedSetup, double frameTime)
    {
        if (!_initialized)
        {
            _spaceMouseDevice.MotionEvent += SpaceMouseMotionHandler;
            _spaceMouseDevice.ButtonEvent += SpaceMouseButtonHandler;
            _initialized = true;
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
                _dampedRotation= Vector3.Lerp( _rotationSum,_dampedRotation, UserSettings.Config.SpaceMouseDamping);
                _dampedTranslation= Vector3.Lerp( _translationSum, _dampedTranslation,UserSettings.Config.SpaceMouseDamping);
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
        var sideDir = Vector3.Cross(upDir, viewDir);
            
        var viewDirLength = viewDir.Length();
        viewDir /= viewDirLength;
            
        // Prevent gimbal lock
        if (viewDir.Y > 0.95f)
        {
            _dampedRotation.X = MathF.Min(_dampedRotation.X, 0);
        }
        else if (viewDir.Y < -0.95f)
        {
            _dampedRotation.X = MathF.Max(_dampedRotation.X, 0);
        }

        float translationVelocity = _dampedTranslation.Length() / 2000.0f;
        var direction = _dampedTranslation;
        if(direction.LengthSquared() > 0.0001f)
            direction = Vector3.Normalize(direction);

        if (translationVelocity <  CameraInteractionParameters.MaxMoveVelocity)
            direction *= translationVelocity;
        else
            direction *= CameraInteractionParameters.MaxMoveVelocity;

        var moveDir = direction.X * sideDir - direction.Y * viewDir - direction.Z * upDir;

        var rotAroundX = Matrix4x4.CreateFromAxisAngle(sideDir, -_dampedRotation.X / 8000.0f);
        var rotAroundY = Matrix4x4.CreateFromAxisAngle(upDir, -_dampedRotation.Y / 8000.0f);
        var rot = Matrix4x4.Multiply(rotAroundX, rotAroundY);
        var newViewDir = Vector3.Transform(viewDir, rot);
        newViewDir = Vector3.Normalize(newViewDir);

        var oldPosition = intendedSetup.Position;
        intendedSetup.Position += moveDir;

        intendedSetup.Target = oldPosition + (moveDir + newViewDir * viewDirLength);
        //Log.Debug($"space mouse move: {moveDir}  eventCount:{_eventCount}");

    }
        
    private void SpaceMouseButtonHandler(object sender, SpaceMouseDevice.ButtonEventArgs e)
    {
        // Todo: Think about what the buttons can be used for
    }

    private void SpaceMouseMotionHandler(object sender, SpaceMouseDevice.MotionEventArgs e)
    {
        if (e.TranslationVector != null)
        {
            _translationSum.X -= e.TranslationVector.X * UserSettings.Config.SpaceMouseMoveSpeedFactor;
            _translationSum.Y += e.TranslationVector.Y * UserSettings.Config.SpaceMouseMoveSpeedFactor;
            _translationSum.Z += e.TranslationVector.Z * UserSettings.Config.SpaceMouseMoveSpeedFactor;
        }
        if (e.RotationVector != null)
        {
            // Swap axes from HID orientation to matching coordinates
            _rotationSum.X += e.RotationVector.X * UserSettings.Config.SpaceMouseRotationSpeedFactor * 0.5f;
            _rotationSum.Y += e.RotationVector.Z * UserSettings.Config.SpaceMouseRotationSpeedFactor;
            _rotationSum.Z += e.RotationVector.Y * UserSettings.Config.SpaceMouseRotationSpeedFactor;
        }
        _eventCount++;
    }
        
    private int _eventCount;
    private Vector3 _translationSum;
    private Vector3 _rotationSum;

    private Vector3 _dampedTranslation;
    private Vector3 _dampedRotation;

    private double _lastUpdateTime;
    private bool _initialized;
    public void ProcessMessage(Message message) => _spaceMouseDevice.ProcessMessage(message);
}
#endif // PLATFORM_WINDOWS