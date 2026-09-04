namespace T3.Core.DataTypes;

/// <summary>
/// Combines buffers required for mesh rendering
/// </summary>
public class ParticleSystem
{
    public BufferWithViews ParticleBuffer;
    public float SpeedFactor;
    public bool IsReset;
    public float InitializeVelocityFactor;
}

