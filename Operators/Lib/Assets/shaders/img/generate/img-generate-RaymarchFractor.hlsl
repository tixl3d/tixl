cbuffer ParamConstants : register(b0)
{
    float4 Fill;
    float4 Background;
    float2 Size;
    float2 Position;
    float Round;
    float Feather;
    float GradientBias;
}

cbuffer TimeConstants : register(b1)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> ImageA : register(t0);
sampler texSampler : register(s0);

#define mod(x, y) (x - y * floor(x / y))
float IsBetween( float value, float low, float high) {
    return (value >= low && value <= high) ? 1:0;
}

// float2 max2(float2 p) {
//     return float2( 
//         abs(p.x),
//         abs(p.y)
//     );
// }


float sdBox( in float2 p, in float2 b )
{
    float2 d = abs(p)-b;
    return length(
        max(d,float2(0,0))) + min(max(d.x,d.y),
        0.0);
}

float4 psMain(vsOutput psInput) : SV_TARGET
{    
    float2 p = psInput.texCoord;

    float4 orgColor = ImageA.Sample(texSampler, psInput.texCoord);

    p  = p *2.-1.;
    p-=Position;
    
    float d = sdBox(p, Size);
    
    float dBiased = GradientBias>= 0 
        ? pow( d, GradientBias+1)
        : 1-pow( clamp(1-d,0,10), -GradientBias+1);

    d = smoothstep(Round, Round+Feather, dBiased);
    float4 c= lerp(Fill, Background,  d);

    float a = clamp(orgColor.a + c.a - orgColor.a*c.a, 0,1);
    float3 rgb = (1.0 - c.a)*orgColor.rgb + c.a*c.rgb;   
    return float4(rgb,a);
}


//---------------------------------------------


//>>> _common parameters
float4x4 objectToWorldMatrix;
float4x4 worldToCameraMatrix;
float4x4 projMatrix;
Texture2D txDiffuse;
float2 RenderTargetSize;
//<<< _common parameters

//>>> _parameters
float Minrad;
float Scale;
float3 Clamping;
float2 Fold;
float3 Increment;
float MaxSteps;
float StepSize;
float MinDistance;
float MaxDistance;
float DistToColor;
float4 Surface1;
float4 Surface2;
float4 Surface3;
float4 Diffuse;
float4 Specular;
float2 Spec;
float4 Glow;
float4 AmbientOcclusion;
float AODistance;
float4 Background;
float Fog;
float3 LightPos;
float3 SpherePos;
float SphereRadius;
//<<< _parameters
float4x4 ViewToWorld;
 
//>>> setup
SamplerState samLinear
{
    Filter = MIN_MAG_MIP_LINEAR;
    AddressU = Clamp;
    AddressV = Clamp;
};
//<<< setup

//>>> declarations
struct VS_IN
{
    float4 pos : POSITION;
    float2 texCoord : TEXCOORD;
};

struct PS_IN
{
    float4 pos : SV_POSITION;
    float2 texCoord: TEXCOORD0;	    
    float3 worldTViewPos: TEXCOORD1;
    float3 worldTViewDir: TEXCOORD2;
};
//<<< declarations

//>>> _GS

//<<< _GS

//>>> VS 
PS_IN VS( VS_IN input )
{
    PS_IN output = (PS_IN)0;
    input.pos = mul(input.pos, objectToWorldMatrix); 
    output.pos = mul(input.pos, worldToCameraMatrix);
    output.pos = mul(output.pos, projMatrix);
    output.texCoord = input.texCoord;

    float4 viewTNearFragPos = float4(input.texCoord.x*2.0 - 1.0, -input.texCoord.y*2.0 + 1.0, 0.0, 1.0);
    float4 worldTNearFragPos = mul(viewTNearFragPos, ViewToWorld);
    worldTNearFragPos /= worldTNearFragPos.w;

    float4 viewTFarFragPos = float4(input.texCoord.x*2.0 - 1.0, -input.texCoord.y*2.0 + 1.0, 1.0, 1.0);
    float4 worldTFarFragPos = mul(viewTFarFragPos, ViewToWorld);
    worldTFarFragPos /= worldTFarFragPos.w;

    output.worldTViewDir = normalize(worldTFarFragPos.xyz - worldTNearFragPos.xyz);

    output.worldTViewPos = worldTNearFragPos;
    return output;
}
//<<< VS

//>>> PS

int iters = 4;

