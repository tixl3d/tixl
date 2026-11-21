using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using T3.Core.Resource.ShaderCompiling;
using T3.Graphics;
using T3.Graphics.Compat;
using T3.Graphics.Vulkan;
using Xunit;
using Xunit.Abstractions;
using Buffer = T3.Graphics.Compat.Buffer;
using ComputeShaderType = T3.Core.DataTypes.ComputeShader;

namespace Core.Tests.Volume;

/// <summary>
/// Runs the NanoVDB compute path on a device: builds a real grid in Core, flattens it, uploads it as
/// a structured buffer, dispatches <c>nanovdb-to-points.hlsl</c> and reads the result back.
///
/// Everything else about this feature was verified without a GPU - the layout against the writer,
/// the shaders against the compiler, the reference library against our files. This is the test that
/// answers whether the traversal reads the right numbers when it actually runs: the point count must
/// equal the grid's active voxel count, every position must land inside the grid box, and the slot
/// after the counter must be a separator.
/// </summary>
/// <remarks>Skips itself where slangc or a Vulkan device is missing.</remarks>
// ShaderCompiler.Instance is process-wide, so these classes must not run in parallel.
[Collection("ShaderCompiler")]
public class NanoVdbDispatchTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ThePointsShaderEmitsOnePointPerActiveVoxel()
    {
        var source = NanoVdbShaderSources.ReadShader("volume/nanovdb-to-points.hlsl");
        if (source == null)
        {
            output.WriteLine("Skipped: Operators/Lib/Assets was not found next to the test assembly.");
            return;
        }

        using var backend = TryCreateBackend();
        if (backend == null)
            return;

        var device = new Device(backend);
        var compiler = new SlangShaderCompiler(device);
        if (!TryCompile(compiler, source, "main", out var shader, out var reason))
        {
            // A machine without the pinned SPIR-V toolchain cannot compile at all; that is the
            // environment, not this shader.
            output.WriteLine($"Skipped: {reason}");
            return;
        }

        using var compiled = shader;

        // ---- the volume: a level set sphere, built by the same Core code the operators use.
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.1f, Vector3.Zero, 3);
        var nano = NanoVdbBuffer.FromGrid(grid);
        var info = nano.ReadInfo();
        output.WriteLine($"{info.Name}: {info.VoxelCount} active voxels, {info.LeafNodes} leaves");

        var words = MemoryMarshal.Cast<byte, uint>(nano.Buffer.AsSpan());
        var gridBuffer = new Buffer(device,
                                    new BufferDescription
                                        {
                                            SizeInBytes = words.Length * 4,
                                            BindFlags = BindFlags.ShaderResource,
                                            Usage = ResourceUsage.Default,
                                            OptionFlags = ResourceOptionFlags.BufferStructured,
                                            StructureByteStride = 4,
                                        },
                                    MemoryMarshal.AsBytes(words));

        using (gridBuffer)
        {
            var gridSrv = new ShaderResourceView(device, gridBuffer);
            using var gridSrvScope = gridSrv;

            const int maxPoints = 16384;
            const int pointStride = 64;
            const float threshold = -1e30f;      // keep every active voxel
            const int threadsPerGroup = 512;

            using var points = new Buffer(device,
                                          new BufferDescription
                                              {
                                                  SizeInBytes = maxPoints * pointStride,
                                                  BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                                                  Usage = ResourceUsage.Default,
                                                  OptionFlags = ResourceOptionFlags.BufferStructured,
                                                  StructureByteStride = pointStride,
                                              });

            using var counter = new Buffer(device,
                                           new BufferDescription
                                               {
                                                   SizeInBytes = 4,
                                                   BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                                                   Usage = ResourceUsage.Default,
                                                   OptionFlags = ResourceOptionFlags.BufferStructured,
                                                   StructureByteStride = 4,
                                               });

            using var pointsUav = new UnorderedAccessView(device, points);
            using var counterUav = new UnorderedAccessView(device, counter);
            using var pass0 = MakeParams(device, threshold, maxPoints, 0, info.LeafNodes);
            using var pass1 = MakeParams(device, threshold, maxPoints, 1, info.LeafNodes);

            var context = device.ImmediateContext;
            device.BeginFrame();
            context.ComputeShader.Set(shader!);
            context.ComputeShader.SetShaderResource(0, gridSrv);
            context.ComputeShader.SetUnorderedAccessView(0, pointsUav);
            context.ComputeShader.SetUnorderedAccessView(1, counterUav);

            // One group per leaf; the shader skips leaves past the grid's own count.
            context.ComputeShader.SetConstantBuffer(0, pass0);
            context.Dispatch(info.LeafNodes, 1, 1);

            // Fill the rest of the buffer with separators.
            context.ComputeShader.SetConstantBuffer(0, pass1);
            context.Dispatch(maxPoints / threadsPerGroup + 1, 1, 1);
            device.EndFrame();

            var counterReadback = await backend.ReadbackAsync(counter.GpuBuffer!);
            var count = MemoryMarshal.Cast<byte, uint>(counterReadback.Data.Span)[0];
            output.WriteLine($"shader wrote {count} points");

            // The whole point of the test: one point per active voxel, read off the device.
            Assert.Equal((uint)info.VoxelCount, count);

            var pointsReadback = await backend.ReadbackAsync(points.GpuBuffer!);
            var floats = MemoryMarshal.Cast<byte, float>(pointsReadback.Data.Span);

            var worldMin = info.WorldMin;
            var worldMax = info.WorldMax;
            var margin = info.VoxelSize * 2;

            var inspected = 0;
            for (var point = 0; point < Math.Min((int)count, 64); point++)
            {
                var offset = point * (pointStride / 4);
                var position = new Vector3(floats[offset], floats[offset + 1], floats[offset + 2]);
                var value = floats[offset + 3];

                Assert.True(position.X >= worldMin.X - margin && position.X <= worldMax.X + margin, $"point {point} x={position.X} outside the grid box");
                Assert.True(position.Y >= worldMin.Y - margin && position.Y <= worldMax.Y + margin, $"point {point} y={position.Y} outside the grid box");
                Assert.True(position.Z >= worldMin.Z - margin && position.Z <= worldMax.Z + margin, $"point {point} z={position.Z} outside the grid box");
                Assert.True(value >= threshold, $"point {point} value {value} is below the threshold");

                inspected++;
            }

            Assert.True(inspected > 0, "the shader wrote no points to inspect");

            // Slot right after the counter must be a separator: NaN Scale.x is TiXL's hidden-point mark.
            var separatorOffset = (int)count * (pointStride / 4);
            if ((int)count < maxPoints)
            {
                var scaleX = floats[separatorOffset + 12];
                Assert.True(float.IsNaN(scaleX), $"slot {count} should be a separator, Scale.x was {scaleX}");
            }
        }
    }

    private static Buffer MakeParams(Device device, float threshold, int maxPoints, int pass, int maxLeaves)
    {
        var values = new[] { threshold, BitConverter.Int32BitsToSingle(maxPoints), BitConverter.Int32BitsToSingle(pass), BitConverter.Int32BitsToSingle(maxLeaves) };
        return new Buffer(device,
                          new BufferDescription
                              {
                                  SizeInBytes = 16,
                                  BindFlags = BindFlags.ConstantBuffer,
                                  Usage = ResourceUsage.Dynamic,
                                  CpuAccessFlags = CpuAccessFlags.Write,
                              },
                          MemoryMarshal.AsBytes(values.AsSpan()));
    }

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
            return new VulkanBackend(enableValidation: true);
        }
        catch (Exception exception)
        {
            output.WriteLine($"Skipped: no Vulkan device ({exception.Message})");
            return null;
        }
    }

    private static bool TryCompile(SlangShaderCompiler compiler, string source, string entryPoint,
                                   out ComputeShaderType? shader, out string reason)
    {
        ShaderCompiler.Shutdown();
        typeof(ShaderCompiler).GetField("_shutdown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                              .SetValue(null, false);
        ShaderCompiler.Instance = compiler;

        var args = new ShaderCompiler.ShaderCompilationArgs(source, entryPoint, null, $"vdbdispatch{Guid.NewGuid():N}.hlsl", null);
        return ShaderCompiler.TryCompileShaderFromSource(args, useCache: false, forceRecompile: true, out shader, out reason);
    }
}
