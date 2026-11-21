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
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace libomtnet
{
    internal class OMTSocketAsyncPool : OMTBase
    {
        private Queue<SocketAsyncEventArgs> pool;
        private int bufferSize;
        private object lockSync = new object(); 

        protected virtual void OnCompleted(object sender, SocketAsyncEventArgs e)
        {
            // TiXL patch: runs on an I/O completion thread, where an unhandled exception terminates the process.
            try
            {
                if (e.SocketError != SocketError.Success)
                {
                    OMTLogging.Write("Socket Pool Error: " + e.SocketError.ToString() + "," + e.BytesTransferred, "OMTSocketAsyncPool");
                }
                ReturnEventArgs(e);
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTSocketAsyncPool");
            }
        }

        public object SyncObject { get { return lockSync; } }

        public OMTSocketAsyncPool(int count, int bufferSize)
        {
            this.bufferSize = bufferSize;
            pool = new Queue<SocketAsyncEventArgs>();
            for (int i = 0; i < count; i++)
            {
                SocketAsyncEventArgs e = new SocketAsyncEventArgs();
                if (bufferSize > 0)
                {
                    byte[] buf = new byte[bufferSize];
                    e.SetBuffer(buf,0,buf.Length);
                }
                e.Completed += OnCompleted;
                pool.Enqueue(e);
            }
        }

        public void Resize(SocketAsyncEventArgs e, int length)
        {
            if (e != null)
            {
                if (e.Buffer.Length < length)
                {
                    byte[] buf = new byte[length];
                    e.SetBuffer(buf, 0, buf.Length);
                    Debug.WriteLine("SocketPool.Resize: " + length);
                }
            }
        }

        public void SendAsync(Socket socket, SocketAsyncEventArgs e)
        {
            lock (lockSync)
            {
                if (socket != null)
                {
                    if (socket.SendAsync(e) == false)
                    {
                        OnCompleted(this, e);
                    }
                }
            }
        }

        internal SocketAsyncEventArgs GetEventArgs()
        {
            lock (lockSync)
            {
                if (pool == null) return null;
                if (pool.Count > 0) {
                   SocketAsyncEventArgs e = pool.Dequeue();
                   e.SetBuffer(0, e.Buffer.Length);
                   return e;
                } 
            }
            return null;
        }

        public int Count { get { lock (pool)  { return pool.Count; } } }

        internal void ReturnEventArgs(SocketAsyncEventArgs e)
        {
            lock (lockSync)
            {
                if (pool == null)
                {
                    e.Dispose();
                } else
                {
                    pool.Enqueue(e);
                }
            }
        }

        protected override void DisposeInternal()
        {
            lock (lockSync)
            {
                if (pool != null)
                {
                    while (pool.Count > 0)
                    {
                        SocketAsyncEventArgs e = pool.Dequeue();
                        if (e != null)
                        {
                            e.Completed -= OnCompleted;
                            e.Dispose();
                        }
                    }
                    pool.Clear();
                    pool = null;
                }
            }
            base.DisposeInternal();
        }
    }
}
