Encaladus / Read aloud in a warm and friendly tone: 

# Tixl Deep Dive: the new shader graph


Hi everybody,

This is a feature update for Tixl the new version of Tooll3. Today, we will have a look at the ongoing shader graph implementation. I'll give a brief overview of that new feature and how you will be able to use it in your projects. 

Initially I also wanted to talk a little about the internal implementation and how you can add your own shader graph operators. But while making this video, I realized that there is sooo much cool stuff to talk about, that'll keep that part for another video.

----

### So why Are We Doing This?

So far, most effects in Tooll 3 have been shaders running on your GPU with fixed shader code defined in HLSL files. In the background these files are loaded and compiled once when needed. 

This approach workd great for hundrets of effects, but it comes with the cost of replicated code and complex operators with many parameters that cover different edge cases. Here’s an example:

- A simple particle effect using a volume force
- A simple selection of points
- A mesh sphere

In each case, we use completely different methods to define and use that volume. Sadly, all of them replicate similar code and add similar parameters. This is just a simple example to make a point, but you can find similar patterns all over Tixl's operators: from generating textures, to particle forces to mesh displacement effects.

With the new shader graph system, you will be able to use easier to understand building blocks that can be combined much more flexible. I will show some examples in a moment.

## new 

With the new graph system we can replace the three nodes with a single **SphereField**. On the left, we have our **VolumeSurfaceForce** driving particles into this spherical container, and we can use the same shape to select points (in this case, driving the radius). The next one is special though because we’re using **Raymarching** to render this volume. Just in case you’ve never heard about raymarching: We no longer use polygons to create shapes, but a function that defines the distance to a volume in space, similar to a 3D density function. This concept has been established by the demoscene and artists like Inigo Quilez on ShaderToy. I’ll go into more detail later about how we use this.

Notice how we can still use ops like [SetMaterial] and [SetFog] to control the look. Although with Raymarching, there are no UV coordinates, so texturing works differently, and not all properties of the PBR material are supported yet.

If we compare these two graphs, they look very similar. In fact, we’re trying very hard to keep the traditional behaviors, interactions, and appearance because those methods are still completely valid and useful.

We’re not going to replace mesh rendering — our aim is to seamlessly extend mesh and particle systems with this new set of field features.

---

### Next Steps

So far, this may not look too impressive, but if we crank up the **Noise offset**, we can already get an idea of why **Raymarching Signed Distance Fields** is so powerful.

This kind of isolated blobs would not be possible with the traditional **MeshDisplace** operator.

Let’s use the noisy sphere for other effects and remove the gap so they are perfectly aligned. 

I'm not sure, I like that the particles are no longer visible. Let's reduce the material opacity. We can do this at the Material or the Raymarching operator. Both colors are multiplied.

Oh, that actually looks pretty sweet!

---- 01f

We could adjust the graph by inserting a [PushPullField] operator to offset the boundary layer.

But we have to keep in mind the draw order. If we render the point sprites after the raymarching pass, the z-buffer will be set as if the raymarched object would be a solid. Transparent objects should always be drawn last.

-----01g

We can add more operators to our graph. Let's insert a [PolarRepeat]. By default it's replicating content along the z-axis around the local space center. So we have to offset our sphere a little bit towards the z - Axis.

The cool thing is, that with raymarching some operations like Space repetition are for free. It's doesn't make a difference if we render 8 or 10000 of these blobs.

# The details

---- 02a

You can see why we are so excited about shader graphs! They are not only fun to play with. They're a new category of tools that will open tons of new possibilities.

As usual you can find all relevant ops in the symbol library. We will add many more operators in the upcoming releases. 

In the rest of the video I will walk you through the most important ones and give some cool examples how to use them.

All shader graph ops are pink and fall into 4 categories:
- **Generators** will generate values like distances or colors for a position in **space**. 
- **Space** operators can manipulate or replicate that *space*.
- **Adjust** modify the calcuated *values*.
- and **Combine** ops merge, intersect or blend fields.

Finally there are operators that *use* or *render* fields. We already saw some examples earlier and I will show more later.

--- 02c generators

As you can see the generators are simple forms. They are defined as super fast mathematical functions that are evaluated for positions in space.

SDF functions like the [Sphere] or the [Box] return a distance to its surface. If you're inside, that distance is negative. That's why these functions are called **Signed** distance fields.

Some shader ops like the [Torus] have so called shader variation parameters. Changing these requires a recompilation of the graph, which can take a while. To speed this up I personally like to disable shader optimization in the project settings. But even then, it would be too slow to animate or drive these parameters by connecting to them.

All of the SDF generators should be straight forward, with the exception of [CustomSDF]. But I will keep this for later :-)

---- 02d

## Fields vs SDFs

You might have noticed that we sometimes call these ops *SDF* and sometimes *fields*. That's because our graph system can handle both! Yes, all generators return not 1 but 4 values.

Although this comes with a tiny performance trade off, the generators can also return their object space coordinates which we can use for texturing. Additionally fields can also return RGB colors, which we can use, for instance to procedurally color meshes, like in this example. And I will talk more about this unified graph concept later.


## Space Manipulation 
--- 03a

Although there is much more going on below the surface, we try to make using graph operators as intuitive as possible. But the next topic is very hard to wrap ones head around: The manipulation of **space**. 

Let's look that this example. Above we see some torus meshes in a circle. Below we see a replicated torus-field. Obviously something is off.

With normal geometry, we create or load an object and then **move** or **instantiate** it to a new position. Field functions work the other way around: First the current space is being shifted into a new space, then the computer checks what's there.

