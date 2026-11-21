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

using libomtnet.linux;
using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using static libomtnet.mac.DnsSd;

namespace libomtnet.mac
{

    /// <summary>
    /// DnsSd/Bonjour based Discovery implementation for MacOS
    /// Issues:
    /// 1. Windows requests for service responses using unicast (QU), but there can be multiple services on a computer listening on port 5353 such as Chrome
    /// that will result in these responses being missed by the desired client.
    /// Solution: On all platforms ensure we refresh periodically so that multicast packets are sent.
    /// </summary>
    internal class OMTDiscoveryDnsSd :OMTDiscovery
    {
        private DnsSd.DNSServiceBrowseReply browseCallback;
        private DnsSd.DNSServiceResolveReply resolveCallback;
        private DispatchQueue dispatchQueue;

        private static DispatchQueue.DispatchDelegate deallocateDelegate = new DispatchQueue.DispatchDelegate(deallocateFunction);

        private List<ResolveRequest> resolveRequests = new List<ResolveRequest>();

        private class ResolveRequest
        {
            private IntPtr context;
            private IntPtr sdRef;
            private DateTime startTime;
            public ResolveRequest(IntPtr sdRef, IntPtr context)
            {
                this.sdRef = sdRef;
                this.context = context;
                startTime = DateTime.Now;
            }
            public IntPtr Context { get { return context; } }
            public IntPtr Ref { get { return sdRef; } }

            public bool IsExpired()
            {
                if (startTime.AddSeconds(5) < DateTime.Now)
                {
                    return true;
                }
                return false;
            }
        }

        private void AddResolveRequest(IntPtr sdRef, IntPtr context)
        {
            ResolveRequest r = new ResolveRequest(sdRef, context);
            lock (resolveRequests)
            {
                resolveRequests.Add(r);
            }
            DnsSd.DNSServiceSetDispatchQueue(r.Ref, dispatchQueue.NativePointer);
        }

        private void CompleteResolveRequest(IntPtr context)
        {
            lock (resolveRequests)
            {
                foreach (ResolveRequest r in resolveRequests)
                {
                    if (r.Context == context)
                    {
                        CompleteResolveRequest(r);
                        break;
                    }
                }
            }
        }
        private void CompleteResolveRequest(ResolveRequest r)
        {
            if (dispatchQueue != null)
            {
                dispatchQueue.Invoke(r.Ref, deallocateDelegate);
            }
            resolveRequests.Remove(r);
            if (r.Context != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(r.Context);
            }
        }

        private void ClearResolveRequests(bool expiredOnly)
        {
            lock (resolveRequests)
            {
                ResolveRequest[] rr = resolveRequests.ToArray();
                foreach (ResolveRequest r in rr)
                {
                    if (r.IsExpired() || expiredOnly == false)
                    {
                        CompleteResolveRequest(r);
                    }
                }
            }
        }

        private static void deallocateFunction(IntPtr context)
        {
            if (context != IntPtr.Zero)
            {
                DnsSd.DNSServiceRefDeallocate(context);
            }
        }

        private class EntryDnsSd : OMTDiscoveryEntry
        {
            public EntryDnsSd(OMTAddress address, DispatchQueue dispatchQueue) : base(address) {
                this.dispatchQueue = dispatchQueue;
            }
            public IntPtr sdRef;
            public ushort RegisteredPort;
            public string RegisteredName;
            private DispatchQueue dispatchQueue;

            public void CancelRequest()
            {
                if (sdRef != IntPtr.Zero)
                {
                    if (dispatchQueue != null)
                    {
                        dispatchQueue.Invoke(sdRef, deallocateDelegate);
                    }
                    sdRef = IntPtr.Zero;
                }
            }

            protected override void DisposeInternal()
            {
                CancelRequest();
                base.DisposeInternal();
            }

        }

        private List<OMTAddress> registeredAddresses = new List<OMTAddress>();
        private IntPtr browseRef;
        private Timer refreshTimer;

        internal OMTDiscoveryDnsSd()
        {
            BeginDispatcher();
            BeginDNSBrowse();
        }

        internal void BeginDispatcher()
        {
            dispatchQueue = new DispatchQueue("local.omt.discovery");
            OMTLogging.Write("BeginDispatcher", "OMTDiscoveryDnsSd");
        }

