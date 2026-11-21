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
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Net;
using libomtnet.src.mdns;
using System.Threading;

namespace libomtnet.win32
{
    /// <summary>
    /// DnsApi on Windows requires a few workarounds documented here:
    /// 1. Entries are not automatically deregistered when the process exits. This leads to many stray entries if a process did not close cleanly.
    ///    Workaround: We check for any of these on ports we control, and remove them manually.
    /// 2. Duplicate entries are renamed by the API automatically. 
    ///    Workaround: We retrieve the instance again after the registration completes so we know what the new name is.
    /// 3. The API will notify of a Ttl of 0 when a source is deregistered, however these sources return with a longer Ttl in any new processes started thereafter.
    ///    Workaround: We track the Ttl and remove from list once finally drops to 0.
    /// 4. The API will not always notify in time for the Ttl (or at all) even if the source is still live. 
    ///    Workaround: A Ttl < 60 seems a reliable indicator we can expire the item.
    /// 5. The default DnsServiceBrowse implementation uses unicast (QU) requests which will result in missing sources on some systems since many apps listen on port 5353 at the same time.
    ///    Workaround: Use DnsStartMulticastQuery instead which uses (QM)
    /// 6. DnsServiceBrowse will return an error when no network is connected. 
    ///    Workaround: Setup thread timer to attempt to start up again every 5 seconds if failed.
    /// </summary>
    internal class OMTDiscoveryWin32 : OMTDiscovery
    {       

        private DnsApi.DnsServiceRegisterComplete registerCallback;
        private DnsApi.DnsServiceBrowseCallback browseCallback;
        private DnsApi.DnsCancelHandle browseCancel = null;

        //private DnsApi.MdnsQueryCallback mdnsQueryCallback;
        //private IntPtr mdnsQueryHandle;

        private MDNSClient mdnsClient;
        private Timer browseTimer;
        private object browseLock = new object();

        private class EntryWin32 : OMTDiscoveryEntry
        {
            public DnsApi.PDNS_SERVICE_REGISTER_REQUEST RegisterRequest;
            public DnsApi.DnsCancelHandle RegisterCancel = null;
            public bool Cancelling = false;
            public EntryWin32(OMTAddress address) : base(address)
            {

            }
            public void CancelRegister()
            {
                if (Cancelling == false)
                {
                    if (RegisterCancel != null)
                    {
                        Cancelling = true;
                        RegisterCancel.Close();
                        RegisterCancel = null;
                        Cancelling = false;
                        Debug.WriteLine("CancelRegister");
                    }
                }
            }
            protected override void DisposeInternal()
            {
                base.DisposeInternal();
                if (RegisterRequest.pServiceInstance != IntPtr.Zero)
                {
                    Marshal.DestroyStructure(RegisterRequest.pServiceInstance, typeof(DnsApi.PDNS_SERVICE_INSTANCE));
                    Marshal.FreeHGlobal(RegisterRequest.pServiceInstance);
                    RegisterRequest.pServiceInstance = IntPtr.Zero;
                }
                CancelRegister(); //Run this after handle is freed, since it ends up calling OnComplete which in turn would call dispose again
            }
        }

        internal OMTDiscoveryWin32()
        {
            browseCallback = new DnsApi.DnsServiceBrowseCallback(OnBrowse);
            registerCallback = new DnsApi.DnsServiceRegisterComplete(OnComplete);
            //mdnsQueryCallback = new DnsApi.MdnsQueryCallback(OnMDNSBrowse);
            BeginDNSBrowse();
            BeginDNSClient();
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
        }

