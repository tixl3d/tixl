using T3.Core.Operator.Slots;

namespace T3.Core.Operator.Interfaces;

/// <summary>
/// Implemented by camera operators whose aim the transform-gizmo layer should visualize: while
/// such an instance is selected (and it also implements <see cref="ITransformable"/>), the gizmo
/// layer draws a line to the aim target and the frame the camera sees, and the target becomes
/// draggable. The drag writes through <see cref="TargetInput"/>.
/// </summary>
public interface ICameraViewGizmo
{
    /// <summary>The Vector3 input the gizmo's target drag writes to.</summary>
    IInputSlot TargetInput { get; }

    /// <summary>Whether the gizmo layer draws the beam styling (glowing beam to the landing frame
    /// plus corner fan) on this camera's view gizmo. Off falls back to plain lines. Default on.</summary>
    bool BeamsEnabled => true;
}
