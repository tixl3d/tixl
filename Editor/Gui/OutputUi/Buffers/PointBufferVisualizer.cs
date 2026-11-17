#nullable enable
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource;

namespace T3.Editor.Gui.OutputUi.Buffers;

/// <summary>
/// Renders a point buffer as if it were connected to a [VisualizePoints] with default parameters. The op is a
/// parentless instance that is connected to the buffer only while it renders, so it never keeps the shown op alive.
/// </summary>
internal sealed class PointBufferVisualizer
{
    public PointBufferVisualizer()
    {
        _onInstanceDisposed = OnInstanceDisposed;
    }

    /// <returns>False if [VisualizePoints] can't be instantiated, e.g. while its package is reloading.</returns>
    public bool Render(Slot<BufferWithViews> bufferSlot, EvaluationContext context)
    {
        if (!TryGetVisualizer())
            return false;

        _pointsInput!.AddConnection(bufferSlot);
        try
        {
            _commandRenderer.RenderForeignOutput(_output!, context);
        }
        finally
        {
            _pointsInput.RemoveConnection();
        }

        return true;
    }

    public void DrawRendered(string viewId)
    {
        if (_output != null)
            _commandRenderer.DrawRenderedOutput(_output, viewId);
    }

    private bool TryGetVisualizer()
    {
        if (_instance != null)
            return _pointsInput != null && _output != null;

        if (!SymbolRegistry.TryGetSymbol(VisualizePointsSymbolId, out var symbol)
            || !symbol.TryGetParentlessInstance(out var instance))
        {
            if (!_hasWarnedAboutMissingSymbol)
                Log.Warning("Can't show points: [VisualizePoints] could not be instantiated.");

            _hasWarnedAboutMissingSymbol = true;
            return false;
        }

        instance.Disposing += _onInstanceDisposed;
        _instance = instance;

        foreach (var input in instance.Inputs)
        {
            if (input.Id == PointsInputId)
                _pointsInput = input as InputSlot<BufferWithViews>;
        }

        foreach (var output in instance.Outputs)
        {
            if (output is Slot<Command> commandOutput)
            {
                _output = commandOutput;
                break;
            }
        }

        return _pointsInput != null && _output != null;
    }

    private void OnInstanceDisposed(IResourceConsumer _)
    {
        if (_instance != null)
            _instance.Disposing -= _onInstanceDisposed;

        _instance = null;
        _pointsInput = null;
        _output = null;
    }

    private static readonly Guid VisualizePointsSymbolId = Guid.Parse("37bdbafc-d14c-4b81-91c3-8f63c3b63812");
    private static readonly Guid PointsInputId = Guid.Parse("54fc4cd7-dfc3-4690-9fd1-2b555f7656d4");

    private readonly CommandOutputUi _commandRenderer = new();
    private readonly Action<IResourceConsumer> _onInstanceDisposed;
    private Instance? _instance;
    private InputSlot<BufferWithViews>? _pointsInput;
    private Slot<Command>? _output;
    private bool _hasWarnedAboutMissingSymbol;
}
