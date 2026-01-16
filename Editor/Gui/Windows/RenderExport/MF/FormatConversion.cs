using System.IO;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace T3.Editor.Gui.Windows.RenderExport.MF;

static class FormatConversion
{
    private static readonly byte[] _bytes = new byte[4];

    private static float Read4BytesToFloat(Stream imageStream)
    {
        _bytes[0] = (byte)imageStream.ReadByte();
        _bytes[1] = (byte)imageStream.ReadByte();
        _bytes[2] = (byte)imageStream.ReadByte();
        _bytes[3] = (byte)imageStream.ReadByte();
        var r = BitConverter.ToSingle(_bytes, 0);
        return r;
    }

    public static float Read2BytesToHalf(Stream imageStream)
    {
        var low = (byte)imageStream.ReadByte();
        var high = (byte)imageStream.ReadByte();
        return ToTwoByteFloat(low, high);
    }

    public static float ToTwoByteFloat(byte ho, byte lo)
    {
        var intVal = BitConverter.ToInt32(new byte[] { ho, lo, 0, 0 }, 0);

        var mant = intVal & 0x03ff;
        var exp = intVal & 0x7c00;
        if (exp == 0x7c00) exp = 0x3fc00;
        else if (exp != 0)
        {
            exp += 0x1c000;
            if (mant == 0 && exp > 0x1c400)
                return BitConverter.ToSingle(BitConverter.GetBytes((intVal & 0x8000) << 16 | exp << 13 | 0x3ff), 0);
        }
        else if (mant != 0)
        {
            exp = 0x1c400;
            do
            {
                mant <<= 1;
                exp -= 0x400;
            }
            while ((mant & 0x400) == 0);

            mant &= 0x3ff;
        }

        return BitConverter.ToSingle(BitConverter.GetBytes((intVal & 0x8000) << 16 | (exp | mant) << 13), 0);
    }

    private static byte[] I2B(int input)
    {
        var bytes1 = BitConverter.GetBytes(input);
        return new[] { bytes1[0], bytes1[1] };
    }

    public static byte[] ToInt(float twoByteFloat)
    {
        var fBits = BitConverter.ToInt32(BitConverter.GetBytes(twoByteFloat), 0);
        var sign = fBits >> 16 & 0x8000;
        var val = (fBits & 0x7fffffff) + 0x1000;
        switch (val)
        {
            case >= 0x47800000 when (fBits & 0x7fffffff) >= 0x47800000:
            {
                if (val < 0x7f800000) return I2B(sign | 0x7c00);
                return I2B(sign | 0x7c00 | (fBits & 0x007fffff) >> 13);
            }
            case >= 0x47800000:
                return I2B(sign | 0x7bff);
            case >= 0x38800000:
                return I2B(sign | val - 0x38000000 >> 13);
            case < 0x33000000:
                return I2B(sign);
            default:
                val = (fBits & 0x7fffffff) >> 23;
                return I2B(sign | ((fBits & 0x7fffff | 0x800000) + (0x800000 >> val - 102) >> 126 - val));
        }
    }

    /// <summary>
    /// get minimum image buffer size in bytes
    /// </summary>
    /// <param name="frame">texture to get information from</param>
    public static int SizeInBytes(ref Texture2D frame)
    {
        var currentDesc = frame.Description;
        var bitsPerPixel = Math.Max(currentDesc.Format.SizeOfInBits(), 1);
        return (currentDesc.Width * currentDesc.Height * bitsPerPixel + 7) / 8;
    }
}