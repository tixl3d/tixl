using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Legacy.Interaction;
using T3.Editor.Gui.Legacy.Interaction.Connections;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.Legacy;

internal sealed partial class Graph
{
    internal sealed class ConnectionSorter
    {
        public readonly List<ConnectionLineUi> Lines = new();
        private readonly IGraphView _graphView;
        private readonly Legacy.Graph _graph;

        public ConnectionSorter(Legacy.Graph graph, IGraphView graphView)
        {
            _graph = graph;
            _graphView = graphView;
        }
            
        public void Init()
        {
            Lines.Clear();
            _linesFromNodes = new Dictionary<SymbolUi.Child, List<ConnectionLineUi>>();
            _linesIntoNodes = new Dictionary<SymbolUi.Child, List<ConnectionLineUi>>();
            _linesToOutputNodes = new Dictionary<IOutputUi, List<ConnectionLineUi>>();
            _linesFromInputNodes = new Dictionary<IInputUi, List<ConnectionLineUi>>();
        }

        public void CreateAndSortLineUi(Symbol.Connection c, SymbolUi symbolUi, SymbolBrowser symbolBrowser)
        {
            var newLine = new ConnectionLineUi(c, _graph);
            Lines.Add(newLine);

            var childUis = symbolUi.ChildUis;

            if (c.IsConnectedToSymbolOutput)
            {
                if (!symbolUi.OutputUis.TryGetValue(c.TargetSlotId, out var outputNode))
                    return;

                if (!_linesToOutputNodes.ContainsKey(outputNode))
                    _linesToOutputNodes.Add(outputNode, new List<ConnectionLineUi>());

                _linesToOutputNodes[outputNode].Add(newLine);
            }
            else if (c.TargetParentOrChildId != ConnectionMaker.NotConnectedId
                     && c.TargetParentOrChildId != ConnectionMaker.UseDraftChildId)
            {
                if (!childUis.TryGetValue(c.TargetParentOrChildId, out var targetNode))
                    return;

                if (!_linesIntoNodes.ContainsKey(targetNode))
                    _linesIntoNodes.Add(targetNode, new List<ConnectionLineUi>());

                _linesIntoNodes[targetNode].Add(newLine);
            }

            if (c.IsConnectedToSymbolInput)
            {
                if (!symbolUi.InputUis.TryGetValue(c.SourceSlotId, out var inputNode))
                    return;

                if (!_linesFromInputNodes.ContainsKey(inputNode))
                    _linesFromInputNodes.Add(inputNode, new List<ConnectionLineUi>());

                _linesFromInputNodes[inputNode].Add(newLine);

                var color = UiColors.Gray;
                if (TypeUiRegistry.TryGetPropertiesForType(inputNode.Type, out var typeUiProperties))
                    color = typeUiProperties.Color;

                newLine.ColorForType = color;
            }
            else if (c.SourceParentOrChildId != ConnectionMaker.NotConnectedId
                     && c.SourceParentOrChildId != ConnectionMaker.UseDraftChildId)
            {
                if (!childUis.TryGetValue(c.SourceParentOrChildId, out var sourceNode))
                    return;

                if (!_linesFromNodes.ContainsKey(sourceNode))
                    _linesFromNodes.Add(sourceNode, new List<ConnectionLineUi>());

                _linesFromNodes[sourceNode].Add(newLine);
            }

            InitTempConnection(newLine, symbolBrowser);
        }

        private void InitTempConnection(ConnectionLineUi newLine, SymbolBrowser symbolBrowser)
        {
            if (!(newLine.Connection is ConnectionMaker.TempConnection c))
                return;

            newLine.ColorForType = TypeUiRegistry.GetPropertiesForType(c.ConnectionType).Color;

            // if (!ConnectionMaker.TempConnections.Contains(c))
            //     return;

            // if (!Equals(newLine.Connection, ConnectionMaker.TempConnections))
            //     return;

            if (c.TargetParentOrChildId == ConnectionMaker.NotConnectedId)
            {
                if (ConnectionSnapEndHelper.BestMatchLastFrame != null)
                {
                    newLine.TargetPosition = new Vector2(ConnectionSnapEndHelper.BestMatchLastFrame.Area.Min.X,
                                                         ConnectionSnapEndHelper.BestMatchLastFrame.Area.GetCenter().Y);
                }
                else
                {
                    newLine.TargetPosition = ImGui.GetMousePos();
                }

                newLine.ColorForType = Color.White;
            }
            else if (c.TargetParentOrChildId == ConnectionMaker.UseDraftChildId)
            {
                newLine.TargetPosition = _graphView.Canvas.TransformPosition(symbolBrowser.PosOnCanvas);
                //newLine.ColorForType = Color.White;
            }
            else if (c.SourceParentOrChildId == ConnectionMaker.NotConnectedId)
            {
                newLine.SourcePosition = ImGui.GetMousePos();
                //newLine.ColorForType = Color.White;
            }
            else if (c.SourceParentOrChildId == ConnectionMaker.UseDraftChildId)
            {
                newLine.SourcePosition = symbolBrowser.OutputPositionOnScreen;
            }
            else
            {
                Log.Warning("invalid temporary connection?");
            }
        }

        private readonly List<ConnectionLineUi> _resultConnection = new(20);

        public IReadOnlyList<ConnectionLineUi> GetLinesFromNodeOutput(SymbolUi.Child childUi, Guid outputId)
        {
            _resultConnection.Clear();

            if (!_linesFromNodes.TryGetValue(childUi, out var lines))
                return Array.Empty<ConnectionLineUi>();

            foreach (var l in lines)
            {
                if (l.Connection.SourceSlotId != outputId)
                    continue;

                _resultConnection.Add(l);
            }

            return _resultConnection;
        }

        public IReadOnlyList<ConnectionLineUi> GetLinesToNodeInputSlot(SymbolUi.Child childUi, Guid inputId)
        {
            _resultConnection.Clear();
            if (!_linesIntoNodes.TryGetValue(childUi, out var lines))
                return Array.Empty<ConnectionLineUi>();

            foreach (var l in lines)
            {
                if (l.Connection.TargetSlotId != inputId)
                    continue;
                _resultConnection.Add(l);
            }

            return _resultConnection;
        }

        public IReadOnlyList<ConnectionLineUi> GetLinesIntoNode(SymbolUi.Child childUi)
        {
            return _linesIntoNodes.TryGetValue(childUi, out var node) ? node : Array.Empty<ConnectionLineUi>();
        }

        public IReadOnlyList<ConnectionLineUi> GetLinesToOutputNodes(IOutputUi outputNode, Guid outputId)
        {
            return _linesToOutputNodes.TryGetValue(outputNode, out var node)
                       ? node.FindAll(l => l.Connection.TargetSlotId == outputId)
                       : Array.Empty<ConnectionLineUi>();
        }

        public IReadOnlyList<ConnectionLineUi> GetLinesFromInputNodes(IInputUi inputNode, Guid inputNodeId)
        {
            return _linesFromInputNodes.TryGetValue(inputNode, out var node)
                       ? node.FindAll(l => l.Connection.SourceSlotId == inputNodeId)
                       : Array.Empty<ConnectionLineUi>();
        }

        private Dictionary<SymbolUi.Child, List<ConnectionLineUi>> _linesFromNodes = new(50);
        private Dictionary<SymbolUi.Child, List<ConnectionLineUi>> _linesIntoNodes = new(50);
        private Dictionary<IOutputUi, List<ConnectionLineUi>> _linesToOutputNodes = new(50);
        private Dictionary<IInputUi, List<ConnectionLineUi>> _linesFromInputNodes = new(50);
    }
}