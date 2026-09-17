using T3.Core.Operator;

using T3.Editor.UiModel.Helpers;

namespace T3.Editor.UiModel.Commands.Graph;

/// <summary>Changes a child's bypass state while preserving routing-anchor callback forwarding.</summary>
public sealed class ChangeInstanceBypassedCommand : ICommand
{
    public string Name => "Bypass";
    public bool IsUndoable => true;

    /// <summary>Captures an operator child's bypass state and the requested replacement value.</summary>
    /// <param name="symbolChild">Child whose current bypass state is captured for undo.</param>
    /// <param name="setBypassedTo">Bypass state to assign when the command executes.</param>
    public ChangeInstanceBypassedCommand(Symbol.Child symbolChild, bool setBypassedTo)
    {
        _inputParentSymbolId = symbolChild.Parent.Id;
        _childId = symbolChild.Id;
        _originalState = symbolChild.IsBypassed;
        _newState = setBypassedTo;
    }

    public void Undo()
    {
        AssignValue(_originalState);
    }

    public void Do()
    {
        AssignValue(_newState);
    }

    /// <summary>Applies bypass to an ordinary child while preserving reroute forwarding contracts.</summary>
    /// <param name="shouldBeBypassed">Requested bypass state; recognized reroutes retain their protected forwarding behavior.</param>
    private void AssignValue(bool shouldBeBypassed)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_inputParentSymbolId, out var symbolUi))
            return;
            
        var symbol = symbolUi.Symbol;
        if (!symbol.Children.TryGetValue(_childId, out var child))
        {
            Log.Assert("Failed to find child");
            return;
        }

        // Enforce this for every caller: bypass would replace a command reroute's prepare/restore proxy.
        if (shouldBeBypassed && SymbolAnalysis.IsReroute(child.Symbol))
            return;

        try
        {
            child.IsBypassed = shouldBeBypassed;
            symbolUi.FlagAsModified();
        }
        catch (Exception e)
        {
            Log.Error($"Failed to set bypass state: {e.Message}");
        }
        
    }

    private readonly bool _newState;
    private readonly bool _originalState;
    private readonly Guid _inputParentSymbolId;
    private readonly Guid _childId;
}
