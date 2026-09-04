# Lib.point.io

## Operators

- [**DataPointConverter**](DataPointConverter.md) — Converts point data from CSV or JSON files into a GPU structured buffer of Point objects. It supports custom column mapping for CSV files and can export the converted points back to CSV or JSON.
- [**DataPointImportExport**](DataPointImportExport.md) — Imports a JSON point list into a GPU structured buffer and optionally exports it again. The node keeps the imported data in an internal buffer, so the points remain visible even when the import trigger is released. It also features an autoload capability that re-imports the file when the ImportFilePath is modified.
- [**LineTextPoints**](LineTextPoints.md) — Loads a single line SVG font and generates a point buffer for a text. This can then be rendered as lines, particle effects, or mesh.
- [**LoadSvg**](LoadSvg.md) — Loads an SVG file as points so it can be rendered as points or lines. The supported SVG feature set is very basic, and depending on your exporting applications, elements could be missing or not correctly represented. Missing SVG features include: - Masking - Text - Fill - Effects - Stroke styles like dashes The "ImportAs Lines" mode inserts double points so closed shapes will be closed when rendering lines. To draw the SVG, you can convert the list to a GPU point buffer with [ListToBuffer] and then use [DrawLines]. Also see: [PrepareSvgLineTransition], [SvgLineTransitionExample]
- [**PrepareSvgLineTransition**](PrepareSvgLineTransition.md) — This will iterate over the length of all line segments and compute offsets into W.

---

*Auto-generated from the operator library.*
