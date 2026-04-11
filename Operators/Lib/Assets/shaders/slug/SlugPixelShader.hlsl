// ===================================================
// Reference pixel shader for the Slug algorithm.
// This code is made available under the MIT License.
// Copyright 2017, by Eric Lengyel.
// ===================================================


// The curve and band textures use a fixed width of 4096 texels.

#define kLogBandTextureWidth 12

// It's convenient to have a texel load function to aid in translation to other shader languages.

#define TexelLoad2D(x, y) x.Load(int3(y, 0))


uint CalcRootCode(float y1, float y2, float y3)
{
	// Calculate the root eligibility code for a sample-relative quadratic Bézier curve.
	// Extract the signs of the y coordinates of the three control points.

	uint i1 = asuint(y1) >> 31U;
	uint i2 = asuint(y2) >> 30U;
	uint i3 = asuint(y3) >> 29U;

	uint shift = (i2 & 2U) | (i1 & ~2U);
	shift = (i3 & 4U) | (shift & ~4U);

	// Eligibility is returned in bits 0 and 8.

	return ((0x2E74U >> shift) & 0x0101U);
}

float2 SolveHorizPoly(float4 p12, float2 p3)
{
	// Solve for the values of t where the curve crosses y = 0.
	// The quadratic polynomial in t is given by
	//
	//     a t^2 - 2b t + c,
	//
	// where a = p1.y - 2 p2.y + p3.y, b = p1.y - p2.y, and c = p1.y.
	// The discriminant b^2 - ac is clamped to zero, and imaginary
	// roots are treated as a double root at the global minimum
	// where t = b / a.

	float2 a = p12.xy - p12.zw * 2.0 + p3;
	float2 b = p12.xy - p12.zw;
	float ra = 1.0 / a.y;
	float rb = 0.5 / b.y;

	float d = sqrt(max(b.y * b.y - a.y * p12.y, 0.0));
	float t1 = (b.y - d) * ra;
	float t2 = (b.y + d) * ra;

	// If the polynomial is nearly linear, then solve -2b t + c = 0.

	if (abs(a.y) < 1.0 / 65536.0) t1 = t2 = p12.y * rb;

	// Return the x coordinates where C(t) = 0.

	return (float2((a.x * t1 - b.x * 2.0) * t1 + p12.x, (a.x * t2 - b.x * 2.0) * t2 + p12.x));
}

float2 SolveVertPoly(float4 p12, float2 p3)
{
	// Solve for the values of t where the curve crosses x = 0.

	float2 a = p12.xy - p12.zw * 2.0 + p3;
	float2 b = p12.xy - p12.zw;
	float ra = 1.0 / a.x;
	float rb = 0.5 / b.x;

	float d = sqrt(max(b.x * b.x - a.x * p12.x, 0.0));
	float t1 = (b.x - d) * ra;
	float t2 = (b.x + d) * ra;

	// If the polynomial is nearly linear, then solve -2b t + c = 0.

	if (abs(a.x) < 1.0 / 65536.0) t1 = t2 = p12.x * rb;

	// Return the y coordinates where C(t) = 0.

	return (float2((a.y * t1 - b.y * 2.0) * t1 + p12.y, (a.y * t2 - b.y * 2.0) * t2 + p12.y));
}

int2 CalcBandLoc(int2 glyphLoc, uint offset)
{
	// If the offset causes the x coordinate to exceed the texture width, then wrap to the next line.

	int2 bandLoc = int2(glyphLoc.x + int(offset), glyphLoc.y);
	bandLoc.y += bandLoc.x >> kLogBandTextureWidth;
	bandLoc.x &= (1 << kLogBandTextureWidth) - 1;
	return (bandLoc);
}

float CalcCoverage(float xcov, float ycov, float xwgt, float ywgt, int flags)
{
	// Combine coverages from the horizontal and vertical rays using their weights.
	// Absolute values ensure that either winding direction convention works.

	float coverage = max(abs(xcov * xwgt + ycov * ywgt) / max(xwgt + ywgt, 1.0 / 65536.0), min(abs(xcov), abs(ycov)));

	// If SLUG_EVENODD is defined during compilation, then check E flag in tex.w. (See vertex shader.)

	#if defined(SLUG_EVENODD)

		if ((flags & 0x1000) == 0)
		{

	#endif

			// Using nonzero fill rule here.

			coverage = saturate(coverage);

	#if defined(SLUG_EVENODD)

		}
		else
		{
			// Using even-odd fill rule here.

			coverage = 1.0 - abs(1.0 - frac(coverage * 0.5) * 2.0);
		}

	#endif

	// If SLUG_WEIGHT is defined during compilation, then take a square root to boost optical weight.

	#if defined(SLUG_WEIGHT)

		coverage = sqrt(coverage);

	#endif

	return (coverage);
}

