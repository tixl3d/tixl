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
using System.Threading;
using libomtnet.codecs;
using System.Runtime.InteropServices;
using System.Diagnostics.SymbolStore;
using System.Runtime.CompilerServices;

namespace libomtnet
{
    public class OMTReceive : OMTSendReceiveBase, IOMTDiscoveryNotify
    {
        private OMTChannel videoChannel;
        private OMTChannel audioChannel;
        private OMTFrameType frameTypes;
        private readonly string address;
        private readonly object connectSync = new object();
        private OMTPinnedBuffer tempAudio;
        private OMTPinnedBuffer tempVideo;
        private OMTPinnedBuffer tempMetaAudio;
        private OMTPinnedBuffer tempMetaVideo;
        private int tempVideoStride;
        private OMTTally tally;
        private OMTReceiveFlags flags;
        private OMTPreferredVideoFormat preferredVideoFormat;
        private OMTVMX1Codec codec = null;

        private IntPtr tempCompressedVideo = IntPtr.Zero;

        private AutoResetEvent videoHandle;
        private AutoResetEvent audioHandle;

        private WaitHandle[] allWaitHandles = { };
        private WaitHandle[] videoHandles = { };
        private WaitHandle[] audioHandles = { };
        private WaitHandle[] avHandles = { };
        private WaitHandle[] metaDataHandles = { };

        private OMTQuality suggestedQuality = OMTQuality.Default;

        private OMTFPA1Codec audioCodec = null;

        private ConnectionState videoConnectionState = null;
        private ConnectionState audioConnectionState = null;

        private OMTDiscovery discovery = null;
        private OMTDiscoveryClient discoveryClient = null;

        private DateTime lastBeginConnect = DateTime.MinValue;

        // Automatic reconnect after the channel died (sender restart, network blip). Discovery
        // re-announcements also reconnect via Notify, but a sender that comes back without one
        // used to leave this receiver bound to a dead channel until it was re-bound by hand.
        private Timer reconnectTimer = null;
        private int reconnectDelayMs = 1000;

        internal delegate void RedirectChangedEventHandler(object sender, OMTRedirectChangedEventArgs e);
        internal event RedirectChangedEventHandler RedirectChanged;
        private string redirectAddress = null;
        internal bool redirectMetadataOnly = false;

        private OMTFrame lastVideoFrame = null;
        private OMTFrame lastAudioFrame = null;

        private class ConnectionState
        {
            public OMTFrameType frameType;
            public Socket socket;
            public OMTMultiConnectSocket multiSocket;
            public IAsyncResult result;

            //This is set to true to gracefully ignore any successful pending socket connect callbacks when we use Reconnect. 
            public bool cancelled;
        }

        private void DestroyWaitHandles()
        {
            if (videoHandle != null) videoHandle.Close();
            if (audioHandle != null) audioHandle.Close();
            if (metadataHandle != null) metadataHandle.Close();
            if (tallyHandle != null) tallyHandle.Close();
            videoHandle = null;
            audioHandle = null;
            metadataHandle = null;
            tallyHandle = null;
        }
        private void SetupWaitHandles()
        {
            videoHandle = new AutoResetEvent(false);
            audioHandle = new AutoResetEvent(false);
            metadataHandle = new AutoResetEvent(false);
            tallyHandle = new AutoResetEvent(false);

            List<WaitHandle> allh = new List<WaitHandle>();
            List<WaitHandle> vh = new List<WaitHandle>();
            List<WaitHandle> ah = new List<WaitHandle>();
            List<WaitHandle> mh = new List<WaitHandle>();
            List<WaitHandle> avh = new List<WaitHandle>();

            allh.Add(videoHandle);
            allh.Add(audioHandle);
            allh.Add(metadataHandle);

            mh.Add(metadataHandle);

            vh.Add(videoHandle);
            avh.Add(videoHandle);
            
            ah.Add(audioHandle);
            avh.Add(audioHandle);

            allWaitHandles = allh.ToArray();
            videoHandles = vh.ToArray();
            audioHandles = ah.ToArray();
            avHandles = avh.ToArray();
            metaDataHandles = mh.ToArray();
        }

        /// <summary>
        /// Create a new Receiver and begin connecting to the Sender specified by address
        /// </summary>
        /// <param name="address">Address to connect to, either the full name provided by OMTDiscovery or a URL in the format omt://hostname:port</param>
        /// <param name="frameTypes">Specify the types of frames to receive, for example to setup audio only or metadata only feeds</param>
        /// <param name="format">Specify the preferred uncompressed video format to receive. UYVYorBGRA will only receive BGRA frames when an alpha channel is present.</param>
        /// <param name="flags">Specify optional flags such as requesting a Preview feed only, or including the compressed (VMX) data with each frame for further processing (or recording).</param>
        public OMTReceive(string address, OMTFrameType frameTypes, OMTPreferredVideoFormat format, OMTReceiveFlags flags) {
            SetupWaitHandles();
            this.preferredVideoFormat = format;
            this.flags = flags;
            this.frameTypes = frameTypes;
            this.address = address;
            this.audioCodec = new OMTFPA1Codec(OMTConstants.AUDIO_MAX_SIZE);
            if (flags.HasFlag(OMTReceiveFlags.IncludeCompressed) || flags.HasFlag(OMTReceiveFlags.CompressedOnly))
            {
                tempCompressedVideo = Marshal.AllocHGlobal(OMTConstants.VIDEO_MAX_SIZE);
            }
            discovery = OMTDiscovery.GetInstance();
            BeginConnect();
            discovery.Subscribe(this);
        }

