namespace OpenNV.Runtime.Content;

internal sealed record FalloutScriptInspectionContext(FalloutScriptValueContext Values,
    Func<string, FalloutScriptFunction?> Function,
    Func<string, string, FalloutScriptFunction> UserFunction);
internal sealed record FalloutScriptStatementInspection(int Index, string Kind, string Operation,
    int PredicateOutcomes, string Ownership, string? Error, FalloutScriptExpressionSyntax? Expression = null);

internal sealed partial class FalloutGameModeProgram
{
    // Static coverage is independent of one saved state. Inspect every authored
    // arm and loop predicate; callbacks supply real declarations/signatures.
    // Legacy statement dispatch and state-dependent types remain separate lanes.
    internal IReadOnlyList<FalloutScriptStatementInspection> Inspect(FalloutScriptInspectionContext context,
        Action<string> inspectValue)
    {
        var result = new List<FalloutScriptStatementInspection>(_lines.Count);
        for (var index = 0; index < _lines.Count; ++index)
        {
            var tokens = _lines[index];
            var kind = tokens[0].ToLowerInvariant();
            var operation = kind[(kind.LastIndexOf('.') + 1)..];
            var ownership = "control-flow";
            string? error = null;
            FalloutScriptExpressionSyntax? syntax = null;
            try
            {
                IReadOnlyList<string>? expression = null;
                var nvse = true;
                switch (kind)
                {
                    case "if" or "elseif":
                        nvse = tokens.Length > 1 && tokens[1].Equals("eval", StringComparison.OrdinalIgnoreCase);
                        expression = tokens[(nvse ? 2 : 1)..];
                        break;
                    case "while": expression = tokens[1..]; break;
                    case "set":
                        if (tokens.Length < 4 || !tokens[2].Equals("to", StringComparison.OrdinalIgnoreCase))
                            throw new NotSupportedException("Script assignment syntax is unbound.");
                        inspectValue(tokens[1]);
                        nvse = false;
                        expression = tokens[3..];
                        break;
                    case "let" or "eval": expression = tokens[1..]; break;
                    case "else" or "endif" or "loop" or "break" or "continue" or "return":
                        if (tokens.Length != 1) throw new NotSupportedException("Control statement arguments are unbound.");
                        break;
                    default:
                        if (IsExpressionStatement(tokens) || context.Values.Arrays?.Function(tokens[0]) is not null)
                            expression = tokens;
                        else ownership = "statement-dispatch-not-inspected";
                        break;
                }
                if (expression is not null)
                {
                    syntax = FalloutNvseNumericExpression.Inspect(expression, context.Values, context.Function,
                        context.UserFunction, inspectValue, nvse);
                    ownership = "expression-declarations-and-signatures";
                }
            }
            catch (Exception failure) when (failure is InvalidDataException or InvalidOperationException or
                NotSupportedException or KeyNotFoundException or OverflowException)
            { error = failure.Message; ownership = "unbound"; }
            result.Add(new(index, kind, operation, kind is "if" or "elseif" or "while" ? 2 : 0, ownership, error, syntax));
        }
        return result;
    }
}
