# Initial draft v1

Hello again. We recently released TiXL version 4.0.6. Besides from the usual hotfixes this will be final release 4.0 before moving on to the next v4.1.

This is a big one and there are so many changes that is a perfect time for another update video. I will walk you through the highlights.

The focus of this release was the export and installation flow. Thanks again to all the people from the community for reaching out and helping us to narrow down the various hick-ups on different systems.

You will notice that from now on, TiXL will install like other applications to Program files. Getting there was a lot of work, because you are not allowed to write any data to that folder after installation. This means, all project data is now under Documents/Tixl/ (you can change this folder in the settings) and all settings, logs and backups to the awkwardly named AppData/Roaming/TiXl/ directory. And yes – saving a bookmark to that one might save some time.

We have plenty of ideas on how to tweak this in upcoming releases, especially in regards to project backups.

So let’s jump right in and walk you through the updates. I will start with the user interface and talk about the new or changed Operators in the second part.

# 

## UI

We optimized how user-projects are created and compiled. This should resolve a lot of issues with incorrect malware detection by windows defender or other startup issues. If you still encounter any problems, please reach out on Discord or GitHub.

We now exclude large files from the backups (which to remind you are located in AppData/Local/Roaming/TiXL).

We also stabilized that export as executable flow which should now handle playback of NDI, Spout, WebCam and most other IO systems. Sadly, this currently comes with an increased build size, but we are going to optimize that in an upcoming release.

We introduced visual unit tests (I’ve talked about this in the meetup earlier). This basically generates image sequences for all major projects, demos and examples and a suite of other test files, which are then compared to reference images. This drastically improves the stability and reduces breaking changes while moving forward.

We added a licenses folder with TiXL’s dependencies to other libraries and projects. Credit where credit is due.

This release also saw a lot of tweaks and quality improvements to the new graph ui. We really appreciate all your feedback. This is super important to hone down on the perfect user experience\!

To start with, when dragging operators they are no longer disconnected when unsnapping from a group. Although this sounded great in theory, there are too many situations where the graph broke accidentally. However, you can still drag out or reorder operators from within stacks, both vertical or horizontal.  Disconnecting, e.g. through shake is so fast anyways that we no longer need that shift-modifier.

Note that connections are temporal during drag operations. This means you can still quickly skim through vertical op stacks to check their output.

You now can also finally drag vertical connection lines. That implementation took longer than it should have\! We also fixed a bunch of other glitches and further improved how the layout is adjusted when connections are changed.

Before replacing connections, they are now highlighted in red.

TiXL now shows thumbnails on all connections even when they are snapped into a triangle. You can adjust this behaviour with the preview toggle in the toolbar.

Horizontal snapping is now enabled by default. You can also align ops towards the right through the new keyboard shortcut Alt-A. Speaking of keyboard shortcuts, although already added in the last version, you might have missed the keyboard layout editor. If you’re one of these optimizers, this might help optimise your workflow.

We improved how TiXL animated between different graph views. This is especially noticeable when scrolling through the results of the Ctrl+F search result list.

And finally we tweaked the rename annotations flow.

Next, let’s talk about the **timeline editor:**

TimeClips now automatically avoid overlap. You can adjust the height of the TimeClip area, rename and delete them through keyboard shorts.

The currently active timeClips are highlighted. And TimeClips that are not connected to anything will never be evaluated and have no effect. We indicated this by fading them out and saying so in the tooltip.

When working with the DopeSheet you will love to hear that we reimplemented the curve editing which should now be more efficient allowing more keyframes.

When working with colors, you will notice that we now indicate HDR colors that exceed a level of 1 with a triangle. This also works with gradients and makes working with HDR colors and glow much easier.

The GradientEditor now displays curve overlays while editing and on hover. This is great to understand the different gradient interpolation types. I will talk about the new BuildGradient op later.

We also changed how numerical parameters are clamped. You can now decide if parameters are clamped on the min, max or both sides of the parameter range. This is great for parameters like ParticleCount or BlurRadius that only have a lower bound.

Under the hood we completely rewrote how Operators handle custom Operator UIs. This means they no longer vanish after rebuilding projects. This also enables developers to use hot code reloading which will lead to more beautifully crafted Uis.

Here is a list of other quality of live improvements in the UI:

If no operator is selected you can pin the current composition with Ctrl-P.

We fixed the interaction with the background in Focus modes.

