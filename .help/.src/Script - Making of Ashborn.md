# Making of Ashborn

So, we recently finished our latest TiXL project called "Ashborn". It was a tough ride with a lot of lessons learned and a great opportunity to battle-test TiXL's new Shader Graph system. Along the way, I made countless smaller improvements to the user interface, added tons of new features, and yes — I completely changed my mind on using variables in the graph.

In this video I will walk you through my creative process and explain in detail how the project and the effects work. I will also talk quite a lot about my tricks to deal with procrastination.

If you like piano music, particles, and animation, this one is for you. You can watch the capture of the final animation linked below. Feel free to pause here and take some time to watch it first.

Everything started with this wonderful soundtrack made by Lucid. It's a fragment of a piano improvisation — and I fell in love with the track. I close my eyes and immediately see images. Always a good sign.

It's only an audio capture — there are no MIDI notes, no tracks, no velocities. Nothing. And of course there is no BPM rate.

To get into the mood I started by setting manual keyframes. My original idea was to capture how the time is stretched over the pacing of the song, so I’d have one continuous curve I could then use for animation. Long story short: I completely failed doing this and aborted the attempt after an hour or so.

The next attempt was to use AudioReaction — and just testing this with a pulsing blob was once again a total failure. It just wasn’t precise enough and I wanted the playback to be reproducible.

So I decided to bite the bullet and set keyframes for every single note. It’s actually not as bad as it sounds:

* Create a value operator

* Animate it

* Connect it to a modulo

* Link it to the radius of a blob

* Press play

* Keep pressing Shift+C to insert keyframes with increment while the music is pulsing in the background

A good trick that I implemented years ago but almost forgot was to press Shift+L to halve the playback speed.

Every time you need to insert or remove keyframes, you have to "recompute values". I noticed a couple of glitches in the dope-sheet editor and spent some time procrastinating. And yes — in this project I spent more time adding features and fixing bugs than actually creating content.

My brain has a very hard time making any sort of creative decision and immediately jumps on every opportunity to get distracted.

One of the things that bothered me was how difficult it becomes to read the curve graph in the dope sheet because the values are normalized to the value range. I ended up with more than 400 manually placed keyframes. Stretched over the height of an animated parameter, this is practically invisible. So, why not just use the visible keyframes to infer the scaling? How difficult could it be?

Well, a couple of hours later, the curves scale nicely, the whole implementation is much more efficient, and my brain has no more excuses. So, be creative.

The hardest part for me is to find a story. I gave a talk about that topic a long time ago and it’s still one of my guiding principles: I need a story to make creative decisions. Others might call it “a concept”. I was fighting with this for a couple of weeks, listening to the track every day.

I finally tried to make some headway by structuring the soundtrack into parts. I decided to use TimeClips for this. Normally TimeClips are used to switch between scenes of a demo. But here, they sounded like a nice hack to annotate the audio track. But… what’s that? There are no keyboard shortcuts to split and rename clips? The clips are not positioned correctly? A good opportunity to procrastinate and clean this feature up.

Of course it needs to be done. Now. There is no other way.

Some hours later the clip editing is so much better, that I don’t have any more excuses. And suddenly I have that idea of a particle system sprouting from a seed, going through phases of development from green blooming, to industrialization, to demise, and falling back into nuclear doom. Bang\! That sounds like a good concept. It could even be tweaked into a cycle.

So maybe I should build some particle effects now. The initial idea is to have some guiding particles that will follow Perlin noise. On every note, we accelerate and switch the noise phase. I was surprised how well it worked. 

But… hold on, says the procrastinating part of my brain\! That motion would look so much cooler if the particles moved faster on the louder notes of the soundtrack. Why be creative when you can spend some time programming instead?

I always wanted to have a procedural method to generate keyframes. So I quickly implemented a new Keyframe Recording operator: We have a trigger parameter for when to insert a keyframe, and another parameter for value. Now I can save the current audio volume every time on every note keyframe. And yes, of course there is a delay and other details that demand tweaking. Only some hours later I actually had a keyframe track that I could pipe into my turbulence force. And lo and behold, it actually looked slightly better.

Okay, I had an effect that worked for 20 seconds or so. Not too bad, but what now? No matter how long I looked at the screen, I was stuck.

One trick I learned to fool my brain into making creative decisions is by switching to another medium. It doesn’t matter how bad sketches are — you can’t turn them into particle effects, so it’s fine to toy around and play with ideas. They don’t need to be pretty, and you can’t spend more than a couple of minutes doing one.

I remembered that there used to be an operator called Sketch that I wrote to draw the storyboard of "Still / There". That was some years ago. And lo and behold: of course it’s no longer working and provides yet another great opportunity for procrastination.

Look mom\! The Sketch operator now supports colors and uses nullable checks. And the sketch files are automatically named by their child ID, so you don’t have to awkwardly rename them.

