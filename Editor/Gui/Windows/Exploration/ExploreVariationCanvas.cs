#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using Vector2 = System.Numerics.Vector2;


namespace T3.Editor.Gui.Windows.Exploration;

internal sealed class ExploreVariationCanvas : ScalableCanvas
{
    public ExploreVariationCanvas(ExplorationWindow explorationWindow)
    {
        ResetView();
        _explorationWindow = explorationWindow;
    }

    public void Draw(Structure? structure)
    {
        _thumbnailCanvasRendering.InitializeCanvasTexture(_thumbnailSize);

        var outputWindow = OutputWindow.OutputWindowInstances.FirstOrDefault(window => window.Config.Visible) as OutputWindow;
        if (outputWindow == null)
        {
            ImGui.TextUnformatted("No output window found");
            return;
        }

        if (structure == null)
        {
            ImGui.TextUnformatted("No graph window open");
            return;
        }

        var instance = outputWindow.ShownInstance;
        if (instance == null || instance.Outputs.Count == 0)
        {
            CustomComponents.EmptyWindowMessage("To explore variations\nselect a graph operator and\none or more of its parameters.");
            return;
        }

        _firstOutputSlot = instance.Outputs[0];
        if (_firstOutputSlot is not Slot<Texture2D> textureSlot)
        {
            CustomComponents.EmptyWindowMessage("Output window must be pinned\nto a texture operator.");
            _firstOutputSlot = null;
            return;
        }

        // Set correct output ui
        {
            var symbolUi = instance.GetSymbolUi();
            if (!symbolUi.OutputUis.ContainsKey(_firstOutputSlot.Id))
                return;

            _explorationWindow.OutputUi = symbolUi.OutputUis[_firstOutputSlot.Id];
        }

        if (textureSlot.Value == null)
            return;

        FillInNextVariation(structure);
        UpdateCanvas(out _);
        Invalidate();

        var drawList = ImGui.GetWindowDrawList();

        // Draw Canvas Texture

        var canvasSize = _thumbnailCanvasRendering.GetCanvasTextureSize();
        var rectOnScreen = ImRect.RectWithSize(WindowPos, canvasSize);
        drawList.AddImage((IntPtr)_thumbnailCanvasRendering.CanvasTextureSrv, rectOnScreen.Min, rectOnScreen.Max);

        foreach (var variation in _variationByGridIndex.Values)
        {
            if (!IsCellVisible(variation.GridCell))
                continue;

            var screenRect = GetScreenRectForCell(variation.GridCell);
            if (variation.ThumbnailNeedsUpdate)
            {
                drawList.AddRectFilled(screenRect.Min, screenRect.Max, _needsUpdateColor);
            }
        }

        _hoveringVariation?.RestoreValues();
        var size = DrawUtils.GetContentRegionArea();
        ImGui.InvisibleButton("variationCanvas", size.GetSize());

        if (ImGui.IsItemHovered())
        {
            _hoveringVariation = CreateVariationAtMouseMouse();

            if (_hoveringVariation != null)
            {
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    var savedVariation = _hoveringVariation.Clone(structure);

                    _explorationWindow.SaveVariation(savedVariation);
                    savedVariation.ApplyPermanently();
                }

                _hoveringVariation.KeepCurrentAndApplyNewValues(structure);
            }
        }
        else
        {
            _hoveringVariation = null;
        }
    }

    public void ClearVariations()
    {
        _currentOffsetIndexForFocus = 0;
        _updateCompleted = false;
        _variationByGridIndex.Clear();
    }

    public void ResetView()
    {
        var extend = new Vector2(3, 3);
        var center = new Vector2(GridCell.VariationGridSize / 2f, GridCell.VariationGridSize / 2f);
        var left = (center - extend) * _thumbnailSize;
        var right = (center + extend) * _thumbnailSize;
        //UserSettings.Config.ZoomSpeed = 20000;

        FitAreaOnCanvas(new ImRect(left, right));
    }

    private ImRect GetScreenRectForCell(GridCell gridCell)
    {
        var thumbnailInCanvas = ImRect.RectWithSize(new Vector2(gridCell.X, gridCell.Y) * _thumbnailSize, _thumbnailSize);
        var r = TransformRect(thumbnailInCanvas);
        return new ImRect((int)r.Min.X, (int)r.Min.Y, (int)r.Max.X - 1, (int)r.Max.Y - 1);
    }

    private GridCell GetScreenRectForGridCell(Vector2 screenPos)
    {
        var centerInCanvas = InverseTransformPositionFloat(screenPos);
        return new GridCell(
                            (int)(centerInCanvas.X / _thumbnailSize.X),
                            (int)(centerInCanvas.Y / _thumbnailSize.Y));
    }

    private bool IsCellVisible(GridCell gridCell)
    {
        var windowPos = ImGui.GetWindowPos();
        var contentRegion = new ImRect(windowPos, windowPos + ImGui.GetWindowSize());
        contentRegion.Expand(_thumbnailSize * Scale);

        var rectOnScreen = GetScreenRectForCell(gridCell);

        var visible = contentRegion.Contains(rectOnScreen);
        return visible;
    }

    private void Invalidate()
    {
        var scaleChanged = Math.Abs(Scale.X - _lastScale) > 0.01f;
        var scrollChanged = Math.Abs(Scroll.X - _lastScroll.X) > 0.1f
                            || Math.Abs(Scroll.Y - _lastScroll.Y) > 0.1f;

        if (Math.Abs(_lastScatter - Scatter) > 0.01f)
        {
            _lastScatter = Scatter;
            ClearVariations();
        }

        // TODO: optimize performance by only invalidating thumbnails and moving part of the canvas  
        if (scaleChanged || scrollChanged)
        {
            _lastScale = Scale.X;
            _lastScroll = Scroll;

            _currentOffsetIndexForFocus = 0;
            _updateCompleted = false;

            foreach (var variation in _variationByGridIndex.Values)
            {
                variation.ThumbnailNeedsUpdate = true;
            }

            if (ImGui.IsWindowHovered())
            {
                SetGridFocusToMousePos();
            }
            else
            {
                SetGridFocusToWindowCenter();
            }

            _thumbnailCanvasRendering.ClearTexture();
        }
    }

    private void SetGridFocusToWindowCenter()
    {
        var center = ImGui.GetWindowPos() + ImGui.GetWindowSize() * 0.5f;
        var centerInCanvas = InverseTransformPositionFloat(center);
        _gridFocusIndex.X = (int)(centerInCanvas.X / _thumbnailSize.X);
        _gridFocusIndex.Y = (int)(centerInCanvas.Y / _thumbnailSize.Y);
    }

    private void SetGridFocusToMousePos()
    {
        var centerInCanvas = InverseTransformPositionFloat(ImGui.GetMousePos());
        _gridFocusIndex.X = (int)(centerInCanvas.X / _thumbnailSize.X);
        _gridFocusIndex.Y = (int)(centerInCanvas.Y / _thumbnailSize.Y);
    }

    private void FillInNextVariation(Structure structure)
    {
        if (_updateCompleted)
        {
            return;
        }

        if (_explorationWindow.VariationParameters.Count == 0)
        {
            return;
        }

        while (_currentOffsetIndexForFocus < _sortedOffset.Length)
        {
            var offset = _sortedOffset[_currentOffsetIndexForFocus++];
            var cell = _gridFocusIndex + offset;
            if (!cell.IsWithinGrid())
                continue;

            if (!IsCellVisible(cell))
                continue;

            var hasVariation = _variationByGridIndex.TryGetValue(cell.GridIndex, out var variation);
            if (hasVariation)
            {
                if (!variation!.ThumbnailNeedsUpdate)
                    continue;

                RenderThumbnail(variation, structure);
                return;
            }
            else
            {
                variation = CreateVariationForCell(cell);
                RenderThumbnail(variation, structure);
                _variationByGridIndex[cell.GridIndex] = variation;
                return;
            }
        }

        _updateCompleted = true;
    }

    private ExplorationVariation CreateVariationAtMouseMouse()
    {
        var mousePos = ImGui.GetMousePos();
        var cellBelowMouse = GetScreenRectForGridCell(mousePos);
        var region = GetScreenRectForCell(cellBelowMouse);
        var posInCell = mousePos - region.Min;

        var cellSize = region.GetSize();
        var halfSize = region.GetSize() / 2;

        posInCell -= halfSize;
        if (posInCell.X < 0)
        {
            cellBelowMouse.X--;
            posInCell.X += halfSize.X;
        }
        else
        {
            posInCell.X -= halfSize.X;
        }

        if (posInCell.Y < 0)
        {
            cellBelowMouse.Y--;
            posInCell.Y += halfSize.Y;
        }
        else
        {
            posInCell.Y -= halfSize.Y;
        }

        ImGui.GetWindowDrawList().AddRect(region.Min, region.Max, UiColors.StatusAnimated);

        var clamp = cellSize / 2f * HoverEdgeBlendFactor;
        var xWeight = posInCell.X.Clamp(-clamp.X, clamp.X) / clamp.X / 2 + 0.5f;
        var yWeight = posInCell.Y.Clamp(-clamp.Y, clamp.Y) / clamp.Y / 2 + 0.5f;

        var neighbours = new List<Tuple<ExplorationVariation, float>>();

        if (_variationByGridIndex.TryGetValue(cellBelowMouse.GridIndex, out var variationTopLeft))
        {
            var weight = (1 - xWeight) * (1 - yWeight);
            neighbours.Add(new Tuple<ExplorationVariation, float>(variationTopLeft, weight));
        }

        if (_variationByGridIndex.TryGetValue((cellBelowMouse + new GridCell(1, 0)).GridIndex, out var variationTopRight))
        {
            var weight = xWeight * (1 - yWeight);
            neighbours.Add(new Tuple<ExplorationVariation, float>(variationTopRight, weight));
        }

        if (_variationByGridIndex.TryGetValue((cellBelowMouse + new GridCell(0, 1)).GridIndex, out var variationBottomLeft))
        {
            var weight = (1 - xWeight) * yWeight;
            neighbours.Add(new Tuple<ExplorationVariation, float>(variationBottomLeft, weight));
        }

        if (_variationByGridIndex.TryGetValue((cellBelowMouse + new GridCell(1, 1)).GridIndex, out var variationBottomRight))
        {
            var weight = xWeight * yWeight;
            neighbours.Add(new Tuple<ExplorationVariation, float>(variationBottomRight, weight));
        }

        return ExplorationVariation.Mix(_explorationWindow.VariationParameters, neighbours, 0);
    }

    private ExplorationVariation CreateVariationForCell(GridCell cell)
    {
        // Collect neighbours
        var neighboursAndWeights = new List<Tuple<ExplorationVariation, float>>();
        foreach (var nOffset in _neighbourOffsets)
        {
            var neighbourCell = cell + nOffset;

            if (_variationByGridIndex.TryGetValue(neighbourCell.GridIndex, out var neighbour))
                neighboursAndWeights.Add(new Tuple<ExplorationVariation, float>(neighbour, 1));
        }

        return ExplorationVariation.Mix(_explorationWindow.VariationParameters, neighboursAndWeights, Scatter, cell);
    }

        
    public void AddVariationToGrid(ExplorationVariation newVariation)
    {
        _variationByGridIndex[newVariation.GridCell.GridIndex] = newVariation;
    }

    private void RenderThumbnail(ExplorationVariation variation, Structure structure)
    {
        variation.ThumbnailNeedsUpdate = false;

        var screenRect = GetScreenRectForCell(variation.GridCell);
        var posInCanvasTexture = screenRect.Min - WindowPos;

        // Set variation values
        variation.KeepCurrentAndApplyNewValues(structure);

        // Render variation
        _thumbnailCanvasRendering.EvaluationContext.Reset();
        _thumbnailCanvasRendering.EvaluationContext.LocalTime = 13.4f;

        // NOTE: This is horrible hack to prevent _imageCanvas from being rendered by ImGui
        // DrawValue will use the current ImageOutputCanvas for rendering
        _imageCanvas.SetAsCurrent();
        ImGui.PushClipRect(new Vector2(0, 0), new Vector2(1, 1), true);
        _explorationWindow.OutputUi.DrawValue(_firstOutputSlot, _thumbnailCanvasRendering.EvaluationContext, "thumbnail");
        ImGui.PopClipRect();
        ImageOutputCanvas.Deactivate();

        if (_firstOutputSlot is Slot<Texture2D> textureSlot)
        {
            var rect = ImRect.RectWithSize(posInCanvasTexture, screenRect.GetSize());
            _thumbnailCanvasRendering.CopyToCanvasTexture(textureSlot, rect);
        }

        variation.RestoreValues();
    }
        
        
    //public IEnumerable<Variation> AllVariations => _variationByGridIndex.Values;

    private const float HoverEdgeBlendFactor = 0.5f;
    private static readonly GridCell[] _sortedOffset = GridCell.BuildSortedOffsets();
    private static readonly GridCell _gridCenter = GridCell.Center;
    private float _lastScale;
    private Vector2 _lastScroll = Vector2.One;
    private static readonly Color _needsUpdateColor = new(1f, 1f, 1f, 0.05f);
    private readonly Dictionary<int, ExplorationVariation> _variationByGridIndex = new();
    private GridCell _gridFocusIndex = _gridCenter;
    private int _currentOffsetIndexForFocus;
    private bool _updateCompleted;
    private readonly ImageOutputCanvas _imageCanvas = new();

    private readonly ExplorationWindow _explorationWindow;
    private ExplorationVariation? _hoveringVariation;

    private static readonly GridCell[] _neighbourOffsets =
        {
            new(-1, -1),
            new(0, -1),
            new(1, -1),
            new(1, 0),
            new(1, 1),
            new(0, 1),
            new(-1, 1),
            new(-1, 0),
        };

    public float Scatter = 20f;
    private float _lastScatter;
    private ISlot? _firstOutputSlot;

    protected override ScalableCanvas? Parent => null;

    private readonly ThumbnailCanvasRendering _thumbnailCanvasRendering = new();
    private static readonly Vector2 _thumbnailSize = new(160, 160 / 16f * 9);
        
        
}