float sphereD(float3 p0) {
    return length(p0+float4(SpherePos.xyz,1))-SphereRadius;
}

float udCubes(float3 p) {
    float3 b= float3(1,10,0.2);
    float3 c= float3(SpherePos.x,SpherePos.y,SpherePos.z);
    float3 q = fmod(p,c)-0.5*c;
    float3  di = abs(p) - b;
    //float mc = maxcomp(di);
    //return min(mc,length(max(di,0.0)));  
    float rad=SphereRadius;
    return length( max( abs(q) - b + float3(rad,rad,rad), 0.0 ) ) - rad;
    //return length(max(abs(p)-b,0.0));
    
}

float BOX_RADIUS= 0.005; //0.015
float dBox(float3 p, float3 b) {
    return length( max( abs(p) - b + float3(BOX_RADIUS,BOX_RADIUS,BOX_RADIUS), 0.0 ) ) - BOX_RADIUS;
}


float3 H_BAR=float3(0.5, 0.1, 0.5);
float3 I_BAR=float3(0.1, 0.5, 0.5);
float3 S_BAR=float3(0.1, 0.25, 0.5);

float dStillLogo(float3 p) {
    float d;
    
    d    =  dBox(p- float3(-1.65,-0.4, 0), H_BAR);
    d=min(d,dBox(p- float3(-1.65, 0 ,0), H_BAR));
    d=min(d,dBox(p- float3(-1.65,+0.40,0), H_BAR));    
    d=min(d,dBox(p- float3(-2.05,+0.2,0), S_BAR));    
    d=min(d,dBox(p- float3(-1.25,-0.20,0), S_BAR));    
    
    d=min(d,dBox(p- float3(-0.75, +0.40,0), H_BAR));    
    d=min(d,dBox(p- float3(-0.75, 0,0), I_BAR));    
    
    d=min(d,dBox(p- float3(0, 0,0), I_BAR));    
    
    d=min(d,dBox(p- float3(+0.35, 0,0), I_BAR));  
    d=min(d,dBox(p- float3(+0.75,-0.40,0), H_BAR));    
    
    d=min(d,dBox(p- float3(+1.5, 0,0), I_BAR));  
    d=min(d,dBox(p- float3(+2,-0.40,0), H_BAR));    
    
    d=min(d, dBox(p- float3(3.3,0,0), float3(0.5,0.5,0.5)));
    
    //float d=  length( max( abs(p) - float3(1,1,1) + float3(BOX_RADIUS,BOX_RADIUS,BOX_RADIUS), 0.0 ) ) - BOX_RADIUS;
    return d;
}

float3 SH_BAR=float3(0.8, 0.01, 0.08);
float3 SI_BAR=float3(0.1, 0.5, 0.5);
float3 SS_BAR=float3(0.1, 0.25, 0.5);

float dStillLogo2(float3 p) {
    float d;

    d=dBox(p- float3(3.3,0,0), float3(0.5,0.5,0.5));

    //d    =  dBox(p- float3(-1.65,-0.4, 0), SH_BAR);
    //S
    d=max(d,-dBox(p- float3(3.3, -0.02 ,0.05), SH_BAR));
    d=max(d,-dBox(p- float3(3.3, 0    , 0.05), SH_BAR));    
    d=max(d,-dBox(p- float3(3.3, 0.02 , 0.05), SH_BAR));

    //q
    d=max(d,-dBox(p- float3(3.3, -0.02 ,0.25), SH_BAR));       
    d=max(d,-dBox(p- float3(3.3, 0.02  ,0.25), SH_BAR));

    //q
    //d=max(d,-dBox(p- float3(3.3, -0.02 ,0.25), SH_BAR));       
    //d=max(d,-dBox(p- float3(3.3, 0.02  ,0.25), SH_BAR));

/*
d=min(d,dBox(p- float3(-1.65,+0.40,0), H_BAR));    
    d=min(d,dBox(p- float3(-2.05,+0.2,0), S_BAR));    
    d=min(d,dBox(p- float3(-1.25,-0.20,0), S_BAR));    
    
    d=min(d,dBox(p- float3(-0.75, +0.40,0), H_BAR));    
    d=min(d,dBox(p- float3(-0.75, 0,0), I_BAR));    
    
    d=min(d,dBox(p- float3(0, 0,0), I_BAR));    
    
    d=min(d,dBox(p- float3(+0.35, 0,0), I_BAR));  
    d=min(d,dBox(p- float3(+0.75,-0.40,0), H_BAR));    
    
    d=min(d,dBox(p- float3(+1.5, 0,0), I_BAR));  
    d=min(d,dBox(p- float3(+2,-0.40,0), H_BAR));    
*/    
    
    //float d=  length( max( abs(p) - float3(1,1,1) + float3(BOX_RADIUS,BOX_RADIUS,BOX_RADIUS), 0.0 ) ) - BOX_RADIUS;
    return d;
}




