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
using System.Net;
using System.Net.Sockets;
using libomtnet.codecs;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.Xml;

namespace libomtnet
{
    public class OMTSend : OMTSendReceiveBase
    {
        private readonly OMTAddress address;
        private Socket listener;
        private OMTChannel[] channels = { };
        private object channelsLock = new object();
        private OMTDiscovery discovery;
        private OMTDiscoveryServer discoveryServer;

        private OMTFrame tempVideo;
        private OMTFrame tempAudio;
        private OMTBuffer tempAudioBuffer;
        private OMTVMX1Codec codec = null;
        private OMTQuality quality = OMTQuality.Default;
        private SocketAsyncEventArgs listenEvent;

        private OMTQuality suggestedQuality = OMTQuality.Default;
        private string senderInfoXml = null;
        private List<string> connectionMetadata = new List<string>();

        private OMTClock videoClock;
        private OMTClock audioClock;

        private bool metadataServer = false;

        internal OMTSend(IPEndPoint endpoint, OMTDiscoveryServer discoveryServer)
        {
            this.metadataServer = true;
            this.discoveryServer = discoveryServer;
            metadataHandle = new AutoResetEvent(false);
            listenEvent = new SocketAsyncEventArgs();
            listenEvent.Completed += OnAccept;
            this.listener = OMTUtils.CreateStreamSocket();
            this.listener.Bind(OMTUtils.MatchSocketFamily(this.listener, endpoint));
            this.listener.Listen(5);
            BeginAccept();
        }

        /// <summary>
        /// Create a new instance of the OMT Sender
        /// </summary>
        /// <param name="name">Specify the name of the source not including hostname</param>
        /// <param name="quality"> Specify the quality to use for video encoding. If Default, this can be automatically adjusted based on Receiver requirements.</param>
        public OMTSend(string name, OMTQuality quality)
        {
            videoClock = new OMTClock(false);
            audioClock = new OMTClock(true);
            metadataHandle = new AutoResetEvent(false);
            tallyHandle = new AutoResetEvent(false);
            listenEvent = new SocketAsyncEventArgs();
            listenEvent.Completed += OnAccept;
            tempVideo = new OMTFrame(OMTFrameType.Video, new OMTBuffer(OMTConstants.VIDEO_MIN_SIZE, true));
            tempAudio = new OMTFrame(OMTFrameType.Audio, new OMTBuffer(OMTConstants.AUDIO_MIN_SIZE, true));
            tempAudioBuffer = new OMTBuffer(OMTConstants.AUDIO_MIN_SIZE, true);
            this.discovery = OMTDiscovery.GetInstance();
            this.quality = quality;
            this.suggestedQuality = quality;
            this.listener = OMTUtils.CreateStreamSocket();

            OMTSettings settings = OMTSettings.GetInstance();
            int startPort = settings.GetInteger("NetworkPortStart", OMTConstants.NETWORK_PORT_START);
            int endPort = settings.GetInteger("NetworkPortEnd", OMTConstants.NETWORK_PORT_END);

            for (int i = startPort; i <= endPort; i++)
            {
                try
                {
                    this.listener.Bind(new IPEndPoint(this.listener.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, i));
                    this.listener.Listen(5);
                    break;
                }
                catch (SocketException se)
                {
                    if (se.SocketErrorCode != SocketError.AddressAlreadyInUse | i == OMTConstants.NETWORK_PORT_END)
                    {
                        throw se;
                    }
                }
            }

            BeginAccept();
            IPEndPoint ip = (IPEndPoint)this.listener.LocalEndPoint;
            this.address = new OMTAddress(name, ip.Port);
            this.address.AddAddress(IPAddress.Loopback);
            this.discovery.RegisterAddress(address);        }

        public override OMTStatistics GetVideoStatistics()
        {
            OMTChannel[] ch = channels;
            if (ch != null)
            {
                foreach (OMTChannel c in ch)
                {
                    if (c.IsVideo() && c.Connected)
                    {
                        OMTStatistics s = c.GetStatistics();
                        UpdateCodecTimerStatistics(ref s);
                        return s;
                    }
                }
            }
            return base.GetVideoStatistics();
        }

