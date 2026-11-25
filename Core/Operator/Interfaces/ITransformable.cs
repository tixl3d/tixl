using System;
using T3.Core.Operator.Slots;

namespace T3.Core.Operator.Interfaces;

/// <summary>
/// Allows operators to be controlled by a 3d transform gizmo
/// </summary>
public interface ITransformable
{
    IInputSlot TranslationInput { get; }
    IInputSlot RotationInput { get;  }
    IInputSlot ScaleInput { get;  }

    /// <summary>
    /// A point (Vector3) the operator aims at, such as a light's or camera's target. When set, the rotate gizmo
    /// swings this point around <see cref="TranslationInput"/> instead of editing <see cref="RotationInput"/>,
    /// and moving the operator leaves it in place.
    /// </summary>
    IInputSlot AimTargetInput => null;
        
    Action<Instance, EvaluationContext> TransformCallback { get; set; }
}