The project list can now be scrolled and the current project can be opened in File Explorer. Also the list is now sorted with the latest op on top.

Picking file assets is still not where it should be, but we improved the dropdown list in the meantime. A new asset workflow is the focus of the upcoming v4.1.

After adding gradients parameters to ops they can be edited without restarting. 

We fixed some aspects of the camera interaction. But new improved camera control is on its way.

When rendering videos, the bitrate option is no longer capped.

When working with FloatList and IntegerList the grid visualization in the output window looks much better. Adjusting the grid column count is great for working with Artnet light fixtures.

Here are some updates to working with Audio and in live performance setups:

The Mute Audio button finally works and you override the project volume in the settings.

Changing the soundtrack and background images has been rewritten and should be more stable. 

You can now disable audio analysis based beat locking, although I’m not sure why you would ever want to do this.

When tapping the maximum BPM rate is now 600 beats per minute. Take that happy hardcore.

Finally exporting projects with or without a soundtrack as executable should be more stable.

Let’s quickly run through the new and updated Operators\!

As usable the is an insane amount of updates on the Shader Graph side:

While the promised tutorial video is still in make, we can keep you busy with a bunch of new operators and effects:

- You can use the HeightMapSdf to build terrains. You might want to watch the indepth “Making of Ashborn” tutorial, if this sounds interesting.  
- You can quickly invert Fields with the fittingly named \[InvertField\] op.  
- \[TranslateUv\] can be used to offset the locale space returned from an Sdf. Although not really UV coordinates, this might already help to tweak designs. On that topic: Newemka fixed that behavior for many of our shadergraph ops.  
- In addition to distances and colors, we’ve now added a vector field type. This can already be used for driving particles with the \[VectorFieldForce\] but we plan to add many more effects to that one.

  To generate vector fields you can use \[SdfToVector\] which will sample the distance field gradient.

- Additionally we fixed \[SdfToColor\] when used with particle effects.  
- We slight improved the implementation of \[BoxSdf\]  
- We fixed BendField.  
- Translating \[PlaneSdf\] with the transform gizmo is now more intuitive.  
- When applying \[TransformField\] with scale factor we now adjust the returned distance to avoid raymarching artifacts. We also added an option to rotate these Vector fields I mentioned earlier.  
- We also added a mix mode parameter to the \[CombineFieldColor\] operator.

Let’s talk about points:

The biggest new effect here will be the SelectPointsWithSdf operator which enables a wide range of effects. We discussed this in detail in a recent meet-up and you can find the video capture of that session if you want to know more. You can also check out the example operator.

Also…

- We touched up our beloved RandomizePoint operator: There now is a uniform scale parameter, HDR colors are no longer clamped and randomising colors got fixed.  
- RadialPoints now have a Color parameter.  
- We cleaned up \[MeshFacesPoints\] and added some cool examples.  
- SnapPointsToGrid and SetPointAttributes now supports a strength fx factor  
- We fixed PointsOnImage attribute defaults  
- And we finally changed the default PointTrail to a more consistent implementation. The fast cycle buffer implementation with that pesky gap, got renamed to PointTrailFast.

Next up: Particles:

We’ve got a bunch of new forces:

