using System;
using System.Runtime.InteropServices;

namespace T3.Core.DataTypes;

[Serializable]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LaserPoint : ICloneable
{
    public int X;
    public int Y;
    public int R;
    public int G;
    public int B;
    public int I;

    public static readonly LaserPoint Zero = new()
    {
        X = 0,
        Y = 0,
        R = 0,
        G = 0,
        B = 0,
        I = 0
    };

    public LaserPoint(int x, int y, int r, int g, int b, int i = 65535)
    {
        X = x;
        Y = y;
        R = r;
        G = g;
        B = b;
        I = i;
    }

    public static LaserPoint CreateBlanked(int x, int y)
    {
        return new LaserPoint(x, y, 0, 0, 0, 0);
    }

    public object Clone()
    {
        return new LaserPoint(X, Y, R, G, B, I);
    }
}
