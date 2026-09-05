using System;
using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using T3.Core.Logging;
using T3.Core.Resource;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace T3.Core.Rendering.Material;

/// <summary>
/// Contains all settings and resource views to initialize a draw call with this material
/// </summary>
public class PbrMaterial: IDisposable
{
    public string Name;
    public ShaderResourceView AlbedoMapSrv;
    public ShaderResourceView EmissiveMapSrv;
    public ShaderResourceView RoughnessMetallicOcclusionSrv;
    public ShaderResourceView NormalSrv;

    public PbrParameters Parameters;
    public Buffer ParameterBuffer;

    public void UpdateParameterBuffer()
    {
        ResourceManager.SetupConstBuffer(Parameters, ref ParameterBuffer);
    }

    [StructLayout(LayoutKind.Explicit, Size = Stride)]
    public struct PbrParameters
    {
        [FieldOffset(0)]
        public Vector4 BaseColor;

        [FieldOffset(4 * 4)]
        public Vector4 EmissiveColor;

        [FieldOffset(8 * 4)]
        public float Roughness;

        [FieldOffset(9 * 4)]
        public float Specular;

        [FieldOffset(10 * 4)]
        public float Metal;

        [FieldOffset(11 * 4)]
        private float __padding;

        public const int Stride = 12 * 4;
    }


    public static PbrMaterial CreateDefault()
    {
        _defaultRmoTexture = TextureUtils.CreateColorTexture(new Vector4(0f, 0, 1, 0));
        _defaultNormalTexture = TextureUtils.CreateColorTexture(new Vector4(0.5f, 0.5f, 1, 0));

        BlackPixelSrv = new ShaderResourceView(ResourceManager.Device, PbrContextSettings.BlackPixelTexture);
        
        WhitePixelSrv = new ShaderResourceView(ResourceManager.Device, PbrContextSettings.WhitePixelTexture);
        DefaultAlbedoColorSrv = WhitePixelSrv;
        
        DefaultEmissiveColorSrv = new ShaderResourceView(ResourceManager.Device, PbrContextSettings.WhitePixelTexture);
        DefaultRoughnessMetallicOcclusionSrv = new ShaderResourceView(ResourceManager.Device, _defaultRmoTexture);
        DefaultNormalSrv = new ShaderResourceView(ResourceManager.Device, _defaultNormalTexture);
        
        var newMaterial= new PbrMaterial()
                             {
                                 Name = "Default",
                                 Parameters = _defaultParameters,
                                 AlbedoMapSrv = DefaultAlbedoColorSrv,
                                 EmissiveMapSrv = DefaultEmissiveColorSrv,
                                 RoughnessMetallicOcclusionSrv = DefaultRoughnessMetallicOcclusionSrv,
                                 NormalSrv = DefaultNormalSrv,

                             };
        newMaterial.UpdateParameterBuffer();
        return newMaterial;
    }

    public static ShaderResourceView BlackPixelSrv { get; set; }

    private static readonly PbrParameters _defaultParameters = new()
                                                                   {
                                                                       BaseColor = Vector4.One,
                                                                       EmissiveColor = new Vector4(0, 0, 0, 1),
                                                                       Roughness = 0.5f,
                                                                       Specular = 10,
                                                                       Metal = 0
                                                                   };
    public static ShaderResourceView DefaultEmissiveColorSrv;
    public static ShaderResourceView DefaultAlbedoColorSrv;
    public static ShaderResourceView WhitePixelSrv;
    public static ShaderResourceView DefaultRoughnessMetallicOcclusionSrv;
    public static ShaderResourceView DefaultNormalSrv;
    
    public static Texture2D _defaultRmoTexture;
    public static Texture2D _defaultNormalTexture;
    
    public void Dispose() => Dispose(true);

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        DisposeUnlessSharedDefault(ref AlbedoMapSrv);
        DisposeUnlessSharedDefault(ref EmissiveMapSrv);
        DisposeUnlessSharedDefault(ref RoughnessMetallicOcclusionSrv);
        DisposeUnlessSharedDefault(ref NormalSrv);

        ParameterBuffer?.Dispose();
        ParameterBuffer = null;
    }

    /// <summary>
    /// The static default SRVs are shared across all materials and must survive individual disposal.
    /// Nulling the field keeps a second Dispose (e.g. via a material shared by several scene dispatches) harmless.
    /// </summary>
    private static void DisposeUnlessSharedDefault(ref ShaderResourceView srv)
    {
        if (srv != null
            && srv != DefaultAlbedoColorSrv
            && srv != DefaultEmissiveColorSrv
            && srv != DefaultRoughnessMetallicOcclusionSrv
            && srv != DefaultNormalSrv
            && srv != BlackPixelSrv
            && srv != WhitePixelSrv)
        {
            srv.Dispose();
        }

        srv = null;
    }
}