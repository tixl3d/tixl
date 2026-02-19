/*
    The MIT License (MIT)
    
    Copyright (c) 2004-2019 Microsoft Corp
    Modified by Carl Emil Carlsen 2018.
    
    Permission is hereby granted, free of charge, to any person obtaining a copy of this
    software and associated documentation files (the "Software"), to deal in the Software
    without restriction, including without limitation the rights to use, copy, modify,
    merge, publish, distribute, sublicense, and/or sell copies of the Software, and to
    permit persons to whom the Software is furnished to do so, subject to the following
    conditions:
    
    The above copyright notice and this permission notice shall be included in all copies
    or substantial portions of the Software.
    
    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
    INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
    PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
    HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
    CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE
    OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
        
    From directx-sdk-samples by Chuck Walbourn:
    https://github.com/walbourn/directx-sdk-samples/blob/master/AdaptiveTessellationCS40/ScanCS.hlsl
*/
    
// #pragma kernel ScanInBucketInclusive
// #pragma kernel ScanInBucketExclusive
// #pragma kernel ScanBucketResult
// #pragma kernel ScanAddBucketResult
    
#define THREADS_PER_GROUP 512 // Ensure that this equals the 'threadsPerGroup' const in the host script.
    
StructuredBuffer<uint> _Input;
RWStructuredBuffer<uint> _Result;
    
groupshared uint2 bucket[THREADS_PER_GROUP];
    
void CSScan( uint3 DTid, uint GI, uint x )
{
    // since CS40 can only support one shared memory for one shader, we use .xy and .zw as ping-ponging buffers
    // if scan a single element type like int, search and replace all .xy to .x and .zw to .y below
    bucket[GI].x = x;
    bucket[GI].y = 0;
    
    // Up sweep  
    [unroll]
    for( uint stride = 2; stride <= THREADS_PER_GROUP; stride <<= 1 )
    {
        GroupMemoryBarrierWithGroupSync();
        if ( (GI & (stride - 1)) == (stride - 1) ) bucket[GI].x += bucket[GI - stride/2].x;
    }
    
    if( GI == (THREADS_PER_GROUP - 1) ) bucket[GI].x = 0;
    
    // Down sweep
    bool n = true;
    [unroll]
    for( stride = THREADS_PER_GROUP / 2; stride >= 1; stride >>= 1 )
    {
        GroupMemoryBarrierWithGroupSync();
    
        uint a = stride - 1;
        uint b = stride | a;
    
        if( n )        // ping-pong between passes
        {
            if( ( GI & b) == b )
            {
                bucket[GI].y = bucket[GI-stride].x + bucket[GI].x;
            } else
            if( (GI & a) == a )
            {
                bucket[GI].y = bucket[GI+stride].x;
            } else      
            {
                bucket[GI].y = bucket[GI].x;
            }
        } else {
            if( ( GI & b) == b )
            {
                bucket[GI].x = bucket[GI-stride].y + bucket[GI].y;
            } else
            if( (GI & a) == a )
            {
                bucket[GI].x = bucket[GI+stride].y;
            } else      
            {
                bucket[GI].x = bucket[GI].y;
            }
        }
        
        n = !n;
    }  
    
    _Result[DTid.x] = bucket[GI].y + x;
}
    
    
// Scan in each bucket.
[numthreads( THREADS_PER_GROUP, 1, 1 )]
void ScanInBucketInclusive( uint DTid : SV_DispatchThreadID, uint GI: SV_GroupIndex ) // CSScanInBucket
{
    uint x = _Input[DTid];
    CSScan( DTid, GI, x );
}
    
// Scan in each bucket.
[numthreads( THREADS_PER_GROUP, 1, 1 )]
void ScanInBucketExclusive( uint DTid : SV_DispatchThreadID, uint GI: SV_GroupIndex ) // CSScanInBucket
{
    uint x = DTid == 0 ? 0 : _Input[ DTid-1 ];
    CSScan( DTid, GI, x );
}
    
    
// Record and scan the sum of each bucket.
[numthreads( THREADS_PER_GROUP, 1, 1 )]
void ScanBucketResult( uint DTid : SV_DispatchThreadID, uint GI: SV_GroupIndex )
{
    uint x = _Input[DTid*THREADS_PER_GROUP - 1];
    CSScan( DTid, GI, x );
}
    
    
// Add the bucket scanned result to each bucket to get the final result.
[numthreads( THREADS_PER_GROUP, 1, 1 )]
void ScanAddBucketResult( uint Gid : SV_GroupID, uint3 DTid : SV_DispatchThreadID )
{
    _Result[DTid.x] = _Result[DTid.x] + _Input[Gid];
}
