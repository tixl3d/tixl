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
using System.Runtime.InteropServices;
using System.Text;

namespace libomtnet.codecs
{
    /// <summary>
    /// Interface for native VMX codec. Needed for platforms which require different library paths, such as iOS
    /// </summary>
    internal interface IVMXCodec
    {
        IntPtr VMX_Create(OMTSize dimensions, VMXProfile profile, VMXColorSpace colorSpace);
        void VMX_Destroy(IntPtr instance);
        void VMX_SetQuality(IntPtr instance, int q);
        int VMX_GetQuality(IntPtr instance);
        void VMX_SetThreads(IntPtr instance, int t);
        int VMX_GetThreads(IntPtr instance);
        int VMX_LoadFrom(IntPtr instance, byte[] data, int dataLen);
        int VMX_SaveTo(IntPtr instance, byte[] data, int maxLen);
        int VMX_EncodeBGRA(IntPtr Instance, IntPtr src, int stride, int interlaced);
        int VMX_EncodeBGRX(IntPtr Instance, IntPtr src, int stride, int interlaced);
        int VMX_EncodeUYVY(IntPtr Instance, IntPtr src, int stride, int interlaced);
        int VMX_EncodeUYVA(IntPtr Instance, IntPtr src, int stride, int interlaced);
        int VMX_EncodeP216(IntPtr Instance, IntPtr src, int stride, int interlaced);
        int VMX_EncodePA16(IntPtr Instance, IntPtr src, int stride, int interlaced);
        int VMX_EncodeYUY2(IntPtr Instance, IntPtr src, int stride, int interlaced);
        int VMX_EncodeNV12(IntPtr Instance, IntPtr srcY, int strideY, IntPtr srcUV, int strideUV, int interlaced);
        int VMX_EncodeYV12(IntPtr Instance, IntPtr srcY, int strideY, IntPtr srcU, int strideU, IntPtr srcV, int strideV, int interlaced);
        int VMX_DecodeUYVY(IntPtr Instance, byte[] dst, int stride);
        int VMX_DecodeUYVA(IntPtr Instance, byte[] dst, int stride);
        int VMX_DecodeP216(IntPtr Instance, byte[] dst, int stride);
        int VMX_DecodePA16(IntPtr Instance, byte[] dst, int stride);
        int VMX_DecodeYUY2(IntPtr Instance, byte[] dst, int stride);
        int VMX_DecodeBGRX(IntPtr Instance, byte[] dst, int stride);
        int VMX_DecodeBGRA(IntPtr Instance, byte[] dst, int stride);
        int VMX_DecodePreviewUYVY(IntPtr Instance, byte[] dst, int stride);
        int VMX_DecodePreviewYUY2(IntPtr Instance, byte[] dst, int stride);
        int VMX_DecodePreviewBGRA(IntPtr Instance, byte[] dst, int stride);
        int VMX_DecodePreviewBGRX(IntPtr Instance, byte[] dst, int stride);
        int VMX_DecodePreviewUYVA(IntPtr Instance, byte[] dst, int stride);
        int VMX_GetEncodedPreviewLength(IntPtr Instance);
        float VMX_CalculatePSNR(byte[] image1, byte[] image2, int stride, int bytesPerPixel, OMTSize sz);
    }
}
