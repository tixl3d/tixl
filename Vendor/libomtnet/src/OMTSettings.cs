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
using System.Xml;
using System.IO;

namespace libomtnet
{
    /// <summary>
    /// These functions override the default settings which are stored in ~/.OMT/settings.xml on Mac and Linux and C:\ProgramData\OMT\settings.xml on WIndows
    /// 
    /// To override the default folder used for for settings, set the OMT_STORAGE_PATH environment variable prior to calling any OMT functions.
    /// 
    /// The following settings are currently supported:
    /// 
    /// DiscoveryServer [string] specify a URL in the format omt://hostname:port to connect to for discovery. If left blank, default DNS-SD discovery behavior is enabled.
    /// 
    /// NetworkPortStart[integer] specify the first port to create Send instances on.Defaults to 6400
    /// 
    /// NetworkPortEnd[integer] specify the last port to create Send instances on.Defaults to 6600
    /// 
    /// </summary>
    public class OMTSettings
    {
        private static object globalLock = new object();
        private object instanceLock = new object();
        private string filename;
        private XmlDocument document;
        private XmlNode rootNode;
        private static OMTSettings instance;
        public static OMTSettings GetInstance()
        {
            lock (globalLock)
            {
                if (instance == null)
                {
                    string sz = OMTPlatform.GetInstance().GetStoragePath() + Path.DirectorySeparatorChar + "settings.xml";
                    instance = new OMTSettings(sz);
                }
                return instance;
            }
        }
        public OMTSettings(string filename)
        {
            this.filename = filename;
            lock (globalLock)
            {
                document = new XmlDocument();
                try
                {
                    if (File.Exists(filename))
                    {
                        document.Load(filename);
                        rootNode = document.DocumentElement;
                    }
                }
                catch (Exception ex)
                {
                    OMTLogging.Write(ex.ToString(), "OMTSettings.New");
                }
                if (rootNode == null)
                {
                    rootNode = document.CreateElement("Settings");
                    document.AppendChild(rootNode);
                }
            }
        }
        public void Save()
        {
            lock (globalLock)
            {
                using (XmlTextWriter writer = new XmlTextWriter(filename, null))
                {
                    writer.Formatting = Formatting.Indented;
                    document.Save(writer);
                }
            }
        }
        public string GetString(string key, string defaultValue)
        {
            lock (instanceLock)
            {
                if (rootNode != null)
                {
                    XmlNode node = rootNode.SelectSingleNode(key);
                    if (node != null)
                    {
                        return node.InnerText;
                    }
                }
                return defaultValue;
            }
        }
        public void SetString(string key, string value)
        {
            lock (instanceLock)
            {
                if (rootNode != null)
                {
                    XmlNode node = rootNode.SelectSingleNode(key);
                    if (node == null)
                    {
                        node = document.CreateElement(key);
                        rootNode.AppendChild(node);
                    }
                    node.InnerText = value;
                }
            }
        }

        public int GetInteger(string key, int defaultValue)
        {
            string value = GetString(key, null);
            if (!string.IsNullOrEmpty(value))
            {
                int v = 0;
                if (int.TryParse(value, out v))
                {
                    return v;
                }
            }
            return defaultValue;
        }
        public void SetInteger(string key, int value)
        {
            SetString(key, value.ToString());
        }
    }
}
