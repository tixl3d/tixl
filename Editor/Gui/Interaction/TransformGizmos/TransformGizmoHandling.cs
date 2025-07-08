#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using Color = T3.Core.DataTypes.Vector.Color;
using GraphicsMath = T3.Core.Utils.Geometry.GraphicsMath;
using Plane = System.Numerics.Plane;
using Quaternion = System.Numerics.Quaternion;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;
using Ray = T3.Core.Utils.Geometry.Ray;

// ReSharper disable RedundantNameQualifier

namespace T3.Editor.Gui.Interaction.TransformGizmos;

/**
 * Handles the interaction with 3d-gizmos for operators selected in the graph.
 */
internal static class TransformGizmoHandling
{
    #region Setup and callback
    
    public static bool IsDragging => _draggedGizmoPart != GizmoParts.None;

    public static void RegisterSelectedTransformable(SymbolUi.Child node, ITransformable transformable)
    {
        if (_selectedTransformables.Contains(transformable))
            return;

        transformable.TransformCallback = TransformCallback;
        _selectedTransformables.Add(transformable);
    }

    public static void ClearDeselectedTransformableNode(ITransformable transformable)
    {
        if (_selectedTransformables.Contains(transformable))
        {
            Log.Warning("trying to deselect an unregistered transformable?");
            return;
        }

        transformable.TransformCallback = null;
        _selectedTransformables.Remove(transformable);
    }

    public static void ClearSelectedTransformables()
    {
        foreach (var selectedTransformable in _selectedTransformables)
        {
            selectedTransformable.TransformCallback = null;
        }

        _selectedTransformables.Clear();
    }

    /// <summary>
    /// We need the foreground draw list at the moment when the output texture is drawn to the to output view...
    /// </summary>
    public static void SetDrawList(ImDrawListPtr drawList)
    {
        _drawList = drawList;
        _isDrawListValid = true;

        if (_dragNotStopped)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                Log.Warning("Gizmo Drag not correctly stopped?");
                _draggedGizmoPart = GizmoParts.None;
            }

