using System;
using System.Collections.Generic;
using System.IO;
using SharpDX.Direct3D11;
using T3.Core.Compilation;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Core.UserData;
using PixelShader = T3.Core.DataTypes.PixelShader;
using Texture2D = T3.Core.DataTypes.Texture2D;
using VertexShader = T3.Core.DataTypes.VertexShader;

namespace T3.Core.Resource;

/// <summary>
/// A collection of rendering resource used across the T3 UI
/// </summary>
public static class SharedResources
{
    public static readonly string Directory = Path.Combine(RuntimeAssemblies.CoreDirectory, FileLocations.ResourcesSubfolder);
    public static readonly IResourcePackage ResourcePackage = new SharedResourceObject();
        
    static SharedResources()
    {
        ResourceManager.AddSharedResourceFolder(ResourcePackage, true);
    }

    public static void Initialize()
    {
        if (ShaderCompiler.Instance == null)
        {
            throw new Exception($"{nameof(ShaderCompiler)}.{nameof(ShaderCompiler.Instance)} not initialized");
        }
            
        _fullScreenVertexShaderResource = ResourceManager.CreateShaderResource<VertexShader>(@"dx11/fullscreen-texture.hlsl", null, () => "vsMain");
        _fullScreenPixelShaderResource = ResourceManager.CreateShaderResource<PixelShader>(@"dx11/fullscreen-texture.hlsl", null, () => "psMain");
            
        if (_fullScreenVertexShaderResource.Value == null)
        {
            throw new Exception($"{nameof(SharedResources)} Failed to load fullscreen vertex shader");
        }
            
        if (_fullScreenPixelShaderResource.Value == null)
        {
            throw new Exception($"{nameof(SharedResources)} Failed to load fullscreen pixel shader");
        }
            
        ViewWindowRasterizerState = new RasterizerState(ResourceManager.Device, new RasterizerStateDescription
                                                                                    {
                                                                                        FillMode = FillMode.Solid, // Wireframe
                                                                                        CullMode = CullMode.None,
                                                                                        IsFrontCounterClockwise = true,
                                                                                        DepthBias = 0,
                                                                                        DepthBiasClamp = 0,
                                                                                        SlopeScaledDepthBias = 0,
                                                                                        IsDepthClipEnabled = false,
                                                                                        IsScissorEnabled = default,
                                                                                        IsMultisampleEnabled = false,
                                                                                        IsAntialiasedLineEnabled = false
                                                                                    }); 
            
        _viewWindowDefaultTexture = ResourceManager.CreateTextureResource(@"images/editor/t3-background.png", null);
        _t3logoAlphaTexture = ResourceManager.CreateTextureResource(@"images/t3-logo-alpha.png", null); //add t3logo to resources for use in about dialog
        _colorPickerTexture = ResourceManager.CreateTextureResource(@"images/editor/t3-colorpicker.png", null);
        
        if (_viewWindowDefaultTexture.Value == null)
        {
            Log.Error("Failed to load default view window background texture");
        }
        else
        {
            _viewWindowDefaultTexture.Value.CreateShaderResourceView(ref ViewWindowDefaultTextureSrv, "view window default texture SRV");
        }
            
        if (_colorPickerTexture.Value == null)
        {
            Log.Error("Failed to load color picker texture");
        }
        else
        {
            _colorPickerTexture.Value.CreateShaderResourceView(ref ColorPickerImageSrv, "color picker image SRV");
        }
        if (_t3logoAlphaTexture.Value == null)
        {
            Log.Error("Failed to load t3 logo texture");
        }
        else
        {
            _t3logoAlphaTexture.Value.CreateShaderResourceView(ref t3logoAlphaTextureImageSrv, "t3 logo image SRV"); // convert to shader resource view
        }

    }
    public static RasterizerState ViewWindowRasterizerState;
    public static ShaderResourceView ViewWindowDefaultTextureSrv;
    public static ShaderResourceView ColorPickerImageSrv;
    public static ShaderResourceView t3logoAlphaTextureImageSrv;
    private static Resource<VertexShader> _fullScreenVertexShaderResource;
    private static Resource<PixelShader> _fullScreenPixelShaderResource;
    private static Resource<Texture2D> _viewWindowDefaultTexture;
    private static Resource<Texture2D> _colorPickerTexture;
    private static Resource<Texture2D> _t3logoAlphaTexture; //create texture for t3logo for about dialog 
    public static Resource<VertexShader> FullScreenVertexShaderResource => _fullScreenVertexShaderResource;

    public static Resource<PixelShader> FullScreenPixelShaderResource => _fullScreenPixelShaderResource;

    private sealed class SharedResourceObject : IResourcePackage
    {
        public string DisplayName => "Shared Resources";
        public string Alias => "t3";
        // ReSharper disable once ReplaceAutoPropertyWithComputedProperty
        public string ResourcesFolder { get; } = Directory;
        public ResourceFileWatcher FileWatcher => null;
        public bool IsReadOnly => true;
        public IReadOnlyCollection<DependencyCounter> Dependencies { get; } = Array.Empty<DependencyCounter>();
    }
}