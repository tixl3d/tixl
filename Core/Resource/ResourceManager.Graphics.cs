#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using T3.Core.Logging;
using T3.Core.Operator.Slots;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Core.Resource;

/// <summary>
/// Loads or creates resources that can be loaded into a shader, such as textures, buffers, SRVs, UAVs, etc
/// </summary>
public static partial class ResourceManager
{
    public static Device Device { get; private set; } = null!;

    public static void Init(Device device)
    {
        Device = device;
        var samplerDesc = new SamplerStateDescription
                              {
                                  Filter = Filter.MinMagMipLinear,
                                  AddressU = TextureAddressMode.Clamp,
                                  AddressV = TextureAddressMode.Clamp,
                                  AddressW = TextureAddressMode.Clamp,
                                  MipLodBias = 0.0f,
                                  MaximumAnisotropy = 1,
                                  ComparisonFunction = Comparison.Never,
                                  BorderColor = new RawColor4(1.0f, 1.0f, 1.0f, 1.0f),
                                  MinimumLod = -Single.MaxValue,
                                  MaximumLod = Single.MaxValue,
                              };

        DefaultSamplerState = new SamplerState(device, samplerDesc);
    
    }
    
    /* TODO, ResourceUsage usage, BindFlags bindFlags, CpuAccessFlags cpuAccessFlags, ResourceOptionFlags miscFlags, int loadFlags*/
    public static Resource<Texture2D> CreateTextureResource(string relativePath, IResourceConsumer? instance)
    {
        return new Resource<Texture2D>(relativePath, instance, TryCreateTextureResourceFromFile);
    }
    
    public static Resource<Texture2D> CreateTextureResource(InputSlot<string> slot)
    {
        return new Resource<Texture2D>(slot, TryCreateTextureResourceFromFile);
    }

    private static bool TryCreateTextureResourceFromFile(FileResource fileResource, Texture2D? currentValue, 
                                                         [NotNullWhen(true)] out Texture2D? resource, 
                                                         [NotNullWhen(false)] out string? failureReason)
    {
        if (!fileResource.TryOpenFileStream(out var fileStream, out failureReason, FileAccess.Read))
        {
            resource = null;
            return false;
        }
        
        using var stream = fileStream;
        
        if (Texture2D.TryLoadFromStream(stream, out resource, out failureReason))
        {
            resource.Name = fileResource.FileInfo?.Name ?? fileResource.AbsolutePath;
            return true;
        }

        return false;
    }

    public static void CreateStructuredBufferUav(Buffer? buffer, UnorderedAccessViewBufferFlags bufferFlags, ref UnorderedAccessView? uav)
    {
        if (buffer == null)
            return;

        try
        {
            if ((buffer.Description.OptionFlags & ResourceOptionFlags.BufferStructured) == 0)
            {
                // Log.Warning($"{nameof(SrvFromStructuredBuffer)} - input buffer is not structured, skipping SRV creation.");
                return;
            }

            uav?.Dispose();
            var uavDesc = new UnorderedAccessViewDescription
                              {
                                  Dimension = UnorderedAccessViewDimension.Buffer,
                                  Format = Format.Unknown,
                                  Buffer = new UnorderedAccessViewDescription.BufferResource
                                               {
                                                   FirstElement = 0,
                                                   ElementCount = buffer.Description.SizeInBytes / buffer.Description.StructureByteStride,
                                                   Flags = bufferFlags
                                               }
                              };
            uav = new UnorderedAccessView(Device, buffer, uavDesc);
        }
        catch (Exception e)
        {
            Log.Warning("Failed to create UAV " + e.Message);
        }
    }

    public static void CreateStructuredBufferSrv(Buffer? buffer, ref ShaderResourceView? srv)
    {
        if (buffer == null)
            return;

        try
        {
            if ((buffer.Description.OptionFlags & ResourceOptionFlags.BufferStructured) == 0)
            {
                // Log.Warning($"{nameof(SrvFromStructuredBuffer)} - input buffer is not structured, skipping SRV creation.");
                return;
            }

            srv?.Dispose();
            var srvDesc = new ShaderResourceViewDescription
                              {
                                  Dimension = ShaderResourceViewDimension.ExtendedBuffer,
                                  Format = Format.Unknown,
                                  BufferEx = new ShaderResourceViewDescription.ExtendedBufferResource
                                                 {
                                                     FirstElement = 0,
                                                     ElementCount = buffer.Description.SizeInBytes / buffer.Description.StructureByteStride
                                                 }
                              };
            srv = new ShaderResourceView(Device, buffer, srvDesc);
        }
        catch (Exception e)
        {
            Log.Warning("Failed to create SRV:" + e.Message);
        }
    }

