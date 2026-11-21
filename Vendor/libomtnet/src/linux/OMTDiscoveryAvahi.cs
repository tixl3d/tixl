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
using System.Runtime.InteropServices;
using static libomtnet.linux.AvahiClient;
using System.Threading;

namespace libomtnet.linux
{
    internal class OMTDiscoveryAvahi : OMTDiscovery
    {
        private IntPtr client = IntPtr.Zero;
        private IntPtr simplePoll = IntPtr.Zero;
        private IntPtr poll = IntPtr.Zero;
        private IntPtr browser = IntPtr.Zero;
        private AvahiClient.AvahiClientCallback clientCallback;
        private AvahiClient.AvahiServiceBrowserCallback serviceBrowserCallback;
        private AvahiClient.AvahiServiceResolverCallback serviceResolverCallback;
        private AvahiClient.AvahiEntryGroupCallback entryGroupCallback;
        private IntPtr serviceType;

        private Thread eventThread;
        private bool eventThreadRunning = false;

        private class EntryAvahi : OMTDiscoveryEntry
        {
            public EntryAvahi(OMTAddress address) : base(address) { }

            public IntPtr Group;
            protected override void DisposeInternal()
            {
                if (Group != IntPtr.Zero)
                {
                    AvahiClient.avahi_entry_group_free(Group);
                    Group = IntPtr.Zero;
                }
                base.DisposeInternal();
            }
        }

        internal OMTDiscoveryAvahi()
        {
            serviceType = OMTUtils.StringToPtrUTF8("_omt._tcp");

            clientCallback = new AvahiClient.AvahiClientCallback(ClientCallback);
            entryGroupCallback = new AvahiClient.AvahiEntryGroupCallback(EntryGroupCallback);
            serviceBrowserCallback = new AvahiClient.AvahiServiceBrowserCallback(ServiceBrowserCallback);
            serviceResolverCallback = new AvahiClient.AvahiServiceResolverCallback(ServiceResolverCallback);

            simplePoll = AvahiClient.avahi_simple_poll_new();
            if (simplePoll == IntPtr.Zero) {
                OMTLogging.Write("Failure creating simple poll", "OMTDiscoveryAvahi");
                return;
            }
            poll = AvahiClient.avahi_simple_poll_get(simplePoll);
            if (poll == IntPtr.Zero) {
                OMTLogging.Write("Failure retrieving poll", "OMTDiscoveryAvahi");
                return;
            }
            int hr = 0;
            client = AvahiClient.avahi_client_new(poll, 0, Marshal.GetFunctionPointerForDelegate(clientCallback), IntPtr.Zero, ref hr);
            if (client == IntPtr.Zero)
            {
                // TiXL patch: avahi asserts on a NULL client (abort, not an exception), so without a running
                // avahi-daemon discovery stays off and direct omt://host:port addresses still work.
                OMTLogging.Write("Failure creating client: " + hr, "OMTDiscoveryAvahi");
                return;
            }
            browser = AvahiClient.avahi_service_browser_new(client, AvahiClient.AVAHI_IF_UNSPEC, AvahiClient.AVAHI_PROTO_UNSPEC
                , serviceType, IntPtr.Zero, 0, Marshal.GetFunctionPointerForDelegate(serviceBrowserCallback), IntPtr.Zero);
            if (browser == IntPtr.Zero)
            {
                OMTLogging.Write("Failure creating browser: " + hr, "OMTDiscoveryAvahi");
            }
            eventThreadRunning = true;
            eventThread = new Thread(EventThread);
            eventThread.IsBackground = true;
            eventThread.Start();
            OMTLogging.Write("BrowserStarted", "OMTDiscoveryAvahi");
        }

