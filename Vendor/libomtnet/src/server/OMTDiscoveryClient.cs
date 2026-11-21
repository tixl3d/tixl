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
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml;

namespace libomtnet
{
    /// <summary>
    /// This is an internal class used to manage connection to the OMT Discovery Server
    /// This should not be used directly by client apps, and is declared public for internal testing purposes only.
    /// </summary>
    public class OMTDiscoveryClient : OMTBase
    {
        private OMTReceive client = null;
        private OMTDiscovery discovery = null;
        private Thread processingThread = null;
        private bool threadExit = false;

        public OMTDiscoveryClient(string address, OMTDiscovery discovery)
        {
            this.discovery = discovery;
            this.client = new OMTReceive(address, this);
            StartClient();
            OMTLogging.Write("Started: " + address, "OMTDiscoveryClient");
        }

        private void StartClient()
        {
            if (processingThread == null)
            {
                threadExit = false;
                processingThread = new Thread(ProcessThread);
                processingThread.IsBackground = true;
                processingThread.Start();
            }
        }
        private void StopClient()
        {
            if (processingThread != null)
            {
                threadExit = true;
                processingThread.Join();
                processingThread = null;
            }
        }

        internal void SendAddress(OMTAddress address)
        {
            try
            {
                string xml = address.ToXML();
                int bytes = client.SendMetadata(new OMTMetadata(0, xml));
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTDiscoveryClient");
            }
        }

        internal void SendAll()
        {
            try
            {
                OMTAddress[] addresses = discovery.GetAddressesInternal();
                if (addresses != null)
                {
                    foreach (OMTAddress a in addresses)
                    {
                        SendAddress(a);
                    }
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTDiscoveryClient");
            }
        }

        internal void Connected()
        {
            try
            {
                OMTLogging.Write("Connected to server", "OMTDiscoveryClient");
                SendAll();
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTDiscoveryClient");
            }
        }

        internal void Disconnected()
        {
            try
            {
                OMTLogging.Write("Disconnected from server", "OMTDiscoveryClient");
                discovery.RemoveServerAddresses();
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTDiscoveryClient");
            }
        }

        private void ProcessThread()
        {
            try
            {
                OMTMetadata frame = null;
                while (threadExit == false)
                {
                    if (client.Receive(400, ref frame))
                    {
                        if (frame != null)
                        {
                            try
                            {
                                OMTAddress a = OMTAddress.FromXML(frame.XML);
                                if (a != null)
                                {
                                    if (a.removed)
                                    {
                                        discovery.RemoveEntry(a, true);
                                        OMTLogging.Write("RemovedFromServer: " + a.ToString(), "OMTDiscoveryClient");
                                    }
                                    else
                                    {
                                        OMTLogging.Write("NewFromServer: " + a.ToString(), "OMTDiscoveryClient");
                                        OMTDiscoveryEntry e = discovery.UpdateDiscoveredEntry(a.ToString(), a.Port, a.Addresses);
                                        if (e != null)
                                        {
                                            e.FromServer = true;
                                        }
                                    }
                                } else
                                {
                                    OMTLogging.Write("Invalid XML Received: " + frame.XML, "OMTDiscoveryClient");
                                }
                            }
                            catch (Exception ex)
                            {
                                OMTLogging.Write(ex.ToString(), "OMTDiscoveryClient");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTDiscoveryClient");
            }
        }

        protected override void DisposeInternal()
        {
            StopClient();
            if (client != null)
            {
                client.Dispose();
                client = null;
            }
            discovery = null;
            base.DisposeInternal();
        }
    }
}
