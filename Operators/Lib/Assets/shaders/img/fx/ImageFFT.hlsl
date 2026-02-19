cbuffer IntParams : register(b0)
{
    uint2 TexSize;
    uint BufferSize;
    uint TotalSteps;
    uint IsInverse;
    uint Direction;
    uint Normalization;
}

cbuffer CallParams : register(b1)
{
    int CallIndex;
}

float2 cmul(float2 a,float2 b){return float2(a.x*b.x-a.y*b.y,a.y*b.x+a.x*b.y);}
float2 cexp(float2 z){return float2(cos(z.y),sin(z.y))*exp(z.x);}

Texture2D<float4> Image : register(t0);

RWStructuredBuffer<float4> DataBuffer : register(u0);

#define IFFT (IsInverse==1)

[numthreads(64,1,1)]
void main_fft(uint3 DTid : SV_DispatchThreadID)
{   
    uint numStructs, stride;
    DataBuffer.GetDimensions(numStructs, stride);
    // buffer 2x size of input texture
    if(DTid.x>= numStructs/2)return;

    // bool Vertical=(Direction==1);
    // uint StepCount=TotalSteps;
    // uint ipass=uint(CallIndex);

    uint2 StepsXY=uint2(log2(TexSize.xy));
    //Direction: 0=horizontal, 1=vertical, 2=horizontal+vertical
    bool Vertical=(Direction==1) || CallIndex>=StepsXY.x;
    uint StepCount=Vertical?StepsXY.y:StepsXY.x;
    uint ipass=(CallIndex>=StepsXY.x)?uint(CallIndex-StepsXY.x):uint(CallIndex);

    uint WriteOffset=((CallIndex%2)==1)*(TexSize.x*TexSize.y);
    uint ReadOffset=((CallIndex%2)==0)*(TexSize.x*TexSize.y);

    int2 ip=int2(DTid.x%TexSize.x,(DTid.x/TexSize.x)%TexSize.y);
    uint blocksize=1<<StepCount;
    if(Vertical)ip=ip.yx;
    int xblock=ip.x/blocksize;
    uint ix=ip.x%blocksize;
    uint iy=ip.y;
    uint n=2<<(ipass);
    uint oddg=(ix/(n/2))%2;
    uint2 xx=uint2(
    (ix/(n))*n+(ix%(n/2)),
    (ix/(n))*n+(ix%(n/2))+n/2
    );
    
//  input scrambler
    if(ipass==0){
        xx.x=reversebits(xx.x)>>(32-StepCount);
        xx.y=reversebits(xx.y)>>(32-StepCount);
    }
    xx+=blocksize*xblock;

    int2 ipe=int2(xx.x,iy);
    int2 ipo=int2(xx.y,iy);
    if(Vertical){
        ipe=int2(iy,xx.x);
        ipo=int2(iy,xx.y);
    }

    float4 pe=0;
    float4 po=0;
    if(CallIndex==0){
        pe=Image.Load(int3(ipe,0));
        po=Image.Load(int3(ipo,0));
    }else{
        pe=DataBuffer[ipe.x+ipe.y*TexSize.x+ReadOffset];
        po=DataBuffer[ipo.x+ipo.y*TexSize.x+ReadOffset];
    }
    float2 w=cexp(float2(0,(IFFT?1:-1)*acos(-1.0)*2.0*float(ix%(n))/float(n)));

    float4 c=float4(pe.xy+cmul(po.xy,w.xy),pe.zw+cmul(po.zw,w.xy));
    if(ipass==StepCount-1){
        
        //float k=1./sqrt(float(n));
        float k=Normalization==0
        ?1./sqrt(float(n))//ortho normalization
        :((IsInverse==1)^(Normalization==2))?1./float(n):1.0;//backward or forward normalization

        c*=k;
    }

    DataBuffer[DTid.x+WriteOffset]=c;
    
}
