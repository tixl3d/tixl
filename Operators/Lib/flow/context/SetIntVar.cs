using T3.Core.Utils;

namespace Lib.flow.context;

[Guid("7953f704-ebee-498b-8bdd-a2c201dfe278")]
internal sealed class SetIntVar : Instance<SetIntVar>
{
    [Output(Guid = "7d806685-4678-4dfc-9dbc-36fdfa0c7a59")]
    public readonly Slot<Command> Output = new();

    public SetIntVar()
    {
        Output.UpdateAction += Update;
    }
        
        
    private int ClampForEnum<T>(int i) where T:Enum 
    {
        return i.Clamp(0, Enum.GetValues(typeof(T)).Length - 1);
    }
        
    private void Update(EvaluationContext context)
    {
        var name = VariableName.GetValue(context);
        var newValue = Value.GetValue(context);
        var clearAfterExecution = ClearAfterExecution.GetValue(context);

        var logLevel =  LogLevel.GetEnumValue<LogLevels>(context);
            
        if (string.IsNullOrEmpty(name))
        {
            if((int)logLevel >= (int)LogLevels.Warnings) 
                Log.Warning($"Can't set variable with invalid name {name}", this);
                
            return;
        }

        if (SubGraph.HasInputConnections)
        {
            var hadPreviousValue = context.IntVariables.TryGetValue(name, out var previous);
            context.IntVariables[name] = newValue;

            SubGraph.GetValue(context);

            if (hadPreviousValue)
            {
                if ((int)logLevel >= (int)LogLevels.Changes)
                    Log.Debug($"Changing {name} from {previous} -> {newValue}", this);

                context.IntVariables[name] = previous;
            }
            else
            {
                if ((int)logLevel >= (int)LogLevels.AllUpdates)
                    Log.Debug($"Setting {name} to {newValue}", this);

                if(!clearAfterExecution)
                    context.IntVariables.Remove(name);
            }
        }
        else
        {
            context.IntVariables[name] = newValue;
        }
    }

        
    [Input(Guid = "662b8a63-58db-4c9e-b53a-7ece1f118e12")]
    public readonly InputSlot<Command> SubGraph = new();
        
    [Input(Guid = "bfd87742-aaf5-4fa8-b714-fd275de1c60d")]
    public readonly InputSlot<string> VariableName = new();
        
    [Input(Guid = "72DD0C80-8E95-474B-9AA5-D8292D0FF0DD")]
    public readonly InputSlot<int> Value = new();

    [Input(Guid = "4AB2A742-7F3F-4D96-B67E-73E14B4A8F47", MappedType = typeof(LogLevels))]
    public readonly InputSlot<int> LogLevel = new();

    [Input(Guid = "DA431996-4C4C-4CDC-9723-9116BBB5440C")]
    public readonly InputSlot<bool> ClearAfterExecution = new ();

    private enum LogLevels
    {
        None,
        Warnings,
        Changes,
        AllUpdates,
    }

        
}