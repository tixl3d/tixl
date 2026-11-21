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
    internal class OMTFPA1Codec : OMTBase
    {
        byte[] zeroBuffer;
        public OMTFPA1Codec(int maxLength)
        {
            zeroBuffer = new byte[maxLength];
        }
        public void Decode(OMTBuffer src, int srcChannels, int srcSamplesPerChannel, OMTActiveAudioChannels srcActiveChannels, OMTBuffer dst)
        {
            int offset = 0;
            int dstoffset = 0;
            for (int i = 0; i < srcChannels; i++)
            {
                OMTActiveAudioChannels chflag = (OMTActiveAudioChannels)(1 << i);
                if ((srcActiveChannels & chflag) == chflag)
                {
                    Buffer.BlockCopy(src.Buffer, src.Offset + offset, dst.Buffer, dst.Offset + dstoffset, srcSamplesPerChannel * OMTConstants.AUDIO_SAMPLE_SIZE);
                    offset += srcSamplesPerChannel * 4;
                }
                else
                {
                    //>twice as fast as Array.Clear
                    Buffer.BlockCopy(zeroBuffer, 0, dst.Buffer, dst.Offset + dstoffset, srcSamplesPerChannel * OMTConstants.AUDIO_SAMPLE_SIZE); 
                }
                dstoffset += srcSamplesPerChannel * OMTConstants.AUDIO_SAMPLE_SIZE;
            }
            dst.SetBuffer(0, srcChannels * srcSamplesPerChannel * OMTConstants.AUDIO_SAMPLE_SIZE);
        }
        private static bool IsEmpty(OMTBuffer buff, int offset, int length)
        {
            for (int i = buff.Offset + offset; i < buff.Offset + offset + length; i++)
            {
                if (buff.Buffer[i] != 0) return false;
            }
            return true;
        }
        public static OMTActiveAudioChannels Encode(OMTBuffer src, int srcChannels, int srcSamplesPerChannel, OMTBuffer dst)
        {
            OMTActiveAudioChannels activeChannels = 0;
            int offset = 0;
            int dstoffset = 0;
            for (int i = 0; i < srcChannels; i++)
            {
                if (!IsEmpty(src, src.Offset + offset, srcSamplesPerChannel * OMTConstants.AUDIO_SAMPLE_SIZE))
                {
                    OMTActiveAudioChannels chflag = (OMTActiveAudioChannels)(1 << i);
                    Buffer.BlockCopy(src.Buffer, src.Offset + offset, dst.Buffer, dst.Offset + dstoffset, srcSamplesPerChannel * OMTConstants.AUDIO_SAMPLE_SIZE);
                    activeChannels = activeChannels | chflag;
                    dstoffset += srcSamplesPerChannel * OMTConstants.AUDIO_SAMPLE_SIZE;
                }
                offset += srcSamplesPerChannel * OMTConstants.AUDIO_SAMPLE_SIZE;
            }
            dst.SetBuffer(dst.Offset, dstoffset);
            return activeChannels;
        }

        protected override void DisposeInternal()
        {
            zeroBuffer = null;
            base.DisposeInternal();
        }
    }
}