In this example, you can imagine, that are fraction of space is being repeated or mirrored and **then** eventually gets filled with the content generated by a distance function. The torus doesn't fit into that replicated space thus it's getting cut off.

I personally imagine it like some weird space-shifting labyrith made of magic rooms: You're always in one of these rooms with the local coordinate space in the center: X pointing toward the right, Y up and so on. Applying a space manipulation now, is like walking through a magic door into a new room. If space gets folded or manipulated you might be standing in many of these rooms at once, but for you it looks normal with with a new local coordinate system on the floor.

Each position in space starts its own visit to this labirith, if we start very close to a splitting plane, we might end up in one or another room, but never in both. In other words: if something doesn't fit into this room, it will not be visible. And if something is cut off, very strange artifacts will appear, like in this example.

____

I imagine this as some kind of strange labyrinth of magically connected rooms. Each position in space defines at which room we teleport next. 

Every room has local coordinate system: with X toward the right, Y up and so one. And once again, each position in that room can teleport you to other rooms. Like Alice in Wonderland, walking through a door could make you smaller, or turn the ceiling upside down. But once you enter, every room would look normal for you with yet another local coordinate system in middle the floor and X pointing towards the right.

Some position that are very close together could lead to totally different rooms, but never to more than one. And Objects crossing such a boundary will cause artifacts.
____

If the torus doesn't fit into that replicated space thus it's getting cut off.

If you're totally confused, welcome to the club. I promise you, that when playing around, it will eventually make *click*. 


---- 03b

In our case we can enable the PolarRepeat's mirror option to avoid artifacts.

If you question, why would anybody want to use SDF if it's so complicated? Well, check this out: We can nest multiple repeats, and quickly create very complex geometry. 

Remember: These are not geometry instances, it is magically split space. It literally makes no difference how many repetitions are used.

And always remember that operations are applied from bottom to top.

We can rearrange the order of the operators, for different effects.

--- 03c-space-bending
As you can see, most space operators are about repeating or folding space. But there are some like [Bend] and [Twist] that fall into a different category.

Unlike space-folding, these functions "corrupt" the space and make it hard for the raymarching method to render correct results. If you want to impress of irritate people at a dinner party you can mention the **Lipschitz continuity**. This defines the rate of how quickly a property changes through space. For a distance to a sphere, for instance, it should smoothly increase with the distance to that object. For raymarching, the rate of change should ideally be constant.

We can use the [VisualizeFieldDistance] operator to analyze our field and move it around to get a better understanding of how the field forms in space. When we increase the twisting, our field becomes more and more distorted. Our contour-lines -- which ideally should have an equal distance -- get squished, which leads to rendering artifacts. 

Some people like me like noise, but well, you know. It's easier to add than to get rid off. One possibility to reduce the problem is to decrease the "StepSize" factor of the Raymarch-Field op. But it can't do wonders and comes with reduced rendering performance.

## 4. Adjusting

Fields can be **adjusted** by adding or subtracting a value to the distance. We’ve seen the \[PushPullSDF] earlier, which shrinks or grows an object.

The \[AddNoise] operator is similar but adds a 3D noise function to the distance. This can look cool, especially when animating the offset, but large values quickly result in rendering artifacts.

## 5. Combining fields

One of the coolest things about SDFs is how they can be combined. I personally like to use the mouse wheel while holding the control key to experiment with the different combine methods.

I really enjoy intersecting complex fractals with a box to study their structure.

You can use many combine ops to build complex structures and frequently adjust the **K** parameter to control the blending smoothness.

The awkwardly named [StairCombine] has an additional parameter that control the number of groves or stairs, which can lead to some really funky retro-architecture designs.

## 6. Raymarching Performance

Creating SDFs can be addictive, but when we talk about raymarching and SDFs, we have to talk about two things: performance and aliasing.

By now, you will have learned that the operators of a shader graph are compiled into a bunch of functions that are called frequently. With a MaxSteps count of 100 (and some additional evaluations for shading and material), this leads to roughly 200 million calls for a full HD resolution. Yes, that’s a large number. So the next time a gamer friend tells you that their new graphics card is fast enough for everything, you can comfortably say: hold my beer.

To optimize raymarching parameters, it helps to understand the surprisingly simple algorithm. I prepared this little op here to visualize it:

For each pixel of the image, we follow the view ray, shown here in pink.
At the first point along our journey, the distance function tells us how far we can safely advance without hitting anything. So we move forward by that distance and repeat the process until the distance is smaller than our **MinDistance** parameter or we exceed the maximum step count. The first time I understood the simplicity of this algorithm, I was really blown away.

For sharp edges and fine details, you’ll need a very small sampling distance, which increases the required step count. You can play with these values to optimize rendering performance.

Okay, we now have a position close to the surface, but to shade it, we need to compute the normal vector that defines the surface orientation. For this, we sample four nearby points. Use smaller distances for sharp edges or slightly increase the value for a more organic look.

## 7. Custom SDF

Now it’s finally time to talk about the awesome \[CustomSDF] operator. I know that writing shader code sounds intimidating to many people, so I’m not going to go into detail here. (If you want to know more, there’s a whole article about it on the wiki.)

Initially, this operator started as a playground for live-coding distance functions—like ShaderToy, but only for distance fields. But a friend pointed me toward the super extensive fractal collection of …

Converting these from GLSL into TiXL HLSL code is very simple, and you can check out the article on our wiki. Once converted, you can replace magic numbers with the playground variables Offset, A, B, C, and D, and save it as a preset. Creating new fractals is then just a matter of minutes. And playing with the parameters is quite the trip. Oh, and of course you can hold ALT to blend the presets.


## Using fields for particles effects


## Using fields to select or modify points