        private void EventThread()
        {
            try
            {
                int hr = 0;
                while (eventThreadRunning)
                {
                    hr = AvahiClient.avahi_simple_poll_iterate(simplePoll, -1);
                    if (hr != 0)
                    {
                        OMTLogging.Write("EventThead exiting...", "OMTDiscoveryAvahi");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTDiscoveryAvahi.EventThread");
            }

        }

        protected override void DisposeInternal()
        {
            eventThreadRunning = false;
            if (simplePoll != IntPtr.Zero)
            {
                AvahiClient.avahi_simple_poll_quit(simplePoll);
            }
            if (eventThread != null)
            {
                eventThread.Join(5000); // TiXL patch: Thread.Abort throws on .NET Core
                eventThread = null;
            }
            if (browser != IntPtr.Zero)
            {
                AvahiClient.avahi_service_browser_free(browser);
                browser = IntPtr.Zero;
            }
            if (client != IntPtr.Zero)
            {
                AvahiClient.avahi_client_free(client);
                client = IntPtr.Zero;
            }
            if (serviceType != IntPtr.Zero) 
            {
                Marshal.FreeHGlobal(serviceType);
                serviceType = IntPtr.Zero;
            }
            if (simplePoll != IntPtr.Zero)
            {
                AvahiClient.avahi_simple_poll_free(simplePoll);
                simplePoll = IntPtr.Zero;
                poll = IntPtr.Zero;
            }
            base.DisposeInternal();
        }

        internal override bool DeregisterAddressInternal(OMTAddress address)
        {
            lock (lockSync)
            {
                OMTDiscoveryEntry entry = GetEntry(address);
                if (entry != null)
                {
                    RemoveEntry(entry.Address, true);
                    OMTLogging.Write("DeRegisterAddress: " + address.ToString(), "OMTDiscoveryAvahi");
                    return true;
                }
                return false;
            }
        }
        internal override bool RegisterAddressInternal(OMTAddress address)
        {
            lock (lockSync)
            {
                OMTDiscoveryEntry entry = GetEntry(address);
                if (entry == null)
                {
                    if (client == IntPtr.Zero) return false; // TiXL patch: see constructor
                    EntryAvahi ctx = new EntryAvahi(address);
                    ctx.Group = AvahiClient.avahi_entry_group_new(client, Marshal.GetFunctionPointerForDelegate(entryGroupCallback), IntPtr.Zero);
                    if (ctx.Group == IntPtr.Zero)
                    {
                        OMTLogging.Write("Could not create avahi group", "OMTDiscoveryAvahi");
                        return false;
                    }
                    IntPtr pName = OMTUtils.StringToPtrUTF8(address.ToString());
                    ushort port = (ushort)address.Port;
                    int hr = AvahiClient.avahi_entry_group_add_service(ctx.Group, AvahiClient.AVAHI_IF_UNSPEC, AVAHI_PROTO_UNSPEC, 0, pName, serviceType, IntPtr.Zero, IntPtr.Zero, port, IntPtr.Zero);
                    Marshal.FreeHGlobal(pName);
                    if (hr != 0)
                    {
                        OMTLogging.Write("Could not add entry to avahi group", "OMTDiscoveryAvahi");
                        return false;
                    }
                    hr = AvahiClient.avahi_entry_group_commit(ctx.Group);
                    if (hr != 0)
                    {
                        OMTLogging.Write("Could not commit new avahi group", "OMTDiscoveryAvahi");
                        return false;
                    }
                    OMTLogging.Write("RegisterAddress.Success: " + address.ToString(), "OMTDiscoveryAvahi");
                    ctx.ChangeStatus(OMTDiscoveryEntryStatus.Registered);
                    AddEntry(ctx);
                    return true;
                }
            }
            return false;
        }

        private void ClientCallback(IntPtr s, int state, IntPtr userData)
        {
        }
        private void EntryGroupCallback(IntPtr group, int state, IntPtr userData)
        {            
        }
        private void ServiceBrowserCallback(IntPtr b, int iface, int protocol, AvahiBrowserEvent evt, IntPtr name, IntPtr type, IntPtr domain, int flags, IntPtr userData)
        {
            // TiXL patch: an exception escaping a native callback terminates the process.
            try
            {
                if (evt == AvahiBrowserEvent.AVAHI_BROWSER_NEW)
                {
                    IntPtr resolver = AvahiClient.avahi_service_resolver_new(client, iface, protocol
                        , name, type, domain, AvahiClient.AVAHI_PROTO_UNSPEC, 0, Marshal.GetFunctionPointerForDelegate(serviceResolverCallback), IntPtr.Zero);
                    if (resolver == IntPtr.Zero)
                    {
                        OMTLogging.Write("Failure creating resolver", "OMTDiscoveryAvahi");
                        return;
                    }
                } else if (evt == AvahiBrowserEvent.AVAHI_BROWSER_REMOVE)
                {
                    if (name != IntPtr.Zero)
                    {
                        string szName = OMTUtils.PtrToStringUTF8(name);
                        RemoveDiscoveredEntry(szName);
                    }               
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTDiscoveryAvahi.ServiceBrowserCallback");
            }
        }
        private void ServiceResolverCallback(IntPtr r, int iface, int protocol, AvahiResolverEvent evt, IntPtr name, IntPtr type, IntPtr domain,
            IntPtr host_name, IntPtr a, UInt16 port, IntPtr txt, int flags, IntPtr userData)
        {
            try
            {
                if (evt == AvahiResolverEvent.AVAHI_RESOLVER_FOUND)
                {
                    if (name != IntPtr.Zero)
                    {
                        IPAddress ip = null;
                        if (a != IntPtr.Zero)
                        {
                            int proto = Marshal.ReadInt32(a);
                            if (proto == AVAHI_PROTO_INET)
                            {
                                byte[] addr = new byte[4];
                                Marshal.Copy(a + 4, addr, 0, 4);
                                ip = new IPAddress(addr);
                            }
                            else if (proto == AVAHI_PROTO_INET6)
                            {
                                byte[] addr = new byte[16];
                                Marshal.Copy(a + 4, addr, 0, 16);
                                ip = new IPAddress(addr, 0);
                            }
                         }
                        string addressName = OMTUtils.PtrToStringUTF8(name);
                        if (OMTAddress.IsValid(addressName))
                        {
                            UpdateDiscoveredEntry(addressName, port, new IPAddress[] { ip });
                        } else
                        {
                            OMTLogging.Write("InvalidAddressReceived: " + addressName, "OMTDiscoveryAvahi");
                        }

                    }
                }
                if (r != IntPtr.Zero)
                {
                    AvahiClient.avahi_service_resolver_free(r);
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTDiscoveryAvahi.Resolver");
            }
        }

    }
}
