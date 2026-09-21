using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.Resource.ShaderCompiling;
using T3.Graphics;
using T3.Graphics.Compat;
using T3.Graphics.Vulkan;
using Xunit;
using Xunit.Abstractions;
using Buffer = T3.Graphics.Compat.Buffer;
using Format = T3.Graphics.Format;
using PixelShader = T3.Core.DataTypes.PixelShader;
using VertexShader = T3.Core.DataTypes.VertexShader;
using Texture2D = T3.Graphics.Compat.Texture2D;

namespace Core.Tests;

/// <summary>
/// Draws a mesh the way an operator graph does, with TiXL's own mesh-Draw.hlsl: vertices and face indices in
/// structured buffers read through SV_VertexID, four constant buffers, two samplers, four 2D maps and a
/// cube map. The single-triangle tests cover none of that, and a graph that draws a mesh is the first thing
/// to exercise it.
/// </summary>
[Collection("Vulkan")]
public class MeshDrawTests(ITestOutputHelper output)
{
    private const int Size = 64;
    private const int VertexStride = 80;   // PbrVertex: 20 floats.
    private const int TriangleCount = 1;

    [Fact]
    public void AMeshDrawBindsEverythingTheShaderDeclares()
    {
        using var backend = TryCreateBackend();

        if (backend == null)
            return;

        var shaders = RepositoryShaderFolder();

        if (shaders == null || SlangShaderCompiler.FindCompiler() == null)
        {
            output.WriteLine("Skipped: slangc or the operator shaders are not available here.");
            return;
        }

        var device = new Device(backend);
        var context = device.ImmediateContext;
        var compiler = new SlangShaderCompiler(device);
        var path = Path.Combine(shaders, "3d", "mesh", "mesh-Draw.hlsl");
        Assert.True(File.Exists(path), path);

        var source = File.ReadAllText(path);

        Assert.True(TryCompile<VertexShader>(compiler, source, "vsMain", shaders, Path.GetFileName(path), out var vertexBlob, out var reason), reason);
        Assert.True(TryCompile<PixelShader>(compiler, source, "psMain", shaders, Path.GetFileName(path), out var pixelBlob, out reason), reason);

        Assert.True(SpirvBlob.TryUnpack(vertexBlob!, out var vertexSpirv, out var vertexBindings, out _));
        Assert.True(SpirvBlob.TryUnpack(pixelBlob!, out var pixelSpirv, out var pixelBindings, out _));
        output.WriteLine($"vs: {vertexBindings.Length} bindings, ps: {pixelBindings.Length} bindings");

        using var vertexShader = new T3.Graphics.Compat.VertexShader(device, backend.CreateShader(ShaderStage.Vertex, vertexSpirv, "vsMain", vertexBindings, "vs"));
        using var pixelShader = new T3.Graphics.Compat.PixelShader(device, backend.CreateShader(ShaderStage.Pixel, pixelSpirv, "psMain", pixelBindings, "ps"));

        // The resources the shader declares, all of them - an unbound slot is a separate test.
        using var vertices = StructuredBuffer(device, VertexStride, 3);
        using var faces = StructuredBuffer(device, 12, TriangleCount);
        using var verticesView = new ShaderResourceView(device, vertices);
        using var facesView = new ShaderResourceView(device, faces);

        using var transforms = ConstantBuffer(device, 640);
        using var parameters = ConstantBuffer(device, 256);
        using var fog = ConstantBuffer(device, 256);
        using var lights = ConstantBuffer(device, 1024);

        using var sampler = new SamplerState(device,
                                             new SamplerStateDescription
                                                 {
                                                     Filter = Filter.MinMagMipLinear,
                                                     AddressU = TextureAddressMode.Wrap,
                                                     AddressV = TextureAddressMode.Wrap,
                                                     AddressW = TextureAddressMode.Wrap,
                                                     MaximumLod = float.MaxValue,
                                                 });

        var maps = new List<ShaderResourceView>();
        var textures = new List<Texture2D>();

        for (var i = 0; i < 5; i++)
        {
            var map = new Texture2D(device, Describe2D());
            textures.Add(map);
            maps.Add(new ShaderResourceView(device, map));
        }

        using var cube = new Texture2D(device, DescribeCube());
        using var cubeView = new ShaderResourceView(device, cube);

        using var target = new Texture2D(device, Describe2D(BindFlags.RenderTarget | BindFlags.ShaderResource));
        using var targetView = new RenderTargetView(device, target);
        using var depthState = new DepthStencilState(device, new DepthStencilStateDescription { IsDepthEnabled = false });
        using var rasterizer = new RasterizerState(device,
                                                   new RasterizerStateDescription
                                                       {
                                                           FillMode = FillMode.Solid,
                                                           CullMode = CullMode.None,
                                                           IsDepthClipEnabled = true,
                                                       });

        device.BeginFrame();
        context.OutputMerger.SetTargets(targetView);
        context.OutputMerger.SetDepthStencilState(depthState);
        context.Rasterizer.State = rasterizer;
        context.Rasterizer.SetViewport(0, 0, Size, Size);
        context.ClearRenderTargetView(targetView, new Vector4(0, 0, 0, 1));

        context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        context.VertexShader.Set(vertexShader);
        context.PixelShader.Set(pixelShader);

        // Both stages read the same t0/t1 and the transforms, which is what the mesh operators bind.
        foreach (var stage in new ShaderStageState[] { context.VertexShader, context.PixelShader })
        {
            stage.SetConstantBuffer(0, transforms);
            stage.SetConstantBuffer(1, parameters);
            stage.SetConstantBuffer(2, fog);
            stage.SetConstantBuffer(3, lights);
            stage.SetShaderResource(0, verticesView);
            stage.SetShaderResource(1, facesView);

            for (var i = 0; i < maps.Count; i++)
            {
                stage.SetShaderResource(2 + i, maps[i]);
            }

            stage.SetShaderResource(6, cubeView);
            stage.SetSampler(0, sampler);
            stage.SetSampler(1, sampler);
        }

        context.Draw(TriangleCount * 3, 0);
        device.EndFrame();

        foreach (var view in maps)
        {
            view.Dispose();
        }

        foreach (var texture in textures)
        {
            texture.Dispose();
        }

        AssertValidationStayedQuiet();
    }