        public override OMTStatistics GetAudioStatistics()
        {
            OMTChannel[] ch = channels;
            if (ch != null)
            {
                foreach (OMTChannel c in ch)
                {
                    if (c.IsAudio() && c.Connected)
                    {
                        return c.GetStatistics();
                    }
                }
            }
            return base.GetAudioStatistics();
        }

        public int Port { get { return this.address.Port; } }

        /// <summary>
        /// Specify information to describe the Sender to any Receivers
        /// </summary>
        /// <param name="senderInfo"></param>
        public void SetSenderInformation(OMTSenderInfo senderInfo)
        {
            if (senderInfo == null)
            {
                this.senderInfoXml = null;
            } else
            {
                this.senderInfoXml = senderInfo.ToXML();
                SendMetadata(new OMTMetadata(0, this.senderInfoXml), null);
            } 
        }

        private void SendConnectionMetadata()
        {
            lock (connectionMetadata)
            {
                foreach (string metadata in connectionMetadata)
                {
                    if (!String.IsNullOrEmpty(metadata))
                    {
                        SendMetadata(new OMTMetadata(0, metadata), null);
                    }
                }
            }
        }
        private void SendConnectionMetadata(OMTChannel ch)
        {
            lock (connectionMetadata)
            {
                foreach (string metadata in connectionMetadata)
                {
                    if (!String.IsNullOrEmpty(metadata))
                    {
                        ch.Send(new OMTMetadata(0, metadata));
                    }
                }
            }
        }

        public void AddConnectionMetadata(string xml)
        {
            lock (connectionMetadata)
            {
                connectionMetadata.Add(xml);
            } 
        }

        public void ClearConnectionMetadata()
        {
            lock (connectionMetadata)
            {
                connectionMetadata.Clear();
            }
        }

        /// <summary>
        /// Use this to inform receivers to connect to a different address.
        /// 
        /// This is used to create a "virtual source" that can be dynamically switched as needed.
        /// 
        /// This is useful for scenarios where receiver needs to be changed remotely.
        /// </summary>
        /// <param name="newAddress">The new address. Set to null or empty to disable redirect.</param>
        public void SetRedirect(string newAddress)
        {
            if (redirect == null) redirect = new OMTRedirect(this);
            redirect.SetRedirect(newAddress);
        }
        protected override void DisposeInternal()
        {
            if (tallyHandle != null)
            {
                tallyHandle.Set();
            }
            if (metadataHandle != null)
            {
                metadataHandle.Set();
            }
            if (videoClock != null)
            {
                videoClock.Dispose();
            }
            if (audioClock != null)
            {
                audioClock.Dispose();
            }
            lock (videoLock) { }      
            lock (audioLock) { }
            lock (metaLock) { }
            if (redirect != null)
            {
                redirect.Dispose();
                redirect = null;
            }
            if (discovery != null)
            {
                discovery.DeregisterAddress(address);
                discovery = null;
            }
            discoveryServer = null;
            if (listener != null)
            {
                listener.Dispose();
                listener = null;
            }
            if (listenEvent != null)
            {
                listenEvent.Completed -= OnAccept;
                listenEvent.Dispose();
                listenEvent = null;
            }
            lock (channelsLock)
            {
                if (channels != null)
                {
                    foreach (OMTChannel channel in channels)
                    {
                        if (channel != null)
                        {
                            channel.Changed -= Channel_Changed;
                            channel.Dispose();
                        }
                    }
                    channels = null;
                }
            }
            if (codec != null)
            { 
                codec.Dispose(); 
                codec = null;
            }
            discovery = null;
            OMTMetadata.FreeIntPtr(lastMetadata);
            lastMetadata = IntPtr.Zero;
            if (metadataHandle != null)
            {
                metadataHandle.Close();
                metadataHandle = null;
            }
            if (tallyHandle != null)
            {
                tallyHandle.Close();
                tallyHandle = null;
            }
            if (tempVideo != null)
            {
                tempVideo.Dispose();
                tempVideo = null;
            }
            if (tempAudio != null)
            {
                tempAudio.Dispose();
                tempAudio = null;
            }
            if (tempAudioBuffer != null)
            {
                tempAudioBuffer.Dispose();
                tempAudioBuffer = null;
            }
            base.DisposeInternal();
        }

        /// <summary>
        /// Discovery address in the format HOSTNAME (NAME)
        /// </summary>
        public string Address { get { return address.ToString(); } }

