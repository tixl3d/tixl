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

namespace libomtnet
{
    internal class OMTBinary
    {
        private byte[] buffer;
        private int offset;
        public void SetBuffer(byte[] buffer, int offset)
        {
            this.buffer = buffer;
            this.offset = offset;
        }
        public byte ReadByte()
        {
            byte value = buffer[offset];
            offset += 1;
            return value;
        }
        public Int32 ReadInt32()
        {
            Int32 result = (int)this.buffer[offset] | ((int)this.buffer[offset + 1] << 8) | ((int)this.buffer[offset + 2] << 16) | ((int)this.buffer[offset + 3] << 24);
            offset += 4;
            return result;
        }

        public UInt16 ReadUInt16()
        {
            UInt16 result = (ushort)((int)this.buffer[offset] | ((int)this.buffer[offset + 1] << 8));
            offset += 2;
            return result;
        }

        public Int64 ReadInt64()
        {
            uint num = (uint)((int)this.buffer[offset] | ((int)this.buffer[offset+1] << 8) | ((int)this.buffer[offset + 2] << 16) | ((int)this.buffer[offset + 3] << 24));
            uint num2 = (uint)((int)this.buffer[offset + 4] | ((int)this.buffer[offset + 5] << 8) | ((int)this.buffer[offset + 6] << 16) | ((int)this.buffer[offset + 7] << 24));
            Int64 val = (long)(((ulong)num2 << 32) | (ulong)num);
            offset += 8; 
            return val;
        }

        public UInt32 ReadUInt32()
        {
            UInt32 val = (uint)((int)this.buffer[offset + 0] | ((int)this.buffer[offset + 1] << 8) | ((int)this.buffer[offset + 2] << 16) | ((int)this.buffer[offset + 3] << 24));
            offset += 4;
            return val;
        }

        public unsafe Single ReadSingle()
        {
            uint num = (uint)((int)this.buffer[offset + 0] | ((int)this.buffer[offset + 1] << 8) | ((int)this.buffer[offset + 2] << 16) | ((int)this.buffer[offset + 3] << 24));
            offset += 4;
            return *(float*)(&num);
        }

        public void Write(byte value)
        {
            this.buffer[offset] = value;
            offset++;
        }
        public void Write(short value)
        {
            this.buffer[offset] = (byte)value;
            this.buffer[offset + 1] = (byte)(value >> 8);
            offset += 2;
        }
        public void Write(ushort value)
        {
            this.buffer[offset] = (byte)value;
            this.buffer[offset + 1] = (byte)(value >> 8);
            offset += 2;
        }
        public void Write(int value)
        {
            this.buffer[offset+ 0] = (byte)value;
            this.buffer[offset + 1] = (byte)(value >> 8);
            this.buffer[offset + 2] = (byte)(value >> 16);
            this.buffer[offset + 3] = (byte)(value >> 24);
            offset += 4;
        }
        public void Write(uint value)
        {
            this.buffer[offset + 0] = (byte)value;
            this.buffer[offset + 1] = (byte)(value >> 8);
            this.buffer[offset + 2] = (byte)(value >> 16);
            this.buffer[offset + 3] = (byte)(value >> 24);
            offset += 4;
        }
        public void Write(long value)
        {
            this.buffer[offset + 0] = (byte)value;
            this.buffer[offset + 1] = (byte)(value >> 8);
            this.buffer[offset + 2] = (byte)(value >> 16);
            this.buffer[offset + 3] = (byte)(value >> 24);
            this.buffer[offset + 4] = (byte)(value >> 32);
            this.buffer[offset + 5] = (byte)(value >> 40);
            this.buffer[offset + 6] = (byte)(value >> 48);
            this.buffer[offset + 7] = (byte)(value >> 56);
            offset += 8;
        }
        public unsafe void Write(float value)
        {
            uint num = *(uint*)(&value);
            this.buffer[offset + 0] = (byte)num;
            this.buffer[offset + 1] = (byte)(num >> 8);
            this.buffer[offset + 2] = (byte)(num >> 16);
            this.buffer[offset + 3] = (byte)(num >> 24);
            offset += 4;
        }

    }
}