    /// <summary>
    /// A matrix goes from System.Numerics through a constant buffer into HLSL exactly as the Transforms
    /// buffer does - transposed on the CPU, because Numerics stores rows and HLSL's default packing reads
    /// columns - and <c>mul(v, M)</c> in the shader has to agree with the CPU. A layout that disagrees
    /// transposes every matrix, which a near-identity 2D transform survives and a perspective one does not.
    /// </summary>
    [Fact]
    public void AMatrixFromTheCpuTransformsAVectorTheSameWayInTheShader()
    {
        using var backend = TryCreateBackend();

        if (backend == null)
            return;

        var shaders = RepositoryShaderFolder();

        if (shaders == null || SlangShaderCompiler.FindCompiler() == null)
        {
            output.WriteLine("Skipped: slangc or the operator shaders are not available here.");
            return;
        }

        const string source = """
                              cbuffer Transforms : register(b0)
                              {
                                  float4x4 Transform;
                              }

                              RWStructuredBuffer<float4> Result : register(u0);

                              [numthreads(1, 1, 1)]
                              void main()
                              {
                                  Result[0] = mul(float4(1, 2, 3, 1), Transform);
                              }
                              """;

        var device = new Device(backend);
        var context = device.ImmediateContext;
        var compiler = new SlangShaderCompiler(device);

        Assert.True(TryCompile<T3.Core.DataTypes.ComputeShader>(compiler, source, "main", shaders, "matrix.hlsl", out var blob, out var reason), reason);
        Assert.True(SpirvBlob.TryUnpack(blob!, out var spirv, out var bindings, out _));
        using var shader = new T3.Graphics.Compat.ComputeShader(device, backend.CreateShader(ShaderStage.Compute, spirv, "main", bindings, "matrix"));

        // Non-symmetric, with a translation: a transposed matrix moves that into the w column and is obvious.
        var matrix = Matrix4x4.CreateRotationZ(0.3f) * Matrix4x4.CreateTranslation(10, 20, 30);
        var expected = Vector4.Transform(new Vector4(1, 2, 3, 1), matrix);

        // What TransformBufferLayout uploads.
        var uploaded = Matrix4x4.Transpose(matrix);
        var matrixBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<Matrix4x4>(ref uploaded)).ToArray();

        using var constants = new Buffer(device,
                                         new BufferDescription
                                             {
                                                 SizeInBytes = 64,
                                                 BindFlags = BindFlags.ConstantBuffer,
                                                 Usage = ResourceUsage.Dynamic,
                                                 CpuAccessFlags = CpuAccessFlags.Write,
                                             },
                                         matrixBytes);

        using var result = new Buffer(device,
                                      new BufferDescription
                                          {
                                              SizeInBytes = 16,
                                              BindFlags = BindFlags.UnorderedAccess,
                                              Usage = ResourceUsage.Default,
                                              OptionFlags = ResourceOptionFlags.BufferStructured,
                                              StructureByteStride = 16,
                                          });
        using var resultView = new UnorderedAccessView(device, result);

