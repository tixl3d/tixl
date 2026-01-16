using System.Reflection;
using ImGuiNET;

namespace T3.Editor.Gui.UiHelpers;


internal record struct State<T>(Action<T> Enter=null, 
                             Action<T> Update=null, 
                             Action<T> Exit=null);


/// <summary>
/// The state machine is a very bare-bones (no hierarchy or events) implementation
/// of a state machine that handles activation of <see cref="State"/>s. There can only be one state active.
/// Most of the update interaction is done in State.Update() overrides.
/// </summary>
internal sealed class StateMachine<T>
{
    public StateMachine( Type statesClass,State<T> defaultState)
    {
        _currentState = defaultState;
        _states = statesClass;
    }

    public void UpdateAfterDraw(T c)
    {
        _currentState.Update(c);
    }

    internal void SetState(State<T> newState, T context)
    {
        _currentState.Exit(context);
        _currentState = newState;
        _stateEnterTime = ImGui.GetTime();
        
        //var activeCommand = context.MacroCommand != null ? "ActiveCmd:" + context.MacroCommand : string.Empty;
        //Log.Debug($"--> {GetMatchingStateFieldName(typeof( GraphStates), _currentState)}  {activeCommand}   {context.ActiveItem}");
        _currentState.Enter(context);
    }

    /// <summary>
    /// Sadly, since states are defined as fields, we need to use reflection to infer their short names... 
    /// </summary>
    private string GetStateName(State<T> state)
    {

        var fields = _states.GetFields(  BindingFlags.NonPublic|BindingFlags.Static);
    
        foreach (var field in fields)
        {
            if (field.FieldType != typeof(State<T>))
                continue;
            
            var fieldValue = (State<T>)field.GetValue(null)!;
            if (fieldValue.Equals(state))
            {
                return field.Name;
            }
        }

        return string.Empty; 
    }
    
    private State<T> _currentState;
    public float StateTime => (float) (ImGui.GetTime() - _stateEnterTime);
    private double _stateEnterTime;
    private readonly Type _states;
    public State<T> CurrentState => _currentState;

    public override string ToString()
    {
        return $"{_states.Name} [{GetStateName(_currentState)}]";
    }
}