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
        
    Action<Instance, EvaluationContext> TransformCallback { get; set; }
}