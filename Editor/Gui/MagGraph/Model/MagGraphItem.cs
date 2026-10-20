#nullable enable
using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.OpUis;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Selection;
// ReSharper disable UseCollectionExpression
// ReSharper disable UseWithExpressionToCopyStruct

namespace T3.Editor.Gui.MagGraph.Model;

internal sealed class MagGraphItem : ISelectableCanvasObject, IValueSnapAttractor
{
    public enum Variants
    {
        Operator,
        Input,
        Output,
        Placeholder,
        Obsolete,
    }

    internal int LastUpdateCycle;
    public Guid Id { get; init; }
    public Variants Variant;
    public Type PrimaryType = typeof(float);
    public required ISelectableCanvasObject Selectable;
    public SymbolUi.Child? ChildUi; // matches Selected for operators
    public Vector2 PosOnCanvas { get => Selectable.PosOnCanvas; set => Selectable.PosOnCanvas = value; }
    public Vector2 DampedPosOnCanvas;
    public Vector2 Size { get; set; }

    public bool IsCollapsedAway => ChildUi != null && ChildUi.IsHiddenInCollapsedSection;

    public OpUiBinding? OpUiBinding;
    public MeasuredTextWidth MeasuredLabel;
    
    public ImRect Area => ImRect.RectWithSize(PosOnCanvas, Size);
    public ImRect VerticalStackArea;

    public override string ToString() => ReadableName;

    public SymbolUi? SymbolUi;
    public Symbol.Child? SymbolChild;

    public IReadOnlyList<Guid>? InstancePath
    {
        get => _instancePath;
        set
        {
            _instancePath = value;
            _instance = null;
        }
    }

    /// <summary>
    /// Resolving the path takes the symbol's creation lock at every nesting level, and drawing reads this many
    /// times per item and frame. The cached instance is dropped once disposed, e.g. after an operator package reload.
    /// </summary>
    public Instance? Instance
    {
        get
        {
            if (_instance is { IsDisposed: false })
                return _instance;

            _instance = null;
            if (_instancePath == null || SymbolChild == null)
                return null;

            SymbolChild.TryGetOrCreateInstance(_instancePath, out _instance, out _, true);
            return _instance;
        }
    }

    public string ReadableName
    {
        get
        {
            return Variant switch
                       {
                           Variants.Operator => SymbolChild == null
                                                    ? "???"
                                                    : SymbolChild.HasCustomName
                                                        ? "\"" + SymbolChild.ReadableName + "\""
                                                        : SymbolChild.ReadableName,
                           Variants.Input  => Selectable is IInputUi inputUi ? inputUi.InputDefinition.Name : "???",
                           Variants.Output => Selectable is IOutputUi outputUi ? outputUi.OutputDefinition.Name : "???",
                           _               => "???"
                       };
        }
    }

    // public bool IsSelected(NodeSelection nodeSelection)
    // {
    //     return nodeSelection.Selection.Any(c => c.Id == Id);
    // }
    public void ResetConnections(int updateCycle)
    {
        InputLines = Array.Empty<InputLine>();
        OutputLines = Array.Empty<OutputLine>();
        LastUpdateCycle = updateCycle;
    }

    public InputLine[] InputLines = Array.Empty<InputLine>();
    public OutputLine[] OutputLines = Array.Empty<OutputLine>();
    public bool HasHiddenOutputs;

    public enum InputLineStates
    {
        Connected,
        TempConnection,
        NotConnected,

        /// <summary>Fed by an operator whose symbol is missing.</summary>
        MissingConnection,
    } 

    /// <summary>
    /// A text width measured once at the font's base size and scaled to the requested size, as glyph advances
    /// scale linearly. Measuring labels for every visible node each frame was a large share of drawing big graphs.
    /// </summary>
    public struct MeasuredTextWidth
    {
        /// <param name="text">The source string; a different value triggers a new measurement.</param>
        /// <param name="displayedText">What is actually drawn, e.g. with spaces added for readability.</param>
        public unsafe float GetWidth(ImFontPtr font, float fontSize, string text, ReadOnlySpan<char> displayedText)
        {
            var fontPtr = (nint)font.NativePtr;
            var spacesAdded = UserSettings.Config.AddSpacesToParameterNames;
            if (fontPtr != _font
                || font.FontSize != _baseFontSize
                || spacesAdded != _spacesAdded
                || !string.Equals(text, _text))
            {
                _widthAtBaseSize = font.CalcTextSizeA(font.FontSize, float.MaxValue, 0, displayedText).X;
                _font = fontPtr;
                _baseFontSize = font.FontSize;
                _spacesAdded = spacesAdded;
                _text = text;
            }

            return _widthAtBaseSize * fontSize / _baseFontSize;
        }