        internal void BeginDNSBrowse()
        {
            browseCallback = new DnsSd.DNSServiceBrowseReply(OnBrowse);
            resolveCallback = new DnsSd.DNSServiceResolveReply(OnResolve);

            IntPtr pType = OMTUtils.StringToPtrUTF8("_omt._tcp");
            int hr = DnsSd.DNSServiceBrowse(ref browseRef, 0, 0,pType, IntPtr.Zero, browseCallback, IntPtr.Zero);
            if (hr == 0)
            {
                OMTLogging.Write("BeginDNSBrowse.OK", "OMTDiscoveryDnsSd");
                hr = DnsSd.DNSServiceSetDispatchQueue(browseRef, dispatchQueue.NativePointer);
                if (hr != 0)
                {
                    OMTLogging.Write("BeginDNSBrowse.SetDispatchQueue.Error: " + hr, "OMTDiscoveryDnsSd");
                }
            }
            else
            {
                OMTLogging.Write("BeginDNSBrowse.Error: " + hr, "OMTDiscoveryDnsSd");
            }
            Marshal.FreeHGlobal(pType);
        }

        internal void EndDnsBrowse()
        {
            if (browseRef != IntPtr.Zero && dispatchQueue != null)
            {
                dispatchQueue.Invoke(browseRef, deallocateDelegate);
                ClearResolveRequests(false);
                browseRef = IntPtr.Zero;
                OMTLogging.Write("EndDNSBrowse", "OMTDiscoveryDnsSd");
            }
        }

        internal void EndDispatcher()
        {
            if (dispatchQueue != null)
            {
                dispatchQueue.Dispose();
                dispatchQueue = null;
                OMTLogging.Write("EndDispatcher", "OMTDiscoveryDnsSd");
            }
        }

        internal override bool DeregisterAddressInternal(OMTAddress address)
        {
            lock (lockSync)
            {
                OMTDiscoveryEntry entry = GetEntry(address);
                if (entry != null)
                {
                    RemoveEntry(entry.Address, true);
                    OMTLogging.Write("DeRegisterAddress: " + address.ToString(), "OMTDiscoveryDnsSd");
                    if (GetRegisteredEntryCount() == 0)
                    {
                        //StopRefreshTimer();
                    }
                    return true;
                }
            }
            return false;
        }

        private byte[] CreateTXTRecord(string record)
        {
            byte[] data = UTF8Encoding.UTF8.GetBytes(" " + record);
            data[0] = (byte)(data.Length - 1);
            return data;
        }

