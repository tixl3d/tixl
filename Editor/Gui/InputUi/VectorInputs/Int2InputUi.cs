using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.VectorInputs;

internal sealed class Int2InputUi : IntVectorInputValueUi<Int2>
{
    public override bool IsAnimatable => true;

    public Int2InputUi() : base(2)
    {
    }

    public override IInputUi Clone()
    {
        return CloneWithType<Int2InputUi>();
    }

    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref Int2 int3Value, bool readOnly)
    {
        IntComponents[0] = int3Value.Width;
        IntComponents[1] = int3Value.Height;

        var inputEditState = VectorValueEdit.Draw(IntComponents, Min, Max, Scale, ClampMin, ClampMax);
        if(!readOnly) 
            int3Value = new Int2(IntComponents[0], IntComponents[1]);

        return inputEditState;
    }


    public override void ApplyValueToAnimation(IInputSlot inputSlot, InputValue inputValue, Animator animator, double time)
    {
        if (inputValue is not InputValue<Int2> typedInputValue)
            return;

        if (!animator.TryGetCurvesForInputSlot(inputSlot, out var curves))
            return;

        IntComponents[0] = typedInputValue.Value.Width;
        IntComponents[1] = typedInputValue.Value.Height;
        Curve.UpdateCurveValues(curves, time, IntComponents);
    }
}