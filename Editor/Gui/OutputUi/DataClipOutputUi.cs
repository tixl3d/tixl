using T3.Core.DataTypes.DataSet;
using T3.Core.Operator.Slots;

namespace T3.Editor.Gui.OutputUi;

/// <summary>
/// Output-side preview for <see cref="DataClip"/> slots — unwraps the clip's underlying
/// <see cref="DataSet"/> and renders it via the same canvas used by
/// <see cref="DataSetOutputUi"/>. Lets the user inspect a DataClip on the graph the
/// same way they inspect a raw DataSet, without needing to manually pluck <c>.Set</c>
/// out into a separate debug op.
/// </summary>
internal sealed class DataClipOutputUi : OutputUi<DataClip>
{
    public override IOutputUi Clone()
    {
        return new DataClipOutputUi()
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size,
                   };
    }

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<DataClip> clipSlot)
            return;

        var dataSet = clipSlot.Value?.Set;
        if (dataSet == null)
            return;

        _canvas.Draw(dataSet);
    }

    private readonly DataSetViewCanvas _canvas = new();
}