        internal OMTReceive(string address, OMTDiscoveryClient discoveryClient)
        {
            this.discoveryClient = discoveryClient;
            SetupWaitHandles();
            this.frameTypes = OMTFrameType.Metadata;
            this.address = address;
            BeginConnect();
        }

        public override OMTStatistics GetVideoStatistics()
        {
            OMTChannel ch = videoChannel;
            if (ch != null)
            {
                OMTStatistics s = ch.GetStatistics();
                UpdateCodecTimerStatistics(ref s);
                return s;
            }
            return base.GetVideoStatistics();
        }

        public override OMTStatistics GetAudioStatistics()
        {
            OMTChannel ch = audioChannel;
            if (ch != null)
            {
                return ch.GetStatistics();
            }
            return base.GetAudioStatistics();
        }

        private void CreateCodec(int width, int height, int framesPerSecond, VMXColorSpace colorSpace)
        {
            if (codec == null)
            {
                codec = new OMTVMX1Codec(width, height, framesPerSecond, VMXProfile.Default, colorSpace);
            }
            else if (codec.Width != width || codec.Height != height || codec.ColorSpace != colorSpace ||  codec.FramesPerSecond != framesPerSecond)
            {
                codec.Dispose();
                codec = new OMTVMX1Codec(width, height, framesPerSecond, VMXProfile.Default, colorSpace);
            }
            tempVideoStride = width * 4;
            int len = tempVideoStride * height * 2;
            if (tempVideo == null)
            {
                tempVideo = new OMTPinnedBuffer(len);
            }
            else if (tempVideo.Length < len)
            {
                tempVideo.Dispose();
                tempVideo = new OMTPinnedBuffer(len);
            }
        }

        protected override void DisposeInternal()
        {
            if (discovery != null) discovery.Unsubscribe(this);
            discovery = null;
            discoveryClient = null;

            if (videoHandle != null) videoHandle.Set();
            if (audioHandle != null) audioHandle.Set();
            if (metadataHandle != null) metadataHandle.Set();
            if (tallyHandle != null) tallyHandle.Set();

            lock (videoLock) { }
            lock (audioLock) { }
            lock (metaLock) { }
            lock (connectSync)
            {
                frameTypes = OMTFrameType.None;
                CloseChannels();
            }

            DestroyWaitHandles();

            if (redirect != null)
            {
                redirect.Dispose();
                redirect = null;
            }
            if (codec != null)
            {
                codec.Dispose();
                codec = null;
            }
            if (tempCompressedVideo != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(tempCompressedVideo);
                tempCompressedVideo = IntPtr.Zero;
            }
            if (tempAudio != null)
            {
                tempAudio.Dispose();
                tempAudio = null;
            }
            if (tempVideo != null)
            {
                tempVideo.Dispose();
                tempVideo = null;
            }
            if (tempMetaAudio != null)
            {
                tempMetaAudio.Dispose();
                tempMetaAudio = null;
            }
            if (tempMetaVideo != null)
            {
                tempMetaVideo.Dispose();
                tempMetaVideo = null;
            }
            if (lastAudioFrame != null)
            {
                lastAudioFrame.Dispose();
                lastAudioFrame = null;
            }
            if (lastVideoFrame != null)
            {
                lastVideoFrame.Dispose();
                lastVideoFrame = null;
            }
            reconnectTimer?.Dispose();
            reconnectTimer = null;
            base.DisposeInternal();
        }
               
        private string GetActualAddress()
        {
            if (!String.IsNullOrEmpty(this.redirectAddress))
            {
                return this.redirectAddress;
            }
            return address;
        }

        void IOMTDiscoveryNotify.Notify(OMTAddress address)
        {
            if (GetActualAddress() == address.ToString())
            {
                BeginConnect(address);
            }
        }
        private void CloseChannels()
        {
            if (videoChannel != null) {
                videoChannel.Changed -= Channel_Changed;
                videoChannel.Dispose(); 
                videoChannel = null;
            }
            if (audioChannel != null) {
                audioChannel.Changed -= Channel_Changed;
                audioChannel.Dispose(); 
                audioChannel = null;
            }
            //For reconnects where a connection was already in progress and not yet completed, clearing these allows new connection will proceed.
            if (audioConnectionState != null)
            {
                audioConnectionState.cancelled = true;
                audioConnectionState = null;
            }
            if (videoConnectionState != null)
            {
                videoConnectionState.cancelled = true;
                videoConnectionState = null;
            }
        }

