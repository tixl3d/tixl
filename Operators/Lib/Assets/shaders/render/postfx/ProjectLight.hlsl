#include "shared/hash-functions.hlsl"


cbuffer ParamConstants : register(b0)
{
    float SurfaceIntensity;
    float RaysIntensity;
    float ShadowBias;
    float ShadowScale;

    float RaysDecay;
    float Time;
    float StepCount;
    float __padding;

    float4 AmbientColor;
    float4 LightColor;

}


cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> Image : register(t0);
Texture2D<float4> Image2 : register(t1);
Texture2D<float4> NormalBuffer : register(t2);
Texture2D<float4> ProjTex : register(t3);
Texture2D<float4> ProjDepth : register(t4);

sampler Sampler : register(s0);
sampler CustomSampler : register(s1);

//additional defines
//float4 rndz(int3 p, int s) {int4 c=int4(p.xyz,s);int r=(int(0x3504f333*c.x*c.x+c.y)*int(0xf1bbcdcb*c.y*c.y+c.x)*int(0xbf5c3da7*c.z*c.z+c.y)*int(0x2eb164b3*c.w*c.w+c.z));
//int4 r4=int4(0xbf5c3da7,0xa4f8e125,0x9284afeb,0xe4f5ae21)*r;return (float4(r4)*(2.0/8589934592.0)+0.5)*0.99999;}



cbuffer TransformsCam1 : register(b2)
{
    float4x4 CameraToClipSpace;
    float4x4 ClipSpaceToCamera;
    float4x4 WorldToCamera;
    float4x4 CameraToWorld;
    float4x4 WorldToClipSpace;
    float4x4 ClipSpaceToWorld;
    float4x4 ObjectToWorld;
    float4x4 WorldToObject;
    float4x4 ObjectToCamera;
    float4x4 ObjectToClipSpace;
};

cbuffer TransformsCam2 : register(b3)
{
    float4x4 p_CameraToClipSpace;
    float4x4 p_ClipSpaceToCamera;
    float4x4 p_WorldToCamera;
    float4x4 p_CameraToWorld;
    float4x4 p_WorldToClipSpace;
    float4x4 p_ClipSpaceToWorld;
    float4x4 p_ObjectToWorld;
    float4x4 p_WorldToObject;
    float4x4 p_ObjectToCamera;
    float4x4 p_ObjectToClipSpace;
};


float plaIntersect(float3 ro, float3 rd, float4 p )
{
    return -(dot(ro,p.xyz)+p.w)/dot(rd,p.xyz);
}
float2 boxIntersection(float3 ro,float3 rd, float3 boxSize) 
{
    float3 m = 1.0/rd; // can precompute if traversing a set of aligned boxes
    float3 n = m*ro;   // can precompute if traversing a set of aligned boxes
    float3 k = abs(m)*boxSize;
    float3 t1 = -n - k;
    float3 t2 = -n + k;
    float tN = max( max( t1.x, t1.y ), t1.z );
    float tF = min( min( t2.x, t2.y ), t2.z );
    if( tN>tF || tF<0.0) return -1; // no intersection
    return float2( tN, tF );
}
void frustumPos(float3 ro,float3 rd, out float3 front, out float3 back,out bool hit){
    // float4 near=p_WorldToClipSpace[i][3] + p_WorldToClipSpace[i][2];
float4 near=0;
    for (int i = 4; i--; ) { near[i]   = p_WorldToClipSpace[i][3] + p_WorldToClipSpace[i][2]; }

    float pt=plaIntersect(ro,rd,near);
    bool cz=mul(float4(ro,1),p_WorldToCamera).z>0;
    if(pt>0&&cz)ro+=rd*pt;
    float3 ofs=float3(0,0,.5);
    float4 ro1=mul(float4(ro,1),p_WorldToClipSpace);
    float4 re1=mul(float4(ro+rd,1),p_WorldToClipSpace);
    float3 rd1=normalize(re1.xyz/re1.w-ro1.xyz/ro1.w);

    ro1=ro1/ro1.w;

    float2 bi=boxIntersection(ro1.xyz-ofs,rd1,float3(1,1,.49995));
    float4 rpf=mul(float4(ro1.xyz+rd1*bi.x-ofs*0,1),p_ClipSpaceToWorld);

    front=rpf.xyz/rpf.w;
    float4 rpb=mul(float4(ro1.xyz+rd1*bi.y-ofs*0,1),p_ClipSpaceToWorld);
    back=rpb.xyz/rpb.w;

    hit=bi.y>0;
    if(bi.x<0&&bi.y>0)front=ro;
    if(pt>800&&cz)hit=0;
}