I finally used my little Wacom Bamboo tablet and sketched out a super rough setup. Suddenly, I was in the flow and sketched scene after scene. With the onion-skin tool it was fun to draft progressions. With the keyframes, I could listen to the track and shift the pacing around.

It was the first time I saw a glimpse of hope that I could actually pull this off.

I was so excited that I immediately jumped into outlining the scene and the camera animation. I created some rough blocking to get a feeling for the proportions for the effects: just some spheres and a ground. Of course I found plenty of opportunity to tweak some operators on the fly, like this grid plane. But yeah, the timing could work. And with the TimeClips I basically had my to-do list fleshed out.

Two weeks left. Plenty of time to procrastinate on other urgent features. Since export of projects with a soundtrack was currently broken, it was a good opportunity to look into that. And yes, it was a mess, obviously begging for a complete rewrite. It took a couple of days, but it was very good preparation to actually release a demo that runs on the demo machine without installing additional dependencies. So that one was not wasted time.

—-

Next I did some more experiments with particles, SDF, and shader graphs and slowly developed the idea that we can model geometry and scenes with signed distance fields and use them to drive particles. The initial tests looked surprisingly cool. I always liked particle demos by Fairlight and gave up a long time ago on fretting about effects that were invented 10 years ago.

On the fly, I cleaned up most of the particle forces and the parameters of the particle system. I experimented with a JumpRandomForce to make the ribbons jump on notes. It looked nice but didn’t fit the mood. Maybe somebody else will make a cool demo with that.

On the fly, I constantly improved render caching issues — always a perfect opportunity for some tasty procrastination. The idea is: everything that is not changing should not be recomputed every frame. For instance, \[Grain\] has an animation speed parameter, which is great because connecting a \[Time\] operator every time would be cumbersome. But now I wanted to add a grain texture to my floor, which suddenly needed to be updated every frame. TiXL indicated this nicely.

With Grain, the solution was to add a new Freeze-Mode to our time operator. When frozen, Time no longer invalidates everything that depends on it. It’s a small improvement, but I was slowly getting nervous about performance. Things like this add up.

I also added a new parameter to control Emit Velocity of a particle system. This allowed me to randomize points before emitting and scatter them slightly. I’m still not really happy with how they appear, but I didn’t have time to work on that further.

I spent a couple of days working on sort algorithms for particles. After I finally got the seven compute shader passes to compile, work, and run, I realized that the atomic counter to fill up the buckets would always flicker between frames, which I deemed unacceptable. Two weeks to the Evoke demo competition. Panic.

I experimented by faking a glow of the initial ribbon on the flow, of course adding more features, ops, and parameters. The effect was okay, but not overwhelming.

Obviously I was creatively blocked. I wasn’t really sure what to do with the rest of the demo, so I spent some time thinking about color interpolations and creating a new HowToUseColors tutorial, because somebody asked for it on Discord.

Five days to Evoke. Panic was really bad now. So I decided to pick a random scene and start working on it. Why not use the final mushroom cloud? I decided to actually try some vibe coding with ChatGPT: create a force to twist particles around a torus. How hard could it be? It turned out that faking it was actually harder than doing the real thing.

I was about to give up after a couple of hours when suddenly — it magically looked like a mushroom cloud. I got some motivation back. Maybe this was possible after all. I ended up with this \[ToroidalVortexForce\] that twists particles into a torus-like shape.

To get more control over the forces, I decided to add a new category of “fields”. In addition to distances and colors, fields can now return a Vector. And of course, these fields can be multiplied and transformed, etc. It’s pretty powerful stuff, but I ended up using none of those new features.

I moved on to the ending. Building it was surprisingly fast: create a grid, instance some spheres, add a fake shadow. But what’s that?\! The specular reflection of the PBR material looked off. Uh oh… that’s bad. After some experimentation, and with the help of ChatGPT, I noticed that the view direction was not correctly applied to the specular component. The fix was easy enough, but now DrawMesh and RaymarchField had slightly different appearances. I had two options: fix all PBR-rendered objects or fix it later. So it was finally time to align the PBR shader computation into a separate include.

We recently added a new suite of visual tests so we could verify that tested scenes looked the same after such a change. I noticed that we didn’t have a test for PBR rendering yet. No time like the present. It was painful, but after a couple of hours everything looked nicely aligned. A couple of hours I didn’t have.

With the beginning and the ending now connected to the note keyframes, I noticed that there were a lot of connections going all over the place. It was crazy, but instead of building more scenes, I started to refactor everything into variables. We now have new variable types for boolean, int, and vectors.

With this, I could create a new section that basically set up all variables for the scene so they could be used later. I noticed that the variable dropdown via type-ahead search was not working smoothly — you know the drill by now.

Three days left. I had 20 seconds in the beginning and 30 seconds at the end. Roughly 60% of the demo was missing. I was already thinking about releasing at another party a couple of months later.

I finally got enough energy to work on the landscape SDF. I looked at a couple of examples on ShaderToy, especially this one by XOR, and the concept didn’t look too complicated. But I wasn’t sure if this could actually be turned into a distance field operator for TiXL’s shader graph.