- The \[VelocityForce\] can push particles forward which is great for syncing to music.  
- The \[ToroidalVortexField\] can build mushroom clouds (again, check out the “Ashborn” video.  
- \[RandomJumpForce\] will randomly offset particles.  
- And finally we have the \[VectorFieldForce\] mentioned earlier.

These particle ops got an update:

- The particleSystem now has an Fx-EmitVelocityFactor which is works especially great with the \[SelectPointsFromSdf\]  
- We completely overhauled \[FieldVolumeForce\] which now supports colorization and optional collisions.  
- \[TurbulenceForce\] now supports value fields.

Okay. Almost done… Next on the list are Mesh and Rendering ops:

- Newemka contributed a new \[Icosahedron\] op with a ton of cool parameters and examples.  
- DrawLines now has a fadeOutLongLines parameter which works great to build plexus-like particle effects.  
- We completely aligned the PBR rendering part for \[DrawMesh\], \[RaymarchField\], \[DrawPointsShaded\], \[DrawMeshAtPoints\] and \[DrawPoints\]. All of them now support overriding the color through fields.  
- \[SetEnvironment\] automatically converts to CubeMap, updates only if required (so no more need to disable the live parameter) and comes with a set of presets.  
- RepeatMeshAtPoints now uses correct point scaling and Fx scale factors.


On the animation side we have a bunch of new cool operators:

- AnimBoolean comes with a nice UI. As with all Animate ops, you can adjust its rate by holding Ctrl and dragging vertically. Of course it also supports SpeedFactors  
- AnimInt also got a new Ui and now features a modulo parameter.  
- With the new \[SetKeyFrames\] operator you can generate keyframe tracks procedurally which can be great for consistent timeline animations. Once again the “Ashborn” video will show you how to use it.  
- Have a look at the release notes for some further fixes and improvements on animation ops.

We’re constantly working on adding more features to connect TiXL to other systems and sensors. Here are the updates on the io-front:

- \[VideoDeviceInput\] now supports webcam input  
- The new \[ArtnetInput\] operator can listen and receive data from artnet.  
- Banidu also completely refactored the \[ArtnetOutput\] operators to be faster, more flexible and no longer rely on external networking libraries. He really pushed the boundary of what can be done with TiXL when it comes to stage lighting. Check out the github wiki for more details.  
- \[OscOutput\] now supports integer values.  
- We also updated NDI, Spout and MidiInput ops.

We added a complete new set of operators for working with Colors: 

- To start with, there is a new Color-List type with its respective type operator. You can use it to create color palettes and like with other list types you can then combine or pick for that list.  
- The new \[BuildGradient\] operator turns a color list into a gradient. Note how you can also provide a float list to specify the step positions.  
- There is a super cool new \[HowToUseColors\] tutorial. As with all \[HowTos\] you should really take a close look at this interactive tutorial. It’s probably the fastest way to learn TiXL.

We also added tons of new number operators. Please check out the release notes for more details if you are into math and value lists.

Finally I want to point out the newly revamped sketch operator. I still think it’s super useful to annotate existing animations or sketch out storyboards. Once again it features prominently in the “Ashboard” tutorial.

That was a serious load of updates. Looking at the intermedia roadmap you can already get some idea of stuff we have on the agenda for v4.1 which is planned for autumn.

As usual, we love to hear from you, either in the comments on our Discord server\! We need you to move forward and contribute new feature ideas, suggestions or even new operators. You might also share your designs with us on Instagram so that we can repost them.

Don’t forget to hit that subscribe button. And once again, thanks for watching. 

—-

# Cleaned up version

Hello again. We recently released TiXL version 4.0.6. Besides the usual hotfixes, this will be the final 4.0 release before moving on to v4.1.

This is a big one, and there are so many changes that it’s a perfect time for another update video. So, let me walk you through the highlights.

The focus of this release was the export and installation flow. Thanks again to all the people from the community for reaching out and helping us narrow down the various hiccups on different systems.

You’ll notice that, from now on, TiXL installs like other applications to Program Files. Getting there was a lot of work, because you’re not allowed to write any data to that folder after installation. This means all project data is now under Documents/TiXL/ (you can change this folder in the settings). 

All settings, logs, and backups go to the awkwardly named AppData/Roaming/TiXL/ directory. And yes—saving a bookmark to that one might save some time.

We have plenty of ideas on how to tweak this in upcoming releases, especially regarding project backups.

Anyways, with that out of the way, let’s jump right in and walk through the updates. I’ll start with the user interface and talk about the new or changed Operators in the second part of this video.

## **UI**

We optimized how user projects are created and compiled. This should resolve a lot of issues with incorrect malware detection by Windows Defender or other startup issues. If you still encounter any problems, please reach out on Discord or GitHub.

We now exclude large files from backups (which, to remind you, are located in AppData/Roaming/TiXL).

We also stabilized the export-as-executable flow, which should now handle playback of NDI, Spout, webcam, and most other I/O systems. Sadly, this currently comes with an increased build size, but we are going to optimize that in an upcoming release.

We introduced visual unit tests (I’ve talked about this in a recent meetup). This basically generates image sequences for all major projects, demos, and examples, plus a suite of other test files, which are then compared to reference images. This drastically improves stability and reduces breaking changes while moving forward.

We added a licenses folder with TiXL’s dependencies on other libraries and projects. Credit where credit is due.

This release also saw a lot of tweaks and quality improvements to the new graph UI. 

To start with, when dragging operators, they are no longer disconnected when unsnapping from a group. Although this sounded like a good idea, there were too many situations where the graph broke accidentally. However, you can still drag out or reorder operators from within stacks, both vertical and horizontal. 

Note that connections are temporary during drag operations. This means you can still quickly skim through vertical op stacks to check their output.

You can finally drag vertical connection lines. That implementation took longer than it should have\! We also fixed a bunch of other glitches and further improved how the layout adjusts when connections are changed.

Before replacing connections, they are now highlighted blinking in red.

TiXL now shows thumbnails on all connections, even when they are snapped into a triangle. You can adjust this behavior with the preview toggle in the toolbar.

Horizontal snapping is now enabled by default. You can also align ops to the right through the new keyboard shortcut Alt-A. Speaking of keyboard shortcuts, although already added in the last version, you might have missed the keyboard layout editor. If you’re one of those optimizers, this might help optimize your workflow.

We improved how TiXL animates between different graph views. This is especially noticeable when scrolling through the results of the Ctrl+F search results list.

And finally, we tweaked the Rename Annotations flow.

**Next, let’s talk about the timeline editor:**

TimeClips now automatically avoid overlap. You can adjust the height of the TimeClip area and rename or delete them through keyboard shortcuts.

The currently active TimeClips are highlighted. TimeClips that are not connected to anything will never be evaluated and have no effect. We indicate this by fading them out and saying so in the tooltip.

When working with the DopeSheet, you’ll love to hear that we reimplemented curve editing, which should now be more efficient, allowing us to use even more keyframes.

When working with colors, you will notice that we now indicate HDR colors that exceed a level of 1 with a triangle. This also works with gradients and makes working with HDR colors and glow much easier.

The GradientEditor now displays curve overlays while editing and on hover. This is great for understanding the different gradient interpolation types. I’ll talk about the new BuildGradient op later.

We also changed how numerical parameters are clamped. You can now decide if parameters are clamped on the min, max, or both sides of the parameter range. This is great for parameters like ParticleCount or BlurRadius that only have a lower bound.

Under the hood we completely rewrote how Operators handle custom Operator UIs. This means they no longer vanish after rebuilding projects. This also enables developers to use hot code reloading, which will lead to more beautifully crafted UIs.

Here is a list of other quality-of-life improvements in the UI:

* If no operator is selected, you can pin the current composition by pressing P or Ctrl+P if in Focus Mode.

* We fixed the interaction with the background in Focus mode and fade out the foreground graph if the background is active.

* The project list can now be scrolled, and the current project can be opened in File Explorer. Also, the list is now sorted with the latest project on top.

* Picking file assets is still not where it should be, but we improved the dropdown list in the meantime. A new asset workflow is the focus of the upcoming v4.1.

* After adding gradient parameters to ops, they can be edited without restarting.

* We fixed some aspects of the camera interaction. A new, improved camera control is on its way.

* When rendering videos, the bitrate option is no longer capped.

* When working with FloatList and IntegerList, the grid visualization in the output window looks much better. Adjusting the grid column count is great for working with Artnet light fixtures.

Here are some updates to working with audio and in live performance setups:

* The Mute Audio button finally works, and you can override the project volume in the settings.

* Changing the soundtrack and background images has been rewritten and should be more stable.

* You can now disable audio-analysis-based tempo locking, although I’m not sure why you would ever want to do this.

* When tapping the beat, the maximum BPM rate is now 600 beats per minute. Take that, happy hardcore.

Let’s quickly run through the new and updated Operators\!

As usual, there is an insane amount of updates on the Shader Graph side:

While the promised tutorial video is still in the making, we can keep you busy with a bunch of new operators and effects:

* You can use the HeightMapSdf to build terrains. You might want to watch the in-depth “Making of Ashborn” tutorial if this sounds interesting.

* You can quickly invert fields with the fittingly named \[InvertField\] op.

* \[TranslateUv\] can be used to offset the local space returned from an Sdf. Although not really UV coordinates, this might already help to tweak designs. On that topic: Newemka fixed that behavior for all of our SDF operators.

* In addition to distances and colors, the Shader graph now supports a vector field type. This can already be used for driving particles with the \[VectorFieldForce\], but we plan to add many more effects to that one.  
   To generate vector fields you can use \[SdfToVector\], which samples the distance field gradient.

* Additionally, we fixed \[SdfToColor\] when used with particle effects.

* We slightly improved the implementation of \[BoxSdf\].

* We fixed BendField.

* Translating \[PlaneSdf\] with the transform gizmo is now more intuitive.

* When applying \[TransformField\] with a scale factor, we now adjust the returned distance to avoid raymarching artifacts. And we also added an option to rotate the vector fields I mentioned earlier.

* We also added a mix-mode parameter to the \[CombineFieldColor\] operator.

Let’s talk about points:

The biggest new effect here is the \[SelectPointsWithSdf\] operator, which enables a wide range of effects. We discussed this in detail in a recent meetup, and you can find the video capture of that session if you want to know more. You should also check out the example operator.

* We touched up our beloved RandomizePoint operator: There is now a uniform scale parameter, HDR colors are no longer clamped, and randomizing colors is fixed.

* RadialPoints now has a Color parameter.

* We cleaned up \[MeshFacesPoints\] and added some cool examples.

* SnapPointsToGrid and SetPointAttributes now support the strength FX factor.

* We fixed PointsOnImage attribute defaults.

* And we finally changed the default PointTrail to a more consistent implementation. The fast cycle buffer implementation with that pesky gap got renamed to PointTrailFast.

Next up: Particles:

We’ve got a bunch of new forces:

* The \[VelocityForce\] can push particles forward, which is great for syncing to music.

* The \[ToroidalVortexField\] can build mushroom clouds (again, check out the “Ashborn” video).

* \[RandomJumpForce\] will randomly offset particles.

* And finally we have the \[VectorFieldForce\] mentioned earlier.

These particle ops got an update:

* The particleSystem now has an FX-EmitVelocityFactor, which works especially well with \[SelectPointsFromSdf\].

* We completely overhauled \[FieldVolumeForce\], which now supports colorization and optional collisions.

* \[TurbulenceForce\] now supports value fields.

Okay. Almost done… Next on the list are Mesh and Rendering ops:

* Newemka contributed a new \[Icosahedron\] op with a ton of cool parameters and examples.

* DrawLines now has a fadeOutLongLines parameter, which works great to build plexus-like particle effects.

* We completely aligned the PBR rendering part for \[DrawMesh\], \[RaymarchField\], \[DrawPointsShaded\], \[DrawMeshAtPoints\], and \[DrawPoints\]. And all of them now support overriding the color through fields.

* \[SetEnvironment\] now automatically converts to a cubemap, updates only if required (so no more need to disable the live-update parameter), and comes with a set of presets.

* RepeatMeshAtPoints now uses correct point scaling and FX scale factors.

On the animation side we have a bunch of new cool operators:

* AnimBoolean comes with a nice UI. As with all Animate ops, you can adjust its rate by holding Ctrl and dragging vertically. Of course it also supports SpeedFactors.

* AnimInt also got a new UI and now features a modulo parameter.

* With the new \[SetKeyFrames\] operator you can generate keyframe tracks procedurally, which can be great for consistent timeline animations. Once again the “Ashborn” video will show you how to use it.

* Have a look at the release notes for some further fixes and improvements on animation ops.

We’re constantly working on adding more features to connect TiXL to other systems and sensors. Here are the updates on the I/O front:

* \[VideoDeviceInput\] now supports webcam input.

* The new \[ArtnetInput\] operator can listen for and receive data from Artnet.

* \[OscOutput\] now supports integer values.

* We also updated NDI, Spout, and MidiInput ops.

We added a completely new set of operators for working with Colors:

* To start with, there is a new Color-List type with its respective type operator. You can use it to create color palettes, and like with other list types, you can then combine or pick from that list.

* The new \[BuildGradient\] operator turns a color list into a gradient. Note how you can also provide a float list to specify the step positions.

* There is a super-cool new \[HowToUseColors\] tutorial. As with all \[HowTos\] you should really take a close look at this interactive tutorial. It’s probably the fastest way to learn TiXL.

We also added tons of new number operators. Please check out the release notes for more details if you are into math and value lists.

Finally I want to point out the newly revamped Sketch operator. I still think it’s super useful to annotate existing animations or sketch out storyboards. Once again it features prominently in the “Ashborn” tutorial.

That was a serious load of updates. Looking at the intermediate roadmap you can already get some idea of what we have on the agenda for v4.1, which is planned for autumn.

As usual, we love to hear from you, either in the comments or on our Discord server\! We need you to move forward and contribute new feature ideas, suggestions, or even new operators. You might also share your designs with us on Instagram so that we can repost them.

Don’t forget to hit that subscribe button. And once again, thanks for watching.

