# A quick introduction to TiXL

TiXL is a new software for motion graphics, live visuals or creative coding. It's fun, it's easy and -- the best part -- it's totally free. We're developing this not for profit, but because we love motion graphics.

You can also follow the video tutorial:

[![Introduction](https://img.youtube.com/vi/_zvzX0fZ8sc/maxresdefault.jpg)](https://www.youtube.com/watch?v=_zvzX0fZ8sc)


## Download

Download the latest version from: https://github.com/tixl3d/tixl/releases

For this tutorial we're using 3.5. Unzip the archive and run StartT3. Currently TiXL is only working on Windows, but the Team is already working on a port to Mac and Linux.

After startup, TiXL will display a dialog asking for our nickname to structure our projects. Using my Windows username here is just fine.

It then creates my "home canvas" with helpful comments and example operators. If it's your first time with TiXL, you'll definitely want to check out the tutorials. They guide you through interactive lessons and provide a comprehensive overview.

However, in our case, we aim to build something from scratch, so let's disregard all that and utilize "Project" "New" to generate a new project.

Now, here's our project. Opening an operator is as simple as double-clicking on it. To return, you can either double-click on the background or use the breadcrumbs at the top.

This template comes preloaded with numerous operators. Let's delete everything except the output node and begin anew!

The default layout features the Symbol browser, displaying all available operators categorized neatly. Let's open the image / generate section and drag the [RyojiPattern] into our project. This operator employs a shader to subdivide an image into smaller sections.

We can observe the parameters, presets, and output of this new operator. Let's delve into each of these windows.

You can toggle between layouts using function keys like F1, F2, etc., or adjust a layout by dragging the window panels around. 

Personally, I prefer working in Full-Screen mode. To enable it, head to the "View" section of the main menu.

## Output window

You can have multiple output windows and there a number of predefined Layouts with such a setup.

Each output window can be assigned to different Operators, by default it's using the current selection. You can use the pin icon in the top left corner to permanently assign an operator regardless of the current selection.

Depending on the type of this op, the window behaves differently:

For images and textures it works like an image viewer. You can pan with right or middle mouse button and zoom with the mouse wheel. Pressing the Fit button or the F key will adjust the view to show everything. Pressing 1:1 will adjust the zoom to match pixels.

### Resolution

In the resolution select we can switch from the default -- filling the size of the current output window -- to a fixed resolution or aspect ratio. Let's switch to full HD to constrain the aspect ratio. 

Clicking the Screenshot icon will save the current output as an image to the screenshot folder.

Let's have a look at the Parameter Window. At the top we can see the type of the current selection, in our case the [RyojiPattern]. We also see that this is part of the lib.image.generate namespace. We'll talk more about namespaces later. Here we also have the option to rename this particular instance to make our Graph more readable, or to bypass or disable it. 

Below we see all the parameters for our pattern operator. We can click on a value and change it. Alternatively we drag on a value and use the jog-dial. Holding shift and alt will increase or decrease the stepsize.

To fine-tune values, you can also hold down the ctrl key and use the mouse wheel. Once again, you can hold shift and alt to adjust the step size.

A nice trick is, that you can also insert formulas into the value field, for instance to double a value or increase it by a certain amount.

You can click on the parameter name to reset it.

The Ryoji-Operator also has some color parameters. You can click on the color field to bring up the color picker. It's looks and works similar like other applications. But is as a couple of hidden tricks: 

1. You can hold ctrl to enable HDR editing and pick colors brighter than one. By default TiXL uses hdr colors which looks great with glow and color grading.
2. You can hold the alt key to pick colors from the screen.
 
There is one more trick when working with colors: You can drag a color switch and drag up or down to adjust its opacity -- or when dragging with the right mouse button -- its brightness.

Below the parameters many operators list a short description on how to use them, and sometimes a small example. Just with the symbol-library you can drag these examples onto the canvas and study them. 

## Presets
Many of TiXL's operators are surprisingly versatile. To help you understand their possibilities or to allow to quickly access frequent configurations, you can use the preset window. 

Just hover over presets to for a preview. Then click to a apply. 
Click the plus icon to create you own presets.

You can also hold the alt key to blend into one presets, between two presets, or between three if non are selected.

## Graph window
TiXL's true power comes from combining operators into graphs, and we do this on the graph canvas. Let's add more operators to our scene: In the symbol library, open the image.fx section. Then, with [RyojiPattern] selected, click on the [Glow] operator to connect it. However, it might not appear in the output window until it's pinned.

Nice! Also, notice how the preset thumbnails are rendered on the fly to give you a preview of how the final result will look. You can adjust this behavior using the preset window context menu.

So far, we've only been working with images, but TiXL is actually a 3D rendering engine. Let's add some more operators:

1. Select the Glow operator and press Tab; this will open the symbol browser. Once you're familiar with some of the ops, it's a very fast option to find operators.
2. With the symbol browser open, type "D" "M" "U" to search for "DrawMeshUnlit" and press Enter.
3. This new operator has two required inputs. We've already connected the texture, but the small dot indicator tells us that the mesh input is missing.

Let's click on that input; the symbol browser opens again and suggests operators that are frequently used in this situation. We can pick [TorusMesh] here, but I encourage you to try out the other suggestions.

Once we pin the new operator in the output window, we can observe the result. The output window is now in 3D mode, so we can navigate like in a 3D video game: use the right mouse to pan, the left mouse to look around, and the A, S, D, W keys to fly. Q and E move the camera up and down. Pressing the "Fit" button or the F key brings the camera right back to its default position.

You can also toggle the "Gizmo" icon to display a floor grid.

We can see that although the glow operator is still active, its effect is less pronounced on the torus. To improve that, it needs to be applied after rendering the torus. 

We can "shake" the [Glow] Operator to disconnect it from our graph.

However, we can't connect the output of the DrawMesh into the [Glow] because the types do not match. We first have to render the Torus into a new image:

1. Click on the output indicator of the [DrawMesh] and select [RenderTarget].
2. Now, drag the first output of the renderTarget into the input of [Glow]. Nice.

To tidy up our graph, select the [Glow] operator and choose "arrange subgraph" from the context menu, or press G on your keyboard.

You might have noticed that the Torus is now rendered with the default camera. This is because we're no longer using the camera of the output window. To add some animation to our scene, we can insert an Orbit camera:

1. Once again, click on the output of the [DrawMesh] to open the Symbol browser. Alternatively, you can also select that op and press Tab on your keyboard.
2. From the suggestions, pick [OrbitCamera].

As you can see, working with TiXL's graph can be a lot of fun. You can play around with other suggested operators and explore their presets. Or you can check out the HowTo's on the home canvas to find more ideas.

## Animation
Let's delve into another strength of TiXL: Animation.

There are numerous methods for animating in T3, including keyframes, MIDI controllers, audio reactions, sensors, and many more. Depending on your project, you'll likely mix whatever gets the job done quickly and is the most flexible.

Let's begin by adding a soundtrack to our project:

1. Open the playback settings with the gear icon.
2. Add a soundtrack file to our project. By convention, we should copy this to the Resources folder that was created with our project.
3. Shortly after adding the MP3, TiXL will display a visualization of the track, which is awesome for syncing.
4. We should also set up the correct BPM rate for our project. Although we could display the time in frames or seconds, internally T3 uses bars as a time unit. That's awesome because you can adjust the BPM rate later or even while playing, and everything will stay in sync.

Let's quickly add some keyframes to the camera distance and the color:

1. Select an operator and click the parameter controller while holding the ALT key.
2. Keyframes will automatically be inserted when the value changes.
3. You can also click into the dope-sheet view in the timeline.

Speaking of the dope-sheet view, I personally like to enable the auto-view keyframe mode here. This will keep animated parameters visible even when switching between operators.

The timeline context menu offers a lot of additional options like interpolation types, etc.

Keyframes snap to beat timing, which is great once you've set up the correct BPM rate.

A cool trick is to select multiple keyframes and then use the ALT key to stretch them.

You can also jump into the Curve edit mode to fine-tune tangents and values.

Okay, keyframes are cool, and people coming from After Effects will feel right at home.

But another cool method for animation is the [AnimValue] operator. It generates an animated value that is synced to your BPM rate. We can use this value, for instance, to sample a color from a gradient and use it to animate a color in our graph. AnimValue comes with many animation styles like Saws, Sine, Noise, etc. You can hold control and drag to adjust these shapes.

You can also hold Ctrl and drag up or down on the frequency to switch from bars to measures or beats to ticks.

You might have noticed that this procedural animation runs even without timeline playback. That's because "Idle Motion" is enabled. This feature is especially cool when you combine procedural animation with keyframes. You will quickly learn that combining different approaches is easy and often results in more than the sum of their parts. Here's an example:

We can control the amount of white flash by adding a value before sampling the gradient. We can now animate that offset with keyframes to control it.


## Animation triggers
So far, all our animations are deterministic: If we rewind our timeline, we will always get the same result. That's awesome, and if possible, sticking with deterministic animation is a great workflow for tweaking parameters and timing so you can then render your animation as video.

In a live performance setup, however, playing back canned videos and sequences is only so much fun. T3's animation features are so streamlined and fast that you can use audio, sensors and MIDI controllers to build animations on the fly!

I want to use the final minutes of this video to highlight the live performance workflow.

Let's start by switching the playback settings to an external device.
The input dropdown lists your system's audio devices that you can use for Audio-Reactions.

We should also switch the sync mode to tapping.

To make things a bit more interesting, press F12 to switch into "focus mode". This will get rid of all UI elements and reveal the content behind the graph.

In Focus-Mode, the parameters will be displayed directly at the selected operator. Here you also have access to presets and documentation. The play icon renders the output to the background.

If you have a second display attached, you could also enable the "2nd render view" to output the final image to it.

We can toggle the UI elements with Shift-ESC to enable showing the timeline. Now we can tap in the beats with the sync button and finally resync the start of the measure with a right-click. You can refine the syncing and rate by resyncing a couple of measures later.

Remember, TiXL's time is based on the BPM rate. That means from now on, all AnimValues are synced to the new track! Let's sync the camera's orbit to every measure. Obviously, you can have many [AnimValues] connected to all kinds of parameters.

Another cool feature useful for live performances is to control parameters with a MIDI controller. To control a parameter by MIDI, just click on the parameter input and select [MIDI Input]. It will pick the first active controller. As usual, there are plenty of options to control smoothing, output range, and other settings.

Finally, we can use the [AudioReaction] operator to add some secondary animation to our scene. You can Ctrl+Drag to adjust the detection range and threshold. And yes, obviously, the [AudioReaction] has tons of options on how to output the detected signals.

A neat trick is that it also has a "Was Hit" output. We can feed this into another cool operator called [TriggerAnim]. And we can feed this into noise, and booom  — we added a camera shake effect...


## Summary

As you can see, playing with TiXL can be awesome, and its user interface brings you right into the flow state.

In this video, we only scratched the surface of what's possible with TiXL. No time to talk about post-processing, color grading, procedural geometry, particles, video sequence rendering, or exporting your executables. And not to mention its incredible workflow of shader and C# development with hot code reloading.

Whether you're a visual artist, a VJ, or a hardcore developer, TiXL offers the right UX for your level. You should give it a try and join us on Discord if you have any questions or suggestions.

Thanks for watching and hit the subscribe button, if you want to keep updated on Tutorials and new Releases.














