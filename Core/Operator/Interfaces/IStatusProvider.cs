#nullable enable
namespace T3.Core.Operator.Interfaces;

/// <summary>
/// Provides provide a status information about a errors and warnings. 
/// </summary>
public interface IStatusProvider
{
    StatusLevel GetStatusLevel();
    string? GetStatusMessage();

    enum StatusLevel
    {
        Undefined,
        Success,
        Tip,
        Notice,
        Warning,
        Error,
    }
}