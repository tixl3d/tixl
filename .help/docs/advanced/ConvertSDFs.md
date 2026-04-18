# Converting raymarching shader functions

[Jon Baker, Graphics Programming](https://jbaker.graphics/writings/DEC.html) is an excellent resource for shader functions, especially signed distance fields.

This page explains how to convert those into a form you can use with the `[CustomSDF]` operator.

![CustomSDF example](/images/ConvertSDFs/image.png)


```glsl
// Original

  float de( vec3 p0 ){
    vec4 p = vec4(p0, 1.);
    for(int i = 0; i < 8; i++){
      p.xyz = mod(p.xyz-1.,2.)-1.;
      p*=1.4/dot(p.xyz,p.xyz);
    }
    return (length(p.xz/p.w)*0.25);
  }
```


What we need to do is to convert glsl into hlsl and replace the CustomSDF wrapper method...

The wrapper looks like this and is a build in part of the op:

```glsl
// A wrapper function of the position p, the optional parameters Offset, A, B, and C
float dCustom(float3 p, float3 Offset, float A, float B, float C)
{
```


```glsl
// This is the part you need to write into the shader parameter:
return 0;
```

```glsl
} // The end of wrappers
``` 


## So lets convert the code:

```glsl
// Original

  float de( vec3 p0 ){                     // the original function needs to go away
    vec4 p = vec4(p0, 1.);                 // this adds a variable p, interfering with our parameter p
    for(int i = 0; i < 8; i++){            // stays
      p.xyz = mod(p.xyz-1.,2.)-1.;         // stays (but some taste magic numbers, that could be replaced with A B C)
      p*=1.4/dot(p.xyz,p.xyz);             // more magic numbers
    }
    return (length(p.xz/p.w)*0.25);        // more magic numbers
  }
```

So the first step would be to replace the ...

```glsl
float4 p2 = float4(p, 1.);                 // We need to replace vec4 -> float4
for(int i = 0; i < 8; i++){            
    p2.xyz = mod(p2.xyz-1.,2.)-1.;         // use p2
    p*=1.4/dot(p2.xyz,p2.xyz);             // use p2
}
return (length(p2.xz/p2.w)*0.25);          // use p2
```

Now let's replace with some parameters. And also remove the glsl required float dots like `1.`.  


## Using parameters

```glsl
float4 p2 = float4(p, 1);                 
for(int i = 0; i < 8; i++){            
    p2.xyz = mod(p2.xyz - 1, 2) - 1;        
    p*=1.4/dot(p2.xyz, p2.xyz);             
}
return (length(p2.xz/p2.w) * A);          // Let's use A so we can tweak it...
```


![Tweaking parameter A](/images/ConvertSDFs/anim.gif)


Keeping the defaults as comments might be a good idea...

```glsl
float4 p2 = float4(p, 1.);
for(int i = 0; i < 8; i++){
    p2.xyz = mod(p2.xyz - 1, B) -1;   // B: 2
    p2*=C / dot(p2.xyz ,p2.xyz);      // C: 1.4
}
return ( length(p2.xz / p2.w) * A);   // A: 0.25
```


## Adding a preset
Creating presets is even cooler, because it allows you to keep the original code and blend between paramters.
(BTW. You may need to move your camera further away to see the fractal correctly)

![Blending between presets](/images/ConvertSDFs/anim-1.gif)


## Add credits and details
Even though Jon Bakers' shaders are CC0 non-commercial, it alwyas feels good to give credit:

```glsl
//From https://jbaker.graphics/writings/DEC.html / unknown
float4 p2 = float4(p, 1.);
for(int i = 0; i < 8; i++){
    p2.xyz = mod(p2.xyz - 1, B) -1;   // B: 2
    p2*=C / dot(p2.xyz ,p2.xyz);      // C: 1.4
}
return ( length(p2.xz / p2.w) * A);   // A: 0.25
```



