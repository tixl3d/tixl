#nullable enable
using Newtonsoft.Json;
using T3.Core.Animation;
using T3.Core.Resource.Assets;
using T3.Core.Utils;
using T3.Serialization;
using T3.SystemUi;

// ReSharper disable RedundantNameQualifier
// ReSharper disable once InconsistentNaming

namespace Lib.point._experimental;

[Guid("b238b288-6e9b-4b91-bac9-3d7566416028")]
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

    public _SketchImpl()
    {
        OutPages.UpdateAction += Update;
        CursorPosInWorld.UpdateAction += Update;
        StatusMessage.UpdateAction += Update;

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
            _absolutePath = GetAbsolutePath(filepath);
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

        // Switch Brush size
        {
            if (StrokeSize.DirtyFlag.IsDirty)
            {
                _brushSize = StrokeSize.GetValue(context);
            }

            for (var index = 0; index < _numberKeys.Length; index++)
            {
                if (!KeyHandler.PressedKeys[_numberKeys[index]])
                    continue;

                _brushSize = (index * index + 0.5f) * 0.1f;
            }
        }

        // Switch colors
        var colorMode = ColorMode.GetEnumValue<ColorModes>(context);
        var isColorDirty = StrokeColor.IsDirty;
        var color = StrokeColor.GetValue(context);
        var colorNeedsUpdate = colorMode != _colorMode || isColorDirty;
        if (colorNeedsUpdate)
        {
            _colorMode = colorMode;
            if (colorMode == ColorModes.Page)
            {
                ColorizePage(color);
            }
        }

        // Switch modes
        if (IsOpSelected && !KeyHandler.PressedKeys[(int)Key.CtrlKey])
        {
            if (KeyHandler.PressedKeys[(int)Key.P])
            {
                _drawMode = DrawModes.Draw;
                ClearSelection();
                _sketchRevision++;
            }
            else if (KeyHandler.PressedKeys[(int)Key.E])
            {
                EraseSelection();
                _drawMode = DrawModes.Erase;
            }
            else if (KeyHandler.PressedKeys[(int)Key.X])
            {
                _paging.Cut(_overridePageIndex);
                _sketchRevision++;
            }
            else if (KeyHandler.PressedKeys[(int)Key.V])
            {
                _paging.Paste(context.LocalTime, _overridePageIndex);
                _sketchRevision++;
            }
            else if (KeyHandler.PressedKeys[(int)Key.C])
            {
                ClearSelection();
            }
            else if (KeyHandler.PressedKeys[(int)Key.S])
            {
                _drawMode = DrawModes.Select;
            }
        }

        wasModified |= DoSketch(context, out CursorPosInWorld.Value, out CurrentBrushSize.Value);

        OutPages.Value = _paging.Pages;
        ActivePageIndexOutput.Value = _paging.ActivePageIndex;
        var pageTitle = _paging.HasActivePage ? $"PAGE{_paging.ActivePageIndex}" : "EMPTY PAGE";
        var tool = !IsOpSelected
                       ? "Not selected"
                       : _drawMode == DrawModes.Draw
                           ? "PEN"
                           : "ERASE";

        var cutSomething = _paging.HasCutPage ? "/ PASTE WITH V" : "";
        StatusMessage.Value = $"{pageTitle}: {tool} {cutSomething}";

        if (wasModified)
        {
            _lastModificationTime = Playback.RunTimeInSecs;
            _sketchRevision++;
        }

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

    private void EraseSelection()
    {
        if (_paging.ActivePage == null || CurrentPointList == null)
            return;

        for (var index = 0; index < CurrentPointList.TypedElements.Length; index++)
        {
            var selection = CurrentPointList.TypedElements[index].F2;
            if (selection > 0.9f)
            {
                CurrentPointList.TypedElements[index].Scale = Vector3.One * float.NaN;
            }
        }

        _sketchRevision++;
    }

    private void ColorizePage(Vector4 fillColor)
    {
        if (_paging.ActivePage == null || CurrentPointList == null)
            return;

        //Log.Debug("Colorize page " + fillColor, this);

        for (var index = 0; index < CurrentPointList.TypedElements.Length; index++)
        {
            CurrentPointList.TypedElements[index].Color = fillColor;
            _sketchRevision++;
        }
    }

    private bool DoSketch(EvaluationContext context, out Vector3 posInWorld, out float visibleBrushSize)
    {
        visibleBrushSize = _brushSize;
        if (_drawMode == DrawModes.Erase)
            visibleBrushSize *= 4;

        posInWorld = CalcPosInWorld(context, MousePos.GetValue(context));

        if (_drawMode == DrawModes.View || !IsOpSelected)
        {
            _isMouseDown = false;
            _currentStrokeLength = 0;
            return false;
        }

        var isMouseDown = IsMouseButtonDown.GetValue(context);
        var justReleased = !isMouseDown && _isMouseDown;
        var justPressed = isMouseDown && !_isMouseDown;
        _isMouseDown = isMouseDown;

        if (justReleased)
        {
            if (_drawMode != DrawModes.Draw || !_paging.HasActivePage)
                return false;

            // Add to points for single click to make it visible as a dot
            var wasClick = _currentStrokeLength == 1;
            if (wasClick)
            {
                if (!GetPreviousStrokePoint(out var clickPoint))
                {
                    return false;
                }

                clickPoint.Position += Vector3.UnitY * 0.02f * 2 * visibleBrushSize;
                AppendPoint(clickPoint);
            }

            AppendPoint(Point.Separator());
            _currentStrokeLength = 0;
            return true;
        }

        if (!_isMouseDown)
            return false;

        if (_currentStrokeLength > 0 && GetPreviousStrokePoint(out var lastPoint))
        {
            var distance = Vector3.Distance(lastPoint.Position, posInWorld);
            var minDistanceForBrushSize = 0.01f;

            var updateLastPoint = distance < visibleBrushSize * minDistanceForBrushSize;
            if (updateLastPoint)
            {
                // Sadly, adding intermedia points causes too many artifacts
                // lastPoint.Position = posInWorld;
                // AppendPoint(lastPoint, advanceIndex: false);
                return false;
            }
        }

        switch (_drawMode)
        {
            case DrawModes.Draw:
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

                var color = StrokeColor.GetValue(context);

                AppendPoint(new Point
                                {
                                    Position = posInWorld,
                                    Color = color,
                                    Scale = Vector3.One * (visibleBrushSize / 2 + 0.002f),
                                    F2 = 0, // Not selected by default
                                });
                AppendPoint(Point.Separator(), advanceIndex: false);
                _currentStrokeLength++;
                return true;

            case DrawModes.Erase:
            case DrawModes.Select:
            {
                if (_paging.ActivePage == null || CurrentPointList == null)
                    return false;

                var wasModified = false;
                for (var index = 0; index < CurrentPointList.NumElements; index++)
                {
                    var distanceToPoint = Vector3.Distance(posInWorld, CurrentPointList.TypedElements[index].Position);
                    if (!(distanceToPoint < visibleBrushSize * 0.02f))
                        continue;

                    if (_drawMode == DrawModes.Erase)
                    {
                        CurrentPointList.TypedElements[index].Scale = Vector3.One * float.NaN;
                    }
                    else if (_drawMode == DrawModes.Select)
                    {
                        CurrentPointList.TypedElements[index].F2 = 1;
                    }

                    wasModified = true;
                }

                return wasModified;
            }
        }

        return false;
    }

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

    private double _lastUpdateContextTime = -1;

    private bool IsOpSelected => MouseInput.SelectedChildId == Parent?.SymbolChildId;

    internal sealed class Page
    {
        public int WriteIndex;
        public double Time;

        [JsonConverter(typeof(StructuredListConverter))]
        public StructuredList<Point> PointsList = new();

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

        public bool HasCutPage => _cutPage != null;

        public void Cut(int overridePageIndex)
        {
            if (ActivePage == null)
                return;

            _cutPage = ActivePage;
            var activeIndex = Pages.IndexOf(ActivePage);

            Pages.Remove(ActivePage);
            if (overridePageIndex >= 0)
            {
                if (activeIndex != overridePageIndex)
                {
                    Log.Warning($"Expected active page index to be {overridePageIndex} not {activeIndex}", this);
                }

                Pages.Insert(activeIndex, new Page
                                              {
                                                  Time = _lastContextTime,
                                                  PointsList = new StructuredList<Point>(BufferIncreaseStep),
                                              });
            }

            UpdatePageIndex(_lastContextTime, overridePageIndex);
        }

        public void Paste(double time, int overridePageIndex)
        {
            if (_cutPage == null || ActivePage == null)
                return;

            if (HasActivePage)
                Pages.Remove(ActivePage);

            _cutPage.Time = time;
            Pages.Add(_cutPage);
            UpdatePageIndex(_lastContextTime, overridePageIndex);
        }

        public int ActivePageIndex { get; private set; } = NoPageIndex;
        private _SketchImpl _sketch;

        public List<Page> Pages = [];
        private Page? _cutPage;
        private double _lastContextTime;

        private const int NoPageIndex = -1;
    }

    private readonly Paging _paging;
    private const int BufferIncreaseStep = 100; // low to reduce page file overhead

    private int _sketchRevision;
    private int _lastSavedSketchVersion;
    private const double TimePrecision = 0.002;
    
    
    private readonly Guid _pathPathInputId = new("2ded8235-157d-486b-a997-87d09d18f998");
    private readonly Guid _overrideKeyframeIndexInputId = new("37093302-053a-47b2-ace6-b9d310d3f4b7");

    private readonly int[] _numberKeys =
        { (int)Key.D1, (int)Key.D2, (int)Key.D3, (int)Key.D4, (int)Key.D5, (int)Key.D6, (int)Key.D7, (int)Key.D8, (int)Key.D9 };

    public enum DrawModes
    {
        View,
        Draw,
        Erase,
        Select,
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