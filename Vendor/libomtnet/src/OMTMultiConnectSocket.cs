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
using System.Net;
using System.Threading;
using System.Net.Sockets;
using System.Diagnostics;

namespace libomtnet
{
    public class OMTMultiConnectSocket
    {
        private IPAddress[] addresses;
        private int port;
        private int timeoutMilliseconds;
        private AsyncCallback completedCallback;
        private MultiAsyncResult result;

        public OMTMultiConnectSocket(IPAddress[] ipAddresses, int port, int timeoutMilliseconds)
        {
            if (ipAddresses == null) throw new ArgumentNullException("ipAddresses");
            if (ipAddresses.Length == 0) throw new ArgumentException("ipAddresses must contain at least one IPAddress");
            this.addresses = ipAddresses;
            this.port = port;
            this.timeoutMilliseconds = timeoutMilliseconds;
        }

        private void ConnectThread(object state)
        {
            AsyncCallback callback = completedCallback;
            Socket connected = null;
            Exception lastError = null;
            MultiAsyncResult result = (MultiAsyncResult)state;
            int index = 0;
            try
            {
                IPAddress next;
                while (connected == null && index < addresses.Length && (next = addresses[index++]) != null)
                {
                    Socket s = null;
                    try
                    {
                        s = OMTUtils.CreateStreamSocket();
                        if (s.AddressFamily == AddressFamily.InterNetwork)
                        {
                            if (next.IsIPv4MappedToIPv6) next = next.MapToIPv4();
                            else if (next.AddressFamily != AddressFamily.InterNetwork) throw new NotSupportedException("IPv6 is disabled on this machine");
                        }
                        IAsyncResult ar = s.BeginConnect(next, port, null, null);
                        if (ar.AsyncWaitHandle.WaitOne(timeoutMilliseconds, false))
                        {
                            s.EndConnect(ar);
                            connected = s;
                        }
                        else
                        {
                            lastError = new TimeoutException("Connection to " + next.ToString() + " timed out");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.ToString());
                        lastError = ex;
                    }
                    finally
                    {
                        if (connected == null && s != null) s.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
            if (connected != null)
                result.SetCompleted(connected);
            else if (lastError == null)
            {
                result.SetError(new InvalidOperationException("No addresses to connect to"));
            }
            else
            {
                result.SetError(lastError);
            }
            try
            {
                if (callback != null)
                    callback(result);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        private class MultiAsyncResult : IAsyncResult
        {

            private Socket socket;
            private volatile bool completed;
            private ManualResetEvent waitEvent;
            private Exception ex;
            private object state;
            public MultiAsyncResult(object state)
            {
                this.state = state;
                waitEvent = new ManualResetEvent(false);
            }

            public Socket Socket { get { return socket; } }

            public Socket EndConnect()
            {
                waitEvent.WaitOne();
                if (ex != null)
                {
                    throw ex;
                }
                return socket;
            }

            private void SetCompleted(Socket socket, Exception ex)
            {
                this.socket = socket;
                this.ex = ex;
                completed = true;
                waitEvent.Set();
            }

            public void SetCompleted(Socket socket)
            {
                SetCompleted(socket, null);
            }

            public void SetError(Exception ex)
            {
                SetCompleted(null, ex);
            }

            public bool IsCompleted
            {
                get
                {
                    return completed;
                }
            }
            public WaitHandle AsyncWaitHandle
            {
                get
                {
                    return waitEvent;
                }
            }
            public object AsyncState
            {
                get
                {
                    return state;
                }
            }
            public bool CompletedSynchronously
            {
                get
                {
                    return false;
                }
            }
        }

        public IAsyncResult BeginConnect(AsyncCallback socketCallback, object state)
        {
            if (result != null)
            {
                throw new InvalidOperationException("Already in progress");
            }
            completedCallback = socketCallback;
            result = new MultiAsyncResult(state);
            ThreadPool.QueueUserWorkItem(ConnectThread, result);
            return result;
        }

        public Socket EndConnect(IAsyncResult ar)
        {
            if (result == null || Object.ReferenceEquals(result, ar) == false)
            {
                throw new InvalidOperationException("Invalid state");
            }
            try
            {
                return result.EndConnect();
            }
            finally
            {
                result = null;
            }
        }
    }
}
