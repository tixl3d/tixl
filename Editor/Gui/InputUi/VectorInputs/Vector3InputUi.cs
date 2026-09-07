#nullable enable

using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.VectorInputs;

internal sealed class Vector3InputUi : FloatVectorInputValueUi<Vector3>
{
    public Vector3InputUi() : base(3) { }

    public override IInputUi Clone()
    {
        return CloneWithType<Vector3InputUi>();
    }

    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref Vector3 float3Value, bool readOnly)
    {
        float3Value.CopyTo(FloatComponents);
        var inputEditState = VectorValueEdit.Draw(FloatComponents, Min, Max, Scale, ClampMin, ClampMax, 0,Format);
        if (readOnly)
            return InputEditStateFlags.Nothing;
            
        float3Value = new Vector3(FloatComponents[0], FloatComponents[1], FloatComponents[2]);
        return inputEditState;
    }
        
    public override void ApplyValueToAnimation(IInputSlot inputSlot, InputValue inputValue, Animator animator, double time)
    {
        if (inputValue is not InputValue<Vector3> typedInputValue)
            return;
        
        if (!animator.TryGetCurvesForInputSlot(inputSlot, out var curves))
        {
            Log.Warning("Can't find vec3 animation curves?");
            return;
        }
        
        typedInputValue.Value.CopyTo(FloatComponents);
        Curve.UpdateCurveValues(curves, time, FloatComponents);
    }
}