using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using T3.Core.Utils;

using Buffer = SharpDX.Direct3D11.Buffer;

namespace T3.Core.Rendering
{

    public static class ResourceUtils
    {
        //Constant buffers are always aligned to 16 bytes
        private const int CBufferAlignment = 16;
        private const int CBufferAlignmentMinusOne = CBufferAlignment - 1;

        /*It should be preferable to use this, even is driver does not enforce 256 alignment on creation,
        driver will do it anyway, so that avoids recreating resources for very small buffers */
        private const int CBufferReadAlignment = 256;
        private const int CBufferReadAlignmentMinusOne = 255;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetBufferSize(int requestedSize)
        {
            int size = ((requestedSize + CBufferAlignmentMinusOne) / CBufferAlignment) * CBufferAlignment;

            return size;
        }

        public static Buffer CreateDynamicConstantBuffer(Device device, int requestedSize)
        {
            int size = GetBufferSize(requestedSize);

            var bufferDesc = new BufferDescription
            {
                Usage = ResourceUsage.Dynamic,
                CpuAccessFlags = CpuAccessFlags.Write,
                SizeInBytes = size,
                BindFlags = BindFlags.ConstantBuffer
            };
            return new Buffer(device, bufferDesc);
        }

        public static bool GetDynamicConstantBuffer(Device device, ref Buffer buffer, int requestedSize)
        {
            int size = GetBufferSize(requestedSize);
            if (buffer != null && buffer.Description.SizeInBytes != size)
            {
                Utilities.Dispose(ref buffer);
            }
            if (buffer == null)
            {
                buffer = CreateDynamicConstantBuffer(device, requestedSize);
                return true;
            }
            else
            {
                return false;
            }
        }

        public static unsafe void WriteDynamicBufferData<T>(DeviceContext deviceContext, Buffer buffer, ReadOnlySpan<T> data) where T : unmanaged
        {
            SharpDX.DataBox box = deviceContext.MapSubresource(buffer, MapMode.WriteDiscard, MapFlags.None, out _);
            Span<T> boxData = new Span<T>((void*)box.DataPointer, data.Length);
            data.CopyTo(boxData);
            deviceContext.UnmapSubresource(buffer, 0);
        }

        public static unsafe void WriteDynamicBufferData<T>(DeviceContext deviceContext, Buffer buffer, T data) where T : unmanaged
        {
            SharpDX.DataBox box = deviceContext.MapSubresource(buffer, MapMode.WriteDiscard, MapFlags.None, out _);
            Unsafe.Write((void*)box.DataPointer, data);
            deviceContext.UnmapSubresource(buffer, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetDynamicConstantBuffer<T>(Device device, ref Buffer buffer, int elementCount) where T : unmanaged
        {
            return GetDynamicConstantBuffer(device, ref buffer, elementCount * Unsafe.SizeOf<T>());
        }
    }
}