        /// <summary>
        /// Direct connection address in the format omt://hostname:port
        /// </summary>
        public string URL { get { return address.ToURL(); } }

        /// <summary>
        /// Total number of connections to this sender. Receivers establish one connection for video/metadata and a second for audio.
        /// </summary>
        public int Connections { get { 
                
                OMTChannel[] ch = channels;
                if (ch != null)
                {
                    return ch.Length;
                }
                return 0;
            
            } }

        private void OnAccept(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                if (e.SocketError == SocketError.Success)
                {
                    Socket socket = null;
                    OMTChannel channel = null;
                    try
                    {
                        socket = e.AcceptSocket;
                        channel = new OMTChannel(socket, OMTFrameType.Metadata, null, metadataHandle, metadataServer);
                        channel.StartReceive();
                        if (senderInfoXml != null)
                        {
                            channel.Send(new OMTMetadata(0, senderInfoXml));
                        }
                        SendConnectionMetadata(channel);
                        channel.Send(OMTMetadata.FromTally(lastTally));
                        if (redirect != null)
                        {
                            redirect.OnNewConnection(channel);
                        }
                        OMTLogging.Write("AddConnection: " + socket.RemoteEndPoint.ToString(), "OMTSend.BeginAccept");
                        AddChannel(channel);
                    }
                    catch (Exception ex)
                    {
                        OMTLogging.Write(ex.ToString(), "OMTSend.BeginAccept");
                        if (channel != null)
                        {
                            channel.Changed -= Channel_Changed;
                            channel.Dispose();
                        }
                        if (socket != null)
                        {
                            socket.Dispose();
                        }
                    }
                }
                if (!Exiting)
                {
                    BeginAccept();
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTSend.OnAccept");
            }            
        }
        private void BeginAccept()
        {
            Socket listener = this.listener;
            if (listener != null)
            {
                listenEvent.AcceptSocket = null;
                if (this.listener.AcceptAsync(listenEvent) == false) {
                    OnAccept(this.listener, listenEvent);
                }
            }                
        }
        internal void AddChannel(OMTChannel channel)
        {
            lock (channelsLock)
            {
                List<OMTChannel> list = new List<OMTChannel>();
                list.AddRange(channels);
                list.Add(channel);
                channels = list.ToArray();
            }
            channel.Changed += Channel_Changed;
            UpdateTally();
            if (discoveryServer != null)
            {
                discoveryServer.Connected(channel.RemoteEndPoint);
            }
        }
        internal bool RemoveChannel(OMTChannel channel)
        {
            lock (channelsLock)
            {
                if (channel != null)
                {
                    List<OMTChannel> list = new List<OMTChannel>();
                    list.AddRange(channels);
                    if (list.Contains(channel))
                    {
                        list.Remove(channel);
                        channels = list.ToArray();
                        channel.Changed -= Channel_Changed;
                        channel.Dispose();
                        OMTLogging.Write("RemoveConnection", "OMTSend.RemoveChannel");
                        return true;
                    }
                }
            }
            return false;

        }

        /// <summary>
        /// Sets the video encoding quality from the next frame. If set to Default will defer to the suggested quality amongst receivers. See OMTQuality for more details.
        /// </summary>
        public OMTQuality Quality { get { return quality; } set { 
                quality = value; 
                if (quality != OMTQuality.Default)
                {
                    suggestedQuality = quality;
                }
            } }

        internal override void OnTallyChanged(OMTTally tally)
        {
            SendMetadata(OMTMetadata.FromTally(tally),null);
        }
        internal override void OnDisconnected(OMTChannel ch)
        {
            if (ch != null)
            {
                if (RemoveChannel(ch))
                {
                    if (discoveryServer != null)
                    {
                        discoveryServer.Disconnected(ch.RemoteEndPoint);
                    }
                    UpdateTally();
                }
            }
        }

