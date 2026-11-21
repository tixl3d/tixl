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
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace libomtnet
{
    internal class OMTBuffer : OMTBase
    {
        private byte[] buffer;
        private int offset;
        private int length;
        private int maximumLength;
        private bool resizable = false;
        public OMTBuffer(byte[] buffer, int offset, int maximumLength) 
        {
            this.buffer = buffer;
            this.offset = offset;
            this.maximumLength = maximumLength;
            this.length = maximumLength;
        }
        public OMTBuffer(int maximumLength, bool resizable)
        {
            this.buffer = new byte[maximumLength];
            this.offset = 0;
            this.length = maximumLength;
            this.maximumLength = maximumLength;
            this.resizable = resizable;
        }

        public void Resize(int newMaximumLength)
        {
            if (resizable)
            {
                if (newMaximumLength > this.maximumLength)
                {
                    Debug.WriteLine("Resizing: " + this.maximumLength + " to " + newMaximumLength);
                    this.maximumLength = newMaximumLength;
                    this.buffer = new byte[maximumLength];
                    this.length = 0;
                    this.offset = 0;
                }
            } else
            {
                throw new Exception("This buffer does not support resizing.");
            }
        }

        public void Append(byte[] buffer, int offset, int count)
        {
            System.Buffer.BlockCopy(buffer, offset, this.buffer, this.offset, count);
            this.offset += count;
            this.length += count;
        }

        public void Append(IntPtr buffer, int offset, int count)
        {
            Marshal.Copy(buffer + offset, this.buffer, this.offset, count);
            this.offset += count;
            this.length += count;
        }

        /// <summary>
        /// Set the buffer where length is the entire length of valid data, not just from offset
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        public void SetBuffer(int offset, int length)
        {
            this.offset = offset;
            this.length = length;
        }

        public void SetBuffer(byte[] buffer, int offset, int length)
        {
            this.buffer = buffer;
            SetBuffer(offset, length);
        }

        public byte[] Buffer { get { return buffer; } }       
        public int Offset { get { return offset; } }
        public int Length { get { return length; } }
        public int MaximumLength { get { return maximumLength; } }
        public static OMTBuffer FromMetadata(string xml)
        {
            byte[] b = UTF8Encoding.UTF8.GetBytes(xml);
            return new OMTBuffer(b, 0, b.Length);
        }

        public string ToMetadata()
        {
            return UTF8Encoding.UTF8.GetString(this.buffer, this.offset, this.length);
        }
        protected override void DisposeInternal()
        {
            buffer = null;
            base.DisposeInternal();
        }
    }

    internal class OMTPinnedBuffer : OMTBuffer
    {
        private GCHandle handle;
        public OMTPinnedBuffer(int length)  : base(length,false)
        {
            handle = GCHandle.Alloc(this.Buffer, GCHandleType.Pinned);
        }

        public IntPtr Pointer { get {  return handle.AddrOfPinnedObject(); } }

        protected override void DisposeInternal()
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
            base.DisposeInternal();
        }
    }
}
