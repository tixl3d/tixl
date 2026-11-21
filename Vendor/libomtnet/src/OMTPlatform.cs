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
using System.IO;

namespace libomtnet
{
    public class OMTPlatform
    {
        private static OMTPlatform instance;
        private static object globalLock = new object();
        private static OMTPlatformType platformType;

        static OMTPlatform()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                platformType = OMTPlatformType.Win32;
            }
            else
            {
                platformType = OMTPlatformType.Linux;
                string path = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                if (path.Contains("/Containers/Data/Application/"))
                {
                    platformType = OMTPlatformType.iOS;
                } else if (Directory.Exists("/System/Applications/Utilities/Terminal.app"))
                {
                    platformType = OMTPlatformType.MacOS;
                }                    
            }
        }

        protected virtual string GetLibraryExtension()
        {
            return ".dll";
        }

        public virtual string GetMachineName()
        {
            return Environment.MachineName.ToUpperInvariant();
        }

        public virtual IntPtr OpenLibrary(string filename)
        {
            return IntPtr.Zero;
        }

        public virtual string GetStoragePath()
        {
            string sz = Environment.GetEnvironmentVariable("OMT_STORAGE_PATH");
            if (!String.IsNullOrEmpty(sz)) return sz;
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + Path.DirectorySeparatorChar + "OMT";
        }
        public static OMTPlatformType GetPlatformType()
        {
            return platformType;
        }
        public static OMTPlatform GetInstance()
        {
            lock (globalLock)
            {
                if (instance == null)
                {
                    switch (GetPlatformType())
                    {
                        case OMTPlatformType.Win32:
                            instance = new win32.Win32Platform();
                            break;
                        case OMTPlatformType.MacOS:
                        case OMTPlatformType.iOS:
                            instance = new mac.MacPlatform();
                            break;
                        case OMTPlatformType.Linux:
                            instance = new linux.LinuxPlatform();
                            break;
                        default:
                            instance = new OMTPlatform();
                            break;
                    }
                }
                return instance;
            }
        }
    }

}
