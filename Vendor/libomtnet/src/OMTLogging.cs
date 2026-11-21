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
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace libomtnet
{
    public class OMTLogging
    {
        public delegate void LogCallbackDelegate(string line);

        private static FileStream logStream;
        private static StreamWriter logWriter;
        private static object lockSync = new object();
        private static Thread loggingThread;
        private static bool threadRunning;
        private static Queue<string> queue = new Queue<string>();
        private static AutoResetEvent readyEvent = new AutoResetEvent(false);
        private static bool initialized = false;
        private static LogCallbackDelegate logCallback;
        private const int MaxQueuedLines = 1000;

        static OMTLogging()
        {
            loggingThread = new Thread(ProcessLog);
            loggingThread.IsBackground = true;
            threadRunning = true;
            loggingThread.Start();
        }

        private static void SetDefaultLogFilename()
        {
            lock (lockSync)
            {
                initialized = true;
                try
                {
                    string name = GetProcessNameAndId();
                    if (name != null)
                    {
                        string szPath = OMTPlatform.GetInstance().GetStoragePath();
                        if (Directory.Exists(szPath) == false)
                        {
                            Directory.CreateDirectory(szPath);
                        }
                        szPath = szPath + Path.DirectorySeparatorChar + "logs";
                        if (Directory.Exists(szPath) == false)
                        {
                            Directory.CreateDirectory(szPath);
                        }
                        SetFilename(szPath + Path.DirectorySeparatorChar + name + ".log");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        private static string GetProcessNameAndId()
        {
            Process process = Process.GetCurrentProcess();
            if (process != null)
            {
                ProcessModule module = process.MainModule;
                if (module != null) //Some platforms, notably iOS return null
                {
                    return module.ModuleName + process.Id;
                } else
                {
                    return process.Id.ToString();
                }
            }
            return null;
        }

        private static void ProcessLog()
        {
            try
            {
                while (threadRunning)
                {
                    readyEvent.WaitOne();
                    lock (lockSync)
                    {
                        if (logWriter != null || logCallback != null )
                        {
                            while (queue.Count > 0)
                            {
                                string line = queue.Dequeue();
                                // TiXL patch: one failing write or callback must not end the thread, or the queue
                                // below would grow for the rest of the process.
                                try
                                {
                                    if ( logWriter != null )
                                    {
                                        logWriter.WriteLine(line);
                                    }
                                    if (logCallback != null)
                                    {
                                        logCallback(line);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine(ex.ToString(), "OMTLogging.ProcessLog");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString(), "OMTLogging.ProcessLog");
            }
        }

        /// <summary>
        /// Shutdown method for unmanaged callers that need to close the logging thread gracefully
        /// </summary>
        public static void Shutdown()
        {
            if (threadRunning)
            {
                Write("Shutdown", "OMTLogging");
                threadRunning = false;
                readyEvent.Set();
                loggingThread.Join();
                lock (lockSync)
                {
                    if (logStream != null)
                    {
                        logStream.Close();
                        logStream = null;
                        logWriter = null;
                    }
                }
            }
        }

        public static void SetFilename(string filename)
        {
            lock (lockSync)
            {
                initialized = true;
                if (logStream != null)
                {
                    logStream.Close();
                }
                logWriter = null;
                if (!String.IsNullOrEmpty(filename)) {
                    logStream = new FileStream(filename, FileMode.OpenOrCreate, FileAccess.Write);
                    logStream.Position = logStream.Length;
                    logWriter = new StreamWriter(logStream);
                    logWriter.AutoFlush = true;
                    OMTLogging.Write("Log Started", "OMTLogging");
                }
            }
        }

        public static void SetCallback(LogCallbackDelegate callback)
        {
            lock (lockSync)
            {
                logCallback = callback;
            }
        }

        public static void Write(string message, string source)
        {
            try
            {
                string line = DateTime.Now.ToString() + ",[" + source + "]," + message;
                Debug.WriteLine(line);
                lock (lockSync)
                {
                    if (!initialized)
                    {
                        SetDefaultLogFilename();
                    }
                    // TiXL patch: a callback alone receives lines too, and the queue is bounded.
                    if ((logWriter != null || logCallback != null) && queue.Count < MaxQueuedLines)
                    {
                        queue.Enqueue(line);
                        readyEvent.Set();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }
    }
}
