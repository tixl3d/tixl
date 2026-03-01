#nullable enable
using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.UiHelpers.DelaunayVoronoi;
using T3.Editor.Gui.UiHelpers.Thumbnails;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Selection;
using Point = T3.Editor.Gui.UiHelpers.DelaunayVoronoi.Point;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.Variations;

/// <summary>
/// Controls arranging and blending variations on a canvas for Presets and Snapshots.
/// </summary>
internal abstract class VariationBaseCanvas : ScalableCanvas, ISelectionContainer
{
    protected abstract string GetTitle();

    private protected abstract Instance? InstanceForBlendOperations { get; }
    private protected abstract SymbolVariationPool? PoolForBlendOperations { get; }
    protected abstract void DrawAdditionalContextMenuContent(Instance instanceForBlendOperations);

    
    public void DrawBaseCanvas(ImDrawListPtr drawList, bool hideHeader = false)
    {
        if (PoolForBlendOperations == null || InstanceForBlendOperations == null)
            return;

        UpdateCanvas(out _);

        // Complete deferred actions
        if (!T3Ui.IsCurrentlySaving && UserActions.DeleteSelection.Triggered())
            DeleteSelectedElements();

        bool pinnedOutputChanged = false;

        // Render variations to pinned output
        
        
        
        if (RenderProcess.OutputWindow != null)
        {
            var instanceForOutput = RenderProcess.OutputWindow?.ShownInstance;
            var instanceForBlending = InstanceForBlendOperations;

            if (RenderProcess.State == RenderProcess.States.ReadyForExport)
            {
                if (instanceForOutput is { Outputs.Count: > 0 }  )
                {
                    var primaryOutput = instanceForOutput?.Outputs[0];
                    if (primaryOutput is Slot<Texture2D> textureSlot2)
                    {
                        UpdateThumbnailRendering(instanceForBlending, textureSlot2);
                    }
                }
            }

            if (instanceForBlending != _currentRenderInstance)
            {
                pinnedOutputChanged = true;
                _currentRenderInstance = instanceForBlending;
            }
        }

        // Get instance for variations
        if (pinnedOutputChanged)
        {
            RefreshView();
        }

        if (UserActions.FocusSelection.Triggered() || _resetViewRequested)
        {
            ResetView();
        }

        HandleFenceSelection(_selectionFence);

        // Blending...
        HandleBlendingInteraction();

        if (!hideHeader)
        {
            ImGui.PushFont(Fonts.FontLarge);
            ImGui.SetCursorPos(new Vector2(10, 35));
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Gray.Rgba);
            ImGui.TextUnformatted(GetTitle());
            ImGui.PopStyleColor();
            ImGui.PopFont();
        }

        // Draw thumbnails...
        var modified = false;
        if (_currentRenderInstance != null)
        {
            for (var index = 0; index < PoolForBlendOperations.AllVariations.Count; index++)
            {
                var variation = PoolForBlendOperations.AllVariations[index];

                var thumbnail = ThumbnailManager.GetThumbnail(variation.Id, _currentRenderInstance.Symbol.SymbolPackage, ThumbnailManager.Categories.PackageMeta);
                modified |= VariationThumbnail.Draw(this,
                                                    variation,
                                                    InstanceForBlendOperations,
                                                    drawList,
                                                    ThumbnailManager.AtlasSrv, thumbnail);
            }
        }

        DrawBlendingOverlay(drawList, InstanceForBlendOperations);

        if (modified)
            PoolForBlendOperations.SaveVariationsToFile();

