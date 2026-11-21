namespace Lib.volume.fields;

/// <summary>
/// Shader text shared by the NanoVDB field operators. The tree walk itself lives in the shader
/// include, so the C# side only has to pull it in once per generated shader; every Vdb field
/// writes the same <see cref="CommonKey"/>, which the assembler's dictionary then keeps once no
/// matter how many of them a graph contains.
/// </summary>
internal static class VdbFieldCode
{
    public const string CommonKey = "NanoVdbCommon";
    public const string OutsideKey = "VdbFieldOutside";

    public const string CommonInclude = "#include \"volume/nanovdb-common.hlsl\"";

    /// <summary>
    /// Outside the grid's box a field has to report something the caller can march against: the
    /// distance to the box plus the (positive) value, so a sphere-trace never steps into the volume
    /// blind, clamped so it stays finite far away.
    /// </summary>
    public const string OutsideBoxFunction = """
                                             float VdbFieldOutside(float3 p, float3 boundsMin, float3 boundsMax, float value, float outsideValue)
                                             {
                                                 float3 outsideDelta = max(boundsMin - p, p - boundsMax);
                                                 float outsideDistance = length(max(outsideDelta, 0.0));
                                                 if (outsideDistance <= 0)
                                                     return value;

                                                 return min(outsideDistance + max(value, 0.0), outsideValue);
                                             }
                                             """;
}