        private void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            try
            {
                DisposeInternal();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        internal void BeginDNSClient()
        {
            try
            {
                mdnsClient = new MDNSClient("_omt._tcp.local");
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTDiscoveryWin32");
            }
        }
        internal void EndDNSClient()
        {
            if (mdnsClient != null)
            {
                mdnsClient.Dispose();
                mdnsClient = null;
            }
        }

        internal void StartDNSBrowseTimer()
        {
            if (browseTimer == null)
            {
                browseTimer = new Timer(BrowseTimerCallback, null, 5000, Timeout.Infinite);
            } else
            {
                browseTimer.Change(5000, Timeout.Infinite);
            }
        }

        internal void StopDNSBrowseTimer()
        {
            if (browseTimer != null)
            {
                browseTimer.Dispose();
                browseTimer = null;
            }
        }

        internal void BrowseTimerCallback(object state)
        {
            try
            {
                BeginDNSBrowse();
            } catch (Exception ex) {
                OMTLogging.Write(ex.ToString(), "OMTDiscoveryWin32");
            }
        }

        internal void BeginDNSBrowse()
        {
            lock (browseLock)
            {
                DnsApi.PDNS_SERVICE_BROWSE_REQUEST request = new DnsApi.PDNS_SERVICE_BROWSE_REQUEST();
                request.InterfaceIndex = 0;
                request.Version = DnsApi.DNS_QUERY_REQUEST_VERSION1;
                request.QueryName = "_omt._tcp.local";
                request.pBrowseCallback = Marshal.GetFunctionPointerForDelegate(browseCallback);

                if (browseCancel != null)
                {
                    browseCancel.Close();
                }
                browseCancel = new DnsApi.DnsCancelHandle(false);
                int hr = DnsApi.DnsServiceBrowse(ref request, browseCancel);
                if (hr == DnsApi.DNS_REQUEST_PENDING)
                {
                    OMTLogging.Write("BeginDNSBrowse.OK", "OMTDiscoveryWin32");
                    StopDNSBrowseTimer();
                }
                else
                {
                    OMTLogging.Write("BeginDNSBrowse.Error: " + hr, "OMTDiscoveryWin32");
                    StartDNSBrowseTimer();
                }
            }
        }

        internal void EndDnsBrowse()
        {
            lock (browseLock)
            {
                if (browseCancel != null)
                {
                    browseCancel.Close();
                    browseCancel = null;
                    OMTLogging.Write("EndDNSBrowse", "OMTDiscoveryWin32");
                }
                StopDNSBrowseTimer();
            }
        }

        private DnsApi.PDNS_SERVICE_REGISTER_REQUEST CreateRegisterRequest(string name, string machineName, int port)
        {
            DnsApi.PDNS_SERVICE_INSTANCE instance = new DnsApi.PDNS_SERVICE_INSTANCE();
            instance.dwInterfaceIndex = 0;
            instance.dwPropertyCount = 0;
            instance.ip4Address = IntPtr.Zero;
            instance.ip6Address = IntPtr.Zero;
            instance.pszInstanceName = name.Replace(".", "") + "._omt._tcp.local"; //dots not supported in instance name on Windows
            instance.pszHostName = machineName + ".local";
            instance.wPort = (ushort)port;
            instance.wPriority = 0;
            instance.wWeight = 0;

            DnsApi.PDNS_SERVICE_REGISTER_REQUEST request = new DnsApi.PDNS_SERVICE_REGISTER_REQUEST();
            request.Version = DnsApi.DNS_QUERY_REQUEST_VERSION1;
            request.InterfaceIndex = 0;
            request.pServiceInstance = Marshal.AllocHGlobal(Marshal.SizeOf(instance));
            Marshal.StructureToPtr(instance, request.pServiceInstance, false);
            request.pRegisterCompletionCallback = Marshal.GetFunctionPointerForDelegate(registerCallback);
            request.unicastEnabled = false;
            return request;
        }

        internal void DeRegisterAddressManual(OMTAddress address)
        {
            lock (lockSync)
            {
                DnsApi.PDNS_SERVICE_REGISTER_REQUEST request = CreateRegisterRequest(address.ToString(), address.MachineName, address.Port);
                EntryWin32 qr = new EntryWin32(address);
                qr.RegisterRequest = request;
                qr.RegisterRequest.pQueryContext = IntPtr.Zero;
                int hr = DnsApi.DnsServiceDeRegister(ref qr.RegisterRequest, IntPtr.Zero);
                if (hr == DnsApi.DNS_REQUEST_PENDING)
                {
                    OMTLogging.Write("DeRegister.Manual: " + address.ToString() + ":" + address.Port, "OMTDiscoveryWin32");
                }
                qr.Dispose();
            }
        }

        private void DeRegisterAddressFromContext(EntryWin32 request)
        {
            request.RegisterRequest.pQueryContext = request.ToIntPtr();
            int hr = DnsApi.DnsServiceDeRegister(ref request.RegisterRequest, IntPtr.Zero);
            if (hr == DnsApi.DNS_REQUEST_PENDING)
            {
                request.ChangeStatus(OMTDiscoveryEntryStatus.PendingDeRegister);
                OMTLogging.Write("DeRegister.Pending: " + request.Address.ToString() + ":" + request.Address.Port, "OMTDiscoveryWin32");
            }
            else
            {
                OMTLogging.Write("DeRegister.Error: " + request.Address.ToString() + ":" + request.Address.Port + ": " + hr, "OMTDiscoveryWin32");
                RemoveEntry(request.Address, true);
            }
        }

        internal override bool DeregisterAddressInternal(OMTAddress address)
        {
            try
            {
                lock (lockSync)
                {
                    OMTDiscoveryEntry request = GetEntry(address);
                    if (request != null)
                    {
                        if (request.Status == OMTDiscoveryEntryStatus.PendingRegisterAfterDeRegister)
                        {
                            request.ChangeStatus(OMTDiscoveryEntryStatus.PendingDeRegister);
                            return true;
                        } else if (request.Status == OMTDiscoveryEntryStatus.PendingRegister)
                        {
                            request.ChangeStatus(OMTDiscoveryEntryStatus.PendingDeRegisterAfterRegister);
                            return true;
                        } else if (request.Status != OMTDiscoveryEntryStatus.Registered)
                        {
                            return false;
                        }
                        if (request.Status != OMTDiscoveryEntryStatus.Discovered)
                        {
                            DeRegisterAddressFromContext((EntryWin32)request);
                        }
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTDiscoveryWin32");
            }
            return false;
        }        
        internal override bool RegisterAddressInternal(OMTAddress address)
        {
            lock (lockSync)
            {
                OMTDiscoveryEntry ctx = GetEntry(address);
                if (ctx != null)
                {
                    if (ctx.Status ==  OMTDiscoveryEntryStatus.PendingDeRegisterAfterRegister)
                    {
                        ctx.ChangeStatus(OMTDiscoveryEntryStatus.PendingRegister);
                        OMTLogging.Write("Register.CancelDeRegister: " + address.ToString() + ":" + address.Port, "OMTDiscoveryWin32");
                    } else if (ctx.Status ==  OMTDiscoveryEntryStatus.PendingDeRegister) 
                    {
                        ctx.ChangeStatus(OMTDiscoveryEntryStatus.PendingRegisterAfterDeRegister);
                        OMTLogging.Write("Register.Restore: " + address.ToString() + ":" + address.Port, "OMTDiscoveryWin32");
                    } else if (ctx.Status == OMTDiscoveryEntryStatus.Discovered)
                    {
                        DeRegisterAddressManual(address);
                        OMTLogging.Write("Register.OverrideExistingEntry: " + address.ToString() + ":" + address.Port, "OMTDiscoveryWin32");
                        RemoveEntry(address, true);
                        ctx = null;
                    }
                }
                if (ctx == null)
                {
                    DnsApi.PDNS_SERVICE_REGISTER_REQUEST request = CreateRegisterRequest(address.ToString(), address.MachineName, address.Port);
                    EntryWin32 q = new EntryWin32(address);
                    q.RegisterRequest = request;
                    q.RegisterRequest.pQueryContext = q.ToIntPtr();
                    q.RegisterCancel = new DnsApi.DnsCancelHandle(true);
                    int hr = DnsApi.DnsServiceRegister(ref q.RegisterRequest, q.RegisterCancel);
                    if (hr == DnsApi.DNS_REQUEST_PENDING)
                    {
                        q.ChangeStatus(OMTDiscoveryEntryStatus.PendingRegister);
                        AddEntry(q);
                        OMTLogging.Write("Register.Pending: " + address.ToString() + ":" + address.Port, "OMTDiscoveryWin32");
                    }
                    else
                    {
                        q.Dispose();
                        OMTLogging.Write("Register.Error: " + address.ToString() + ":" + address.Port + ": " +  hr, "OMTDiscoveryWin32");
                    }
                    return true;
                }
                return false;
            }
        }
        protected override void DisposeInternal()
        {
            AppDomain.CurrentDomain.ProcessExit -= CurrentDomain_ProcessExit;
            EndDNSClient();
            EndDnsBrowse();
            base.DisposeInternal();
        }

        void OnComplete(uint status, IntPtr pQueryContext, IntPtr instance)
        {
            try
            {
                if (status == DnsApi.ERROR_CANCELLED) return;
                if (pQueryContext != IntPtr.Zero)
                {
                    EntryWin32 q = (EntryWin32)OMTDiscoveryEntry.FromIntPtr(pQueryContext);
                    if (q != null)
                    {
                        if (q.Cancelling)
                        {
                            return;
                        }
                        lock (lockSync)
                        {
                            if (q.Status == OMTDiscoveryEntryStatus.PendingDeRegister || q.Status == OMTDiscoveryEntryStatus.PendingRegisterAfterDeRegister)
                            {
                                if (status == 0)
                                {
                                    OMTLogging.Write("DeRegister.Complete: " + q.Address.ToString() + ":" + q.Address.Port, "OMTDiscoveryWin32");
                                }
                                else
                                {
                                    OMTLogging.Write("DeRegister.Error: " + q.Address.ToString() + ":" + q.Address.Port + ": " + status, "OMTDiscoveryWin32");
                                }
                                RemoveEntry(q.Address, true);
                                if (q.Status == OMTDiscoveryEntryStatus.PendingRegisterAfterDeRegister)
                                {
                                    RegisterAddressInternal(q.Address);
                                }
                            }
                            else if (q.Status ==  OMTDiscoveryEntryStatus.PendingRegister || q.Status ==  OMTDiscoveryEntryStatus.PendingDeRegisterAfterRegister)
                            {
                                if (status == 0)
                                {
                                    if (q.RegisterCancel != null)
                                    {
                                        q.RegisterCancel.Clear();
                                        q.RegisterCancel = null;
                                    } 
                                    if (instance != IntPtr.Zero)
                                    {
                                        DnsApi.PDNS_SERVICE_INSTANCE i = (DnsApi.PDNS_SERVICE_INSTANCE)Marshal.PtrToStructure(instance, typeof(DnsApi.PDNS_SERVICE_INSTANCE));
                                        if (q.RegisterRequest.pServiceInstance != IntPtr.Zero)
                                        {                                                                                        
                                            //We need to update here as the DNS service may rename the dns name due to duplicates. We need that new name to deregister properly.
                                            Marshal.StructureToPtr(i, q.RegisterRequest.pServiceInstance, true);
                                        }
                                    }
                                    if (q.Status ==  OMTDiscoveryEntryStatus.PendingDeRegisterAfterRegister)
                                    {
                                        OMTLogging.Write("Register.DeRegister: " + q.Address.ToString() + ":" + q.Address.Port, "OMTDiscoveryWin32");
                                        DeRegisterAddressFromContext(q);
                                    } else
                                    {
                                        q.ChangeStatus(OMTDiscoveryEntryStatus.Registered);
                                        OMTLogging.Write("Register.Complete: " + q.Address.ToString() + ":" + q.Address.Port, "OMTDiscoveryWin32");
                                    }
                                }
                                else
                                {
                                    OMTLogging.Write("RegisterAddress.Error: " + q.Address.ToString() + ":" + q.Address.Port + ": " + status, "OMTDiscoveryWin32");
                                    RemoveEntry(q.Address, true);
                                }
                            }
                        }
                        
                    }
                }
                if (instance != IntPtr.Zero)
                {
                    DnsApi.DnsServiceFreeInstance(instance);
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTDiscoveryWin32");
            }
        }

        void ProcessDnsRecord(IntPtr pDnsRecord)
        {
            if (pDnsRecord != IntPtr.Zero)
            {
                try
                {
                    string addressName = "";
                    int addressPort = 0;
                    IntPtr pNext = pDnsRecord;
                    List<IPAddress> addresses = new List<IPAddress>();
                    int dwTtl = 0;
                    while (pNext != IntPtr.Zero)
                    {
                        string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pNext, IntPtr.Size));
                        DnsApi.DNS_TYPE wType = (DnsApi.DNS_TYPE)Marshal.ReadInt16(pNext, IntPtr.Size * 2);
                        if (wType == DnsApi.DNS_TYPE.DNS_TYPE_SRV)
                        {
                            addressName = ParseAddressName(name);
                            byte[] b = new byte[2];
                            Marshal.Copy(pNext + (IntPtr.Size * 3) + 16 + 4, b, 0, 2);
                            addressPort = BitConverter.ToUInt16(b, 0);
                            dwTtl = Marshal.ReadInt32(pNext, (IntPtr.Size * 2) + 8);
                        }
                        else if (wType == DnsApi.DNS_TYPE.DNS_TYPE_A)
                        {
                            byte[] b = new byte[4];
                            Marshal.Copy(pNext + (IntPtr.Size * 2) + 16, b, 0, b.Length);
                            IPAddress ip = new IPAddress(b);
                            addresses.Add(ip);
                        }
                        else if (wType == DnsApi.DNS_TYPE.DNS_TYPE_AAAA)
                        {
                            byte[] b = new byte[16];
                            Marshal.Copy(pNext + (IntPtr.Size * 2) + 16, b, 0, b.Length);
                            IPAddress ip = new IPAddress(b);
                            addresses.Add(ip);
                        }
                        else if (wType == DnsApi.DNS_TYPE.DNS_TYPE_PTR)
                        {
                            string nameHost = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pNext, (IntPtr.Size * 2) + 16));
                            if (!String.IsNullOrEmpty(nameHost))
                            {
                                dwTtl = Marshal.ReadInt32(pNext, (IntPtr.Size * 2) + 8);
                                addressName = ParseAddressName(nameHost);
                            }
                        }
                        pNext = Marshal.ReadIntPtr(pNext);
                    }
                    if (OMTAddress.IsValid(addressName) && ((addressPort > 0) || dwTtl == 0))
                    {
                        addressName = OMTAddress.UnescapeFullName(addressName);

                        //Sometimes when a process closes the discovery entry is not properly deleted
                        //If we know the discovered entry is no longer valid due to using the same port, we manually de-register it here
                        OMTDiscoveryEntry portMatch = GetEntryByPort(addressPort, false);
                        if (portMatch != null)
                        {
                            OMTAddress address = OMTAddress.Create(addressName, addressPort);
                            if (portMatch.Address.MachineName == address.MachineName && portMatch.Address.ToString() != address.ToString())
                            {
                                OMTLogging.Write("DuplicatePort: " + address.ToString() + ":" + addressPort, "OMTDiscoveryWin32");
                                DeRegisterAddressManual(address);
                            }
                        }

                        if (dwTtl == 0)
                        {
                            RemoveDiscoveredEntry(addressName);
                        }
                        else
                        {
                            OMTDiscoveryEntry entry = UpdateDiscoveredEntry(addressName, addressPort, addresses.ToArray());
                            if (entry != null)
                            {
                                if (dwTtl < 60)
                                {
                                    entry.Expiry = DateTime.Now.AddSeconds(dwTtl);
                                }
                                else
                                {
                                    entry.Expiry = DateTime.MinValue;
                                }
                            }
                        }
                    }
                    DnsApi.DnsFree(pDnsRecord, DnsApi.DNS_FREE_TYPE.DnsFreeRecordList);
                    RemoveExpiredAddresses();
                }
                catch (Exception ex)
                {
                    OMTLogging.Write(ex.ToString(), "OMTDiscoveryWin32");
                }
            }
        }
        void OnBrowse(uint status, IntPtr pQueryContext, IntPtr pDnsRecord)
        {
            ProcessDnsRecord(pDnsRecord);
        }

        //void OnMDNSBrowse(IntPtr pQueryContext, IntPtr pQueryHandle, ref DnsApi.PMDNS_QUERY_RESULT pQueryResults)
        //{
        //    ProcessDnsRecord(pQueryResults.pQueryRecords);
        //}
    }
}
