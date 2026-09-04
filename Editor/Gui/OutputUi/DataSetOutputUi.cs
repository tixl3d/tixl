using T3.Core.DataTypes.DataSet;
using T3.Core.Operator.Slots;

namespace T3.Editor.Gui.OutputUi;

internal sealed class DataSetOutputUi : OutputUi<DataSet>
{
    public override IOutputUi Clone()
    {
        return new DataSetOutputUi()
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }


    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<DataSet> dataSetSlot)
            return;

        //DrawDataSet(dataSetSlot.Value);
        _canvas.Draw(dataSetSlot.Value);
    }

    private readonly DataSetViewCanvas _canvas = new();
}