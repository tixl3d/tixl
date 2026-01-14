#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Operator.Interfaces;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.TransformGizmos;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Interaction.Camera;

/// <summary>
/// Controls the manipulation of camera view within an 3d output window.
/// Each output window has its own instance. 
/// </summary>
internal sealed class CameraInteraction
{
    internal static ICameraManipulator[] ManipulationDevices = [];

    internal void Update(ICamera? camera, bool allowCameraInteraction)
    {
        if (camera == null)
            return;
        
        if (!camera.CameraTarget._IsFinite() || !camera.CameraPosition._IsFinite() ||  !_intendedSetup.Position._IsFinite() || !_smoothedSetup.Position._IsFinite() || !_intendedSetup.Target._IsFinite() || !_smoothedSetup.Target._IsFinite())
        {
            _intendedSetup.Reset();
            _smoothedSetup.Reset();
            camera.CameraTarget = _intendedSetup.Target;
            camera.CameraPosition = _intendedSetup.Position;
        }
        
        var cameraNodeModified = !_smoothedSetup.Matches(camera) && !_intendedSetup.Matches(camera);
        if (cameraNodeModified)
        {
            _intendedSetup.SetTo(camera);
            _smoothedSetup.SetTo(camera);
            return;
        }

        _viewAxis.ComputeForCamera(camera);
        //_deltaTime = ImGui.GetIO().DeltaTime;
        _deltaTime = (float)(Playback.LastFrameDuration).ClampMax(4/60f);

        var cameraSwitched = camera != _lastCameraNode;
        if (cameraSwitched)
        {
            _intendedSetup.SetTo(camera);
            _smoothedSetup.SetTo(camera);
            _moveVelocity = Vector3.Zero;
            _lastCameraNode = camera;
        }

        var wasManipulated = false;

        if (allowCameraInteraction)
        {
            wasManipulated |= ManipulateCameraByMouse();
            wasManipulated |= ManipulateCameraByKeyboard();
        }

        var frameTime = ImGui.GetTime();

        foreach (var device in ManipulationDevices)
        {
            device.ManipulateCamera(_intendedSetup, frameTime);
        }

        var updateRequired = ComputeSmoothMovement(wasManipulated);
        if (!updateRequired)
            return;

        camera.CameraPosition = _smoothedSetup.Position;
        camera.CameraTarget = _smoothedSetup.Target;
    }

    public void ResetView()
    {
        _moveVelocity = Vector3.Zero;
        _intendedSetup.Reset();
    }

    private bool ComputeSmoothMovement(bool isInteracting)
    {
        var stillDamping = !_smoothedSetup.MatchesSetup(_intendedSetup);
        var stillSliding = _moveVelocity.Length() > CameraInteractionParameters.StopDistanceThreshold;
        var stillOrbiting = _orbitVelocity.Length() > 0.0002f;

        var cameraIsStillMoving = stillDamping
                                  || stillSliding
                                  || stillOrbiting
                                  || _manipulatedByMouseWheel
                                  || _manipulatedByKeyboard;

        if (!isInteracting && !cameraIsStillMoving)
        {
            _smoothedSetup.SetTo(_intendedSetup);
            _orbitVelocity = Vector2.Zero;
            _moveVelocity = Vector3.Zero;
            return false;
        }

        if (_orbitVelocity.Length() > 0.0002f)
        {
            ApplyOrbitVelocity(_orbitVelocity);
            _orbitVelocity = Vector2.Lerp(_orbitVelocity, Vector2.Zero, (_deltaTime * CameraInteractionParameters.OrbitHorizontalDamping * 60).ClampMax(1f));
        }

        var maxVelocityForScale = _viewAxis.ViewDistance.Length() * CameraInteractionParameters.MaxMoveVelocity * UserSettings.Config.CameraSpeed;
        if (_moveVelocity.Length() > maxVelocityForScale)
        {
            _moveVelocity *= maxVelocityForScale / _moveVelocity.Length();
        }

        if (!_manipulatedByKeyboard)
        {
            _moveVelocity = Vector3.Lerp(_moveVelocity, Vector3.Zero, (_deltaTime * CameraInteractionParameters.CameraMoveDamping * 60f).ClampMax(1f));
        }

        _intendedSetup.Position += _moveVelocity * _deltaTime;
        _intendedSetup.Target += _moveVelocity * _deltaTime;

        _smoothedSetup.BlendTo(_intendedSetup, (CameraInteractionParameters.CameraDamping * _deltaTime * 60f).Clamp(0,1));

        _manipulatedByMouseWheel = false;
        _manipulatedByKeyboard = false;
        return true;
    }

