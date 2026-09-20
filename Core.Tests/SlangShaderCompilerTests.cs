using T3.Core.DataTypes;
using T3.Core.Resource.ShaderCompiling;
using T3.Graphics;
using T3.Graphics.Compat;
using T3.Graphics.Vulkan;
using Xunit;
using Xunit.Abstractions;
using ComputeShader = T3.Core.DataTypes.ComputeShader;
using Buffer = T3.Graphics.Compat.Buffer;
using Format = T3.Graphics.Format;
using PixelShader = T3.Core.DataTypes.PixelShader;

namespace Core.Tests;

/// <summary>
/// Compiles HLSL the way TiXL's shaders are written and runs the result on the GPU. The compiler's own
/// output proves little on its own: what matters is that the bindings it reports are the ones the backend
/// then binds, so these tests dispatch and read the answer back.
/// </summary>
/// <remarks>
/// Skips itself where slangc or a Vulkan device is missing, so it runs on a developer machine and stays
/// quiet on one without the toolchain.
/// </remarks>
public class SlangShaderCompilerTests(ITestOutputHelper output)
{
    [Fact]
    public void AComputeShaderCompilesAndReportsItsBindings()
    {
        using var backend = TryCreateBackend();

        if (backend == null)
            return;

        var device = new Device(backend);
        var compiler = new SlangShaderCompiler(device);

        // Written the way TiXL's shaders are: registers, a structured buffer, and the `sampler` keyword that
        // only FXC accepts.
        const string source = """
                              cbuffer Params : register(b0)
                              {
                                  float4 Tint;
                              }

                              Texture2D<float4> InputTexture : register(t0);
                              StructuredBuffer<float> Weights : register(t1);
                              sampler TexSampler : register(s0);
                              RWStructuredBuffer<float4> Result : register(u0);

                              [numthreads(8, 4, 2)]
                              void main(uint3 id : SV_DispatchThreadID)
                              {
                                  Result[id.x] = InputTexture.SampleLevel(TexSampler, float2(0, 0), 0) * Tint * Weights[0];
                              }
                              """;

        Assert.True(TryCompile<ComputeShader>(compiler, source, "main", out var shader, out var reason), reason);
        using var compiled = shader;

        Assert.True(shader!.TryGetThreadGroups(out var threadGroups));
        Assert.Equal(8, threadGroups.X);
        Assert.Equal(4, threadGroups.Y);
        Assert.Equal(2, threadGroups.Z);

        Assert.True(SpirvBlob.TryUnpack(ShaderBytecodeOf(shader), out var spirv, out var bindings, out _));

        // SPIR-V starts with its magic number; anything else means DXBC slipped through.
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(spirv, 0));

        var stageBase = ShaderSlots.BaseOf(ShaderStage.Compute);
        output.WriteLine(string.Join(", ", bindings.Select(b => $"{b.Name}@{b.Slot} {b.Kind}")));