        private void BeginConnect()
        {
            if (lastBeginConnect > DateTime.Now.AddSeconds(-1)) return;
            lastBeginConnect = DateTime.Now;      
            if (discovery != null)
            {
                OMTAddress address = discovery.FindByFullNameOrUrl(GetActualAddress());
                if (address != null)
                {
                    BeginConnect(address);
                }
            }
            else
            {
                int port = OMTPublicConstants.DISCOVERY_SERVER_DEFAULT_PORT;
                OMTAddress address = OMTDiscovery.CreateFromUrl(GetActualAddress(), port);
                if (address != null)
                {
                    BeginConnect(address);
                }
            }
        }
        private void BeginConnect(OMTAddress address)
        {
            lock (connectSync)
            {
                if (Exiting) return;
                if (!IsConnected())
                {
                    IPAddress[] addresses = address.Addresses;
                    if (addresses.Length > 0)
                    {
                        if (audioConnectionState == null && videoConnectionState == null)
                        {
                            CloseChannels();
                            if (frameTypes.HasFlag(OMTFrameType.Video) && videoConnectionState == null)
                            {
                                OMTLogging.Write("ConnectingVideo: " + address.ToString() + ":" + address.Port, "OMTReceive.BeginConnect");
                                videoConnectionState = BeginConnect(OMTFrameType.Video, address.Addresses, address.Port);
                            }
                            if (frameTypes.HasFlag(OMTFrameType.Audio) && audioConnectionState == null)
                            {
                                OMTLogging.Write("ConnectingAudio: " + address.ToString() + ":" + address.Port, "OMTReceive.BeginConnect");
                                audioConnectionState = BeginConnect(OMTFrameType.Audio, address.Addresses, address.Port);
                            }
                            if (frameTypes == OMTFrameType.Metadata && videoConnectionState == null)
                            {
                                OMTLogging.Write("ConnectingMetadata: " + address.ToString() + ":" + address.Port, "OMTReceive.BeginConnect");
                                videoConnectionState = BeginConnect(OMTFrameType.Metadata, address.Addresses, address.Port);
                            }
                        }
                    }
                }
            }
        }

        private ConnectionState BeginConnect(OMTFrameType frameType, IPAddress[] ips, int port)
        {
            ConnectionState cs = new ConnectionState();
            cs.cancelled = false;
            cs.frameType = frameType;
            //cs.socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            //cs.socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
            //cs.result = cs.socket.BeginConnect(ips, port, ConnectionCompleted, cs);
            cs.multiSocket = new OMTMultiConnectSocket(ips, port, OMTConstants.NETWORK_CONNECTION_TIMEOUT);
            cs.result = cs.multiSocket.BeginConnect(ConnectionCompleted, cs);
            if (cs.result.CompletedSynchronously)
            {
                return null;
            }
            return cs;
        }

        private void ConnectionCompleted(IAsyncResult ar)
        {
            lock (connectSync)
            {
                ConnectionState cs = (ConnectionState)ar.AsyncState;
                try
                {
                    //cs.socket.EndConnect(ar);
                    cs.socket = cs.multiSocket.EndConnect(ar);
                    if (ar.IsCompleted)
                    {
                        if (cs.cancelled)
                        {
                            cs.socket.Close();
                            return;
                        }

                        if (cs.frameType == OMTFrameType.Video)
                        {
                            this.videoChannel = new OMTChannel(cs.socket, OMTFrameType.Video, videoHandle, metadataHandle,false);
                            this.videoChannel.Changed += Channel_Changed;
                            this.videoChannel.StartReceive();
                            this.videoChannel.Send(new OMTMetadata(0, OMTMetadataConstants.CHANNEL_SUBSCRIBE_METADATA));
                            if (flags.HasFlag(OMTReceiveFlags.Preview))
                            {
                                this.videoChannel.Send(new OMTMetadata(0, OMTMetadataConstants.CHANNEL_PREVIEW_VIDEO_ON));
                            }
                            if (frameTypes.HasFlag(OMTFrameType.Video))
                            {
                                this.videoChannel.Send(new OMTMetadata(0, OMTMetadataConstants.CHANNEL_SUBSCRIBE_VIDEO));
                                SendSuggestedQuality();
                            }
                            SendTally();                            
                        }
                        if (cs.frameType == OMTFrameType.Audio)
                        {
                            this.audioChannel = new OMTChannel(cs.socket, OMTFrameType.Audio, audioHandle, metadataHandle,false);
                            this.audioChannel.Changed += Channel_Changed;
                            this.audioChannel.StartReceive();
                            if (frameTypes.HasFlag(OMTFrameType.Video) == false)
                            {
                                this.audioChannel.Send(new OMTMetadata(0, OMTMetadataConstants.CHANNEL_SUBSCRIBE_METADATA));
                            }
                            this.audioChannel.Send(new OMTMetadata(0, OMTMetadataConstants.CHANNEL_SUBSCRIBE_AUDIO));                            
                        }
                        if (cs.frameType == OMTFrameType.Metadata)
                        {
                            this.videoChannel = new OMTChannel(cs.socket, OMTFrameType.Metadata, videoHandle, metadataHandle, false);
                            this.videoChannel.Changed += Channel_Changed;
                            this.videoChannel.StartReceive();
                            this.videoChannel.Send(new OMTMetadata(0, OMTMetadataConstants.CHANNEL_SUBSCRIBE_METADATA));                            
                        }
                        OMTLogging.Write("Connected." + cs.frameType.ToString() + ": " + GetActualAddress().ToString(), "OMTReceive.Connect");
                        if (discoveryClient != null)
                        {
                            discoveryClient.Connected();
                        }
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        if (cs.socket != null)
                        {
                            cs.socket.Close();
                        }
                    }
                    catch (Exception ex2)
                    {
                        OMTLogging.Write(ex2.ToString(), "OMTReceive.Connect");
                    }
                    OMTLogging.Write(ex.Message, "OMTReceive.Connect");
                }
                if (cs.frameType == OMTFrameType.Video || cs.frameType == OMTFrameType.Metadata)
                {
                    videoConnectionState = null;
                }
                else if (cs.frameType == OMTFrameType.Audio)
                {
                    audioConnectionState = null;
                }
            }            
        }