        private string? _text;
        private nint _font;
        private float _baseFontSize;
        private bool _spacesAdded;
        private float _widthAtBaseSize;
    }

    public struct InputLine
    {
        public Type Type;
        public Guid Id;
        public ISlot Input;
        public IInputUi InputUi;
        public int VisibleIndex;
        public MagGraphConnection? ConnectionIn;
        public int MultiInputIndex;
        public InputLineStates ConnectionState;
        public MeasuredTextWidth MeasuredLabel;
    }

    public struct OutputLine
    {
        public Guid Id;
        public ISlot Output;
        public IOutputUi? OutputUi;
        public int VisibleIndex;
        public int OutputIndex;
        public List<MagGraphConnection> ConnectionsOut;
        public MeasuredTextWidth MeasuredLabel;

        public Vector2 GetRightAnchorPosition(MagGraphItem item)
        {
            return item.DampedPosOnCanvas + new Vector2(GridSize.X + GridSize.Y * (0.5f * VisibleIndex));
        }
    }

    public struct InputAnchorPoint
    {
        public Vector2 PositionOnCanvas;
        public Directions Direction;
        public Type ConnectionType;
        public int SnappedConnectionHash;
        public Guid SlotId;
        public InputLine InputLine;
    }
    
    public struct OutputAnchorPoint
    {
        public Vector2 PositionOnCanvas;
        public Directions Direction;
        public Type ConnectionType;
        public int SnappedConnectionHash;
        public Guid SlotId;
        public int OutputLineIndex;
    }


    public enum Directions
    {
        Horizontal,
        Vertical,
    }

    public const float Width = 140;
    public const float WidthHalf = Width / 2;
    public const float LineHeight = 35;
    public static readonly Vector2 GridSize = new(Width, LineHeight);

    public ImRect Bounds => ImRect.RectWithSize(PosOnCanvas, Size);
    
    public static ImRect GetItemsBounds(IEnumerable<MagGraphItem> items)
    {
        ImRect extend = default;
        var index = 0;

        // Can't use list because enumerable...
        foreach (var item in items)
        {
            if (index == 0)
            {
                extend = item.Area;
            }
            else
            {
                extend.Add(item.Area);
            }

            index++;
        }

        return extend;
    }
    
    /// <summary>
    /// input anchor taken if
    /// - connected
    ///
    /// output anchor is taken if...
    /// - 
    /// </summary>
    public void GetOutputAnchorAtIndex(int index, ref OutputAnchorPoint point)
    {
        if (index == 0)
        {
            point.PositionOnCanvas = new Vector2(WidthHalf, Size.Y) + DampedPosOnCanvas;
            point.Direction = Directions.Vertical;
            point.ConnectionType = OutputLines[0].Output.ValueType;
            point.SnappedConnectionHash = GetSnappedConnectionHash(OutputLines[0].ConnectionsOut);
            point.SlotId = OutputLines[0].Output.Id;
            point.OutputLineIndex = 0;
            return;
        }

        var lineIndex = index - 1;
        point.PositionOnCanvas = new Vector2(Width, (0.5f + OutputLines[lineIndex].VisibleIndex) * LineHeight) + DampedPosOnCanvas;
        point.Direction = Directions.Horizontal;
        point.ConnectionType = OutputLines[lineIndex].Output.ValueType;
        point.SnappedConnectionHash = GetSnappedConnectionHash(OutputLines[lineIndex].ConnectionsOut);
        point.SlotId = OutputLines[lineIndex].Output.Id;
        point.OutputLineIndex = lineIndex;
    }

    public int GetOutputAnchorCount() => OutputLines.Length == 0 ? 0 : OutputLines.Length + 1;


    /// <summary>
    /// Get input anchors with current position and orientation for drawing and snapping
    /// </summary>
    /// <remarks>
    /// Using an Enumerable interface here is bad, because it creates a lot of allocations.
    /// In the long term, this should be cached.
    /// </remarks>
    public void GetInputAnchorAtIndex(int index, ref InputAnchorPoint anchorPoint)
    {
        if (index == 0)
        {
            anchorPoint.PositionOnCanvas = new Vector2(WidthHalf, 0) + DampedPosOnCanvas;
            anchorPoint.Direction = Directions.Vertical;
            anchorPoint.ConnectionType = InputLines[0].Type;
            anchorPoint.SnappedConnectionHash = InputLines[0].ConnectionIn?.ConnectionHash ?? FreeAnchor;
            anchorPoint.SlotId = InputLines[0].Id;
            anchorPoint.InputLine = InputLines[0];
            
            return;
        }
        
        var lineIndex = index - 1;
        anchorPoint.PositionOnCanvas = new Vector2(0, (0.5f + InputLines[lineIndex].VisibleIndex) * LineHeight) + DampedPosOnCanvas;
        anchorPoint.Direction = Directions.Horizontal;
        anchorPoint.ConnectionType = InputLines[lineIndex].Type;
        anchorPoint.SnappedConnectionHash = InputLines[lineIndex].ConnectionIn?.ConnectionHash ?? FreeAnchor;
        anchorPoint.SlotId = InputLines[lineIndex].Id;
        anchorPoint.InputLine = InputLines[lineIndex]; //TODO avoid copy
    }
    
