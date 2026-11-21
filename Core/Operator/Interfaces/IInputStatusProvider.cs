#nullable enable
using System;

namespace T3.Core.Operator.Interfaces;

/// <summary>
/// Reports a problem with one input, e.g. a code snippet that doesn't compile, so the editor can mark that input and
/// show the message next to it instead of only on the operator.
/// </summary>
public interface IInputStatusProvider
{
    /// <returns>A message when the input has a problem, otherwise null.</returns>
    string? GetInputStatus(Guid inputId);
}
