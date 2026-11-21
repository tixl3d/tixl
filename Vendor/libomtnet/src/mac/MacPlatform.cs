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
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace libomtnet.mac
{
    internal class MacPlatform : OMTPlatform
    {
        private const int RTLD_NOW = 2;
        private const int RTLD_GLOBAL = 8;

        [DllImport("libdl.dylib")]
        static extern IntPtr dlopen(string filename, int flags);

        [DllImport("libc")]
        private static extern uint getuid();

        [DllImport("libc")]
        private static extern IntPtr getpwuid(uint uid);

        [DllImport("libc")]
        private static extern int gethostname(IntPtr name, IntPtr size);

        public override string GetMachineName()
        {
            int len = 4096;
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                int result = gethostname(buf, (IntPtr)len);
                if (result == 0)
                {
                    string name = OMTUtils.PtrToStringUTF8(buf);
                    if (!String.IsNullOrEmpty(name))
                    {
                        return name.ToUpperInvariant();
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
            OMTLogging.Write("Unable to retrieve full hostname", "MacPlatform");
            return base.GetMachineName();
        }

        public override string GetStoragePath()
        {
            string sz = Environment.GetEnvironmentVariable("OMT_STORAGE_PATH");
            if (!String.IsNullOrEmpty(sz)) return sz;
            sz = GetRealUserHome();
            if (!String.IsNullOrEmpty(sz)) {
                return Path.Combine(sz, ".OMT"); 
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".OMT");
        }

        /// <summary>
        /// Attempts to get the real user home directory by manually reading pointer offset into passwd struct.
        /// </summary>
        /// <returns>Real user home directory path or null if unavailable.</returns>
        private static string GetRealUserHome()
        {
            try
            {
                uint uid = getuid();
                IntPtr pwdPtr = getpwuid(uid);
                if (pwdPtr == IntPtr.Zero) return null;

                int offsetOfPwDir = IntPtr.Size * 6;
                IntPtr pwDirPtr = Marshal.ReadIntPtr(pwdPtr, offsetOfPwDir);
                if (pwDirPtr == IntPtr.Zero) return null;

                string home = Marshal.PtrToStringAnsi(pwDirPtr);
                return home;
            }
            catch
            {
                return null;
            }
        }

        public override IntPtr OpenLibrary(string filename)
        {
            return dlopen(filename, RTLD_NOW | RTLD_GLOBAL);
        }

        protected override string GetLibraryExtension()
        {
            return ".dylib";
        }
    }
}
