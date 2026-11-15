using System.Collections.Generic;

namespace Lib.Utils;

/// <summary>
/// The context variable names a <c>Get*Var</c> op offers in its name dropdown. Names are collected during the op's
/// update, where scoped variables are still set, but only on the update after the dropdown asked for them, so
/// unselected ops neither enumerate the context nor allocate.
/// </summary>
internal sealed class VariableNameOptions
{
    public List<string> Names { get; } = new();

    /** Called by the dropdown, which draws every frame while the op is selected, so the list stays current. */
    public void Request()
    {
        _isRequested = true;
    }

    public void CollectIfRequested<T>(Dictionary<string, T> variables)
    {
        if (!_isRequested)
            return;

        _isRequested = false;
        Names.Clear();
        foreach (var name in variables.Keys)
        {
            Names.Add(name);
        }
    }

    /// <summary>For variable types that share <c>ObjectVariables</c>: lists only the names holding a <typeparamref name="TValue"/>.</summary>
    public void CollectOfTypeIfRequested<TValue>(Dictionary<string, object> variables)
    {
        if (!_isRequested)
            return;

        _isRequested = false;
        Names.Clear();
        foreach (var pair in variables)
        {
            if (pair.Value is TValue)
                Names.Add(pair.Key);
        }
    }

    private bool _isRequested;
}
