/*

Based on the MIT license video writing code at
https://github.com/jtpgames/Kinect-Recorder/blob/master/KinectRecorder/Multimedia/MediaFoundationVideoWriter.cs

*/

/*
The MIT License (MIT)

Copyright(c) 2016 Juri Tomak

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/

using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.MediaFoundation;

namespace T3.Editor.Gui.Windows.RenderExport.MF;

internal struct WaveFormatExtension
{
    public SharpDX.Multimedia.WaveFormatEncoding _wFormatTag;
    public ushort _nChannels;
    public uint _nSamplesPerSec;
    public uint _nAvgBytesPerSec;
    public ushort _nBlockAlign;
    public ushort _wBitsPerSample;
    public ushort _cbSize;

    public static WaveFormatExtension DefaultPcm
    {
        get
        {
            var waveFormatEx = new WaveFormatExtension
                                   {
                                       _wFormatTag = SharpDX.Multimedia.WaveFormatEncoding.Pcm,
                                       _nChannels = 2,
                                       _nSamplesPerSec = 48000,
                                       _wBitsPerSample = 24
                                   };
            waveFormatEx._nBlockAlign = (ushort)(waveFormatEx._nChannels * waveFormatEx._wBitsPerSample / 8);
            waveFormatEx._nAvgBytesPerSec = waveFormatEx._nSamplesPerSec * waveFormatEx._nBlockAlign;
            waveFormatEx._cbSize = 0;

            return waveFormatEx;
        }
    }

    public static WaveFormatExtension DefaultIeee
    {
        get
        {
            var waveFormatEx = new WaveFormatExtension
                                   {
                                       _wFormatTag = SharpDX.Multimedia.WaveFormatEncoding.IeeeFloat,
                                       _nChannels = 2,
                                       _nSamplesPerSec = 48000,
                                       _wBitsPerSample = 32
                                   };
            waveFormatEx._nBlockAlign = (ushort)(waveFormatEx._nChannels * waveFormatEx._wBitsPerSample / 8);
            waveFormatEx._nAvgBytesPerSec = waveFormatEx._nSamplesPerSec * waveFormatEx._nBlockAlign;
            waveFormatEx._cbSize = 0;

            return waveFormatEx;
        }
    }

    public SharpDX.Multimedia.WaveFormat ToSharpDx()
    {
        return SharpDX.Multimedia.WaveFormat.CreateCustomFormat(
                                                                _wFormatTag,
                                                                (int)_nSamplesPerSec,
                                                                _nChannels,
                                                                (int)_nAvgBytesPerSec,
                                                                _nBlockAlign,
                                                                _wBitsPerSample
                                                               );
    }
}

internal class MediaFoundationAudioWriter
{
    /// <summary>
    /// Gets all the available media types for a particular 
    /// </summary>
    /// <param name="audioSubtype">Audio subtype - a value from the AudioSubtypes class</param>
    /// <returns>An array of available media types that can be encoded with this subtype</returns>
    private static IEnumerable<MediaType> GetOutputMediaTypes(Guid audioSubtype)
    {
        Collection availableTypes;
        try
        {
            availableTypes = MediaFactory.TranscodeGetAudioOutputAvailableTypes(audioSubtype, TransformEnumFlag.All, null);
        }
        catch (SharpDXException c)
        {
            if (c.ResultCode.Code == ResultCode.NotFound.Code)
            {
                // Don't worry if we didn't find any - just means no encoder available for this type
                //return new MediaType[0];
                return Array.Empty<MediaType>();
            }
            throw;
        }

        var count = availableTypes.ElementCount;
        var mediaTypes = new List<MediaType>(count);
        for (var n = 0; n < count; n++)
        {
            ComObject mediaTypeObject = (ComObject)availableTypes.GetElement(n);
            mediaTypes.Add(new MediaType(mediaTypeObject.NativePointer));
        }
        availableTypes.Dispose();
        return mediaTypes.ToArray();
    }

    /// <summary>
    /// Queries the available bitrates for a given encoding output type, sample rate and number of channels
    /// </summary>
    /// <param name="audioSubtype">Audio subtype - a value from the AudioSubtypes class</param>
    /// <param name="sampleRate">The sample rate of the PCM to encode</param>
    /// <param name="channels">The number of channels of the PCM to encode</param>
    /// <returns>An array of available bitrates in average bits per second</returns>
    public static int[] GetEncodeBitrates(Guid audioSubtype, int sampleRate, int channels)
    {
        return GetOutputMediaTypes(audioSubtype)
              .Where(mt => mt.Get(MediaTypeAttributeKeys.AudioSamplesPerSecond) == sampleRate &&
                           mt.Get(MediaTypeAttributeKeys.AudioNumChannels) == channels)
              .Select(mt => mt.Get(MediaTypeAttributeKeys.AudioAvgBytesPerSecond) * 8)
              .Distinct()
              .OrderBy(br => br)
              .ToArray();
    }

    /// <summary>
    /// Tries to find the encoding media type with the closest bitrate to that specified
    /// </summary>
    /// <param name="audioSubtype">Audio subtype, a value from AudioSubtypes</param>
    /// <param name="inputFormat">Your encoder input format (used to check sample rate and channel count)</param>
    /// <param name="desiredBitRate">Your desired bitrate</param>
    /// <returns>The closest media type, or null if none available</returns>
    private static MediaType SelectMediaType(Guid audioSubtype, SharpDX.Multimedia.WaveFormat inputFormat, int desiredBitRate)
    {
        return GetOutputMediaTypes(audioSubtype)
            .Where(mt => mt.Get(MediaTypeAttributeKeys.AudioSamplesPerSecond) == inputFormat.SampleRate &&
                mt.Get(MediaTypeAttributeKeys.AudioNumChannels) == inputFormat.Channels)
            .Select(mt => new { MediaType = mt, Delta = Math.Abs(desiredBitRate - mt.Get(MediaTypeAttributeKeys.AudioAvgBytesPerSecond) * 8) })
            .OrderBy(mt => mt.Delta)
            .Select(mt => mt.MediaType)
            .FirstOrDefault();
    }

    private readonly int _streamIndex;
    public int StreamIndex => _streamIndex;

    public virtual Guid AudioFormat { get; }

    protected MediaFoundationAudioWriter(SinkWriter sinkWriter, ref WaveFormatExtension waveFormat, int desiredBitRate = 192000)
    {
        var sharpWf = waveFormat.ToSharpDx();

        // Information on configuring an AAC media type can be found here:
        // http://msdn.microsoft.com/en-gb/library/windows/desktop/dd742785%28v=vs.85%29.aspx
        var outputMediaType = SelectMediaType(AudioFormat, sharpWf, desiredBitRate);
        if (outputMediaType == null) throw new InvalidOperationException("No suitable encoders available");

        var inputMediaType = new MediaType();
        var size = 18 + sharpWf.ExtraSize;

        sinkWriter.AddStream(outputMediaType, out _streamIndex);

        MediaFactory.InitMediaTypeFromWaveFormatEx(inputMediaType, new[] { sharpWf }, size);
        sinkWriter.SetInputMediaType(_streamIndex, inputMediaType, null);
    }

    public Sample CreateSampleFromFrame(ref byte[] data)
    {
        var mediaBuffer = MediaFactory.CreateMemoryBuffer(data.Length);

        // Write all contents to the MediaBuffer for media foundation
        int cbMaxLength = 0;
        int cbCurrentLength = 0;
        IntPtr mediaBufferPointer = mediaBuffer.Lock(out cbMaxLength, out cbCurrentLength);
        //var mediaBufferPointer = mediaBuffer.Lock(out _, out _);
        try
        {
            Marshal.Copy(data, 0, mediaBufferPointer, data.Length);
        }
        finally
        {
            mediaBuffer.Unlock();
            mediaBuffer.CurrentLength = data.Length;
        }

        // Create the sample (includes image and timing information)
        var sample = MediaFactory.CreateSample();
        sample.AddBuffer(mediaBuffer);

        return sample;
    }
}

internal sealed class Mp3AudioWriter : MediaFoundationAudioWriter
{
    public Mp3AudioWriter(SinkWriter sinkWriter, ref WaveFormatExtension waveFormat, int desiredBitRate = 192000)
        : base(sinkWriter, ref waveFormat, desiredBitRate)
    {
    }

    public override Guid AudioFormat => AudioFormatGuids.Mp3;
}

internal sealed class FlacAudioWriter : MediaFoundationAudioWriter
{
    public FlacAudioWriter(SinkWriter sinkWriter, ref WaveFormatExtension waveFormat, int desiredBitRate = 192000)
        : base(sinkWriter, ref waveFormat, desiredBitRate)
    {
    }

    public override Guid AudioFormat => AudioFormatGuids.Flac;
}

internal sealed class AacAudioWriter : MediaFoundationAudioWriter
{
    public AacAudioWriter(SinkWriter sinkWriter, ref WaveFormatExtension waveFormat, int desiredBitRate = 192000)
        : base(sinkWriter, ref waveFormat, desiredBitRate)
    {
    }

    public override Guid AudioFormat => AudioFormatGuids.Aac;
}

