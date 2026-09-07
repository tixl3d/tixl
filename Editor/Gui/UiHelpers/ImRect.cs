using T3.Core.Utils;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// 2D axis aligned bounding-box. It's a port of IMGUIs internal class.
/// FIXME: this should be replaced with a .net Rect-Class
/// </summary>
public struct ImRect
{
    public Vector2 Min; // Upper-left
    public Vector2 Max; // Lower-right

    public ImRect(Vector2 min, Vector2 max)
    {
        Min = min;
        Max = max;
    }

    public ImRect(Vector4 v)
    {
        Min = new Vector2(v.X, v.Y);
        Max = new Vector2(v.Z, v.W);
    }

    public ImRect(float x1, float y1, float x2, float y2)
    {
        Min = new Vector2(x1, y1);
        Max = new Vector2(x2, y2);
    }

    public Vector2 GetCenter()
    {
        return new Vector2((Min.X + Max.X) * 0.5f, (Min.Y + Max.Y) * 0.5f);
    }

    public Vector2 GetSize()
    {
        return new Vector2(Max.X - Min.X, Max.Y - Min.Y);
    }

    public readonly float GetWidth()
    {
        return Max.X - Min.X;
    }

    public readonly float GetHeight()
    {
        return Max.Y - Min.Y;
    }

    /// <summary>
    /// This is required before using <see cref="Contains(Vector2)"/>
    /// </summary>
    public ImRect MakePositive()
    {
        if (Min.X > Max.X)
        {
            (Min.X, Max.X) = (Max.X, Min.X);
        }

        if (Min.Y > Max.Y)
        {
            (Min.Y, Max.Y) = (Max.Y, Min.Y);
        }

        return this;
    }

    /// <summary>
    /// Top-left
    /// </summary>
    public Vector2 GetTL()
    {
        return Min;
    }

    /// <summary>
    /// Top right
    /// </summary>
    public Vector2 GetTR()
    {
        return new Vector2(Max.X, Min.Y);
    }

    /// <summary>
    /// Bottom left
    /// </summary>
    public Vector2 GetBL()
    {
        return new Vector2(Min.X, Max.Y);
    }

    /// <summary>
    /// Bottom right
    /// </summary>
    public Vector2 GetBR()
    {
        return Max;
    }

    /// <summary>
    /// This is required before using <see cref="Contains(Vector2)"/>
    /// </summary>
    /// <remarks>Please make sure to make the rectangle positive before testing</remarks>
    public bool Contains(Vector2 p)
    {
        return p.X >= Min.X && p.Y >= Min.Y && p.X < Max.X && p.Y < Max.Y;
    }

    public bool Contains(in ImRect r)
    {
        return r.Min.X >= Min.X && r.Min.Y >= Min.Y && r.Max.X <= Max.X && r.Max.Y <= Max.Y;
    }

    public bool Overlaps(in ImRect r)
    {
        return r.Min.Y < Max.Y && r.Max.Y > Min.Y && r.Min.X < Max.X && r.Max.X > Min.X;
    }

    public void Add(Vector2 p)
    {
        if (Min.X > p.X) Min.X = p.X;
        if (Min.Y > p.Y) Min.Y = p.Y;
        if (Max.X < p.X) Max.X = p.X;
        if (Max.Y < p.Y) Max.Y = p.Y;
    }

    public void Add(ImRect r)
    {
        if (Min.X > r.Min.X) Min.X = r.Min.X;
        if (Min.Y > r.Min.Y) Min.Y = r.Min.Y;
        if (Max.X < r.Max.X) Max.X = r.Max.X;
        if (Max.Y < r.Max.Y) Max.Y = r.Max.Y;
    }

    public void Expand(float amount)
    {
        Min.X -= amount;
        Min.Y -= amount;
        Max.X += amount;
        Max.Y += amount;
    }

    public void Expand(Vector2 amount)
    {
        Min.X -= amount.X;
        Min.Y -= amount.Y;
        Max.X += amount.X;
        Max.Y += amount.Y;
    }

    public void Translate(Vector2 d)
    {
        Min.X += d.X;
        Min.Y += d.Y;
        Max.X += d.X;
        Max.Y += d.Y;
    }

    public void TranslateX(float dx)
    {
        Min.X += dx;
        Max.X += dx;
    }

    public void TranslateY(float dy)
    {
        Min.Y += dy;
        Max.Y += dy;
    }

    public static ImRect RectBetweenPoints(Vector2 a, Vector2 b)
    {
        return new ImRect(
                          x1: Math.Min(a.X, b.X),
                          y1: Math.Min(a.Y, b.Y),
                          x2: Math.Max(a.X, b.X),
                          y2: Math.Max(a.Y, b.Y));
    }

    public static ImRect RectWithSize(Vector2 position, Vector2 size)
    {
        return new ImRect(position, position + size);
    }

    // Simple version, may lead to an inverted rectangle, which is fine for Contains/Overlaps test but not for display.
    public void ClipWith(ImRect r)
    {
        Min = MathUtils.Max(Min, r.Min);
        Max = MathUtils.Min(Max, r.Max);
    }

    // Full version, ensure both points are fully clipped.
    public void ClipWithFull(ImRect r)
    {
        Min = Min.Clamp(r.Min, r.Max);
        Max = Max.Clamp(r.Min, r.Max);
    }

    public void Floor()
    {
        var size = Max - Min;
        Min.X = (int)Min.X;
        Min.Y = (int)Min.Y;
        Max.X = Min.X + (int)size.X;
        Max.Y = Min.Y + (int)size.Y;
    }

    private bool IsInverted()
    {
        return Min.X > Max.X || Min.Y > Max.Y;
    }

    public override string ToString()
    {
        return $"Rect {Min}  {Max}";
    }

    public float GetAspect()
    {
        return GetWidth() / GetHeight();
    }
}