RWTexture2D<float4> outputTexture : register(u0);

Texture2D<float4> inputTexture : register(t0);
Texture2D<float4> inputTexture2 : register(t1);
Texture2D<float4> inputTexture3 : register(t2);

sampler texSampler : register(s0);

cbuffer TimeConstants : register(b0)
{
    float globalTime;
    float time;
    float2 dummy;   // @cynic was macht das? 
}

cbuffer ParamConstants : register(b1)
{
    float param1;
    float param2;
    float param3;
    float param4;
}


float4 Render(float2 uv) {
    return float4(uv.x, uv.y, 0,1);

}



/*
	Emin Kura - http://emin.me
    Raymarching from https://www.shadertoy.com/view/XsfGR8
*/
float3 rotate( float3 pos, float x, float y, float z )
{
	float3x3 rotX = float3x3( 1.0, 0.0, 0.0, 0.0, cos( x ), -sin( x ), 0.0, sin( x ), cos( x ) );
	float3x3 rotY = float3x3( cos( y ), 0.0, sin( y ), 0.0, 1.0, 0.0, -sin(y), 0.0, cos(y) );
	float3x3 rotZ = float3x3( cos( z ), -sin( z ), 0.0, sin( z ), cos( z ), 0.0, 0.0, 0.0, 1.0 );
return float3(1,1,1);
	//return rotX * rotY * rotZ * pos;
}

float hit( float3 r )
{
    //return 1;
    float iTime = 0;
	r = rotate( r, sin(iTime), cos(iTime), 0.0 );
	float3 zn = float3( r.xyz );
	float rad = 0.0;
	float hit = 0.0;
	float p = 8.0;
	float d = 1.0;
	for( int i = 0; i < 10; i++ )
	{
        rad = length( zn );

        if( rad > 2.0 )
        {	
            hit = 0.5 * log(rad) * rad / d;
        }
        else{
            float th = atan2( length( zn.xy ), zn.z );
            float phi = atan2( zn.y, zn.x );		
            float rado = pow(rad, 8.0);
            d = pow(rad, 7.0) * 7.0 * d + 1.0;
            
            float sint = sin( th * p );
            zn.x = rado * sint * cos( phi * p );
            zn.y = rado * sint * sin( phi * p );
            zn.z = rado * cos( th * p ) ;
            zn += r;
        }			
	}
	
	return hit;

}

float3 eps = float3( .1, 0.0, 0.0 );

float4 mainImage(float2 fragCoord )
{
	float2 pos = fragCoord;// -1.0 + 2.0 * fragCoord.xy/iResolution.xy;	

	//pos.x *= iResolution.x / iResolution.y;

	float3 ro = float3( pos, -1.2 );
	float3 la = float3( 0.0, 0.0, 1.0 );
	
	float3 cameraDir = normalize( la - ro );
	float3 cameraRight = normalize( cross( cameraDir, float3( 0.0, 1.0, 0.0 ) ) );
	float3 cameraUp = normalize( cross( cameraRight, cameraDir ) );
	

	float3 rd = normalize( cameraDir + float3( pos, 0.0 ) );

	float t = 0.0;
	float d = 200.0;
	
	float3 r;
	float3 color = float3(0,0,0);

	for( int i = 0; i < 1; i++ ){
		if( d > .001 )
		{	
			r = ro + rd * t;
			d = hit( r );
			t+=d;	
		}
	}

    float3 n = float3( hit( r + eps ) - hit( r - eps ),
            hit( r + eps.yxz ) - hit( r - eps.yxz ),
            hit( r + eps.zyx ) - hit( r - eps.zyx ) );
	 
	float3 mat = float3( .5, .1, .3 ); 
 	float3 light = float3( .5, .5, -2.0 );
	float3 lightCol = float3(.6, .4, .5);
	
	float3 ldir = normalize( light - r );
  	float3 diff = dot( ldir, n ) * lightCol * 60.0;
	
	color = diff  * mat;
    return float4( color.xyz, 1 );
}

float4 Render2(float2 uv) {
    //float4 color;
    return mainImage(uv);
}


[numthreads(16,16,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint width, height;    
    outputTexture.GetDimensions(width, height);

    float2 uv = (float2)i.xy / float2(width - 1, height - 1);
    // float4 colorFromImage = inputTexture3.SampleLevel(texSampler, uv, 0.0);
    
    // float4 grayB= pow(length(colorFromImage.rgb), 6);
    // float4 grayG= colorFromImage.ggga;
    
     //outputTexture[i.xy] = grayB  * (time %1);
     //return;

    // float4 result = lerp(
    //     grayG, 
    //     grayB, 
    //     uv.x); 

    // float4 result = float4(0,0,0,1);
    // float steps=20;

    // for (float j=0; j<steps; j = j+1) {

    //     float2 newUV = uv;
    //     newUV.x += sin(time) * (grayB.x * 0.04 * j/steps + 0.04) ;
    //     newUV.y += cos(time) * (grayB.x * 0.04 * j/steps + 0.04);

    //     float4 colorFromOffsetPosition = inputTexture.SampleLevel(texSampler, newUV, 0.0);
    //     result+= colorFromOffsetPosition;
    // }

    //  outputTexture[i.xy] = result / steps;
    // return;

    //outputTexture[i.xy]= Render2(uv);
    //return;
    // float4 inputColor2 = inputTexture3.SampleLevel(texSampler, uv, 0.0);
    // inputColor2.r=0.4f * sin(param2);

    // outputTexture[i.xy] = inputColor2;
    // return;

    float b = sin(time)*0.5 + 0.5;
    //float4 calcColor = float4(uv, b, 1);
    float4 calcColor = float4(1,1,1,1);
    uv = uv * 2 - 1.0;
    float l = length(uv);
    uv *= sin(l*time);
    uv *= b; //sin(time);
    uv = uv*0.5 + 0.5;
    //uv.x;
    //float4 inputColor = inputTexture.Load(int3(i.x, i.y, 3)); // using mips here works, below not!
    float4 inputColor = inputTexture.SampleLevel(texSampler, uv, 0.0);
    inputColor *= 3*inputTexture2.SampleLevel(texSampler, uv, 0);
    float4 outputColor = lerp(calcColor, inputColor, 0.5);
    //outputColor.r = param1;

    outputTexture[i.xy] = outputColor;
}