        internal override bool RegisterAddressInternal(OMTAddress address)
        {
            lock (lockSync)
            {
                OMTDiscoveryEntry ctx = GetEntry(address);
                if (ctx == null)
                {
                    string addressName = address.ToString();
                    ushort port = (ushort)address.Port;
                    byte[] b = BitConverter.GetBytes(port);
                    Array.Reverse(b);
                    port = BitConverter.ToUInt16(b, 0);
                    IntPtr pType = OMTUtils.StringToPtrUTF8("_omt._tcp");
                    IntPtr pAddress = OMTUtils.StringToPtrUTF8(addressName);
                    IntPtr newRequest = IntPtr.Zero;
                    int hr = DnsSd.DNSServiceRegister(ref newRequest, 0, 0, pAddress, pType, IntPtr.Zero, IntPtr.Zero, port, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    if (hr == 0)
                    {
                        EntryDnsSd sd = new EntryDnsSd(address, dispatchQueue);
                        sd.sdRef = newRequest;
                        sd.RegisteredPort = port;
                        sd.RegisteredName = addressName;
                        sd.ChangeStatus(OMTDiscoveryEntryStatus.Registered);
                        AddEntry(sd);
                        OMTLogging.Write("RegisterAddress: " + address.ToString(), "OMTDiscoveryDnsSd");
                        //StartRefreshTimer(); //No longer required since moving to QM requests on Win32
                    }
                    else
                    {
                        OMTLogging.Write("RegisterAddress.Error: " + hr, "OMTDiscoveryDnsSd");
                    }
                    Marshal.FreeHGlobal(pType);
                    Marshal.FreeHGlobal(pAddress);
                    return true;
                }
                return false;
            }
        }

        private void StartRefreshTimer()
        {
            if (refreshTimer == null)
            {
                refreshTimer = new Timer(RefreshTimerCallback, null, 10000, 10000);
                OMTLogging.Write("StartRefreshTimer", "OMTDiscoveryDnsSd");
            }
        }
        private void StopRefreshTimer()
        {
            if (refreshTimer != null)
            {
                refreshTimer.Dispose();
                refreshTimer = null;
                OMTLogging.Write("StopRefreshTimer", "OMTDiscoveryDnsSd");
            }
        }
        private void RefreshTimerCallback(object state)
        {
            try
            {
                lock (lockSync)
                {
                    foreach (OMTDiscoveryEntry entry in entries)
                    {
                        if (entry.Status == OMTDiscoveryEntryStatus.Registered)
                        {
                            EntryDnsSd sd = (EntryDnsSd)entry;
                            if (sd.sdRef != IntPtr.Zero)
                            {
                                IntPtr pType = OMTUtils.StringToPtrUTF8("_omt._tcp");
                                IntPtr pAddress = OMTUtils.StringToPtrUTF8(sd.RegisteredName);
                                DnsSd.DNSServiceRefDeallocate(sd.sdRef);
                                sd.sdRef = IntPtr.Zero;
                                int hr = DnsSd.DNSServiceRegister(ref sd.sdRef, 0, 0, pAddress, pType, IntPtr.Zero, IntPtr.Zero, sd.RegisteredPort, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                                if (hr != 0)
                                {
                                    OMTLogging.Write("RefreshAddress.Error: " + hr, "OMTDiscoveryDnsSd");
                                }
                                Marshal.FreeHGlobal(pType);
                                Marshal.FreeHGlobal(pAddress);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTDiscoveryDnsSd");
            }

        }

        protected override void DisposeInternal()
        {
            EndDnsBrowse();
            //StopRefreshTimer();
            base.DisposeInternal();
            EndDispatcher();
        }

        void OnResolve(IntPtr sdRef, DNSServiceFlags flags, uint interfaceIndex, int errorCode, IntPtr fullName, IntPtr hostTarget, UInt16 port, UInt16 txtLen, IntPtr txtRecord, IntPtr context)
        {
            try
            {
                string addressName = "";
                if (context != IntPtr.Zero)
                {
                    addressName = OMTUtils.PtrToStringUTF8(context);
                }
                if (errorCode == 0)
                {
                    string szHostTarget = OMTUtils.PtrToStringUTF8(hostTarget);
                    string szFullName = OMTUtils.PtrToStringUTF8(fullName);                 

                    if (OMTAddress.IsValid(addressName)) {

                        byte[] b = BitConverter.GetBytes(port);
                        Array.Reverse(b);
                        port = BitConverter.ToUInt16(b, 0);
                        IPHostEntry a = Dns.GetHostEntry(szHostTarget);
                        UpdateDiscoveredEntry(addressName, port, a.AddressList);

                    } else
                    {
                        OMTLogging.Write("InvalidAddressReceived: " + addressName, "OMTDiscoveryDnsSd");
                    }
                }
                else
                {
                    OMTLogging.Write("OnResolve.Error: " + errorCode, "OMTDiscoveryDnsSd");
                }
                if (flags.HasFlag(DNSServiceFlags.kDNSServiceFlagsMoreComing) == false)
                {
                    CompleteResolveRequest(context);
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTDiscoveryDnsSd");
            }
        }

        void OnBrowse(IntPtr sdRef, DNSServiceFlags flags, uint interfaceIndex, int errorCode, IntPtr serviceName, IntPtr regType, IntPtr replyDomain, IntPtr context)
        {
            try
            {
                if (errorCode == 0)
                {
                    if (serviceName != IntPtr.Zero && regType != IntPtr.Zero && replyDomain != IntPtr.Zero)
                    {
                        string szServiceName = OMTUtils.PtrToStringUTF8(serviceName);
                        if (flags.HasFlag(DnsSd.DNSServiceFlags.kDNSServiceFlagsAdd))
                        {
                            IntPtr sdRRef = IntPtr.Zero;
                            IntPtr ctx = OMTUtils.StringToPtrUTF8(szServiceName);
                            int hr = DnsSd.DNSServiceResolve(ref sdRRef, 0, interfaceIndex, serviceName, regType, replyDomain, resolveCallback, ctx);
                            if (hr == 0)
                            {
                                AddResolveRequest(sdRRef, ctx);
                             } else
                            {
                                Marshal.FreeHGlobal(ctx);
                            }
                        }
                        else
                        {
                            RemoveDiscoveredEntry(szServiceName);
                        }
                    }
                    ClearResolveRequests(true);
                }
                else
                {
                    OMTLogging.Write("OnBrowse.Error: " + errorCode, "OMTDiscoveryDnsSd");
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTDiscoveryDnsSd");
            }
        }
    }
}
