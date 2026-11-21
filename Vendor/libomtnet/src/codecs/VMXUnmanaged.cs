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
    internal class VMXUnmanaged
    {
        private const string DLLPATH = @"libvmx";
        [DllImport(DLLPATH)]
        internal static extern IntPtr VMX_Create(OMTSize dimensions, VMXProfile profile, VMXColorSpace colorSpace);
        [DllImport(DLLPATH)]
        internal static extern void VMX_Destroy(IntPtr instance);
        [DllImport(DLLPATH)]
        internal static extern void VMX_SetQuality(IntPtr instance, int q);
        [DllImport(DLLPATH)]
        internal static extern int VMX_GetQuality(IntPtr instance);
        [DllImport(DLLPATH)]
        internal static extern void VMX_SetThreads(IntPtr instance, int t);
        [DllImport(DLLPATH)]
        internal static extern int VMX_GetThreads(IntPtr instance);
        [DllImport(DLLPATH)]
        internal static extern int VMX_LoadFrom(IntPtr instance, byte[] data, int dataLen);
        [DllImport(DLLPATH)]
        internal static extern int VMX_SaveTo(IntPtr instance, byte[] data, int maxLen);
        [DllImport(DLLPATH)]
        internal static extern int VMX_EncodeBGRA(IntPtr Instance, IntPtr src, int stride, int interlaced);
        [DllImport(DLLPATH)]
        internal static extern int VMX_EncodeBGRX(IntPtr Instance, IntPtr src, int stride, int interlaced);
        [DllImport(DLLPATH)]
        internal static extern int VMX_EncodeP216(IntPtr Instance, IntPtr src, int stride, int interlaced);
        [DllImport(DLLPATH)]
        internal static extern int VMX_EncodePA16(IntPtr Instance, IntPtr src, int stride, int interlaced);
        [DllImport(DLLPATH)]
        internal static extern int VMX_EncodeUYVY(IntPtr Instance, IntPtr src, int stride, int interlaced);
        [DllImport(DLLPATH)]
        internal static extern int VMX_EncodeUYVA(IntPtr Instance, IntPtr src, int stride, int interlaced);
        [DllImport(DLLPATH)]
        internal static extern int VMX_EncodeYUY2(IntPtr Instance, IntPtr src, int stride, int interlaced);    
        [DllImport(DLLPATH)]
        internal static extern int VMX_EncodeNV12(IntPtr Instance, IntPtr srcY, int strideY, IntPtr srcUV, int strideUV, int interlaced);
        [DllImport(DLLPATH)]
        internal static extern int VMX_EncodeYV12(IntPtr Instance, IntPtr srcY, int strideY, IntPtr srcU, int strideU, IntPtr srcV, int strideV, int interlaced);
        [DllImport(DLLPATH)]
        internal static extern int VMX_DecodeUYVY(IntPtr Instance, byte[] dst, int stride);
        [DllImport(DLLPATH)]
        internal static extern int VMX_DecodeUYVA(IntPtr Instance, byte[] dst, int stride);
        [DllImport(DLLPATH)]
        internal static extern int VMX_DecodeP216(IntPtr Instance, byte[] dst, int stride);
        [DllImport(DLLPATH)]
        internal static extern int VMX_DecodePA16(IntPtr Instance, byte[] dst, int stride);
        [DllImport(DLLPATH)]
        internal static extern int VMX_DecodeYUY2(IntPtr Instance, byte[] dst, int stride);
        [DllImport(DLLPATH)]
        internal static extern int VMX_DecodeBGRX(IntPtr Instance, byte[] dst, int stride);
        [DllImport(DLLPATH)]
        internal static extern int VMX_DecodeBGRA(IntPtr Instance, byte[] dst, int stride);
        [DllImport(DLLPATH)]
        internal static extern int VMX_DecodePreviewUYVY(IntPtr Instance, byte[] dst, int stride);
        [DllImport(DLLPATH)]
        internal static extern int VMX_DecodePreviewYUY2(IntPtr Instance, byte[] dst, int stride);
        [DllImport(DLLPATH)]
        internal static extern int VMX_DecodePreviewBGRA(IntPtr Instance, byte[] dst, int stride);
        [DllImport(DLLPATH)]
        internal static extern int VMX_DecodePreviewBGRX(IntPtr Instance, byte[] dst, int stride);
        [DllImport(DLLPATH)]
        internal static extern int VMX_DecodePreviewUYVA(IntPtr Instance, byte[] dst, int stride);
        [DllImport(DLLPATH)]
        internal static extern int VMX_GetEncodedPreviewLength(IntPtr Instance);
        [DllImport(DLLPATH)]
        internal static extern float VMX_CalculatePSNR(byte[] image1, byte[] image2, int stride, int bytesPerPixel, OMTSize sz);
    }
}
