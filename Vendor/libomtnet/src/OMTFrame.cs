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

namespace libomtnet
{
    internal enum OMTVersion
    {
        Version1 = 1
    }

    [Flags]
    internal enum OMTActiveAudioChannels : uint
    {
        C1 = 1,
        C2 = 2,
        C3 = 4,
        C4 = 8,
        C5 = 16,
        C6 = 32,
        C7 = 64,
        C8 = 128,
        C9 = 256,
        C10 = 512,
        C11 = 1024,
        C12 = 2048,
        C13 = 4096,
        C14 = 8192,
        C15 = 16384,
        C16 = 32768,
        C17 = 65536,
        C18 = 131072,
        C19 = 262144,
        C20 = 524288,
        C21 = 1048576,
        C22 = 2097152,
        C23 = 4194304,
        C24 = 8388608,
        C25 = 16777216,
        C26 = 33554432,
        C27 = 67108864,
        C28 = 134217728,
        C29 = 268435456,
        C30 = 536870912,
        C31 = 1073741824,
        C32 = 2147483658
    }

    internal struct OMTFrameHeader
    {
        public byte Version; //=1
        public byte FrameType;
        public long Timestamp;
        //public byte Reserved1;
        //public byte Reserved2;
        public ushort MetadataLength; //Length in bytes of UTF-8 metadata including null character
        public int DataLength; //Including extended header and metadata
    }
    internal struct OMTVideoHeader
    {
        public int Codec;
        public int Width;
        public int Height;
        public int FrameRateN;
        public int FrameRateD;
        public float AspectRatio;
        public int Flags;
        public int ColorSpace;
    }
    internal struct OMTAudioHeader
    {
        public int Codec;
        public int SampleRate;
        public int SamplesPerChannel;
        public int Channels;
        public uint ActiveChannels;
        public int Reserved1;
    }

    internal enum OMTFrameLength
    {
        None = 0,
        Header = 16,
        ExtendedHeaderVideo = 32,
        ExtendedHeaderAudio = 24
    }

    internal class OMTFrameBase : OMTBase
    {
        public virtual OMTFrameType FrameType { get { return OMTFrameType.None; } }
        public virtual long Timestamp
        { get { return 0; } set { } }
    }

    internal class OMTFrame : OMTFrameBase
    {
        protected OMTFrameHeader header;
        protected OMTBuffer buffer;
        protected OMTVideoHeader videoHeader;
        protected OMTAudioHeader audioHeader;
        protected int previewLength;
        protected bool preview;
        protected OMTBinary binary = new OMTBinary();
        public OMTFrame(OMTFrameType frameType, int maxDataLength, bool resizable)
        {
            header.Version = (byte)OMTVersion.Version1;
            header.FrameType = (byte)frameType;
            buffer = new OMTBuffer(maxDataLength, resizable);
            buffer.SetBuffer(0, 0);
            UpdateDataLength();
        }
        public OMTFrame(int maxDataLength, bool resizable)
        {
            buffer = new OMTBuffer(maxDataLength, resizable);
            buffer.SetBuffer(0, 0);
        }

        public OMTFrame(OMTFrameType frameType, OMTBuffer buff)
        {
            header.Version = (byte)OMTVersion.Version1;
            header.FrameType = (byte)frameType;
            buffer = buff;
            UpdateDataLength();
        }
        public int HeaderLength
        {
            get { return (int)OMTFrameLength.Header; }
        }

        public int MetadataLength
        {
            get { return (int)header.MetadataLength; }
        }
        public int ExtendedHeaderLength
        {
            get
            {
                if (header.FrameType == (byte)OMTFrameType.Video)
                {
                    return (int)OMTFrameLength.ExtendedHeaderVideo;
                }
                else if (header.FrameType == (byte)OMTFrameType.Audio)
                {
                    return (int)OMTFrameLength.ExtendedHeaderAudio;
                }
                return 0;
            }
        }
        public int Length
        {
            get
            {
                if (preview)
                {
                    return HeaderLength + previewLength;
                } else
                {
                    return HeaderLength + header.DataLength;
                } 
            }
        }

        protected void WriteHeaderInternal()
        {
            OMTBinary b = binary;
            b.Write(header.Version);
            b.Write(header.FrameType);
            b.Write(header.Timestamp);
            b.Write(header.MetadataLength);
            if (preview)
            {
                b.Write(previewLength);
            }
            else
            {
                b.Write(header.DataLength);
            }
        }

        public override OMTFrameType FrameType
        {
            get { return (OMTFrameType)header.FrameType; }
        }
        public override long Timestamp
        { get { return header.Timestamp; } set { header.Timestamp = value; } }

        protected void WriteExtendedHeaderInternal()
        {
            OMTBinary b = binary;
            if (header.FrameType == (byte)OMTFrameType.Video)
            {
                b.Write(videoHeader.Codec);
                b.Write(videoHeader.Width);
                b.Write(videoHeader.Height);
                b.Write(videoHeader.FrameRateN);
                b.Write(videoHeader.FrameRateD);
                b.Write(videoHeader.AspectRatio);
                if (preview)
                {
                    b.Write(videoHeader.Flags | (int)OMTVideoFlags.Preview);
                }
                else
                {
                    b.Write(videoHeader.Flags);
                }
                b.Write(videoHeader.ColorSpace);
            }
            else if (header.FrameType == (byte)OMTFrameType.Audio)
            {
                b.Write(audioHeader.Codec);
                b.Write(audioHeader.SampleRate);
                b.Write(audioHeader.SamplesPerChannel);
                b.Write(audioHeader.Channels);
                b.Write(audioHeader.ActiveChannels);
                b.Write(audioHeader.Reserved1);
            }
        }
        protected bool ReadHeaderInternal(byte[] data, int offset)
        {
            OMTBinary b = binary;
            b.SetBuffer(data, offset);
            header.Version = b.ReadByte();
            if (header.Version == (byte)OMTVersion.Version1)
            {
                header.FrameType = b.ReadByte();
                header.Timestamp = b.ReadInt64();
                header.MetadataLength = b.ReadUInt16();
                header.DataLength = b.ReadInt32();
                return true;
            }
            return false;
        }

