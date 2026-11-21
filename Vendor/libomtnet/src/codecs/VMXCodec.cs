/*
* MIT License
*
* Copyright (c) 2025 Open Media Transport Contributors
*
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
*
* The above copyright notice and this permission notice shall be included in all
* copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
* SOFTWARE.
*
*/

using System;
using System.Collections.Generic;
using System.Text;

namespace libomtnet.codecs
{
    internal class VMXCodec : IVMXCodec
    {
        public float VMX_CalculatePSNR(byte[] image1, byte[] image2, int stride, int bytesPerPixel, OMTSize sz)
        {
            return VMXUnmanaged.VMX_CalculatePSNR(image1, image2, stride, bytesPerPixel, sz);
        }

        public IntPtr VMX_Create(OMTSize dimensions, VMXProfile profile, VMXColorSpace colorSpace)
        {
            return VMXUnmanaged.VMX_Create(dimensions, profile, colorSpace);
        }

        public int VMX_DecodeBGRA(IntPtr Instance, byte[] dst, int stride)
        {
            return VMXUnmanaged.VMX_DecodeBGRA(Instance, dst, stride);
        }

        public int VMX_DecodeBGRX(IntPtr Instance, byte[] dst, int stride)
        {
            return VMXUnmanaged.VMX_DecodeBGRX(Instance, dst, stride); 
        }

        public int VMX_DecodePreviewBGRA(IntPtr Instance, byte[] dst, int stride)
        {
            return VMXUnmanaged.VMX_DecodePreviewBGRA(Instance, dst, stride);   
        }

        public int VMX_DecodePreviewBGRX(IntPtr Instance, byte[] dst, int stride)
        {
            return VMXUnmanaged.VMX_DecodePreviewBGRX(Instance, dst, stride);
        }

        public int VMX_DecodePreviewUYVY(IntPtr Instance, byte[] dst, int stride)
        {
            return VMXUnmanaged.VMX_DecodePreviewUYVY(Instance, dst, stride);
        }

        public int VMX_DecodePreviewUYVA(IntPtr Instance, byte[] dst, int stride)
        {
            return VMXUnmanaged.VMX_DecodePreviewUYVA(Instance, dst, stride);
        }

        public int VMX_DecodePreviewYUY2(IntPtr Instance, byte[] dst, int stride)
        {
            return VMXUnmanaged.VMX_DecodePreviewYUY2(Instance, dst, stride);
        }

        public int VMX_DecodeUYVY(IntPtr Instance, byte[] dst, int stride)
        {
            return VMXUnmanaged.VMX_DecodeUYVY(Instance, dst, stride);
        }

        public int VMX_DecodeUYVA(IntPtr Instance, byte[] dst, int stride)
        {
            return VMXUnmanaged.VMX_DecodeUYVA(Instance, dst, stride);
        }

        public int VMX_DecodeP216(IntPtr Instance, byte[] dst, int stride)
        {
            return VMXUnmanaged.VMX_DecodeP216(Instance, dst, stride);
        }

        public int VMX_DecodePA16(IntPtr Instance, byte[] dst, int stride)
        {
            return VMXUnmanaged.VMX_DecodePA16(Instance, dst, stride);
        }

        public int VMX_DecodeYUY2(IntPtr Instance, byte[] dst, int stride)
        {
            return VMXUnmanaged.VMX_DecodeYUY2(Instance, dst, stride);
        }

        public void VMX_Destroy(IntPtr instance)
        {
            VMXUnmanaged.VMX_Destroy(instance);
        }

        public int VMX_EncodeBGRA(IntPtr Instance, IntPtr src, int stride, int interlaced)
        {
            return VMXUnmanaged.VMX_EncodeBGRA(Instance,src,stride, interlaced);
        }

        public int VMX_EncodeBGRX(IntPtr Instance, IntPtr src, int stride, int interlaced)
        {
            return VMXUnmanaged.VMX_EncodeBGRX(Instance, src, stride, interlaced);
        }

        public int VMX_EncodeNV12(IntPtr Instance, IntPtr srcY, int strideY, IntPtr srcUV, int strideUV, int interlaced)
        {
            return VMXUnmanaged.VMX_EncodeNV12(Instance,srcY,strideY,srcUV,strideUV,interlaced);
        }

        public int VMX_EncodeUYVY(IntPtr Instance, IntPtr src, int stride, int interlaced)
        {
            return VMXUnmanaged.VMX_EncodeUYVY(Instance,src,stride,interlaced);
        }

        public int VMX_EncodeUYVA(IntPtr Instance, IntPtr src, int stride, int interlaced)
        {
            return VMXUnmanaged.VMX_EncodeUYVA(Instance, src, stride, interlaced);
        }

        public int VMX_EncodeP216(IntPtr Instance, IntPtr src, int stride, int interlaced)
        {
            return VMXUnmanaged.VMX_EncodeP216(Instance, src, stride, interlaced);
        }
        public int VMX_EncodePA16(IntPtr Instance, IntPtr src, int stride, int interlaced)
        {
            return VMXUnmanaged.VMX_EncodePA16(Instance, src, stride, interlaced);
        }

        public int VMX_EncodeYUY2(IntPtr Instance, IntPtr src, int stride, int interlaced)
        {
            return VMXUnmanaged.VMX_EncodeYUY2(Instance, src, stride, interlaced);
        }

        public int VMX_EncodeYV12(IntPtr Instance, IntPtr srcY, int strideY, IntPtr srcU, int strideU, IntPtr srcV, int strideV, int interlaced)
        {
            return VMXUnmanaged.VMX_EncodeYV12(Instance,srcY,strideY,srcU,strideU,srcV,strideV,interlaced);
        }

        public int VMX_GetEncodedPreviewLength(IntPtr Instance)
        {
            return VMXUnmanaged.VMX_GetEncodedPreviewLength(Instance);
        }

        public int VMX_GetThreads(IntPtr instance)
        {
            return VMXUnmanaged.VMX_GetThreads(instance);
        }

        public int VMX_LoadFrom(IntPtr instance, byte[] data, int dataLen)
        {
            return VMXUnmanaged.VMX_LoadFrom(instance,data,dataLen);
        }

        public int VMX_SaveTo(IntPtr instance, byte[] data, int maxLen)
        {
            return VMXUnmanaged.VMX_SaveTo(instance, data, maxLen);
        }

        public void VMX_SetQuality(IntPtr instance, int q)
        {
            VMXUnmanaged.VMX_SetQuality(instance, q);
        }

        public void VMX_SetThreads(IntPtr instance, int t)
        {
           VMXUnmanaged.VMX_SetThreads(instance, t);
        }

        public int VMX_GetQuality(IntPtr instance)
        {
            return VMXUnmanaged.VMX_GetQuality(instance);
        }
    }
}