    public static void SetupStructuredBuffer(int sizeInBytes, int stride, ref Buffer? buffer)
    {
        try
        {
            if (buffer == null || buffer.Description.SizeInBytes != sizeInBytes)
            {
                buffer?.Dispose();
                var bufferDesc = new BufferDescription
                                     {
                                         Usage = ResourceUsage.Default,
                                         BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                                         SizeInBytes = sizeInBytes,
                                         OptionFlags = ResourceOptionFlags.BufferStructured,
                                         StructureByteStride = stride
                                     };
                try
                {
                    buffer = new Buffer(Device, bufferDesc);
                }
                catch (Exception e)
                {
                    Log.Error($"Failed to setup structured buffer (stride:{stride} {sizeInBytes}b):" + e.Message);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning("Failed to create Structured buffer " + e.Message);
        }
    }

    public static void SetupStructuredBuffer(DataStream data, int sizeInBytes, int stride, ref Buffer? buffer)
    {
        if (buffer == null || buffer.Description.SizeInBytes != sizeInBytes)
        {
            buffer?.Dispose();
            var bufferDesc = new BufferDescription
                                 {
                                     Usage = ResourceUsage.Default,
                                     BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                                     SizeInBytes = sizeInBytes,
                                     OptionFlags = ResourceOptionFlags.BufferStructured,
                                     StructureByteStride = stride
                                 };
            buffer = new Buffer(Device, data, bufferDesc);
        }
        else
        {
            Device.ImmediateContext.UpdateSubresource(new DataBox(data.DataPointer, 0, 0), buffer);
        }
    }

    public static void SetupStructuredBuffer<T>(T[] bufferData, int sizeInBytes, int stride, ref Buffer? buffer) where T : struct
    {
        using var data = new DataStream(sizeInBytes, true, true);

        data.WriteRange(bufferData);
        data.Position = 0;

        SetupStructuredBuffer(data, sizeInBytes, stride, ref buffer);
    }

    public static void SetupStructuredBuffer<T>(T[] bufferData, ref Buffer? buffer) where T : struct
    {
        int stride = Marshal.SizeOf(typeof(T));
        int sizeInBytes = stride * bufferData.Length;
        SetupStructuredBuffer(bufferData, sizeInBytes, stride, ref buffer);
    }

    public static void CreateBufferUav<T>(Buffer? buffer, Format format, ref UnorderedAccessView? uav)
    {
        if (buffer == null)
            return;

        if ((buffer.Description.OptionFlags & ResourceOptionFlags.BufferStructured) != 0)
        {
            Log.Warning("Input buffer is structured, skipping UAV creation.");
            return;
        }

        uav?.Dispose();
        var desc = new UnorderedAccessViewDescription
                       {
                           Dimension = UnorderedAccessViewDimension.Buffer,
                           Format = format,
                           Buffer = new UnorderedAccessViewDescription.BufferResource
                                        {
                                            FirstElement = 0,
                                            ElementCount = buffer.Description.SizeInBytes / Marshal.SizeOf<T>(),
                                            Flags = UnorderedAccessViewBufferFlags.None
                                        }
                       };
        uav = new UnorderedAccessView(Device, buffer, desc);
    }

    public static void SetupConstBuffer<T>(T bufferData, [NotNull] ref Buffer? buffer) where T : struct
    {
        using var data = new DataStream(Marshal.SizeOf(typeof(T)), true, true);

        data.Write(bufferData);
        data.Position = 0;

        if (buffer == null)
        {
            var bufferDesc = new BufferDescription
                                 {
                                     Usage = ResourceUsage.Default,
                                     SizeInBytes = Marshal.SizeOf(typeof(T)),
                                     BindFlags = BindFlags.ConstantBuffer
                                 };
            buffer = new Buffer(Device, data, bufferDesc);
        }
        else
        {
            Device.ImmediateContext.UpdateSubresource(new DataBox(data.DataPointer, 0, 0), buffer);
        }
    }
    
    public static void UpdateConstBuffer<T>(T value, Buffer buffer) where T : struct
    {
        using var ds = new SharpDX.DataStream(System.Runtime.InteropServices.Marshal.SizeOf(typeof(T)), true, true);
        
        ds.Write(value);
        ds.Position = 0;
        Device.ImmediateContext.UpdateSubresource(new SharpDX.DataBox(ds.DataPointer, 0, 0), buffer);
    }

    public static SamplerState DefaultSamplerState { get; private set; } = null!;
}