// Compute the distance from |pos| to the Mandelbox.
float dMandelbox(float3 pos) {
    float4 pN = float4(pos,1);
    //return dStillLogo(pN);
  
    // precomputed constants
    float minRad2 = clamp(Minrad, 1.0e-9, 1.0);
    float4 scale = float4(Scale, Scale, Scale, abs(Scale)) / minRad2 ;
    float absScalem1 = abs(Scale - 1.0);
    float AbsScaleRaisedTo1mIters = pow(abs(Scale), float(1-iters));
    float DIST_MULTIPLIER = StepSize;

    float4 p = float4(pos,1);
    float4 p0 = p;  // p.w is the distance estimate
  

  for (int i=0; i<iters; i++) {
     //box folding: 
     //if (p>1) p = 2-p; else if (p<-1) p = -2-p;
    p.xyz = abs(1+p.xyz) - p.xyz - abs(1.0-p.xyz);  // add;add;abs.add;abs.add (130.4%)
    //p.xyz = clamp(p.xyz*0.5+0.5, 0.0, 1.0) * 4.0 - 2.0 - p.xyz;  // mad.sat;mad;add (102.3%)    
    //p.xyz = clamp(p.xyz, -1.0, 1.0) * 2.0 - p.xyz;  // min;max;mad    
    p.xyz = clamp(p.xyz, Clamping.x, Clamping.y) * Clamping.z - p.xyz;  // min;max;mad
    

    // sphere folding: if (r2 < minRad2) p /= minRad2; else if (r2 < 1.0) p /= r2;
    float r2 = dot(p.xyz, p.xyz);
    p *= clamp(max(minRad2/r2, minRad2), Fold.x, Fold.y);  // dp3,div,max.sat,mul
    p.xyz+= float3(Increment.x,Increment.y,Increment.z);
    // scale, translate
    p = p*scale + p0;
  }
  float d=((length(p.xyz) - absScalem1) / p.w - AbsScaleRaisedTo1mIters) * DIST_MULTIPLIER;
  //return d;
  //d=0.1;
  //return max(udCubes(p0),d );
  //return max( udCubes(p0), dStillLogo(p0));
  //return min(dStillLogo(pN), d);
    return d;
}

float getDistance(float3 p) {
    float d= dMandelbox(p);
    
    float dLogo= dStillLogo(p);
    dLogo= max(dBox( p + float3(SpherePos.x - SpherePos.y , 0,0), float3(SpherePos.y,3,3)), dLogo );
    
    return max(d, dLogo);  
    
}


// Blinn-Phong shading model with rim lighting (diffuse light bleeding to the other side).
// |normal|, |view| and |light| should be normalized.
float3 blinn_phong(float3 normal, float3 view, float3 light, float3 diffuseColor) {
  float3 halfLV = normalize(light + view);
  float spe = pow(max( dot(normal, halfLV), Spec.x ), Spec.y);
  float dif = dot(normal, light) * 0.1 + 0.15;
  return dif*diffuseColor + spe*Specular;
}





float3 getNormal(float3 p, float offset)
{
    float dt=.0001;
    float3 n=float3(getDistance(p+float3(dt,0,0)),
                    getDistance(p+float3(0,dt,0)),
                    getDistance(p+float3(0,0,dt)))-getDistance(p);
    return normalize(n);
}


float getAO(float3 aoposition, float3 aonormal, float aodistance, float aoiterations, float aofactor)
{
    float ao = 0.0;
    float k = aofactor;
    aodistance /= aoiterations;
    for (float i=1; i < 4; i += 1)
    {
        ao += (i * aodistance - getDistance(aoposition + aonormal * i * aodistance)) / pow(2,i);
    }
    return 1.0 - k * ao;
}
/*
float4 getTexture2(float3 p, float3 n) 
{
    float s = 1.3;
    float dx = abs(n.x);
    float dy = abs(n.y);
    float dz = abs(n.z);
    if (dx > dy  > dz) return  Image.Sample(samLinear, float2(frac(p.z*s), frac(p.y * s))); 
    if (dy > dz) return  Image.Sample(samLinear, float2(frac(p.x*s), frac(p.z * s)));
    return  Image.Sample(samLinear, float2(frac(p.x*s), frac(p.y * s)));
}*/

