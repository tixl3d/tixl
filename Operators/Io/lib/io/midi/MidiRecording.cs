using T3.Core.DataTypes.DataSet;
using T3.Core.Utils;

namespace Lib.io.midi;

[Guid("2d1c9633-b66e-4958-913c-116ae36963a5")]
internal sealed class MidiRecording : Instance<MidiRecording>
{
    [Output(Guid = "f89b5a87-a757-4f10-aa13-396c2cd9829b")]
    public readonly Slot<DataSet> DataSet = new();
        
    public MidiRecording()
    {
        DataSet.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (T3.Core.DataTypes.DataSet.DataRecording.ActiveRecordingSet == null)
            return;
            
        var wasResetTriggered = MathUtils.WasTriggered(ResetTrigger.GetValue(context), ref _resetTrigger);
        if (wasResetTriggered)
        {
            T3.Core.DataTypes.DataSet.DataRecording.ActiveRecordingSet.Clear();
        }

        DataSet.Value = T3.Core.DataTypes.DataSet.DataRecording.ActiveRecordingSet;
    }

    [Input(Guid = "9b844a51-d108-426e-a264-d570d30031c6")]
    public readonly InputSlot<bool> ResetTrigger = new();

    private bool _resetTrigger;
}