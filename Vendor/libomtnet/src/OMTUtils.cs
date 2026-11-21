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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace libomtnet
{
    public class OMTUtils
    {
        internal static IPAddress[] ResolveHostname(string hostname)
        {
            try
            {
                return Dns.GetHostAddresses(hostname);
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTUtils.ResolveHostname");
            }
            return null;
        }

        public static IntPtr StringToPtrUTF8(string s)
        {         
            byte[] b = UTF8Encoding.UTF8.GetBytes(s);
            IntPtr dst = Marshal.AllocHGlobal(b.Length + 1);
            Marshal.Copy(b, 0, dst, b.Length);
            Marshal.WriteByte(dst, b.Length, 0);
            return dst;
        }
        public static IntPtr StringToPtrUTF8(string s, out int length)
        {
            byte[] b = UTF8Encoding.UTF8.GetBytes(s);
            IntPtr dst = Marshal.AllocHGlobal(b.Length + 1);
            Marshal.Copy(b, 0, dst, b.Length);
            Marshal.WriteByte(dst, b.Length, 0);
            length = b.Length + 1;
            return dst;
        }

        public static void WriteStringToPtrUTF8(string s, IntPtr dst)
        {
            byte[] b = UTF8Encoding.UTF8.GetBytes(s);
            Marshal.Copy(b, 0, dst, b.Length);
            Marshal.WriteByte(dst, b.Length, 0);
        }
        public static void WriteStringToPtrUTF8(string s, IntPtr dst, int maxLength)
        {
            if (maxLength <= 0) return;
            byte[] b = UTF8Encoding.UTF8.GetBytes(s);
            int len = Math.Min(maxLength - 1, b.Length);
            Marshal.Copy(b, 0, dst, len);
            Marshal.WriteByte(dst, len, 0);
        }

        public static string PtrToStringUTF8(IntPtr ptr)
        {
            using (MemoryStream m = new MemoryStream())
            {
                int offset = 0;
                while (true)
                {
                    byte b = Marshal.ReadByte(ptr, offset);
                    if (b == 0) break;
                    m.WriteByte(b);
                    offset++;
                }
                return UTF8Encoding.UTF8.GetString(m.ToArray());
            }
        }

        public static string PtrToStringUTF8(IntPtr ptr, int maxLength)
        {
            using (MemoryStream m = new MemoryStream())
            {
                for (int i = 0; i < maxLength; i++)
                {
                    byte b = Marshal.ReadByte(ptr, i);
                    if (b == 0) break;
                    m.WriteByte(b);
                }
                return UTF8Encoding.UTF8.GetString(m.ToArray());
            }
        }

        public static void InterleavedToPlanarAudio32F32F(int numSamples, int channels, int sampleStride, float[] src, float[] dst)
        {
            int offset = 0;
            for (int i = 0; i < numSamples; i++)
            {
                for (int c = 0; c < channels; c++)
                {
                    dst[(sampleStride * c) + i] = src[offset];
                    offset += 1;
                }
            }
        }
        public static void InterleavedToPlanarAudio1632F(int numSamples, int channels, int sampleStride, short[] src, float[] dst)
        {
            int offset = 0;
            for (int i = 0; i < numSamples; i++)
            {
                for (int c = 0; c < channels; c++)
                {
                    float s = src[offset];
                    dst[(sampleStride * c) + i] = s / short.MaxValue;
                    offset += 1;
                }
            }
        }

        public static IntPtr XMLToIntPtr(string xml, ref int length)
        {
            byte[] utf8 = UTF8Encoding.UTF8.GetBytes(xml);
            length = utf8.Length + 1;
            IntPtr data = Marshal.AllocHGlobal(length);
            Marshal.Copy(utf8, 0, data, utf8.Length);
            Marshal.WriteByte(data, utf8.Length, 0);
            return data;
        }

        public static string IntPtrToXML(IntPtr ptr, int length)
        {
            if (ptr != IntPtr.Zero && length > 0)
            {
                byte[] b = new byte[length];
                Marshal.Copy(ptr, b, 0, length);
                string xml = UTF8Encoding.UTF8.GetString(b);
                return xml;
            }
            return null;
        }

        public static void FreeXMLIntPtr(IntPtr x)
        {
            if (x != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(x);
            }
        }

        public static float ToFrameRate(int frameRateN, int frameRateD)
        {
            if (frameRateD == 0) return 0;
            double d = (double)frameRateN / (double)frameRateD;
            d = Math.Round(d, 2);
            return (float)d;
        }

        public static void FromFrameRate(float fps, ref int frameRateN, ref int frameRateD)
        {
            switch (Math.Round(fps, 2))
            {
                case 29.97:
                    frameRateN = 30000;
                    frameRateD = 1001;
                    break;
                case 59.94:
                    frameRateN = 60000;
                    frameRateD = 1001;
                    break;
                case 119.88:
                    frameRateN = 120000;
                    frameRateD = 1001;
                    break;
                case 239.76:
                    frameRateN = 240000;
                    frameRateD = 1001;
                    break;
                case 23.98:
                case 23.976:
                    frameRateN = 24000;
                    frameRateD = 1001;
                    break;
                default:
                    frameRateN = (int)fps;
                    frameRateD = 1;
                    break;
            }
        }

        public static bool IsIPv4(IPAddress address)
        {
            if (address != null)
            {
                if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return true;
                if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    byte[] b = address.GetAddressBytes();
                    for (int i = 0; i < 10; i++)
                    {
                        if (b[i] != 0) return false;
                    }
                    if (b[10] == 0xFF && b[11] == 0xFF) return true;
                }
            }
            return false;
        }


        /// <summary>
        /// TiXL patch: a dual-stack TCP socket where the OS has IPv6, an IPv4 one where it is disabled
        /// (ipv6.disable=1, some containers), instead of failing every sender and receiver there.
        /// </summary>
        internal static Socket CreateStreamSocket()
        {
            if (Socket.OSSupportsIPv6)
            {
                try
                {
                    Socket s = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
                    s.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                    return s;
                }
                catch (SocketException)
                {
                }
            }
            return new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        internal static IPEndPoint MatchSocketFamily(Socket socket, IPEndPoint endpoint)
        {
            if (socket.AddressFamily == AddressFamily.InterNetwork && endpoint.AddressFamily == AddressFamily.InterNetworkV6)
            {
                IPAddress a = endpoint.Address.Equals(IPAddress.IPv6Any) ? IPAddress.Any : endpoint.Address.MapToIPv4();
                return new IPEndPoint(a, endpoint.Port);
            }
            return endpoint;
        }
    }
}