        public bool IsConnected()
        {
            OMTChannel ch = videoChannel;
            if (frameTypes.HasFlag(OMTFrameType.Video) || frameTypes == OMTFrameType.Metadata)
            {
                if (!IsConnected(ch))
                {
                    return false;
                }
            }
            ch = audioChannel;
            if (frameTypes.HasFlag(OMTFrameType.Audio))
            {
                if (!IsConnected(ch))
                {
                    return false;
                }
            }
            if (frameTypes == OMTFrameType.None)
            {
                return true;
            }
            return true;
        }
        private bool IsConnected(OMTChannel ch)
        {
            if (ch != null)
            {
                if (!ch.Connected) return false;
            }
            else
            {
                return false;
            }
            return true;
        }
        internal override void OnDisconnected(OMTChannel ch)
        {
            if (ch != null)
            {
                if (discoveryClient != null)
                {
                    discoveryClient.Disconnected();
                }

                ScheduleReconnect();
            }
        }

        /// <summary>
        /// Re-establishes the channel after a disconnect, retrying with exponential backoff until the
        /// connection is live again. Attempts go through <see cref="BeginConnect"/>, which re-resolves
        /// the address through discovery (so a sender that re-announces under the same name is found)
        /// and is throttled to one attempt per second on its own.
        /// </summary>
        private void ScheduleReconnect()
        {
            lock (connectSync)
            {
                if (Exiting || IsConnected() || reconnectTimer != null)
                    return;

                var delay = reconnectDelayMs;
                reconnectDelayMs = Math.Min(reconnectDelayMs * 2, 10000);
                reconnectTimer = new Timer(_ =>
                {
                    lock (connectSync)
                    {
                        reconnectTimer.Dispose();
                        reconnectTimer = null;
                    }

                    if (Exiting)
                        return;

                    if (IsConnected())
                    {
                        reconnectDelayMs = 1000;
                        return;
                    }

                    BeginConnect();

                    // A failed connect attempt raises no disconnect event, so keep re-arming until the
                    // channel is back; the delay doubles from 1s up to 10s while nothing responds.
                    ScheduleReconnect();
                }, null, delay, Timeout.Infinite);
            }
        }

        public string RedirectAddress {  get { return this.redirectAddress;  } }

        internal void OnRedirectConnection(string newAddress)
        {
            if (Exiting) return;
            if (this.redirectAddress != newAddress)
            {
                this.redirectAddress = newAddress;
                OMTLogging.Write("Redirecting " + this.address + " to " + this.redirectAddress, "OMTReceive");
                ThreadPool.QueueUserWorkItem(ReconnectAsync);
            }
        }

