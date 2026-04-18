# What is TiXL

> *This is a draft. If you use TiXL and can describe it better than "visual programming environment for motion graphics", please edit.*

TiXL is a real-time visual programming environment for Windows. You compose visuals by connecting **operators** in a graph: one operator generates an image, another adds bloom, another wraps it on a torus, another exports to a projector. The whole graph evaluates every frame, at display rate.

It is built with C# and DirectX 11; its UI is ImGui-based; its effects pipeline uses HLSL compute and pixel shaders. Because you can also export interactive applications, it is simultaneously a live tool *and* a production pipeline for standalone pieces.

## What people use TiXL for

- **Motion graphics** and short-form visuals — title sequences, music videos, generative loops.
- **Live VJing** — react to MIDI, audio, OSC, or a projected DJ set; blend snapshots on the fly.
- **Audiovisual installations** — projection mapping, interactive captures via NDI / Spout, sensor-driven behavior.
- **Exploratory shader work** — the `[CustomPixelShader]` / `[CustomPointShader]` operators combine TiXL's preset system with live HLSL editing.
- **Small demos** — exporting a whole scene as a standalone executable.

## Compared with…

- **TouchDesigner** — broader feature set, commercial, Python-scriptable. TiXL is narrower, faster to pick up for code-adjacent artists, free, with C# instead of Python.
- **vvvv** — deep shared lineage in the demo scene. Both are free; both are DirectX. TiXL leans harder on timeline / animation workflows and preset blending.
- **Notch** — commercial, GPU-effects-focused. Similar real-time intent; different licensing and ecosystem.
- **After Effects** — pre-rendered compositing. TiXL is real-time; it's a different shape of tool.

## What TiXL is not

- It is not a DAW — audio is input/reaction, not editing.
- It is not a general-purpose game engine — no physics, no runtime scripting beyond C# operators.
- It is not yet a Mac or Linux-native application. See the [install pages](../install/README.md) for Wine / Sikarugir workarounds.