        protected bool ReadExtendedHeaderInternal(byte[] data, int offset)
        {
            OMTBinary b = binary;
            b.SetBuffer(data, offset);
            if (header.Version == (byte)OMTVersion.Version1)
            {
                if (header.FrameType == (byte)OMTFrameType.Video)
                {
                    videoHeader.Codec = b.ReadInt32();
                    videoHeader.Width = b.ReadInt32();
                    videoHeader.Height = b.ReadInt32();
                    videoHeader.FrameRateN = b.ReadInt32();
                    videoHeader.FrameRateD = b.ReadInt32();
                    videoHeader.AspectRatio = b.ReadSingle();
                    videoHeader.Flags = b.ReadInt32();
                    videoHeader.ColorSpace = b.ReadInt32();
                    return true;
                }
                else if (header.FrameType == (byte)OMTFrameType.Audio)
                {
                    audioHeader.Codec = b.ReadInt32();
                    audioHeader.SampleRate = b.ReadInt32();
                    audioHeader.SamplesPerChannel = b.ReadInt32();
                    audioHeader.Channels = b.ReadInt32();
                    audioHeader.ActiveChannels = b.ReadUInt32();
                    audioHeader.Reserved1 = b.ReadInt32();
                    return true;
                }
            }
            return false;
        }

        public OMTBuffer Data
        {
            get { return buffer; }
        }
        private void UpdateDataLength()
        {
            header.DataLength = this.buffer.Length + ExtendedHeaderLength;
        }

        /// <summary>
        /// Includes MetadataLength
        /// </summary>
        /// <param name="length"></param>
        public void SetPreviewDataLength(int length)
        {
            previewLength = ExtendedHeaderLength + length;
        }

        /// <summary>
        /// Includes MetadataLength
        /// </summary>
        /// <param name="length"></param>
        public void SetDataLength(int length)
        {
            this.buffer.SetBuffer(0, length);
            UpdateDataLength();
        }
        public void SetMetadataLength(int length)
        {
            header.MetadataLength = (ushort)length;
        }

        public void SetPreviewMode(bool preview)
        {
            this.preview = preview;
        }
         public void WriteHeaderTo(byte[] buffer, int offset, int count)
        {
            binary.SetBuffer(buffer, offset);
            WriteHeaderInternal();
            WriteExtendedHeaderInternal();
        }        
        public void WriteDataTo(byte[] buffer, int srcOffset, int dstOffset, int count)
        {
            Buffer.BlockCopy(this.buffer.Buffer, this.buffer.Offset + srcOffset, buffer, dstOffset, count);
        }
        public bool ReadHeaderFrom(byte[] buffer, int offset, int count)
        {
            if (count < HeaderLength) return false;
            return ReadHeaderInternal(buffer, offset);
        }
        public bool ReadExtendedHeaderFrom(byte[] buffer, int offset, int count)
        {
            if (count < HeaderLength + ExtendedHeaderLength) return false;
            if (ExtendedHeaderLength == 0) return true;
            return ReadExtendedHeaderInternal(buffer, offset + HeaderLength);
        }
        public bool ReadDataFrom(byte[] buffer, int offset, int count)
        {
            if (count < HeaderLength + header.DataLength) return false;
            int len = header.DataLength - ExtendedHeaderLength;
            this.buffer.Resize(len);
            this.buffer.SetBuffer(0, 0);
            this.buffer.Append(buffer, offset + HeaderLength + ExtendedHeaderLength, len);
            this.buffer.SetBuffer(0, len);
            return true;
        }

        public OMTVideoHeader GetVideoHeader()
        {
            return videoHeader;
        }

        public OMTAudioHeader GetAudioHeader()
        {
            return audioHeader;
        }
        public void ConfigureVideo(int codec, int width, int height, int framerateN, int framerateD, float aspectRatio, OMTVideoFlags flags, OMTColorSpace colorSpace)
        {
            videoHeader.Codec = codec;
            videoHeader.Width = width;
            videoHeader.Height = height;
            videoHeader.FrameRateN = framerateN;
            videoHeader.FrameRateD = framerateD;
            videoHeader.AspectRatio = aspectRatio;
            videoHeader.Flags = (int)flags;
            videoHeader.ColorSpace = (int)colorSpace;
        }
        public void ConfigureAudio(int sampleRate, int channels, int samplesPerChannel, OMTActiveAudioChannels activeAudioChannels)
        {
            audioHeader.SampleRate = sampleRate;
            audioHeader.Channels = channels;
            audioHeader.SamplesPerChannel = samplesPerChannel;
            audioHeader.ActiveChannels = (uint)activeAudioChannels;
            audioHeader.Codec = (int)OMTCodec.FPA1;
        }

        protected override void DisposeInternal()
        {
            if (this.buffer != null)
            {
                this.buffer.Dispose();
            }
            base.DisposeInternal();
        }

    }
 
}
