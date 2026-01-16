using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.Utilities;

public static class SvgFontConversion
{
    public static void Draw()
    {
        FormInputs.AddSectionHeader("SVG Font conversion");
        CustomComponents
           .HelpText("Single line SVG fonts can be used by the [LineTextPoints] operator to generate point set which can then be used for various effects.\n"
                     + "Creating this fonts can be surprisingly difficult. This is an internal helper we wrote to help.");
        FormInputs.AddVerticalSpace();
        FormInputs.SetIndent(120);

        var modified = false;
        modified |= FormInputs.AddFilePicker("SvgFile", ref _svgFilePath, "not-a-font.svg", null, null, FileOperations.FilePickerTypes.File);
        modified |= FormInputs.AddFloat("UnitsPerEm", ref _unitsPerEm, -1000, 2000, 1);
        modified |= FormInputs.AddFloat("Ascent", ref _svgFontAscent, 0, 2000, 1);
        modified |= FormInputs.AddFloat("Descent", ref _svgFontDescent, 0, 2000, 1);
        modified |= FormInputs.AddFloat("AdvanceX", ref _svgAdvanceX, 0, 2000, 1);
        FormInputs.AddCheckBox("Convert on change", ref _autoConvertOnParameterChange);

        FormInputs.ApplyIndent();
        var autoTriggered = _autoConvertOnParameterChange && modified;
        if (autoTriggered || CustomComponents.DisablableButton("Convert To SvgFont", File.Exists(_svgFilePath)))
        {
            ConvertSvgToSvgFont(_svgFilePath);
        }
    }

    private static void ConvertSvgToSvgFont(string filePath)
    {
        Log.Debug($"Converting {filePath} to SvgFont");
        if (!File.Exists(filePath))
            return;

        var xDoc = XDocument.Load(filePath);

        var svg = GetSingleElement(xDoc, _ns + "svg");
        if (svg == null)
            return;

        // Ignore and remove original def blocks
        foreach (var def in svg.Elements(_ns + "defs"))
        {
            def.Remove();
        }

        // Ignore and remove original rect blocks
        foreach (var rect in svg.Elements(_ns + "rect"))
        {
            rect.Remove();
        }

        var newDefs = new XElement(_ns + "defs");
        svg.Add(newDefs);

        var fontDef = new XElement(_ns + "font");
        fontDef.SetAttributeValue("horiz-adv-x", 50); // TODO: set correctly
        newDefs.Add(fontDef);

        var fontGroups = svg
                        .Elements(_ns + "g")?
                        .Where(c => AttrEndsWith(c, "-Font"));

        if (fontGroups?.Count() != 1)
        {
            Log.Warning($"Can't convert {filePath} to SvgFont. It requires 1 block with a '-Font' suffix.");
            Log.Warning($" Please read more: https://github.com/tixl3d/tixl/wiki/SvgLineFonts");
        }

        var fontFace = new XElement(_ns + "font-face");
        fontFace.SetAttributeValue("font-family", "Hershey Sans 1-stroke");
        fontFace.SetAttributeValue("units-per-em", _unitsPerEm);
        fontFace.SetAttributeValue("ascent", _svgFontAscent);
        fontFace.SetAttributeValue("descent", _svgFontDescent);
        fontFace.SetAttributeValue("cap-height", "500");
        fontFace.SetAttributeValue("x-height", "300");
        fontDef.Add(fontFace);

        var glyphGroups = Extensions.Elements<XElement>(fontGroups, _ns + "g");

        foreach (var g in glyphGroups)
        {
            var frameRect = GetSingleElement(g, _ns + "rect");
            if (frameRect == null)
                continue;

            _stringBuilder.Clear();
            foreach (var path in g.Elements(_ns + "path"))
            {
                var d1 = path.Attribute("d")?.Value;
                if (!string.IsNullOrEmpty(d1))
                {
                    _stringBuilder.Append(d1);
                    _stringBuilder.Append(" ");
                }
            }

            var d = _stringBuilder.ToString();

            // if (path == null)
            //     continue;

            var xTransform = GetTranslateOrDefault(frameRect);
            var width = GetFloatAttribute(frameRect, "width");
            var height = GetFloatAttribute(frameRect, "height");

            var id = g.Attribute("id")?.Value;

            if (string.IsNullOrEmpty(id))
            {
                Log.Warning("Skipping svg glyph with missing id");
                continue;
            }

            // If the name is not a single character it might be encoded as something like &#00901;
            if (id.Length > 1)
            {
                // Replace double encoded &
                id = id.Replace("&#38;", "&");

                // Try to replace encoded unicode characters
                id = System.Net.WebUtility.HtmlDecode(id);

                // Some special characters are saved with a strange encoding by Figma.
                // We try to decode them from Latin1 into UTF8 characters
                if (id.Length > 1)
                {
                    var bytes = Encoding.Latin1.GetBytes(id);
                    var converted = Encoding.UTF8.GetString(bytes);
                    Log.Debug($"Converted from {id} [{bytes}] -> '{converted}'");
                    id = converted;
                }
            }

            if (id.Length != 1)
            {
                Log.Debug($"Found group with incorrect Id '{id}'");
            }

            var newGlyph = new XElement(_ns + "glyph");
            newGlyph.SetAttributeValue("horiz-adv-x", width);
            newGlyph.SetAttributeValue("vert-origin-x", xTransform);
            newGlyph.SetAttributeValue("unicode", id);
            newGlyph.SetAttributeValue("glyph-name", id);
            newGlyph.SetAttributeValue("d", d);

            frameRect.Remove();
            fontDef.Add(newGlyph);
        }

        var folder = Path.GetDirectoryName(filePath);
        if (folder == null)
        {
            Log.Debug($"Can't extract folder from {filePath}");
            return;
        }

        var newFileName = Path.GetFileNameWithoutExtension(filePath) + "_font.svg";
        var newFilePath = Path.Combine(folder, newFileName);

        foreach (var group in fontGroups)
        {
            group.Remove();
        }

        xDoc.Save(newFilePath);

        Log.Debug($"  Saving {newFileName} with {fontDef.Elements().Count()} glyphs.");
    }

    private static XElement GetSingleElement(XContainer xElement, XName xName)
    {
        var elements = xElement.Elements(xName);
        return elements.Count() == 1
                   ? elements.First()
                   : null;
    }

    private static float GetTranslateOrDefault(XElement frameRect, float @default = 0)
    {
        var transformString = (string)frameRect.Attribute("transform");

        if (transformString == null)
            return @default;

        var result = Regex.Match(transformString, @"translate\((.*?)\)");
        if (!result.Success)
            return @default;

        return float.TryParse(result.Groups[1].Value, out var xx) ? xx : @default;
    }

    private static float GetFloatAttribute(XElement frameRect, string attributeName, float defaultValue = 0)
    {
        var valueString = frameRect.Attribute(attributeName);
        if (valueString == null)
            return defaultValue;

        return float.TryParse(valueString.Value, out var f) ? f : defaultValue;
    }

    private static bool AttrEndsWith(XElement c, string suffix)
    {
        var attr = c.Attribute("id");
        return attr?.Value != null && attr.Value.EndsWith(suffix);
    }

    private static float _unitsPerEm = -62f;
    private static float _svgFontAscent = 30f;
    private static float _svgFontDescent = 20f;
    private static float _svgAdvanceX = 50f;
    private static bool _autoConvertOnParameterChange = true;

    private static readonly XNamespace _ns = "http://www.w3.org/2000/svg";
    private static readonly StringBuilder _stringBuilder = new();
    private static string _svgFilePath;
}