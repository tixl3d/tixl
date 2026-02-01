#nullable enable
using Newtonsoft.Json;
using T3.Core.Animation;
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
    }

    private string GetAbsolutePath(string relativePath)
    {
        var sketchInstance = Parent;
        var compositionWithSketchOp = sketchInstance?.Parent; 
        
        if (sketchInstance == null || compositionWithSketchOp == null)
            return relativePath;
        
        return Path.Combine(compositionWithSketchOp.Symbol.SymbolPackage.AssetsFolder, relativePath.Replace("{id}", sketchInstance.SymbolChildId.ShortenGuid()));
    }

    private string _absolutePath = string.Empty;
    private int _overridePageIndex;

    private void Update(EvaluationContext context)
    {
        var isFilePathDirty = FilePath.DirtyFlag.IsDirty;

        var overrideIndexWasDirty = OverridePageIndex.DirtyFlag.IsDirty;
        _overridePageIndex = OverridePageIndex.GetValue(context);

        if (this.Parent == null)
        {
            Log.Warning("Implementation needs a wrapper op", this);
            return;
        }
        
        if (isFilePathDirty)
        {
            var filepath = FilePath.GetValue(context) ?? string.Empty;
            _absolutePath = GetAbsolutePath(filepath);
            //Log.Debug($"Absolute path: {_absolutePath}", this);
            _paging.LoadPages(_absolutePath);
        }

        var pageIndexNeedsUpdate = Math.Abs(_lastUpdateContextTime - context.LocalTime) > 0.001;
        if (pageIndexNeedsUpdate || isFilePathDirty || overrideIndexWasDirty)
        {
            _paging.UpdatePageIndex(context.LocalTime, _overridePageIndex);
            _lastUpdateContextTime = context.LocalTime;
        }

        // Switch Brush size
        {
            if (BrushSize.DirtyFlag.IsDirty)
            {
                _brushSize = BrushSize.GetValue(context);
            }

            for (var index = 0; index < _numberKeys.Length; index++)
            {
                if (!KeyHandler.PressedKeys[_numberKeys[index]])
                    continue;

                _brushSize = (index * index + 0.5f) * 0.1f;
            }
        }

        // Switch modes
        if(IsOpSelected && !KeyHandler.PressedKeys[(int)Key.CtrlKey]) {
            // if (Mode.DirtyFlag.IsDirty)
            // {
            //     _drawMode = (DrawModes)Mode.GetValue(context).Clamp(0, Enum.GetNames(typeof(DrawModes)).Length - 1);
            // }
            //
            if (KeyHandler.PressedKeys[(int)Key.P])
            {
                _drawMode = DrawModes.Draw;
                ClearSelection();
            }
            else if (KeyHandler.PressedKeys[(int)Key.E])
            {
                EraseSelection();
                _drawMode = DrawModes.Erase;
            }
            else if (KeyHandler.PressedKeys[(int)Key.X])
            {
                _paging.Cut(_overridePageIndex);
            }
            else if (KeyHandler.PressedKeys[(int)Key.V])
            {
                _paging.Paste(context.LocalTime, _overridePageIndex);
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

        var wasModified = DoSketch(context, out CursorPosInWorld.Value, out CurrentBrushSize.Value);

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
            _needsSave = true;
        }

        if (_needsSave && Playback.RunTimeInSecs - _lastModificationTime > 2)
        {
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
            _needsSave = false;
        }
    }

    private void ClearSelection()
    {
        if (_paging.ActivePage == null  || CurrentPointList==null)
            return;

        for (var index = 0; index < CurrentPointList.TypedElements.Length; index++)
        {
            CurrentPointList.TypedElements[index].F2 = 0;
        }
    }
    
    private void EraseSelection()
    {
        if (_paging.ActivePage == null  || CurrentPointList==null)
            return;

        for (var index = 0; index < CurrentPointList.TypedElements.Length; index++)
        {
            var selection =CurrentPointList.TypedElements[index].F2;
            if (selection > 0.9f)
            {
                CurrentPointList.TypedElements[index].Scale = Vector3.One * float.NaN;
            }
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
                    _paging.InsertNewPage();

                if (justPressed && KeyHandler.PressedKeys[(int)Key.ShiftKey] && _paging.ActivePage!.WriteIndex > 1)
                {
                    // Discard last separator point
                    _paging.ActivePage.WriteIndex--;
                    _currentStrokeLength = 1;
                }

                var color = BrushColor.GetValue(context);

                AppendPoint(new Point
                                {
                                    Position = posInWorld,
                                    Color = color,
                                    Scale = Vector3.One * (visibleBrushSize / 2 + 0.002f ),
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
                        CurrentPointList.TypedElements[index].Scale = Vector3.One* float.NaN;
                    }
                    else if (_drawMode == DrawModes.Select)
                    {
                        CurrentPointList.TypedElements[index].F2 = 1;
                    }
                    wasModified = true;
                }

                return wasModified;
            }
            
            // {
            //     if (_paging.ActivePage == null || CurrentPointList == null)
            //         return false;
            //
            //     var wasModified = false;
            //     for (var index = 0; index < CurrentPointList.NumElements; index++)
            //     {
            //         var distanceToPoint = Vector3.Distance(posInWorld, CurrentPointList.TypedElements[index].Position);
            //         if (!(distanceToPoint < visibleBrushSize * 0.02f))
            //             continue;
            //
            //         CurrentPointList.TypedElements[index].Scale = Vector3.One* float.NaN;
            //         //CurrentPointList.TypedElements[index].F2 = 0.8f;
            //         wasModified = true;
            //     }
            //
            //     return wasModified;
            // }
        }

        return false;
    }

    private static Vector3 CalcPosInWorld(EvaluationContext context, Vector2 mousePos)
    {
        const float offsetFromCamPlane = 0.99f;
        var posInClipSpace = new System.Numerics.Vector4((mousePos.X - 0.5f) * 2, (-mousePos.Y + 0.5f) * 2, offsetFromCamPlane, 1);
        Matrix4x4.Invert(context.CameraToClipSpace, out var clipSpaceToCamera);
        Matrix4x4.Invert(context.WorldToCamera, out var cameraToWorld);
        //Matrix4x4.Invert(context.ObjectToWorld, out var worldToObject);

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
        if (_paging.ActivePage == null || _currentStrokeLength == 0 || _paging.ActivePage.WriteIndex == 0 || CurrentPointList==null)
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
    private bool _needsSave;
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
        public StructuredList<Point> PointsList= new();
    }

    /// <summary>
    /// Controls switching between different sketch pages
    /// </summary>
    private sealed class Paging
    {
        /// <summary>
        /// Derives active page index from local time or parameter override 
        /// </summary>
        public void UpdatePageIndex(double contextLocalTime, int overridePageIndex)
        {
            _lastContextTime = contextLocalTime;
            

            if (overridePageIndex >= 0)
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
                if (!(Math.Abs(page.Time - contextLocalTime) < 0.05))
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
                catch ( Exception e)
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
            catch(Exception e)
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
            
            //if (overridePageIndex < 0)
            //{
            //
            //}
            // else
            // {
            //     var index = Pages.IndexOf(ActivePage);
            //     if (index != -1)
            //     {
            //         Pages[index] = null;
            //     }
            // }
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

        public List<Page> Pages = [];
        private Page? _cutPage;
        private double _lastContextTime;

        private const int NoPageIndex = -1;
    }

    private readonly Paging _paging = new();

    private const int BufferIncreaseStep = 100; // low to reduce page file overhead

    private readonly int[] _numberKeys =
        { (int)Key.D1, (int)Key.D2, (int)Key.D3, (int)Key.D4, (int)Key.D5, (int)Key.D6, (int)Key.D7, (int)Key.D8, (int)Key.D9 };

    public enum DrawModes
    {
        View,
        Draw,
        Erase,
        Select,
    }

    [Input(Guid = "C427F009-7E04-4168-82E6-5EBE2640204D")]
    public readonly InputSlot<Vector2> MousePos = new();

    [Input(Guid = "520A2023-7450-4314-9CAC-850D6D692461")]
    public readonly InputSlot<bool> IsMouseButtonDown = new();

    [Input(Guid = "1057313C-006A-4F12-8828-07447337898B")]
    public readonly InputSlot<float> BrushSize = new();

    [Input(Guid = "AE7FB135-C216-4F34-B73F-5115417E916B")]
    public readonly InputSlot<Vector4> BrushColor = new();

    [Input(Guid = "51641425-A2C6-4480-AC8F-2E6D2CBC300A")]
    public readonly InputSlot<string> FilePath = new();

    [Input(Guid = "0FA40E27-C7CA-4BB9-88C6-CED917DFEC12")]
    public readonly InputSlot<int> OverridePageIndex = new();
}