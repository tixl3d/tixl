namespace T3.Graphics;

// Value types both layers use. Names follow DXGI/D3D11 where projects store them, so they cannot change.

public struct SampleDescription(int count, int quality)
{
    public int Count = count;
    public int Quality = quality;
}
