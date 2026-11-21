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
using System.Runtime.InteropServices;
using System.Xml;
using System.IO;

namespace libomtnet
{
    [Flags]
    public enum OMTFrameType
    {
        None = 0,
        Metadata = 1,
        Video = 2,
        Audio = 4
    }

    /// <summary>
    /// Flags set on video frames:
    /// 
    /// Interlaced: Frames are interlaced
    /// 
    /// Alpha: Frames contain an alpha channel. If this is not set, BGRA will be encoded as BGRX and UYVA will be encoded as UYVY.
    /// 
    /// PreMultiplied: When combined with Alpha, alpha channel is premultiplied, otherwise straight
    /// 
    /// Preview: Frame is a special 1/8th preview frame
    /// 
    /// HighBitDepth: Sender automatically adds this flag for frames encoded using P216 or PA16 pixel formats.
    /// 
    /// Set this manually for VMX1 compressed data where the the frame was originally encoded using P216 or PA16.
    /// This determines which pixel format is selected on the decode side.
    /// 
    /// </summary>
    [Flags]
    public enum OMTVideoFlags
    {
        None = 0,
        Interlaced = 1,
        Alpha = 2,
        PreMultiplied = 4,
        Preview = 8,
        HighBitDepth = 16
    }

    /// <summary>
    /// Supported Codecs:
    /// 
    /// VMX1 = Fast video codec
    /// 
    /// UYVY = 8-bit YUV format
    /// 
    /// YUY2 = 8-bit YUV format with YUYV pixel order
    /// 
    /// UYVA = 8-bit YUV format immediately followed by an alpha plane
    /// 
    /// NV12 = Planar 4:2:0 YUV format. Y plane followed by interleaved half height U/V plane.
    /// 
    /// YV12 = Planar 4:2:0 YUV format. Y plane followed by half height U and V planes.
    /// 
    /// BGRA = 32bpp RGBA format (Same as ARGB32 on Win32)
    /// 
    /// P216 = Planar 4:2:2 YUV format. 16bit Y plane followed by interlaved 16bit UV plane.
    /// 
    /// PA16 = Same as P216 followed by an additional 16bit alpha plane.
    /// 
    /// FPA1 = Floating-point Planar Audio 32bit
    /// 
    /// </summary>
    public enum OMTCodec
    {
        VMX1 = 0x31584D56,
        FPA1 = 0x31415046, //Planar audio
        UYVY = 0x59565955,
        YUY2 = 0x32595559,
        BGRA = 0x41524742,
        NV12 = 0x3231564E,
        YV12 = 0x32315659,
        UYVA = 0x41565955,
        P216 = 0x36313250,
        PA16 = 0x36314150
    }

    public enum OMTPlatformType
    {
        Unknown = 0,
        Win32 = 1,
        MacOS = 2,
        Linux = 3,
        iOS = 4
    }

    /// <summary>
    /// Specify the color space of the uncompressed Frame. This is used to determine the color space for YUV<>RGB conversions internally.
    /// 
    /// If undefined, the codec will assume BT601 for heights < 720, BT709 for everything else.
    /// 
    /// </summary>
    public enum OMTColorSpace
    {
        Undefined = 0,
        BT601 = 601,
        BT709 = 709
    }

    /// <summary>
    /// Specify the preferred uncompressed video format of decoded frames.
    /// 
    /// UYVY is always the fastest, if no alpha channel is required.
    /// 
    /// UYVYorBGRA will provide BGRA only when alpha channel is present.
    /// 
    /// BGRA will always convert back to BGRA
    /// 
    /// UYVYorUYVA will provide UYVA only when alpha channel is present.
    /// 
    /// UYVYorUYVAorP216orPA16 will provide P216 if sender encoded with high bit depth, or PA16 if sender encoded with high bit depth and alpha. Otherwise same as UYVYorUYVA.
    /// 
    /// P216 To receive only P216 frames
    /// 
    /// </summary>
    public enum OMTPreferredVideoFormat
    {
        UYVY = 0,
        UYVYorBGRA = 1,
        BGRA = 2,
        UYVYorUYVA = 3,
        UYVYorUYVAorP216orPA16 = 4,
        P216 = 5
    }