    public int GetInputAnchorCount() => InputLines.Length == 0 ? 0 : InputLines.Length + 1;

    /** Assume as free (I.e. not connected) unless an connection is snapped, then return this connection as hash. */
    private static int GetSnappedConnectionHash(List<MagGraphConnection> snapGraphConnections)
    {
        foreach (var sc in snapGraphConnections)
        {
            if (!sc.IsSnapped)
                continue;

            return sc.ConnectionHash;
        }

        return FreeAnchor;
    }

    public bool TryGetPrimaryInConnection([NotNullWhen(true)]out  MagGraphConnection? connection)
    {
        connection = null;

        if (InputLines.Length == 0)
            return false;

        connection= InputLines[0].ConnectionIn;
        return connection != null;
    }
    
    public bool TryGetPrimaryOutConnections(out List<MagGraphConnection> connections)
    {
        if (OutputLines.Length == 0)
        {
            connections = [];
            return false;
        }

        connections= OutputLines[0].ConnectionsOut;
        return connections.Count > 0;
    }
    

    public const int FreeAnchor = -1;

    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        if (snapResult.Orientation == SnapResult.Orientations.Horizontal)
        {
            snapResult.TryToImproveWithAnchorValue(DampedPosOnCanvas.X);
            //snapResult.TryToImproveWithAnchorValue(DampedPosOnCanvas.X + Size.X);
        }
        else  if (snapResult.Orientation == SnapResult.Orientations.Vertical)
        {
            snapResult.TryToImproveWithAnchorValue(DampedPosOnCanvas.Y);
            //snapResult.TryToImproveWithAnchorValue(DampedPosOnCanvas.Y + Size.Y);
        }
    }
    
    //
    // public void ForOutputAnchors(Action<AnchorPoint> call)
    // {
    //     if (OutputLines.Length == 0)
    //         return;
    //
    //     call(new AnchorPoint
    //              {
    //                  PositionOnCanvas = new Vector2(WidthHalf, Size.Y) + PosOnCanvas,
    //                  Direction = Directions.Vertical,
    //                  ConnectionType = OutputLines[0].Output.ValueType,
    //                  ConnectionHash = OutputLines[0].Output.HasInputConnections
    //                                       ? OutputLines[0].Output.GetConnection(0).GetHashCode()
    //                                       : 0,
    //                  SlotId = OutputLines[0].Output.Id,
    //              });
    //
    //     foreach (var il in OutputLines)
    //     {
    //         call(new AnchorPoint
    //                  {
    //                      PositionOnCanvas = new Vector2(Width, (0.5f + il.VisibleIndex) * LineHeight) + PosOnCanvas,
    //                      Direction = Directions.Horizontal,
    //                      ConnectionType = il.Output.ValueType,
    //                      ConnectionHash = il.Output.HasInputConnections
    //                                           ? OutputLines[0].Output.GetConnection(0).GetHashCode()
    //                                           : 0,
    //                      SlotId = il.Output.Id,
    //                  });
    //     }
    // }

    public void Select(NodeSelection nodeSelection)
    {
        // TODO: Avoid this by not using magGraphItem as selectable
        if (Variant == MagGraphItem.Variants.Operator
            && Instance?.Parent != null
            && SymbolUiRegistry.TryGetSymbolUi(Instance.Parent.Symbol.Id, out var parentSymbolUi)
            && parentSymbolUi.ChildUis.TryGetValue(Instance.SymbolChildId, out var childUi))
        {
            nodeSelection.SetSelection(childUi, Instance);
        }
        else
        {
            //nodeSelection.Clear();
            nodeSelection.SetSelection(Selectable);
        }
    }

    public void AddToSelection(NodeSelection nodeSelection)
    {
        // TODO: Avoid this by not using magGraphItem as selectable
        if (Variant == MagGraphItem.Variants.Operator
            && Instance?.Parent != null
            && SymbolUiRegistry.TryGetSymbolUi(Instance.Parent.Symbol.Id, out var parentSymbolUi)
            && parentSymbolUi.ChildUis.TryGetValue(Instance.SymbolChildId, out var childUi))
        {
            nodeSelection.AddSelection(childUi, Instance);
        }
        else
        {
            nodeSelection.AddSelection(this);
        }
    }

    private IReadOnlyList<Guid>? _instancePath;
    private Instance? _instance;
}
