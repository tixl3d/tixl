using System.Runtime.InteropServices;
using T3.Graphics.Compat;
using T3.Core.Resource;

namespace T3.Core.Rendering;

/// <summary>
/// Parameters [SetShadow] passes to the draws that receive its shadow map, through
/// <see cref="T3.Core.Operator.EvaluationContext.ShadowParameters"/>.
/// </summary>
public static class ShadowSettings
{
    /// <summary>Parameters outside [SetShadow]: a transparent shadow color, so draws skip the shadow map lookup.</summary>
    public static Buffer DefaultParameterBuffer
    {
        get
        {
            if (_defaultParameterBuffer == null)
            {
                ResourceManager.SetupConstBuffer(new ShadowParameters(), ref _defaultParameterBuffer);
            }

            return _defaultParameterBuffer;
        }
    }

    /// <summary>Layout of <c>cbuffer ShadowParams</c> in <c>mesh-Draw.hlsl</c>.</summary>
    [StructLayout(LayoutKind.Explicit, Size = Stride)]
    public struct ShadowParameters
    {
        /// <summary>The light camera's world to clip space matrix, with the rows as [SetMatrixVar] stores them.</summary>
        [FieldOffset(0)]
        public Matrix4x4 WorldToLightClipSpace;

        /// <summary>Color shadowed surfaces are blended towards. Alpha is the strength, 0 disables shadows.</summary>
        [FieldOffset(16 * 4)]
        public Vector4 Color;

        /// <summary>How much deeper than the occluder in the shadow map a surface must be to count as shadowed.</summary>
        [FieldOffset(20 * 4)]
        public float Bias;

        /// <summary>World space distance the lookup moves along the surface normal, against self-shadowing acne.</summary>
        [FieldOffset(21 * 4)]
        public float NormalOffset;

        private const int Stride = 24 * 4;
    }

    private static Buffer _defaultParameterBuffer;
}
