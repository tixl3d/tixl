int t;  // implicit constant buffer
RWTexture2D<float4> y;
[numthreads(16,16,1)]
void cs_5_0(uint3 i:sv_dispatchthreadid)
{
    float3 v=i/float3(640,400,1)-1,
    w=normalize(float3(v.x,-v.y*.8-1,2)),
    p=float3(sin(t*.0044),sin(t*.0024)+2,sin(t*.0034)-5);
    float b=dot(w,p),d=b*b-dot(p,p)+1,x=0;
    if (d>0)
    {
        p-=w*(b+sqrt(d));
        x=pow(d,8);
        w=reflect(w,p);
    }
    if (w.y<0)
    {
        p-=w*(p.y+1)/w.y;
        if(sin(p.z*6)*sin(p.x*6)>0)x+=2/length(p);
    }
    y[i.xy]=(abs(v.y-v.x)>.1&&abs(v.y+v.x)>.1)?x:float4(1,0,0,0);
}