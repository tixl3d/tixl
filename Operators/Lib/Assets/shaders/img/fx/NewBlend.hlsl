#include "shared/color-functions.hlsl"

cbuffer ParamConstants : register(b0) {
  float4 ImageAColor;
  float4 ImageBColor;
  float ColorMode;
  float AlphaMode;
  float UseNormalForUpperHalf;
  float ScaleMode;
  float Factor;
  float ClampFactor;
  float ClampOutput;
}

struct vsOutput {
  float4 position : SV_POSITION;
  float2 texCoord : TEXCOORD;
};

Texture2D<float4> ImageA : register(t0);
Texture2D<float4> ImageB : register(t1);
Texture2D<float4> ImageMask : register(t2);
sampler texSampler : register(s0);

float IsBetween(float value, float low, float high) {
  return (value >= low && value <= high) ? 1 : 0;
}

float4 psMain(vsOutput psInput) : SV_TARGET {
  float2 uv = psInput.texCoord;

  int height, width;

  ImageA.GetDimensions(width, height);
  float imageAAspect = (float)width / height;

  ImageB.GetDimensions(width, height);
  float imageBAspect = (float)width / height;

  float aspectDifference =
      (imageAAspect - imageBAspect) * (ScaleMode > 1.5 ? 1 : -1);

  float2 uvB =
      ScaleMode < 0.5
          ? uv
          : (aspectDifference < 0
                 ? float2((uv.x - 0.5) * imageAAspect / imageBAspect + 0.5,
                          uv.y)
                 : float2(uv.x,
                          (uv.y - 0.5) * imageBAspect / imageAAspect + 0.5));

  float4 tA = ImageA.Sample(texSampler, uv) * ImageAColor;
  float4 tB = ImageB.Sample(texSampler, uvB) * ImageBColor;
  float4 tM = ImageMask.Sample(texSampler,uv);
  tA.a = clamp(tA.a, 0, 1);
  tB.a = clamp(tB.a, 0, 1);

  float normalRatio = saturate(tB.a * 2 - 1);

  if (UseNormalForUpperHalf > 0.5)
    tB.a = saturate(tB.a * 2);

  // float3 rgb = (1.0 - tB.a)*tA.rgb + tB.a*tB.rgb;
  float3 rgbNormalBlended = (1.0 - tB.a) * tA.rgb + tB.a * tB.rgb;
  float4 rgba = tA.rgba;

  float4 f = Factor*tM;
  // clamp f if neefed
  if(ClampFactor == 1){f = saturate(f);}
  float4 fc = 1-f;

  float3 lchA, lchB;

  // function taken from
  // https://en.wikipedia.org/wiki/Blend_modes
  switch ((int)ColorMode) {
  // Normal B
  case 0:
    rgba = lerp(tA,tB,f);
    break;

  // add A + B
  case 1:
    rgba = tA + tB*f;
    break;

  // sub A - B
  case 2:
    rgba = tA - tB*f;
    break;

  // difference abs(A-B)
  case 3:
    rgba = lerp(tA,abs(tA.rgba - tB.rgba*f),f);
    break;

  // multiply A*B
  case 4:
    rgba = tA.rgba * lerp(1,tB,f);
    break;

  // div A/B
  case 5:
    rgba = tA/lerp(1,tB,f);
    break;

  // screen 1-(1-A)*(1-B)
  case 6:
    rgba = 1 - (1 - tA.rgba) * (1 - tB.rgba*f);
    break;

  // overlay 1 - 2*(1 - B)*(1 - A)
  case 7:
    rgba =
        float4(tA.r < 0.5 ? tA.r * (2 * tB.r * f.r + fc.r) : 1 - (1 - tA.r) * (2 * (1 - tB.r) * f.r + fc.r),
               tA.g < 0.5 ? tA.g * (2 * tB.g * f.g + fc.g) : 1 - (1 - tA.g) * (2 * (1 - tB.g) * f.g + fc.g),
               tA.b < 0.5 ? tA.b * (2 * tB.b * f.b + fc.b) : 1 - (1 - tA.r) * (2 * (1 - tB.r) * f.b + fc.b),
               tA.a < 0.5 ? tA.a * (2 * tB.a * f.a + fc.a) : 1 - (1 - tA.a) * (2 * (1 - tB.a) * f.a + fc.a));
    // rgb = lerp(tA.rgb, rgb, tB.a); // why ?
    break;

  // color dodge A/(1 - B)
  case 8:
    rgba = tA / (f*(1 - tB)+fc);
    break;

  // burn 1 - (1 - A)/B
  case 9:
    rgba = 1 - (1 - tA) / (tB*f + fc);
    break;

  // MixUsingImageB_A
  case 10:
    rgba = lerp(tA,lerp(tA, tB, ImageBColor.a),f);
    break;

  // linear light A + 2*B - 1
  case 11:
    rgba = tA + (2 * tB - 1)*f;
    break;

  // soft light (pure magic)
  case 12:
    rgba = tA * (f*((1 - 2 * tB) * tA + 2 * tB)+fc);
    break;

  // darken min(A,B)
  case 13:
    rgba = lerp(tA,min(tA, tB),f);
    break;

  // lighten max(A,B)
  case 14:
    rgba = lerp(tA,max(tA, tB),f);
    break;

  // hue
  case 15:
    lchB = RgbToLCh(tB.rgb);
    if (lchB.y != 0) {
      lchA = RgbToLCh(tA.rgb);
      rgba.rgb = LChToRgb(float3(lchA.x, lchA.y, lchB.z));
    } else {
      rgba.rgb = tA.rgb;
    }
    rgba = lerp(tA,rgba,f);
    break;

  // saturation
  case 16:
    lchB = RgbToLCh(tB.rgb);
    lchA = RgbToLCh(tA.rgb);
    if (lchB.y != 0) {
      rgba.rgb = LChToRgb(float3(lchA.x, lchB.y, lchA.z));
    } else {
      rgba.rgb = lchA.x;
    }
    rgba = lerp(tA,rgba,f);
    break;

  // color
  case 17:
    lchB = RgbToLCh(tB.rgb);
    lchA = RgbToLCh(tA.rgb);
    if (lchB.y != 0) {
      rgba.rgb = LChToRgb(float3(lchA.x, lchB.y, lchB.z));
    } else {
      rgba.rgb = lchA.x;
    }
    rgba = lerp(tA,rgba,f);
    break;

    // Luminosity
  case 18:
    lchA = RgbToLCh(tA.rgb);
    lchB = RgbToLCh(tB.rgb);
    if (lchA.y != 0) {
      rgba.rgb = LChToRgb(float3(lchB.x, lchA.y, lchA.z));
    } else {
      rgba.rgb = lchB.x;
    }
    rgba = lerp(tA,rgba,f);
    break;
  }

  switch ((int)AlphaMode) {

    // case 1:
    //     a = tA.a;
    //     break;

  case 0:
      rgba.a = tB.a;
      
  case 1:
    rgba.a = tA.a * tB.a;
    break;

  case 2:
    rgba.a = 1;
    break;

  case 3:
    break;

  case 4:
    rgba.a = tA.a;
    break;

  case 5:
    rgba.a = tB.a;
    break;

  case 6:
    rgba.a = (rgba.r + rgba.g + rgba.b);
    break;

  case 7:
    rgba.a = (tA.r + tA.g + tA.b) / 3;
    break;

  case 8:
    rgba.a = (tB.r + tB.g + tB.b) / 3;
    break;

  case 9:
    rgba.a = tA.a + tB.a;
    break;

  case 10:
    rgba.a = max(tA.a, tB.a);
    break;
  }

  if (UseNormalForUpperHalf > 0.5) {rgba.rgb = lerp(rgba.rgb, rgbNormalBlended, normalRatio);}

  if (ClampOutput == 1) {
      rgba.r = saturate(rgba.r);
      rgba.g = saturate(rgba.g);
      rgba.b = saturate(rgba.b);
      rgba.a = saturate(rgba.a);
  }
  
  return rgba;
}