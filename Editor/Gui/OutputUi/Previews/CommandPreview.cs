#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource;

namespace T3.Editor.Gui.OutputUi.Previews;

/// <summary>
/// Renders a value through a parentless op that takes it as its input and draws it to a <see cref="Command"/>
/// output, as if the value were connected to that op with default parameters. The value is connected only while it
/// renders, so the preview never keeps the shown op alive.
/// </summary>
internal sealed class CommandPreview<T>
{
    public CommandPreview(Guid symbolId, Guid inputId, string symbolName)
    {
        _symbolId = symbolId;
        _inputId = inputId;
        _symbolName = symbolName;
        _onInstanceDisposed = OnInstanceDisposed;
    }

    /// <returns>False if the preview op can't be instantiated, e.g. while its package is reloading.</returns>
    public bool Render(Slot<T> valueSlot, EvaluationContext context)
    {
        if (!TryGetInstance())
            return false;

        _input!.AddConnection(valueSlot);
        try
        {
            _commandRenderer.RenderForeignOutput(_output!, context);
        }
        finally
        {
            _input.RemoveConnection();
        }

        return true;
    }

    /// <summary>Draws the last render and leaves the cursor where it was, so controls can be drawn on top.</summary>
    public void DrawRendered(string viewId)
    {
        if (_output == null)
            return;

        var cursorPos = ImGui.GetCursorScreenPos();
        _commandRenderer.DrawRenderedOutput(_output, viewId);
        ImGui.SetCursorScreenPos(cursorPos);
    }

    private bool TryGetInstance()
    {
        if (_instance != null)
            return _input != null && _output != null;

        if (!SymbolRegistry.TryGetSymbol(_symbolId, out var symbol)
            || !symbol.TryGetParentlessInstance(out var instance))
        {
            if (!_hasWarnedAboutMissingSymbol)
                Log.Warning($"Can't render the preview: [{_symbolName}] could not be instantiated.");

            _hasWarnedAboutMissingSymbol = true;
            return false;
        }

        instance.Disposing += _onInstanceDisposed;
        _instance = instance;

        foreach (var input in instance.Inputs)
        {
            if (input.Id == _inputId)
                _input = input as InputSlot<T>;
        }

        foreach (var output in instance.Outputs)
        {
            if (output is Slot<Command> commandOutput)
            {
                _output = commandOutput;
                break;
            }
        }

        return _input != null && _output != null;
    }

    private void OnInstanceDisposed(IResourceConsumer _)
    {
        if (_instance != null)
            _instance.Disposing -= _onInstanceDisposed;

        _instance = null;
        _input = null;
        _output = null;
    }

    private readonly Guid _symbolId;
    private readonly Guid _inputId;
    private readonly string _symbolName;
    private readonly CommandOutputUi _commandRenderer = new();
    private readonly Action<IResourceConsumer> _onInstanceDisposed;
    private Instance? _instance;
    private InputSlot<T>? _input;
    private Slot<Command>? _output;
    private bool _hasWarnedAboutMissingSymbol;
}
