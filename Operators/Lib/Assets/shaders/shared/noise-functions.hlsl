#ifndef NOISEFUNC_H
#define NOISEFUNC_H

//
// Description : Array and textureless GLSL 2D/3D/4D simplex
//               noise functions.
//      Author : Ian McEwan, Ashima Arts.
//  Maintainer : stegu
//     Lastmod : 20110822 (ijm)
//     License : Copyright (C) 2011 Ashima Arts. All rights reserved.
//               Distributed under the MIT License. See LICENSE file.
//               https://github.com/ashima/webgl-noise
//               https://github.com/stegu/webgl-noise
//

float3 mod289(float3 x)
{
  return x - floor(x * (1.0 / 289.0)) * 289.0;
}

float4 mod289(float4 x)
{
  return x - floor(x * (1.0 / 289.0)) * 289.0;
}

float4 permute(float4 x)
{
  return mod289(((x * 34.0) + 1.0) * x);
}

float4 taylorInvSqrt(float4 r)
{
  return 1.79284291400159 - 0.85373472095314 * r;
}

float3 fade(float3 t)
{
  return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

float cnoise(float3 P)
{
  float3 Pi0 = floor(P);                    // Integer part for indexing
  float3 Pi1 = Pi0 + float3(1.0, 1.0, 1.0); // Integer part + 1
  Pi0 = mod289(Pi0);
  Pi1 = mod289(Pi1);
  float3 Pf0 = frac(P);                     // Fractional part for interpolation
  float3 Pf1 = Pf0 - float3(1.0, 1.0, 1.0); // Fractional part - 1.0
  float4 ix = float4(Pi0.x, Pi1.x, Pi0.x, Pi1.x);
  float4 iy = float4(Pi0.yy, Pi1.yy);
  float4 iz0 = Pi0.zzzz;
  float4 iz1 = Pi1.zzzz;

  float4 ixy = permute(permute(ix) + iy);
  float4 ixy0 = permute(ixy + iz0);
  float4 ixy1 = permute(ixy + iz1);

  float4 gx0 = ixy0 * (1.0 / 7.0);
  float4 gy0 = frac(floor(gx0) * (1.0 / 7.0)) - 0.5;
  gx0 = frac(gx0);
  float4 gz0 = float4(0.5, 0.5, 0.5, 0.5) - abs(gx0) - abs(gy0);
  float4 sz0 = step(gz0, float4(0.0, 0.0, 0.0, 0.0));
  gx0 -= sz0 * (step(0.0, gx0) - 0.5);
  gy0 -= sz0 * (step(0.0, gy0) - 0.5);

  float4 gx1 = ixy1 * (1.0 / 7.0);
  float4 gy1 = frac(floor(gx1) * (1.0 / 7.0)) - 0.5;
  gx1 = frac(gx1);
  float4 gz1 = float4(0.5, 0.5, 0.5, 0.5) - abs(gx1) - abs(gy1);
  float4 sz1 = step(gz1, float4(0.0, 0.0, 0.0, 0.0));
  gx1 -= sz1 * (step(0.0, gx1) - 0.5);
  gy1 -= sz1 * (step(0.0, gy1) - 0.5);

  float3 g000 = float3(gx0.x, gy0.x, gz0.x);
  float3 g100 = float3(gx0.y, gy0.y, gz0.y);
  float3 g010 = float3(gx0.z, gy0.z, gz0.z);
  float3 g110 = float3(gx0.w, gy0.w, gz0.w);
  float3 g001 = float3(gx1.x, gy1.x, gz1.x);
  float3 g101 = float3(gx1.y, gy1.y, gz1.y);
  float3 g011 = float3(gx1.z, gy1.z, gz1.z);
  float3 g111 = float3(gx1.w, gy1.w, gz1.w);

  float4 norm0 = taylorInvSqrt(float4(dot(g000, g000), dot(g010, g010), dot(g100, g100), dot(g110, g110)));
  g000 *= norm0.x;
  g010 *= norm0.y;
  g100 *= norm0.z;
  g110 *= norm0.w;
  float4 norm1 = taylorInvSqrt(float4(dot(g001, g001), dot(g011, g011), dot(g101, g101), dot(g111, g111)));
  g001 *= norm1.x;
  g011 *= norm1.y;
  g101 *= norm1.z;
  g111 *= norm1.w;

  float n000 = dot(g000, Pf0);
  float n100 = dot(g100, float3(Pf1.x, Pf0.yz));
  float n010 = dot(g010, float3(Pf0.x, Pf1.y, Pf0.z));
  float n110 = dot(g110, float3(Pf1.xy, Pf0.z));
  float n001 = dot(g001, float3(Pf0.xy, Pf1.z));
  float n101 = dot(g101, float3(Pf1.x, Pf0.y, Pf1.z));
  float n011 = dot(g011, float3(Pf0.x, Pf1.yz));
  float n111 = dot(g111, Pf1);

  float3 fade_xyz = fade(Pf0);
  float4 n_z = lerp(float4(n000, n100, n010, n110), float4(n001, n101, n011, n111), fade_xyz.z);
  float2 n_yz = lerp(n_z.xy, n_z.zw, fade_xyz.y);
  float n_xyz = lerp(n_yz.x, n_yz.y, fade_xyz.x);
  return 2.2 * n_xyz;
}

float pnoise(float3 P, float3 rep)
{
  float3 Pi0 = fmod(floor(P), rep);                    // Integer part, modulo period
  float3 Pi1 = fmod(Pi0 + float3(1.0, 1.0, 1.0), rep); // Integer part + 1, mod period
  Pi0 = mod289(Pi0);
  Pi1 = mod289(Pi1);
  float3 Pf0 = frac(P);                     // Fractional part for interpolation
  float3 Pf1 = Pf0 - float3(1.0, 1.0, 1.0); // Fractional part - 1.0
  float4 ix = float4(Pi0.x, Pi1.x, Pi0.x, Pi1.x);
  float4 iy = float4(Pi0.yy, Pi1.yy);
  float4 iz0 = Pi0.zzzz;
  float4 iz1 = Pi1.zzzz;

  float4 ixy = permute(permute(ix) + iy);
  float4 ixy0 = permute(ixy + iz0);
  float4 ixy1 = permute(ixy + iz1);

  float4 gx0 = ixy0 * (1.0 / 7.0);
  float4 gy0 = frac(floor(gx0) * (1.0 / 7.0)) - 0.5;
  gx0 = frac(gx0);
  float4 gz0 = float4(0.5, 0.5, 0.5, 0.5) - abs(gx0) - abs(gy0);
  float4 sz0 = step(gz0, float4(0.0, 0.0, 0.0, 0.0));
  gx0 -= sz0 * (step(0.0, gx0) - 0.5);
  gy0 -= sz0 * (step(0.0, gy0) - 0.5);

  float4 gx1 = ixy1 * (1.0 / 7.0);
  float4 gy1 = frac(floor(gx1) * (1.0 / 7.0)) - 0.5;
  gx1 = frac(gx1);
  float4 gz1 = float4(0.5, 0.5, 0.5, 0.5) - abs(gx1) - abs(gy1);
  float4 sz1 = step(gz1, float4(0.0, 0.0, 0.0, 0.0));
  gx1 -= sz1 * (step(0.0, gx1) - 0.5);
  gy1 -= sz1 * (step(0.0, gy1) - 0.5);

  float3 g000 = float3(gx0.x, gy0.x, gz0.x);
  float3 g100 = float3(gx0.y, gy0.y, gz0.y);
  float3 g010 = float3(gx0.z, gy0.z, gz0.z);
  float3 g110 = float3(gx0.w, gy0.w, gz0.w);
  float3 g001 = float3(gx1.x, gy1.x, gz1.x);
  float3 g101 = float3(gx1.y, gy1.y, gz1.y);
  float3 g011 = float3(gx1.z, gy1.z, gz1.z);
  float3 g111 = float3(gx1.w, gy1.w, gz1.w);

  float4 norm0 = taylorInvSqrt(float4(dot(g000, g000), dot(g010, g010), dot(g100, g100), dot(g110, g110)));
  g000 *= norm0.x;
  g010 *= norm0.y;
  g100 *= norm0.z;
  g110 *= norm0.w;
  float4 norm1 = taylorInvSqrt(float4(dot(g001, g001), dot(g011, g011), dot(g101, g101), dot(g111, g111)));
  g001 *= norm1.x;
  g011 *= norm1.y;
  g101 *= norm1.z;
  g111 *= norm1.w;

  float n000 = dot(g000, Pf0);
  float n100 = dot(g100, float3(Pf1.x, Pf0.yz));
  float n010 = dot(g010, float3(Pf0.x, Pf1.y, Pf0.z));
  float n110 = dot(g110, float3(Pf1.xy, Pf0.z));
  float n001 = dot(g001, float3(Pf0.xy, Pf1.z));
  float n101 = dot(g101, float3(Pf1.x, Pf0.y, Pf1.z));
  float n011 = dot(g011, float3(Pf0.x, Pf1.yz));
  float n111 = dot(g111, Pf1);

  float3 fade_xyz = fade(Pf0);
  float4 n_z = lerp(float4(n000, n100, n010, n110), float4(n001, n101, n011, n111), fade_xyz.z);
  float2 n_yz = lerp(n_z.xy, n_z.zw, fade_xyz.y);
  float n_xyz = lerp(n_yz.x, n_yz.y, fade_xyz.x);
  return 2.2 * n_xyz;
}

float snoise(float3 v)
{
  const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);
  const float4 D = float4(0.0, 0.5, 1.0, 2.0);

  // First corner
  float3 i = floor(v + dot(v, C.yyy));
  float3 x0 = v - i + dot(i, C.xxx);

  // Other corners
  float3 g = step(x0.yzx, x0.xyz);
  float3 l = 1.0 - g;
  float3 i1 = min(g.xyz, l.zxy);
  float3 i2 = max(g.xyz, l.zxy);

  //   x0 = x0 - 0.0 + 0.0 * C.xxx;
  //   x1 = x0 - i1  + 1.0 * C.xxx;
  //   x2 = x0 - i2  + 2.0 * C.xxx;
  //   x3 = x0 - 1.0 + 3.0 * C.xxx;
  float3 x1 = x0 - i1 + C.xxx;
  float3 x2 = x0 - i2 + C.yyy; // 2.0*C.x = 1/3 = C.y
  float3 x3 = x0 - D.yyy;      // -1.0+3.0*C.x = -0.5 = -D.y

  // Permutations
  i = mod289(i);
  float4 p = permute(permute(permute(
                                 i.z + float4(0.0, i1.z, i2.z, 1.0)) +
                             i.y + float4(0.0, i1.y, i2.y, 1.0)) +
                     i.x + float4(0.0, i1.x, i2.x, 1.0));

  // Gradients: 7x7 points over a square, mapped onto an octahedron.
  // The ring size 17*17 = 289 is close to a multiple of 49 (49*6 = 294)
  float n_ = 0.142857142857; // 1.0/7.0
  float3 ns = n_ * D.wyz - D.xzx;

  float4 j = p - 49.0 * floor(p * ns.z * ns.z); //  mod(p,7*7)

  float4 x_ = floor(j * ns.z);
  float4 y_ = floor(j - 7.0 * x_); // mod(j,N)

  float4 x = x_ * ns.x + ns.yyyy;
  float4 y = y_ * ns.x + ns.yyyy;
  float4 h = 1.0 - abs(x) - abs(y);

  float4 b0 = float4(x.xy, y.xy);
  float4 b1 = float4(x.zw, y.zw);

  // float4 s0 = float4(lessThan(b0,0.0))*2.0 - 1.0;
  // float4 s1 = float4(lessThan(b1,0.0))*2.0 - 1.0;
  float4 s0 = floor(b0) * 2.0 + 1.0;
  float4 s1 = floor(b1) * 2.0 + 1.0;
  float4 sh = -step(h, float4(0, 0, 0, 0));

  float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
  float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

  float3 p0 = float3(a0.xy, h.x);
  float3 p1 = float3(a0.zw, h.y);
  float3 p2 = float3(a1.xy, h.z);
  float3 p3 = float3(a1.zw, h.w);

  // Normalise gradients
  float4 norm = taylorInvSqrt(float4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
  p0 *= norm.x;
  p1 *= norm.y;
  p2 *= norm.z;
  p3 *= norm.w;

  // Mix final noise value
  float4 m = max(0.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
  m = m * m;
  return 42.0 * dot(m * m, float4(dot(p0, x0), dot(p1, x1),
                                  dot(p2, x2), dot(p3, x3)));
}

float3 snoiseVec3(float3 p)
{
  float s = snoise(float3(p.x + 0.0001, p.y, p.z));
  float s1 = snoise(float3(p.y - 19.1, p.z + 33.4, p.x + 47.2));
  float s2 = snoise(float3(p.z + 74.2, p.x - 124.5, p.y + 99.4));
  float3 c = float3(s, s1, s2);
  return c;
}

float3 curlNoise(float3 p)
{
  const float e = .001;
  float3 dx = float3(e, 0.0, 0.0);
  float3 dy = float3(0.0, e, 0.0);
  float3 dz = float3(0.0, 0.0, e);

  float3 p_x0 = snoiseVec3(p - dx);
  float3 p_x1 = snoiseVec3(p + dx);
  float3 p_y0 = snoiseVec3(p - dy);
  float3 p_y1 = snoiseVec3(p + dy);
  float3 p_z0 = snoiseVec3(p - dz);
  float3 p_z1 = snoiseVec3(p + dz);

  float x = p_y1.z - p_y0.z - p_z1.y + p_z0.y;
  float y = p_z1.x - p_z0.x - p_x1.z + p_x0.z;
  float z = p_x1.y - p_x0.y - p_y1.x + p_y0.x;

  const float divisor = 1.0 / (2.0 * e);
  return normalize(float3(x, y, z) * divisor);
}

#endif