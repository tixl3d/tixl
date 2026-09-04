# Using TiXL for Live Performances

In live performance situations, TiXL stands out for its real-time rendering effects and excellent support for MIDI, OSC, and audio inputs. It has been battle-proven in many performances and live VJ sets and is well-suited for both controlled, rehearsed art performances and free on-the-fly coding and patching.

TiXL has been developed to take VJing from "rehashing canned video snippets" to crafting and designing visuals on the fly. This article will guide you through setting up and getting started with TiXL.



## Inputs

TiXL supports many input channels that you can use to control or sync your visuals, including:

- MIDI input for controlling parameters, launching scenes, blending presets and snapshots, and syncing time code to MIDI clock
- SpaceMouse for flying around in 3D scenes with smooth motions
- AudioReaction for triggering events and parameters to the current input audio level
- OSC input for using connected interfaces like Hexler’s TouchOsc
- NDI input for connecting to video sources like a smartphone screen capture 
- Spout input for connecting to other video signals provided by applications running on your machine

## Outputs

Currently, TiXL can only handle one additional fullscreen display output. This means you have the following options:

- Duplicate the display and use the output in the background (this is great for live patching) and sharing with the audience what you're doing.
- Extend to two displays and use the primary notebook screen for controlling TiXL and the secondary display as fullscreen output connected via HDMI, USB-C, or DisplayPort.
- Use various other tricks to output and split video signals (like HDMI mixers, splitters, Spout output, etc.).

## Overview

Here are the steps for starting a simple setup for live patching:

