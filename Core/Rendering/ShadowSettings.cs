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

        /// <summary>Shadow map texels the lookup moves along the surface normal, against self-shadowing acne.</summary>
        [FieldOffset(21 * 4)]
        public float NormalOffset;

        /// <summary>
        /// World size of one shadow map texel at a light clip space w of 1: 2 / (projection x scale * resolution).
        /// Multiplied by w it gives the texel size at a surface, for perspective and orthographic shadows alike.
        /// </summary>
        [FieldOffset(22 * 4)]
        public float TexelSizePerW;

        /// <summary>Cosine of the half angle inside which the casting spot light is at full strength.</summary>
        [FieldOffset(23 * 4)]
        public float SpotCosInner;

        /// <summary>
        /// Cosine of the half angle outside which the casting spot light is dark, so the shadow fades with its cone.
        /// Below -1 for other lights, which keeps the shadow at full strength.
        /// </summary>
        [FieldOffset(24 * 4)]
        public float SpotCosOuter;

        /// <summary>Tangent of half the shadow camera's field of view; 0 for orthographic shadows.</summary>
        [FieldOffset(25 * 4)]
        public float TanHalfFieldOfView;

        private const int Stride = 28 * 4;
    }

    private static Buffer _defaultParameterBuffer;
}
