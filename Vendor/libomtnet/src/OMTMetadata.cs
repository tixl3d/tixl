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
using System.Xml;
namespace libomtnet
{
    /// <summary>
    /// Fixed static XML commands for protocol use. 
    /// Receivers will check for these exact string matches and won't bother to parse the XML.
    /// This means any changes to these, even slightly will result in the commands being ignored entirely.
    /// </summary>
    internal class OMTMetadataConstants
    {
        public const string CHANNEL_SUBSCRIBE_VIDEO = @"<OMTSubscribe Video=""true"" />";
        public const string CHANNEL_SUBSCRIBE_AUDIO = @"<OMTSubscribe Audio=""true"" />";
        public const string CHANNEL_SUBSCRIBE_METADATA = @"<OMTSubscribe Metadata=""true"" />";
        public const string CHANNEL_PREVIEW_VIDEO_ON = @"<OMTSettings Preview=""true"" />";
        public const string CHANNEL_PREVIEW_VIDEO_OFF = @"<OMTSettings Preview=""false"" />";

        //NB: There is a double == typo in the following that must be kept for compatibility reasons.
        public const string TALLY_PREVIEW = @"<OMTTally Preview=""true"" Program==""false"" />";
        public const string TALLY_PROGRAM = @"<OMTTally Preview=""false"" Program==""true"" />";
        public const string TALLY_PREVIEWPROGRAM = @"<OMTTally Preview=""true"" Program==""true"" />";
        public const string TALLY_NONE = @"<OMTTally Preview=""false"" Program==""false"" />";
    }
    internal class OMTMetadataTemplates
    {
        public const string SUGGESTED_QUALITY_PREFIX = @"<OMTSettings Quality=";
        public const string SUGGESTED_QUALITY = @"<OMTSettings Quality=""Default"" />";
        public const string SENDER_INFO_NAME = @"OMTInfo";
        public const string SENDER_INFO_PREFIX = @"<OMTInfo";
        public const string ADDRESS_NAME = @"OMTAddress";
        public const string REDIRECT_NAME = @"OMTRedirect";
        public const string REDIRECT_PREFIX = @"<OMTRedirect";
    }

    internal class OMTMetadataUtils
    {
        public static XmlDocument TryParse(string xml)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);
                return doc;
            }
            catch (Exception)
            {
                OMTLogging.Write("Invalid XML: " + xml, "OMTMetadata");
                return null;
            }
        }
    }
    internal class OMTMetadata : OMTFrameBase
    {
        public long timestamp;
        public string XML;
        public IPEndPoint Endpoint;
        public OMTMetadata(long timestamp, string xML)
        {
            Timestamp = timestamp;
            XML = xML;
        }
        public OMTMetadata(long timestamp, string xML, IPEndPoint endpoint)
        {
            Timestamp = timestamp;
            XML = xML;
            Endpoint = endpoint;
        }

        public override long Timestamp
        { get { return timestamp; } set { timestamp = value; } }

        public override OMTFrameType FrameType
        { get { return OMTFrameType.Metadata; } }

        public IntPtr ToIntPtr(ref int length)
        {
            return OMTUtils.XMLToIntPtr(XML, ref length);
        }

        public static OMTMetadata FromTally(OMTTally tally)
        {
            if (tally.Preview == 0 && tally.Program == 0)
            {
                return new OMTMetadata(0, OMTMetadataConstants.TALLY_NONE);
            }
            else if (tally.Preview == 1 && tally.Program == 0)
            {
                return new OMTMetadata(0, OMTMetadataConstants.TALLY_PREVIEW);
            }
            else if (tally.Program == 1 && tally.Preview == 0)
            {
                return new OMTMetadata(0, OMTMetadataConstants.TALLY_PROGRAM);
            }
            else
            {
                return new OMTMetadata(0, OMTMetadataConstants.TALLY_PREVIEWPROGRAM);
            }
         }

        public static OMTMetadata FromMediaFrame(OMTMediaFrame metadata)
        {
            if (metadata.Data != IntPtr.Zero && metadata.DataLength > 0)
            {
                string xml = OMTUtils.IntPtrToXML(metadata.Data, metadata.DataLength);
                return new OMTMetadata(metadata.Timestamp, xml);
            }
            return null;
        }

        public static void FreeIntPtr(IntPtr ptr)
        {
            OMTUtils.FreeXMLIntPtr(ptr);
        }

    }
}
