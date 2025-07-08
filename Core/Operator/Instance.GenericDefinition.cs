using System;

namespace T3.Core.Operator;

public class Instance<T> : Instance where T : Instance<T>, new()
{
    /// <summary>
    /// this intended to be a different symbol per-type
    /// this is set via reflection in the Symbol ApplyInstanceType method
    /// this way, an instance type can be associated with a symbol within its constructor
    /// or prior to the instance being created in the first place
    /// </summary>
    // ReSharper disable once StaticMemberInGenericType
    // ReSharper disable once MemberCanBePrivate.Global
    // ReSharper disable once FieldCanBeMadeReadOnly.Global
    private protected static Symbol StaticSymbol = null!;
        
    public sealed override Type Type => typeof(T);
    public sealed override Symbol Symbol => StaticSymbol;

    protected Instance()
    {
        SetupInputAndOutputsFromType();
    }
}