        internal int Send(OMTFrame frame)
        {
            int len = 0;
            OMTQuality suggested = OMTQuality.Default;
            OMTChannel[] channels = this.channels;
            if (channels != null)
            {
                for (int i = 0; i < channels.Length; i++)
                {
                    if (channels[i].Connected)
                    {
                        len += channels[i].Send(frame);
                        if (channels[i].IsVideo())
                        {
                            if (channels[i].SuggestedQuality > suggested)
                            {
                                suggested = channels[i].SuggestedQuality;
                            }
                        }
                    }
                }
                if (quality == OMTQuality.Default)
                {
                    suggestedQuality = suggested;
                }
            }
            return len;
        }
        private void CreateCodec(int width, int height, int framesPerSecond, VMXColorSpace colorSpace)
        {
            VMXProfile prof = VMXProfile.Default;
            if (suggestedQuality != OMTQuality.Default)
            {
                if (suggestedQuality >= OMTQuality.Low) prof = VMXProfile.OMT_LQ;
                if (suggestedQuality >= OMTQuality.Medium) prof = VMXProfile.OMT_SQ;
                if (suggestedQuality >= OMTQuality.High) prof = VMXProfile.OMT_HQ;
            }
            if (codec == null)
            {
                codec = new OMTVMX1Codec(width, height, framesPerSecond, prof, colorSpace);
            }
            else if (codec.Width != width || codec.Height != height || codec.Profile != prof || codec.ColorSpace != colorSpace || codec.FramesPerSecond != framesPerSecond)
            {
                int lastQuality = codec.GetQuality();
                codec.Dispose();
                codec = new OMTVMX1Codec(width, height, framesPerSecond, prof, colorSpace);
                codec.SetQuality(lastQuality); //Preserve the last quality in cases of profile change, so there isn't a temporarily drop in quality.
            }
        }

        /// <summary>
        /// Receive any available metadata in the buffer, or wait for metadata if empty
        /// 
        /// Returns true if metadata was found, false of timed out
        /// </summary>
        /// <param name="millisecondsTimeout">The maximum time to wait for a new frame if empty</param>
        /// <param name="outFrame">The frame struct to fill with the received data</param>
        public bool Receive(int millisecondsTimeout, ref OMTMediaFrame outFrame)
        {
            OMTMetadata metadata = null;
            if (Receive(millisecondsTimeout, ref metadata))
            {
                return ReceiveMetadata(metadata, ref outFrame);
            }
            return false;
        }
        internal bool Receive(int millisecondsTimeout, ref OMTMetadata metadata)
        {
            lock (metaLock)
            {
                if (Exiting) return false;
                if (ReceiveInternal(ref metadata)) return true;
                for (int i = 0; i < 2; i++)
                {
                    if (metadataHandle.WaitOne(millisecondsTimeout) == false) return false;
                    if (Exiting) return false;
                    if (ReceiveInternal(ref metadata)) return true;
                }
            }
            return false;
        }

        private bool ReceiveInternal(ref OMTMetadata frame)
        {
            OMTChannel[] channels = this.channels;
            for (int i = 0; i < channels.Length; i++)
            {
                OMTChannel ch = channels[i];
                if (ch != null)
                {
                    if (ch.ReadyMetadataCount > 0)
                    {
                        frame = ch.ReceiveMetadata();
                        return true;
                    }
                }
            }
            return false;
        }

        internal override OMTTally GetTallyInternal()
        {
            OMTTally tally = new OMTTally();
            OMTChannel[] channels = this.channels;
            if (channels != null)
            {
                for (int i = 0; i < channels.Length; i++)
                {
                    OMTTally t = channels[i].GetTally();
                    if (t.Program == 1) tally.Program = 1;
                    if (t.Preview == 1) tally.Preview = 1;
                }
            }
            return tally;
        }

        /// <summary>
        /// Send a frame to any receivers currently connected. 
        /// 
        /// Video: 'UYVY', 'YUY2', 'NV12', 'YV12, 'BGRA', 'UYVA', 'VMX1' are supported (BGRA will be treated as BGRX and UYVA as UYVY where alpha flags are not set)
        /// 
        /// Audio: Supports planar 32bit floating point audio
        /// 
        /// Metadata: Supports UTF8 encoded XML 
        /// </summary>
        /// <param name="frame">The frame to send</param>
        public int Send(OMTMediaFrame frame)
        {
            if (Exiting) return 0;
            if (frame.Type == OMTFrameType.Video)
            {
                return SendVideo(frame);
            }
            else if (frame.Type == OMTFrameType.Audio)
            {
                return SendAudio(frame);
            }
            else if (frame.Type == OMTFrameType.Metadata)
            {
                return SendMetadata(frame);
            }
            return 0;
        }

