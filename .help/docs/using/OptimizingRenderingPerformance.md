# Optimizing render performance

There's no silver bullet. Typical bottlenecks:

1. **Fillrate.** Drawing many large, overlapping particles is slow. On a GTX 2070 a 1080p buffer can be overdrawn roughly 100 times before you hit the limit.
2. **RenderTarget sizes.** Start with your target resolution (e.g. 1080p) in the output-window resolution selector. The cost is often smaller than you expect.
3. **Sample counts.** Image operators like `[Blur]` sample the image many times to produce a smooth gradient. 50 samples is usually fine; you often need fewer. Above 200, justify each sample.
4. **Multisampling.** The default is 4× MSAA. Disabling it on a complex scene (millions of lines) can have a large effect.
5. **Too many RenderTargets.** Where possible, reduce the number of "magenta" ops and replace them with "cyan" command ops.
6. **Animating buffer sizes.** Changing the size of a buffer is much slower than changing its content. Under 100,000 elements is fine; above that, resizes can dominate frame time.
7. **Draw calls.** Rendering objects via individual draw calls (e.g. with `[Loop]`) is extremely slow. Loop counts above ~1000 iterations are unlikely to run in real time.
8. **Multiple displays.** Your display setup can strongly affect performance:
   - Make sure every display uses the same scaling factor in Windows "Display" settings. Set a custom scale factor if in doubt.
   - Keep windows from overlapping display boundaries.
   - Sometimes restarting the machine is needed to get this to stick.

Don't assume — measure. Graphics pipelines are complex and highly optimized. A few myths:

- The difference between power-of-two and arbitrary texture sizes (256×256 vs 100×100) is usually negligible.
- Generating mipmaps is very cheap.

## Measuring your machine

The **Benchmark** window (**Help → Development Tools → Benchmark**, or **Benchmark...** in the Performance window) measures how this machine
handles three kinds of load:

- **CPU:** evaluating operators. A [Loop] runs a small chain of six operators more and more often.
- **Compute:** points running through [AddNoise] and [TransformPoints], from 10 up to 10 million.
- **Draw:** blended full-screen layers at 1920×1080.

Click **Run benchmark** and leave the editor alone for about a minute and a half. VSync is off during the run. Each
test raises its amount step by step (1, 2, 5, 10, 20, …) until a step takes longer than 50 ms, then goes back down
and up again, so every step is measured three times.
Both axes of the graphs are logarithmic, so equal distances mean equal factors.

The vertical line in each graph is the **Reference**: a typical amount for that kind of work, e.g. 500K points.
The title shows how long this machine needs for it and a grade from A+ (as fast as current high-end machines) to
F (too slow for realtime): green for A+ and A, blue for B and C, red for D and F. Hover the title to see the
range of each grade. Debug builds evaluate about 2.5× slower, so their CPU grade range is stretched by that factor.

The blue band around a curve shows the spread between the three passes. A wide band or a marked
point means the machine wasn't in a steady state: it heated up and slowed down, its clocks changed, or something
else was running. Run it again with the charger connected and other programs closed.

Every run is saved as a file. **Open folder** shows where. To compare machines, operating systems or TiXL
versions, copy the result files of the other runs into that folder and tick them under **Compare**. **Copy
summary** puts a small table with the Reference results on the clipboard, handy for bug reports.

**History** lists the earlier runs of this machine with the same build, newest first, with the commit TiXL was
built from. Each value shows its change against the run below it: green when it got more than 10 % faster, red
when it got slower. A `*` means the test itself changed in between, so the numbers aren't comparable.

Debug builds of TiXL and the graphics validation layer make the CPU much slower. The window says so, and CPU
results are only comparable with runs of the same kind.

## Tips

- Disable frame-sync by clicking the performance graph in the application header.
- Disable parts of your render graph and compare the impact of each.
- Showing complex graphs in the graph window has its own performance cost.