    private bool ManipulateCameraByMouse()
    {
        if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup | ImGuiHoveredFlags.ChildWindows))
            return false;

        var modified = false;
        modified |= HandleMouseWheel();
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            if (ImGui.GetIO().KeyAlt)
            {
                Pan();
            }
            else if (ImGui.GetIO().KeyCtrl)
            {
                modified |= LookAround();
            }
            else
            {
                var delta = new Vector2(ImGui.GetIO().MouseDelta.X,
                                        -ImGui.GetIO().MouseDelta.Y);
                if (delta.Length() > 0)
                {
                    modified = true;
                    _orbitVelocity += delta * _deltaTime * -0.1f;
                }
            }
        }
        else if (ImGui.IsMouseDragging(ImGuiMouseButton.Right) && !CustomComponents.IsDragScrolling && !ScalableCanvas.IsAnyCanvasDragged)
        {
            modified |= Pan();
        }
        else if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
        {
            modified |= LookAround();
        }

        return modified;
    }

    private bool HandleMouseWheel()
    {
        var delta = ImGui.GetIO().MouseWheel;
        if (Math.Abs(delta) < 0.01f)
            return false;

        var viewDistance = _intendedSetup.Position - _intendedSetup.Target;
        var zoomFactorForCurrentFramerate = 1 + (CameraInteractionParameters.ZoomSpeed * FrameDurationFactor);

        if (UserSettings.Config.AdjustCameraSpeedWithMouseWheel)
        {
            if (
                ImGui.IsMouseDown(ImGuiMouseButton.Left)
                || ImGui.IsMouseDown(ImGuiMouseButton.Middle)
                || ImGui.IsMouseDown(ImGuiMouseButton.Right))
            {
                if (delta < 0)
                {
                    UserSettings.Config.CameraSpeed /= zoomFactorForCurrentFramerate;
                }

                if (delta > 0)
                {
                    UserSettings.Config.CameraSpeed *= zoomFactorForCurrentFramerate;
                }

                _intendedSetup.Position = _intendedSetup.Target + viewDistance;
                _manipulatedByMouseWheel = true;
            }
            else
            {
                var acc = CameraInteractionParameters.CameraAcceleration * UserSettings.Config.CameraSpeed * _deltaTime * 60;
                var sign = delta < 0 ? -1 : 1;
                _moveVelocity += Vector3.Normalize(_viewAxis.ViewDistance) * acc * sign;
                _manipulatedByKeyboard = true;
            }
        }
        else
        {
            if (delta < 0)
            {
                viewDistance *= zoomFactorForCurrentFramerate;
            }

            if (delta > 0)
            {
                viewDistance /= zoomFactorForCurrentFramerate;
            }

            _intendedSetup.Position = _intendedSetup.Target + viewDistance;
            _manipulatedByMouseWheel = true;
        }

        return true;
    }

    private bool LookAround()
    {
        if (!ImGui.IsWindowHovered())
            return false;

        var dragDelta = ImGui.GetIO().MouseDelta;
        if (dragDelta.Length() == 0)
            return false;

        var factorX = -dragDelta.X * CameraInteractionParameters.RotateMouseSensitivity * 1.5f;
        var factorY = dragDelta.Y * CameraInteractionParameters.RotateMouseSensitivity * 1.5f;
        var rotAroundY = Matrix4x4.CreateFromAxisAngle(_viewAxis.Up, factorX);
        var rotAroundX = Matrix4x4.CreateFromAxisAngle(_viewAxis.Left, factorY);
        var rot = Matrix4x4.Multiply(rotAroundX, rotAroundY);
        
        var viewDir2 = new Vector4(_intendedSetup.Target - _intendedSetup.Position, 0);
        var length = viewDir2.Length();
        
        var viewDirRotated = Vector4.Transform(viewDir2, rot);
        viewDirRotated = Vector4.Normalize(viewDirRotated) * length;

        var newTarget = _intendedSetup.Position + new Vector3(viewDirRotated.X, viewDirRotated.Y, viewDirRotated.Z);
        _intendedSetup.Target = newTarget;
        return true;
    }

    private void ApplyOrbitVelocity(Vector2 orbitVelocity)
    {
        if (UserSettings.Config.AdjustCameraSpeedWithMouseWheel)
        {
            var rotAroundX = Matrix4x4.CreateFromAxisAngle(_viewAxis.Left, orbitVelocity.Y);
            var rotAroundY = Matrix4x4.CreateFromAxisAngle(_viewAxis.Up, orbitVelocity.X);
            var rot = Matrix4x4.Multiply(rotAroundX, rotAroundY);

            // The following was an attempt to offset rotation target with cameraSpeed.
            // This approach had too many side effects. I'm leaving it here for reference...
            //
            // var view = _intendedSetup.Target - _intendedSetup.Position;
            // var viewLength = view.Length();
            // var viewDirection = view / viewLength;
            //
            // var rotatedViewDir = Vector3.Transform(viewDirection, rot);
            //
            // var tempTarget = _intendedSetup.Position + viewDirection * UserSettings.Config.CameraSpeed * 3;
            // _intendedSetup.Position = tempTarget - rotatedViewDir  * UserSettings.Config.CameraSpeed * 3;
            // _intendedSetup.Target = _intendedSetup.Position + rotatedViewDir * viewLength;

            var viewDir = _intendedSetup.Target - _intendedSetup.Position;
            var viewDirLength = viewDir.Length();
            viewDir /= viewDirLength;

            var newViewDir = Vector3.Transform(viewDir, rot);
            var newViewVector = newViewDir * viewDirLength;
            _intendedSetup.Position = _intendedSetup.Target - newViewVector;

            _intendedSetup.Target = _intendedSetup.Position + newViewDir * UserSettings.Config.CameraSpeed * GraphicsMath.DefaultCameraDistance;
        }
        else
        {
            var viewDir = new Vector4(_intendedSetup.Target - _intendedSetup.Position, 1);
            var viewDirLength = viewDir.Length();
            viewDir /= viewDirLength;

            var rotAroundX = Matrix4x4.CreateFromAxisAngle(_viewAxis.Left, orbitVelocity.Y);
            var rotAroundY = Matrix4x4.CreateFromAxisAngle(_viewAxis.Up, orbitVelocity.X);
            var rot = Matrix4x4.Multiply(rotAroundX, rotAroundY);

            var newViewDir = Vector4.Transform(viewDir, rot);
            newViewDir = Vector4.Normalize(newViewDir);
            _intendedSetup.Position = _intendedSetup.Target - new Vector3(newViewDir.X, newViewDir.Y, newViewDir.Z) * viewDirLength;
        }
    }

    private bool Pan()
    {
        if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows))
            return false;

        var dragDelta = ImGui.GetIO().MouseDelta;
        if (dragDelta.Length() == 0)
            return false;

        var factorX = dragDelta.X / CameraInteractionParameters.RenderWindowHeight;
        var factorY = dragDelta.Y / CameraInteractionParameters.RenderWindowHeight;

        var length = (_intendedSetup.Target - _intendedSetup.Position).Length() * UserSettings.Config.CameraSpeed;
        var delta = _viewAxis.Left * factorX * length
                    + _viewAxis.Up * factorY * length;

        _intendedSetup.Position += delta;
        _intendedSetup.Target += delta;
        return false;
    }

    private bool ManipulateCameraByKeyboard()
    {
        if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) || ImGui.GetIO().KeyCtrl)
            return false;

        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows))
            return false;
        

        var acc = CameraInteractionParameters.CameraAcceleration * UserSettings.Config.CameraSpeed * _deltaTime * 60;
        var wasModified = false;

        if (UserActions.CameraLeft.Triggered())
        {   
            _moveVelocity += _viewAxis.Left * acc;
            wasModified = true;
        }

        if (UserActions.CameraRight.Triggered())
        {
            _moveVelocity -= _viewAxis.Left * acc;
            wasModified = true;
        }

        if (UserActions.CameraForward.Triggered())
        {
            _moveVelocity += Vector3.Normalize(_viewAxis.ViewDistance) * acc;
            wasModified = true;
        }

        if (UserActions.CameraBackward.Triggered())
        {
            _moveVelocity -= Vector3.Normalize(_viewAxis.ViewDistance) * acc;
            wasModified = true;
        }

        if (UserActions.CameraUp.Triggered())
        {
            _moveVelocity += _viewAxis.Up * acc;
            wasModified = true;
        }

        if (UserActions.CameraDown.Triggered())
        {
            _moveVelocity -= _viewAxis.Up * acc;
            wasModified = true;
        }

        if (UserActions.CameraReset.Triggered())
        {
            _moveVelocity = Vector3.Zero;
            _intendedSetup.Reset();
            wasModified = true;
        }

        if (UserActions.CameraFocusSelection.Triggered())
        {
            _moveVelocity = Vector3.Zero;
            _intendedSetup.Target = TransformGizmoHandling.GetLatestSelectionCenter();
            wasModified = true;
        }

        _manipulatedByKeyboard |= wasModified;
        return wasModified;
    }

    private struct ViewAxis
    {
        public Vector3 Up;
        public Vector3 Left;
        public Vector3 ViewDistance;

        public void ComputeForCamera(ICamera camera)
        {
            ViewDistance = camera.CameraTarget - camera.CameraPosition;

            var worldUp = Vector3.UnitY;
            var rolledUp = Vector3.Normalize(Vector3.Transform(worldUp, Matrix4x4.CreateFromAxisAngle(ViewDistance, camera.CameraRoll)));

            Left = Vector3.Normalize(Vector3.Cross(rolledUp, ViewDistance));
            Up = Vector3.Normalize(Vector3.Cross(ViewDistance, Left));
        }
    }

    public static void ResetCamera(ICamera cam)
    {
        cam.CameraPosition = new Vector3(0, 0, GraphicsMath.DefaultCameraDistance);
        cam.CameraTarget = Vector3.Zero;
        cam.CameraRoll = 0;
    }

    private ViewAxis _viewAxis;
    private readonly CameraSetup _smoothedSetup = new();
    private readonly CameraSetup _intendedSetup = new();

    private static float FrameDurationFactor => ImGui.GetIO().DeltaTime;
    private bool _manipulatedByMouseWheel;
    private bool _manipulatedByKeyboard;

    private Vector3 _moveVelocity;
    private Vector2 _orbitVelocity;
    private ICamera? _lastCameraNode;
    private float _deltaTime;
}