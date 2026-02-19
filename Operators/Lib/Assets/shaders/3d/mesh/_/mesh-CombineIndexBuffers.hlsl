cbuffer Params : register(b0)
{
    int StartIndex;
    int StartVertex;
}

StructuredBuffer<int3> Indices : t0;
RWStructuredBuffer<int3> ResultIndices : u0;


[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint size, stride;
    Indices.GetDimensions(size, stride);

    if(i.x >= size)
        return;

    uint targetIndex = i.x + (int)StartIndex;

    int3 faceIndices =  Indices[i.x] +  StartVertex;
    ResultIndices[targetIndex] = faceIndices;
}
