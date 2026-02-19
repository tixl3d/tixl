float4 BlendColors(float4 tA, float4 tB, int blendMode)
{
    tA.a = saturate(tA.a);
    tB.a = saturate(tB.a);

    float a = tA.a + tB.a - tA.a * tB.a;

    float normalRatio = saturate(tB.a * 2 - 1);

    // float3 rgb = (1.0 - tB.a)*tA.rgb + tB.a*tB.rgb;
    float3 rgbNormalBlended = (1.0 - tB.a) * tA.rgb + tB.a * tB.rgb;
    float3 rgb = 1;

    switch ((int)blendMode)
    {
    // normal
    case 0:
        rgb = rgbNormalBlended;
        break;

    // screen
    case 1:
        rgb = 1 - (1 - tA.rgb) * (1 - tB.rgb * tB.a);
        break;

    // multiply
    case 2:
        rgb = lerp(tA.rgb, tA.rgb * tB.rgb, tB.a);
        break;

    // overlay
    case 3:
        rgb = float3(
            tA.r < 0.5 ? (2.0 * tA.r * tB.r) : (1.0 - 2.0 * (1.0 - tA.r) * (1.0 - tB.r)),
            tA.g < 0.5 ? (2.0 * tA.g * tB.g) : (1.0 - 2.0 * (1.0 - tA.g) * (1.0 - tB.g)),
            tA.b < 0.5 ? (2.0 * tA.b * tB.b) : (1.0 - 2.0 * (1.0 - tA.b) * (1.0 - tB.b)));

        rgb = lerp(tA.rgb, rgb, tB.a);
        break;

    // difference
    case 4:
        rgb = abs(tA.rgb - tB.rgb) * tB.a + tB.rgb * (1.0 - tB.a);
        break;

    // use a
    case 5:
        rgb = tA.rgb;
        break;

    // use b
    case 6:
        rgb = tB.rgb;
        break;

    // colorDodge
    case 7:
        rgb = tA.rgb / (1.0001 - saturate( tB.rgb)); 
        break;
        
    // linearDodge  
    case 8: 
        rgb = tA.rgb + tB.rgb;
        break;
    case 9: 
        a = tA.a * tB.a;
        break;

    }

    return float4(rgb, a);
}