        using var staging = new Buffer(device,
                                       new BufferDescription
                                           {
                                               SizeInBytes = 16,
                                               Usage = ResourceUsage.Staging,
                                               CpuAccessFlags = CpuAccessFlags.Read,
                                           });

        device.BeginFrame();
        context.ComputeShader.Set(shader);
        context.ComputeShader.SetConstantBuffer(0, constants);
        context.ComputeShader.SetUnorderedAccessView(0, resultView);
        context.Dispatch(1, 1, 1);
        context.CopyResource(result, staging);
        device.EndFrame();

        device.BeginFrame();
        var box = context.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        var values = new float[4];
        System.Runtime.InteropServices.Marshal.Copy(box.DataPointer, values, 0, 4);
        context.UnmapSubresource(staging, 0);
        device.EndFrame();

        var actual = new Vector4(values[0], values[1], values[2], values[3]);
        output.WriteLine($"expected {expected}, shader {actual}");

        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Z, actual.Z, 3);
        Assert.Equal(expected.W, actual.W, 3);
        AssertValidationStayedQuiet();
    }

    private static Buffer StructuredBuffer(Device device, int stride, int count)
        => new(device,
               new BufferDescription
                   {
                       SizeInBytes = stride * count,
                       BindFlags = BindFlags.ShaderResource,
                       Usage = ResourceUsage.Default,
                       OptionFlags = ResourceOptionFlags.BufferStructured,
                       StructureByteStride = stride,
                   });

    private static Buffer ConstantBuffer(Device device, int sizeInBytes)
        => new(device,
               new BufferDescription
                   {
                       SizeInBytes = sizeInBytes,
                       BindFlags = BindFlags.ConstantBuffer,
                       Usage = ResourceUsage.Default,
                   });

    private static Texture2DDescription Describe2D(BindFlags bindFlags = BindFlags.ShaderResource)
        => new()
               {
                   Width = Size,
                   Height = Size,
                   MipLevels = 1,
                   ArraySize = 1,
                   Format = Format.R8G8B8A8_UNorm,
                   SampleDescription = new SampleDescription(1, 0),
                   BindFlags = bindFlags,
                   Usage = ResourceUsage.Default,
               };

    private static Texture2DDescription DescribeCube()
        => new()
               {
                   Width = Size,
                   Height = Size,
                   MipLevels = 1,
                   ArraySize = 6,
                   Format = Format.R8G8B8A8_UNorm,
                   SampleDescription = new SampleDescription(1, 0),
                   BindFlags = BindFlags.ShaderResource,
                   Usage = ResourceUsage.Default,
                   OptionFlags = ResourceOptionFlags.TextureCube,
               };

    private static bool TryCompile<TShader>(SlangShaderCompiler compiler, string source, string entryPoint, string shaders, string name,
                                            out byte[]? blob, out string reason)
        where TShader : AbstractShader
    {
        var args = new ShaderCompiler.ShaderCompilationArgs(source, entryPoint, new ShaderFolderConsumer(shaders), name, null);
        return compiler.TryCompileSource<TShader>(args, out blob, out reason);
    }

    private void AssertValidationStayedQuiet()
    {
        if (_validationErrorsAtStart < 0)
            return;

        Assert.Equal(_validationErrorsAtStart, VulkanBackend.ValidationErrorCount);
    }

    private VulkanBackend? TryCreateBackend()
    {
        try
        {
            GraphicsLog.Error = message => output.WriteLine(message);
            var backend = new VulkanBackend(enableValidation: true);
            _validationErrorsAtStart = VulkanBackend.ValidationErrorCount;
            return backend;
        }
        catch (Exception exception)
        {
            output.WriteLine($"Skipped: no Vulkan device ({exception.Message})");
            return null;
        }
    }

    private static string? RepositoryShaderFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Operators", "Lib", "Assets", "shaders");

            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    private int _validationErrorsAtStart = -1;

    /// <summary>Points the compiler's include search at the repository's shader folder.</summary>
    private sealed class ShaderFolderConsumer(string assetsFolder) : T3.Core.Resource.IResourceConsumer
    {
        public IReadOnlyList<T3.Core.Resource.IResourcePackage> AvailableResourcePackages =>
            [new ShaderCompiler.ShaderResourcePackage(new FileInfo(Path.Combine(assetsFolder, "any.hlsl")))];

        public T3.Core.Model.SymbolPackage? Package => null;
        public event Action<T3.Core.Resource.IResourceConsumer>? Disposing;
    }
}
