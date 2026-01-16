namespace T3.Core.DataTypes.ShaderGraph;

public static class ShaderGraphIncludes
{
    public const string IncludeBiasFunctions
        = """
          #include "shared/bias-functions.hlsl"
          """;
    
    public const string Common
        = """
          #ifndef PI
          #define PI 3.14159265
          #endif
          #ifndef TAU
          #define TAU (2*PI)
          #endif
          #ifndef PHI
          #define PHI (sqrt(5)*0.5 + 0.5)
          #endif

          #ifndef mod
          #define mod(x, y) ((x) - (y) * floor((x) / (y)))
          #endif
          """;

    /// <summary>
    /// This is part of HG_SDF https://mercury.sexy/hg_sdf/
    /// </summary>
    public const string CommonHgSdf
        = """

          // Sign function that doesn't return 0
          float sgn(float x) {
          	return (x<0)?-1:1;
          }

          float2 sgn(float2 v) {
          	return float2((v.x<0)?-1:1, (v.y<0)?-1:1);
          }

          float square (float x) {
          	return x*x;
          }

          float2 square (float2 x) {
          	return x*x;
          }

          float3 square (float3 x) {
          	return x*x;
          }

          float lengthSqr(float3 x) {
          	return dot(x, x);
          }


          // Maximum/minumum elements of a vector
          float vmax(float2 v) {
          	return max(v.x, v.y);
          }

          float vmax(float3 v) {
          	return max(max(v.x, v.y), v.z);
          }

          float vmax(float4 v) {
          	return max(max(v.x, v.y), max(v.z, v.w));
          }

          float vmin(float2 v) {
          	return min(v.x, v.y);
          }

          float vmin(float3 v) {
          	return min(min(v.x, v.y), v.z);
          }

          float vmin(float4 v) {
          	return min(min(v.x, v.y), min(v.z, v.w));
          }

          // Rotate around a coordinate axis (i.e. in a plane perpendicular to that axis) by angle <a>.
          // Read like this: R(p.xz, a) rotates "x towards z".
          // This is fast if <a> is a compile-time constant and slower (but still practical) if not.
          void pR(inout float2 p, float a) {
          	p = cos(a)*p + sin(a)*float2(p.y, -p.x);
          }

          // Shortcut for 45-degrees rotation
          void pR45(inout float2 p) {
          	p = (p + float2(p.y, -p.x))*sqrt(0.5);
          }

          // Repeat space along one axis. Use like this to repeat along the x axis:
          // <float cell = pMod1(p.x,5);> - using the return value is optional.
          float pMod1(inout float p, float size) {
          	float halfsize = size*0.5;
          	float c = floor((p + halfsize)/size);
          	p = mod(p + halfsize, size) - halfsize;
          	return c;
          }
          """;

    public const string GetColorBlendFactor
        = """
            float GetColorBlendFactor(float d2, float d1, float k) 
            {
              return  clamp(0.5 + 0.5 * (d2 - d1) / k, 0.0, 1.0);
            };
          """;
}