namespace Types.Routing;

// Passes T3.Core.Operator.GizmoVisibility through the .t3 connection; no C# evaluator is needed.
[Guid("bf099cc0-025d-44e5-a97b-46ae404442ae")]
public sealed class RerouteGizmoVisibility : Instance<RerouteGizmoVisibility>, IRerouteNode
{
    [Output(Guid = "06338380-a84a-42c4-a340-bfd72c073534")]
    public readonly Slot<T3.Core.Operator.GizmoVisibility> Output = new();

    [Input(Guid = "6176e31b-23ac-4b90-850f-8f0edfe82ad5")]
    public readonly InputSlot<T3.Core.Operator.GizmoVisibility> Input = new();
}