1. Connect your USB controllers.
1. Connect the second display.
1. Press Windows-Key+P and switch to extend.
1. Use Display Settings advanced and make sure the connected display is running with 1920x1080x60fps. Other resolutions are okay, but a different framerate can impact performance.
1. Start TiXL (either as Stand-Alone or via IDE). For performance reasons, I prefer Release mode when starting from IDE.
1. If you already have a project, open it by double-clicking. Otherwise, create a new project by selecting "New → Empty Project" (giving it a meaningful title won't hurt). Then, double-click your new project to open it.
1. Enable "Window → 2nd Render Window." This will open a second TiXL window that mirrors the operator pinned to the output panel. If the two windows are swapped (i.e. the main window is shown on your projector), activate "Windows → Settings → User Interface → Fullscreen Window Swap."
1. For performance reasons, the output result will only be rendered once and used for both the output panel of TiXL's main window and the render output window. For that reason, you need to adjust the resolution of your output and switch the resolution selector in the output panel toolbar from "Fill" to "1080p".
1. If not already, switch to full-screen mode (View → Fullscreen).
1. Create an operator with a texture output, e.g. [RyojiPattern2].
1. Pin that operator to the output either using the pin icon in the output panel toolbar or by pressing "P" on your keyboard.

## Connecting Audio Reaction

To adjust the audio input and playback settings for your project, follow the steps below. If you’re currently on your Home canvas, create and open a project before proceeding.

1. Click on the gear icon of the timeline toolbar in your project to open the play settings.
1. Check the box “Specify settings for <Your Project>”.
1. Switch the Audio Source to External Device.
1. Use the drop-down menu under Input Device to choose where the audio comes from. Leave it on **Default Audio Input** to use the device configured for this machine — this keeps the project portable, so it still works when opened on another computer. The machine’s default device is set right below (and also under Settings → Audio → Default Input Device); set it once per machine. If a project needs a specific device regardless of machine, pick that device explicitly here instead. Check the gain-meter of the input level to ensure it’s working correctly. Note that these devices are provided by Windows’ internal WASAPI audio API, so you may need to adjust the device properties, drivers, etc. until you see the correct device listed and working.
1. If you don’t have an audio-input device that provides line-in, you can use your notebook’s microphone as a workaround. However, [a cheap USB-audio card](https://www.amazon.de/usb-soundkarte/s?k=usb+soundkarte) will do the trick more effectively.
1. Create an [AudioReaction] (see [this YouTube tutorial](https://www.youtube.com/watch?v=M67r-D4R5WM) for more details).
1. Connect the output of the [AudioReaction] to a [SampleGradient] operator.
1. Connect the color output of [SampleGradient] to a parameter of an operator visible in the pinned output.
1. The audio reaction is quite flexible, and you can experiment with different settings, such as:
    - Switching the OutputMode from “Pulse” to "Level" or "AccumulatedLevel" (good for rotations).
    - Inserting a [Damp] operator between the output and where it’s used.
    - Inserting a [Multiply] operator and connecting the 2nd multiply parameter to a [MidiInput] to control the strength of the result.
    - Connecting a [TriggerAnimation] to the .WasHit output of the [AudioReaction].
    - Checking out the [HowToAnimatedThings] tutorial for more ideas.

All these settings will be saved with your project, meaning that the next time you open it with TiXL, everything should be as you left it.

## Connecting and using MIDI Controllers

The central operator for MIDI is [MidiInput]. You can create it by:

- Pressing the Tab key on your keyboard and searching for MIDI.
- Finding the operator in the symbol library in the lib/io/midi/ namespace and dragging it onto your canvas.
- Finding the operator in the main menu under Add → Lib → Io → Midi.
- The coolest option is finding the parameter you want to control and dragging the “+” icon on the left side of the parameter name onto the graph canvas, searching for “midi…” and pressing Return. This will immediately create the connection to the parameter.

This tutorial also showcases some of the live performance features:

[![Introduction](https://img.youtube.com/vi/QJ0-qZgS8Dw/maxresdefault.jpg)](https://www.youtube.com/watch?v=QJ0-qZgS8Dw)


### Syncing using MIDI Clock

You can use [LinkToMidiTime] to control the playback time using a MIDI clock. The setup might look like this. It works great because TiXL’s internal time unit is the bar, so you can use operators like [AnimValue] synced out of the box. Here is an example of how this could look:
![image](https://user-images.githubusercontent.com/1732545/236685098-b918e12f-71ad-4440-bd02-d9cdcc914e0e.png)

You can use a virtual MIDI device like [LoopBe1](https://www.nerds.de/en/loopbe1.html) to forward the MIDI events.

## Recording a session

You can capture a live performance — audio and the MIDI / OSC events that drove it — with a single click on the timeline toolbar's record button, then replay it later to tune the visuals against the exact session you played. See [Recording](./Recording.md) for details.

## Advanced Steps

In addition to the basic setups described above, TiXL offers many advanced features that can help you create complex, sophisticated live performance setups. Here are a few examples:

- Snapshots: With snapshots, you can save and recall the state of your project at any time, allowing you to quickly switch between different setups during a performance. This can be especially useful if you have multiple songs or pieces with different instrumentation, tempo, and effects.
- Midi Controllers: By using external midi controllers, you can interact with your project in real-time, adding an extra layer of expressiveness and control. TiXL is compatible with most midi controllers on the market, including the popular Novation Launchpad and Akai APC series.
- Time Features: TiXL's time features include the ability to sync to external midi clocks, set the BPM of your project, and create time-based animations and effects. This can be useful for synchronizing your visuals with other elements of your performance, such as lighting or pyrotechnics.

For example, in a recent project, we used a combination of snapshots, midi controllers, and time features to create a complex live performance setup for a music festival. Our setup included multiple layers of visuals, synced to both Ableton Live and we used TiXL's time-based operators to create dynamic animations that changed with the tempo of the music.

For [WAKE ](https://wake-audiovisual.com/)our setup looks like this:
![image](https://user-images.githubusercontent.com/1732545/236685444-b8eebc17-f8a6-4048-8e98-aaebb8774f3d.png)

Lucid is driving the audio with a complex setup in Ableton live using, Push, Microkorg, Keyboard and other live instruments. We then use [MidiPipe](http://www.subtlesoft.square7.net/MidiPipe.html) to forward all MidiEvents through a network node:
![image](https://user-images.githubusercontent.com/1732545/236685431-cf8bc390-08da-448f-9347-19d5d944a7e3.png)

On windows we use Tobias Erichsen’s [rtpMidi ](https://www.tobias-erichsen.de/software/rtpmidi.html)to create a virtual midi device to connect to. We then use a setup of defined and rehearse midi signals to drive events, link the midi clock or use the [SetBpm] operator to switch between the different BPM’s for different tracks. Here are [a couple of videos](https://www.youtube.com/@wakeaudiovisual) how this can look.

## Future Features

TiXL is constantly evolving, with new features and capabilities being added all the time. Some of the upcoming features on our roadmap include:

Improved Video Playback: We are working on improving the video playback capabilities of TiXL, with support for high-performance codecs like HAP and NotchLC.

Sensor Integration: We are exploring ways to integrate more sensors into TiXL, including the Kinect Azure and other depth cameras.

Multiple Display Outputs: We are working on improving TiXL's support for multiple display outputs, so you can control multiple screens or projectors from a single machine.

Projection Mapping: We are developing new tools for projection mapping, allowing you to create dynamic, immersive visual experiences on any surface.

Multi-Client Rendering: We are working on a new feature that will allow you to distribute the rendering load across multiple machines, making it possible to create even larger and more complex visual setups.

## Conclusion

TiXL is a powerful tool for creating live visual performances, with a wide range of features and capabilities that can help you bring your ideas to life. Whether you're a musician, a VJ, or an artist, TiXL offers a flexible, customizable platform for creating stunning visual experiences. With its intuitive interface and powerful tools, TiXL makes it easy to create everything from simple visualizations to complex, multi-layered performances. So why not give it a try and see what you can create?

As always, if you have any questions, ideas or suggestions, [join us on Discord](https://discord.com/invite/YmSyQdeH3S) and let us know.

