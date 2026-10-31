#nullable enable
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using LinqExpression = System.Linq.Expressions.Expression;

namespace Lib.numbers.@float.basic;

/// <summary>
/// Evaluates a math formula with variables a, b, c, d. The formula is parsed into a compiled
/// delegate that is cached until its text changes. Example: (1 - a) + (b * 2 * c) - d / Sin(c)
/// </summary>
[Guid("a3f7c2e1-9b4d-4e8f-b6a1-2c5d8e0f3a7b")]
internal sealed class Expression : Instance<Expression>
{
    [Output(Guid = "b8e4d1f6-3a7c-4f2e-9d5b-1c6a8e0f4b2d")]
    public readonly Slot<float> Result = new();

    public Expression()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var expression = Formula.GetValue(context);
        var a = A.GetValue(context);
        var b = B.GetValue(context);
        var c = C.GetValue(context);
        var d = D.GetValue(context);

        if (string.IsNullOrWhiteSpace(expression))
        {
            Result.Value = float.NaN;
            return;
        }

        if (expression != _compiledExpression)
        {
            Compile(expression);
        }

        var evaluate = _evaluate;
        if (evaluate == null)
        {
            Result.Value = float.NaN;
            return;
        }

        Result.Value = evaluate(a, b, c, d);
    }

    private void Compile(string expression)
    {
        _compiledExpression = expression;
        _evaluate = null;

        try
        {
            _evaluate = FormulaParser.Compile(expression);
        }
        catch (FormatException e)
        {
            T3.Core.Logging.Log.Warning($"Expression '{expression}': {e.Message}", this);
        }
    }

    private delegate float ExpressionFn(float a, float b, float c, float d);

    private string? _compiledExpression;
    private ExpressionFn? _evaluate;

    /// <summary>Recursive descent parser. Formulas can only reach the variables, constants and functions listed here.</summary>
    private sealed class FormulaParser
    {
        public static ExpressionFn Compile(string text)
        {
            var parser = new FormulaParser(text);
            var body = parser.ParseSum();
            parser.SkipWhitespace();
            if (parser._position < text.Length)
                throw parser.Error($"Unexpected '{text[parser._position]}'");

            return LinqExpression.Lambda<ExpressionFn>(body, _variables).Compile();
        }

        private FormulaParser(string text)
        {
            _text = text;
        }

        private LinqExpression ParseSum()
        {
            var left = ParseProduct();
            while (true)
            {
                if (TryConsume('+'))
                {
                    left = LinqExpression.Add(left, ParseProduct());
                }
                else if (TryConsume('-'))
                {
                    left = LinqExpression.Subtract(left, ParseProduct());
                }
                else
                {
                    return left;
                }
            }
        }

        private LinqExpression ParseProduct()
        {
            var left = ParseUnary();
            while (true)
            {
                if (TryConsume('*'))
                {
                    left = LinqExpression.Multiply(left, ParseUnary());
                }
                else if (TryConsume('/'))
                {
                    left = LinqExpression.Divide(left, ParseUnary());
                }
                else if (TryConsume('%'))
                {
                    left = LinqExpression.Modulo(left, ParseUnary());
                }
                else
                {
                    return left;
                }
            }
        }

        private LinqExpression ParseUnary()
        {
            // The text can come from a connected string, and overflowing the stack would take down the whole process.
            if (++_nestingDepth > MaxNestingDepth)
                throw Error("Formula is nested too deeply");

            LinqExpression result;
            if (TryConsume('-'))
            {
                result = LinqExpression.Negate(ParseUnary());
            }
            else if (TryConsume('+'))
            {
                result = ParseUnary();
            }
            else
            {
                result = ParsePrimary();
            }

            _nestingDepth--;
            return result;
        }

        private LinqExpression ParsePrimary()
        {
            if (TryConsume('('))
            {
                var inner = ParseSum();
                Expect(')');
                return inner;
            }

            SkipWhitespace();
            if (_position >= _text.Length)
                throw Error("Unexpected end of formula");

            var next = _text[_position];
            if (char.IsAsciiDigit(next) || next == '.')
                return ParseNumber();

            if (char.IsAsciiLetter(next))
                return ParseName();

            throw Error($"Unexpected '{next}'");
        }

        private LinqExpression ParseNumber()
        {
            var start = _position;
            SkipDigits();
            if (_position < _text.Length && _text[_position] == '.')
            {
                _position++;
                SkipDigits();
            }

            if (_position < _text.Length && _text[_position] is 'e' or 'E')
            {
                var exponentStart = _position;
                _position++;
                if (_position < _text.Length && _text[_position] is '+' or '-')
                    _position++;

                // Without digits this "e" is not an exponent, so it is left to fail as an unexpected name.
                if (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
                {
                    SkipDigits();
                }
                else
                {
                    _position = exponentStart;
                }
            }

            var numberSpan = _text.AsSpan(start, _position - start);
            if (!float.TryParse(numberSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                _position = start;
                throw Error($"Invalid number '{numberSpan}'");
            }

            // Accepted so that values pasted from C# code keep working.
            if (_position < _text.Length && _text[_position] is 'f' or 'F')
                _position++;

            return LinqExpression.Constant(value);
        }

        private LinqExpression ParseName()
        {
            var start = _position;
            while (_position < _text.Length && char.IsAsciiLetterOrDigit(_text[_position]))
            {
                _position++;
            }

            var name = _text[start.._position];
            SkipWhitespace();

            if (_position < _text.Length && _text[_position] == '(')
                return ParseFunctionCall(name, start);

            if (name.Length == 1 && char.ToLowerInvariant(name[0]) - 'a' is >= 0 and < 4)
                return _variables[char.ToLowerInvariant(name[0]) - 'a'];

            if (_constants.TryGetValue(name, out var constant))
                return LinqExpression.Constant(constant);

            _position = start;
            throw Error($"Unknown name '{name}'");
        }

        private LinqExpression ParseFunctionCall(string name, int nameStart)
        {
            if (!_functions.TryGetValue(name, out var function))
            {
                _position = nameStart;
                throw Error($"Unknown function '{name}'");
            }

            Expect('(');
            var arguments = new List<LinqExpression>();
            if (!TryConsume(')'))
            {
                do
                {
                    arguments.Add(ParseSum());
                }
                while (TryConsume(','));

                Expect(')');
            }

            var expectedCount = function.GetParameters().Length;
            if (arguments.Count != expectedCount)
            {
                _position = nameStart;
                throw Error($"{name} takes {expectedCount} argument(s), not {arguments.Count}");
            }

            return LinqExpression.Call(function, arguments);
        }

        private bool TryConsume(char expected)
        {
            SkipWhitespace();
            if (_position >= _text.Length || _text[_position] != expected)
                return false;

            _position++;
            return true;
        }

        private void Expect(char expected)
        {
            if (!TryConsume(expected))
                throw Error($"Expected '{expected}'");
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }

        private void SkipDigits()
        {
            while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
            {
                _position++;
            }
        }

        private FormatException Error(string message)
        {
            return new FormatException($"{message} at character {_position + 1}");
        }

        private static float Clamp(float x, float min, float max) => x < min ? min : (x > max ? max : x);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float DegToRad(float deg) => deg * MathF.PI / 180f;
        private static float RadToDeg(float rad) => rad * 180f / MathF.PI;

        /** Unlike MathF.Sign this passes NaN through, which would otherwise throw during evaluation. */
        private static float Sign(float x) => x > 0 ? 1 : (x < 0 ? -1 : x);

        private static MethodInfo Function(Func<float, float> function) => function.Method;
        private static MethodInfo Function(Func<float, float, float> function) => function.Method;
        private static MethodInfo Function(Func<float, float, float, float> function) => function.Method;

        private const int MaxNestingDepth = 100;

        private static readonly ParameterExpression[] _variables =
            [
                LinqExpression.Parameter(typeof(float), "a"),
                LinqExpression.Parameter(typeof(float), "b"),
                LinqExpression.Parameter(typeof(float), "c"),
                LinqExpression.Parameter(typeof(float), "d"),
            ];

        private static readonly Dictionary<string, float> _constants = new(StringComparer.OrdinalIgnoreCase)
                                                                           {
                                                                               ["PI"] = MathF.PI,
                                                                               ["E"] = MathF.E,
                                                                           };

        private static readonly Dictionary<string, MethodInfo> _functions = new(StringComparer.OrdinalIgnoreCase)
                                                                                {
                                                                                    ["Sin"] = Function(MathF.Sin),
                                                                                    ["Cos"] = Function(MathF.Cos),
                                                                                    ["Tan"] = Function(MathF.Tan),
                                                                                    ["Asin"] = Function(MathF.Asin),
                                                                                    ["Acos"] = Function(MathF.Acos),
                                                                                    ["Atan"] = Function(MathF.Atan),
                                                                                    ["Atan2"] = Function(MathF.Atan2),
                                                                                    ["Sinh"] = Function(MathF.Sinh),
                                                                                    ["Cosh"] = Function(MathF.Cosh),
                                                                                    ["Tanh"] = Function(MathF.Tanh),
                                                                                    ["Sqrt"] = Function(MathF.Sqrt),
                                                                                    ["Pow"] = Function(MathF.Pow),
                                                                                    ["Exp"] = Function(MathF.Exp),
                                                                                    ["Log"] = Function((Func<float, float>)MathF.Log),
                                                                                    ["Log10"] = Function(MathF.Log10),
                                                                                    ["Abs"] = Function(MathF.Abs),
                                                                                    ["Floor"] = Function(MathF.Floor),
                                                                                    ["Ceil"] = Function(MathF.Ceiling),
                                                                                    ["Ceiling"] = Function(MathF.Ceiling),
                                                                                    ["Round"] = Function(MathF.Round),
                                                                                    ["Min"] = Function(MathF.Min),
                                                                                    ["Max"] = Function(MathF.Max),
                                                                                    ["Clamp"] = Function(Clamp),
                                                                                    ["Lerp"] = Function(Lerp),
                                                                                    ["Sign"] = Function(Sign),
                                                                                    ["DegToRad"] = Function(DegToRad),
                                                                                    ["RadToDeg"] = Function(RadToDeg),
                                                                                };

        private readonly string _text;
        private int _position;
        private int _nestingDepth;
    }

        [Input(Guid = "d0e6f3a9-5c2b-4d8f-9a4e-3b0c7f1d6e2a")]
        public readonly InputSlot<float> A = new InputSlot<float>();

        [Input(Guid = "e1f7a4b0-6d3c-4e9a-0b5f-4c1d8a2e7f3b")]
        public readonly InputSlot<float> B = new InputSlot<float>();

        [Input(Guid = "f2a8b5c1-7e4d-4f0b-1c6a-5d2e9b3f8a4c")]
        public readonly InputSlot<float> C = new InputSlot<float>();

        [Input(Guid = "a3b9c6d2-8f5e-4a1c-2d7b-6e3f0c4a9b5d")]
        public readonly InputSlot<float> D = new InputSlot<float>();

        [Input(Guid = "c9d5e2f8-4b1a-4c7e-8f3d-2a9b6e0c5d1f")]
        public readonly InputSlot<string> Formula = new InputSlot<string>();
}
