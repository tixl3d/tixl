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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace libomtnet
{
    public class OMTSendReceiveBase : OMTBase
    {
        protected object videoLock = new object();
        protected object audioLock = new object();
        protected object metaLock = new object();

        protected AutoResetEvent metadataHandle;
        protected AutoResetEvent tallyHandle;
        protected IntPtr lastMetadata;
        protected OMTTally lastTally = new OMTTally();

        private Stopwatch timer = Stopwatch.StartNew();
        private long codecTime = 0;
        private long codecTimeSinceLast = 0;
        private long codecStartTime = 0;

        internal OMTRedirect redirect = null;

        /// <summary>
        /// Receives the current tally state across all connections to a Sender.
        /// If this function times out, the last known tally state will be received.
        /// </summary>
        /// <param name="millisecondsTimeout">milliseconds to wait for tally change. set to 0 to receive current tally</param>
        /// <param name="tally"></param>
        /// <returns></returns>
        public bool GetTally(int millisecondsTimeout, ref OMTTally tally)
        {
            if (Exiting) return false;
            if (millisecondsTimeout > 0)
            {
                if (tallyHandle != null)
                {
                    if (tallyHandle.WaitOne(millisecondsTimeout))
                    {
                        tally = lastTally;
                        return true;
                    }
                }
            }
            tally = lastTally;
            return false;
        }

        internal virtual OMTTally GetTallyInternal()
        {
            return new OMTTally();
        }

        internal void Channel_Changed(object sender, OMTEventArgs e)
        {
            if (Exiting) return; //Avoid deadlock where Channel may call back into sender while dispose is in progress.
            if (e.Type == OMTEventType.TallyChanged)
            {
                UpdateTally();
            } else if (e.Type == OMTEventType.Disconnected)
            {
                if (sender != null)
                {
                    OMTChannel ch = (OMTChannel)sender;
                    OnDisconnected(ch);
                }
            } else if (e.Type == OMTEventType.RedirectChanged)
            {
                OMTChannel ch = (OMTChannel)sender;
                OnRedirectChanged(ch);
            }
        }

        internal virtual void OnRedirectChanged(OMTChannel ch)
        {

        }

        internal virtual void OnDisconnected(OMTChannel ch)
        {

        }

        internal virtual void OnTallyChanged( OMTTally tally)
        {
        }

        internal void UpdateTally()
        {
            OMTTally tally = GetTallyInternal();
            if (tally.Preview != lastTally.Preview || tally.Program != lastTally.Program)
            {
                lastTally = tally;
                OnTallyChanged(lastTally);
                if (tallyHandle != null)
                {
                    tallyHandle.Set();
                }
            }
        }

        public virtual OMTStatistics GetVideoStatistics()
        {
            return new OMTStatistics();
        }
        public virtual OMTStatistics GetAudioStatistics()
        {
            return new OMTStatistics();
        }
        internal void BeginCodecTimer()
        {
            codecStartTime = timer.ElapsedMilliseconds;
        }
        internal void EndCodecTimer()
        {
            long v = (timer.ElapsedMilliseconds - codecStartTime);
            codecTime += v;
            codecTimeSinceLast += v;
        }
        internal void UpdateCodecTimerStatistics(ref OMTStatistics v)
        {
            v.CodecTime = codecTime;
            v.CodecTimeSinceLast = codecTimeSinceLast;
            codecTimeSinceLast = 0;
        }
        internal bool ReceiveMetadata(OMTMetadata frame, ref OMTMediaFrame outFrame)
        {
            lock (metaLock)
            {
                if (Exiting) return false;
                OMTMetadata.FreeIntPtr(lastMetadata);
                lastMetadata = IntPtr.Zero;
                outFrame.Type = OMTFrameType.Metadata;
                outFrame.Timestamp = frame.Timestamp;
                outFrame.Data = frame.ToIntPtr(ref outFrame.DataLength);
                lastMetadata = outFrame.Data;
                return true;
            }
        }

        protected override void DisposeInternal()
        {
            OMTMetadata.FreeIntPtr(lastMetadata);
            lastMetadata = IntPtr.Zero;
            base.DisposeInternal();
        }
    }
}