        internal void ReconnectAsync(object state)
        {
            try
            {
                OMTLogging.Write("Reconnect: " + GetActualAddress(), "OMTReceive");
                lock (connectSync)
                {
                    if (Exiting) return;
                    CloseChannels();
                    lastBeginConnect = DateTime.MinValue;
                    BeginConnect();
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTReceive.Reconnect");
            }
        }

        internal override void OnRedirectChanged(OMTChannel ch)
        {
            try
            {
                if (ch != null)
                {
                    if (Exiting) return;
                    string newAddress = ch.RedirectAddress;
                    if (this.address != newAddress)
                    {
                        if (redirectMetadataOnly)
                        {
                            //Notify the sender of a change in redirect upstream
                            RedirectChanged?.Invoke(this, new OMTRedirectChangedEventArgs(newAddress));
                        }
                        else
                        {
                            if (redirect == null) 
                            {
                                if (this.redirectAddress != newAddress)
                                {
                                    //This is a normal Receiver, establish a side connection to original address to keep track of changes
                                    //Side connection is maintained for the life of this receiver
                                    this.redirectAddress = newAddress;
                                    redirect = new OMTRedirect(this);
                                    redirect.OnReceiveChanged();
                                    OMTLogging.Write("First redirect of " + this.address + " to " + this.redirectAddress, "OMTReceive");
                                    ThreadPool.QueueUserWorkItem(ReconnectAsync);
                                }
                            } else
                            {
                                OMTLogging.Write("Skipping redirect to " + newAddress + " due to existing side channel.", "OMTReceive");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTReceive");
            }
        }

        private OMTFrameBase ReceiveInternal(OMTFrameType frameTypes)
        {
            OMTChannel v = videoChannel;
            OMTChannel a = audioChannel;

            if (frameTypes.HasFlag(OMTFrameType.Video))
            {
                if (v != null)
                {
                    if (lastVideoFrame != null)
                    {
                        v.ReturnFrame(lastVideoFrame);
                        lastVideoFrame = null;
                    }
                    if (v.ReadyFrameCount > 0)
                    {
                        lastVideoFrame = v.ReceiveFrame();
                        return lastVideoFrame;
                    }
                }
            }
            if (frameTypes.HasFlag(OMTFrameType.Audio))
            {
                if (a != null)
                {
                    if (lastAudioFrame != null)
                    {
                        a.ReturnFrame(lastAudioFrame);
                        lastAudioFrame = null;
                    }
                    if (a.ReadyFrameCount > 0)
                    {
                        lastAudioFrame = a.ReceiveFrame();
                        return lastAudioFrame;
                    }
                }
            }
            if (frameTypes.HasFlag(OMTFrameType.Metadata))
            {
                if (v != null)
                {
                    if (v.ReadyMetadataCount > 0)
                    {
                        return v.ReceiveMetadata();
                    }
                }
                if (a != null)
                {
                    if (a.ReadyMetadataCount > 0)
                    {
                        return a.ReceiveMetadata();
                    }
                }
            }
            return null;
        }

        internal void CheckConnection()
        {
            if (Exiting) return;
            if (!IsConnected())
            {
                BeginConnect();
            }
            if (redirect != null)
            {
                redirect.CheckConnection();
            }
        }

        private OMTFrameBase ReceiveInternal(OMTFrameType frameTypes, int millisecondsTimeout)
        {
            if (Exiting) return null;
            if (frameTypes == OMTFrameType.None) return null;
            CheckConnection();
            OMTFrameBase frame = ReceiveInternal(frameTypes);
            if (frame == null)
            {
                for (int i = 0; i < 4; i ++)
                {
                    int result = WaitHandle.WaitTimeout;
                    if (frameTypes == (OMTFrameType.Video | OMTFrameType.Audio | OMTFrameType.Metadata))
                    {
                        result = WaitHandle.WaitAny(allWaitHandles, millisecondsTimeout);
                    }
                    else if (frameTypes == (OMTFrameType.Video | OMTFrameType.Audio))
                    {
                        result = WaitHandle.WaitAny(avHandles, millisecondsTimeout);
                    }
                    else if (frameTypes == OMTFrameType.Video)
                    {
                        result = WaitHandle.WaitAny(videoHandles, millisecondsTimeout);
                    }
                    else if (frameTypes == OMTFrameType.Audio)
                    {
                        result = WaitHandle.WaitAny(audioHandles, millisecondsTimeout);
                    }
                    else if (frameTypes == OMTFrameType.Metadata)
                    {
                        result = WaitHandle.WaitAny(metaDataHandles, millisecondsTimeout);
                    }
                    frame = ReceiveInternal(frameTypes);
                    if (frame != null) break;
                    if (result == WaitHandle.WaitTimeout) break;
                    if (Exiting) break;
                }
            }
            return frame;
        }

        internal int SendMetadata(OMTMetadata metadata)
        {
            OMTChannel ch = null;
            if (videoChannel != null)
            {
                ch = videoChannel;
            }
            else if (audioChannel != null)
            {
                ch = audioChannel;
            }
            if (ch != null)
            {
                return ch.Send(metadata);
            }
            return 0;
        }

        public OMTSenderInfo GetSenderInformation()
        {
            OMTChannel ch = videoChannel;
            if (ch == null) ch = audioChannel;
            if (ch != null)
            {
                return ch.SenderInformation;
            }
            return null;
        }

        public IPEndPoint GetRemoteEndPoint()
        {
            OMTChannel ch = videoChannel;
            if (ch == null) ch = audioChannel;
            if (ch != null)
            {
                return ch.RemoteEndPoint;
            }
            return null;
        }

        /// <summary>
        /// Send a metadata frame to the sender. Does not support other frame types.
        /// </summary>
        /// <param name="metadata"></param>
        /// <returns></returns>
        public int Send(OMTMediaFrame metadata)
        {
            if (Exiting) return 0;
            if (metadata.Type != OMTFrameType.Metadata) return 0;
            CheckConnection();
            OMTMetadata m = OMTMetadata.FromMediaFrame(metadata);
            if (m != null)
            {
                return SendMetadata(m);
            }
            return 0;
        }

        private bool ReceiveVideo(OMTFrame frame, ref OMTMediaFrame videoFrame)
        {
            lock (videoLock)
            {
                if (Exiting) return false;
                OMTVideoHeader header = frame.GetVideoHeader();
                if (header.Codec == (int)OMTCodec.VMX1)
                {
                    OMTVideoFlags flags = (OMTVideoFlags)header.Flags;

                    bool result = false;
                    bool alpha = flags.HasFlag(OMTVideoFlags.Alpha);
                    bool preview = flags.HasFlag(OMTVideoFlags.Preview);
                    bool interlaced = flags.HasFlag(OMTVideoFlags.Interlaced);
                    bool highBitDepth = flags.HasFlag(OMTVideoFlags.HighBitDepth);
                    int frameLength = frame.Data.Length - frame.MetadataLength;
                    int framesPerSecond = (int)OMTUtils.ToFrameRate(header.FrameRateN, header.FrameRateD);

                    bool compressedOnly = this.flags.HasFlag(OMTReceiveFlags.CompressedOnly);
                    if (compressedOnly == false)
                    {
                        CreateCodec(header.Width, header.Height, framesPerSecond, (VMXColorSpace)header.ColorSpace);
                        byte[] dst = tempVideo.Buffer;
                        BeginCodecTimer();
                        if (preview)
                        {
                            OMTSize sz = codec.GetPreviewSize(interlaced);
                            header.Width = sz.Width;
                            header.Height = sz.Height;

                            if (preferredVideoFormat == OMTPreferredVideoFormat.UYVY | 
                                (preferredVideoFormat == OMTPreferredVideoFormat.UYVYorBGRA & (alpha == false)) | 
                                (preferredVideoFormat == OMTPreferredVideoFormat.UYVYorUYVA & (alpha == false)) |
                                (preferredVideoFormat == OMTPreferredVideoFormat.UYVYorUYVAorP216orPA16 & (alpha == false))
                                )
                            {
                                tempVideoStride = header.Width * 2;
                                result = codec.DecodePreview(VMXImageType.UYVY, frame.Data.Buffer, frameLength, ref dst, tempVideoStride);
                                videoFrame.Codec = (int)OMTCodec.UYVY;
                            }
                            else if (preferredVideoFormat == OMTPreferredVideoFormat.BGRA | 
                                (preferredVideoFormat == OMTPreferredVideoFormat.UYVYorBGRA & alpha)
                                )
                            {
                                tempVideoStride = header.Width * 4;
                                if (alpha)
                                {
                                    result = codec.DecodePreview(VMXImageType.BGRA, frame.Data.Buffer, frameLength, ref dst, tempVideoStride);
                                }
                                else
                                {
                                    result = codec.DecodePreview(VMXImageType.BGRX, frame.Data.Buffer, frameLength, ref dst, tempVideoStride);
                                }
                                videoFrame.Codec = (int)OMTCodec.BGRA;
                            }
                            else if ((preferredVideoFormat == OMTPreferredVideoFormat.UYVYorUYVA & alpha) |
                                (preferredVideoFormat == OMTPreferredVideoFormat.UYVYorUYVAorP216orPA16 & alpha)
                                )
                            {
                                tempVideoStride = header.Width * 2;
                                result = codec.DecodePreview(VMXImageType.UYVA, frame.Data.Buffer, frameLength, ref dst, tempVideoStride);
                                videoFrame.Codec = (int)OMTCodec.UYVA;
                            }
                        }
                        else
                        {
                            if (preferredVideoFormat == OMTPreferredVideoFormat.UYVY |
                                (preferredVideoFormat == OMTPreferredVideoFormat.UYVYorUYVA & (alpha == false)) |
                                (preferredVideoFormat == OMTPreferredVideoFormat.UYVYorBGRA & (alpha == false)) |
                                (preferredVideoFormat == OMTPreferredVideoFormat.UYVYorUYVAorP216orPA16 & (alpha == false & highBitDepth == false))
                                )
                            {
                                tempVideoStride = header.Width * 2;
                                result = codec.Decode(VMXImageType.UYVY, frame.Data.Buffer, frameLength, ref dst, tempVideoStride);
                                videoFrame.Codec = (int)OMTCodec.UYVY;
                            }
                            else if (preferredVideoFormat == OMTPreferredVideoFormat.BGRA | 
                                (preferredVideoFormat == OMTPreferredVideoFormat.UYVYorBGRA & alpha)
                                )
                            {
                                tempVideoStride = header.Width * 4;
                                if (alpha)
                                {
                                    result = codec.Decode(VMXImageType.BGRA, frame.Data.Buffer, frameLength, ref dst, tempVideoStride);
                                }
                                else
                                {
                                    result = codec.Decode(VMXImageType.BGRX, frame.Data.Buffer, frameLength, ref dst, tempVideoStride);
                                }
                                videoFrame.Codec = (int)OMTCodec.BGRA;
                            }
                            else if ((preferredVideoFormat == OMTPreferredVideoFormat.UYVYorUYVA & alpha) |
                                (preferredVideoFormat == OMTPreferredVideoFormat.UYVYorUYVAorP216orPA16 & (alpha & highBitDepth == false))
                                )
                            {
                                tempVideoStride = header.Width * 2;
                                result = codec.Decode(VMXImageType.UYVA, frame.Data.Buffer, frameLength, ref dst, tempVideoStride);
                                videoFrame.Codec = (int)OMTCodec.UYVA;
                            } else if (preferredVideoFormat == OMTPreferredVideoFormat.UYVYorUYVAorP216orPA16 | preferredVideoFormat == OMTPreferredVideoFormat.P216)
                            {
                                tempVideoStride = header.Width * 2;
                                if (alpha & preferredVideoFormat != OMTPreferredVideoFormat.P216)
                                {
                                    result = codec.Decode(VMXImageType.PA16, frame.Data.Buffer, frameLength, ref dst, tempVideoStride);
                                    videoFrame.Codec = (int)OMTCodec.PA16;
                                } else
                                {
                                    result = codec.Decode(VMXImageType.P216, frame.Data.Buffer, frameLength, ref dst, tempVideoStride);
                                    videoFrame.Codec = (int)OMTCodec.P216;                                    
                                }
                            } else
                            {
                                OMTLogging.Write("No matching preferred format found", "OMTReceive");
                            }
                        }
                        EndCodecTimer();
                    } else
                    {
                        result = true;
                        tempVideoStride = 0;
                        videoFrame.Codec = (int)OMTCodec.VMX1;
                    } 
                    if (result)
                    {
                        videoFrame.Type = OMTFrameType.Video;
                        videoFrame.Timestamp = frame.Timestamp;
                        videoFrame.Width = header.Width;
                        videoFrame.Height = header.Height;

                        if (compressedOnly)
                        {
                            videoFrame.Data = IntPtr.Zero;
                            videoFrame.DataLength = 0;
                        } else
                        {
                            videoFrame.Data = tempVideo.Pointer;
                            videoFrame.DataLength = tempVideoStride * header.Height;
                            if (videoFrame.Codec == (int)OMTCodec.UYVA)
                            {
                                videoFrame.DataLength += header.Width * header.Height;
                            } else if (videoFrame.Codec == (int)OMTCodec.P216) {
                                videoFrame.DataLength *= 2;
                            } else if (videoFrame.Codec == (int)OMTCodec.PA16)
                            {
                                videoFrame.DataLength *= 3;
                            }
                        }

                        videoFrame.Stride = tempVideoStride;

                        if (tempCompressedVideo != IntPtr.Zero)
                        {
                            Marshal.Copy(frame.Data.Buffer, 0, tempCompressedVideo, frameLength);
                        }
                        videoFrame.CompressedData = tempCompressedVideo;
                        videoFrame.CompressedLength = frameLength;
                        videoFrame.Flags = flags;
                        videoFrame.ColorSpace = (OMTColorSpace)header.ColorSpace;
                        videoFrame.AspectRatio = header.AspectRatio;
                        videoFrame.FrameRateN = header.FrameRateN;
                        videoFrame.FrameRateD = header.FrameRateD;

                        ReceiveFrameMetadata(frame, ref tempMetaVideo, ref videoFrame);

                        return true;
                    }
                    else
                    {
                        OMTLogging.Write("Unable to decode video at timestamp: " + frame.Timestamp, "OMTReceive.ReceiveVideo");
                    }
                }
                else
                {
                    OMTLogging.Write("Unsupported audio codec: " + header.Codec, "OMTReceive.ReceiveVideo");
                }
                return false;
            }
        }

        /// <summary>
        /// Receive any available frames in the buffer, or wait for frames if empty
        /// 
        /// Returns true if a frame was found, false of timed out
        /// </summary>
        /// <param name="frameTypes">The frame types to receive. Set multiple types to receive them all in a single thread. Set individually if using separate threads for audio/video/metadata</param>
        /// <param name="millisecondsTimeout">The maximum time to wait for a new frame if empty</param>
        /// <param name="outFrame">The frame struct to fill with the received data</param>
        public bool Receive(OMTFrameType frameTypes, int millisecondsTimeout, ref OMTMediaFrame outFrame)
        {
            if (Exiting) return false;
            OMTFrameBase frame = ReceiveInternal(frameTypes, millisecondsTimeout);
            if (frame != null)
            {
                if (frame.FrameType == OMTFrameType.Video)
                {
                    return ReceiveVideo((OMTFrame)frame, ref outFrame);
                }
                else if (frame.FrameType == OMTFrameType.Audio)
                {
                    return ReceiveAudio((OMTFrame)frame, ref outFrame);
                }
                else if (frame.FrameType == OMTFrameType.Metadata)
                {
                    return ReceiveMetadata((OMTMetadata)frame, ref outFrame);
                }
            }
            return false;
        }

        internal bool Receive(int millisecondsTimeout, ref OMTMetadata metadata)
        {
            if (Exiting) return false;
            OMTFrameBase frame = ReceiveInternal(OMTFrameType.Metadata, millisecondsTimeout);
            if (frame != null)
            {
                metadata = (OMTMetadata)frame;
                return true;
            }
            return false;
        }

        public void SetTally(OMTTally tally)
        {
            this.tally = tally;
            SendTally();
        }

        internal override OMTTally GetTallyInternal()
        {
            OMTChannel ch = videoChannel;
            if (ch == null) {
                ch = audioChannel;
            }
            if (ch != null)
            {
                return ch.GetTally();
            }
            return new OMTTally();
        }

        public void SetFlags(OMTReceiveFlags flags)
        {
            if (this.flags != flags)
            {
                this.flags = flags;
                SendPreview();
            }
        }

        public string Address { get { return this.address;  } }

        private void SendPreview()
        {
            if (flags.HasFlag(OMTReceiveFlags.Preview))
            {
                SendMetadata(new OMTMetadata(0, OMTMetadataConstants.CHANNEL_PREVIEW_VIDEO_ON));
            }
            else
            {
                SendMetadata(new OMTMetadata(0, OMTMetadataConstants.CHANNEL_PREVIEW_VIDEO_OFF));
            }
        }

        private void SendTally()
        {
            SendMetadata(OMTMetadata.FromTally(tally));
        }

        public void SetSuggestedQuality(OMTQuality quality)
        {
            suggestedQuality = quality;
            SendSuggestedQuality();
        }

        private void SendSuggestedQuality()
        {
            if (suggestedQuality == OMTQuality.Default)
            {
                SendMetadata(new OMTMetadata(0, OMTMetadataTemplates.SUGGESTED_QUALITY));
            } else
            {
                string template = OMTMetadataTemplates.SUGGESTED_QUALITY.Replace("Default", suggestedQuality.ToString());
                SendMetadata(new OMTMetadata(0, template));
            }
        }

        private void ReceiveFrameMetadata(OMTFrame frame, ref OMTPinnedBuffer temp, ref OMTMediaFrame outFrame)
        {
            if (frame.MetadataLength > 0)
            {
                if (temp == null)
                {
                    temp = new OMTPinnedBuffer(OMTConstants.METADATA_FRAME_SIZE);
                }
                temp.SetBuffer(0, 0);
                temp.Append(frame.Data.Buffer, frame.Data.Length - frame.MetadataLength, frame.MetadataLength);
                outFrame.FrameMetadata = temp.Pointer;
                outFrame.FrameMetadataLength = frame.MetadataLength;
            }
        }

        private bool ReceiveAudio(OMTFrame frame, ref OMTMediaFrame audioFrame)
        {
            lock (audioLock)
            {
                if (Exiting) return false;
                OMTAudioHeader header = frame.GetAudioHeader();
                if (header.Codec == (int)OMTCodec.FPA1)
                {
                    int len = (header.SamplesPerChannel * header.Channels * OMTConstants.AUDIO_SAMPLE_SIZE);
                    if (len <= OMTConstants.AUDIO_MAX_SIZE)
                    {
                        if (tempAudio == null)
                        {
                            tempAudio = new OMTPinnedBuffer(len);
                        }
                        else if (tempAudio.Length < len)
                        {
                            tempAudio.Dispose();
                            tempAudio = new OMTPinnedBuffer(len);
                        }
                        tempAudio.SetBuffer(0, 0);
                        audioCodec.Decode(frame.Data, header.Channels, header.SamplesPerChannel, (OMTActiveAudioChannels)header.ActiveChannels, tempAudio);
                        
                        audioFrame.Type = OMTFrameType.Audio;
                        audioFrame.Codec = (int)OMTCodec.FPA1;
                        audioFrame.Timestamp = frame.Timestamp;
                        audioFrame.SampleRate = header.SampleRate;
                        audioFrame.Channels = header.Channels;
                        audioFrame.SamplesPerChannel = header.SamplesPerChannel;
                        audioFrame.Data = tempAudio.Pointer;
                        audioFrame.DataLength = tempAudio.Length;

                        ReceiveFrameMetadata(frame,ref tempMetaAudio, ref audioFrame);

                        return true;
                    }
                    else
                    {
                        OMTLogging.Write("InvalidAudioSize: " + len, "OMTReceive.ReceiveAudio");
                    }
                }
                else
                {
                    OMTLogging.Write("Unsupported audio codec: " + header.Codec, "OMTReceive.ReceiveAudio");
                }
                return false;
            }
        }
    }
}