Sometimes you get lucky: The \[HeightMapSdf\] turned out to work great and played nicely together with \[SdfToColor\]. Let’s break down this example:

* Start with a FractalNoise. We only need a single color channel, that’s why it’s read. We can tweak the parameters, and the GainAndBias is great for tweaking the shapes.

* Cut a valley through our mountain range using the \[SinForm\] operator. I hadn’t used this one for some years, and of course my procrastinating brain couldn’t resist the opportunity to first optimize its caching.

* Use the SedSDFMaterial to add some color: A PlaneSdf gives us the distance from the ground level, we add some noise, and then convert that distance map to a color gradient.

* Combine our mountains with another PlaneSdf for the lakes.

Tweaking the landscape was surprisingly pleasant.

And suddenly we could use this field to attract particles. The \[FieldVolumeForce\] also applied the colors to the particles when they collided with the surface. Boom, an effect was born.

And it was Thursday. Two days to go. I took the afternoon off to work on the demo. I started by animating the mountain scene, and things were slowly coming together. But while tweaking camera animations, I noticed that there was a bug that would break interpolation when looking around with the middle mouse button.

It was 7 o'clock in the evening. I was really tired. Even my procrastinating brain wasn’t eager to fix that. I gave it a try anyway, and yes — TiXL’s camera system needs some serious overhaul.

—---

After adding some filler content and tweaking a bit more, I try another runthrough and suddenly things are coming together. It really reminded me of that phrase in Ed Catmull’s book *Creativity Inc.*, where suddenly and miraculously a movie no longer sucks.  
 I spend a couple more hours tweaking: doing a runthrough, identifying the 3 most critical glitches and gaps, and repeating that over and over again.  
 At 11 at night I render out a preview version and send it to friends for feedback.

Friday is mostly traveling. Working on content on the train turns out to be impossible, so I spend some time fixing more UI glitches I noticed earlier.

The next day I finally have the energy to work on the middle section and the ending. Things must be going well, because my procrastinating brain decides that 5 hours before the deadline is the perfect time to tweak how the Editor displays and handles the content below the graph layout. The notebook display is tiny and I have so many keyframes that this was actually time well spent.

And then it’s release time. I’m surprised how well everything suddenly comes together. Normally I can’t stand watching my work on the big screen – all I see are the glitches and details I didn’t have time for. But this time… my procrastinating brain is finally at peace.

So here is the complete project:  
 Everything is built procedurally. There are no images of objects besides the font.  
 I’ll use the rest of the video to quickly run you through the project. It will be available as an example with the next TiXL release.

We can start with the final output:

* The title and credits text should be rendered after the post processing, so we add a group on top of our post processed main output.

* The group’s color is animated to fade out at the end.

* The post processing is nothing special: just Screen Space Occlusion, some bloom and grain. Not even a \[ColorGrade\].

* Next comes our MainCamera with a PerlinNoise driving the RotationOffset parameter and some handheld camera shake.

* Tweaking the camera animation was tedious. Once in a while I duplicated the op as backup. All keyframes are set to Cubic interpolation.

* The DrawCamGizmos lets us see the camera view in relation to the scene. To use it, switch the output window camera mode from Auto to Viewer.

* I also used the GizmoVisibility op extensively to show debug content when Gizmo mode is enabled.

After the main camera comes the “Setup” group:

* Here I’m setting context variables for the main focus position, the NoteIndex, a boolean for whether a note was triggered, and the intensity I recorded from the audio volume.

* My little debug animation is still in there, useful for checking the sync with the soundtrack.

Then comes the actual content:

* The “Storyboard” is still connected but disabled. I toggled it once in a while to check if the camera animation was on track.

* A procedural environment map is added with tweaked exposure and blur parameters.

* Then comes the single main point light, driven by the focus position variable and an animated value for intensity.

* The fog parameter was cumbersome to tweak. Both the camera and distance values had to be adjusted and animated.

* The opening scene uses GridPoints with randomized positions and scale. I tweaked the randomPhase to avoid overlap with the main ribbon.

Speaking of the devil, here’s how the Main Ribbons are built:

* We start with RadialPoints and map their color with a gradient.

* Emit them only once or when time is rewound.

* I used 5 forces to guide the particles through all scenes:

* Two field forces keep particles contained in a sphere and prevent them from colliding with the ground.

* A turbulence force drives the particles at the beginning: on each hit, the intensity is animated with a \[TriggerAnim\] and the phase offset by the note index.

* A new \[VelocityForce\] pushes them forward on every note.

* I’ll talk about the 5th force in a moment.

* Finally, pipe the particles into a point trail and draw them.

In the one month I spent creating the project, I made 214 commits to TiXL. Most were only slightly related to “Ashgrow,” and many were completely unrelated.  
 It’s probably the worst way to make a demo, but the next TiXL release will benefit greatly from it.

 

 

 

