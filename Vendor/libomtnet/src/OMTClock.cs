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
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

namespace libomtnet
{    internal class OMTClock : OMTBase
    {
        private long lastTimestamp = -1;
        private Stopwatch clock = Stopwatch.StartNew();
        private long clockTimestamp = -1;
        private int frameRateN = -1;
        private int frameRateD = -1;
        private int sampleRate = -1;
        private long frameInterval = -1;
        private bool audio;
        public OMTClock(bool audio)
        {
            this.audio = audio;
        }

        public void Process(ref OMTMediaFrame frame)
        {
            if (audio && frame.SampleRate != sampleRate)
            {
                Reset(frame);
            } else if ((frame.FrameRateN != frameRateN) || frame.FrameRateD != frameRateD)
            {
                Reset(frame);
            }
            if (frame.Timestamp == -1)
            {
                if (lastTimestamp == -1)
                {
                    Reset(frame);
                    frame.Timestamp = 0;
                } else
                {
                    if (audio && sampleRate > 0 && frame.SamplesPerChannel > 0)
                    {
                        frameInterval = 10000000L * frame.SamplesPerChannel;
                        frameInterval /= sampleRate;
                    }
                    frame.Timestamp = lastTimestamp + frameInterval;
                    clockTimestamp += frameInterval;

                    long diff = clockTimestamp - (clock.ElapsedMilliseconds * 10000);
                    while (diff < -frameInterval)
                    {
                        frame.Timestamp += frameInterval;
                        clockTimestamp += frameInterval;
                        diff += frameInterval;
                    }
                    while (!Exiting && (clockTimestamp > clock.ElapsedMilliseconds * 10000))
                    {
                        Thread.Sleep(1);
                    }
                }
            }
            lastTimestamp = frame.Timestamp;
        }
        private void Reset(OMTMediaFrame frame)
        {
            frameRateD = frame.FrameRateD;
            frameRateN = frame.FrameRateN;
            sampleRate = frame.SampleRate;
            if (frame.FrameRate > 0)
            {
                frameInterval = (long)(10000000 / frame.FrameRate);
            } 
            clock = Stopwatch.StartNew();
            clockTimestamp = 0;
            Debug.WriteLine("OMTClock.Reset");
        }
    }
}
