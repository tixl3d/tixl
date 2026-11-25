using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using T3.Graphics.Compat;
using SharpDX.D3DCompiler;
using T3.Core.DataTypes;
using T3.Core.IO;
using T3.Core.Settings;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Core.Resource.Assets;
using ComputeShader = T3.Graphics.Compat.ComputeShader;
using GeometryShader = T3.Graphics.Compat.GeometryShader;
using PixelShader = T3.Graphics.Compat.PixelShader;
using VertexShader = T3.Graphics.Compat.VertexShader;

namespace T3.Core.Resource.ShaderCompiling;

/// <summary>
/// An implementation of the <see cref="ShaderCompiler"/> class that uses the DirectX 11 shader compiler from SharpDX
/// </summary>
// ReSharper disable once InconsistentNaming
public sealed partial class DX11ShaderCompiler : ShaderCompiler
{
    public Device Device { get; set; }

    protected override bool CompileShaderFromSource<TShader>(ShaderCompilationArgs args, out byte[] blob, out string errorMessage,
                                                             List<ShaderCompileFailures.Diagnostic> diagnostics)
    {
        CompilationResult compilationResult = null;
        string resultMessage;
        bool success;
        var profile = _shaderProfiles[typeof(TShader)];
        try
        {
            ShaderFlags flags = ShaderFlags.None;
            #if DEBUG || FORCE_SHADER_DEBUG
            flags |= ShaderFlags.Debug;
            #endif

            if (CoreSettings.Config.SkipOptimization)
            {
                flags |= ShaderFlags.SkipOptimization;
            }
            compilationResult = ShaderBytecode.Compile(args.SourceCode, 
                                                       args.EntryPoint, 
                                                       profile, 
                                                       flags, 
                                                       EffectFlags.None, 
                                                       null, 
                                                       new IncludeHandler(args.Owner),
                                                       // Named, so errors in the source itself can be told from errors in its includes.
                                                       args.Name);

            success = compilationResult.ResultCode == SharpDX.Result.Ok;
            resultMessage = compilationResult.Message;
        }
        catch (Exception ce)
        {
            success = false;
            resultMessage = ce.Message;
        }

        if (success)
        {
            blob = compilationResult.Bytecode.Data;
            errorMessage = string.Empty;
        }
        else
        {
            errorMessage = FxcDiagnostics.Describe(resultMessage, args.Name, diagnostics);
            blob = null;
        }

        return success;
    }
    
    protected override void CreateShaderInstance<TShader>(string name, in byte[] blob, out TShader shader)
    {
        // As shader type is generic we've to use Activator and PropertyInfo to create/set the shader object
        var shaderType = typeof(TShader);

        shader = (TShader)_shaderConstructors[shaderType].Invoke(Device, blob);
        
        var debugNameInfo = shaderType.GetProperty("DebugName");
        debugNameInfo?.SetValue(shader, name);
    }
    private static readonly IReadOnlyDictionary<Type, Func<Device, byte[], AbstractShader> > _shaderConstructors = new Dictionary<Type, Func<Device, byte[], AbstractShader>>()
                                                                                   {
                                                                                       { typeof(T3.Core.DataTypes.VertexShader), (device, data) => new DataTypes.VertexShader(new VertexShader(device, data, null), data) },
                                                                                       { typeof(T3.Core.DataTypes.PixelShader), (device, data) => new DataTypes.PixelShader(new PixelShader(device, data, null), data) },
                                                                                       { typeof(T3.Core.DataTypes.ComputeShader), (device, data) => new DataTypes.ComputeShader(new ComputeShader(device, data, null), data) },
                                                                                       { typeof(T3.Core.DataTypes.GeometryShader), (device, data) => new DataTypes.GeometryShader(new GeometryShader(device, data, null), data) },
                                                                                   };
    
    private static readonly IReadOnlyDictionary<Type, string> _shaderProfiles = new Dictionary<Type, string>()
                                                                                   {
                                                                                       { typeof(T3.Core.DataTypes.VertexShader), "vs_5_0" },
                                                                                       { typeof(T3.Core.DataTypes.PixelShader), "ps_5_0" },
                                                                                       { typeof(T3.Core.DataTypes.ComputeShader), "cs_5_0" },
                                                                                       { typeof(T3.Core.DataTypes.GeometryShader), "gs_5_0" },
                                                                                   };

    private sealed class IncludeHandler : SharpDX.D3DCompiler.Include, IResourceConsumer
    {
        private StreamReader _streamReader;
        private readonly IResourceConsumer _owner;
        
        public IncludeHandler(IResourceConsumer owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            Disposing?.Invoke(this); // Notify subscribers
            _streamReader?.Dispose();
        }

        public IDisposable Shadow { get; set; }

        public Stream Open(IncludeType type, string sharedIncludeFileName, Stream parentStream)
        {
            if(TryResolveSharedIncludeAsset(sharedIncludeFileName,  out var asset))
            {
                _streamReader = new StreamReader(asset.FullPath);
                return _streamReader.BaseStream;
            }

            Log.Error($"Could not locate include file '{sharedIncludeFileName}'");
            _streamReader = null;
            return null;
        }

        public void Close(Stream stream)
        {
            _streamReader.Close();
        }


        public IReadOnlyList<IResourcePackage> AvailableResourcePackages { get; }
        public SymbolPackage Package { get; }
        public event Action<IResourceConsumer> Disposing;
    }
}