        DrawContextMenu(InstanceForBlendOperations);
    }

    private bool _rerenderRequested;
    private bool _rerenderToFileRequested;

    /// <summary>
    /// Updates keeps rendering thumbnails until all are processed.
    /// </summary>
    private void UpdateThumbnailRendering(Instance instanceForBlending, Slot<Texture2D> textureOutputSlot)
    {
        if (!UserSettings.Config.VariationLiveThumbnails && !_rerenderRequested)
            return;

        var outputSymbolUi = textureOutputSlot.Parent.Symbol.GetSymbolUi();
        if (!outputSymbolUi.OutputUis.TryGetValue(textureOutputSlot.Id, out var textureOutputUi))
            return;

        UpdateNextVariationThumbnail(instanceForBlending, textureOutputUi, textureOutputSlot);
    }

    private void DrawBlendingOverlay(ImDrawListPtr drawList, Instance instanceForBlending)
    {
        if (!IsBlendingActive || PoolForBlendOperations == null)
            return;

        var mousePos = ImGui.GetMousePos();
        if (_blendPoints.Count == 1)
        {
            PoolForBlendOperations.BeginWeightedBlend(instanceForBlending, _blendVariations, _blendWeights);

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                PoolForBlendOperations.ApplyCurrentBlend();
            }
        }
        else if (_blendPoints.Count == 2)
        {
            foreach (var p in _blendPoints)
            {
                drawList.AddCircleFilled(p, 5, UiColors.BackgroundFull.Fade(0.5f));
                drawList.AddCircleFilled(p, 3, UiColors.ForegroundFull);
            }

            drawList.AddLine(_blendPoints[0], _blendPoints[1], UiColors.ForegroundFull, 2);
            var blendPosition = _blendPoints[0] * _blendWeights[0] + _blendPoints[1] * _blendWeights[1];

            drawList.AddCircleFilled(blendPosition, 5, UiColors.ForegroundFull);

            PoolForBlendOperations.BeginWeightedBlend(instanceForBlending, _blendVariations, _blendWeights);

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                PoolForBlendOperations.ApplyCurrentBlend();
            }
        }
        else if (_blendPoints.Count == 3)
        {
            drawList.AddTriangleFilled(_blendPoints[0], _blendPoints[1], _blendPoints[2], UiColors.BackgroundFull.Fade(0.3f));
            foreach (var p in _blendPoints)
            {
                drawList.AddCircleFilled(p, 5, UiColors.BackgroundFull.Fade(0.5f));
                drawList.AddLine(mousePos, p, UiColors.ForegroundFull, 2);
                drawList.AddCircleFilled(p, 3, UiColors.ForegroundFull);
            }

            drawList.AddCircleFilled(mousePos, 5, UiColors.ForegroundFull);
            PoolForBlendOperations.BeginWeightedBlend(instanceForBlending, _blendVariations, _blendWeights);

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                PoolForBlendOperations.ApplyCurrentBlend();
            }
        }
    }

    /// <summary>
    /// This will trigger a reset few on next update.
    /// </summary>
    /// <remark>
    /// Calling ResetView within the current update might lead to invalid view configuration, because the
    /// use FillWindow mode might use incorrect window references for determining the current window size and position.
    /// </remark>
    protected void RequestResetView()
    {
        _resetViewRequested = true;
    }

    private bool _resetViewRequested;

    private void HandleBlendingInteraction()
    {
        IsBlendingActive = (ImGui.IsWindowHovered() || ImGui.IsWindowFocused()) && ImGui.GetIO().KeyAlt;

        var mousePos = ImGui.GetMousePos();
        _blendPoints.Clear();
        _blendWeights.Clear();
        _blendVariations.Clear();

        if (!IsBlendingActive || PoolForBlendOperations == null)
            return;

        foreach (var s in CanvasElementSelection.SelectedElements)
        {
            _blendPoints.Add(GetNodeCenterOnScreen(s));
            if (s is Variation v)
                _blendVariations.Add(v);
        }

        if (CanvasElementSelection.SelectedElements.Count == 1)
        {
            var posOnScreen = TransformPosition(_blendVariations[0].PosOnCanvas);
            var sizeOnScreen = TransformDirection(_blendVariations[0].Size);
            var a = (mousePos.X - posOnScreen.X) / sizeOnScreen.X;

            _blendWeights.Add(a);
        }
        else if (CanvasElementSelection.SelectedElements.Count == 2)
        {
            if (_blendPoints[0] == _blendPoints[1])
            {
                _blendWeights.Add(0.5f);
                _blendWeights.Add(0.5f);
            }
            else
            {
                var v1 = _blendPoints[1] - _blendPoints[0];
                var v2 = mousePos - _blendPoints[0];
                var lengthV1 = v1.Length();

                var a = Vector2.Dot(v1 / lengthV1, v2 / lengthV1);
                _blendWeights.Add(1 - a);
                _blendWeights.Add(a);
            }
        }
        else if (CanvasElementSelection.SelectedElements.Count == 3)
        {
            Barycentric(mousePos, _blendPoints[0], _blendPoints[1], _blendPoints[2], out var u, out var v, out var w);
            _blendWeights.Add(u);
            _blendWeights.Add(v);
            _blendWeights.Add(w);
        }
        else
        {
            var points = new List<Point>();

            Vector2 minPos = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maxPos = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            foreach (var v in PoolForBlendOperations.AllVariations)
            {
                var vec2 = GetNodeCenterOnScreen(v);
                minPos = Vector2.Min(vec2, minPos);
                maxPos = Vector2.Max(vec2, maxPos);
                points.Add(new Point(vec2.X, vec2.Y));
            }

            minPos -= Vector2.One * 100;
            maxPos += Vector2.One * 100;

            var triangulator = new DelaunayTriangulator();
            var borderPoints = triangulator.SetBorder(new Point(minPos.X, minPos.Y), new Point(maxPos.X, maxPos.Y));
            points.AddRange(borderPoints);

            var triangles = triangulator.BowyerWatson(points);

            foreach (var t in triangles)
            {
                var p0 = t.Vertices[0].ToVec2();
                var p1 = t.Vertices[1].ToVec2();
                var p2 = t.Vertices[2].ToVec2();
                Barycentric(mousePos,
                            p0,
                            p1,
                            p2,
                            out var u,
                            out var v,
                            out var w);

                var insideTriangle = u >= 0 && u <= 1 && v >= 0 && v <= 1 && w >= 0 && w <= 1;
                if (!insideTriangle)
                    continue;

                _blendPoints.Clear();
                _blendWeights.Clear();
                _blendVariations.Clear();

                var weights = new[] { u, v, w };

                for (var vertexIndex = 0; vertexIndex < t.Vertices.Length; vertexIndex++)
                {
                    var vertex = t.Vertices[vertexIndex];
                    var variationIndex = points.IndexOf(vertex);
                    if (variationIndex < PoolForBlendOperations.AllVariations.Count)
                    {
                        _blendVariations.Add(PoolForBlendOperations.AllVariations[variationIndex]);
                        _blendWeights.Add(weights[vertexIndex]);
                        _blendPoints.Add(vertex.ToVec2());
                    }
                }

                if (_blendWeights.Count == 2)
                {
                    var sum = _blendWeights[0] + _blendWeights[1];
                    _blendWeights[0] /= sum;
                    _blendWeights[1] /= sum;
                }
                else if (_blendWeights.Count == 1)
                {
                    _blendWeights.Clear();
                    _blendPoints.Clear();
                    _blendVariations.Clear();
                }

                break;
            }
        }
    }

    public bool TryGetBlendWeight(Variation v, out float weight)
    {
        weight = 0;
        if (_blendWeights.Count == 0)
            return false;

        var index = _blendVariations.IndexOf(v);
        if (index == -1)
        {
            return false;
        }

        weight = _blendWeights[index];
        return true;
    }

    private Vector2 GetNodeCenterOnScreen(ISelectableCanvasObject node)
    {
        var min = TransformPosition(node.PosOnCanvas);
        var max = TransformPosition(node.PosOnCanvas + node.Size);
        return (min + max) * 0.5f;
    }

    private void DrawContextMenu(Instance instance)
    {
        if (FrameStats.Current.OpenedPopUpName == string.Empty)
        {
            CustomComponents.DrawContextMenuForScrollCanvas(() =>
                                                            {
                                                                var oneOrMoreSelected = CanvasElementSelection.SelectedElements.Count > 0;
                                                                var oneSelected = CanvasElementSelection.SelectedElements.Count == 1;

                                                                if (ImGui.MenuItem("Delete selected",
                                                                                   "Del", // We should use the correct assigned short cut, but "Del or Backspace" is too long for layout
                                                                                   false,
                                                                                   oneOrMoreSelected))
                                                                {
                                                                    DeleteSelectedElements();
                                                                }

                                                                if (ImGui.MenuItem("Rename",
                                                                                   "",
                                                                                   false,
                                                                                   oneSelected))
                                                                {
                                                                    VariationThumbnail.VariationForRenaming =
                                                                        CanvasElementSelection.SelectedElements[0] as Variation;
                                                                }

                                                                if (ImGui.MenuItem("Update thumbnails",
                                                                                   ""))
                                                                {
                                                                    _rerenderRequested = true;
                                                                    _rerenderToFileRequested = true;
                                                                    TriggerThumbnailUpdate();
                                                                }

                                                                ImGui.Separator();
                                                                ImGui.MenuItem("Live Render Previews", "", ref UserSettings.Config.VariationLiveThumbnails,
                                                                               true);
                                                                ImGui.MenuItem("Preview on Hover", "", ref UserSettings.Config.VariationHoverPreview, true);

                                                                DrawAdditionalContextMenuContent(instance);
                                                            }, ref _contextMenuIsOpen);
        }
    }

    private bool _contextMenuIsOpen;

    public void StartHover(Variation variation, Instance instanceForBlending)
    {
        PoolForBlendOperations?.BeginHover(instanceForBlending, variation);
    }

    public void Apply(Variation variation, Instance instanceForBlending)
    {
        PoolForBlendOperations?.StopHover();
        PoolForBlendOperations?.Apply(instanceForBlending, variation);
    }

    public void StartBlendTo(Variation variation, float blend, Instance instanceForBlending)
    {
        if (variation.IsPreset)
        {
            PoolForBlendOperations?.BeginBlendToPresent(instanceForBlending, variation, blend);
        }
    }

    public void StopHover()
    {
        PoolForBlendOperations?.StopHover();
    }

    protected void TriggerThumbnailUpdate()
    {
        _renderThumbnailIndex = 0;
        _allThumbnailsRendered = false;
    }

    protected void TriggerThumbnailSave()
    {
        _renderThumbnailIndex = 0;
        _allThumbnailsRendered = false;
        _rerenderToFileRequested = true;
        _rerenderRequested = true;
    }
    

    protected void ResetView(bool hideHeader = false)
    {
        var pool = PoolForBlendOperations;
        if (pool == null)
            return;

        if (TryToGetBoundingBox(pool.AllVariations, 40, out var area))
        {
            if (!hideHeader)
            {
                area.Min.Y -= 50;
            }

            FitAreaOnCanvas(area);
        }

        _resetViewRequested = false;
    }

    private void HandleFenceSelection(SelectionFence selectionFence)
    {
        switch (selectionFence.UpdateAndDraw(out var selectMode))
        {
            case SelectionFence.States.PressedButNotMoved:
                if (selectMode == SelectionFence.SelectModes.Replace)
                    CanvasElementSelection.Clear();
                break;

            case SelectionFence.States.Updated:
                HandleSelectionFenceUpdate(selectionFence.BoundsInScreen);
                break;

            case SelectionFence.States.CompletedAsClick:
                CanvasElementSelection.Clear();
                break;
        }
    }

    private void HandleSelectionFenceUpdate(ImRect boundsInScreen)
    {
        if (PoolForBlendOperations == null)
            return;

        var boundsInCanvas = InverseTransformRect(boundsInScreen);
        var elementsToSelect = (from child in PoolForBlendOperations.AllVariations
                                let rect = new ImRect(child.PosOnCanvas, child.PosOnCanvas + child.Size)
                                where rect.Overlaps(boundsInCanvas)
                                select child).ToList();

        CanvasElementSelection.Clear();
        foreach (var element in elementsToSelect)
        {
            CanvasElementSelection.AddSelection(element);
        }
    }

    private void DeleteSelectedElements()
    {
        if (PoolForBlendOperations == null)
            return;

        if (CanvasElementSelection.SelectedElements.Count <= 0)
            return;

        var list = new List<Variation>();
        foreach (var e in CanvasElementSelection.SelectedElements)
        {
            if (e is Variation v)
            {
                list.Add(v);
            }
        }

        VariationsWindow.DeleteVariationsFromPool(PoolForBlendOperations, list);
        PoolForBlendOperations.SaveVariationsToFile();
    }

    #region thumbnail rendering
    private void UpdateNextVariationThumbnail(Instance instanceForBlending, IOutputUi textureOutputUi, Slot<Texture2D> textureOutputSlot)
    {
        if (_allThumbnailsRendered || PoolForBlendOperations == null)
            return;

        //_thumbnailCanvasRendering.InitializeCanvasTexture(VariationThumbnail.ThumbnailSize);

        if (PoolForBlendOperations.AllVariations.Count == 0 || textureOutputSlot?.Value == null)
        {
            _allThumbnailsRendered = true;
            _rerenderRequested = false;
            return;
        }

        if (!TryGetNextVariationForThumbnailRendering(out var variation))
        {
            _allThumbnailsRendered = true;
            _rerenderRequested = false;
            _rerenderToFileRequested = false;
            return;
        }
        
        RenderThumbnail(instanceForBlending, textureOutputSlot, variation);
        _renderThumbnailIndex++;
    }

    private bool TryGetNextVariationForThumbnailRendering([NotNullWhen(true)] out Variation? variation)
    {
        variation = null;

        if (PoolForBlendOperations == null)
            return false;

        var variations = PoolForBlendOperations.AllVariations;

        for (; _renderThumbnailIndex < variations.Count; _renderThumbnailIndex++)
        {
            var candidate = variations[_renderThumbnailIndex];

            // If nothing is selected, we just take the first available item.
            // Otherwise, we skip until we find a selected one.
            if (CanvasElementSelection.SelectedElements.Count == 0 || 
                CanvasElementSelection.SelectedElements.Contains(candidate))
            {
                variation = candidate;
                return true;
            }
        }

        return false;
    } 
    

    private void RenderThumbnail(Instance instanceForBlending, Slot<Texture2D> textureOutputSlot, Variation variation)
    {
        if (PoolForBlendOperations == null)
            return;

        // if (instanceForBlending.Outputs.Count == 0)
        //     return;
        
        // Set variation values
        PoolForBlendOperations.BeginHover(instanceForBlending, variation);
        
        textureOutputSlot.DirtyFlag.ForceInvalidate();
        textureOutputSlot.Update(_imageContext);
        
        var saveAs = _rerenderToFileRequested
                         ? ThumbnailManager.Categories.PackageMeta
                         : ThumbnailManager.Categories.Temp;
        
        var saveToFile = _rerenderToFileRequested;
        
        ThumbnailManager.SaveThumbnail(variation.Id, instanceForBlending.Symbol.SymbolPackage, textureOutputSlot.Value, saveAs, saveToFile);

        PoolForBlendOperations.StopHover();
    }
    #endregion

    #region layout and view
    public void RefreshView()
    {
        TriggerThumbnailUpdate();
        CanvasElementSelection.Clear();
        //ResetView();
        _resetViewRequested = true;
    }

    private static bool TryToGetBoundingBox(IEnumerable<Variation>? variations, float extend, out ImRect area)
    {
        area = new ImRect();
        if (variations == null)
            return false;

        var foundOne = false;

        foreach (var v in variations)
        {
            if (!foundOne)
            {
                area = ImRect.RectWithSize(v.PosOnCanvas, v.Size);
                foundOne = true;
            }
            else
            {
                area.Add(ImRect.RectWithSize(v.PosOnCanvas, v.Size));
            }
        }

        if (!foundOne)
            return false;

        area.Expand(Vector2.One * extend);
        return true;
    }

    /// <summary>
    /// This uses a primitive algorithm: Look for the bottom edge of a all element bounding box
    /// Then step through possible positions and check if a position would intersect with an existing element.
    /// Wrap columns to enforce some kind of grid.  
    /// </summary>
    internal static Vector2 FindFreePositionForNewThumbnail(IReadOnlyList<Variation> variations)
    {
        if (!TryToGetBoundingBox(variations, 0, out var area))
        {
            return Vector2.Zero;
        }

        const int columns = 3;
        var columnIndex = 0;

        var stepWidth = VariationThumbnail.ThumbnailSize.X + VariationThumbnail.SnapPadding.X;
        var stepHeight = VariationThumbnail.ThumbnailSize.Y + VariationThumbnail.SnapPadding.Y;

        var pos = new Vector2(area.Min.X,
                              area.Max.Y - VariationThumbnail.ThumbnailSize.Y);
        var rowStartPos = pos;

        while (true)
        {
            var intersects = false;
            var targetArea = new ImRect(pos, pos + VariationThumbnail.ThumbnailSize);

            foreach (var v in variations)
            {
                if (!targetArea.Overlaps(ImRect.RectWithSize(v.PosOnCanvas, v.Size)))
                    continue;

                intersects = true;
                break;
            }

            if (!intersects)
                return pos;

            columnIndex++;
            if (columnIndex == columns)
            {
                columnIndex = 0;
                rowStartPos += new Vector2(0, stepHeight);
                pos = rowStartPos;
            }
            else
            {
                pos += new Vector2(stepWidth, 0);
            }
        }
    }
    #endregion

    // Compute barycentric coordinates (u, v, w) for
    // point p with respect to triangle (a, b, c)
    private static void Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float u, out float v, out float w)
    {
        Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
        var den = v0.X * v1.Y - v1.X * v0.Y;
        v = (v2.X * v1.Y - v1.X * v2.Y) / den;
        w = (v0.X * v2.Y - v2.X * v0.Y) / den;
        u = 1.0f - v - w;
    }

    /// <summary>
    /// Implement selectionContainer
    /// </summary>
    public IEnumerable<ISelectableCanvasObject> GetSelectables()
    {
        return PoolForBlendOperations?.AllVariations ?? [];
    }
    
    protected override ScalableCanvas? Parent => null;
    private static EvaluationContext _imageContext = new() { RequestedResolution = new Int2(170,130)};
    

    public bool IsBlendingActive { get; private set; }
    private readonly List<float> _blendWeights = new(3);
    private readonly List<Vector2> _blendPoints = new(3);
    private readonly List<Variation> _blendVariations = new(3);

    private int _renderThumbnailIndex;
    private bool _allThumbnailsRendered;
    internal readonly CanvasElementSelection CanvasElementSelection = new();
    private Instance? _currentRenderInstance;
    private readonly SelectionFence _selectionFence = new();
}