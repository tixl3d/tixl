// ===================================================
// Reference vertex shader for the Slug algorithm.
// SPDX-License-Identifier: MIT OR Apache-2.0
// Copyright 2017, by Eric Lengyel.
// ===================================================


// The per-vertex input data consists of 5 attributes all having 4 floating-point components:
//
// 0 - pos
// 1 - tex
// 2 - jac
// 3 - bnd
// 4 - col

// pos.xy = object-space vertex coordinates.
// pos.zw = object-space normal vector.

// tex.xy = em-space sample coordinates.

// tex.z = location of glyph data in band texture (interpreted as integer):

// | 31                         24 | 23                         16 | 15                          8 | 7                           0 |
// +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+
// |           y coordinate of glyph data in band texture          |           x coordinate of glyph data in band texture          |
// +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+

// tex.w = max band indexes and flags (interpreted as integer):

// | 31                         24 | 23                         16 | 15                          8 | 7                           0 |
// +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+
// | 0   0   0 | E | 0   0   0   0 |           band max y          | 0   0   0   0   0   0   0   0 |           band max x          |
// +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+

// jac = inverse Jacobian matrix entries (00, 01, 10, 11).
// bnd = (band scale x, band scale y, band offset x, band offset y).
// col = vertex color (red, green, blue, alpha).


void SlugUnpack(float4 tex, float4 bnd, out float4 vbnd, out int4 vgly)
{
	uint2 g = asuint(tex.zw);
	vgly = int4(g.x & 0xFFFFU, g.x >> 16U, g.y & 0xFFFFU, g.y >> 16U);
	vbnd = bnd;
}

float2 SlugDilate(float4 pos, float4 tex, float4 jac, float4 m0, float4 m1, float4 m3, float2 dim, out float2 vpos)
{
	float2 n = normalize(pos.zw);
	float s = dot(m3.xy, pos.xy) + m3.w;
	float t = dot(m3.xy, n);

	float u = (s * dot(m0.xy, n) - t * (dot(m0.xy, pos.xy) + m0.w)) * dim.x;
	float v = (s * dot(m1.xy, n) - t * (dot(m1.xy, pos.xy) + m1.w)) * dim.y;

	float s2 = s * s;
	float st = s * t;
	float uv = u * u + v * v;
	float2 d = pos.zw * (s2 * (st + sqrt(uv)) / (uv - st * st));

	vpos = pos.xy + d;
	return (float2(tex.x + dot(d, jac.xy), tex.y + dot(d, jac.zw)));
}

cbuffer ParamStruct : register(b0)
{
	float4 slug_matrix[4];							// The four rows of the MVP matrix.
	float4 slug_viewport;							// The viewport dimensions, in pixels.
};

struct VertexStruct
{
	float4 position : SV_Position;					// Clip-space vertex position.
	float4 color : U_COLOR;							// Vertex color.
	float2 texcoord : U_TEXCOORD;					// Em-space sample coordinates.
	nointerpolation float4 banding : U_BANDING;		// Band scale and offset, constant over glyph.
	nointerpolation int4 glyph : U_GLYPH;			// (glyph data x coord, glyph data y coord, band max x, band max y and flags), constant over glyph.
};

VertexStruct main(float4 attrib[5] : ATTRIB, uint vid : SV_VertexID)
{
	float2 p;
	VertexStruct vresult;

	// Apply dynamic dilation to vertex position. Returns new em-space sample position.

	vresult.texcoord = SlugDilate(attrib[0], attrib[1], attrib[2], slug_matrix[0], slug_matrix[1], slug_matrix[3], slug_viewport.xy, p);

	// Apply MVP matrix to dilated vertex position.

	vresult.position.x = p.x * slug_matrix[0].x + p.y * slug_matrix[0].y + slug_matrix[0].w;
	vresult.position.y = p.x * slug_matrix[1].x + p.y * slug_matrix[1].y + slug_matrix[1].w;
	vresult.position.z = p.x * slug_matrix[2].x + p.y * slug_matrix[2].y + slug_matrix[2].w;
	vresult.position.w = p.x * slug_matrix[3].x + p.y * slug_matrix[3].y + slug_matrix[3].w;

	// Unpack or pass through remaining vertex data.

	SlugUnpack(attrib[1], attrib[3], vresult.banding, vresult.glyph);
	vresult.color = attrib[4];
	return (vresult);
}
