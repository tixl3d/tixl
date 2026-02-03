using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Interaction.Midi.CommandProcessing;

/// <summary>
/// Defines and invokes matching commands if input signals are matching. 
/// </summary>
/// <remarks>
/// The requirements turned out to be quite complicated. This sketch might give an overview:
/// - https://www.figma.com/file/lLlkKILVbLuv1mxuTq6jMb/Interaction?node-id=823%3A57
///
/// The general idea of the <see cref="CommandProcessing"/> namespace is to abstract as
/// much functionally as possible so new midi-devices can be quickly added without implementing
/// too much logic. Ideally 
/// 
/// </remarks>
public sealed class CommandTriggerCombination
{
    public CommandTriggerCombination(Action<int> indexAction, CompatibleMidiDevice.InputModes requiredInputMode, ButtonRange[] keyRanges,
                                     ExecutesAt executesAt)
    {
        _keyRanges = keyRanges;
        _requiredInputMode = requiredInputMode;
        _executesAt = executesAt;
        _indexAction = indexAction;
    }

    public CommandTriggerCombination(Action<int[]> action, CompatibleMidiDevice.InputModes requiredInputMode, ButtonRange[] keyRanges, ExecutesAt executesAt)
    {
        _keyRanges = keyRanges;
        _requiredInputMode = requiredInputMode;
        _executesAt = executesAt;
        _indicesAction = action;
    }

    public CommandTriggerCombination(Action<int, float> action, CompatibleMidiDevice.InputModes requiredInputMode, ButtonRange[] keyRanges,
                                     ExecutesAt executesAt)
    {
        _keyRanges = keyRanges;
        _requiredInputMode = requiredInputMode;
        _executesAt = executesAt;
        _controllerValueUpdateAction = action;
    }

    public CommandTriggerCombination(Action action, CompatibleMidiDevice.InputModes requiredInputMode, ButtonRange[] keyRanges, ExecutesAt executesAt)
    {
        _keyRanges = keyRanges;
        _requiredInputMode = requiredInputMode;
        _executesAt = executesAt;
        _actionWithoutParameters = action;
    }

    public override string ToString()
    {
        var keys = string.Join(", ", _keyRanges);
        var mode = _requiredInputMode is CompatibleMidiDevice.InputModes.None or CompatibleMidiDevice.InputModes.Default  
                       ? "" 
                       : "+ " +  _requiredInputMode;
        
        if(_indexAction != null )
            return $"{mode}{keys} -> IndexAction {_indexAction.Method.Name}";
        
        if(_indicesAction != null )
            return $"{mode}{keys} -> IndicesAction {_indicesAction.Method.Name}";
        
        if(_actionWithoutParameters != null )
            return $"{mode}{keys} -> ActionWithoutParameters {_actionWithoutParameters.Method.Name}";

        if(_controllerValueUpdateAction != null )
            return $"{mode}{keys} -> ControllerValueUpdateAction {_controllerValueUpdateAction.Method.Name}";

        
        return "Undefined CommandTriggerAction";
    }

    public void InvokeMatchingButtonCommands(List<ButtonSignal> buttonSignals, CompatibleMidiDevice.InputModes activeMode,
                                             CompatibleMidiDevice.InputModes releasedMode)
    {
        UpdateMatchingRangeIndices(buttonSignals);

        if (_executesAt == ExecutesAt.ModeButtonReleased)
        {
            if (releasedMode != _requiredInputMode)
                return;

            if (_activatedIndices.Count <= 0)
                return;
            
            LogMidiDebug($"Invoking {this} with index {_activatedIndices[0]} (ModeButtonReleased)");
            _indexAction?.Invoke(_activatedIndices[0]);
            _indicesAction?.Invoke(_activatedIndices.ToArray());
            return;
        }

        if (_requiredInputMode != CompatibleMidiDevice.InputModes.None && activeMode != _requiredInputMode)
            return;

        switch (_executesAt)
        {
            case ExecutesAt.SingleRangeButtonPressed:
            {
                if (_holdIndices.Count != 0 || _justPressedIndices.Count <= 0)
                    return;
                
                LogMidiDebug($"Invoking {this} with index {_justPressedIndices[0]} (SingleRangeButtonPressed)");
                _indexAction?.Invoke(_justPressedIndices[0]);
                _indicesAction?.Invoke(_activatedIndices.ToArray());
                return;
            }

            case ExecutesAt.SingleActionButtonPressed:
            {
                if (buttonSignals.Count == 1
                    && _keyRanges[0].IncludesButtonIndex(buttonSignals[0].ButtonId)
                    && buttonSignals[0].State == ButtonSignal.States.JustPressed
                   )
                {
                    LogMidiDebug($"Invoking {this} (SingleActionButtonPressed)");
                    _actionWithoutParameters?.Invoke();
                }

                return;
            }
                
            case ExecutesAt.AllCombinedButtonsReleased:
            {
                if (_releasedIndices.Count > 1 && _justPressedIndices.Count == 0 && _holdIndices.Count == 0)
                {
                    LogMidiDebug($"Invoking {this} with {_activatedIndices.Count} indices (AllCombinedButtonsReleased)");
                    _indicesAction?.Invoke(_activatedIndices.ToArray());
                }

                break;
            }
        }
    }

