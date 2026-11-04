#nullable enable
using LibTessDotNet;
using Newtonsoft.Json;
using T3.Core.Animation;
using T3.Core.DataTypes.Vector;
using T3.Core.Resource.Assets;
using T3.Core.Utils;
using T3.Serialization;
using T3.SystemUi;

// ReSharper disable RedundantNameQualifier
// ReSharper disable once InconsistentNaming

namespace Lib.point._experimental;

[Guid("b238b288-6e9b-4b91-bac9-3d7566416028")]
[ExportDependencies("LibTessDotNet.dll")]
internal sealed class _SketchImpl : Instance<_SketchImpl>
{
    [Output(Guid = "EB2272B3-8B4A-46B1-A193-8B10BDC2B038")]
    public readonly Slot<object> OutPages = new();

    [Output(Guid = "974F46E5-B1DC-40AE-AC28-BBB1FB032EFE")]
    public readonly Slot<Vector3> CursorPosInWorld = new();

    [Output(Guid = "532B35D1-4FEE-41E6-AA6A-D42152DCE4A0")]
    public readonly Slot<float> CurrentBrushSize = new();

    [Output(Guid = "E1B35EFA-3A49-4AB3-83AE-A2DED1CEF908")]
    public readonly Slot<int> ActivePageIndexOutput = new();

    [Output(Guid = "BD29C7D2-1296-48CB-AD85-F96C27A35B92")]
    public readonly Slot<string> StatusMessage = new();

    /** Lasso outline and color picker, drawn on top of the sketch. */
    [Output(Guid = "6C3E9D41-2B7A-4F0E-9A51-8D2F4C7B1E63")]
    public readonly Slot<StructuredList> OverlayPoints = new();

    /** Filled shapes of the active page, three points per triangle. */
    [Output(Guid = "A41F7C2E-5D83-4B96-8E0A-3C9B6D2F7E15")]
    public readonly Slot<StructuredList> FillTriangles = new();

    public _SketchImpl()
    {
        OutPages.UpdateAction += Update;
        CursorPosInWorld.UpdateAction += Update;
        StatusMessage.UpdateAction += Update;
        OverlayPoints.UpdateAction += Update;
        FillTriangles.UpdateAction += Update;

        _keyframeSync = new KeyframeSync(this);
        _paging = new Paging(this);
    }

    private string GetAbsolutePath(string relativePath)
    {
        var sketchInstance = Parent;
        var compositionWithSketchOp = sketchInstance?.Parent;

        if (sketchInstance == null || compositionWithSketchOp == null)
            return relativePath;

        AssetRegistry.TryResolveAddress(relativePath, compositionWithSketchOp, out var path2, out _, isFolder: false, logWarnings: true);
        return path2;
    }

    private string _absolutePath = string.Empty;
    private int _overridePageIndex;

    private ColorModes _colorMode = ColorModes.Page;

    private bool _enableKeyframeSync;
    private int _lastUpdateFrame = -1;

