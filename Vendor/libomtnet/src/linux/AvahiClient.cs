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
using System.Runtime.InteropServices;

namespace libomtnet.linux
{
    internal class AvahiClient
    {
        public const string DLL_PATH_CLIENT = "libavahi-client.so.3";
        public const string DLL_PATH_COMMON = "libavahi-common.so.3";

        public const int AVAHI_PROTO_UNSPEC = -1;
        public const int AVAHI_PROTO_INET = 0;
        public const int AVAHI_PROTO_INET6 = 1;
        public const int AVAHI_IF_UNSPEC = -1;

        public delegate void AvahiClientCallback(IntPtr s, int state, IntPtr userData);
        public delegate void AvahiServiceBrowserCallback(IntPtr b, int iface, int protocol, AvahiBrowserEvent evt, IntPtr name, IntPtr type, IntPtr domain, int flags, IntPtr userData);
        public delegate void AvahiServiceResolverCallback(IntPtr r, int iface, int protocol, AvahiResolverEvent evt, IntPtr name, IntPtr type, IntPtr domain,
            IntPtr host_name, IntPtr a, UInt16 port, IntPtr txt, int flags, IntPtr userData);
        public delegate void AvahiEntryGroupCallback(IntPtr group, int state, IntPtr userData);

        public enum AvahiBrowserEvent
        {
            AVAHI_BROWSER_NEW,
            AVAHI_BROWSER_REMOVE,
            AVAHI_BROWSER_CACHE_EXHAUSTED,
            AVAHI_BROWSER_ALL_FOR_NOW,
            AVAHI_BROWSER_FAILURE
        }

        public enum AvahiResolverEvent
        {
            AVAHI_RESOLVER_FOUND,
            AVAHI_RESOLVER_FAILURE
        }

        [DllImport(DLL_PATH_COMMON)]
        public static extern IntPtr avahi_simple_poll_new();

        [DllImport(DLL_PATH_COMMON)]
        public static extern void avahi_simple_poll_free(IntPtr sp);

        [DllImport(DLL_PATH_COMMON)]
        public static extern void avahi_simple_poll_quit(IntPtr sp);

        [DllImport(DLL_PATH_COMMON)]
        public static extern IntPtr avahi_simple_poll_get(IntPtr s);

        [DllImport(DLL_PATH_COMMON)]
        public static extern int avahi_simple_poll_iterate(IntPtr sp, int sleepTime);

        [DllImport(DLL_PATH_CLIENT)]
        public static extern IntPtr avahi_client_new(IntPtr poll, int flags, IntPtr callback, IntPtr userdata, ref int error);
        [DllImport(DLL_PATH_CLIENT)]
        public static extern IntPtr avahi_service_browser_new(IntPtr client, int iface, int protocol, IntPtr type, IntPtr domain, int flags, IntPtr callback, IntPtr userData);

        [DllImport(DLL_PATH_CLIENT)]
        public static extern void avahi_client_free(IntPtr client);

        [DllImport(DLL_PATH_CLIENT)]
        public static extern int avahi_service_browser_free(IntPtr browser);

        [DllImport(DLL_PATH_CLIENT)]
        public static extern IntPtr avahi_service_resolver_new(IntPtr client, int iface, int protocol, IntPtr name, IntPtr type, IntPtr domain, int aprotocol, int flags, IntPtr callback, IntPtr userData);

        [DllImport(DLL_PATH_CLIENT)]
        public static extern int avahi_service_resolver_free(IntPtr resolver);

        [DllImport(DLL_PATH_CLIENT)]
        public static extern IntPtr avahi_entry_group_new(IntPtr client, IntPtr callback, IntPtr userData);
        [DllImport(DLL_PATH_CLIENT)]
        public static extern int avahi_entry_group_free(IntPtr group);

        [DllImport(DLL_PATH_CLIENT)]
        public static extern int avahi_entry_group_add_service(IntPtr group, int iface, int protocol, int flags, IntPtr name,
            IntPtr type, IntPtr domain, IntPtr host, UInt16 port, IntPtr args);

        [DllImport(DLL_PATH_CLIENT)]
        public static extern int avahi_entry_group_commit(IntPtr group);
    }
}