void ProjectLight(
    float2 uv,
    out float4 SurfaceColor,
    out float4 RaysColor,
    out float4 DebugColor,
    int MaxIterations,
    int RandomSeed=0,
    float RaysDecay=2
    ){

    int2 TargetSize=int2(TargetWidth,TargetHeight);
    int2 PixelCoord=int2(round(uv*TargetSize-.25));

    SurfaceColor=0;
    RaysColor=0;

    float depth = Image2.SampleLevel(Sampler, uv,0).r;
    depth = min(depth, 0.99999);
    float4 viewTFragPos = float4((uv*2-1)*float2(1,-1), depth, 1.0);
    float4 vp=mul(viewTFragPos, ClipSpaceToCamera);
    float4 wp=mul(vp/vp.w, CameraToWorld);

    float3 n=NormalBuffer.SampleLevel(Sampler, uv,0).xyz;

    //wp.xyz+=n*A;
    float4 pvp=mul(wp,p_WorldToCamera);
    float4 pcp=mul(pvp,p_CameraToClipSpace);
    float2 puv=pcp.xy/pcp.w*0.5*float2(1,-1)+0.5;

    float3 pn=mul(n,(float3x3)p_WorldToCamera);

    float fresnel =  dot(pn, normalize( pvp.xyz));

    bool hit=1;
    hit=pn.z>0;
    float4 smp=ProjTex.SampleLevel(Sampler, puv,0);

    //float mask=smp.a * pow(saturate(pn.z),.5);
    float mask = saturate(-fresnel);
    //mask = 1;
    //mask = 10;
    

    float sampleDepth = ProjDepth.SampleLevel(CustomSampler, puv,0).x;

    float4 csp= mul(float4(wp.xyz,1), p_WorldToClipSpace);
    float2 suv=(csp.xy/csp.w)*float2(1,-1)*.5+.5;
    float zs=csp.z/csp.w;
    smp.xyz*=smoothstep(exp2(-30)+ShadowScale*exp2(-20),0,zs-sampleDepth-ShadowBias*exp2(-15));

    SurfaceColor=smp*(hit&&all(abs(puv-.5)<.5)) * mask;

    float4 sum=0;
    float3 ro=CameraToWorld[3].xyz;
    float3 rd=normalize(wp.xyz-ro);
    float3 p=ro;
    int iter=MaxIterations;
    float3 rnd1=hash33u(int3(PixelCoord.xy,1+RandomSeed)); 

    float3 front;
    float3 back;
    bool fhit;
    frustumPos(ro,rd,front,back,fhit);
    float3 pstart=ro;

    float3 pend=wp.xyz;
    pend=length(pend-ro)<length(back-ro)?pend:back;

    pstart=front;
    float seglength=length(pstart-pend);
    if(fhit){
        for(int i=0;i<iter;i++){
            float lf=(float(i)+rnd1.x)/iter;
            //p=lerp(ro,wp.xyz,lf);
            //p=lerp(front,back,lf);
            p=lerp(pstart,pend,lf);
            float4 cp=mul(float4(p.xyz,1), p_WorldToClipSpace);
            float2 cuv=(cp.xy/cp.w)*float2(1,-1)*.5+.5;
            float msk=all(abs(cuv-.5)<.5);
            //sum.xyz+=frac(p*8)*msk;
            float sdc = ProjDepth.SampleLevel(CustomSampler, cuv,0).x;
            float zsc=cp.z/cp.w;
            msk*=smoothstep(.00001,0,zsc-sdc);
            float4 smpc=ProjTex.SampleLevel(Sampler, cuv,0+.5*log2(seglength*0+1));
            float fd=1/pow(1+.25*length(p-p_CameraToWorld[3].xyz),RaysDecay);
            sum.xyz+=smpc.xyz*msk*fd;
        }
    }

    RaysColor=float4(sum.xyz*seglength/iter,1);
    front=mul(float4(front.xyz,1),p_WorldToCamera).xyz;
    back=mul(float4(back.xyz,1),p_WorldToCamera).xyz;

    DebugColor=float4(0,0,0,fhit);

    DebugColor.rgb+=.1*(abs(frac(back*5)*2-1)>.9)*fhit;
    DebugColor.rgb+=.1*(abs(frac(front*5)*2-1)>.9)*fhit;

}


float4 psMain(vsOutput input) : SV_TARGET
{
    float width, height;
    Image.GetDimensions(width, height);
    float4 c=float4(1,1,0,1);
   
    float2 uv = input.texCoord;
    int2 TargetSize=int2(TargetWidth,TargetHeight);
    int2 PixelCoord=int2(round(uv*TargetSize-.25));
    
    //code snippet
    float4 surfaceColor;
    float4 raysColor;
    float4 debugColor;
    int RandomSeed=int(Time*10000);

    int steps = clamp(StepCount,1,400);

    ProjectLight(uv,surfaceColor,raysColor,debugColor,steps,RandomSeed,RaysDecay);

    c=Image.SampleLevel(Sampler, uv,0);

    c.rgb=(c.rgb*(surfaceColor.rgb * SurfaceIntensity) + raysColor.rgb*RaysIntensity) 
    * LightColor.rgb + c.rgb* AmbientColor.rgb;
    return c;
}