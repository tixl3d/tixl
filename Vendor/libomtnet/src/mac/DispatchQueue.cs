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
using System.Runtime.InteropServices;

namespace libomtnet.mac
{
    internal class DispatchQueue : OMTBase
    {
        private const string DLL_PATH = @"libSystem.dylib";
        [DllImport(DLL_PATH, CharSet = CharSet.Ansi)]
        private static extern IntPtr dispatch_queue_create(string label, IntPtr attr);
        [DllImport(DLL_PATH)]
        private static extern IntPtr dispatch_release(IntPtr dispatchObject);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void DispatchDelegate(IntPtr context);
        [DllImport(DLL_PATH)]
        private static extern void dispatch_async_f(IntPtr queue, IntPtr context, DispatchDelegate func);

        private IntPtr queue;

        public IntPtr NativePointer { get { return queue; } }

        public void Invoke(IntPtr context, DispatchDelegate func)
        {
            if (queue != IntPtr.Zero)
            {
                dispatch_async_f(queue, context, func);
            }
        }

        public DispatchQueue(string label)
        {
            queue = dispatch_queue_create(label, IntPtr.Zero);
        }
        protected override void DisposeInternal()
        {
            if (queue != IntPtr.Zero)
            {
                dispatch_release(queue);
                queue = IntPtr.Zero;
            }
            base.DisposeInternal();
        }
    }
}
