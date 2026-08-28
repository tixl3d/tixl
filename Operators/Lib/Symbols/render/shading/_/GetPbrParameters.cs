using T3.Core.Rendering.Material;

namespace Lib.render.shading.@_;

[Guid("ca4fe8c4-cf61-4196-84e4-d69dc8869a25")]
internal sealed class GetPbrParameters : Instance<GetPbrParameters>
{
    [Output(Guid = "3D2EBD10-2670-46B7-8F1A-9475A81A516D", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Buffer> PbrParameterBuffer = new();

    [Output(Guid = "7C3D08E2-85E2-442A-9196-0E946571DA5A", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<ShaderResourceView> AlbedoColorMap = new();

    [Output(Guid = "B6BAD091-131A-49F3-8ACC-7011A4919435", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<ShaderResourceView> EmissiveColorMap = new();

    [Output(Guid = "B48F674B-2B5A-4501-992E-26E07A247DDF", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<ShaderResourceView> RoughnessMetallicOcclusionMap = new();

    [Output(Guid = "B815BF49-0E44-4DB0-BF32-9C7D188D6AA2", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<ShaderResourceView> NormalMap = new();

    [Output(Guid = "671F198D-4173-4FE9-AF5A-FCD5D0A71895", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<ShaderResourceView> BrdfLookupMap = new();
        
    [Output(Guid = "AB644673-9EAA-4CEC-9663-FBFDC445D112", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Texture2D> PrefilteredSpecularMap = new();
        
    public GetPbrParameters()
    {
        PbrParameterBuffer.UpdateAction += UpdatePbrParameterBuffer;
        AlbedoColorMap.UpdateAction += UpdateAlbedoColorMap;
        EmissiveColorMap.UpdateAction += UpdateEmissiveColorMap;
        RoughnessMetallicOcclusionMap.UpdateAction += UpdateRoughnessMetallicOcclusionMap;
        NormalMap.UpdateAction += UpdateNormalMap;
        BrdfLookupMap.UpdateAction += UpdateBrdfLookupMap;
        PrefilteredSpecularMap.UpdateAction += UpdatePrefilteredSpecularMap;
    }

        
    private void UpdatePbrParameterBuffer(EvaluationContext context) => PbrParameterBuffer.Value = context.PbrMaterial.ParameterBuffer;
    private void UpdateAlbedoColorMap(EvaluationContext context) => AlbedoColorMap.Value = context.PbrMaterial.AlbedoMapSrv;
    private void UpdateEmissiveColorMap(EvaluationContext context) => EmissiveColorMap.Value = context.PbrMaterial.EmissiveMapSrv;
    private void UpdateRoughnessMetallicOcclusionMap(EvaluationContext context) => RoughnessMetallicOcclusionMap.Value = context.PbrMaterial.RoughnessMetallicOcclusionSrv;
    private void UpdateNormalMap(EvaluationContext context) => NormalMap.Value = context.PbrMaterial.NormalSrv;
    private void UpdateBrdfLookupMap(EvaluationContext context) => BrdfLookupMap.Value = PbrContextSettings.PbrLookUpTextureSrv;

    private void UpdatePrefilteredSpecularMap(EvaluationContext context)
    {
        context.ContextTextures.TryGetValue(PbrContextSettings.PrefilteredSpecularId, out var texture);
        PrefilteredSpecularMap.Value = texture;
    }
}