    /// <summary>
    /// Flags to enable certain features on a Receiver:
    /// 
    /// Preview: Receive only a 1/8th preview of the video.
    /// 
    /// IncludeCompressed: Include a copy of the compressed VMX video frames for further processing or recording.
    /// 
    /// CompressedOnly: Include only the compressed VMX video frame without decoding. In this instance DataLength will always be 0.
    /// 
    /// </summary>
    [Flags]
    public enum OMTReceiveFlags
    {
        None = 0,
        Preview = 1,
        IncludeCompressed = 2,
        CompressedOnly = 4
    }

    /// <summary>
    /// Specify the video encoding quality.
    /// 
    /// If set to Default, the Sender is configured to allow suggestions from all Receivers.
    /// 
    /// The highest suggestion amongst all receivers is then selected.
    /// 
    /// If a Receiver is set to Default, then it will defer the quality to whatever is set amongst other Receivers.
    /// 
    /// </summary>
    public enum OMTQuality
    {
        Default = 0,
        Low = 1,
        Medium = 50,
        High = 100
    }

    public struct OMTStatistics
    {
        public long BytesSent;
        public long BytesReceived;
        public long BytesSentSinceLast;
        public long BytesReceivedSinceLast;

        public long Frames;
        public long FramesSinceLast;
        public long FramesDropped;

        public long CodecTime;
        public long CodecTimeSinceLast;

        public void ToIntPtr(IntPtr ptr)
        {
            if (ptr != IntPtr.Zero)
            {
                Marshal.WriteInt64(ptr, BytesSent);
                Marshal.WriteInt64(ptr + 8, BytesReceived);
                Marshal.WriteInt64(ptr + 16, BytesSentSinceLast);
                Marshal.WriteInt64(ptr + 24, BytesReceivedSinceLast);
                Marshal.WriteInt64(ptr + 32, Frames);
                Marshal.WriteInt64(ptr + 40, FramesSinceLast);
                Marshal.WriteInt64(ptr + 48, FramesDropped);
                Marshal.WriteInt64(ptr + 56, CodecTime);
                Marshal.WriteInt64(ptr + 64, CodecTimeSinceLast);
            }
        }
    }

    public class OMTSenderInfo
    {
        public string ProductName;
        public string Manufacturer;
        public string Version;

        public OMTSenderInfo() { }
        public OMTSenderInfo(string productName, string manufacturer, string version)
        {
            ProductName = productName;
            Manufacturer = manufacturer;
            Version = version;
        }

