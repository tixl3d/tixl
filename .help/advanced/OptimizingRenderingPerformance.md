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

## Tips

- Disable frame-sync by clicking the performance graph in the application header.
- Disable parts of your render graph and compare the impact of each.
- Showing complex graphs in the graph window has its own performance cost.
