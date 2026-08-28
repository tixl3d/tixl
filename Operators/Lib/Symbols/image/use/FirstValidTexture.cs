namespace Lib.image.use;

[Guid("db73b47d-3d42-4b17-b8fd-08b6f1286716")]
internal sealed class FirstValidTexture : Instance<FirstValidTexture>
{
    [Output(Guid = "3d3d2dbd-dadc-492d-bf03-b780b21e738e")]
    public readonly Slot<Texture2D> Output = new();
        
        
    public FirstValidTexture()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var foundSomethingValid = false;
            
        _complainedOnce |= Input.DirtyFlag.IsDirty;
            
        var connections = Input.GetCollectedTypedInputs();
        if (connections != null && connections.Count > 0)
        {
            for (int index = 0; index < connections.Count; index++)
            {
                var v = connections[index].GetValue(context);
                if (v != null)
                {
                    Output.Value = v;
                    foundSomethingValid = true;
                    break;
                }
            }
        }

        if (!foundSomethingValid && !_complainedOnce)
        {
            Log.Debug("No valid texture found", this);
            _complainedOnce = true;
        }
        
        Input.DirtyFlag.Clear();
    }

    private bool _complainedOnce;
        
    [Input(Guid = "1725F61D-44E5-4718-9331-F6520F105657")]
    public readonly MultiInputSlot<Texture2D> Input = new();
}