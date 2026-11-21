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

using libomtnet.codecs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;

namespace libomtnet
{
    internal class OMTChannel : OMTBase
    {
        private Socket socket;
        private SocketAsyncEventArgs receiveargs;
        private OMTSocketAsyncPool sendpool;
        private OMTSocketAsyncPool metapool;
        private OMTBuffer tempReceiveBuffer;
        private OMTFramePool framePool;
        private OMTFrame pendingFrame;
        private readonly Queue<OMTFrame> readyFrames;
        private AutoResetEvent frameReadyEvent;
        private OMTFrameType subscriptions = OMTFrameType.None;
        private readonly Queue<OMTMetadata> metadatas;
        private AutoResetEvent metadataReadyEvent;
        private OMTTally tally;
        private bool preview;
        private object lockSync = new object();
        private object sendSync = new object();
        private OMTQuality suggestedQuality = OMTQuality.Default;
        private OMTSenderInfo senderInfo = null;
        private IPEndPoint endPoint = null;
        private OMTStatistics statistics = new OMTStatistics();

        public delegate void ChangedEventHandler(object sender, OMTEventArgs e);
        public event ChangedEventHandler Changed;
        private OMTEventArgs tempEvent = new OMTEventArgs(OMTEventType.None);

        private string redirectAddress = null;

        public string RedirectAddress {  get { return redirectAddress;  } }
        public IPEndPoint RemoteEndPoint { get { return endPoint; } }
        
        public OMTChannel(Socket sck, OMTFrameType receiveFrameType, AutoResetEvent frameReady, AutoResetEvent metadataReady, bool metadataServer)
        {
            socket = sck;
            endPoint = (IPEndPoint)sck.RemoteEndPoint;

            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            socket.SendBufferSize = OMTConstants.NETWORK_SEND_BUFFER;
            if (receiveFrameType == OMTFrameType.Metadata)
            {
                socket.ReceiveBufferSize = OMTConstants.NETWORK_SEND_RECEIVE_BUFFER;
            } else
            {
                socket.ReceiveBufferSize = OMTConstants.NETWORK_RECEIVE_BUFFER;
            }
            tempReceiveBuffer = new OMTBuffer(OMTConstants.VIDEO_MIN_SIZE, true);

            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                socket.SetSocketOption(SocketOptionLevel.Tcp, (SocketOptionName)3, (int)5); //TCP_KEEPIDLE=3 in Win32 and is translated in .NET8+ to platform specific values
                OMTLogging.Write("KeepAlive.Enabled", "OMTChannel");
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTChannel.KeepAlive");
            }

            receiveargs = new SocketAsyncEventArgs();
            receiveargs.Completed += Receive_Completed;

            int poolCount = 1;
            int startingFrameSize = 0;
            if (receiveFrameType == OMTFrameType.Video)
            {
                poolCount = OMTConstants.VIDEO_FRAME_POOL_COUNT;
                startingFrameSize = OMTConstants.VIDEO_MIN_SIZE;
                receiveargs.SetBuffer(new byte[OMTConstants.VIDEO_MAX_SIZE], 0, OMTConstants.NETWORK_RECEIVE_MAX_TRANSFER);
            } else if (receiveFrameType == OMTFrameType.Audio) {
                poolCount = OMTConstants.AUDIO_FRAME_POOL_COUNT;
                startingFrameSize = OMTConstants.AUDIO_MIN_SIZE;
                receiveargs.SetBuffer(new byte[OMTConstants.AUDIO_MAX_SIZE], 0, OMTConstants.NETWORK_RECEIVE_MAX_TRANSFER);
            } else {
                poolCount = 1;
                startingFrameSize = OMTConstants.AUDIO_MIN_SIZE;
                receiveargs.SetBuffer(new byte[OMTConstants.AUDIO_MAX_SIZE], 0, OMTConstants.NETWORK_RECEIVE_MAX_TRANSFER);
            }
            int sendPoolCount = OMTConstants.NETWORK_ASYNC_COUNT;
            int startingSendPoolSize = OMTConstants.AUDIO_MIN_SIZE;
            if (metadataServer)
            {
                sendPoolCount = OMTConstants.NETWORK_ASYNC_COUNT_META_ONLY;
                startingSendPoolSize = OMTConstants.NETWORK_ASYNC_BUFFER_META_ONLY;
            }
            sendpool = new OMTSocketAsyncPool(sendPoolCount, startingSendPoolSize);
            metapool = new OMTSocketAsyncPool(OMTConstants.NETWORK_ASYNC_COUNT_META_ONLY, OMTConstants.NETWORK_ASYNC_BUFFER_META_ONLY);
            framePool = new OMTFramePool(poolCount, startingFrameSize, true);

