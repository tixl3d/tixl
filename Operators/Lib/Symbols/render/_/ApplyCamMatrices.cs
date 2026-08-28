using T3.Core.Utils.Geometry;

namespace Lib.render.@_;

[Guid("3dae14a8-3d0b-432f-b951-bdd7afd7e5f8")]
internal sealed class ApplyCamMatrices : Instance<ApplyCamMatrices>
{
    [Output(Guid = "bd6cd982-c99e-4366-906a-2f0280cf32de")]
    public readonly Slot<Command> Output = new();

    public ApplyCamMatrices()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var worldToCam = WorldToCamRows.GetValue(context).ToMatrixFromRows();
        var camToClipSpace = CamToClipSpaceRows.GetValue(context).ToMatrixFromRows();
        var aspect = camToClipSpace.M22 / camToClipSpace.M11;
        //Log.Debug($" M11: {camToClipSpace.M11:0.00}  M22: {camToClipSpace.M22:0.00} ", this);
            
        var x = 1;            
        worldToCam.M11 /= camToClipSpace.M11 / x;
        worldToCam.M22 /= camToClipSpace.M22 / x;

        var previousWorldTobject = context.ObjectToWorld;
            
        context.ObjectToWorld = Matrix4x4.Multiply(worldToCam, context.ObjectToWorld);
            
        Command.GetValue(context);
        context.ObjectToWorld = previousWorldTobject;
    }

    [Input(Guid = "aad90ca4-5085-4b90-bf58-b87bdc852d62")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "86FA7A95-C6DF-4BB8-A447-E92882A99037")]
    public readonly InputSlot<Vector4[]> WorldToCamRows = new();
        
    [Input(Guid = "6DD0EC68-5B73-4C19-8043-1CA91BBD0AF3")]
    public readonly InputSlot<Vector4[]> CamToClipSpaceRows = new();
        
}