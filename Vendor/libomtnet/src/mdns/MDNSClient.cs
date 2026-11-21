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
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;

namespace libomtnet.src.mdns
{
    /// <summary>
    /// This class periodically sends out a MDNS QM (multicast) query for the service type.
    /// This overcomes a limitation of DNS-SD API on Windows, where it will stop sending out queries after some time.
    /// </summary>
    internal class MDNSClient : OMTBase
    {
        private const int DEFAULT_PORT = 5353;
        private const string MULTICAST_ADDRESS = "224.0.0.251";
        private const string MULTICAST_ADDRESS_V6 = "ff02::fb";
        private const int SEND_INTERVAL_MILLISECONDS = 8000;

        private Socket[] sockets;
        private Timer refreshTimer;
        private byte[] query;

        private IPEndPoint mdns4;
        private IPEndPoint mdns6;

        private object lockSync = new object();

        // Adapters without multicast support (VPN/virtual) make GetIPv4/IPv6Properties throw on Windows;
        // one note instead of a stack trace per adapter keeps the log readable.
        private static bool loggedUnusableAdapter;
        public MDNSClient(string serviceType)
        {
            query = CreateDNSQuery(serviceType);
            mdns4 = new IPEndPoint(IPAddress.Parse(MULTICAST_ADDRESS), DEFAULT_PORT);
            mdns6 = new IPEndPoint(IPAddress.Parse(MULTICAST_ADDRESS_V6), DEFAULT_PORT);
            sockets = CreateMulticastSockets();
            refreshTimer = new Timer(RefreshTimerCallback, null, 0, SEND_INTERVAL_MILLISECONDS);
        }

        private void SendQueryToSocket(Socket s)
        {
            if (s.AddressFamily == AddressFamily.InterNetworkV6)
            {
                s.SendTo(query, mdns6);
            }
            else
            {
                s.SendTo(query, mdns4);
            }
        }

        private Socket[] CreateMulticastSockets()
        {
            List<Socket> l = new List<Socket>();
            Socket s;
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface n in nics)
            {
                if (n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    if (n.SupportsMulticast)
                    {
                        IPInterfaceProperties ip = n.GetIPProperties();
                        if (ip != null)
                        {
                            try
                            {
                                IPv4InterfaceProperties ipv4 = ip.GetIPv4Properties();
                                if (ipv4 != null)
                                {
                                    s = CreateMulticastSocket(AddressFamily.InterNetwork, ipv4.Index, DEFAULT_PORT);
                                    if (s != null) l.Add(s);
                                }
                            }
                            catch (Exception ex)
                            {
                                if (!loggedUnusableAdapter)
                                {
                                    loggedUnusableAdapter = true;
                                    OMTLogging.Write($"Skipping an adapter whose multicast properties are unavailable ({ex.GetType().Name}: {ex.Message}); further skips are silent.", "MDNSClient");
                                }
                            }
                            try
                            {
                                IPv6InterfaceProperties ipv6 = ip.GetIPv6Properties();
                                if (ipv6 != null)
                                {
                                    s = CreateMulticastSocket(AddressFamily.InterNetworkV6, ipv6.Index, DEFAULT_PORT);
                                    if (s != null) l.Add(s);
                                }
                            }
                            catch (Exception ex)
                            {
                                if (!loggedUnusableAdapter)
                                {
                                    loggedUnusableAdapter = true;
                                    OMTLogging.Write($"Skipping an adapter whose multicast properties are unavailable ({ex.GetType().Name}: {ex.Message}); further skips are silent.", "MDNSClient");
                                }
                            }
                        }
                    }
                }
            }
            return l.ToArray();
        }
        private byte[] CreateDNSQuery(string serviceType)
        {
            byte[] sn = StringToDNS(serviceType);
            int messageLength = sn.Length + 16;
            byte[] query = new byte[messageLength];
            int pos = 5;
            query[pos] = 1;
            pos = 12;
            Buffer.BlockCopy(sn, 0, query, pos, sn.Length);
            pos += sn.Length;
            pos += 1;
            query[pos] = 12;
            pos += 2;
            query[pos] = 1;
            return query;
        }

        private Socket CreateMulticastSocket(AddressFamily af, int interfaceIndex, int port)
        {
            try
            {
                Socket socket = new Socket(af, SocketType.Dgram, ProtocolType.Udp);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                if (af == AddressFamily.InterNetworkV6)
                {
                    socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, interfaceIndex);
                    socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastLoopback, false);
                    socket.Bind(new IPEndPoint(IPAddress.IPv6Any, DEFAULT_PORT));
                }
                else
                {
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, IPAddress.HostToNetworkOrder(interfaceIndex));
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, false);
                    socket.Bind(new IPEndPoint(IPAddress.Any, DEFAULT_PORT));
                }
                return socket;
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "MDNSClient");
            }
            return null;
        }

        private byte[] StringToDNS(string str)
        {
            using (MemoryStream m = new MemoryStream())
            {
                string[] strs = str.Split('.');
                foreach (string s in strs)
                {
                    byte[] b = ASCIIEncoding.ASCII.GetBytes(s);
                    m.WriteByte((byte)b.Length);
                    m.Write(b, 0, b.Length);
                }
                m.WriteByte(0);
                return m.ToArray();
            }
        }

        private void RefreshTimerCallback(object state)
        {
            try
            {
                if (Exiting) return;
                lock (lockSync)
                {
                    if (sockets != null)
                    {
                        foreach (Socket s in sockets)
                        {
                            try
                            {
                                SendQueryToSocket(s);
                            }
                            catch (Exception ex)
                            {
                                OMTLogging.Write(ex.ToString(), "MDNSClient"); 
                                List<Socket> list = new List<Socket>();
                                list.AddRange(sockets);
                                list.Remove(s);
                                sockets = list.ToArray();
                                OMTLogging.Write("Removed failed socket", "MDNSClient");
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "MDNSClient");
            }
        }

        protected override void DisposeInternal()
        {
            try
            {
                if (refreshTimer != null)
                {
                    refreshTimer.Dispose();
                    refreshTimer = null;
                }
                if (sockets != null)
                {
                    lock (lockSync)
                    {
                        foreach (Socket s in sockets)
                        {
                            s.Close();
                        }
                    }
                    sockets = null;
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "MDNSClient");
            }
            base.DisposeInternal();
        }
    }
}