            readyFrames = new Queue<OMTFrame>();
            frameReadyEvent = frameReady;
            metadatas = new Queue<OMTMetadata>();
            metadataReadyEvent =  metadataReady;
        }

        protected void OnEvent(OMTEventType type)
        {
            tempEvent.Type = type;
            Changed?.Invoke(this, tempEvent);
        }

        public OMTQuality SuggestedQuality {  get {  return suggestedQuality; } }       
        public OMTSenderInfo SenderInformation { get {  return senderInfo; } }

        public bool Connected { get {
                if (socket == null) return false;
                return socket.Connected; 
            } }

        private void CloseSocket()
        {
            lock (lockSync)
            {
                if (socket != null)
                {
                    socket.Close();
                    socket = null;
                }
            } 
        }

        public Socket Socket { get { return socket; } }

        public int Send(OMTMetadata metadata)
        {
            OMTBuffer m = OMTBuffer.FromMetadata(metadata.XML);
            OMTFrame frame = new OMTFrame(OMTFrameType.Metadata, m);
            frame.Timestamp = metadata.Timestamp;
            return Send(frame);
        }

        public bool IsVideo()
        {
            if (subscriptions.HasFlag(OMTFrameType.Video)) return true;
            return false;
        }

        public bool IsMetadata()
        {
            if (subscriptions.HasFlag(OMTFrameType.Metadata)) return true;
            return false;
        }

        public bool IsAudio()
        {
            if (subscriptions.HasFlag(OMTFrameType.Audio)) return true;
            return false;
        }

        public int Send(OMTFrame frame)
        {
            lock (sendSync)
            {
                if (Exiting) return 0;
                int written = 0;
                try
                {
                    if ((frame.FrameType != OMTFrameType.Metadata) && (subscriptions & frame.FrameType) != frame.FrameType)
                    {
                        return 0;
                    }
                    frame.SetPreviewMode(preview);
                    int length = frame.Length;
                    if (length > OMTConstants.VIDEO_MAX_SIZE)
                    {
                        statistics.FramesDropped += 1;
                        Debug.WriteLine("OMTChannel.Send.DroppedOversizedFrame");
                        return 0;
                    }
                    OMTSocketAsyncPool pool = sendpool;
                    if (frame.FrameType == OMTFrameType.Metadata)
                    {
                        pool = metapool;
                    }
                    SocketAsyncEventArgs e = pool.GetEventArgs();
                    if (e == null)
                    {
                        statistics.FramesDropped += 1;
                        Debug.WriteLine("OMTChannel.Send.DroppedFrame");
                        return 0;
                    }
                    pool.Resize(e, length);
                    frame.WriteHeaderTo(e.Buffer, 0, e.Count);
                    int headerLength = frame.HeaderLength + frame.ExtendedHeaderLength;
                    frame.WriteDataTo(e.Buffer, 0, headerLength, length - headerLength);
                    e.SetBuffer(0, length);
                    pool.SendAsync(socket, e);
                    written = length;
                    if (frame.FrameType != OMTFrameType.Metadata)
                    {
                        statistics.Frames += 1;
                        statistics.FramesSinceLast += 1;
                    }
                    statistics.BytesSent += written;
                    statistics.BytesSentSinceLast += written;
                }
                catch (Exception ex)
                {
                    OMTLogging.Write(ex.ToString(), "OMTChannel.Send");
                }
                return written;
            }            
        }

        public int ReadyFrameCount { get
        {
            lock (readyFrames)
            {
                return readyFrames.Count;
            }
        } }

        public int ReadyMetadataCount { get {                 
            lock (metadatas)
            {
                return metadatas.Count;
            }
        } }

        public OMTStatistics GetStatistics() {
            OMTStatistics s = statistics;
            statistics.FramesSinceLast = 0;
            statistics.BytesSentSinceLast = 0;
            statistics.BytesReceivedSinceLast = 0;
            return s; 
        }

        public OMTFrame ReceiveFrame()
        {
            lock (readyFrames)
            {
                if (Exiting) return null;
                if (readyFrames.Count > 0)
                {
                    return readyFrames.Dequeue();
                }
            }
            return null;
        }

