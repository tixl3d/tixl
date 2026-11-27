using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.VectorInputs;

internal sealed class FloatInputUi : FloatVectorInputValueUi<float>
{
    public FloatInputUi() : base(1) { }
        
    public override IInputUi Clone()
    {
        return CloneWithType<FloatInputUi>();
    }
        
    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref float value, bool readOnly)
    {
        FloatComponents[0] = value;
        GetDrawRange(out var min, out var max, out var scale, out var clampMin, out var clampMax);
        var inputEditState = VectorValueEdit.Draw(FloatComponents, min, max, scale, clampMin, clampMax, 0, Format);
        if (readOnly)
            return InputEditStateFlags.Nothing;
            
        value = FloatComponents[0];
        return inputEditState;
    }
        
    public InputEditStateFlags DrawEditControl(ref float value)
    {
        GetDrawRange(out var min, out var max, out var scale, out var clampMin, out var clampMax);
        return SingleValueEdit.Draw(ref value, -Vector2.UnitX, min, max, clampMin, clampMax, scale);
    }
        
    public override void ApplyValueToAnimation(IInputSlot inputSlot, InputValue inputValue, Animator animator, double time)
    {
        if (inputValue is not InputValue<float> typedInputValue)
            return;

        if (!animator.TryGetCurvesForInputSlot(inputSlot, out var curves))
            return;
        
        FloatComponents[0] = typedInputValue.Value;
        Curve.UpdateCurveValues(curves, time, FloatComponents);
    }        
}