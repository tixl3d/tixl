#nullable enable
using System;
using System.Collections.Generic;

namespace T3.Core.Operator.Interfaces;

/// <summary>
/// An interface that can be used by operators that need to generate selection lists
/// on runtime (e.g. input sources that are only available after startup)
///
/// This interface can then be activated by adding a string input with usage "custom input"
/// in the parameter settings.
/// </summary>
public interface ICustomDropdownHolder
{
    string? GetValueForInput(Guid inputId);
    IEnumerable<string> GetOptionsForInput(Guid inputId);
    void HandleResultForInput(Guid inputId, string? selected, bool isAListItem);
}