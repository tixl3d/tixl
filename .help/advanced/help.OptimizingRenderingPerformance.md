# Optimizing Render Performance

There is on one silver bullet. Typical bottlenecks could be:

1. **Fillrate**: Drawing many large particles on top of each other performance is slow. On my GTX2070 I can fill a 1080p buffer roughly 100 times.
2. **RenderTarget sizes**: I personally recommend to always start with your target resolution (e.g. 1080p) in the Output window resolution selector in the [Output window](T3UserInterface#output-window). Frequently the impact is not as drastic as you might expect.
3. **Sample counts**: Many image operators like [Blur](lib.img.adjust.Blur) sample the image many times to get a smooth gradient. Sample counts of 50 are typically okay. Frequently you need much less. Sometimes 200 or more is justified.
4. **RenderTarget.MultiSampling**: As a default we enabled 4xMSAA. Changing this setting on a complex scene (e.g. with millions of lines) will have a considerable performance impact.
5. **Lots of RenderTargets**: If possible reduce the number of "magenta" Ops and try to replace them with "cyan" command Ops.
6. **Changing Buffer sizes**: Animating the size of buffers is much(!) slower than changing its content. It should be fine for counts below 100.000. Above it might have a significant performance impact.
7. **Draw calls**: Rendering objects with individual draw calls (e.g. by using [Loop](lib.exec.Loop)) is extremely slow. Loop counts with more the 1000 iterations are unlikely to run in realtime.
8. **Multiple displays**: Your display setup can have a severe impact on render performance. Some tips:
  - In windows "Display" settings make sure that all displays use the same scaling. Set a "custom scale factor" if in doubt
  - Make sure non of your windows are overlapping display boundaries.
  - Sometimes I had to restart my machine to align this.

Don't make any assumptions -- measure! Graphic pipelines are extremely complex and highly optimized. Here are some false friends (or myths)

1. The difference between power of two and arbitrary texture resolutions (e.g. 256x256 vs. 100x100) is (for the most part) neglatable.
2. Generating MipMaps is very cheap.



Some tips:
1. Disable frame-sync by clicking the performance graph in [application header](T3UserInterface#appplication-header)
2. Disable parts of your render graph and compare the impact of various aspects.
3. Be aware that showing complex graphs in graph window also have a considerable performance impact.