float MAX_DIST=300;

float3 surfaceColor1 = float3(0.95, 0.64, 0.1);
float3 surfaceColor2 = float3(0.89, 0.95, 0.75);
float3 surfaceColor3 = float3(0.55, 0.06, 0.03);

// Compute the color at |pos|.
float3 color(float3 pos) {
  float3 p = pos, p0 = p;
  float trap = 1.0;

  for (int i=0; i<3; i++) {
    p.xyz = clamp(p.xyz, -1.0, 1.0) * 2.0 - p.xyz;
    float r2 = dot(p.xyz, p.xyz);
    p *= clamp(max(Minrad/r2, Minrad), 0.0, 1.0);
    p = p*Scale + p0.xyz;
    trap = min(trap, r2);
  }
  // |c.x|: log final distance (fractional iteration count)
  // |c.y|: spherical orbit trap at (0,0,0)
  float2 c = clamp(float2( 0.33*log(dot(p,p))-1.0, sqrt(trap) ), 0.0, 1.0);

  return lerp(lerp(Surface1, Surface2, c.y), Surface3, c.x);
}


float4 PS( PS_IN input ) : SV_Target
{

    //float4 filter= Image2.Sample(samLinear, input.texCoord);
    float3 p = input.worldTViewPos;
    float3 dp = normalize(input.worldTViewDir);

  float totalD = 0.0;
  float D = 3.4e38;
  D=StepSize;
  float extraD = 0.0;
  float lastD;
  
  int steps;

    /*  
    // Intersect the view ray with the Mandelbox using raymarching.
    for (steps=0; steps<MaxSteps; steps++) {
        lastD = D;
        D = getDistance(p + totalD * dp);

        // Overstepping: have we jumped too far? Cancel last step.    
        if (extraD > 0.0 && D < extraD) {
            totalD -= extraD;
            extraD = 0.0; 
            D = 3.4e38;
            steps--;
            continue;
        }

        if (D < MinDistance/1000) break;

        totalD += D;

        // Overstepping is based on the optimal length of the last step.
        totalD += extraD = 0.096 * D*(D+extraD)/lastD;
    }
    */


    // SImple iterator
    
    
    for(steps=0;steps<MaxSteps && abs(D)>MinDistance/1000;steps++)
    {
        D=getDistance(p);
        p+=dp*D;	
    }
    
    
    p += totalD * dp;


  // Color the surface with Blinn-Phong shading, ambient occlusion and glow.
  float3 col = Background;
   float a=1;
  // We've got a hit or we're not sure.
  if (D < MAX_DIST) {
    float3 n = normalize( getNormal(p, D));
    //n*=float3(1,1,10);
    n= normalize(n);
    col = color(p);
    //col = blinn_phong(n, -dp, normalize(input.worldTViewPos+float3(10,1,0)+dp), col);
    col = blinn_phong(n, -dp, LightPos, col);
    //float getAO(float3 aoposition, float3 aonormal, float aodistance, float aoiterations, float aofactor)

    col = lerp(AmbientOcclusion, col, getAO(p,n,AODistance, 3, AmbientOcclusion.a));

    // We've gone through all steps, but we haven't hit anything.
    // Mix in the background color.
    if (D > MinDistance) {
        a=1-clamp(log(D/MinDistance) * DistToColor, 0.0, 1.0);
      col = lerp(col, Background, a);
    }
  }
  else {
  a=0;
  }

  // Glow is based on the number of steps.
  col = lerp(col, Glow, float(steps)/float(MaxSteps) * Glow.a);
  float f= clamp(log( length(p- input.worldTViewPos)/Fog), 0,1);
  col = lerp(col, Background, f);
  a*=(1-f*Background.a);
    //col = float3(1,1,0);
    //col.rgb=a;
  return float4(col, a);



}
//<<< PS

//>>> _technique
technique10 Render
{
    pass P0
    {
        SetGeometryShader( 0 );
        SetVertexShader( CompileShader( vs_4_0, VS() ) );
        SetPixelShader( CompileShader( ps_4_0, PS() ) );
    }
}
//<<< _technique
