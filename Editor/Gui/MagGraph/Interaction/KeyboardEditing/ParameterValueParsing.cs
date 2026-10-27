#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator.Slots;

namespace T3.Editor.Gui.MagGraph.Interaction.KeyboardEditing;

/// <summary>
/// Parses values typed into the parameter popover: numbers and expressions, vector components, colors and bools.
/// </summary>
internal static class ParameterValueParsing
{
    internal static bool TryParseValue(InputValue current, string text, [NotNullWhen(true)] out InputValue? result)
    {
        result = null;
        switch (current)
        {
            case InputValue<float> floatValue:
                if (!TryEvaluateNumber(floatValue.Value, text, out var floatResult))
                    return false;

                result = new InputValue<float>((float)floatResult);
                return true;

            case InputValue<int> intValue:
                if (!TryEvaluateNumber(intValue.Value, text, out var intResult))
                    return false;

                result = new InputValue<int>((int)Math.Round(intResult));
                return true;

            case InputValue<bool>:
                if (!TryParseBool(text, out var boolResult))
                    return false;

                result = new InputValue<bool>(boolResult);
                return true;

            case InputValue<Vector2> v2:
                Span<double> c2 = [v2.Value.X, v2.Value.Y];
                if (!TryEvaluateComponents(c2, text))
                    return false;

                result = new InputValue<Vector2>(new Vector2((float)c2[0], (float)c2[1]));
                return true;

            case InputValue<Vector3> v3:
                Span<double> c3 = [v3.Value.X, v3.Value.Y, v3.Value.Z];
                if (!TryEvaluateComponents(c3, text))
                    return false;

                result = new InputValue<Vector3>(new Vector3((float)c3[0], (float)c3[1], (float)c3[2]));
                return true;

            case InputValue<Vector4> v4:
                if (!TryEvaluateColor(v4.Value, text, out var color))
                    return false;

                result = new InputValue<Vector4>(color);
                return true;

            case InputValue<Int2> i2:
                Span<double> ci2 = [i2.Value.X, i2.Value.Y];
                if (!TryEvaluateComponents(ci2, text))
                    return false;

                result = new InputValue<Int2>(new Int2((int)Math.Round(ci2[0]), (int)Math.Round(ci2[1])));
                return true;

            case InputValue<Int3> i3:
                Span<double> ci3 = [i3.Value.X, i3.Value.Y, i3.Value.Z];
                if (!TryEvaluateComponents(ci3, text))
                    return false;

                result = new InputValue<Int3>(new Int3((int)Math.Round(ci3[0]), (int)Math.Round(ci3[1]), (int)Math.Round(ci3[2])));
                return true;
        }

        return false;
    }

    /// <summary>
    /// "0 1 0" sets components in order, a single value ("*2") applies to all of them, and a component letter
    /// addresses one: "x 1", "y+2". Components that aren't mentioned keep their value.
    /// </summary>
    internal static bool TryEvaluateComponents(Span<double> components, string text, string componentLetters = ComponentLetters)
    {
        var tokens = text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return false;

        if (tokens.Length == 1 && componentLetters.IndexOf(char.ToLowerInvariant(tokens[0][0])) < 0)
        {
            for (var index = 0; index < components.Length; index++)
            {
                if (!TryEvaluateNumber(components[index], tokens[0], out components[index]))
                    return false;
            }

            return true;
        }

        var positionalIndex = 0;
        for (var tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
        {
            var token = tokens[tokenIndex];
            var componentIndex = componentLetters.IndexOf(char.ToLowerInvariant(token[0]));
            if (componentIndex >= 0)
            {
                token = token.Length > 1 ? token[1..] : tokenIndex + 1 < tokens.Length ? tokens[++tokenIndex] : string.Empty;
            }
            else
            {
                componentIndex = positionalIndex++;
            }

            if (componentIndex >= components.Length || !TryEvaluateNumber(components[componentIndex], token, out components[componentIndex]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// A color name or hex code ("orange", "#f80") optionally followed by channels ("orange a 0.5"), or numbers like
    /// vectors where r g b a address channels. A single number ("*0.5") changes RGB and keeps alpha.
    /// </summary>
    internal static bool TryEvaluateColor(Vector4 current, string text, out Vector4 color)
    {
        color = current;
        var firstTokenEnd = text.IndexOf(' ');
        var firstToken = firstTokenEnd < 0 ? text : text[..firstTokenEnd];
        var rest = firstTokenEnd < 0 ? string.Empty : text[(firstTokenEnd + 1)..];

        // A single channel letter with a value ("b 0.5") addresses that channel, not a name starting with it
        var isChannel = firstToken.Length == 1 && ChannelLetters.IndexOf(char.ToLowerInvariant(firstToken[0])) >= 0 && rest.Length > 0;
        if (!isChannel
            && (ColorValueParsing.TryParseHex(firstToken, out var parsedColor)
                || ColorValueParsing.TryFindName(firstToken, out _, out parsedColor)))
        {
            color = parsedColor;
            if (rest.Length == 0)
                return true;

            text = rest;
        }
        else if (firstTokenEnd < 0 && ChannelLetters.IndexOf(char.ToLowerInvariant(text[0])) < 0)
        {
            Span<double> rgb = [color.X, color.Y, color.Z];
            if (!TryEvaluateComponents(rgb, text))
                return false;

            color = new Vector4((float)rgb[0], (float)rgb[1], (float)rgb[2], color.W);
            return true;
        }

        Span<double> components = [color.X, color.Y, color.Z, color.W];
        if (!TryEvaluateComponents(components, text, ChannelLetters))
            return false;

        color = new Vector4((float)components[0], (float)components[1], (float)components[2], (float)components[3]);
        return true;
    }

    /// <summary>
    /// A number or expression ("100*2+1"). A leading + * / or ^ applies it to the current value, which vectors and
    /// colors use for all components ("*2"); a leading "=" makes it absolute ("=-5").
    /// </summary>
    internal static bool TryEvaluateNumber(double current, string text, out double result)
    {
        result = current;
        if (text.Length == 0)
            return false;

        if (text[0] == '=')
            return ValueExpression.TryEvaluate(text[1..], out result);

        if (text[0] is '+' or '*' or '/' or '^')
            return ValueExpression.TryEvaluate(current.ToString("R", CultureInfo.InvariantCulture) + text, out result);

        return ValueExpression.TryEvaluate(text, out result);
    }

    internal static bool TryParseBool(string text, out bool result)
    {
        switch (text.ToLowerInvariant())
        {
            case "1" or "true" or "on" or "y" or "yes":
                result = true;
                return true;
            case "0" or "false" or "off" or "n" or "no":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }

    internal static InputValue CreateEnumValue(Type enumType, int value)
    {
        var valueType = typeof(InputValue<>).MakeGenericType(enumType);
        return (InputValue)Activator.CreateInstance(valueType, Enum.ToObject(enumType, value))!;
    }

    internal static object GetEnumValue(InputValue value)
    {
        return value.GetType().GetField("Value")?.GetValue(value) ?? 0;
    }

    internal static string FormatNumber(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    internal const string ComponentLetters = "xyzw";
    internal const string ChannelLetters = "rgba";
}
