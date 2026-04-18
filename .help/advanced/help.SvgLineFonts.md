# Designing, Converting and rendering Single Line Fonts

![image](https://user-images.githubusercontent.com/1732545/211174728-bee5ee12-9d7f-4a14-8f44-5764b9ca392f.png)


## Preface
Single line fonts can be a very expressive content type for visual effects and typographic experiments. Sadly this it's on the outer fringe of creative and commercial interest so single line fonts and tools to create them are rare and finicky.

## Requirements
- Process for creating these fonts should support baseline, kerning, etc. made of a limited set of points or curves.
- Should work for MonoSpaced and varying spaced font.

_SvgFont_ is the goto file format for defining single line fonts. It's a well defined format. Sadly exporting single lines output is not really supported by most font design solutions.

## Things I have tried...

- Searching for fonts online gives surprisingly few results.
- Searching for editing tools that are capable of rendering / exporting: So far I've tried:
  - *GlyphsMini* - As far as I can tell, [it does not support exporting SLF](https://forum.glyphsapp.com/t/export-single-line-font/1140/5).
  - *Figma SVG-Export* - not feasible because the outline and width of glyphs is no longer accessible due to optimization. Rendered bounding boxes will only be exported if visible, then, well, they ARE visible.
  - *Figma PlugIns* - Introduces some artifacts and does not correctly set the unicode attribute.
  - *BirdFont* - Supports SvgFont export but outlines the stroke before export.

## My current solution

1. Design fonts in Figma. You can see [this example](https://www.figma.com/file/XIogCm20KLsruQYqHqwCWY/monono?node-id=3381%3A4844&t=wiowmCX7t4ErFaKS-1)
2. Add Frame with UniCode character titles or HTML-encoded titles for special chars (e.g. `&#32;` for space).
3. Make sure that these frames have a background fill color so we can derive glyph boundaries.
4. Group all glyph frames and mark for export to SVG.
5. Export SVG intermediate file with Id attributes enabled.
6. In TiXL.6 use the Convert SvgFont from the Utilities Windows

Sadly this process does not:
- Correctly transfer font information like baselines
- Support Kerning-Pairs

## In the long term
- Maybe BirdFont or other solutions eventually will [support exporting single line SVG fonts](https://github.com/johanmattssonm/birdfont/issues/146).

## Other resources

- [Specification MDN](https://developer.mozilla.org/en-US/docs/Web/SVG/Tutorial/SVG_fonts)
- [Discussing the Hershey Text extension for InkScape](https://www.evilmadscientist.com/2011/hershey-text-an-inkscape-extension-for-engraving-fonts/)
- [Svg Fonts repository with a selection of some single line fonts](https://gitlab.com/oskay/svg-fonts/)
- https://github.com/isdat-type/SingleLine_otf-svgMaker
- https://github.com/isdat-type/Relief-SingleLine
- http://cutlings.wasbo.net/single-line-fonts-options/
- [imajeenyus.com has a nice collection of single line fonts](http://www.imajeenyus.com/computer/20150110_single_line_fonts/index.shtml)
