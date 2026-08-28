using T3.Core.Utils;

namespace Lib.render._dx11.fxsetup;

[Guid("179093f6-d3ef-43bc-a5af-1df2379ec081")]
internal sealed class SwitchBlendState : Instance<SwitchBlendState>
{
    [Output(Guid = "0608B46B-4778-4F95-B688-3A749F9664AE")]
    public readonly Slot<BlendState> Output = new();

    public SwitchBlendState()
    {
        Output.UpdateAction += Update;
    }

    private BlendState[] _blendStates = new BlendState[0];
    private void Update(EvaluationContext context)
    {
        // var blendStates = BlendStates.GetCollectedTypedInputs();
        BlendStates.GetValues(ref _blendStates, context);
        var index = Index.GetValue(context);

        if (_blendStates.Length == 0 || index == -1)
        {
            return;
        }
            
        Output.Value = _blendStates[index.Mod(_blendStates.Length)];
    }

    [Input(Guid = "A737BB60-D98B-4405-914C-7DF91A58D8BC")]
    public readonly MultiInputSlot<BlendState> BlendStates = new();
        
    [Input(Guid = "232a10e8-0357-4adc-935b-9cb1b7938730")]
    public readonly InputSlot<int> Index = new();
}