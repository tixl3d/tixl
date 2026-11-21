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
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace libomtnet.win32
{
    internal static class DnsApi
    {
        public const uint DNS_QUERY_REQUEST_VERSION1 = 1;
        public const uint DNS_REQUEST_PENDING = 0x2522;
        public const uint DNS_ERROR_RECORD_DOES_NOT_EXIST = 0x25E5;
        public const uint ERROR_CANCELLED = 0x4C7;

        [StructLayout(LayoutKind.Sequential)]
        public struct PDNS_SERVICE_REGISTER_REQUEST
        {
            public uint Version;
            public uint InterfaceIndex;
            public IntPtr pServiceInstance;
            public IntPtr pRegisterCompletionCallback;
            public IntPtr pQueryContext;
            public IntPtr hCredentials;
            [MarshalAs(UnmanagedType.Bool)]
            public bool unicastEnabled;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PDNS_SERVICE_CANCEL
        {
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PDNS_SERVICE_INSTANCE
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pszInstanceName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pszHostName;
            public IntPtr ip4Address;
            public IntPtr ip6Address;
            public ushort wPort;
            public ushort wPriority;
            public ushort wWeight;
            public uint dwPropertyCount;
            public IntPtr keys;
            public IntPtr values;
            public uint dwInterfaceIndex;
        }

        public enum DNS_TYPE : uint
        {
            DNS_TYPE_A = 0x1,
            DNS_TYPE_NS = 0x2,
            DNS_TYPE_MD = 0x3,
            DNS_TYPE_MF = 0x4,
            DNS_TYPE_CNAME = 0x5,
            DNS_TYPE_SOA = 0x6,
            DNS_TYPE_MB = 0x7,
            DNS_TYPE_MG = 0x8,
            DNS_TYPE_MR = 0x9,
            DNS_TYPE_NULL = 0xA,
            DNS_TYPE_WKS = 0xB,
            DNS_TYPE_PTR = 0xC,
            DNS_TYPE_HINFO = 0xD,
            DNS_TYPE_MINFO = 0xE,
            DNS_TYPE_MX = 0xF,
            DNS_TYPE_TEXT = 0x10,
            DNS_TYPE_TXT = DNS_TYPE_TEXT,
            DNS_TYPE_RP = 0x11,
            DNS_TYPE_AFSDB = 0x12,
            DNS_TYPE_X25 = 0x13,
            DNS_TYPE_ISDN = 0x14,
            DNS_TYPE_RT = 0x15,
            DNS_TYPE_NSAP = 0x16,
            DNS_TYPE_NSAPPTR = 0x17,
            DNS_TYPE_SIG = 0x18,
            DNS_TYPE_KEY = 0x19,
            DNS_TYPE_PX = 0x1A,
            DNS_TYPE_GPOS = 0x1B,
            DNS_TYPE_AAAA = 0x1C,
            DNS_TYPE_LOC = 0x1D,
            DNS_TYPE_NXT = 0x1E,
            DNS_TYPE_EID = 0x1F,
            DNS_TYPE_NIMLOC = 0x20,
            DNS_TYPE_SRV = 0x21,
            DNS_TYPE_ATMA = 0x22,
            DNS_TYPE_NAPTR = 0x23,
            DNS_TYPE_KX = 0x24,
            DNS_TYPE_CERT = 0x25,
            DNS_TYPE_A6 = 0x26,
            DNS_TYPE_DNAME = 0x27,
            DNS_TYPE_SINK = 0x28,
            DNS_TYPE_OPT = 0x29,
            DNS_TYPE_DS = 0x2B,
            DNS_TYPE_RRSIG = 0x2E,
            DNS_TYPE_NSEC = 0x2F,
            DNS_TYPE_DNSKEY = 0x30,
            DNS_TYPE_DHCID = 0x31,
            DNS_TYPE_UINFO = 0x64,
            DNS_TYPE_UID = 0x65,
            DNS_TYPE_GID = 0x66,
            DNS_TYPE_UNSPEC = 0x67,
            DNS_TYPE_ADDRS = 0xF8,
            DNS_TYPE_TKEY = 0xF9,
            DNS_TYPE_TSIG = 0xFA,
            DNS_TYPE_IXFR = 0xFB,
            DNS_TYPE_AFXR = 0xFC,
            DNS_TYPE_MAILB = 0xFD,
            DNS_TYPE_MAILA = 0xFE,
            DNS_TYPE_ALL = 0xFF,
            DNS_TYPE_ANY = DNS_TYPE_ALL,
            DNS_TYPE_WINS = 0xFF01,
            DNS_TYPE_WINSR = 0xFF02,
            DNS_TYPE_NBSTAT = DNS_TYPE_WINSR
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate void DnsServiceRegisterComplete(uint status, IntPtr pQueryContext, IntPtr pInstance);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PDNS_SERVICE_BROWSE_REQUEST
        {
            public uint Version;
            public uint InterfaceIndex;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string QueryName;
            public IntPtr pBrowseCallback;
            public IntPtr pQueryContext;
        }

        public enum DNS_FREE_TYPE
        {
            DnsFreeFlat = 0,
            DnsFreeRecordList,
            DnsFreeParsedMessageFields
        }


        public class DnsCancelHandle : SafeHandle
        {
            private bool registerType;
            public DnsCancelHandle(bool registerType) : base(IntPtr.Zero, true)
            {
                this.registerType = registerType;
                this.handle = Marshal.AllocHGlobal(8);
                Marshal.WriteInt64(this.handle, 0);
            }
            public override bool IsInvalid
            {
                get
                {
                    if (handle == IntPtr.Zero)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            internal IntPtr GetInternalValue()
            {
                if (handle != IntPtr.Zero)
                {
                    return Marshal.ReadIntPtr(handle);
                }
                return IntPtr.Zero;
            }

            public void Clear()
            {
                if (handle != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(handle);
                    handle = IntPtr.Zero;
                }
            }
            protected override bool ReleaseHandle()
            {
                if (handle != IntPtr.Zero)
                {
                    Int64 v = Marshal.ReadInt64(handle);
                    if (v != 0)
                    {
                        if (registerType)
                        {
                            DnsServiceRegisterCancel(handle);
                        }
                        else
                        {
                            DnsServiceBrowseCancel(handle);
                        }
                    }
                    Marshal.FreeHGlobal(handle);
                    handle = IntPtr.Zero;
                }
                return true;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate void DnsServiceBrowseCallback(uint status, IntPtr pQueryContext, IntPtr pDnsRecord);

        [DllImport("dnsapi.dll", CharSet = CharSet.Unicode)]
        public static extern int DnsServiceBrowse(ref PDNS_SERVICE_BROWSE_REQUEST pRequest, DnsCancelHandle pCancel);

        [DllImport("dnsapi.dll")]
        private static extern int DnsServiceBrowseCancel(IntPtr pCancel);

        [DllImport("dnsapi.dll")]
        private static extern int DnsServiceRegisterCancel(IntPtr pCancel);

        [DllImport("dnsapi.dll")]
        public static extern void DnsFree(IntPtr pData, DNS_FREE_TYPE FreeType);
        [DllImport("dnsapi.dll")]
        public static extern void DnsServiceFreeInstance(IntPtr pInstance);

        [DllImport("dnsapi.dll")]
        public static extern int DnsServiceRegister(ref PDNS_SERVICE_REGISTER_REQUEST pRequest, DnsCancelHandle pCancel);

        [DllImport("dnsapi.dll")]
        public static extern int DnsServiceDeRegister(ref PDNS_SERVICE_REGISTER_REQUEST pRequest, IntPtr pCancel);

        //MDNS API
        [DllImport("dnsapi.dll")]
        public static extern int DnsStartMulticastQuery(ref PMDNS_QUERY_REQUEST pRequest, IntPtr pHandle);

        [DllImport("dnsapi.dll")]
        public static extern int DnsStopMulticastQuery(IntPtr pHandle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate void MdnsQueryCallback(IntPtr pQueryContext, IntPtr pQueryHandle, ref PMDNS_QUERY_RESULT pQueryResults);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PMDNS_QUERY_REQUEST
        {
            public uint Version;
            public uint ulRefCount;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Query;
            public UInt16 QueryType;
            public UInt64 QueryOptions;
            public UInt32 InterfaceIndex;
            public IntPtr pQueryCallback;
            public IntPtr pQueryContext;
            public UInt32 fAnswerReceived;
            public UInt32 ulResendCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PMDNS_QUERY_RESULT
        {
            public uint Version;
            public int QueryStatus;
            public UInt64 QueryOptions;
            public IntPtr pQueryRecords;
            public IntPtr pReserved;
        }

    }

}