        public string ToXML()
        {
            StringWriter sw = new StringWriter();
            XmlTextWriter t = new XmlTextWriter(sw);
            t.Formatting = Formatting.Indented;
            t.WriteStartElement(OMTMetadataTemplates.SENDER_INFO_NAME);
            t.WriteAttributeString("ProductName", ProductName);
            t.WriteAttributeString("Manufacturer", Manufacturer);
            t.WriteAttributeString("Version", Version);
            t.WriteEndElement();
            t.Close();
            return sw.ToString();
        }
        public static OMTSenderInfo FromXML(string xml)
        {
            XmlDocument doc = OMTMetadataUtils.TryParse(xml);
            if (doc != null)
            {
                XmlNode e = doc.DocumentElement;
                if (e != null)
                {
                    OMTSenderInfo senderInfo = new OMTSenderInfo();
                    XmlNode a = e.Attributes.GetNamedItem("ProductName");
                    if (a != null) senderInfo.ProductName = a.InnerText;
                    a = e.Attributes.GetNamedItem("Manufacturer");
                    if (a != null) senderInfo.Manufacturer = a.InnerText;
                    a = e.Attributes.GetNamedItem("Version");
                    if (a != null) senderInfo.Version = a.InnerText;
                    return senderInfo;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Stores one frame of Video, Audio or Metadata
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OMTMediaFrame
    {

        /// <summary>
        /// Specify the type of frame. This determines which values of this struct are valid/used.
        /// </summary>
        public OMTFrameType Type;

        /// <summary>
        /// This is a timestamp where 1 second = 10,000,000
        /// 
        /// This should not be left 0 unless this is the very first frame.
        /// 
        /// This should represent the accurate time the frame or audio sample was generated at the original source and be used on the receiving end to synchronize
        /// and record to file as a presentation timestamp (pts).
        /// 
        /// A special value of -1 can be specified to tell the Sender to generate timestamps and throttle as required to maintain
        /// the specified FrameRate or SampleRate of the frame. 
        /// 
        /// </summary>
        public long Timestamp;

        /// <summary>
        /// Sending:
        /// 
        ///     Video: 'UYVY', 'YUY2', 'NV12', 'YV12, 'BGRA', 'UYVA', 'VMX1' are supported (BGRA will be treated as BGRX and UYVA as UYVY where alpha flags are not set)
        ///     
        ///     Audio: Only 'FPA1' is supported (32bit floating point planar audio)
        ///     
        /// Receiving:
        /// 
        ///     Video: Only 'UYVY', 'UYVA', 'BGRA' and 'BGRX' are supported
        ///     
        ///     Audio: Only 'FPA1' is supported (32bit floating point planar audio)
        ///     
        /// </summary>
        public int Codec;

        //Video Properties
        public int Width;
        public int Height;

        /// <summary>
        /// Stride in bytes of each row of pixels. Typically width*2 for UYVY, width*4 for BGRA and just width for planar formats.
        /// </summary>
        public int Stride;

        public OMTVideoFlags Flags;

        /// <summary>
        /// Frame Rate Numerator/Denominator in Frames Per Second, for example Numerator 60 and Denominator 1 is 60 frames per second.
        /// </summary>
        public int FrameRateN;
        public int FrameRateD;

        /// <summary>
        /// Display aspect ratio expressed as a ratio of width/height. For example 1.777777777777778 for 16/9
        /// </summary>
        public float AspectRatio;

        /// <summary>
        /// Color space of the frame. If undefined a height < 720 is BT601 and everything else BT709
        /// </summary>
        public OMTColorSpace ColorSpace;

        //Audio Properties
        // Sample rate, i.e 48000, 44100 etc
        public int SampleRate;
        // Audio Channels. A maximum of 32 channels are supported.
        public int Channels;
        // Number of 32bit floating point samples per channel/plane. Each plane should contain SamplesPerChannel*4 bytes.
        public int SamplesPerChannel;

        //Data Properties

        /// <summary>
        /// Video: Uncompressed pixel data  (or compressed VMX1 data when sending and Codec set to VMX1)
        /// 
        /// Audio: Planar 32bit floating point audio
        /// 
        /// Metadata: UTF-8 encoded XML string with terminating null character
        /// 
        /// </summary>
        public IntPtr Data;

        /// <summary>
        /// Video: Number of bytes total including stride
        /// 
        /// Audio: Number of bytes (SamplesPerChannel * Channels * 4)
        /// 
        /// Metadata: Number of bytes in UTF-8 encoded string + 1 for terminating null character. 
        /// 
        /// </summary>
        public int DataLength;

        /// <summary>
        /// Receive only. Use standard Data/DataLength if sending VMX1 frames with a Sender
        /// 
        /// If IncludeCompressed or CompressedOnly OMTReceiveFlags is set, this will include the original compressed video frame in VMX1 format.
        /// 
        /// This could then be muxed into an AVI or MOV file using FFmpeg or similar APIs
        /// 
        /// </summary>
        public IntPtr CompressedData;
        public int CompressedLength;

        /// <summary>
        /// Per frame metadata as UTF-8 encoded string + 1 for null character. Up to 65536 bytes supported.
        /// </summary>
        public IntPtr FrameMetadata;

        /// <summary>
        /// Length in bytes of per frame metadata including null character
        /// </summary>
        public int FrameMetadataLength;

        public static IntPtr ToIntPtr(OMTMediaFrame frame)
        {
            IntPtr dst = Marshal.AllocHGlobal(Marshal.SizeOf(frame));
            Marshal.StructureToPtr(frame, dst, false);
            return dst;
        }
        public static void FreeIntPtr(IntPtr ptr)
        {
            if (ptr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        public static OMTMediaFrame FromIntPtr(IntPtr ptr)
        {
           return (OMTMediaFrame)Marshal.PtrToStructure(ptr, typeof(OMTMediaFrame));
        }

        public float FrameRate { get {
                return OMTUtils.ToFrameRate(FrameRateN, FrameRateD);
            } 
            set
            {
                OMTUtils.FromFrameRate(value,ref FrameRateN,ref FrameRateD);
            }
        }

    }

    public struct OMTSize
    {
        public int Width;
        public int Height;
        public OMTSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Tally where 0 = 0 off, 1 = on.
    /// </summary>
    public struct OMTTally
    {
        public int Preview;
        public int Program;
        public OMTTally(int preview, int program)
        {
            this.Preview = preview;
            this.Program = program;
        }

        public override string ToString()
        {
            return "Preview: " + Preview + " Program: " + Program;
        }
    }

}