    public void InvokeMatchingControlCommands(IEnumerable<ControlChangeSignal> controlSignals, CompatibleMidiDevice.InputModes activeMode)
    {
        if (_requiredInputMode != CompatibleMidiDevice.InputModes.None && activeMode != _requiredInputMode)
            return;

        if (_executesAt != ExecutesAt.ControllerChange)
            return;

        foreach (var signal in controlSignals)
        {
            foreach (var range in _keyRanges)
            {
                if (!range.IncludesButtonIndex(signal.ControllerId))
                    continue;

                var mappedIndex = range.GetMappedIndex(signal.ControllerId);
                // Note: Debug log removed - too verbose for continuous controllers
                _controllerValueUpdateAction.Invoke(mappedIndex, signal.ControllerValue);
            }

            // if (_holdIndices.Count == 0 && _justPressedIndices.Count > 0)
            // {
            //     _indexAction?.Invoke(_justPressedIndices[0]);
            //     _indicesAction?.Invoke(_activatedIndices.ToArray());
            // }
        }
    }

    private void UpdateMatchingRangeIndices(List<ButtonSignal> buttonSignals)
    {
        _releasedIndices.Clear();
        _justPressedIndices.Clear();
        _holdIndices.Clear();
        _activatedIndices.Clear();

        if (buttonSignals.Count == 0)
            return;

        foreach (var range in _keyRanges)
        {
            foreach (var s in buttonSignals)
            {
                var includes = range.IncludesButtonIndex(s.ButtonId);
                if (!includes)
                    continue;
                    
                var mappedIndex = range.GetMappedIndex(s.ButtonId);

                switch (s.State)
                {
                    case ButtonSignal.States.JustPressed:
                        _justPressedIndices.Add(mappedIndex);
                        break;
                    case ButtonSignal.States.Released:
                        _releasedIndices.Add(mappedIndex);
                        break;
                    case ButtonSignal.States.Hold:
                        _holdIndices.Add(mappedIndex);
                        break;
                }

                _activatedIndices.Add(mappedIndex);
            }
        }
    }

    public enum ExecutesAt
    {
        SingleRangeButtonPressed,
        AllCombinedButtonsReleased,
        ModeButtonReleased,
        SingleActionButtonPressed,
        ControllerChange
    }

    // ReSharper disable InconsistentNaming
    private static readonly List<int> _activatedIndices = new(10);
    private static readonly List<int> _releasedIndices = new(10);
    private static readonly List<int> _justPressedIndices = new(10);
    private static readonly List<int> _holdIndices = new(10);

    private readonly ButtonRange[] _keyRanges;
    private readonly CompatibleMidiDevice.InputModes _requiredInputMode;
    private readonly ExecutesAt _executesAt;
    private readonly Action _actionWithoutParameters;
    private readonly Action<int> _indexAction;
    private readonly Action<int[]> _indicesAction;
    private readonly Action<int, float> _controllerValueUpdateAction;
    
    /// <summary>
    /// Gets the required input mode for this command trigger combination.
    /// </summary>
    public CompatibleMidiDevice.InputModes RequiredInputMode => _requiredInputMode;

    /// <summary>
    /// Logs a debug message if MIDI debug logging is enabled in settings.
    /// </summary>
    private static void LogMidiDebug(string message)
    {
        if (UserSettings.Config.EnableMidiDebugLogging)
            Log.Debug(message);
    }
}