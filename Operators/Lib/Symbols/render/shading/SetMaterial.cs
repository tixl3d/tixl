using T3.Core.Rendering.Material;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.render.shading;

[Guid("0ed2bee3-641f-4b08-8685-df1506e9af3c")]
internal sealed class SetMaterial : Instance<SetMaterial>
{
    [Output(Guid = "d80e1028-a48d-4171-8c8c-e6856bd2143d")]
    public readonly Slot<Command> Output = new();

    [Output(Guid = "51612678-3573-4D40-A423-9E23FC72EA44")]
    public readonly Slot<PbrMaterial> Reference = new();
        
    public SetMaterial()
    {
        Output.UpdateAction += Update;
        Reference.UpdateAction += Update;
    }
        
    private void Update(EvaluationContext context)
    {
        var parameterBufferNeedsUpdate = BaseColor.DirtyFlag.IsDirty ||
                                         EmissiveColor.DirtyFlag.IsDirty ||
                                         Roughness.DirtyFlag.IsDirty ||
                                         Specular.DirtyFlag.IsDirty ||
                                         Metal.DirtyFlag.IsDirty ||
                                         _pbrMaterial == null;
            
        _pbrMaterial ??= new PbrMaterial();
            
        if (parameterBufferNeedsUpdate)
        {
            _pbrMaterial.Parameters.BaseColor = BaseColor.GetValue(context);
            _pbrMaterial.Parameters.EmissiveColor = EmissiveColor.GetValue(context);
            _pbrMaterial.Parameters.Roughness = Roughness.GetValue(context);
            _pbrMaterial.Parameters.Specular = Specular.GetValue(context);
            _pbrMaterial.Parameters.Metal = Metal.GetValue(context);

            _pbrMaterial.UpdateParameterBuffer();
        }

        UpdateSrv(BaseColorMap, context, ref _pbrMaterial.AlbedoMapSrv, PbrMaterial.DefaultAlbedoColorSrv);
        UpdateSrv(NormalMap, context, ref _pbrMaterial.NormalSrv, PbrMaterial.DefaultNormalSrv);
        UpdateSrv(EmissiveColorMap, context, ref _pbrMaterial.EmissiveMapSrv, PbrMaterial.DefaultEmissiveColorSrv);
        UpdateSrv(RoughnessMetallicOcclusionMap, context, ref _pbrMaterial.RoughnessMetallicOcclusionSrv, PbrMaterial.DefaultRoughnessMetallicOcclusionSrv);

        var previousMaterial = context.PbrMaterial;
        context.PbrMaterial = _pbrMaterial;
            
        var isValid = _pbrMaterial != null;

        if (isValid)
        {
            _pbrMaterial.Name = MaterialId.GetValue(context);
            context.Materials.Add(_pbrMaterial);
        } 

        SubTree.GetValue(context);
            
        if(isValid)
            context.Materials.RemoveAt(context.Materials.Count - 1);
            
        // TODO: replace with stack
        context.PbrMaterial = previousMaterial;

        Reference.Value = _pbrMaterial;
        Reference.DirtyFlag.Clear();
    }

    private void UpdateSrv(InputSlot<Texture2D> textureInputSlot, EvaluationContext context, ref ShaderResourceView currentSrv, ShaderResourceView defaultSrv)
    {
        var textureChanged = textureInputSlot.DirtyFlag.IsDirty;
        var needsUpdate = textureChanged || currentSrv == null;

        if (!needsUpdate)
            return;

        if(currentSrv != defaultSrv)
            Utilities.Dispose(ref currentSrv);

        var changedTexture = textureInputSlot.GetValue(context);

        if (changedTexture == null || changedTexture.IsDisposed)
        {
            currentSrv = defaultSrv;
            return;
        }

        try
        {
            var srv = new ShaderResourceView(ResourceManager.Device, changedTexture);
            currentSrv = srv;
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to create SRV for {textureInputSlot.Input.Name} texture {e.Message}", this);
            currentSrv = defaultSrv;
        }
    }

    private PbrMaterial _pbrMaterial;

        
    [Input(Guid = "2a585a23-b60c-4c8b-8cfa-9ab2a8b04c7a")]
    public readonly InputSlot<Command> SubTree = new();

    [Input(Guid = "9FF5ADE2-CFA7-4616-AD89-356F3248E04F")]
    public readonly InputSlot<Vector4> BaseColor = new();

    [Input(Guid = "0EB51DF1-570A-4AC6-BAE6-5E03D6E66CEB")]
    public readonly InputSlot<Texture2D> BaseColorMap = new();

    [Input(Guid = "2C91C306-1815-4B22-A70F-746232F024D7")]
    public readonly InputSlot<Vector4> EmissiveColor = new();

    [Input(Guid = "6D859756-0243-42C5-973D-6DE2DCDC5609")]
    public readonly InputSlot<Texture2D> EmissiveColorMap = new();
        
    [Input(Guid = "9D5CA726-28B0-4F3D-A5AA-F0AE3E2043A9")]
    public readonly InputSlot<float> Specular = new();

    [Input(Guid = "E14DCC43-7C18-4ED7-AD39-DFEAB10E3D33")]
    public readonly InputSlot<float> Roughness = new();

    [Input(Guid = "108FF533-F205-4989-B894-ACF48E3CC1DB")]
    public readonly InputSlot<float> Metal = new();

    [Input(Guid = "600BBC24-6B3A-4AC4-9CEB-702E71C839E9")]
    public readonly InputSlot<Texture2D> NormalMap = new();

    [Input(Guid = "C8003FBD-C6CE-440C-9F1F-6B15B5EE5274")]
    public readonly InputSlot<Texture2D> RoughnessMetallicOcclusionMap = new();

    [Input(Guid = "71E289F0-382B-4D0F-A2E0-701C7019A360")]
    public readonly InputSlot<string> MaterialId = new();


}