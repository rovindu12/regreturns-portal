using System.Globalization;

using RegReturns.Domain.Common;

namespace RegReturns.Domain.Templates;

/// <summary>
/// An arithmetic expression over field values, used by cross-field rules (ADR 0021). The grammar is deliberately small:
/// decimal numbers, field references in brackets (<c>[TOTAL_HQLA]</c>), <c>+ - * /</c>, parentheses, unary minus and
/// the functions <c>Min</c>, <c>Max</c> and <c>Abs</c>. Arithmetic is in <see cref="decimal"/>, so results match what a
/// regulator computes by hand, and nothing in an expression can call code.
/// </summary>
public sealed class RuleExpression
{
    /// <summary>The functions an expression may call, with their minimum argument counts.</summary>
    private static readonly Dictionary<string, int> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Min"] = 2,
        ["Max"] = 2,
        ["Abs"] = 1,
    };

    private readonly INode _root;

    private RuleExpression(string text, INode root, IReadOnlySet<string> fieldCodes)
    {
        Text = text;
        _root = root;
        FieldCodes = fieldCodes;
    }

    /// <summary>Gets the expression as written (trimmed).</summary>
    public string Text { get; }

    /// <summary>Gets the field codes the expression refers to.</summary>
    public IReadOnlySet<string> FieldCodes { get; }

    /// <summary>Parses an expression.</summary>
    /// <param name="text">The expression, for example <c>[L1_HQLA] + [L2A_HQLA]</c>.</param>
    /// <returns>The parsed expression, or <see cref="TemplateErrors.InvalidExpression"/> naming the problem and its position.</returns>
    public static Result<RuleExpression> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return TemplateErrors.InvalidExpression.WithMessage("The expression is empty.");
        }

        var trimmed = text.Trim();
        if (trimmed.Length > ValidationRule.ExpressionMaxLength)
        {
            return TemplateErrors.InvalidExpression.WithMessage(
                $"The expression must be at most {ValidationRule.ExpressionMaxLength} characters.");
        }

        var parser = new Parser(trimmed);
        try
        {
            var root = parser.ParseAll();
            return new RuleExpression(trimmed, root, parser.Fields);
        }
        catch (ExpressionSyntaxException ex)
        {
            return TemplateErrors.InvalidExpression.WithMessage($"{ex.Message} (at character {ex.Position + 1}).");
        }
    }

    /// <summary>
    /// Evaluates the expression. Returns <see langword="null"/> when it has no value: a referenced field is blank or
    /// not a number, a division by zero, or a result too large for a decimal.
    /// </summary>
    /// <param name="valueOf">Returns a field's numeric value, or <see langword="null"/> if it has none.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public decimal? Evaluate(Func<string, decimal?> valueOf)
    {
        ArgumentNullException.ThrowIfNull(valueOf);
        try
        {
            return _root.Evaluate(valueOf);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public override string ToString() => Text;

    private interface INode
    {
        decimal? Evaluate(Func<string, decimal?> valueOf);
    }

    private sealed record NumberNode(decimal Value) : INode
    {
        public decimal? Evaluate(Func<string, decimal?> valueOf) => Value;
    }

    private sealed record FieldNode(string Code) : INode
    {
        public decimal? Evaluate(Func<string, decimal?> valueOf) => valueOf(Code);
    }

    private sealed record NegateNode(INode Operand) : INode
    {
        public decimal? Evaluate(Func<string, decimal?> valueOf) => -Operand.Evaluate(valueOf);
    }

    private sealed record BinaryNode(char Operator, INode Left, INode Right) : INode
    {
        public decimal? Evaluate(Func<string, decimal?> valueOf)
        {
            if (Left.Evaluate(valueOf) is not { } left || Right.Evaluate(valueOf) is not { } right)
            {
                return null;
            }

            return Operator switch
            {
                '+' => left + right,
                '-' => left - right,
                '*' => left * right,
                _ => right == 0m ? null : left / right,
            };
        }
    }

    private sealed record FunctionNode(string Name, IReadOnlyList<INode> Arguments) : INode
    {
        public decimal? Evaluate(Func<string, decimal?> valueOf)
        {
            var values = new List<decimal>(Arguments.Count);
            foreach (var argument in Arguments)
            {
                if (argument.Evaluate(valueOf) is not { } value)
                {
                    return null;
                }

                values.Add(value);
            }

            return Name switch
            {
                "min" => values.Min(),
                "max" => values.Max(),
                _ => Math.Abs(values[0]),
            };
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S3871:Exception types should be \"public\"",
        Justification = "Never leaves the parser: Parse turns it into a Result before returning.")]
    private sealed class ExpressionSyntaxException(string message, int position) : Exception(message)
    {
        public int Position { get; } = position;
    }

    /// <summary>Recursive-descent parser: expression = term (('+'|'-') term)*; term = unary (('*'|'/') unary)*.</summary>
    private sealed class Parser(string text)
    {
        // Deep nesting is never needed in a rulebook and would only risk a stack overflow.
        private const int MaxDepth = 32;

        private int _position;
        private int _depth;

        public HashSet<string> Fields { get; } = new(StringComparer.Ordinal);

        public INode ParseAll()
        {
            var node = ParseExpression();
            SkipWhitespace();
            return _position < text.Length
                ? throw Error($"Unexpected '{text[_position]}'")
                : node;
        }

        private INode ParseExpression()
        {
            if (++_depth > MaxDepth)
            {
                throw Error($"The expression is nested more than {MaxDepth} levels deep");
            }

            var node = ParseTerm();
            while (TryConsume('+', '-') is { } op)
            {
                node = new BinaryNode(op, node, ParseTerm());
            }

            _depth--;
            return node;
        }

        private INode ParseTerm()
        {
            var node = ParseUnary();
            while (TryConsume('*', '/') is { } op)
            {
                node = new BinaryNode(op, node, ParseUnary());
            }

            return node;
        }

        private INode ParseUnary()
        {
            if (TryConsume('-') is not null)
            {
                return new NegateNode(ParseUnary());
            }

            return TryConsume('+') is not null ? ParseUnary() : ParsePrimary();
        }

        private INode ParsePrimary()
        {
            SkipWhitespace();
            if (_position >= text.Length)
            {
                throw Error("The expression ends too early");
            }

            var c = text[_position];
            if (c == '(')
            {
                _position++;
                var inner = ParseExpression();
                Expect(')');
                return inner;
            }

            if (c == '[')
            {
                return ParseField();
            }

            if (char.IsAsciiDigit(c) || c == '.')
            {
                return ParseNumber();
            }

            return char.IsAsciiLetter(c) ? ParseFunction() : throw Error($"Unexpected '{c}'");
        }

        private FieldNode ParseField()
        {
            var start = _position++;
            var end = text.IndexOf(']', _position);
            if (end < 0)
            {
                throw new ExpressionSyntaxException("A field reference is missing its closing ']'", start);
            }

            var code = text[_position..end];
            if (code.Length == 0 || code.Length > TemplateField.CodeMaxLength || !char.IsAsciiLetterUpper(code[0])
                || !code.All(ch => char.IsAsciiLetterUpper(ch) || char.IsAsciiDigit(ch) || ch == '_'))
            {
                throw new ExpressionSyntaxException($"'[{code}]' is not a valid field code", start);
            }

            _position = end + 1;
            Fields.Add(code);
            return new FieldNode(code);
        }

        private NumberNode ParseNumber()
        {
            var start = _position;
            while (_position < text.Length && (char.IsAsciiDigit(text[_position]) || text[_position] == '.'))
            {
                _position++;
            }

            var literal = text[start.._position];
            return decimal.TryParse(literal, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                ? new NumberNode(value)
                : throw new ExpressionSyntaxException($"'{literal}' is not a number", start);
        }

        private FunctionNode ParseFunction()
        {
            var start = _position;
            while (_position < text.Length && char.IsAsciiLetter(text[_position]))
            {
                _position++;
            }

            var name = text[start.._position];
            if (!Functions.TryGetValue(name, out var minimumArguments))
            {
                throw new ExpressionSyntaxException(
                    $"Unknown function '{name}'; use Min, Max or Abs, and write fields as [CODE]", start);
            }

            Expect('(');
            var arguments = new List<INode> { ParseExpression() };
            while (TryConsume(',') is not null)
            {
                arguments.Add(ParseExpression());
            }

            Expect(')');
            var isAbs = string.Equals(name, "Abs", StringComparison.OrdinalIgnoreCase);
            if (arguments.Count < minimumArguments || (isAbs && arguments.Count != 1))
            {
                throw new ExpressionSyntaxException(
                    isAbs ? "Abs takes exactly one argument" : $"{name} takes at least {minimumArguments} arguments", start);
            }

            return new FunctionNode(name.ToLowerInvariant(), arguments);
        }

        private char? TryConsume(params char[] candidates)
        {
            SkipWhitespace();
            if (_position < text.Length && Array.IndexOf(candidates, text[_position]) >= 0)
            {
                return text[_position++];
            }

            return null;
        }

        private void Expect(char expected)
        {
            if (TryConsume(expected) is null)
            {
                throw Error(_position < text.Length ? $"Expected '{expected}' but found '{text[_position]}'" : $"Expected '{expected}'");
            }
        }

        private void SkipWhitespace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }

        private ExpressionSyntaxException Error(string message) => new(message, Math.Min(_position, text.Length - 1));
    }
}
