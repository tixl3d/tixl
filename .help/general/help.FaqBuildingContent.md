# Building graphs

## Best practices for making "subpatches"

❔ *(are they called that?) — e.g. what kind of stuff should I encapsulate in a subpatch? For example, for 2D effects does it make sense to have each patch output a texture that I then switch with a PickTexture? How about 3D stuff (like particle effects or other 3D objects?)*

This is basically a matter of personal taste and preference. But there are some indicators that might help you make these decisions:

### When to create sub-graphs

* Whenever you see parts of your graph being repeated multiple times (e.g. more than 2) it’s a strong indicator that something could be generalized. This could be:

  * scenes (e.g. a 3D element repeating)
  * effects (e.g. a combination of point modifiers)
  * typographical elements (e.g. opening titles displaying names)
  * math logic (e.g. something that computes the size of an element)
* A single nested operator is probably too small. Most ops have between 2 and 30 ops.
* Most operators have a primary output (its type defines its color).
* Very few have additional outputs (e.g. a secondary image texture).
* Adding parameters (e.g. the “Names” of the before-mentioned titles) is fast and easy. You can always change, rearrange, or rename parameters.

## Variables

### How do they work?

❔ *I would like to define a bunch of global variables that I can then use in any subpatch in the demo. Mostly this would be to have a bunch of variables telling the various subpatches which variant to pick based on MIDI or whatnot.*

Variables are part of the *Evaluation Context*. They are stored in dictionaries by name for float, int, and vector types.
This means you need to assign them using \[SetFloat], \[SetBool], etc. early in the graph (i.e. on the right side, close to the final output) so they can be used further down the graph (on the left side).

Variables can be overridden in sub-graphs (the previous value will be restored).

## Flow Control

### Conditional stuff

❔ *How do I do `if(time >= 16.0 && time < 18.0) { ... }` in Tooll? I would like to enable/disable stuff based on this kind of setup as that’s how I typically have done my demo designs.*

* The most convenient method could be \[TimeClips] — especially if you set up your BPM rate correctly and use the bar-raster mode of the timeline. This allows precise snapping, cutting, etc.

Alternatively you can…

* use **\[Compare]** and boolean logic to control the **\[Execute]** or **\[Group]** *Enabled* parameter.
* animate the **\[Switch]** index:

  * to the index of the connected graph you want to evaluate (all others will not be evaluated, which is identical to being deactivated)
  * to `-1` → everything disabled
  * to `-2` → everything enabled
* disable ops (and their complete sub-graph), but this cannot be animated or controlled through parameters.


# Particles

## how do i use the particle age to scale particles? 

_like if i set fx1 to particleage and then use fx1 as the scale factor later, it's the wrong way around (the age starts at 0 and goes to 1, which means that particles are 0 in size when they spawn and then max size when they die. i want the opposite. or rather i would want to be able to have a graph here where particles start at size 0, expand to their max size pretty quickly, and then fade out back to 0)_

[ParticleSystem]->[MapPointAttributes] (Mode set to F1)