            _dragNotStopped = false;
        }
        else if (_draggedGizmoPart != GizmoParts.None && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _dragNotStopped = true;
        }
    }

    // 
    /// <summary>
    /// In certain situations the release is not correctly handled during the update.
    /// In SetDrawList 
    /// </summary>
    private static bool _dragNotStopped;

    public static void RestoreDrawList()
    {
        _isDrawListValid = false;
    }

    private static Vector3 _selectedCenter;

    public static Vector3 GetLatestSelectionCenter()
    {
        if (_selectedTransformables.Count == 0)
            return Vector3.Zero;

        return _selectedCenter;
    }

    /// <summary>
    /// Called from <see cref="ITransformable"/> operators during update call
    /// </summary>
    public static void TransformCallback(Instance instance, EvaluationContext context)
    {
        if (!_isDrawListValid)
        {
            Log.Warning("can't draw gizmo without initialized draw list");
            return;
        }

        if (instance is not ITransformable tmp)
            return;

        _instance = instance;

        _transformable = tmp;

        if (!_selectedTransformables.Contains(_transformable))
        {
            Log.Warning("transform-callback from non-selected node?" + _transformable);
            return;
        }

        if (context.ShowGizmos == GizmoVisibility.Off)
        {
            return;
        }

        _objectToWorld = context.ObjectToWorld;
        _objectToClipSpace = context.ObjectToWorld * context.WorldToCamera * context.CameraToClipSpace;

        PrepareCalculations(context);

        if (_canvas == null)
            return;

        var gizmoScale = CalcGizmoScale(context, _localToObject, _viewport.Width, _viewport.Height, 45f, UserSettings.Config.GizmoSize);
        _centerPadding = 0.2f * gizmoScale / _canvas.Scale.X;
        _gizmoLength = 2f * gizmoScale / _canvas.Scale.Y;
        _planeGizmoSize = 0.5f * gizmoScale / _canvas.Scale.X;
        //var lineThickness = 2;

        // Handle different gizmo modes
        switch (context.TransformGizmoMode)
        {
            case TransformGizmoModes.Move:
                if (_transformable.TranslationInput != null)
                {
                    HandlePositionGizmos();
                }

                break;

            case TransformGizmoModes.Scale:
                if (_transformable.ScaleInput != null)
                {
                    HandleScaleGizmos();
                }

                break;

            case TransformGizmoModes.Rotate:
                if (_transformable.RotationInput != null)
                {
                    HandleRotationGizmos(context);
                }

                break;
        }
    }

    // Extract position gizmo handling into separate method
    private static bool HandlePositionGizmos()
    {
        var isHoveringSomething = HandleDragOnAxis(Vector3.UnitX, Color.Red, GizmoParts.PositionXAxis);
        isHoveringSomething |= HandleDragOnAxis(Vector3.UnitY, Color.Green, GizmoParts.PositionYAxis);
        isHoveringSomething |= HandleDragOnAxis(Vector3.UnitZ, Color.Blue, GizmoParts.PositionZAxis);
        isHoveringSomething |= HandleDragOnPlane(Vector3.UnitX, Vector3.UnitY, Color.Blue, GizmoParts.PositionOnXyPlane);
        isHoveringSomething |= HandleDragOnPlane(Vector3.UnitX, Vector3.UnitZ, Color.Green, GizmoParts.PositionOnXzPlane);
        isHoveringSomething |= HandleDragOnPlane(Vector3.UnitY, Vector3.UnitZ, Color.Red, GizmoParts.PositionOnYzPlane);
        isHoveringSomething |= HandleDragInScreenSpace();

        return isHoveringSomething;
    }
    
    
    private static void PrepareCalculations(EvaluationContext context)
    {
        Debug.Assert(_transformable != null);

        // Terminology of the matrices:
        // objectToClipSpace means in this context the transform without application of the ITransformable values. These are
        // named 'local'. So localToObject is the matrix of applying the ITransformable values and localToClipSpace to transform
        // points from the local system (including trans/rot of ITransformable) to the projected space. Scale is ignored for
        // local here as the local values are only used for drawing, and therefore we don't want to draw anything scaled by this values.
        _mousePosInScreen = ImGui.GetIO().MousePos;
        

        var translation = TryGetVectorFromInput(_transformable.TranslationInput);
        var center = Vector3.TransformNormal(translation, _objectToWorld);
        _selectedCenter = center;

        _localToObject = GraphicsMath.CreateTransformationMatrix(
                                                                 scalingCenter: Vector3.Zero,
                                                                 scalingRotation: Quaternion.Identity,
                                                                 scaling: Vector3.One,
                                                                 rotationCenter: Vector3.Zero,
                                                                 rotation: Quaternion.Identity, // no rotation here
                                                                 translation: translation);

        _localToClipSpace = _localToObject * _objectToClipSpace;
        
        // Camera looks along negative Z in view space
        // Camera looks along negative Z in world space
        var viewDirWorld = -Vector3.UnitZ;
        
        // Bring to object space
        _viewDirLocal = Vector3.TransformNormal(viewDirWorld, _initialObjectToLocal);
        _originInClipSpace = GraphicsMath.TransformCoordinate(translation, _objectToClipSpace);

        // Compute gizmo center in world space
        _initialOriginWorld = Vector3.Transform(_initialOrigin, _objectToWorld);
        
        // Don't draw gizmo behind camera (view plane)
        _renderGizmo = Math.Abs(_originInClipSpace.Z) <= 1 && Math.Abs(_originInClipSpace.X) <= 2 && Math.Abs(_originInClipSpace.Y) <= 2;

        var viewports = ResourceManager.Device.ImmediateContext.Rasterizer.GetViewports<SharpDX.Mathematics.Interop.RawViewportF>();
        _viewport = viewports[0];
        var originInViewport = new Vector2(_viewport.Width * (_originInClipSpace.X * 0.5f + 0.5f),
                                           _viewport.Height * (1.0f - (_originInClipSpace.Y * 0.5f + 0.5f)));

        _canvas = ImageOutputCanvas.Current;
        if (_canvas == null)
            return;

        var originInCanvas = _canvas.TransformDirection(originInViewport);
        _topLeftOnScreen = _canvas.TransformPosition(System.Numerics.Vector2.Zero);
        _originInScreen = _topLeftOnScreen + originInCanvas;

        Matrix4x4.Invert(_localToObject, out _initialObjectToLocal);
    }
    #endregion

    
    #region scale ----------------------------------------------------------------
    // New method to handle scale gizmos
    private static bool HandleScaleGizmos()
    {
        var isHoveringSomething = HandleAndDrawScaleDragOnAxis(Vector3.UnitX, Color.Red, GizmoParts.ScaleXAxis);
        isHoveringSomething |= HandleAndDrawScaleDragOnAxis(Vector3.UnitY, Color.Green, GizmoParts.ScaleYAxis);
        isHoveringSomething |= HandleAndDrawScaleDragOnAxis(Vector3.UnitZ, Color.Blue, GizmoParts.ScaleZAxis);
        isHoveringSomething |= HandleUniformScale();

        return isHoveringSomething;
    }

    private static bool HandleAndDrawScaleDragOnAxis(Vector3 gizmoAxis, Color color, GizmoParts mode)
    {
        var axisStartInScreen = LocalPosToScreenPos(gizmoAxis * _centerPadding);
        var deltaScaleForAxis = (_draggedGizmoPart == GizmoParts.ScaleUniform || mode == _draggedGizmoPart)
                                    ? _deltaScaleFactor
                                    : 1;
        var axisEndInScreen = LocalPosToScreenPos(gizmoAxis * _gizmoLength * deltaScaleForAxis);

        // Draw scale handle as a cube at the end of the axis
        var handleSize = 12f;
        var handleRect = new Vector2(handleSize, handleSize);
        var handleCenter = axisEndInScreen;
        var handleMin = handleCenter - handleRect * 0.5f;
        var handleMax = handleCenter + handleRect * 0.5f;

        var isHovering = false;
        if (!IsDragging)
        {
            // Check if mouse is over the handle
            isHovering = (_mousePosInScreen.X >= handleMin.X && _mousePosInScreen.X <= handleMax.X &&
                          _mousePosInScreen.Y >= handleMin.Y && _mousePosInScreen.Y <= handleMax.Y);

            if (isHovering && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                StartScaleDragging(mode);
            }
        }
        else if (_draggedGizmoPart == mode
                 && _draggedTransformable == _transformable
                 && _dragInteractionWindowId == ImGui.GetID(""))
        {
            isHovering = true;
            UpdateScaleDragging(gizmoAxis);

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                CompleteScaleDragging();
            }
        }

        if (_renderGizmo)
        {
            // Draw axis line
            _drawList.AddLine(axisStartInScreen, axisEndInScreen, color, 2 * (isHovering ? 3 : 1));

            // Draw scale handle (cube)
            var handleColor = color;
            handleColor.Rgba.W = isHovering ? 1.0f : 0.8f;
            _drawList.AddRectFilled(handleMin, handleMax, handleColor);
            _drawList.AddRect(handleMin, handleMax, Color.White, 0, ImDrawFlags.None, 1);
        }

        return isHovering;
    }

    // New method to handle uniform scaling
    private static bool HandleUniformScale()
    {
        const float handleSize = 12f;
        var handleRect = new Vector2(handleSize, handleSize);
        var handleCenter = _originInScreen;
        var handleMin = handleCenter - handleRect * 0.5f;
        var handleMax = handleCenter + handleRect * 0.5f;

        var isHovering = false;

        if (!IsDragging)
        {
            isHovering = (_mousePosInScreen.X >= handleMin.X && _mousePosInScreen.X <= handleMax.X &&
                          _mousePosInScreen.Y >= handleMin.Y && _mousePosInScreen.Y <= handleMax.Y);

            if (isHovering && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                StartScaleDragging(GizmoParts.ScaleUniform);
            }
        }
        else if (_draggedGizmoPart == GizmoParts.ScaleUniform
                 && _draggedTransformable == _transformable
                 && _dragInteractionWindowId == ImGui.GetID(""))
        {
            isHovering = true;
            UpdateUniformScaleDragging();

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                CompleteScaleDragging();
            }
        }

        if (_renderGizmo)
        {
            var color = UiColors.StatusAnimated;
            color.Rgba.W = isHovering ? 1.0f : 0.6f;
            _drawList.AddRectFilled(handleMin, handleMax, color);
            _drawList.AddRect(handleMin, handleMax, Color.White, 0, ImDrawFlags.None, 2);
        }

        return isHovering;
    }

    // New method to start scale dragging
    private static void StartScaleDragging(GizmoParts mode)
    {
        Debug.Assert(_instance?.Parent != null);
        Debug.Assert(_transformable != null);

        _draggedGizmoPart = mode;
        _inputCommandInFlight = new ChangeInputValueCommand(_instance.Parent.Symbol,
                                                            _instance.SymbolChildId,
                                                            _transformable.ScaleInput.Input,
                                                            _transformable.ScaleInput.Input.Value);

        _draggedTransformable = _transformable;
        _dragInteractionWindowId = ImGui.GetID("");
        _initialOffsetToOrigin = _mousePosInScreen - _originInScreen;
        _initialScale = TryGetVectorFromInput(_transformable.ScaleInput, 1f);

        var rayInObject = GetPickRayInObject(_mousePosInScreen);
        var rayInLocal = rayInObject;
        rayInLocal.Direction = Vector3.TransformNormal(rayInObject.Direction, _initialObjectToLocal);
        rayInLocal.Origin = Vector3.Transform(rayInObject.Origin, _initialObjectToLocal);

        _plane = GetPlaneForDragMode(mode, rayInObject.Direction, _initialOrigin);
        if (!_plane.Intersects(rayInLocal, out _initialIntersectionPoint))
        {
            _plane.D = -_plane.D;
            if (!_plane.Intersects(rayInLocal, out _initialIntersectionPoint))
            {
                _plane.Normal = Vector3.Zero;
                Log.Debug($"Couldn't intersect pick ray with gizmo axis plane.");
            }
        }
    }

    // New method to update scale dragging
    private static void UpdateScaleDragging(Vector3 axis)
    {
        Debug.Assert(_inputCommandInFlight != null);
        Debug.Assert(_transformable != null);

        var rayInObject = GetPickRayInObject(_mousePosInScreen);
        var rayInLocal = rayInObject;
        rayInLocal.Direction = Vector3.TransformNormal(rayInObject.Direction, _initialObjectToLocal);
        rayInLocal.Origin = Vector3.Transform(rayInObject.Origin, _initialObjectToLocal);

        if (_plane.Normal != Vector3.Zero && _plane.Intersects(rayInLocal, out Vector3 intersectionPoint))
        {
            var deltaInLocal = intersectionPoint - _initialIntersectionPoint;
            var scaleDelta = Vector3.Dot(deltaInLocal, axis);

            var newScale = _initialScale + axis * scaleDelta;
            _deltaScaleFactor = 1 + scaleDelta;

            // Prevent negative or zero scaling
            //newScale.X = Math.Max(0.001f, newScale.X);
            //newScale.Y = Math.Max(0.001f, newScale.Y);
            //newScale.Z = Math.Max(0.001f, newScale.Z);

            TrySetVector3ToInput(_transformable.ScaleInput, newScale);
            _inputCommandInFlight.AssignNewValue(_transformable.ScaleInput.Input.Value);
        }
    }

    /** Uniform */
    private static float _deltaScaleFactor = 1;

    // New method to update uniform scale dragging
    private static void UpdateUniformScaleDragging()
    {
        Debug.Assert(_inputCommandInFlight != null);
        Debug.Assert(_transformable != null);

        var mouseDelta = _mousePosInScreen - (_originInScreen + _initialOffsetToOrigin);
        var scaleDelta = 1 + mouseDelta.X * 0.01f; // Use X movement for uniform scaling
        _deltaScaleFactor = scaleDelta;

        //var newScale = _initialScale + Vector3.One * scaleDelta;

        var newScale = _initialScale * scaleDelta;

        TrySetVector3ToInput(_transformable.ScaleInput, newScale);
        _inputCommandInFlight.AssignNewValue(_transformable.ScaleInput.Input.Value);
    }

    // New method to complete scale dragging
    private static void CompleteScaleDragging()
    {
        Debug.Assert(_inputCommandInFlight != null);
        UndoRedoStack.Add(_inputCommandInFlight);
        _inputCommandInFlight = null;

        _draggedGizmoPart = GizmoParts.None;
        _draggedTransformable = null;
        _dragInteractionWindowId = 0;
        _deltaScaleFactor = 1;
        _dragNotStopped = false;
    }
    #endregion


    #region translate ----------------------------------------------------
    
    // Returns true if hovered or active
    private static bool HandleDragOnAxis(Vector3 gizmoAxis, Color color, GizmoParts mode)
    {
        var axisStartInScreen = LocalPosToScreenPos(gizmoAxis * _centerPadding);
        var axisEndInScreen = LocalPosToScreenPos(gizmoAxis * _gizmoLength);

        var isHovering = false;
        if (!IsDragging)
        {
            isHovering = IsPointOnLine(_mousePosInScreen, axisStartInScreen, axisEndInScreen);

            if (isHovering && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                StartPositionDragging(mode, true);
            }
        }
        else if (_draggedGizmoPart == mode
                 && _draggedTransformable == _transformable
                 && _dragInteractionWindowId == ImGui.GetID(""))
        {
            isHovering = true;

            var rayInObject = GetPickRayInObject(_mousePosInScreen);
            var rayInLocal = rayInObject;
            rayInLocal.Direction = Vector3.TransformNormal((rayInObject.Direction), _initialObjectToLocal);
            rayInLocal.Origin = Vector3.Transform((rayInObject.Origin), _initialObjectToLocal);

            Vector3 newOrigin = _initialOrigin; // (GetNewOffsetInObject() - _initialOrigin) * (gizmoAxis1 + gizmoAxis2) + _initialOrigin;
            if (_plane.Normal != Vector3.Zero
                && _plane.Intersects(rayInLocal, out Vector3 intersectionPoint))
            {
                Vector3 offsetInLocal = (intersectionPoint - _initialIntersectionPoint) * gizmoAxis;
                var offsetInObject = Vector4.Transform(new Vector4(offsetInLocal, 1), _localToObject);
                newOrigin = new Vector3(offsetInObject.X, offsetInObject.Y, offsetInObject.Z) / offsetInObject.W;
            }

            UpdatePositionDragging(newOrigin);

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                CompletePositionDragging();
            }
        }

        if (_renderGizmo)
            _drawList.AddLine(axisStartInScreen, axisEndInScreen, color, 2 * (isHovering ? 3 : 1));

        return isHovering;
    }

    // Returns true if hovered or active
    private static bool HandleDragOnPlane(Vector3 gizmoAxis1, Vector3 gizmoAxis2, Color color, GizmoParts mode)
    {
        var origin = (gizmoAxis1 + gizmoAxis2) * _centerPadding;
        Vector2[] pointsOnScreen =
            {
                LocalPosToScreenPos(origin),
                LocalPosToScreenPos(origin + gizmoAxis1 * _planeGizmoSize),
                LocalPosToScreenPos(origin + (gizmoAxis1 + gizmoAxis2) * _planeGizmoSize),
                LocalPosToScreenPos(origin + gizmoAxis2 * _planeGizmoSize),
            };
        var isHovering = false;

        if (!IsDragging)
        {
            isHovering = IsPointInQuad(_mousePosInScreen, pointsOnScreen);

            if (isHovering && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                StartPositionDragging(mode, true);
            }
        }
        else if (_draggedGizmoPart == mode
                 && _draggedTransformable == _transformable
                 && _dragInteractionWindowId == ImGui.GetID(""))
        {
            isHovering = true;

            var rayInObject = GetPickRayInObject(_mousePosInScreen);
            var rayInLocal = rayInObject;
            rayInLocal.Direction = (Vector3.TransformNormal((rayInObject.Direction), _initialObjectToLocal));
            rayInLocal.Origin = (Vector3.Transform((rayInObject.Origin), _initialObjectToLocal));

            Vector3 newOrigin = _initialOrigin; // (GetNewOffsetInObject() - _initialOrigin) * (gizmoAxis1 + gizmoAxis2) + _initialOrigin;
            if (_plane.Normal != Vector3.Zero
                && _plane.Intersects(rayInLocal, out Vector3 intersectionPoint))
            {
                Vector3 offsetInLocal = (intersectionPoint - _initialIntersectionPoint) * (gizmoAxis1 + gizmoAxis2);
                var offsetInObject = GraphicsMath.TransformCoordinate(offsetInLocal, _localToObject);
                newOrigin = offsetInObject;
            }

            UpdatePositionDragging(newOrigin);

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                CompletePositionDragging();
            }
        }

        if (_renderGizmo)
        {
            var color2 = color;
            color2.Rgba.W = isHovering ? 0.4f : 0.2f;
            _drawList.AddConvexPolyFilled(ref pointsOnScreen[0], 4, color2);
        }

        return isHovering;
    }

    private static bool HandleDragInScreenSpace()
    {
        const float gizmoSize = 4;
        var screenSquaredMin = _originInScreen - new Vector2(gizmoSize, gizmoSize);
        var screenSquaredMax = _originInScreen + new Vector2(gizmoSize, gizmoSize);

        var isHovering = false;

        if (_draggedGizmoPart == GizmoParts.None)
        {
            isHovering = (_mousePosInScreen.X > screenSquaredMin.X && _mousePosInScreen.X < screenSquaredMax.X &&
                          _mousePosInScreen.Y > screenSquaredMin.Y && _mousePosInScreen.Y < screenSquaredMax.Y);
            if (isHovering && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                StartPositionDragging(GizmoParts.PositionInScreenPlane, true);
            }
        }
        else if (_draggedGizmoPart == GizmoParts.PositionInScreenPlane
                 && _draggedTransformable == _transformable
                 && _dragInteractionWindowId == ImGui.GetID(""))
        {
            isHovering = true;
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                CompletePositionDragging();
            }
            else
            {
                UpdatePositionDragging(GetNewOffsetInObject());
                // not internal plane udpate needed here since it's not used at all
            }
        }

        if (_renderGizmo)
        {
            var color2 = UiColors.StatusAnimated;
            color2.Rgba.W = isHovering ? 0.8f : 0.3f;
            _drawList.AddRectFilled(screenSquaredMin, screenSquaredMax, color2);
        }

        return isHovering;
    }

    private static void StartPositionDragging(GizmoParts mode, bool createUndo)
    {
        Debug.Assert(_instance?.Parent != null);
        Debug.Assert(_transformable != null);

        _draggedGizmoPart = mode;
        if (createUndo)
        {
            _inputCommandInFlight = new ChangeInputValueCommand(_instance.Parent.Symbol,
                                                                     _instance.SymbolChildId,
                                                                     _transformable.TranslationInput.Input,
                                                                     _transformable.TranslationInput.Input.Value);
        }

        _draggedTransformable = _transformable;
        _dragInteractionWindowId = ImGui.GetID("");
        _initialOffsetToOrigin = _mousePosInScreen - _originInScreen;
        _initialOrigin = _localToObject.Translation;

        var rayInObject = GetPickRayInObject(_mousePosInScreen);
        var rayInLocal = rayInObject;
        rayInLocal.Direction = Vector3.TransformNormal((rayInObject.Direction), _initialObjectToLocal);
        rayInLocal.Origin = Vector3.Transform((rayInObject.Origin), _initialObjectToLocal);

        _plane = GetPlaneForDragMode(mode, (rayInObject.Direction), _initialOrigin);
        if (mode == GizmoParts.PositionInScreenPlane)
        {
            _plane.Normal = Vector3.Zero;
        }
        else if (!_plane.Intersects(rayInLocal, out _initialIntersectionPoint))
        {
            _plane.D = -_plane.D;
            if (!_plane.Intersects(rayInLocal, out _initialIntersectionPoint))
            {
                _plane.Normal = Vector3.Zero;
                Log.Debug($"Couldn't intersect pick ray with gizmo axis plane.");
            }
        }
    }

    private static void UpdatePositionDragging(in Vector3 newOrigin)
    {
        Debug.Assert(_inputCommandInFlight != null);
        Debug.Assert(_transformable != null);

        TrySetVector3ToInput(_transformable.TranslationInput, newOrigin);
        InputValue value = _transformable.TranslationInput.Input.Value;

        _inputCommandInFlight.AssignNewValue(value);
    }

    private static void CompletePositionDragging()
    {
        Debug.Assert(_inputCommandInFlight != null);
        Debug.Assert(_transformable != null);

        UndoRedoStack.Add(_inputCommandInFlight);
        _inputCommandInFlight = null;

        _draggedGizmoPart = GizmoParts.None;
        _draggedTransformable = null;
        _dragInteractionWindowId = 0;
        _dragNotStopped = false;
    }

    private static Vector3 GetNewOffsetInObject()
    {
        Debug.Assert(_canvas != null);

        Vector2 newOriginInScreen = _mousePosInScreen - _initialOffsetToOrigin;
        // transform back to object space
        Matrix4x4.Invert(_objectToClipSpace, out var clipSpaceToObject);
        var newOriginInCanvas = newOriginInScreen - _topLeftOnScreen;
        var newOriginInViewport = _canvas.InverseTransformDirection(newOriginInCanvas);
        var newOriginInClipSpace = new Vector4(2.0f * newOriginInViewport.X / _viewport.Width - 1.0f,
                                               -(2.0f * newOriginInViewport.Y / _viewport.Height - 1.0f),
                                               _originInClipSpace.Z, 1);
        var newOriginInObject = Vector4.Transform(newOriginInClipSpace, clipSpaceToObject);
        Vector3 newTranslation = new Vector3(newOriginInObject.X, newOriginInObject.Y, newOriginInObject.Z) / newOriginInObject.W;
        return new Vector3(newTranslation.X, newTranslation.Y, newTranslation.Z);
    }
    #endregion



    #region rotate ----------------------------------------------------------
    private static bool HandleRotationGizmos(EvaluationContext context)
    {
        if (_transformable?.RotationInput == null)
            return false;

        var rotation = TryGetVectorFromInput(_transformable.RotationInput);
        var yaw = rotation.Y.ToRadians();
        var pitch = rotation.X.ToRadians();

        // Create individual quaternions
        var yawQ = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        var pitchQ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch);

        // Gimbal axes
        var axisYaw = Vector3.UnitY;
        var axisPitch = Vector3.Transform(Vector3.UnitX, yawQ);
        var yawPitchQ = Quaternion.Concatenate(pitchQ, yawQ);
        var axisRoll = Vector3.Transform(Vector3.UnitZ, yawPitchQ);

        var isHovering = HandleRotationOnAxis(context, axisYaw, Color.Green, GizmoParts.RotationYAxis);
        isHovering |= HandleRotationOnAxis(context,axisPitch, Color.Red, GizmoParts.RotationXAxis);
        isHovering |= HandleRotationOnAxis(context,axisRoll, Color.Blue, GizmoParts.RotationZAxis);
        isHovering |= HandleScreenRotation();

        return isHovering;
    }

    private static bool HandleRotationOnAxis(EvaluationContext context, Vector3 axis, Color color, GizmoParts mode)
    {
        const int circleSegments = 32;
        var gizmoRadius = _gizmoLength * 0.6f;

        var circlePoints = new Vector2[circleSegments];

        // Create two perpendicular vectors to form the circle plane
        Vector3 tangent1, tangent2;
        if (MathF.Abs(Vector3.Dot(axis, Vector3.UnitZ)) > 0.999f)
        {
            tangent1 = Vector3.UnitX;
            tangent2 = Vector3.UnitY;
        }
        else
        {
            tangent1 = Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitZ));
            tangent2 = Vector3.Normalize(Vector3.Cross(axis, tangent1));
        }

        // Calculate circle points in 3D space then project to screen
        for (int i = 0; i < circleSegments; i++)
        {
            float angle = (float)i / circleSegments * MathF.PI * 2;
            Vector3 point3D = tangent1 * MathF.Cos(angle) * gizmoRadius +
                              tangent2 * MathF.Sin(angle) * gizmoRadius;

            circlePoints[i] = LocalPosToScreenPos(point3D);
        }

        var isHovering = false;

        if (!IsDragging)
        {
            isHovering = IsPointNearPolygon(_mousePosInScreen, circlePoints);

            if (isHovering && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                StartRotationDragging(mode, axis);
            }
        }
        else if (_draggedGizmoPart == mode
                 && _draggedTransformable == _transformable
                 && _dragInteractionWindowId == ImGui.GetID(""))
        {
            isHovering = true;
            UpdateRotationDragging(_draggedRotationAxis);

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                CompleteRotationDragging();
            }
            
            
        }

        if (_renderGizmo)
        {
            Vector3[] points3D = new Vector3[circleSegments];
            Vector2[] points2D = new Vector2[circleSegments];
            // Recompute view vector every frame
            
            Matrix4x4.Invert(context.WorldToCamera, out var cameraToWorld);            
            Vector3 cameraWorldPos = Vector3.Transform(Vector3.Zero, cameraToWorld); 
            Vector3 dirWorld = Vector3.Normalize(_initialOriginWorld - cameraWorldPos);
            _viewDirLocal = Vector3.TransformNormal(dirWorld, _initialObjectToLocal);
            
            for (int i = 0; i < circleSegments; i++)
            {
                float angle = i / (float)circleSegments * MathF.PI * 2;
                float cos = MathF.Cos(angle);
                float sin = MathF.Sin(angle);

                Vector3 p3D = tangent1 * cos * gizmoRadius + tangent2 * sin * gizmoRadius;
                points3D[i] = p3D;
                points2D[i] = LocalPosToScreenPos(p3D);
            }

            for (int i = 0; i < circleSegments; i++)
            {
                int next = (i + 1) % circleSegments;

                Vector3 midPoint3D = (points3D[i] + points3D[next]) * 0.5f;
                float facing = Vector3.Dot(midPoint3D, _viewDirLocal);

                float alpha = facing >= 0 ? 0.3f : 1.0f;

                var segColor = color;
                segColor.Rgba.W = alpha;

                _drawList.AddLine(points2D[i], points2D[next], segColor, isHovering ? 3 : 2);
            }            
            
            if (_draggedGizmoPart == mode &&
                _draggedTransformable == _transformable &&
                _dragInteractionWindowId == ImGui.GetID(""))
            {
                Vector3 initialWorldPos =  _initialRotationVector * gizmoRadius * 1.05f;
                Vector3 currentWorldPos =  _currentRotationVector * gizmoRadius * 1.05f;
                
                Vector2 initialScreenPos = LocalPosToScreenPos(initialWorldPos);
                Vector2 currentScreenPos = LocalPosToScreenPos(currentWorldPos);

                float markerRadius = 5.0f;


                _drawList.AddCircleFilled(initialScreenPos, markerRadius*0.6f, color);
                _drawList.AddCircleFilled(currentScreenPos, markerRadius, color);
            }  
        }

        return isHovering;
    }

    private static Vector3 _initialOriginWorld;
    
    // Add this method to handle screen space rotation
    private static bool HandleScreenRotation()
    {
        const float outerRadius = 2.0f;
        const float innerRadius = 1.8f;

        var outerCirclePoints = new Vector2[32];
        var innerCirclePoints = new Vector2[32];

        // Calculate circle points in screen space
        for (int i = 0; i < 32; i++)
        {
            float angle = (float)i / 32 * MathF.PI * 2;
            outerCirclePoints[i] = _originInScreen + new Vector2(
                                                                 MathF.Cos(angle) * outerRadius,
                                                                 MathF.Sin(angle) * outerRadius);

            innerCirclePoints[i] = _originInScreen + new Vector2(
                                                                 MathF.Cos(angle) * innerRadius,
                                                                 MathF.Sin(angle) * innerRadius);
        }

        var isHovering = false;

        if (!IsDragging)
        {
            // Check if mouse is in the ring between inner and outer circles
            isHovering = IsPointInRing(_mousePosInScreen, _originInScreen, innerRadius, outerRadius);

            if (isHovering && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                StartRotationDragging(GizmoParts.RotationScreen, Vector3.Zero);
            }
        }
        else if (_draggedGizmoPart == GizmoParts.RotationScreen
                 && _draggedTransformable == _transformable
                 && _dragInteractionWindowId == ImGui.GetID(""))
        {
            isHovering = true;
            UpdateScreenRotationDragging();

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                CompleteRotationDragging();
            }
        }

        if (_renderGizmo)
        {
            // Draw screen rotation ring
            var color = UiColors.StatusAnimated;
            color.Rgba.W = isHovering ? 0.8f : 0.6f;

            for (int i = 0; i < 32; i++)
            {
                int next = (i + 1) % 32;
                _drawList.AddQuad(
                                  outerCirclePoints[i],
                                  outerCirclePoints[next],
                                  innerCirclePoints[next],
                                  innerCirclePoints[i],
                                  color);
            }
        }

        return isHovering;
    }

    private static void StartRotationDragging(GizmoParts mode, Vector3 axis)
    {
        Debug.Assert(_instance?.Parent != null);
        Debug.Assert(_transformable != null);

        _draggedGizmoPart = mode;
        _inputCommandInFlight = new ChangeInputValueCommand(
                                                               _instance.Parent.Symbol,
                                                               _instance.SymbolChildId,
                                                               _transformable.RotationInput.Input,
                                                               _transformable.RotationInput.Input.Value);

        _draggedTransformable = _transformable;
        _dragInteractionWindowId = ImGui.GetID("");
        _initialOrigin = _localToObject.Translation;
        _initialRotation = TryGetVectorFromInput(_transformable.RotationInput);
        _initialMousePos = _mousePosInScreen;
        _draggedRotationAxis = axis;

        // Get initial mouse pick ray in object space
        var ray = GetPickRayInObject(_mousePosInScreen);

        // Intersect with rotation plane
        var plane = new Plane(axis, -Vector3.Dot(axis, _initialOrigin));
        if (!PlaneIntersectRay(plane, ray, out var intersection))
        {
            Log.Warning("Couldn't intersect pick ray with rotation plane.");
            _initialRotationVector = Vector3.UnitX; // fallback
            return;
        }

        // Vector from gizmo origin to intersection, normalized
        _initialRotationVector = Vector3.Normalize(intersection - _initialOrigin);
    }

    private static void UpdateRotationDragging(Vector3 axis)
    {
        Debug.Assert(_inputCommandInFlight != null);
        Debug.Assert(_transformable != null);

        var ray = GetPickRayInObject(_mousePosInScreen);
        var plane = new Plane(axis, -Vector3.Dot(axis, _initialOrigin));

        if (!PlaneIntersectRay(plane, ray, out var intersection))
            return;

        var currentVector = Vector3.Normalize(intersection - _initialOrigin);
        _currentRotationVector = currentVector;
        
        // Compute signed angle between initial and current vectors
        float angleRad = MathF.Atan2(
                                     Vector3.Dot(axis, Vector3.Cross(_initialRotationVector, currentVector)),
                                     Vector3.Dot(_initialRotationVector, currentVector)
                                    );
        float angleDeg = angleRad * (180f / MathF.PI);
        Vector3 newRotation = _initialRotation;

        var snap = ImGui.GetIO().KeyCtrl;

        switch (_draggedGizmoPart)
        {
            case GizmoParts.RotationXAxis:
                newRotation.X += angleDeg;
                if (snap)
                    newRotation.X = MathF.Round(newRotation.X/15)*15;
                break;
            case GizmoParts.RotationYAxis:
                newRotation.Y += angleDeg;
                if(snap)
                    newRotation.Y = MathF.Round(newRotation.Y/15)*15;
                break;
            case GizmoParts.RotationZAxis:
                newRotation.Z += angleDeg;
                if(snap)
                    newRotation.Z = MathF.Round(newRotation.Z/15)*15;
                break;
            default:
                Log.Debug("No valid gizmo part for rotation.");
                break;
        }

        TrySetVector3ToInput(_transformable.RotationInput, newRotation);
        _inputCommandInFlight.AssignNewValue(_transformable.RotationInput.Input.Value);
    }

    private static bool PlaneIntersectRay(Plane plane, Ray ray, out Vector3 intersection)
    {
        float denom = Vector3.Dot(plane.Normal, ray.Direction);
        if (MathF.Abs(denom) < 1e-6f)
        {
            intersection = Vector3.Zero;
            return false;
        }

        float t = -(Vector3.Dot(plane.Normal, ray.Origin) + plane.D) / denom;
        intersection = ray.Origin + ray.Direction * t;
        return t >= 0;
    }

    


    private static void UpdateScreenRotationDragging()
    {
        Debug.Assert(_inputCommandInFlight != null);
        Debug.Assert(_transformable != null);

        // For screen rotation, use circular motion around the gizmo center
        Vector2 initialDir = Vector2.Normalize(_initialMousePos - _originInScreen);
        Vector2 currentDir = Vector2.Normalize(_mousePosInScreen - _originInScreen);

        // Calculate the angle between the two directions
        float dot = Vector2.Dot(initialDir, currentDir);
        float det = initialDir.X * currentDir.Y - initialDir.Y * currentDir.X;
        float angleDelta = MathF.Atan2(det, dot) * (180f / MathF.PI);

        // Apply to all axes equally for screen rotation, or choose one axis
        Vector3 newRotation = _initialRotation;
        newRotation.Z += angleDelta; // Apply to Z-axis for screen rotation

        TrySetVector3ToInput(_transformable.RotationInput, newRotation);
        _inputCommandInFlight.AssignNewValue(_transformable.RotationInput.Input.Value);
    }

    private static void CompleteRotationDragging()
    {
        Debug.Assert(_inputCommandInFlight != null);
        UndoRedoStack.Add(_inputCommandInFlight);
        _inputCommandInFlight = null;

        _draggedGizmoPart = GizmoParts.None;
        _draggedTransformable = null;
        _dragInteractionWindowId = 0;
        _dragNotStopped = false;
    }
    
    // Add this helper method to check if point is near a polygon
    private static bool IsPointNearPolygon(Vector2 point, Vector2[] polygon, float threshold = 5f)
    {
        for (int i = 0; i < polygon.Length; i++)
        {
            int next = (i + 1) % polygon.Length;
            if (IsPointOnLine(point, polygon[i], polygon[next], threshold))
            {
                return true;
            }
        }

        return false;
    }

    // Add this helper method to check if point is in a ring
    private static bool IsPointInRing(Vector2 point, Vector2 center, float innerRadius, float outerRadius)
    {
        float distanceSquared = Vector2.DistanceSquared(point, center);
        return distanceSquared >= innerRadius * innerRadius &&
               distanceSquared <= outerRadius * outerRadius;
    }
    #endregion
    
    
    #region setting helpers -----------------------------------------
    
    private static Vector3 TryGetVectorFromInput(IInputSlot input, float defaultValue = 0f)
    {
        return input switch
                   {
                       InputSlot<System.Numerics.Vector3> vec3Input => vec3Input.Value,
                       InputSlot<System.Numerics.Vector2> vec2Input => new Vector3(vec2Input.Value.X, vec2Input.Value.Y, defaultValue),
                       _                                            => new Vector3(defaultValue, defaultValue, defaultValue)
                   };
    }

    private static void TrySetVector3ToInput(IInputSlot input, Vector3 vector3)
    {
        switch (input)
        {
            case InputSlot<System.Numerics.Vector3> vec3Input:
                vec3Input.SetTypedInputValue(vector3);
                break;
            case InputSlot<System.Numerics.Vector2> vec2Input:
                vec2Input.SetTypedInputValue(new Vector2(vector3.X, vector3.Y));
                break;
        }
    }    
    #endregion
    
    #region math
    private static Ray GetPickRayInObject(Vector2 posInScreen)
    {
        Debug.Assert(_canvas != null);

        Matrix4x4.Invert(_objectToClipSpace, out var clipSpaceToObject);
        var newOriginInCanvas = posInScreen - _topLeftOnScreen;
        var newOriginInViewport = _canvas.InverseTransformDirection(newOriginInCanvas);

        float xInClipSpace = 2.0f * newOriginInViewport.X / _viewport.Width - 1.0f;
        float yInClipSpace = -(2.0f * newOriginInViewport.Y / _viewport.Height - 1.0f);

        var rayStartInClipSpace = new Vector3(xInClipSpace, yInClipSpace, 0);
        var rayStartInObject = GraphicsMath.TransformCoordinate(rayStartInClipSpace, clipSpaceToObject);

        var rayEndInClipSpace = new Vector3(xInClipSpace, yInClipSpace, 1);
        var rayEndInObject = GraphicsMath.TransformCoordinate(rayEndInClipSpace, clipSpaceToObject);

        var rayDir = (rayEndInObject - rayStartInObject);
        //rayDir = Vector3.Normalize(rayDir);

        return new Ray(rayStartInObject, rayDir);
    }

    // Calculates the scale for a gizmo based on the distance to the cam
    private static float CalcGizmoScale(EvaluationContext context, in Matrix4x4 localToObject, float width, float height, float fovInDegree,
                                        float gizmoSize)
    {
        var localToCamera = localToObject * context.ObjectToWorld * context.WorldToCamera;
        var distance = localToCamera.Translation.Length(); // distance of local origin to cam
        var denom = Math.Sqrt(width * width + height * height) * Math.Tan(fovInDegree.ToRadians());
        return (float)Math.Max(0.0001, (distance / denom) * gizmoSize);
    }

    private static Vector2 LocalPosToScreenPos(Vector3 posInLocal)
    {
        Debug.Assert(_canvas != null);

        var homogenousPosInLocal = new Vector4(posInLocal.X, posInLocal.Y, posInLocal.Z, 1);
        Vector4 originInClipSpace = Vector4.Transform(homogenousPosInLocal, _localToClipSpace);
        Vector3 posInNdc = new Vector3(originInClipSpace.X, originInClipSpace.Y, originInClipSpace.Z) / originInClipSpace.W;
        var viewports = ResourceManager.Device.ImmediateContext.Rasterizer.GetViewports<SharpDX.Mathematics.Interop.RawViewportF>();
        var viewport = viewports[0];
        var originInViewport = new Vector2(viewport.Width * (posInNdc.X * 0.5f + 0.5f),
                                           viewport.Height * (1.0f - (posInNdc.Y * 0.5f + 0.5f)));

        var posInCanvas = _canvas.TransformDirection(originInViewport);
        return _topLeftOnScreen + posInCanvas;
    }

    // Update the GetPlaneForDragMode method to handle scale modes
    private static Plane GetPlaneForDragMode(GizmoParts mode, Vector3 normDir, Vector3 origin)
    {
        Vector3 firstAxis, secondAxis;

        switch (mode)
        {
            case GizmoParts.PositionXAxis:
            case GizmoParts.ScaleXAxis:
            {
                firstAxis = Vector3.UnitX;
                secondAxis = Math.Abs(Vector3.Dot(normDir, Vector3.UnitY)) <
                             Math.Abs(Vector3.Dot(normDir, Vector3.UnitZ))
                                 ? Vector3.UnitY
                                 : Vector3.UnitZ;
                break;
            }
            case GizmoParts.PositionYAxis:
            case GizmoParts.ScaleYAxis:
            {
                firstAxis = Vector3.UnitY;
                secondAxis = Math.Abs(Vector3.Dot(normDir, Vector3.UnitX)) <
                             Math.Abs(Vector3.Dot(normDir, Vector3.UnitZ))
                                 ? Vector3.UnitX
                                 : Vector3.UnitZ;
                break;
            }
            case GizmoParts.PositionZAxis:
            case GizmoParts.ScaleZAxis:
            {
                firstAxis = Vector3.UnitZ;
                secondAxis = Math.Abs(Vector3.Dot(normDir, Vector3.UnitX)) <
                             Math.Abs(Vector3.Dot(normDir, Vector3.UnitY))
                                 ? Vector3.UnitX
                                 : Vector3.UnitY;
                break;
            }
            case GizmoParts.PositionOnXyPlane:
            case GizmoParts.ScaleOnXyPlane:
                firstAxis = Vector3.UnitX;
                secondAxis = Vector3.UnitY;
                break;
            case GizmoParts.PositionOnXzPlane:
            case GizmoParts.ScaleOnXzPlane:
                firstAxis = Vector3.UnitX;
                secondAxis = Vector3.UnitZ;
                break;
            case GizmoParts.PositionOnYzPlane:
            case GizmoParts.ScaleOnYzPlane:
                firstAxis = Vector3.UnitY;
                secondAxis = Vector3.UnitZ;
                break;
            case GizmoParts.ScaleUniform:
                // For uniform scaling, use a plane perpendicular to the view direction
                firstAxis = Vector3.UnitX;
                secondAxis = Vector3.UnitY;
                break;
            default:
                Log.Error($"{nameof(GetPlaneForDragMode)}(...) called with wrong GizmoDraggingMode.");
                firstAxis = Vector3.UnitX;
                secondAxis = Vector3.UnitY;
                break;
        }

        var point2 = origin + firstAxis;
        var point3 = origin + secondAxis;
        return Plane.CreateFromVertices(origin, point2, point3);
    }

    private static bool IsPointOnLine(Vector2 point, Vector2 lineStart, Vector2 lineEnd, float threshold = 3)
    {
        var rect = new ImRect(lineStart, lineEnd).MakePositive();
        rect.Expand(threshold);
        if (!rect.Contains(point))
            return false;

        var positionOnLine = GetClosestPointOnLine(point, lineStart, lineEnd);
        return Vector2.Distance(point, positionOnLine) <= threshold;
    }

    private static Vector2 GetClosestPointOnLine(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        var v = (lineEnd - lineStart);
        var vLen = v.Length();

        var d = Vector2.Dot(v, point - lineStart) / vLen;
        return lineStart + v * d / vLen;
    }

    private static bool IsPointInTriangle(Vector2 p, Vector2 p0, Vector2 p1, Vector2 p2)
    {
        var a = 0.5f * (-p1.Y * p2.X + p0.Y * (-p1.X + p2.X) + p0.X * (p1.Y - p2.Y) + p1.X * p2.Y);
        var sign = a < 0 ? -1 : 1;
        var s = (p0.Y * p2.X - p0.X * p2.Y + (p2.Y - p0.Y) * p.X + (p0.X - p2.X) * p.Y) * sign;
        var t = (p0.X * p1.Y - p0.Y * p1.X + (p0.Y - p1.Y) * p.X + (p1.X - p0.X) * p.Y) * sign;

        return s > 0 && t > 0 && (s + t) < 2 * a * sign;
    }

    private static bool IsPointInQuad(Vector2 p, Vector2[] corners)
    {
        return IsPointInTriangle(p, corners[0], corners[1], corners[2])
               || IsPointInTriangle(p, corners[0], corners[2], corners[3]);
    }
    #endregion

    #region too many members
    
    
    public enum GizmoParts
    {
        None,
        PositionInScreenPlane,
        PositionXAxis,
        PositionYAxis,
        PositionZAxis,
        PositionOnXyPlane,
        PositionOnXzPlane,
        PositionOnYzPlane,

        // Scale gizmo parts
        ScaleXAxis,
        ScaleYAxis,
        ScaleZAxis,
        ScaleUniform,
        ScaleOnXyPlane,
        ScaleOnXzPlane,
        ScaleOnYzPlane,

        // Rotation gizmo parts
        RotationXAxis,
        RotationYAxis,
        RotationZAxis,
        RotationScreen,
    }
    
    #endregion

    private static Instance? _instance;
    private static ITransformable? _transformable;

    private static GizmoParts _draggedGizmoPart = GizmoParts.None;
    private static ITransformable? _draggedTransformable;
    private static ChangeInputValueCommand? _inputCommandInFlight;

    private static float _centerPadding;
    private static float _gizmoLength;
    private static float _planeGizmoSize;

    private static ImDrawListPtr _drawList = null;
    private static bool _isDrawListValid;

    private static uint _dragInteractionWindowId;

    private static readonly HashSet<ITransformable> _selectedTransformables = [];

    private static SharpDX.Mathematics.Interop.RawViewportF _viewport;
    private static Vector2 _mousePosInScreen;
    private static ImageOutputCanvas? _canvas;
    private static Vector2 _topLeftOnScreen;

    private static Vector2 _originInScreen;
    private static Vector3 _originInClipSpace;
    private static bool _renderGizmo;
    
    // Keep values when interaction started
    private static Vector3 _initialOrigin;
    private static Vector2 _initialOffsetToOrigin;
    private static Matrix4x4 _initialObjectToLocal;
    private static Vector3 _initialIntersectionPoint;

    private static Plane _plane;

    private static Matrix4x4 _objectToClipSpace;
    private static Matrix4x4 _objectToWorld;
    private static Matrix4x4 _localToObject;
    private static Matrix4x4 _localToClipSpace;

    private static Vector3 _viewDirLocal;

    // Scale gizmo state
    private static Vector3 _initialScale;

    private static Vector3 _currentRotationVector;
    private static Vector3 _initialRotation;
    private static Vector3 _initialRotationVector;
    private static Vector3 _draggedRotationAxis;
    private static Vector2 _initialMousePos;
}