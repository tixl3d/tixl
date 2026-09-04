# Lib.image.fx.stylize

## Operators

- [**AsciiRender**](AsciiRender.md) — Draws the incoming image as shaded ASCII characters, similar to 'The Matrix'.
- [**ChromaticAbberation**](ChromaticAbberation.md) — Simulates an imaging error of optical camera lenses that manifests itself as blurring or discoloration at the outer edges.
- [**ColorPhysarum**](ColorPhysarum.md) — Work in progress for a new edge detect like effect.
- [**DetectEdges**](DetectEdges.md) — Detects edges in the incoming image.
- [**Dither**](Dither.md) — Applies Floyd-Steinberg dithering to an image to convert it to black and white colors.
- [**FakeLight**](FakeLight.md) — Applies fake shading to the image.
- [**Glow**](Glow.md) — Adds a glow effect to the incoming image. It uses multiple resolution passes and is reasonably fast.
- [**HoneyCombTiles**](HoneyCombTiles.md) — Creates a hexagonal pattern based on an incoming image.
- [**LightRaysFx**](LightRaysFx.md) — Based on "Simpler God Rays" by akufishi
- [**MosiacTiling**](MosiacTiling.md) — Subdivides the incoming image into recursive tiles.
- [**Pixelate**](Pixelate.md) — TilesAmount parameter works only if Divisor = 0. If Divisor is greater than 0, it will divide the resolution of your image and try to keep the tiles close to a square shape.
- [**ScreenCloseUp**](ScreenCloseUp.md) — Uses the incoming 2D image and puts it on a virtual LCD/screen in a 3D scene that looks as if it is being filmed by a real camera (including depth of field etc)
- [**StarGlowStreaks**](StarGlowStreaks.md) — Renders streaks onto the incoming image. You can use the threshold to limit the range that is used for streaks. Also, check out the presets.
- [**Steps**](Steps.md) — A versatile effect that uses the value of the input image to draw 'steps'. These can be shaded and animated.
- [**VoronoiCells**](VoronoiCells.md) — Creates a Voronoi cell pattern based on an incoming image.

---

*Auto-generated from the operator library.*
