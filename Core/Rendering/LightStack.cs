using System;
using Buffer = T3.Graphics.Compat.Buffer;
using System.Runtime.InteropServices;
using T3.Graphics.Compat;
using T3.Core.Logging;
using T3.Core.Resource;

namespace T3.Core.Rendering;

/// <summary>Matches the <c>LIGHT_TYPE_*</c> defines in <c>shared/point-light.hlsl</c>.</summary>
public enum LightType
{
    Point = 0,
    Spot = 1,
    Directional = 2,
}

/// <summary>
/// One entry of the light cbuffer (b3), mirrored by <c>struct PointLight</c> in <c>shared/point-light.hlsl</c>.
/// Shaders that only know point lights read the first five fields, so their offsets must not change.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 6 * 16)]
public struct Light
{
    public static Light CreatePoint(Vector3 position, float intensity, Vector4 color, float range, float decay = 2)
    {
        return new Light
                   {
                       Position = position,
                       Intensity = intensity,
                       Color = color,
                       Range = range,
                       Decay = decay,
                       Type = LightType.Point,
                   };
    }

    /// <param name="coneAngleDegrees">Full opening angle of the cone.</param>
    /// <param name="softness">Fraction of the cone, from its edge inwards, over which the light fades.</param>
    public static Light CreateSpot(Vector3 position, Vector3 target, float intensity, Vector4 color, float range, float decay,
                                   float coneAngleDegrees, float softness)
    {
        var halfAngle = MathF.Max(coneAngleDegrees, 0) * 0.5f * MathF.PI / 180f;
        var innerHalfAngle = halfAngle * (1 - Math.Clamp(softness, 0, 1));
        return new Light
                   {
                       Position = position,
                       Intensity = intensity,
                       Color = color,
                       Range = range,
                       Decay = decay,
                       Type = LightType.Spot,
                       Direction = NormalizeOrDown(target - position),
                       SpotCosInner = MathF.Cos(innerHalfAngle),
                       SpotCosOuter = MathF.Cos(halfAngle),
                   };
    }

    public static Light CreateDirectional(Vector3 direction, float intensity, Vector4 color)
    {
        var normalizedDirection = NormalizeOrDown(direction);
        return new Light
                   {
                       // Shaders without directional support see a distant point light with constant falloff
                       // (decay 0 halves the intensity), which is close enough until they are ported.
                       Position = -normalizedDirection * DirectionalFallbackDistance,
                       Intensity = intensity,
                       Color = color,
                       Range = DirectionalFallbackDistance,
                       Decay = 0,
                       Type = LightType.Directional,
                       Direction = normalizedDirection,
                   };
    }

    [FieldOffset(0)]
    public Vector3 Position;

    [FieldOffset(3 * 4)]
    public float Intensity;

    [FieldOffset(4 * 4)]
    public Vector4 Color;

    [FieldOffset(8 * 4)]
    public float Range;

    [FieldOffset(9 * 4)]
    public float Decay;

    [FieldOffset(10 * 4)]
    public LightType Type;

    /** Cosine of the half angle inside which a spot light is at full strength. */
    [FieldOffset(11 * 4)]
    public float SpotCosInner;

    /** Normalized direction the light travels in (spot and directional). */
    [FieldOffset(12 * 4)]
    public Vector3 Direction;

    /** Cosine of the half angle outside which a spot light is dark. */
    [FieldOffset(15 * 4)]
    public float SpotCosOuter;

    /** Orientation of rectangular area lights. */
    [FieldOffset(16 * 4)]
    public Vector3 Right;

    /** Extent of area lights. */
    [FieldOffset(20 * 4)]
    public Vector2 Size;

    private static Vector3 NormalizeOrDown(Vector3 direction)
    {
        var length = direction.Length();
        return length > 1e-6f ? direction / length : -Vector3.UnitY;
    }

    private const float DirectionalFallbackDistance = 10000;
}

/// <summary>The lights of the current subtree, pushed and popped by the light operators.</summary>
public class LightStack
{
    public const int MaxLights = 8;

    public Buffer ConstBuffer
    {
        get
        {
            if (_currentSize == 0)
            {
                return GetDefaultBuffer();
            }

            if (_isConstBufferDirty)
            {
                UpdateConstBuffer(ref _lights, ref _constBuffer, _currentSize);
                _isConstBufferDirty = false;
            }

            return _constBuffer;
        }
    }

    public void Clear()
    {
        _currentSize = 0;
        _isConstBufferDirty = true;
    }

    public bool Push(Light light)
    {
        if (_currentSize == MaxLights)
        {
            Log.Warning($"Trying to push a new light, but limit {MaxLights} reached");
            return false;
        }

        _lights[_currentSize++] = light;
        _isConstBufferDirty = true;

        return true;
    }

    public void Pop()
    {
        if (_currentSize > 0)
        {
            _currentSize--;
            _isConstBufferDirty = true;
        }
    }

    private static void UpdateConstBuffer(ref Light[] lights, ref Buffer constBuffer, int activeLightCount)
    {
        var size = Marshal.SizeOf<Light>() * MaxLights + 16;
        using (var data = new DataStream(size, true, true))
        {
            foreach (var light in lights)
            {
                data.Write(light);
            }
            data.Write(activeLightCount);
            data.Position = 0;

            if (constBuffer == null)
            {
                var bufferDesc = new BufferDescription
                                     {
                                         Usage = ResourceUsage.Default,
                                         SizeInBytes = size,
                                         BindFlags = BindFlags.ConstantBuffer
                                     };
                constBuffer = new Buffer(ResourceManager.Device, data, bufferDesc) { DebugName = "LightsConstBuffer" };
            }
            else
            {
                ResourceManager.Device.ImmediateContext.UpdateSubresource(new DataBox(data.DataPointer, 0, 0), constBuffer, 0);
            }
        }
    }

    private Buffer GetDefaultBuffer()
    {
        if (_defaultConstBuffer == null)
        {
            _defaultLights[0] = Light.CreatePoint(new Vector3(8, 20, 3), 4, new Vector4(1, 0.93f, 0.95f, 1), 1);
            _defaultLights[1] = Light.CreatePoint(new Vector3(-8, -20, -8), 3f, new Vector4(0.97f, 0.96f, 1, 1), 1);
            UpdateConstBuffer(ref _defaultLights, ref _defaultConstBuffer, 2);
        }

        return _defaultConstBuffer;
    }

    public int Count => _currentSize > 0 ? _currentSize : 2;

    public Light GetLight(int index)
    {
        var useDefaultLights = _currentSize == 0;

        if (index >= Count)
        {
            Log.Warning($"Requested light index {index} exceed current count of {Count}");
            index = Count - 1;
        }

        return useDefaultLights
                   ? _defaultLights[index]
                   : _lights[index];
    }

    private Light[] _lights = new Light[MaxLights];
    private int _currentSize = 0;
    private bool _isConstBufferDirty = true;
    private Buffer _constBuffer = null;

    private static Light[] _defaultLights = new Light[MaxLights];
    private static Buffer _defaultConstBuffer;
}
