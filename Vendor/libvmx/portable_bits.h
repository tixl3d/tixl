// Force-included into the baseline (non-AVX2) objects of libvmx for TiXL's Linux build.
// LZCNT/TZCNT run as BSR/BSF on CPUs without them and return different results (lzcnt(0) must be 64),
// so the baseline path uses zero-safe builtins instead; only the runtime-gated AVX2 object uses the instructions.
#pragma once
#include <x86intrin.h>
static inline unsigned long long vmx_portable_lzcnt64(unsigned long long v) { return v ? (unsigned long long)__builtin_clzll(v) : 64ULL; }
static inline unsigned long long vmx_portable_tzcnt64(unsigned long long v) { return v ? (unsigned long long)__builtin_ctzll(v) : 64ULL; }
#define __lzcnt64(v) vmx_portable_lzcnt64(v)
#define _tzcnt_u64(v) vmx_portable_tzcnt64(v)
