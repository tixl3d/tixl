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
using System.Net;
using System.Xml;
using System.IO;

namespace libomtnet
{
    public class OMTAddress
    {
        private string name;
        private readonly string machineName;
        private int port;
        private IPAddress[] addresses = { };
        private const int MAX_FULLNAME_LENGTH = 63;
        internal bool removed = false;

        public OMTAddress(string name, int port)
        {
            this.name = SanitizeName(name);
            this.port = port;
            this.machineName = SanitizeName(OMTPlatform.GetInstance().GetMachineName());
            this.addresses = new IPAddress[] { };
            LimitNameLength();
        }
        public OMTAddress(string machineName, string name, int port)
        {
            this.name = SanitizeName(name);
            this.port = port;
            this.machineName = SanitizeName(machineName);
            this.addresses = new IPAddress[] { };
            LimitNameLength();
        }

        public string ToURL()
        {
            return OMTConstants.URL_PREFIX + this.machineName + ":" + port; 
        }

        private void LimitNameLength()
        {
            int oversize = ToString().Length - MAX_FULLNAME_LENGTH;
            if (oversize > 0)  
            {
                if (oversize < this.name.Length)
                {
                    this.name = this.name.Substring(0, this.name.Length - oversize).Trim();
                }
            }
        }

        public void ClearAddresses()
        {
            addresses = new IPAddress[]{ };
        }

        public bool AddAddress(IPAddress address)
        {
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                byte[] b = address.GetAddressBytes();
                byte[] b128 = new byte[16];

                b128[10] = 0xFF;
                b128[11] = 0xFF;

                b128[12] = b[0];
                b128[13] = b[1];
                b128[14] = b[2];
                b128[15] = b[3];

                address = new IPAddress(b128);
            } else if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal) return false;
            }
            if (!HasAddress(address))
            {
                List<IPAddress> list = new List<IPAddress>();
                bool v4 = OMTUtils.IsIPv4(address);
                foreach (IPAddress a in this.addresses)
                {
                    if (OMTUtils.IsIPv4(a))
                    {
                        list.Add(a);
                    }
                }
                if (v4) list.Add(address);
                foreach (IPAddress a in this.addresses)
                {
                    if (!OMTUtils.IsIPv4(a))
                    {
                        list.Add(a);
                    }
                }
                if (!v4) list.Add(address);
                addresses = list.ToArray();
                return true;
            }
            return false;
        }
        internal bool HasAddress(IPAddress address)
        {
            foreach (IPAddress a in addresses)
            {
                if (a.Equals(address))
                {
                    return true;
                }
            }
            return false;
        }

        public static string EscapeFullName(string fullName)
        {
            return fullName.Replace("\\", "\\\\").Replace(".", "\\.");
        }

        public static string SanitizeName(string name)
        {
            return name; // return name.Replace(".", " ");
        }

        public static string UnescapeFullName(string fullName)
        {
            StringBuilder sb = new StringBuilder();
            bool beginEscape = false;
            string num = "";
            foreach (char c in fullName.ToCharArray())
            {
                if (beginEscape)
                {
                    if (Char.IsDigit(c))
                    {
                        num = num + c.ToString();
                        if (num.Length == 3)
                        {
                            int n = 0;
                            if (int.TryParse(num, out n))
                            {
                                sb.Append(Convert.ToChar(n));
                            }
                            beginEscape = false;
                        }
                    } else
                    {
                        sb.Append(c);
                        beginEscape = false;
                    }
                } else
                {
                    if (c == '\\')
                    {
                        beginEscape = true;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }
            return sb.ToString();
        }


        public string MachineName { get { return machineName; } }
        public string Name { get { return name; } }
        public IPAddress[] Addresses { get { return addresses; } }
        public int Port { get { return port; } set { port = value; } }

        public override string ToString()
        {
            return ToString(machineName, name);
        }

        public static string ToString(string machineName, string name)
        {
            return machineName + " (" + name + ")";
        }

        public static bool IsValid(string fullName)
        {
            if (!string.IsNullOrEmpty(fullName))
            {
                if (fullName.Contains("("))
                {
                    if (fullName.Contains(")"))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        public static OMTAddress Create(string fullName, int port)
        {
            if (!IsValid(fullName)) return null;
            int index = fullName.IndexOf('(');
            string machineName = fullName.Substring(0, index).Trim();
            if (index > 0)
            {
               string name = fullName.Substring(index + 1);
               name = name.Substring(0, name.Length - 1);
               return new OMTAddress(machineName, name, port);
            }
            return null;
        }

        public static string GetMachineName(string fullName)
        {
            string[] s = fullName.Split('(');
            return s[0].Trim();
        }
        public static string GetName(string fullName)
        {
            int index = fullName.IndexOf('(');
            if (index > 0)
            {
                string name = fullName.Substring(index + 1);
                name = name.Substring(0, name.Length - 1);
                return name;
            }
            return "";
        }

        public string ToXML()
        {
            using (StringWriter sw = new StringWriter())
            {
                using (XmlTextWriter t = new XmlTextWriter(sw))
                {
                    t.Formatting = Formatting.Indented;
                    t.WriteStartElement(OMTMetadataTemplates.ADDRESS_NAME);
                    t.WriteElementString("Name", ToString());
                    t.WriteElementString("Port", port.ToString());
                    if (removed)
                    {
                        t.WriteElementString("Removed", "True");
                    }
                    t.WriteStartElement("Addresses");
                    foreach (IPAddress ip in addresses)
                    {
                        t.WriteElementString("IPAddress", ip.ToString());
                    }
                    t.WriteEndElement();
                    t.WriteEndElement();
                    return sw.ToString();
                }
            }

        }

        public static OMTAddress FromXML(string xml)
        {
            XmlDocument doc = OMTMetadataUtils.TryParse(xml);
            if (doc != null)
            {
                XmlNode e = doc.DocumentElement;
                if (e != null)
                {
                    if (e.Name == OMTMetadataTemplates.ADDRESS_NAME)
                    {
                        XmlNode nm = e.SelectSingleNode("Name");
                        if (nm != null)
                        {
                            XmlNode prt = e.SelectSingleNode("Port");
                            if (prt != null)
                            {
                                int port = int.Parse(prt.InnerText);
                                OMTAddress a = OMTAddress.Create(nm.InnerText, port);
                                if (a != null)
                                {
                                    foreach (XmlNode ipn in e.SelectNodes("Addresses/IPAddress"))
                                    {
                                        IPAddress ip = null;
                                        if (IPAddress.TryParse(ipn.InnerText, out ip))
                                        {
                                            a.AddAddress(ip);
                                        }
                                    }
                                    XmlNode del = e.SelectSingleNode("Removed");
                                    if (del != null)
                                    {
                                        if (del.InnerText.ToLowerInvariant() == "true")
                                        {
                                            a.removed = true;
                                        }
                                    }
                                    return a;
                                }
                            }
                        }
                    }
                }
            }
            return null;
        }

    }

    internal class OMTAddressSorter : IComparer<OMTAddress>
    {
        public int Compare(OMTAddress x, OMTAddress y)
        {
            if (x != null && y != null)
            {
                return String.Compare(x.ToString(), y.ToString());
            }
            return 0;
        }
    }

}