        public void ReturnFrame(OMTFrame frame)
        {
            lock (readyFrames)
            {
                if (frame != null)
                {
                    if (Exiting)
                    {
                        frame.Dispose();
                    }
                    else
                    {
                        framePool.Return(frame);
                    }
                }
            }
        }

        public OMTMetadata ReceiveMetadata()
        {
            lock (metadatas)
            {
                if (Exiting) return null;
                if (metadatas.Count > 0)
                {
                    return metadatas.Dequeue();
                }
            }
            return null;
        }

        public OMTTally GetTally()
        {
            return tally;
        }
        private void UpdateTally(OMTTally t)
        {
            if (t.Preview != tally.Preview || t.Program != tally.Program)
            {
                tally = t;
                OnEvent(OMTEventType.TallyChanged);
            }
        }

        private bool ProcessMetadata(OMTFrame frame)
        {
            if (frame.FrameType == OMTFrameType.Metadata)
            {
              string xml = frame.Data.ToMetadata();
                if (xml == OMTMetadataConstants.CHANNEL_SUBSCRIBE_VIDEO)
                {
                    subscriptions |= OMTFrameType.Video;
                    return true;
                }
                else if (xml == OMTMetadataConstants.CHANNEL_SUBSCRIBE_AUDIO)
                {
                    subscriptions |= OMTFrameType.Audio;
                    return true;
                } else if (xml == OMTMetadataConstants.CHANNEL_SUBSCRIBE_METADATA)
                {
                    subscriptions |= OMTFrameType.Metadata;
                    return true;
                } else if (xml == OMTMetadataConstants.TALLY_PREVIEWPROGRAM)
                {
                    UpdateTally(new OMTTally(1, 1));
                    return true;
                } else if (xml == OMTMetadataConstants.TALLY_PROGRAM)
                {
                    UpdateTally(new OMTTally(0, 1));
                    return true;
                } else if (xml == OMTMetadataConstants.TALLY_PREVIEW)
                {
                    UpdateTally(new OMTTally(1, 0));
                    return true;
                } else if (xml == OMTMetadataConstants.TALLY_NONE)
                {
                    UpdateTally(new OMTTally(0, 0));
                    return true;
                } else if (xml == OMTMetadataConstants.CHANNEL_PREVIEW_VIDEO_ON)
                {
                    preview = true;
                    return true;
                } else if (xml == OMTMetadataConstants.CHANNEL_PREVIEW_VIDEO_OFF)
                {
                    preview = false;
                    return true;
                } else if (xml.StartsWith(OMTMetadataTemplates.SUGGESTED_QUALITY_PREFIX))
                {
                    XmlDocument doc = OMTMetadataUtils.TryParse(xml);
                    if (doc != null)
                    {
                        XmlNode n = doc.DocumentElement;
                        if (n != null)
                        {
                            XmlNode a = n.Attributes.GetNamedItem("Quality");
                            if (a != null)
                            {
                                if (a.InnerText!= null)
                                {
                                    foreach (OMTQuality e in Enum.GetValues(typeof(OMTQuality)))
                                    {
                                        if (e.ToString() == a.InnerText)
                                        {
                                            suggestedQuality = e;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    return true;
                } else if (xml.StartsWith(OMTMetadataTemplates.SENDER_INFO_PREFIX))
                {
                    senderInfo = OMTSenderInfo.FromXML(xml);
                    //Don't return here, as this info should passthrough to receiver as well.
                } else if (xml.StartsWith(OMTMetadataTemplates.REDIRECT_PREFIX))
                {
                    this.redirectAddress = OMTRedirect.FromXML(xml);
                    OnEvent(OMTEventType.RedirectChanged);
                    return true;
                }
                lock (metadatas)
                {
                    if (metadatas.Count < OMTConstants.METADATA_MAX_COUNT)
                    {
                        metadatas.Enqueue(new OMTMetadata(frame.Timestamp, xml, endPoint));
                    }
                    if (metadataReadyEvent != null)
                    {
                        metadataReadyEvent.Set();
                    }
                }
                return true;
            }
            return false;
        }
        private void ProtocolFailure(string reason)
        {
            CloseSocket();
            OMTLogging.Write("ProtocolFailure: " + reason, "OMTReceive");
        }
        private void Receive_Completed(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                lock (lockSync)
                {
                    if (Exiting) return;
                    if (socket == null) return;
                    if (e.SocketError == SocketError.Success && e.BytesTransferred > 0)
                    {
                        statistics.BytesReceived += e.BytesTransferred;
                        statistics.BytesReceivedSinceLast += e.BytesTransferred;
                        int len = e.Offset + e.BytesTransferred;
                        while (len > 0)
                        {
                            if (pendingFrame == null)
                            {
                                pendingFrame = framePool.Get();
                            }
                            if (pendingFrame.ReadHeaderFrom(e.Buffer, 0, len))
                            {
                                if (pendingFrame.FrameType != OMTFrameType.Video & pendingFrame.FrameType != OMTFrameType.Audio & pendingFrame.FrameType != OMTFrameType.Metadata)
                                {
                                    ProtocolFailure("Invalid packet or unsupported frame type: " + pendingFrame.FrameType);
                                    return;
                                }
                                if (pendingFrame.ReadExtendedHeaderFrom(e.Buffer, 0, len))
                                {
                                    if (pendingFrame.ReadDataFrom(e.Buffer, 0, len))
                                    {
                                        int read = pendingFrame.Length;
                                        int remaining = len - read;
                                        if (remaining > 0)
                                        {
                                            tempReceiveBuffer.Resize(remaining);
                                            Buffer.BlockCopy(e.Buffer, read, tempReceiveBuffer.Buffer, 0, remaining);
                                            Buffer.BlockCopy(tempReceiveBuffer.Buffer, 0, e.Buffer, 0, remaining);
                                            len = remaining;
                                        }
                                        else
                                        {
                                            len = 0;
                                        }
                                        if (ProcessMetadata(pendingFrame))
                                        {
                                            framePool.Return(pendingFrame);
                                            pendingFrame = null;
                                        }
                                        else
                                        {
                                            if (framePool.Count > 0)
                                            {
                                                lock (readyFrames)
                                                {
                                                    readyFrames.Enqueue(pendingFrame);
                                                }
                                                pendingFrame = null;
                                                if (frameReadyEvent != null)
                                                {
                                                    frameReadyEvent.Set();
                                                }
                                                statistics.Frames += 1;
                                                statistics.FramesSinceLast += 1;
                                            }
                                            else
                                            {
                                                statistics.FramesDropped += 1;
                                                Debug.WriteLine("Receive.DroppedFrame: Ready " + readyFrames.Count);
                                            }
                                        }
                                    }
                                    else { break; }
                                }
                                else { break; }
                            }
                            else { break; }
                        }
                        e.SetBuffer(len, Math.Min(OMTConstants.NETWORK_RECEIVE_MAX_TRANSFER, e.Buffer.Length - len));
                        StartReceive(e);
                    }
                    else
                    {
                        OMTLogging.Write("SocketClosing: " + e.SocketError.ToString() + "," + e.BytesTransferred,"OMTChannel.Receive");
                        CloseSocket();
                        OnEvent(OMTEventType.Disconnected);
                    }
                }                
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTChannel.Receive");
            }
        }

        public void StartReceive(SocketAsyncEventArgs e)
        {
            if (!Exiting)
            {
                if (socket != null)
                {
                    if (socket.Connected)
                    {
                        if (socket.ReceiveAsync(e) == false)
                        {
                            Receive_Completed(this, e);
                        }
                    }
                }
            }
        }

        public void StartReceive()
        {            
            receiveargs.SetBuffer(0, OMTConstants.NETWORK_RECEIVE_MAX_TRANSFER);
            StartReceive(receiveargs);
        }

        protected override void DisposeInternal()
        {
            lock (sendSync) { }
            lock (readyFrames) { }
            lock (metadatas)
            {
                metadatas.Clear();
            }
            CloseSocket();
            if (receiveargs != null)
            {
                receiveargs.Completed -= Receive_Completed;
                receiveargs.Dispose();
                receiveargs = null;
            }
            if (sendpool != null)
            { 
                sendpool.Dispose(); 
                sendpool = null;
            }
            if (metapool != null)
            {
                metapool.Dispose();
                metapool = null;
            }
            if (framePool != null)
            {
                framePool.Dispose();
                framePool = null;
            }
            if (readyFrames != null)
            {
                lock (readyFrames)
                {
                    foreach (OMTFrame frame in readyFrames)
                    {
                        if (frame != null)
                        {
                            frame.Dispose();
                        }
                    }
                    readyFrames.Clear();
                }
            }
            if (pendingFrame != null)
            {
                pendingFrame.Dispose();
                pendingFrame = null;
            }
            if (tempReceiveBuffer != null)
            {
                tempReceiveBuffer.Dispose();
                tempReceiveBuffer = null;
            }
            base.DisposeInternal();
        }
    }
}
