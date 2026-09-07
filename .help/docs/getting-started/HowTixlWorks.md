# How TiXL works

## How TiXL generates Content

For complex projects it will be helpful to understand how TiXL generates content. 

TiXL uses a technology called [Directed Acyclic Graphs](https://en.wikipedia.org/wiki/Directed_acyclic_graph). That sound more complicated than it actually is. So let’s break that down: A “graph” is a system of Nodes (in our case Operators) linked by connections. This graph is being evaluated in the direction starting from the output. That’s why it’s called “directed”. “Acyclic"  is just a fancy term that means this graph must not have cycles.

Let’s take an example: let’s assume you have an [RandomCamera] pinned to the output window. To generate the output we first gather all information required for this task: What is the current output resolution? What is the current Playtime? What is the current viewer camera perspective, etc. All this information is being added to an object called “Render Context” (click the link for more details on this).
Then every operator is being “evaluated” (I.e. updated) with this context. Let’s assume our [RandomCamera] should show a [Layer2d]. The camera first replaces the default camera perspective with its random view, and then evaluates the [Layer2d] instance with this new context. Each an every other connected operator does the same thing always in the same well defined order: first all children required for the first input. Then all children for next etc.

You can see how this would update all the connected nodes. Some of these nodes would eventually actually use the context to draw something on the screen using the information get get in the context. 
Although this would work, it would be terribly inefficient to update everything all the time. It doesn’t make sense to load the image every frame again, or compile the same shader or generate the same texture. This is why we need _Caching_:
Each output can store its current value and has a property called a “Dirty” flag. If an output is dirty we have to update it. If it’s not we can just use its current value.

Of course this just introduces another problem: if nothing is dirty nothing gets updated: to solve this we add a third concept called _Cache invalidation_: on the beginning of each frame we collect all things that should be updated: this means everything animated, every connected to the playtime or other inputs like sensors or MidiControllers. If any of those get dirty it “invalidates” (set the Dirty-Flag)  all Operators depending on it, all the way to the [Layer2d] and the [RandomCamera].

TiXL’s graph view visualizes some of this: updated Operators are opaque. Not required or cached ones are fading out. 

This sounds terribly complex, but in practice this works flawlessly for graphs with thousands of operators.

With this concept we have some awesome possibilities: 
- you can use operators to override the context time, perspective, material or resolution that incoming operator should use for rendering.
- You can loop multiple times over a sub graph to emulate things like true motion blur.
- You can store custom context values that can be used by operators “further down” the graph (this is how [AnalyzeAudio] and [AudioReaction] work).

If you ever need to define an output that gets automatically invalidated (maybe because it’s getting its input from a sensor) you can override its update settings by right clicking on the output.


## Understanding how Resolution is working
In the last chapter we discussed how TiXL updates animated inputs or inputs driven by time.

Let’s quickly describe how TiXL deals with resolution. We are looking for a flexible solution that can seamlessly switch between different resolutions like 1080p and 4K, but also supports different aspect ratios like 16:9 or 4:3 without stretching pixels or fonts, and finally can apply image effects to loaded images of arbitrary size and aspect ratio.

To do this we distinguish between 3 different resolution types:
- _Explicit resolutions_ are between 1 and 16384 pixels in width and height (That’s the maximum texture dimension of most graphic cards). Most of the time you don’t have to deal with power of two sizes. Whenever you enter an explicit resolution into a resolution parameter the resulting output will have that size.
- The default value for most resolutions however is (0, 0). This means it will either use the Auto resolution: Either of the incoming image or - if nothing is connected - the resolution requested by the output window. This is super convenient because most of the time you don’t have to deal with resolutions at all. It just works. If you want to switch to 4K because you open a project on a better graphics hardware, just change the output resolution → done.
-  The final “special resolution” is _force to output resolution_: If either of the size values is smaller than 0, tooll will ignore the resolution of connected input textures and prefer the requested output resolution instead. This can be useful in the rare cases where you want to force an image effect into the output resolution.

If this sounds confusing, well it is. Most of the time it just works, but when ever you suddenly need a special solution, you can come back to this chapter.

You can always check the current resolution be looking at the output window label.

You can also use the [RequestedResolution] operator to use to current render resolution for effects or other purposes.


