namespace T3.Core.Operator.Interfaces;

/// <summary>
/// Provides a special hook to combine compound operators with an update method.
/// </summary>
/// <remarks>This can be useful to invoke special actions before using the update for the the inner Output slots.</remarks>
public interface ICompoundWithUpdate
{
    // void RegisterOutputUpdateAction(ISlot slot, Action<EvaluationContext> connectedUpdate);
}