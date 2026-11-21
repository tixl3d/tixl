#nullable enable
using System;

namespace T3.Core.Operator.Interfaces;

/// <summary>A problem with one input. <see cref="Line"/> is 1-based within the input's text, 0 when it isn't tied to a line.</summary>
public readonly record struct InputStatus(string Message, int Line = 0);

/// <summary>
/// Reports a problem with one input, e.g. a code snippet that doesn't compile, so the editor can mark that input and
/// show the message next to it instead of only on the operator.
/// </summary>
public interface IInputStatusProvider
{
    /// <returns>False when the input has no problem.</returns>
    bool TryGetInputStatus(Guid inputId, out InputStatus status);
}
