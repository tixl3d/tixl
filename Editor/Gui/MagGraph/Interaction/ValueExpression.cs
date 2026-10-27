#nullable enable
using System.Globalization;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Evaluates small arithmetic expressions typed as parameter values: + - * / % ^, parentheses, unary minus and pi,
/// e.g. "100*2+1" or "(1+2)^2".
/// </summary>
internal static class ValueExpression
{
    internal static bool TryEvaluate(string text, out double result)
    {
        var position = 0;
        if (!TryParseSum(text, ref position, out result))
            return false;

        SkipSpaces(text, ref position);
        return position == text.Length && double.IsFinite(result);
    }

    private static bool TryParseSum(string text, ref int position, out double result)
    {
        if (!TryParseProduct(text, ref position, out result))
            return false;

        while (TryTakeOperator(text, ref position, "+-", out var operation))
        {
            if (!TryParseProduct(text, ref position, out var right))
                return false;

            result = operation == '+' ? result + right : result - right;
        }

        return true;
    }

    private static bool TryParseProduct(string text, ref int position, out double result)
    {
        if (!TryParsePower(text, ref position, out result))
            return false;

        while (TryTakeOperator(text, ref position, "*/%", out var operation))
        {
            if (!TryParsePower(text, ref position, out var right))
                return false;

            result = operation switch
                         {
                             '*' => result * right,
                             '/' => result / right,
                             _   => result % right,
                         };
        }

        return true;
    }

    private static bool TryParsePower(string text, ref int position, out double result)
    {
        if (!TryParseUnary(text, ref position, out result))
            return false;

        if (!TryTakeOperator(text, ref position, "^", out _))
            return true;

        // Right associative: 2^3^2 = 2^9
        if (!TryParsePower(text, ref position, out var exponent))
            return false;

        result = Math.Pow(result, exponent);
        return true;
    }

    private static bool TryParseUnary(string text, ref int position, out double result)
    {
        if (TryTakeOperator(text, ref position, "-+", out var sign))
        {
            if (!TryParseUnary(text, ref position, out result))
                return false;

            if (sign == '-')
                result = -result;

            return true;
        }

        return TryParsePrimary(text, ref position, out result);
    }

    private static bool TryParsePrimary(string text, ref int position, out double result)
    {
        result = 0;
        SkipSpaces(text, ref position);
        if (position >= text.Length)
            return false;

        if (text[position] == '(')
        {
            position++;
            if (!TryParseSum(text, ref position, out result))
                return false;

            return TryTakeOperator(text, ref position, ")", out _);
        }

        if (string.Compare(text, position, "pi", 0, 2, StringComparison.OrdinalIgnoreCase) == 0)
        {
            position += 2;
            result = Math.PI;
            return true;
        }

        var start = position;
        while (position < text.Length && (char.IsDigit(text[position]) || text[position] == '.'))
        {
            position++;
        }

        // Exponent notation like 1e-3
        if (position < text.Length && position > start && text[position] is 'e' or 'E')
        {
            var exponentEnd = position + 1;
            if (exponentEnd < text.Length && text[exponentEnd] is '+' or '-')
                exponentEnd++;

            if (exponentEnd < text.Length && char.IsDigit(text[exponentEnd]))
            {
                position = exponentEnd;
                while (position < text.Length && char.IsDigit(text[position]))
                {
                    position++;
                }
            }
        }

        return position > start
               && double.TryParse(text.AsSpan(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryTakeOperator(string text, ref int position, string operators, out char operation)
    {
        SkipSpaces(text, ref position);
        if (position < text.Length && operators.Contains(text[position]))
        {
            operation = text[position];
            position++;
            return true;
        }

        operation = default;
        return false;
    }

    private static void SkipSpaces(string text, ref int position)
    {
        while (position < text.Length && text[position] == ' ')
        {
            position++;
        }
    }
}
