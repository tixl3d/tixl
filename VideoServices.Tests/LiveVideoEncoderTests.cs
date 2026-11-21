using T3.Core.Video;
using Xunit;

namespace T3.VideoServices.Tests;

/// <summary>
/// Exercises the packet-sink H.264 path used by the collab preview: encode RGBA frames without a
/// muxer, decode the Annex-B access units back, and check the picture survives the round trip.
/// </summary>
public class LiveVideoEncoderTests
{
    [Fact]
    public void Encode_ProducesInBandHeadersAndAKeyFrame()
    {
        using var encoder = CreateEncoder(320, 240);
        var accessUnits = new List<byte[]>();
        encoder.AccessUnitEncoded += accessUnits.Add;

        encoder.Encode(SolidFrame(320, 240, 220, 40, 40), 320 * 4, forceKeyFrame: true);
        encoder.Encode(SolidFrame(320, 240, 40, 220, 40), 320 * 4, forceKeyFrame: false);

        Assert.True(accessUnits.Count > 0, $"encoder '{encoder.EncoderName}' produced no access units");

        // Every stream needs SPS (7) and PPS (8); without the global-header flag they arrive in-band.
        var first = accessUnits[0];
        Assert.Contains(7, NalTypes(first));
        Assert.Contains(8, NalTypes(first));
        Assert.Contains(5, NalTypes(first));
    }

    [Fact]
    public void Encode_DecodedFrameKeepsItsColors()
    {
        using var encoder = CreateEncoder(320, 240);
        var accessUnits = new List<byte[]>();
        encoder.AccessUnitEncoded += accessUnits.Add;

        var frame = SolidFrame(320, 240, 220, 40, 40);
        encoder.Encode(frame, 320 * 4, forceKeyFrame: true);
        encoder.Encode(frame, 320 * 4, forceKeyFrame: false);
        encoder.Encode(frame, 320 * 4, forceKeyFrame: false);
        Assert.True(accessUnits.Count > 0, $"encoder '{encoder.EncoderName}' produced no access units");

        using var decoder = new LiveH264Decoder();
        foreach (var accessUnit in accessUnits)
            decoder.TryDecode(accessUnit);
        decoder.Flush();

        Assert.True(decoder.Width > 0, "the decoder produced no frame");
        Assert.Equal(320, decoder.Width);
        Assert.Equal(240, decoder.Height);

        var rgba = decoder.Rgba;
        var center = ((240 / 2) * 320 + 320 / 2) * 4;
        Assert.True(rgba[center] > 150, $"red channel was {rgba[center]}");
        Assert.True(rgba[center + 1] < 100, $"green channel was {rgba[center + 1]}");
        Assert.True(rgba[center + 2] < 100, $"blue channel was {rgba[center + 2]}");
        Assert.Equal(255, rgba[center + 3]);
    }

    [Fact]
    public void EncodeScaled_ScalesASourceOfADifferentSize()
    {
        using var encoder = CreateEncoder(320, 240);
        var accessUnits = new List<byte[]>();
        encoder.AccessUnitEncoded += accessUnits.Add;

        var frame = SolidFrame(640, 480, 40, 40, 220);
        encoder.EncodeScaled(frame, 640 * 4, 640, 480, forceKeyFrame: true);
        encoder.EncodeScaled(frame, 640 * 4, 640, 480, forceKeyFrame: false);
        encoder.EncodeScaled(frame, 640 * 4, 640, 480, forceKeyFrame: false);
        Assert.True(accessUnits.Count > 0, $"encoder '{encoder.EncoderName}' produced no access units");

        using var decoder = new LiveH264Decoder();
        foreach (var accessUnit in accessUnits)
            decoder.TryDecode(accessUnit);
        decoder.Flush();

        Assert.Equal(320, decoder.Width);
        Assert.Equal(240, decoder.Height);

        var rgba = decoder.Rgba;
        var center = ((240 / 2) * 320 + 320 / 2) * 4;
        Assert.True(rgba[center + 2] > 150, $"blue channel was {rgba[center + 2]}");
    }

    private static LiveH264Encoder CreateEncoder(int width, int height) =>
        new(new VideoLiveEncoderSettings
                {
                    Width = width,
                    Height = height,
                    FrameRate = 30,
                    BitRate = 1_500_000,
                });

    private static byte[] SolidFrame(int width, int height, byte r, byte g, byte b)
    {
        var frame = new byte[width * height * 4];
        for (var i = 0; i < frame.Length; i += 4)
        {
            frame[i] = r;
            frame[i + 1] = g;
            frame[i + 2] = b;
            frame[i + 3] = 255;
        }

        return frame;
    }

    /// <summary>NAL unit types in an Annex-B buffer, for header/keyframe assertions.</summary>
    private static IEnumerable<int> NalTypes(byte[] annexB)
    {
        for (var i = 0; i + 4 < annexB.Length; i++)
        {
            if (annexB[i] != 0 || annexB[i + 1] != 0 || annexB[i + 2] != 0 || annexB[i + 3] != 1)
                continue;

            yield return annexB[i + 4] & 0x1F;
            i += 3;
        }
    }
}
