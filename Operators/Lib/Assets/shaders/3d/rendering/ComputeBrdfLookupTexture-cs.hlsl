// Physically Based Rendering
// Copyright (c) 2017-2018 Michał Siejak

// Pre-integrates Cook-Torrance specular BRDF for varying roughness and viewing directions.
// Results are saved into 2D LUT texture in the form of DFG1 and DFG2 split-sum approximation terms,
// which act as a scale and bias to F0 (Fresnel reflectance at normal incidence) during rendering.

static const float PI = 3.141592;
static const float TwoPI = 2 * PI;
static const float Epsilon = 0.001; // This program needs larger eps.

static const uint NumSamples = 1024;
static const float InvNumSamples = 1.0 / float(NumSamples);

RWTexture2D<float4> LUT : register(u0);

// Compute Van der Corput radical inverse
// See: http://holger.dammertz.org/stuff/notes_HammersleyOnHemisphere.html
float radicalInverse_VdC(uint bits)
{
	bits = (bits << 16u) | (bits >> 16u);
	bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
	bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
	bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
	bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
	return float(bits) * 2.3283064365386963e-10; // / 0x100000000
}

// Sample i-th point from Hammersley point set of NumSamples points total.
float2 sampleHammersley(uint i)
{
	return float2(i * InvNumSamples, radicalInverse_VdC(i));
}

// Importance sample GGX normal distribution function for a fixed roughness value.
// This returns normalized half-vector between Li & Lo.
// For derivation see: http://blog.tobias-franke.eu/2014/03/30/notes_on_importance_sampling.html
float3 sampleGGX(float u1, float u2, float roughness)
{
	float alpha = roughness * roughness;

	float cosTheta = sqrt((1.0 - u2) / (1.0 + (alpha*alpha - 1.0) * u2));
	float sinTheta = sqrt(1.0 - cosTheta*cosTheta); // Trig. identity
	float phi = TwoPI * u1;

	// Convert to Cartesian upon return.
	return float3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);
}

// Single term for separable Schlick-GGX below.
float gaSchlickG1(float cosTheta, float k)
{
	return cosTheta / (cosTheta * (1.0 - k) + k);
}

// Schlick-GGX approximation of geometric attenuation function using Smith's method (IBL version).
float gaSchlickGGX_IBL(float cosLi, float cosLo, float roughness)
{
	float r = roughness;
	float k = (r * r) / 2.0; // Epic suggests using this roughness remapping for IBL lighting.
	return gaSchlickG1(cosLi, k) * gaSchlickG1(cosLo, k);
}

[numthreads(32, 32, 1)]
void main(uint2 ThreadID : SV_DispatchThreadID)
{
	// Get output LUT dimensions.
	float outputWidth, outputHeight;
	LUT.GetDimensions(outputWidth, outputHeight);

	// Get integration parameters.
	float cosLo = ThreadID.x / outputWidth;
	float roughness = ThreadID.y / outputHeight;

	// Make sure viewing angle is non-zero to avoid divisions by zero (and subsequently NaNs).
	cosLo = max(cosLo, Epsilon);

	// Derive tangent-space viewing vector from angle to normal (pointing towards +Z in this reference frame).
	float3 Lo = float3(sqrt(1.0 - cosLo*cosLo), 0.0, cosLo);

	// We will now pre-integrate Cook-Torrance BRDF for a solid white environment and save results into a 2D LUT.
	// DFG1 & DFG2 are terms of split-sum approximation of the reflectance integral.
	// For derivation see: "Moving Frostbite to Physically Based Rendering 3.0", SIGGRAPH 2014, section 4.9.2.
	float DFG1 = 0;
	float DFG2 = 0;

	for(uint i=0; i<NumSamples; ++i) {
		float2 u = sampleHammersley(i);

		// Sample directly in tangent/shading space since we don't care about reference frame as long as it's consistent.
		float3 Lh = sampleGGX(u.x, u.y, roughness);

		// Compute incident direction (Li) by reflecting viewing direction (Lo) around half-vector (Lh).
		float3 Li = 2.0 * dot(Lo, Lh) * Lh - Lo;

		float cosLi   = Li.z;
		float cosLh   = Lh.z;
		float cosLoLh = max(dot(Lo, Lh), 0.0);

		if(cosLi > 0.0) {
			float G  = gaSchlickGGX_IBL(cosLi, cosLo, roughness);
			float Gv = G * cosLoLh / (cosLh * cosLo);
			float Fc = pow(1.0 - cosLoLh, 5);

			DFG1 += (1 - Fc) * Gv;
			DFG2 += Fc * Gv;
		}
	}

	LUT[ThreadID] = float4(float2(DFG1, DFG2) * InvNumSamples, 0, 1) ;
}