float SlugRender(Texture2D curveData, Texture2D<uint4> bandData, float2 renderCoord, float4 bandTransform, int4 glyphData)
{
	int curveIndex;

	// The effective pixel dimensions of the em square are computed
	// independently for x and y directions with texcoord derivatives.

	float2 emsPerPixel = fwidth(renderCoord);
	float2 pixelsPerEm = 1.0 / emsPerPixel;

	int2 bandMax = glyphData.zw;
	bandMax.y &= 0x00FF;

	// Determine what bands the current pixel lies in by applying a scale and offset
	// to the render coordinates. The scales are given by bandTransform.xy, and the
	// offsets are given by bandTransform.zw. Band indexes are clamped to [0, bandMax.xy].

	int2 bandIndex = clamp(int2(renderCoord * bandTransform.xy + bandTransform.zw), int2(0, 0), bandMax);
	int2 glyphLoc = glyphData.xy;

	float xcov = 0.0;
	float xwgt = 0.0;

	// Fetch data for the horizontal band from the index texture. The number
	// of curves intersecting the band is in the x component, and the offset
	// to the list of locations for those curves is in the y component.

	uint2 hbandData = TexelLoad2D(bandData, int2(glyphLoc.x + bandIndex.y, glyphLoc.y)).xy;
	int2 hbandLoc = CalcBandLoc(glyphLoc, hbandData.y);

	// Loop over all curves in the horizontal band.

	for (curveIndex = 0; curveIndex < int(hbandData.x); curveIndex++)
	{
		// Fetch the location of the current curve from the index texture.

		int2 curveLoc = int2(TexelLoad2D(bandData, int2(hbandLoc.x + curveIndex, hbandLoc.y)).xy);

		// Fetch the three 2D control points for the current curve from the curve texture.
		// The first texel contains both p1 and p2 in the (x,y) and (z,w) components, respectively,
		// and the the second texel contains p3 in the (x,y) components. Subtracting the render
		// coordinates makes the curve relative to the sample position. The quadratic Bézier curve
		// C(t) is given by
		//
		//     C(t) = (1 - t)^2 p1 + 2t(1 - t) p2 + t^2 p3

		float4 p12 = TexelLoad2D(curveData, curveLoc) - float4(renderCoord, renderCoord);
		float2 p3 = TexelLoad2D(curveData, int2(curveLoc.x + 1, curveLoc.y)).xy - renderCoord;

		// If the largest x coordinate among all three control points falls
		// left of the current pixel, then there are no more curves in the
		// horizontal band that can influence the result, so exit the loop.
		// (The curves are sorted in descending order by max x coordinate.)

		if (max(max(p12.x, p12.z), p3.x) * pixelsPerEm.x < -0.5) break;

		uint code = CalcRootCode(p12.y, p12.w, p3.y);
		if (code != 0U)
		{
			// At least one root makes a contribution. Calculate them and scale so
			// that the current pixel corresponds to the range [0,1].

			float2 r = SolveHorizPoly(p12, p3) * pixelsPerEm.x;

			// Bits in code tell which roots make a contribution.

			if ((code & 1U) != 0U)
			{
				xcov += saturate(r.x + 0.5);
				xwgt = max(xwgt, saturate(1.0 - abs(r.x) * 2.0));
			}

			if (code > 1U)
			{
				xcov -= saturate(r.y + 0.5);
				xwgt = max(xwgt, saturate(1.0 - abs(r.y) * 2.0));
			}
		}
	}

	float ycov = 0.0;
	float ywgt = 0.0;

	// Fetch data for the vertical band from the index texture. This follows
	// the data for all horizontal bands, so we have to add bandMax.y + 1.

	uint2 vbandData = TexelLoad2D(bandData, int2(glyphLoc.x + bandMax.y + 1 + bandIndex.x, glyphLoc.y)).xy;
	int2 vbandLoc = CalcBandLoc(glyphLoc, vbandData.y);

	// Loop over all curves in the vertical band.

	for (curveIndex = 0; curveIndex < int(vbandData.x); curveIndex++)
	{
		int2 curveLoc = int2(TexelLoad2D(bandData, int2(vbandLoc.x + curveIndex, vbandLoc.y)).xy);
		float4 p12 = TexelLoad2D(curveData, curveLoc) - float4(renderCoord, renderCoord);
		float2 p3 = TexelLoad2D(curveData, int2(curveLoc.x + 1, curveLoc.y)).xy - renderCoord;

		// If the largest y coordinate among all three control points falls
		// below the current pixel, then there are no more curves in the
		// vertical band that can influence the result, so exit the loop.
		// (The curves are sorted in descending order by max y coordinate.)

		if (max(max(p12.y, p12.w), p3.y) * pixelsPerEm.y < -0.5) break;

		uint code = CalcRootCode(p12.x, p12.z, p3.x);
		if (code != 0U)
		{
			float2 r = SolveVertPoly(p12, p3) * pixelsPerEm.y;

			if ((code & 1U) != 0U)
			{
				ycov -= saturate(r.x + 0.5);
				ywgt = max(ywgt, saturate(1.0 - abs(r.x) * 2.0));
			}

			if (code > 1U)
			{
				ycov += saturate(r.y + 0.5);
				ywgt = max(ywgt, saturate(1.0 - abs(r.y) * 2.0));
			}
		}
	}

	return (CalcCoverage(xcov, ycov, xwgt, ywgt, glyphData.w));
}

struct VertexStruct
{
	float4 position : SV_Position;					// Clip-space vertex position.
	float4 color : U_COLOR;							// Vertex color.
	float2 texcoord : U_TEXCOORD;					// Em-space sample coordinates.
	nointerpolation float4 banding : U_BANDING;		// Band scale and offset, constant over glyph.
	nointerpolation int4 glyph : U_GLYPH;			// (glyph data x coord, glyph data y coord, band max x, band max y and flags), constant over glyph.
};

Texture2D curveTexture : register(t0);				// Control point texture.
Texture2D<uint4> bandTexture : register(t1);		// Band data texture.

float4 main(VertexStruct vresult) : SV_Target
{
	float coverage = SlugRender(curveTexture, bandTexture, vresult.texcoord, vresult.banding, vresult.glyph);
	return (vresult.color * coverage);
}