        private int SendMetadata(OMTMediaFrame metadata)
        {
            OMTMetadata m = OMTMetadata.FromMediaFrame(metadata);
            if (m != null)
            {
                return SendMetadata(m, null);
            }
            return 0;
        }

        internal int SendMetadata(OMTMetadata metadata, IPEndPoint endpoint)
        {
            lock (metaLock)
            {
                if (Exiting) return 0;
                int len = 0;
                OMTChannel[] channels = this.channels;
                if (channels != null)
                {
                    for (int i = 0; i < channels.Length; i++)
                    {
                        OMTChannel ch = channels[i];
                        if (ch.IsMetadata())
                        {
                            if (endpoint == null || ch.RemoteEndPoint == endpoint)
                            {
                                len += channels[i].Send(metadata);
                            }
                        }
                    }
                }
                return len;
            }
        }

        private int SendVideo(OMTMediaFrame frame)
        {
            lock (videoLock)
            {
                if (Exiting) return 0;
                if (frame.Data != IntPtr.Zero && frame.DataLength > 0)
                {
                    tempVideo.Data.Resize(frame.DataLength + frame.FrameMetadataLength);

                    if ((frame.Codec == (int)OMTCodec.UYVY) || (frame.Codec == (int)OMTCodec.BGRA) ||
                        (frame.Codec == (int)OMTCodec.YUY2) || (frame.Codec == (int)OMTCodec.NV12) || 
                        (frame.Codec == (int)OMTCodec.YV12) || (frame.Codec == (int)OMTCodec.UYVA) ||
                        (frame.Codec == (int)OMTCodec.P216) || (frame.Codec == (int)OMTCodec.PA16)
                        )
                    {
                        if (frame.Width >= 16 && frame.Height >= 16 && frame.Stride >= frame.Width)
                        {
                            bool interlaced = frame.Flags.HasFlag(OMTVideoFlags.Interlaced);
                            bool alpha = frame.Flags.HasFlag(OMTVideoFlags.Alpha);

                            CreateCodec(frame.Width, frame.Height, (int)frame.FrameRate, (VMXColorSpace)frame.ColorSpace);
                            byte[] buffer = tempVideo.Data.Buffer;
                            int len;
                            BeginCodecTimer();
                            VMXImageType itype = VMXImageType.None;
                            if (frame.Codec == (int)OMTCodec.UYVY)
                            {
                                itype = VMXImageType.UYVY;                                
                            }
                            else if (frame.Codec == (int)OMTCodec.YUY2)
                            {
                                itype = VMXImageType.YUY2;
                            }
                            else if (frame.Codec == (int)OMTCodec.NV12)
                            {
                                itype = VMXImageType.NV12;
                            } else if (frame.Codec == (int)OMTCodec.YV12)
                            {
                                itype = VMXImageType.YV12;
                            }
                            else if (frame.Codec == (int)OMTCodec.BGRA)
                            {
                                if (alpha)
                                {
                                    itype = VMXImageType.BGRA;
                                } else
                                {
                                    itype = VMXImageType.BGRX;
                                } 
                            }
                            else if (frame.Codec == (int)OMTCodec.UYVA)
                            {
                                if (alpha)
                                {
                                    itype = VMXImageType.UYVA;
                                }
                                else
                                {
                                    itype = VMXImageType.UYVY;
                                }
                            }
                            else if (frame.Codec == (int)OMTCodec.PA16)
                            {
                                frame.Flags |= OMTVideoFlags.HighBitDepth;
                                if (alpha)
                                {
                                    itype = VMXImageType.PA16;                                    
                                }
                                else
                                {
                                    itype = VMXImageType.P216;
                                }
                            } else if (frame.Codec == (int)OMTCodec.P216)
                            {
                                frame.Flags |= OMTVideoFlags.HighBitDepth;
                                itype = VMXImageType.P216;
                            }
                            len = codec.Encode(itype, frame.Data, frame.Stride, buffer, interlaced);
                            EndCodecTimer();
                            if (len > 0)
                            {
                                if (frame.FrameMetadataLength > 0)
                                {
                                    tempVideo.Data.SetBuffer(len, len);
                                    tempVideo.Data.Append(frame.FrameMetadata,0,frame.FrameMetadataLength);
                                }
                                tempVideo.SetDataLength(len + frame.FrameMetadataLength);
                                tempVideo.SetMetadataLength(frame.FrameMetadataLength);
                                tempVideo.SetPreviewDataLength(codec.GetEncodedPreviewLength() + frame.FrameMetadataLength);
                                tempVideo.ConfigureVideo((int)OMTCodec.VMX1, frame.Width, frame.Height, frame.FrameRateN, frame.FrameRateD, frame.AspectRatio, frame.Flags, frame.ColorSpace);
                                videoClock.Process(ref frame);
                                tempVideo.Timestamp = frame.Timestamp;
                                return Send(tempVideo);
                            }
                            else
                            {
                                OMTLogging.Write("Encoding failed at timestamp: " + frame.Timestamp, "OMTSend.SendVideo");
                            }

                        }
                        else
                        {
                            OMTLogging.Write("Frame dimensions invalid: " + frame.Width + "x" + frame.Height + " Stride: " + frame.Stride, "OMTSend.SendVideo");
                        }
                    } else if (frame.Codec == (int)OMTCodec.VMX1)
                    {
                        if (frame.DataLength > 0)
                        {
                            tempVideo.SetDataLength(frame.DataLength + frame.FrameMetadataLength);
                            tempVideo.SetMetadataLength(frame.FrameMetadataLength);
                            tempVideo.SetPreviewDataLength(frame.DataLength + frame.FrameMetadataLength);                            
                            Marshal.Copy(frame.Data, tempVideo.Data.Buffer, 0, frame.DataLength);
                            if (frame.FrameMetadataLength > 0)
                            {
                                Marshal.Copy(frame.FrameMetadata, tempVideo.Data.Buffer,frame.DataLength, frame.FrameMetadataLength);
                            }
                            tempVideo.ConfigureVideo((int)OMTCodec.VMX1, frame.Width, frame.Height, frame.FrameRateN, frame.FrameRateD, frame.AspectRatio, frame.Flags, frame.ColorSpace);
                            videoClock.Process(ref frame);
                            tempVideo.Timestamp = frame.Timestamp;
                            return Send(tempVideo);
                        } else
                        {
                            OMTLogging.Write("Frame DataLength invalid", "OMTSend.SendVideo");
                        }
                    }
                    else
                    {
                        OMTLogging.Write("Codec not supported: " + frame.Codec, "OMTSend.SendVideo");
                    }
                }
            }
            return 0;
        }
        private int SendAudio(OMTMediaFrame frame)
        {
            lock (audioLock)
            {
                if (Exiting) return 0;
                if (frame.Data != IntPtr.Zero && frame.DataLength > 0 && frame.Channels > 0 && frame.SampleRate > 0 && frame.SamplesPerChannel > 0 && frame.Channels <= 32)
                {
                    if (frame.DataLength > OMTConstants.AUDIO_MAX_SIZE)
                    {
                        OMTLogging.Write("Audio DataLength exceeded maximum: " + frame.DataLength, "OMTSend");
                        return 0;
                    }
                    tempAudioBuffer.Resize(frame.DataLength);
                    tempAudio.Data.Resize(frame.DataLength + frame.FrameMetadataLength);
                    Marshal.Copy(frame.Data, tempAudioBuffer.Buffer, 0, frame.DataLength);
                    tempAudioBuffer.SetBuffer(0, frame.DataLength);
                    tempAudio.Data.SetBuffer(0, 0);
                    OMTActiveAudioChannels ch = OMTFPA1Codec.Encode(tempAudioBuffer, frame.Channels, frame.SamplesPerChannel, tempAudio.Data);
                    if (frame.FrameMetadataLength > 0 && frame.FrameMetadata != IntPtr.Zero)
                    {
                        tempAudio.Data.Append(frame.FrameMetadata,0, frame.FrameMetadataLength);
                    }
                    tempAudio.SetDataLength(tempAudio.Data.Length);
                    tempAudio.SetMetadataLength(frame.FrameMetadataLength);
                    tempAudio.ConfigureAudio(frame.SampleRate, frame.Channels, frame.SamplesPerChannel, ch);
                    audioClock.Process(ref frame);
                    tempAudio.Timestamp = frame.Timestamp;
                    return Send(tempAudio);
                }
            }
            return 0;
        }

    }
}