    private void Update(EvaluationContext context)
    {
        if (_lastUpdateFrame == Playback.FrameCount)
            return;

        _lastUpdateFrame = Playback.FrameCount;
        
        var isFilePathDirty = FilePath.DirtyFlag.IsDirty;
        
        var overrideIndexWasDirty = OverridePageIndex.DirtyFlag.IsDirty;
        _overridePageIndex = OverridePageIndex.GetValue(context);

        if (this.Parent == null)
        {
            Log.Warning("Implementation needs a wrapper op", this);
            return;
        }

        var wasModified = false;
        
        AssignUniqueFilePath();

        if (isFilePathDirty)
        {
            var filepath = FilePath.GetValue(context) ?? string.Empty;
            RegisterFilePath(GetAbsolutePath(filepath));
            //Log.Debug($"Absolute path: {_absolutePath}", this);
            _paging.LoadPages(_absolutePath);
        }

        var pageIndexNeedsUpdate = Math.Abs(_lastUpdateContextTime - context.LocalTime) > TimePrecision;
        if (pageIndexNeedsUpdate || isFilePathDirty || overrideIndexWasDirty)
        {
            _paging.UpdatePageIndex(context.LocalTime, _overridePageIndex);
            _lastUpdateContextTime = context.LocalTime;
        }

        _enableKeyframeSync = EnableKeyframeSync.GetValue(context);
        if (_enableKeyframeSync)
        {
            wasModified |= _keyframeSync.UpdateIfEnabled(_enableKeyframeSync, Playback.FrameCount);
            
        }

        if (IsOpSelected)
            OperatorKeyboardCapture.Claim(Parent.SymbolChildId);

        HandleBrushSizeKeys(context);

        // Switch colors
        var colorMode = ColorMode.GetEnumValue<ColorModes>(context);
        // Compares values instead of the dirty flag: the color square sets the brush color right away, and the
        // parameter it writes only reaches this input later, so a stale value must not overwrite it.
        var color = StrokeColor.GetValue(context);
        var isColorDirty = !_hasReadStrokeColor || color != _lastReadStrokeColor;
        _lastReadStrokeColor = color;
        if (isColorDirty)
            _strokeColor = color;

        var colorNeedsUpdate = colorMode != _colorMode || isColorDirty;
        if (colorNeedsUpdate)
        {
            _colorMode = colorMode;
            if (colorMode == ColorModes.Page)
            {
                ColorizePage(color);
            }
            else if (isColorDirty && _hasReadStrokeColor)
            {
                // Undoing the parameter change recolors the selection back, so this needs no undo step of its own.
                wasModified |= RecolorSelection(color);
            }
        }

        _hasReadStrokeColor = true;

        wasModified |= HandleShortcuts(context);
        wasModified |= DoSketch(context, out CursorPosInWorld.Value, out CurrentBrushSize.Value);
        UpdatePreviousKeyStates();

        OutPages.Value = _paging.Pages;
        ActivePageIndexOutput.Value = _paging.ActivePageIndex;
        var pageTitle = _paging.HasActivePage ? $"PAGE{_paging.ActivePageIndex}" : "EMPTY PAGE";
        var tool = !IsOpSelected
                       ? "Not selected"
                       : _isPickingColor
                           ? "COLOR (SHIFT CHANGES HUE / CLICK TO APPLY / ESC TO CANCEL)"
                       : _transformMode switch
                             {
                                 TransformModes.Move  => "MOVE (CLICK TO APPLY / ESC TO CANCEL)",
                                 TransformModes.Scale => "SCALE (CLICK TO APPLY / ESC TO CANCEL)",
                                 _ => _drawMode switch
                                          {
                                              DrawModes.Draw  => _isFillBrush ? "FILL" : "PEN",
                                              DrawModes.Erase => "ERASE",
                                              DrawModes.Lasso => "LASSO",
                                              _               => "VIEW",
                                          },
                             };

        var pasteHint = Clipboard.Count > 0 ? "/ PASTE WITH V" : "";
        StatusMessage.Value = $"{pageTitle}: {tool} {pasteHint}";
        OverlayPoints.Value = BuildOverlay(context);

        if (wasModified)
        {
            _lastModificationTime = Playback.RunTimeInSecs;
            _sketchRevision++;
        }

        FillTriangles.Value = BuildFillTriangles();

        var needsSave = _lastSavedSketchVersion < _sketchRevision;

        if (needsSave && Playback.RunTimeInSecs - _lastModificationTime > 2)
        {
            //Log.Debug("Saving?");
            //var filepath1 = FilePath.GetValue(context);
            var folder = Path.GetDirectoryName(_absolutePath);
            if (string.IsNullOrEmpty(folder))
            {
                Log.Warning("No directory for sketch?", this);
                return;
            }

            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception e)
            {
                Log.Warning($"Can't create sketch directory {folder}? (${e.Message}", this);
                return;
            }

            JsonUtils.TrySaveJson(_paging.Pages, _absolutePath);
            _lastSavedSketchVersion = _sketchRevision;
        }
    }

    private void AssignUniqueFilePath()
    {
        if (Parent == null)
            return;

        var composition = Parent.Parent;
        if (composition == null)
            return;

        var pathInput = Parent.Inputs.FirstOrDefault(i => i.Id == _pathPathInputId);
        if (pathInput == null)
            return;

        if (!pathInput.Input.IsDefault)
            return;

        if (pathInput is not InputSlot<string> stringInput)
            return;

        var symbolPackageName = Parent.Parent?.Symbol.SymbolPackage.Name;
        if (string.IsNullOrEmpty(symbolPackageName))
            return;

        var path = $"{symbolPackageName}:sketches/{Parent.SymbolChildId.ShortenGuid()}.json";

        stringInput.SetTypedInputValue(path);
    }

    private void RegisterFilePath(string absolutePath)
    {
        if (SketchesByFilePath.TryGetValue(_absolutePath, out var registered) && registered == this)
            SketchesByFilePath.Remove(_absolutePath);

        _absolutePath = absolutePath;
        SketchesByFilePath[_absolutePath] = this;
    }

    protected override void Dispose(bool disposing)
    {
        if (SketchesByFilePath.TryGetValue(_absolutePath, out var registered) && registered == this)
            SketchesByFilePath.Remove(_absolutePath);

        base.Dispose(disposing);
    }

    #region keyboard
    private void HandleBrushSizeKeys(EvaluationContext context)
    {
        if (StrokeSize.DirtyFlag.IsDirty)
        {
            _brushSize = StrokeSize.GetValue(context);
        }

        if (!HasKeyboard || KeyHandler.PressedKeys[(int)Key.CtrlKey] || KeyHandler.PressedKeys[(int)Key.ShiftKey])
            return;

        for (var index = 0; index < _numberKeys.Length; index++)
        {
            if (WasKeyPressed(_numberKeys[index]))
                SetBrushSize((index * index + 0.5f) * 0.1f);
        }

        if (WasKeyPressed(Key.SquareBracketLeft))
            SetBrushSize(_brushSize / BrushSizeStepFactor);

        if (WasKeyPressed(Key.SquareBracketRight))
            SetBrushSize(_brushSize * BrushSizeStepFactor);
    }

    /** Also writes the Stroke Size parameter, so it shows the size in use and is saved with the project. */
    private void SetBrushSize(float size)
    {
        _brushSize = Math.Clamp(size, MinBrushSize, MaxBrushSize);
        if (Parent?.Inputs.FirstOrDefault(i => i.Id == _strokeSizeInputId) is InputSlot<float> strokeSizeInput)
            strokeSizeInput.SetTypedInputValue(_brushSize);
    }

    private bool HandleShortcuts(EvaluationContext context)
    {
        if (!HasKeyboard || KeyHandler.PressedKeys[(int)Key.CtrlKey])
            return false;

        if (_isPickingColor)
        {
            if (WasKeyPressed(Key.Esc))
            {
                CancelColorPicking();
                return true;
            }

            // Hold Q, move and release applies; a tap without moving keeps the square open.
            if (_isPickerKeyHeld && !KeyHandler.PressedKeys[(int)Key.Q])
            {
                _isPickerKeyHeld = false;
                var movedDistance = Vector2.Distance(MousePos.GetValue(context), _pickerStartMousePos);
                if (movedDistance > MinPickerMoveForHoldToApply)
                {
                    ConfirmColorPicking();
                    return true;
                }
            }

            if (WasKeyPressed(Key.Return) || WasKeyPressed(Key.Q))
            {
                ConfirmColorPicking();
                return true;
            }

            return false;
        }

        if (_transformMode != TransformModes.None)
        {
            if (WasKeyPressed(Key.Esc))
            {
                CancelTransform();
                return true;
            }

            if (WasKeyPressed(Key.Return))
            {
                ConfirmTransform();
                return true;
            }

            return false;
        }

        if (WasKeyPressed(Key.B) || WasKeyPressed(Key.F))
        {
            _drawMode = DrawModes.Draw;
            _isFillBrush = WasKeyPressed(Key.F);
            ClearSelection();
            return true;
        }

        if (WasKeyPressed(Key.E))
        {
            _drawMode = DrawModes.Erase;
            return DeleteSelection("Erase sketch selection");
        }

        if (WasKeyPressed(Key.S))
        {
            _drawMode = DrawModes.Lasso;
            return false;
        }

        if (WasKeyPressed(Key.G))
        {
            StartTransform(TransformModes.Move, context);
            return false;
        }

        if (WasKeyPressed(Key.R))
        {
            StartTransform(TransformModes.Scale, context);
            return false;
        }

        if (WasKeyPressed(Key.Q))
        {
            StartColorPicking(context);
            return false;
        }

        if (WasKeyPressed(Key.C))
        {
            CopyToClipboard(out _);
            return false;
        }

        if (WasKeyPressed(Key.X))
        {
            if (!CopyToClipboard(out var copiedSelection))
                return false;

            return copiedSelection
                       ? DeleteSelection("Cut sketch selection")
                       : CutPage();
        }

        if (WasKeyPressed(Key.V))
            return PasteFromClipboard();

        if (WasKeyPressed(Key.Esc))
        {
            ClearSelection();
            return true;
        }

        return false;
    }

    /** KeyHandler only reports held keys; actions must fire once per press, not on every frame the key is down. */
    private bool WasKeyPressed(Key key) => WasKeyPressed((int)key);

    private bool WasKeyPressed(int keyCode) => KeyHandler.PressedKeys[keyCode] && !_previousKeyStates[keyCode];

    private void UpdatePreviousKeyStates()
    {
        for (var keyCode = 0; keyCode < _previousKeyStates.Length; keyCode++)
        {
            _previousKeyStates[keyCode] = KeyHandler.PressedKeys[keyCode];
        }
    }
    #endregion

    #region selection, clipboard and undo
    private static bool IsSelected(in Point point) => point.F2 > 0.5f && !Point.IsSeparator(point);

    private static bool HasSelection(Page page)
    {
        var points = page.PointsList.TypedElements;
        var count = Math.Min(page.WriteIndex, points.Length);
        for (var index = 0; index < count; index++)
        {
            if (IsSelected(points[index]))
                return true;
        }

        return false;
    }

    private void ClearSelection()
    {
        if (_paging.ActivePage == null || CurrentPointList == null)
            return;

        for (var index = 0; index < CurrentPointList.TypedElements.Length; index++)
        {
            CurrentPointList.TypedElements[index].F2 = 0;
        }

        _sketchRevision++;
    }

    private void ColorizePage(Vector4 fillColor)
    {
        if (_paging.ActivePage == null || CurrentPointList == null)
            return;

        for (var index = 0; index < CurrentPointList.TypedElements.Length; index++)
        {
            CurrentPointList.TypedElements[index].Color = fillColor;
        }

        _sketchRevision++;
    }

    /** Turns the selected points into separators, which splits strokes where a selection ends. */
    private bool DeleteSelection(string editName)
    {
        var page = _paging.ActivePage;
        if (page == null || !HasSelection(page))
            return false;

        var edit = BeginEdit();
        var points = page.PointsList.TypedElements;
        var count = Math.Min(page.WriteIndex, points.Length);
        for (var index = 0; index < count; index++)
        {
            if (IsSelected(points[index]))
                points[index].Scale = Vector3.One * float.NaN;
        }

        CommitEdit(edit, editName);
        return true;
    }

    /** Copies the selected strokes, or all strokes of the page if nothing is selected. */
    private bool CopyToClipboard(out bool copiedSelection)
    {
        copiedSelection = false;
        var page = _paging.ActivePage;
        if (page == null)
            return false;

        copiedSelection = HasSelection(page);
        Clipboard.Clear();

        var points = page.PointsList.TypedElements;
        var count = Math.Min(page.WriteIndex, points.Length);
        for (var index = 0; index < count; index++)
        {
            var point = points[index];
            var isIncluded = !Point.IsSeparator(point) && (!copiedSelection || IsSelected(point));
            if (isIncluded)
            {
                point.F2 = 0;
                Clipboard.Add(point);
            }
            else if (Clipboard.Count > 0 && !Point.IsSeparator(Clipboard[^1]))
            {
                Clipboard.Add(Point.Separator());
            }
        }

        if (Clipboard.Count > 0 && !Point.IsSeparator(Clipboard[^1]))
            Clipboard.Add(Point.Separator());

        return Clipboard.Count > 0;
    }

    private bool CutPage()
    {
        if (_paging.ActivePage == null)
            return false;

        var edit = BeginEdit();
        _paging.RemoveActivePage(_overridePageIndex);
        CommitEdit(edit, "Cut sketch page");
        return true;
    }

    /** Appends the copied strokes to the current page and selects them, so they can be moved right away. */
    private bool PasteFromClipboard()
    {
        if (Clipboard.Count == 0)
            return false;

        var edit = BeginEdit();
        if (!_paging.HasActivePage)
            _paging.InsertNewPage();

        ClearSelection();
        foreach (var copiedPoint in Clipboard)
        {
            var point = copiedPoint;
            if (!Point.IsSeparator(point))
                point.F2 = 1;

            AppendPoint(point);
        }

        CommitEdit(edit, "Paste sketch strokes");
        return true;
    }

    private SketchEdit BeginEdit() => new(this);

    private void CommitEdit(SketchEdit edit, string name)
    {
        edit.Complete(this);
        OperatorUndoRedo.Add(name, edit.Undo, edit.Redo);
    }

    private void ApplyEdit(List<Page> pages, Page? page, PageContent? content)
    {
        _transformMode = TransformModes.None;
        _transformEdit = null;
        _isPickingColor = false;
        _colorPickerEdit = null;
        _strokeEdit = null;
        _isLassoActive = false;
        _currentStrokeLength = 0;
        _hasTailPoint = false;

        if (page != null && content != null)
            page.Restore(content);

        _paging.ReplacePages(pages);
        _sketchRevision++;
        _lastModificationTime = Playback.RunTimeInSecs;
    }
    #endregion

    #region move and scale
    private void StartTransform(TransformModes mode, EvaluationContext context)
    {
        var page = _paging.ActivePage;
        if (page == null || _isMouseDown || !HasSelection(page))
            return;

        var points = page.PointsList.TypedElements;
        var count = Math.Min(page.WriteIndex, points.Length);
        var sum = Vector3.Zero;
        var selectedCount = 0;
        for (var index = 0; index < count; index++)
        {
            if (!IsSelected(points[index]))
                continue;

            sum += points[index].Position;
            selectedCount++;
        }

        _transformEdit = BeginEdit();
        _transformMode = mode;
        _transformCenter = sum / selectedCount;
        _transformStartPos = CalcPosInWorld(context, MousePos.GetValue(context));
    }

    /** Sets the selected points from their positions before the transform started, so the result never drifts. */
    private bool ApplyTransform(Vector3 posInWorld)
    {
        var page = _paging.ActivePage;
        var before = _transformEdit?.ContentBefore;
        if (page == null || before == null || page != _transformEdit!.Page)
        {
            CancelTransform();
            return true;
        }

        var offset = Vector3.Zero;
        var scale = 1f;
        if (_transformMode == TransformModes.Move)
        {
            offset = posInWorld - _transformStartPos;
        }
        else
        {
            var startDistance = Vector3.Distance(_transformStartPos, _transformCenter);
            if (startDistance > 0.00001f)
                scale = Vector3.Distance(posInWorld, _transformCenter) / startDistance;
        }

        var points = page.PointsList.TypedElements;
        var originalPoints = before.Points.TypedElements;
        var count = Math.Min(Math.Min(page.WriteIndex, points.Length), originalPoints.Length);
        for (var index = 0; index < count; index++)
        {
            if (!IsSelected(points[index]))
                continue;

            var originalPos = originalPoints[index].Position;
            points[index].Position = _transformCenter + (originalPos - _transformCenter) * scale + offset;
        }

        return true;
    }

    private void ConfirmTransform()
    {
        if (_transformEdit == null)
            return;

        CommitEdit(_transformEdit, _transformMode == TransformModes.Move ? "Move sketch strokes" : "Scale sketch strokes");
        _transformEdit = null;
        _transformMode = TransformModes.None;
    }

    private void CancelTransform()
    {
        var edit = _transformEdit;
        _transformEdit = null;
        _transformMode = TransformModes.None;
        edit?.Revert(this);
    }
    #endregion

    #region lasso
    private bool ContinueLasso(Vector2 mousePos, Vector3 posInWorld)
    {
        if (!_isLassoActive || _paging.ActivePage == null || CurrentPointList == null)
            return false;

        var posInClipSpace = MouseToClipSpace(mousePos);
        if (_lassoPointsInClipSpace.Count > 0
            && Vector2.Distance(_lassoPointsInClipSpace[^1], posInClipSpace) < MinLassoPointDistanceInClipSpace)
            return false;

        if (_lassoPointsInWorld.Count >= MaxOverlayPoints - 2)
            return false;

        _lassoPointsInClipSpace.Add(posInClipSpace);
        _lassoPointsInWorld.Add(posInWorld);
        return true;
    }

    /** Tests in clip space, so strokes are selected by where they appear even if the camera moved since drawing. */
    private void SelectInsideLasso(EvaluationContext context)
    {
        var page = _paging.ActivePage;
        if (page == null || _lassoPointsInClipSpace.Count < 3)
            return;

        var worldToClipSpace = Matrix4x4.Multiply(context.WorldToCamera, context.CameraToClipSpace);
        var points = page.PointsList.TypedElements;
        var count = Math.Min(page.WriteIndex, points.Length);
        for (var index = 0; index < count; index++)
        {
            if (Point.IsSeparator(points[index]))
                continue;

            var posInClipSpace = Vector4.Transform(new Vector4(points[index].Position, 1), worldToClipSpace);
            if (posInClipSpace.W <= 0)
                continue;

            var projected = new Vector2(posInClipSpace.X, posInClipSpace.Y) / posInClipSpace.W;
            if (IsInsidePolygon(_lassoPointsInClipSpace, projected))
                points[index].F2 = 1;
        }
    }

    /** Even-odd rule, so self-intersecting lassos behave predictably. */
    private static bool IsInsidePolygon(List<Vector2> polygon, Vector2 point)
    {
        var isInside = false;
        for (int index = 0, previousIndex = polygon.Count - 1; index < polygon.Count; previousIndex = index++)
        {
            var a = polygon[index];
            var b = polygon[previousIndex];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                isInside = !isInside;
            }
        }

        return isInside;
    }

    private static Vector2 MouseToClipSpace(Vector2 mousePos) => new((mousePos.X - 0.5f) * 2, (-mousePos.Y + 0.5f) * 2);
    #endregion

    #region color picking
    /// <summary>
    /// Opens a square placed so the cursor starts on the current color: horizontal is opacity, vertical runs from
    /// black over the color to white, Shift with horizontal movement changes the hue. With a selection, the
    /// selection's color is edited live.
    /// </summary>
    private void StartColorPicking(EvaluationContext context)
    {
        if (_isMouseDown)
            return;

        var page = _paging.ActivePage;
        var baseColor = _strokeColor;
        _colorPickerEdit = null;
        if (page != null && TryGetFirstSelectedColor(page, out var selectedColor))
        {
            baseColor = selectedColor;
            _colorPickerEdit = BeginEdit();
        }

        new Color(baseColor).GetHSV(out _pickerHue, out _pickerSaturation, out var value);
        _pickerStartTone = value < 1 ? value / 2 : 0.5f;
        _pickerBaseColor = baseColor;
        _pickedColor = baseColor;
        _pickerAnchorMousePos = MousePos.GetValue(context);
        _lastPickerMousePos = _pickerAnchorMousePos;
        _pickerStartMousePos = _pickerAnchorMousePos;
        _isPickerKeyHeld = true;
        _isPickingColor = true;
    }

    private bool UpdateColorPicking(Vector2 mousePos)
    {
        var mouseDelta = mousePos - _lastPickerMousePos;
        _lastPickerMousePos = mousePos;
        if (KeyHandler.PressedKeys[(int)Key.ShiftKey])
        {
            // The square follows the cursor while the hue changes, so opacity stays where it was.
            _pickerHue = (_pickerHue + mouseDelta.X * HueChangePerScreenWidth) % 1f;
            if (_pickerHue < 0)
                _pickerHue += 1;

            _pickerAnchorMousePos.X += mouseDelta.X;
        }

        var (opacity, tone) = GetPickerCoordinates(mousePos);
        _pickedColor = GetPickerColor(opacity, tone);

        if (_colorPickerEdit != null)
            RecolorSelection(_pickedColor);

        return true;
    }

    private void ConfirmColorPicking()
    {
        if (!_isPickingColor)
            return;

        _isPickingColor = false;
        if (_colorPickerEdit != null)
        {
            CommitEdit(_colorPickerEdit, "Recolor sketch selection");
            _colorPickerEdit = null;
            return;
        }

        _strokeColor = _pickedColor;

        // Writes the parameter, so the picked color shows in the parameter window and is saved with the project.
        if (Parent?.Inputs.FirstOrDefault(i => i.Id == _strokeColorInputId) is InputSlot<Vector4> strokeColorInput)
            strokeColorInput.SetTypedInputValue(_pickedColor);
    }

    private void CancelColorPicking()
    {
        _isPickingColor = false;
        var edit = _colorPickerEdit;
        _colorPickerEdit = null;
        edit?.Revert(this);
    }

    /** The square's center, offset from where it was opened so that point shows the starting color. */
    private Vector2 GetPickerCenterInClipSpace()
    {
        var halfWidth = PickerHalfHeightInClipSpace / _aspectRatio;
        var offset = new Vector2((0.5f - _pickerBaseColor.W) * 2 * halfWidth,
                                 (0.5f - _pickerStartTone) * 2 * PickerHalfHeightInClipSpace);
        return MouseToClipSpace(_pickerAnchorMousePos) + offset;
    }

    /** The cursor's position in the square, as opacity (horizontal) and tone (vertical), both 0..1. */
    private (float Opacity, float Tone) GetPickerCoordinates(Vector2 mousePos)
    {
        var halfWidth = PickerHalfHeightInClipSpace / _aspectRatio;
        var offsetInClipSpace = MouseToClipSpace(mousePos) - GetPickerCenterInClipSpace();
        var opacity = Math.Clamp(offsetInClipSpace.X / (2 * halfWidth) + 0.5f, 0, 1);
        var tone = Math.Clamp(offsetInClipSpace.Y / (2 * PickerHalfHeightInClipSpace) + 0.5f, 0, 1);
        return (opacity, tone);
    }

    /** The lower half darkens the color to black, the upper half fades it to white. */
    private Vector4 GetPickerColor(float opacity, float tone)
    {
        var value = tone < 0.5f ? tone * 2 : 1;
        var saturation = tone > 0.5f
                             ? MathUtils.Lerp(_pickerSaturation, 0, (tone - 0.5f) * 2)
                             : _pickerSaturation;
        return Color.FromHSV(_pickerHue, saturation, value, opacity).Rgba;
    }

    private bool RecolorSelection(Vector4 color)
    {
        var page = _paging.ActivePage;
        if (page == null)
            return false;

        var points = page.PointsList.TypedElements;
        var count = Math.Min(page.WriteIndex, points.Length);
        var wasModified = false;
        for (var index = 0; index < count; index++)
        {
            if (!IsSelected(points[index]))
                continue;

            points[index].Color = color;
            wasModified = true;
        }

        return wasModified;
    }

    private static bool TryGetFirstSelectedColor(Page page, out Vector4 color)
    {
        var points = page.PointsList.TypedElements;
        var count = Math.Min(page.WriteIndex, points.Length);
        for (var index = 0; index < count; index++)
        {
            if (!IsSelected(points[index]))
                continue;

            color = points[index].Color;
            return true;
        }

        color = Vector4.One;
        return false;
    }
    #endregion

    #region overlay
    /// <summary>
    /// Fills the overlay with the lasso outline or the color picker. The picker is drawn as horizontal bands of
    /// lines whose colors are interpolated along each line, which matches the piecewise linear mapping exactly.
    /// </summary>
    private StructuredList BuildOverlay(EvaluationContext context)
    {
        _aspectRatio = Math.Abs(context.CameraToClipSpace.M11) > 0.0001f
                           ? context.CameraToClipSpace.M22 / context.CameraToClipSpace.M11
                           : 1;

        _overlayCount = 0;
        if (_isLassoActive && _lassoPointsInWorld.Count > 1)
        {
            for (var index = 0; index <= _lassoPointsInWorld.Count; index++)
            {
                AddOverlayPoint(_lassoPointsInWorld[index % _lassoPointsInWorld.Count], LassoColor, LassoLineScale);
            }

            AddOverlaySeparator();
        }
        else if (_isPickingColor && IsOpSelected)
        {
            AddColorPickerToOverlay(context);
        }

        if (_overlayCount == 0)
            return _emptyOverlay;

        // Unused entries from earlier frames must not be drawn.
        var points = _overlayPoints.TypedElements;
        for (var index = _overlayCount; index < _lastOverlayCount; index++)
        {
            points[index] = Point.Separator();
        }

        _lastOverlayCount = _overlayCount;
        return _overlayPoints;
    }

    private void AddColorPickerToOverlay(EvaluationContext context)
    {
        var center = GetPickerCenterInClipSpace();
        var halfHeight = PickerHalfHeightInClipSpace;
        var halfWidth = halfHeight / _aspectRatio;
        var bandHeight = 2 * halfHeight / PickerBandCount;

        // DrawLines widths are 0.2 * Scale in clip space units of the screen height.
        var bandScale = bandHeight / 0.2f;
        for (var bandIndex = 0; bandIndex < PickerBandCount; bandIndex++)
        {
            var tone = (bandIndex + 0.5f) / PickerBandCount;
            var y = center.Y + (tone * 2 - 1) * halfHeight;
            for (var column = -1; column <= 1; column++)
            {
                var color = GetPickerColor((column + 1) * 0.5f, tone);
                AddOverlayPoint(ClipSpaceToWorld(context, new Vector2(center.X + column * halfWidth, y)), color, bandScale);
            }

            AddOverlaySeparator();
        }

        // Frame, and a bar above it comparing the original color (left) with the picked one (right).
        var frameScale = PickerFrameLineWidthInClipSpace / 0.2f;
        var left = center.X - halfWidth;
        var right = center.X + halfWidth;
        var bottom = center.Y - halfHeight;
        var top = center.Y + halfHeight;
        AddOverlayPoint(ClipSpaceToWorld(context, new Vector2(left, bottom)), PickerFrameColor, frameScale);
        AddOverlayPoint(ClipSpaceToWorld(context, new Vector2(right, bottom)), PickerFrameColor, frameScale);
        AddOverlayPoint(ClipSpaceToWorld(context, new Vector2(right, top)), PickerFrameColor, frameScale);
        AddOverlayPoint(ClipSpaceToWorld(context, new Vector2(left, top)), PickerFrameColor, frameScale);
        AddOverlayPoint(ClipSpaceToWorld(context, new Vector2(left, bottom)), PickerFrameColor, frameScale);
        AddOverlaySeparator();

        var barY = top + PickerComparisonBarHeightInClipSpace;
        var barScale = PickerComparisonBarHeightInClipSpace / 0.2f;
        AddOverlayPoint(ClipSpaceToWorld(context, new Vector2(left, barY)), _pickerBaseColor, barScale);
        AddOverlayPoint(ClipSpaceToWorld(context, new Vector2(center.X, barY)), _pickerBaseColor, barScale);
        AddOverlaySeparator();
        AddOverlayPoint(ClipSpaceToWorld(context, new Vector2(center.X, barY)), _pickedColor, barScale);
        AddOverlayPoint(ClipSpaceToWorld(context, new Vector2(right, barY)), _pickedColor, barScale);
        AddOverlaySeparator();
    }

    private void AddOverlayPoint(Vector3 position, Vector4 color, float scale)
    {
        if (_overlayCount >= MaxOverlayPoints - 1)
            return;

        _overlayPoints.TypedElements[_overlayCount++] = new Point
                                                            {
                                                                Position = position,
                                                                Color = color,
                                                                Scale = Vector3.One * scale,
                                                                F2 = 0,
                                                            };
    }

    private void AddOverlaySeparator()
    {
        if (_overlayCount >= MaxOverlayPoints)
            return;

        _overlayPoints.TypedElements[_overlayCount++] = Point.Separator();
    }

    private static Vector3 ClipSpaceToWorld(EvaluationContext context, Vector2 posInClipSpace)
    {
        return CalcPosInWorld(context, new Vector2(posInClipSpace.X / 2 + 0.5f, 0.5f - posInClipSpace.Y / 2));
    }
    #endregion

    #region fills
    private static bool IsFillPoint(in Point point) => point.F1 < 0 && !Point.IsSeparator(point);

    /// <summary>
    /// Tessellates every fill stroke of the active page into triangles. Only runs when the page or the sketch
    /// changed, so it allocates while drawing but not while idle.
    /// </summary>
    private StructuredList BuildFillTriangles()
    {
        var page = _paging.ActivePage;
        if (page == null)
            return _emptyFill;

        if (page == _filledPage && _sketchRevision == _filledSketchRevision)
            return _fillCount == 0 ? _emptyFill : _fillPoints;

        _filledPage = page;
        _filledSketchRevision = _sketchRevision;
        _fillCount = 0;

        var points = page.PointsList.TypedElements;
        // While drawing, the tail point that follows the cursor sits at the write index.
        var count = Math.Min(page.WriteIndex + (_hasTailPoint ? 1 : 0), points.Length);
        var runStart = -1;
        for (var index = 0; index <= count; index++)
        {
            var isInRun = index < count && IsFillPoint(points[index]);
            if (isInRun)
            {
                if (runStart < 0)
                    runStart = index;

                continue;
            }

            if (runStart >= 0 && index - runStart >= 3)
                AddFillTriangles(points, runStart, index - runStart);

            runStart = -1;
        }

        if (_fillCount == 0)
            return _emptyFill;

        if (_fillPoints.NumElements != _fillCount)
            _fillPoints.SetLength(_fillCount);

        Array.Copy(_fillBuffer, _fillPoints.TypedElements, _fillCount);
        return _fillPoints;
    }

    /** Non-zero winding, so a shape that crosses itself is filled solid like in other drawing apps. */
    private void AddFillTriangles(Point[] points, int start, int length)
    {
        var contour = new ContourVertex[length];
        for (var index = 0; index < length; index++)
        {
            var position = points[start + index].Position;
            contour[index] = new ContourVertex(new Vec3(position.X, position.Y, position.Z));
        }

        var tess = new Tess();
        tess.AddContour(contour, ContourOrientation.Original);
        tess.Tessellate(WindingRule.NonZero, ElementType.Polygons, 3);

        var color = points[start].Color;
        var vertices = tess.Vertices;
        var elements = tess.Elements;
        for (var triangleIndex = 0; triangleIndex < tess.ElementCount; triangleIndex++)
        {
            var i0 = elements[triangleIndex * 3];
            var i1 = elements[triangleIndex * 3 + 1];
            var i2 = elements[triangleIndex * 3 + 2];
            if (i0 == Tess.Undef || i1 == Tess.Undef || i2 == Tess.Undef)
                continue;

            AddFillVertex(vertices[i0].Position, color);
            AddFillVertex(vertices[i1].Position, color);
            AddFillVertex(vertices[i2].Position, color);
        }
    }

    private void AddFillVertex(Vec3 position, Vector4 color)
    {
        if (_fillCount >= _fillBuffer.Length)
            Array.Resize(ref _fillBuffer, _fillBuffer.Length * 2);

        _fillBuffer[_fillCount++] = new Point
                                        {
                                            Position = new Vector3(position.X, position.Y, position.Z),
                                            Color = color,
                                            Scale = Vector3.One,
                                        };
    }
    #endregion

    #region strokes
    private bool DoSketch(EvaluationContext context, out Vector3 posInWorld, out float visibleBrushSize)
    {
        visibleBrushSize = _brushSize;
        if (_drawMode == DrawModes.Erase)
            visibleBrushSize *= 4;

        var mousePos = MousePos.GetValue(context);
        posInWorld = CalcPosInWorld(context, mousePos);

        if (_drawMode == DrawModes.View || !IsOpSelected)
        {
            var wasModified = EndStroke(context);
            if (_transformMode != TransformModes.None)
            {
                CancelTransform();
                wasModified = true;
            }

            if (_isPickingColor)
            {
                CancelColorPicking();
                wasModified = true;
            }

            _isMouseDown = false;
            _isResizingBrush = false;
            return wasModified;
        }

        var isMouseDown = IsMouseButtonDown.GetValue(context);
        var justReleased = !isMouseDown && _isMouseDown;
        var justPressed = isMouseDown && !_isMouseDown;
        _isMouseDown = isMouseDown;

        if (_ignoreMouseUntilReleased)
        {
            if (!isMouseDown)
                _ignoreMouseUntilReleased = false;

            return false;
        }

        if (_isPickingColor)
        {
            if (justPressed)
            {
                ConfirmColorPicking();
                _ignoreMouseUntilReleased = true;
                return true;
            }

            return UpdateColorPicking(mousePos);
        }

        if (_isResizingBrush)
        {
            if (!isMouseDown)
            {
                _isResizingBrush = false;
                return false;
            }

            // Right or up grows, left or down shrinks. Horizontal moves are measured in screen heights, so both
            // axes feel alike; exponential, so the same distance feels alike for small and large brushes.
            var dragDistance = (mousePos.X - _brushResizeStartMousePos.X) * _aspectRatio - (mousePos.Y - _brushResizeStartMousePos.Y);
            var scale = MathF.Exp(dragDistance * BrushResizeSpeed);
            SetBrushSize(_brushSizeAtResizeStart * scale);
            visibleBrushSize = _drawMode == DrawModes.Erase ? _brushSize * 4 : _brushSize;
            posInWorld = _brushResizeAnchorPos;
            return false;
        }

        if (justPressed && _transformMode == TransformModes.None && KeyHandler.PressedKeys[(int)Key.CtrlKey])
        {
            // The brush preview stays where the drag started, so its size can be judged against the drawing.
            _isResizingBrush = true;
            _brushResizeStartMousePos = mousePos;
            _brushSizeAtResizeStart = _brushSize;
            _brushResizeAnchorPos = posInWorld;
            return false;
        }

        if (_transformMode != TransformModes.None)
        {
            if (!justPressed)
                return ApplyTransform(posInWorld);

            ConfirmTransform();
            _ignoreMouseUntilReleased = true;
            return true;
        }

        if (justReleased)
            return EndStroke(context);

        if (!isMouseDown)
            return false;

        if (justPressed)
            BeginStroke();

        var isStrokeActive = _strokeEdit != null || _isLassoActive;
        if (!isStrokeActive)
            return false;

        switch (_strokeMode)
        {
            case DrawModes.Draw:
                return ContinueDrawing(posInWorld, visibleBrushSize, justPressed);

            case DrawModes.Erase:
                return EraseAt(posInWorld, visibleBrushSize);

            case DrawModes.Lasso:
                return ContinueLasso(mousePos, posInWorld);
        }

        return false;
    }

    private void BeginStroke()
    {
        _strokeMode = _drawMode;
        switch (_strokeMode)
        {
            case DrawModes.Draw:
            case DrawModes.Erase:
                _strokeEdit = BeginEdit();
                _strokeErasedPoints = false;
                break;

            case DrawModes.Lasso:
                if (!_paging.HasActivePage)
                    break;

                _lassoPointsInClipSpace.Clear();
                _lassoPointsInWorld.Clear();
                _isLassoActive = true;
                if (!KeyHandler.PressedKeys[(int)Key.ShiftKey])
                    ClearSelection();

                break;
        }
    }

    private bool EndStroke(EvaluationContext context)
    {
        var edit = _strokeEdit;
        _strokeEdit = null;

        switch (_strokeMode)
        {
            case DrawModes.Draw:
                if (edit == null)
                    return false;

                FinishDrawnStroke();
                CommitEdit(edit, "Draw sketch stroke");
                return true;

            case DrawModes.Erase:
                if (edit == null || !_strokeErasedPoints)
                    return false;

                CommitEdit(edit, "Erase sketch strokes");
                return true;

            case DrawModes.Lasso:
                if (!_isLassoActive)
                    return false;

                _isLassoActive = false;
                SelectInsideLasso(context);
                return true;
        }

        return false;
    }

    private bool ContinueDrawing(Vector3 posInWorld, float visibleBrushSize, bool justPressed)
    {
        if (_currentStrokeLength > 0 && GetPreviousStrokePoint(out var lastPoint)
                                     && Vector3.Distance(lastPoint.Position, posInWorld) < visibleBrushSize * MinPointDistanceForBrushSize)
        {
            UpdateTailPoint(lastPoint, posInWorld);
            return true;
        }

        if (!_paging.HasActivePage)
        {
            _paging.InsertNewPage();
            _sketchRevision++;
        }

        // Draw Lines with Shift
        if (justPressed && KeyHandler.PressedKeys[(int)Key.ShiftKey] && _paging.ActivePage!.WriteIndex > 1)
        {
            // Discard last separator point
            _paging.ActivePage.WriteIndex--;
            _currentStrokeLength = 1;
        }

        AppendPoint(new Point
                        {
                            Position = posInWorld,
                            Color = _strokeColor,
                            // A thin outline gives the filled shape antialiased edges.
                            Scale = Vector3.One * (_isFillBrush ? FillOutlineScale : visibleBrushSize / 2 + 0.002f),
                            F1 = _isFillBrush ? FillStrokeMarker : 1,
                            F2 = 0, // Not selected by default
                        });
        AppendPoint(Point.Separator(), advanceIndex: false);
        _currentStrokeLength++;
        _hasTailPoint = false;
        return true;
    }

    private void FinishDrawnStroke()
    {
        if (_paging.HasActivePage && _currentStrokeLength > 0)
        {
            // Add to points for single click to make it visible as a dot
            var wasClick = _currentStrokeLength == 1;
            if (wasClick)
            {
                if (GetPreviousStrokePoint(out var clickPoint))
                {
                    clickPoint.Position += Vector3.UnitY * 0.02f * 2 * _brushSize;
                    AppendPoint(clickPoint);
                }
            }
            else if (_hasTailPoint)
            {
                // End the stroke where the pen was lifted.
                _paging.ActivePage!.WriteIndex++;
            }

            AppendPoint(Point.Separator());
        }

        _currentStrokeLength = 0;
        _hasTailPoint = false;
    }

    private bool EraseAt(Vector3 posInWorld, float visibleBrushSize)
    {
        if (_paging.ActivePage == null || CurrentPointList == null)
            return false;

        var wasModified = false;
        for (var index = 0; index < CurrentPointList.NumElements; index++)
        {
            ref var point = ref CurrentPointList.TypedElements[index];
            if (Point.IsSeparator(point) || !(Vector3.Distance(posInWorld, point.Position) < visibleBrushSize * 0.02f))
                continue;

            point.Scale = Vector3.One * float.NaN;
            wasModified = true;
        }

        _strokeErasedPoints |= wasModified;
        return wasModified;
    }
    #endregion

    private static Vector3 CalcPosInWorld(EvaluationContext context, Vector2 mousePos)
    {
        const float offsetFromCamPlane = 0.99f;
        var posInClipSpace = new System.Numerics.Vector4((mousePos.X - 0.5f) * 2, (-mousePos.Y + 0.5f) * 2, offsetFromCamPlane, 1);
        Matrix4x4.Invert(context.CameraToClipSpace, out var clipSpaceToCamera);
        Matrix4x4.Invert(context.WorldToCamera, out var cameraToWorld);

        var clipSpaceToWorld = Matrix4x4.Multiply(clipSpaceToCamera, cameraToWorld);
        var m = Matrix4x4.Multiply(cameraToWorld, clipSpaceToCamera);
        Matrix4x4.Invert(m, out m);

        var p = Vector4.Transform(posInClipSpace, clipSpaceToWorld);
        return new System.Numerics.Vector3(p.X, p.Y, p.Z) / p.W;
    }

    private void AppendPoint(Point p, bool advanceIndex = true)
    {
        if (_paging.ActivePage == null || CurrentPointList == null)
        {
            Log.Warning("Tried writing to undefined sketch page", this);
            return;
        }

        if (_paging.ActivePage.WriteIndex >= CurrentPointList.NumElements - 1)
        {
            //Log.Debug($"Increasing paint buffer length of {CurrentPointList.NumElements} by {BufferIncreaseStep}...", this);
            CurrentPointList.SetLength(CurrentPointList.NumElements + BufferIncreaseStep);
        }

        CurrentPointList.TypedElements[_paging.ActivePage.WriteIndex] = p;

        if (advanceIndex)
            _paging.ActivePage.WriteIndex++;
    }

    /// <summary>
    /// Shows the stroke reaching the cursor before it moved far enough to commit a new point.
    /// The tail sits in the slot that the next committed point overwrites, followed by the separator.
    /// </summary>
    private void UpdateTailPoint(Point lastCommittedPoint, Vector3 posInWorld)
    {
        if (_paging.ActivePage == null || CurrentPointList == null)
            return;

        lastCommittedPoint.Position = posInWorld;
        AppendPoint(lastCommittedPoint, advanceIndex: false);

        // AppendPoint keeps the buffer one element longer than the write index.
        CurrentPointList.TypedElements[_paging.ActivePage.WriteIndex + 1] = Point.Separator();
        _hasTailPoint = true;
    }

    private bool GetPreviousStrokePoint(out Point point)
    {
        if (_paging.ActivePage == null || _currentStrokeLength == 0 || _paging.ActivePage.WriteIndex == 0 || CurrentPointList == null)
        {
            Log.Warning("Can't get previous stroke point", this);
            point = new Point();
            return false;
        }

        point = CurrentPointList.TypedElements[_paging.ActivePage.WriteIndex - 1];
        return true;
    }

    private double _lastModificationTime;
    private StructuredList<Point>? CurrentPointList => _paging.ActivePage?.PointsList;

    private float _brushSize;

    //private bool _needsSave;
    private DrawModes _drawMode = DrawModes.Draw;
    private bool _isMouseDown;

    private int _currentStrokeLength;
    private bool _hasTailPoint;
    private Vector4 _strokeColor = Vector4.One;

    private DrawModes _strokeMode = DrawModes.Draw;
    private SketchEdit? _strokeEdit;
    private bool _strokeErasedPoints;
    private bool _ignoreMouseUntilReleased;

    private TransformModes _transformMode = TransformModes.None;
    private SketchEdit? _transformEdit;
    private Vector3 _transformCenter;
    private Vector3 _transformStartPos;

    private bool _isResizingBrush;
    private Vector2 _brushResizeStartMousePos;
    private float _brushSizeAtResizeStart;
    private Vector3 _brushResizeAnchorPos;
    private const float BrushResizeSpeed = 5f;
    private const float BrushSizeStepFactor = 1.25f;
    private const float MinBrushSize = 0.02f;
    private const float MaxBrushSize = 20f;

    private bool _isPickingColor;
    private SketchEdit? _colorPickerEdit;
    private Vector2 _pickerAnchorMousePos;
    private Vector2 _lastPickerMousePos;
    private Vector2 _pickerStartMousePos;
    private bool _isPickerKeyHeld;
    private const float MinPickerMoveForHoldToApply = 0.01f;
    private Vector4 _pickerBaseColor;
    private Vector4 _pickedColor;
    private float _pickerHue;
    private float _pickerSaturation;
    private float _pickerStartTone;
    private bool _hasReadStrokeColor;
    private Vector4 _lastReadStrokeColor;
    private float _aspectRatio = 1;
    private const float PickerHalfHeightInClipSpace = 0.25f;
    private const int PickerBandCount = 64;
    private const float HueChangePerScreenWidth = 1f;
    private const float PickerFrameLineWidthInClipSpace = 0.004f;
    private const float PickerComparisonBarHeightInClipSpace = 0.06f;
    private static readonly Vector4 PickerFrameColor = new(1, 1, 1, 0.8f);

    private const int MaxOverlayPoints = 2048;
    private readonly StructuredList<Point> _overlayPoints = new(MaxOverlayPoints);
    private readonly StructuredList<Point> _emptyOverlay = new(1);
    private int _overlayCount;
    private int _lastOverlayCount;

    private bool _isFillBrush;
    private const float FillStrokeMarker = -1;
    private const float FillOutlineScale = 0.012f;
    private Point[] _fillBuffer = new Point[3000];
    private readonly StructuredList<Point> _fillPoints = new(3);
    private readonly StructuredList<Point> _emptyFill = new(1);
    private int _fillCount;
    private Page? _filledPage;
    private int _filledSketchRevision = -1;

    private bool _isLassoActive;
    private readonly List<Vector2> _lassoPointsInClipSpace = new(256);
    private readonly List<Vector3> _lassoPointsInWorld = new(256);
    private const float MinLassoPointDistanceInClipSpace = 0.01f;
    private const float LassoLineScale = 0.03f;
    private static readonly Vector4 LassoColor = new(1, 1, 1, 0.6f);

    private readonly bool[] _previousKeyStates = new bool[KeyHandler.PressedKeys.Count];

    /** Shared by all sketch instances, so strokes can be copied between sketches. */
    private static readonly List<Point> Clipboard = new(1000);

    /** Lets undo commands find the live sketch even after the operator was recreated by a reload. */
    private static readonly Dictionary<string, _SketchImpl> SketchesByFilePath = new();

    /** Spacing of committed stroke points relative to the brush size; denser points fold the line geometry on every bend. */
    private const float MinPointDistanceForBrushSize = 0.03f;

    private double _lastUpdateContextTime = -1;

    private bool IsOpSelected => MouseInput.SelectedChildId == Parent?.SymbolChildId;

    /** Keys only reach the sketch while the editor grants it the keyboard, so they never double as editor shortcuts. */
    private bool HasKeyboard => Parent != null && OperatorKeyboardCapture.IsCapturedBy(Parent.SymbolChildId);

    internal sealed class Page
    {
        public int WriteIndex;
        public double Time;

        [JsonConverter(typeof(StructuredListConverter))]
        public StructuredList<Point> PointsList = new();

        public PageContent CaptureContent() => new((StructuredList<Point>)PointsList.TypedClone(), WriteIndex, Time);

        /** Restores in place, because undo snapshots of other edits refer to this page object. */
        public void Restore(PageContent content)
        {
            PointsList = (StructuredList<Point>)content.Points.TypedClone();
            WriteIndex = content.WriteIndex;
            Time = content.Time;
        }

        public Page Clone()
        {
            var structuredList = (StructuredList<Point>)PointsList.TypedClone();
            return new Page
                       {
                           Time = Time,
                           PointsList = structuredList,
                       };
        }
    }

    internal sealed record PageContent(StructuredList<Point> Points, int WriteIndex, double Time);

    /// <summary>
    /// Undo snapshot of the page list and of the one page an edit changes. Resolves the sketch by its file path
    /// when applied, so it never holds an operator instance.
    /// </summary>
    private sealed class SketchEdit
    {
        public SketchEdit(_SketchImpl sketch)
        {
            _sketchFilePath = sketch._absolutePath;
            _pagesBefore = [..sketch._paging.Pages];
            Page = sketch._paging.ActivePage;
            ContentBefore = Page?.CaptureContent();
        }

        public Page? Page { get; private set; }
        public PageContent? ContentBefore { get; }

        /** An edit that starts on an empty frame creates its page, so the page is only known now. */
        public void Complete(_SketchImpl sketch)
        {
            _pagesAfter = [..sketch._paging.Pages];
            Page ??= sketch._paging.ActivePage;
            _contentAfter = Page?.CaptureContent();
        }

        public void Undo() => ApplyToRegisteredSketch(_pagesBefore, ContentBefore);
        public void Redo() => ApplyToRegisteredSketch(_pagesAfter, _contentAfter);
        public void Revert(_SketchImpl sketch) => sketch.ApplyEdit(_pagesBefore, Page, ContentBefore);

        private void ApplyToRegisteredSketch(List<Page> pages, PageContent? content)
        {
            if (!SketchesByFilePath.TryGetValue(_sketchFilePath, out var sketch) || sketch.IsDisposed)
            {
                Log.Warning($"Can't undo sketch edit, because {_sketchFilePath} is no longer open.");
                return;
            }

            sketch.ApplyEdit(pages, Page, content);
        }

        private readonly string _sketchFilePath;
        private readonly List<Page> _pagesBefore;
        private List<Page> _pagesAfter = [];
        private PageContent? _contentAfter;
    }

    /// <summary>
    /// Controls switching between different sketch pages
    /// </summary>
    private sealed class Paging
    {
        public Paging(_SketchImpl sketch)
        {
            _sketch = sketch;
        }
        
        /// <summary>
        /// Derives active page index from local time or parameter override 
        /// </summary>
        public void UpdatePageIndex(double contextLocalTime, int overridePageIndex)
        {
            _lastContextTime = contextLocalTime;

            if (overridePageIndex >= 0 && !_sketch._enableKeyframeSync)
            {
                if (overridePageIndex >= Pages.Count)
                {
                    ActivePage = null;
                    return;
                }

                ActivePageIndex = overridePageIndex;
                ActivePage = Pages[overridePageIndex];
                return;
            }

            for (var pageIndex = 0; pageIndex < Pages.Count; pageIndex++)
            {
                var page = Pages[pageIndex];
                if (!(Math.Abs(page.Time - contextLocalTime) < TimePrecision))
                    continue;

                ActivePageIndex = pageIndex;
                ActivePage = Pages[pageIndex];
                return;
            }

            ActivePageIndex = NoPageIndex;
            ActivePage = null;
        }

        public void InsertNewPage()
        { 
            Pages.Add(new Page
                          {
                              Time = _lastContextTime,
                              PointsList = new StructuredList<Point>(BufferIncreaseStep),
                          });
            Pages = Pages.OrderBy(p => p.Time).ToList();
            UpdatePageIndex(_lastContextTime, NoPageIndex); // This is probably bad
        }

        public void LoadPages(string filepath)
        {
            Pages = [];
            try
            {
                try
                {
                    Pages = JsonUtils.TryLoadingJson<List<Page>>(filepath) ?? [];
                }
                catch (Exception e)
                {
                    Log.Debug("Failed reading sketch pages from json: " + e.Message, this);
                }

                foreach (var page in Pages)
                {
                    if (page.PointsList.NumElements == 0)
                    {
                        page.PointsList = new StructuredList<Point>(BufferIncreaseStep);
                        continue;
                    }

                    if (page.PointsList.NumElements > page.WriteIndex)
                        continue;

                    //Log.Warning($"Adjusting writing index {page.WriteIndex} -> {page.PointsList.NumElements}", this);
                    page.WriteIndex = page.PointsList.NumElements + 1;
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Failed to load pages in {filepath}: {e.Message}", this);
            }
        }

        public bool HasActivePage => ActivePage != null;

        public Page? ActivePage;

        /** With an override page index, an empty page keeps the indices of the following pages stable. */
        public void RemoveActivePage(int overridePageIndex)
        {
            if (ActivePage == null)
                return;

            var activeIndex = Pages.IndexOf(ActivePage);
            Pages.Remove(ActivePage);
            if (overridePageIndex >= 0)
            {
                Pages.Insert(activeIndex, new Page
                                              {
                                                  Time = _lastContextTime,
                                                  PointsList = new StructuredList<Point>(BufferIncreaseStep),
                                              });
            }

            UpdatePageIndex(_lastContextTime, overridePageIndex);
        }

        public void ReplacePages(List<Page> pages)
        {
            Pages = [..pages];
            UpdatePageIndex(_lastContextTime, _sketch._overridePageIndex);
        }

        public int ActivePageIndex { get; private set; } = NoPageIndex;
        private _SketchImpl _sketch;

        public List<Page> Pages = [];
        private double _lastContextTime;

        private const int NoPageIndex = -1;
    }

    private readonly Paging _paging;
    private const int BufferIncreaseStep = 100; // low to reduce page file overhead

    private int _sketchRevision;
    private int _lastSavedSketchVersion;
    private const double TimePrecision = 0.002;
    
    
    private readonly Guid _pathPathInputId = new("2ded8235-157d-486b-a997-87d09d18f998");
    private readonly Guid _strokeColorInputId = new("31f3942e-bac5-407f-ad44-6d09920754d9");
    private readonly Guid _strokeSizeInputId = new("1c5d0d86-c000-449e-903a-3212d19d8e1d");
    private readonly Guid _overrideKeyframeIndexInputId = new("37093302-053a-47b2-ace6-b9d310d3f4b7");

    private readonly int[] _numberKeys =
        { (int)Key.D1, (int)Key.D2, (int)Key.D3, (int)Key.D4, (int)Key.D5, (int)Key.D6, (int)Key.D7, (int)Key.D8, (int)Key.D9 };

    public enum DrawModes
    {
        View,
        Draw,
        Erase,
        Lasso,
    }

    private enum TransformModes
    {
        None,
        Move,
        Scale,
    }

    public enum ColorModes
    {
        Stroke,
        Page,
    }


    [Input(Guid = "C427F009-7E04-4168-82E6-5EBE2640204D")]
    public readonly InputSlot<Vector2> MousePos = new();

    [Input(Guid = "520A2023-7450-4314-9CAC-850D6D692461")]
    public readonly InputSlot<bool> IsMouseButtonDown = new();

    [Input(Guid = "1057313C-006A-4F12-8828-07447337898B")]
    public readonly InputSlot<float> StrokeSize = new();

    [Input(Guid = "AE7FB135-C216-4F34-B73F-5115417E916B")]
    public readonly InputSlot<Vector4> StrokeColor = new();

    [Input(Guid = "37558056-88D8-4D2E-89E2-9F3460565BC8", MappedType = typeof(ColorModes))]
    public readonly InputSlot<int> ColorMode = new();

    [Input(Guid = "51641425-A2C6-4480-AC8F-2E6D2CBC300A")]
    public readonly InputSlot<string> FilePath = new();

    [Input(Guid = "0FA40E27-C7CA-4BB9-88C6-CED917DFEC12")]
    public readonly InputSlot<int> OverridePageIndex = new();

    [Input(Guid = "D156BD8F-F2A7-47F2-BF14-99BE4AAD32D7")]
    public readonly InputSlot<bool> EnableKeyframeSync = new();

    private sealed class KeyframeSync
    {
        public KeyframeSync(_SketchImpl sketch)
        {
            _sketch = sketch;
        }

        public bool UpdateIfEnabled(bool enabled, int frameCount)
        {
            if (!enabled)
                return false;

            if (!TryGetOverrideCurve(out var curve))
                return false;

            var sketchChanged = _sketch._sketchRevision != _lastSeenSketchRevision;
            var curveChanged = _lastAppliedCurveRevision != curve.ChangeCount;

            if (!sketchChanged && !curveChanged)
                return false;
            
            if (curveChanged)
            {
                if (_lastSeenCurveRevision != curve.ChangeCount)
                {
                    _frameCountSinceLastCurveChange = 0;
                    _lastSeenCurveRevision = curve.ChangeCount;
                    return false;
                }

                // Wait for some 
                if (_frameCountSinceLastCurveChange++ < 20)
                {
                    return false;
                }
                
                ApplyCurveToPages(curve, _sketch._paging.Pages);
                NormalizeKeyValuesByTime(curve);
                
                _sketch._sketchRevision++;
                _lastSeenSketchRevision = _sketch._sketchRevision;
                _lastAppliedCurveRevision = curve.ChangeCount;
                return true;
            }

            if (sketchChanged)
            {
                ApplyPagesToCurve(_sketch._paging.Pages, curve);
                _lastSeenSketchRevision = _sketch._sketchRevision;
                _lastAppliedCurveRevision = curve.ChangeCount;
            }

            return false;
        }

        private bool TryGetOverrideCurve([NotNullWhen(true)] out Curve? curve)
        {
            curve = null;

            var composition = _sketch.Parent?.Parent;
            if (composition == null)
                return false;

            if (!composition.Symbol.Animator.TryGetCurvesForChildInput(_sketch.Parent!.SymbolChildId,
                                                                       _sketch._overrideKeyframeIndexInputId,
                                                                       out var curves))
                return false;

            if (curves.Length != 1)
                return false;

            curve = curves[0];
            return true;
        }

        private static Page CreateBlankPage(double time)
        {
            return new Page
                       {
                           Time = time,
                           PointsList = new StructuredList<Point>(BufferIncreaseStep),
                           WriteIndex = 0,
                       };
        }

        private static int FindNearestUnusedByTime(IReadOnlyList<double> times, bool[] used, double t, double eps)
        {
            var bestIndex = -1;
            var bestDist = double.MaxValue;

            for (var i = 0; i < times.Count; i++)
            {
                if (used[i])
                    continue;

                var dist = Math.Abs(times[i] - t);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0 && bestDist <= eps)
                return bestIndex;

            return -1;
        }

        private static void NormalizeKeyValuesByTime(Curve curve)
        {
            var keyframes = curve.GetVDefinitions();
            for (var i = 0; i < keyframes.Count; i++)
            {
                var t = keyframes[i].U;
                curve.AddOrUpdateV(t,
                                   new VDefinition
                                       {
                                           U = t,
                                           Value = i,
                                           InInterpolation = VDefinition.KeyInterpolation.Constant,
                                           OutInterpolation = VDefinition.KeyInterpolation.Constant,
                                       });
            }
        }

        private static VDefinition MakeConstantIndexV(double t, int index)
        {
            return new VDefinition
                       {
                           U = t,
                           Value = index,
                           InInterpolation = VDefinition.KeyInterpolation.Constant,
                           OutInterpolation = VDefinition.KeyInterpolation.Constant,
                       };
        }

        private static void SafeMoveOrRewriteKey(Curve curve, double oldTime, double newTime, int newIndex)
        {
            var oldExists = !double.IsNaN(oldTime) && curve.HasVAt(oldTime);
            var newExists = curve.HasVAt(newTime);

            var needsMove = oldExists && Math.Abs(newTime - oldTime) > TimePrecision;

            if (needsMove && !newExists)
            {
                curve.MoveKey(oldTime, newTime);
                curve.AddOrUpdateV(newTime, MakeConstantIndexV(newTime, newIndex));
                return;
            }

            if (needsMove && newExists)
            {
                curve.RemoveKeyframeAt(oldTime);
            }

            curve.AddOrUpdateV(newTime, MakeConstantIndexV(newTime, newIndex));
        }

        // Replace ApplyCurveToPages(...) with this version
        private void ApplyCurveToPages(Curve curve, List<Page> pages)
        {
            var keys = curve.GetVDefinitions();
            if (keys.Count == 0)
            {
                pages.Clear();
                return;
            }

            // Current pages represent "old indices" (their current list order).
            // We still keep a time-sorted view for the fallback matching.
            var pagesByIndex = pages; // old index == current list index
            var pagesByTime = pages.OrderBy(p => p.Time).ToList();
            var pageTimes = pagesByTime.Select(p => p.Time).ToList();

            var usedByIndex = new bool[pagesByIndex.Count];
            var usedByTime = new bool[pagesByTime.Count];

            var newPages = new List<Page>(keys.Count);

            for (var keyIndex = 0; keyIndex < keys.Count; keyIndex++)
            {
                var key = keys[keyIndex];
                Page? chosen = null;

                // 1) Prefer value-based reuse (page indices encoded in key.Value)
                var oldIndex = (int)Math.Round(key.Value);
                if (oldIndex >= 0 && oldIndex < pagesByIndex.Count && !usedByIndex[oldIndex])
                {
                    usedByIndex[oldIndex] = true;
                    chosen = pagesByIndex[oldIndex];
                }

                // 2) Fallback: reuse page by (approx) time match
                if (chosen == null)
                {
                    var match = FindNearestUnusedByTime(pageTimes, usedByTime, key.U, TimePrecision);
                    if (match >= 0)
                    {
                        usedByTime[match] = true;
                        chosen = pagesByTime[match];
                    }
                }

                // 3) Otherwise: new blank page
                chosen ??= CreateBlankPage(key.U);
                chosen.Time = key.U;
                newPages.Add(chosen);
                _sketch._paging.ActivePage = chosen;
            }

            pages.Clear();
            pages.AddRange(newPages);
        }

        private void ApplyPagesToCurve(List<Page> pages, Curve curve)
        {
            var pagesByTime = pages.OrderBy(p => p.Time).ToList();
            var keys = curve.GetVDefinitions();

            var usedKeys = new bool[keys.Count];
            var keyTimes = keys.Select(k => k.U).ToList();

            var movesOrUpdates = new List<(double OldTime, double NewTime, int NewIndex)>(pagesByTime.Count);
            var adds = new List<(double Time, int NewIndex)>();
            var removes = new List<double>();

            for (var pageIndex = 0; pageIndex < pagesByTime.Count; pageIndex++)
            {
                var page = pagesByTime[pageIndex];

                var keyIndex = FindNearestUnusedByTime(keyTimes, usedKeys, page.Time, TimePrecision);
                if (keyIndex >= 0)
                {
                    usedKeys[keyIndex] = true;
                    movesOrUpdates.Add((keys[keyIndex].U, page.Time, pageIndex));
                }
                else
                {
                    adds.Add((page.Time, pageIndex));
                }
            }

            for (var keyIndex = 0; keyIndex < keys.Count; keyIndex++)
            {
                if (!usedKeys[keyIndex])
                    removes.Add(keys[keyIndex].U);
            }

            for (var i = 0; i < removes.Count; i++)
            {
                var t = removes[i];
                curve.RemoveKeyframeAt(t);
            }

            for (var i = 0; i < movesOrUpdates.Count; i++)
            {
                var (oldT, newT, newIndex) = movesOrUpdates[i];
                SafeMoveOrRewriteKey(curve, oldT, newT, newIndex);
            }

            for (var i = 0; i < adds.Count; i++)
            {
                var (t, newIndex) = adds[i];
                curve.AddOrUpdateV(t, MakeConstantIndexV(t, newIndex));
            }

            NormalizeKeyValuesByTime(curve);
        }

        private readonly _SketchImpl _sketch;

        private int _lastAppliedCurveRevision = -1;
        private int _lastSeenCurveRevision = -1;
        private int _frameCountSinceLastCurveChange = 0;
        private int _lastSeenSketchRevision = -1;
    }

    private readonly KeyframeSync _keyframeSync;
}