        Assert.Equal(stageBase + ShaderSlots.SamplerBase, SlotOf(bindings, "TexSampler"));
        Assert.Equal(stageBase + ShaderSlots.ConstantBufferBase, SlotOf(bindings, "Params"));
        Assert.Equal(stageBase + ShaderSlots.ShaderResourceBase, SlotOf(bindings, "InputTexture"));
        Assert.Equal(stageBase + ShaderSlots.ShaderResourceBase + 1, SlotOf(bindings, "Weights"));
        Assert.Equal(stageBase + ShaderSlots.UnorderedAccessBase, SlotOf(bindings, "Result"));
    }

    [Fact]
    public void AnUnusedResourceIsNotReported()
    {
        using var backend = TryCreateBackend();

        if (backend == null)
            return;

        var compiler = new SlangShaderCompiler(new Device(backend));

        // Every TiXL shader file declares more than one entry point uses. A binding for a resource this entry
        // point ignores would not match the pipeline's layout.
        const string source = """
                              cbuffer Params : register(b0) { float4 Tint; }
                              Texture2D<float4> Unused : register(t0);
                              RWStructuredBuffer<float4> Result : register(u0);

                              [numthreads(1, 1, 1)]
                              void main(uint3 id : SV_DispatchThreadID)
                              {
                                  Result[id.x] = Tint;
                              }
                              """;

        Assert.True(TryCompile<ComputeShader>(compiler, source, "main", out var shader, out var reason), reason);
        using var compiled = shader;
        Assert.True(SpirvBlob.TryUnpack(ShaderBytecodeOf(shader!), out _, out var bindings, out _));

        Assert.DoesNotContain(bindings, binding => binding.Name == "Unused");
        Assert.Contains(bindings, binding => binding.Name == "Params");
    }

    [Fact]
    public void ACompiledShaderRunsAndWritesWhatItWasTold()
    {
        using var backend = TryCreateBackend();

        if (backend == null)
            return;

        var device = new Device(backend);
        var compiler = new SlangShaderCompiler(device);

        const string source = """
                              cbuffer Params : register(b0)
                              {
                                  float4 Tint;
                              }

                              RWStructuredBuffer<float4> Result : register(u0);

                              [numthreads(4, 1, 1)]
                              void main(uint3 id : SV_DispatchThreadID)
                              {
                                  Result[id.x] = Tint * (id.x + 1);
                              }
                              """;

        Assert.True(TryCompile<ComputeShader>(compiler, source, "main", out var shader, out var reason), reason);
        using var compiled = shader;

        var tint = new[] { 0.25f, 0.5f, 0.75f, 1f };
        using var constants = new Buffer(device,
                                         new BufferDescription
                                             {
                                                 SizeInBytes = 16,
                                                 BindFlags = BindFlags.ConstantBuffer,
                                                 Usage = ResourceUsage.Dynamic,
                                                 CpuAccessFlags = CpuAccessFlags.Write,
                                             },
                                         System.Runtime.InteropServices.MemoryMarshal.AsBytes<float>(tint));

        const int elements = 4;
        using var result = new Buffer(device,
                                      new BufferDescription
                                          {
                                              SizeInBytes = elements * 16,
                                              BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                                              Usage = ResourceUsage.Default,
                                              OptionFlags = ResourceOptionFlags.BufferStructured,
                                              StructureByteStride = 16,
                                          });

        using var resultUav = new UnorderedAccessView(device, result);

        var context = device.ImmediateContext;
        device.BeginFrame();
        context.ComputeShader.Set(shader!);
        context.ComputeShader.SetConstantBuffer(0, constants);
        context.ComputeShader.SetUnorderedAccessView(0, resultUav);
        context.Dispatch(1, 1, 1);
        device.EndFrame();

        var readback = backend.ReadbackAsync(ResultBufferOf(result)).GetAwaiter().GetResult();
        var values = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(readback.Data.Span);

        output.WriteLine("values: " + string.Join(", ", values.ToArray().Select(v => v.ToString("0.###"))));

        // Element i is the tint times (i + 1), which no wrongly bound resource would produce.
        for (var element = 0; element < elements; element++)
        {
            for (var channel = 0; channel < 4; channel++)
            {
                Assert.Equal(tint[channel] * (element + 1), values[element * 4 + channel], 3);
            }
        }

        // Process-wide, so what matters is that this test added none.
        Assert.Equal(_validationErrorsAtStart, VulkanBackend.ValidationErrorCount);
    }

    [Fact]
    public void ARealShaderWithIncludesCompiles()
    {
        using var backend = TryCreateBackend();

        if (backend == null)
            return;

        var shaders = RepositoryShaderFolder();

        if (shaders == null)
        {
            output.WriteLine("Skipped: the operator shaders were not found next to the test binary.");
            return;
        }

        // A real one, includes and all: this is what the include search paths have to resolve.
        var path = Path.Combine(shaders, "3d", "mesh", "mesh-Displace.hlsl");
        Assert.True(File.Exists(path), path);

        var compiler = new SlangShaderCompiler(new Device(backend));
        var args = new ShaderCompiler.ShaderCompilationArgs(File.ReadAllText(path), "main", new TestResourceConsumer(shaders),
                                                            Path.GetFileName(path), null);

        // Straight to the compiler: the usual path resolves includes through the asset registry, which only
        // exists once packages are loaded. What is under test here is the include search path slangc gets.
        Assert.True(compiler.TryCompileSource<ComputeShader>(args, out var blob, out var reason), reason);
        Assert.True(SpirvBlob.TryUnpack(blob, out _, out var bindings, out var threadGroups));
        output.WriteLine($"{bindings.Length} bindings, thread groups {threadGroups.X}x{threadGroups.Y}x{threadGroups.Z}");
        Assert.NotEmpty(bindings);
        Assert.True(threadGroups.X > 0);
    }

    /// <summary>The operator shaders in the repository, or null when the tests run from somewhere else.</summary>
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

    private static GpuBuffer ResultBufferOf(Buffer buffer) => buffer.GpuBuffer!;

    private static byte[] ShaderBytecodeOf(AbstractShader shader) => shader.CompiledBytecode;

    private static int SlotOf(ShaderBinding[] bindings, string name)
    {
        foreach (var binding in bindings)
        {
            if (binding.Name == name)
                return binding.Slot;
        }

        return -1;
    }

    private static bool TryCompile<TShader>(SlangShaderCompiler compiler, string source, string entryPoint, out TShader? shader, out string reason)
        where TShader : AbstractShader
    {
        ShaderCompiler.Shutdown();
        typeof(ShaderCompiler).GetField("_shutdown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                              .SetValue(null, false);
        ShaderCompiler.Instance = compiler;

        var args = new ShaderCompiler.ShaderCompilationArgs(source, entryPoint, new TestResourceConsumer(), $"test{Guid.NewGuid():N}.hlsl", null);

        // No cache: these tests are about what the compiler produces, not about the cache.
        return ShaderCompiler.TryCompileShaderFromSource(args, useCache: false, forceRecompile: true, out shader, out reason);
    }

    private int _validationErrorsAtStart;

    private VulkanBackend? TryCreateBackend()
    {
        if (SlangShaderCompiler.FindCompiler() == null)
        {
            output.WriteLine("Skipped: slangc was not found.");
            return null;
        }

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

    /// <summary>A consumer with one asset folder, which is where an include is looked for.</summary>
    private sealed class TestResourceConsumer(string? assetsFolder = null) : T3.Core.Resource.IResourceConsumer
    {
        public IReadOnlyList<T3.Core.Resource.IResourcePackage> AvailableResourcePackages =>
            assetsFolder == null ? [] : [new ShaderCompiler.ShaderResourcePackage(new FileInfo(Path.Combine(assetsFolder, "any.hlsl")))];
        public T3.Core.Model.SymbolPackage? Package => null;
        public event Action<T3.Core.Resource.IResourceConsumer>? Disposing;
    }
}
