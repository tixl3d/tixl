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
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace libomtnet.win32
{
    internal class Win32Platform : OMTPlatform
    {
        enum COMPUTER_NAME_FORMAT
        {
            ComputerNameNetBIOS,
            ComputerNameDnsHostname,
            ComputerNameDnsDomain,
            ComputerNameDnsFullyQualified,
            ComputerNamePhysicalNetBIOS,
            ComputerNamePhysicalDnsHostname,
            ComputerNamePhysicalDnsDomain,
            ComputerNamePhysicalDnsFullyQualified
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr LoadLibrary(string filename);

        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)]
        private static extern bool GetComputerNameEx(COMPUTER_NAME_FORMAT NameType, [Out()] StringBuilder lpBuffer, ref int nSize);

        public override string GetStoragePath()
        {
           return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + Path.DirectorySeparatorChar + "OMT";
        }
        public override string GetMachineName()
        {
            int len = 4096;
            StringBuilder sb = new StringBuilder(len);
            if (GetComputerNameEx(COMPUTER_NAME_FORMAT.ComputerNamePhysicalDnsHostname,sb,ref len))
            {
                return sb.ToString().ToUpperInvariant();
            }
            OMTLogging.Write("Unable to retrieve full hostname", "Win32Platform");
            return base.GetMachineName();
        }
        public override IntPtr OpenLibrary(string filename)
        {
            return LoadLibrary(filename);
        }

    }
}
