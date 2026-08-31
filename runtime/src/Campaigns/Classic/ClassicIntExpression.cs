namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed record ClassicIntExpression(
    string Kind,
    int Offset,
    int? Value,
    IReadOnlyList<ClassicIntExpression> Arguments);

internal sealed record ClassicIntExpressionContext(
    IReadOnlyDictionary<int, int> ProgramVariables,
    IReadOnlyDictionary<int, int> LocalVariables,
    IReadOnlyDictionary<int, int> ScriptLocalVariables,
    IReadOnlyDictionary<int, int> MapVariables,
    IReadOnlyDictionary<int, int> GlobalVariables,
    int DudeObject,
    int SelfObject,
    int CombatDifficulty,
    int DifficultyLevel,
    IReadOnlyDictionary<(int Object, int Stat), int> CritterStats,
    IReadOnlyDictionary<(int Rule, int Argument), int> MetaruleValues,
    IReadOnlyDictionary<int, int> SfallArrayLengths,
    IReadOnlyDictionary<(int MessageList, int MessageId), int> MessageHandles,
    IReadOnlyDictionary<string, int> ExternalVariables,
    int GameTime,
    int GameTimeHour,
    int Month,
    IClassicIntObjectFactory ObjectFactory);

internal sealed record ClassicIntExpressionValue(
    ClassicRetailRandomLifecycleState RandomState,
    int Value);

internal static class ClassicIntExpressionOwner
{
    internal static ClassicIntExpressionValue EvaluateRandomSite(
        ClassicMapIntRandomSite site,
        ClassicIntExpressionContext context,
        ClassicRetailRandomLifecycleState randomState,
        ClassicRetailRandomContract randomContract)
    {
        if (site.ExpressionStatus != "executable" ||
            site.MinimumExpression is null || site.MaximumExpression is null)
            throw new InvalidOperationException(
                $"Classic INT expression is unsupported: {site.SourceIdentity}.");
        var minimum = Evaluate(
            site.MinimumExpression, context, randomState, randomContract, site);
        var maximum = Evaluate(
            site.MaximumExpression, context, minimum.RandomState, randomContract, site);
        return Consume(
            minimum.RandomState,
            randomContract,
            site,
            site.Offset,
            minimum.Value,
            maximum.Value);
    }

    private static ClassicIntExpressionValue Evaluate(
        ClassicIntExpression expression,
        ClassicIntExpressionContext context,
        ClassicRetailRandomLifecycleState randomState,
        ClassicRetailRandomContract randomContract,
        ClassicMapIntRandomSite site)
    {
        if (expression.Kind == "literal" && expression.Value is { } literal &&
            expression.Arguments.Count == 0)
            return new ClassicIntExpressionValue(randomState, literal);
        var arguments = new List<int>();
        foreach (var argument in expression.Arguments)
        {
            var result = Evaluate(argument, context, randomState, randomContract, site);
            randomState = result.RandomState;
            arguments.Add(result.Value);
        }
        int value = expression.Kind switch
        {
            "program-variable" => RequiredVariable(
                context.ProgramVariables, arguments, expression, site),
            "local-variable" => RequiredVariable(
                context.LocalVariables, arguments, expression, site),
            "script-local-variable" => RequiredVariable(
                context.ScriptLocalVariables, arguments, expression, site),
            "map-variable" => RequiredVariable(
                context.MapVariables, arguments, expression, site),
            "global-variable" => RequiredVariable(
                context.GlobalVariables, arguments, expression, site),
            "dude-object" when arguments.Count == 0 => context.DudeObject,
            "self-object" when arguments.Count == 0 => context.SelfObject,
            "combat-difficulty" when arguments.Count == 0 => context.CombatDifficulty,
            "difficulty-level" when arguments.Count == 0 => context.DifficultyLevel,
            "critter-stat" => RequiredGameValue(
                context.CritterStats,
                arguments,
                expression,
                site),
            "metarule" => RequiredGameValue(
                context.MetaruleValues,
                arguments,
                expression,
                site),
            "sfall-array-length" => RequiredVariable(
                context.SfallArrayLengths, arguments, expression, site),
            "equal" => Boolean(arguments[0] == arguments[1]),
            "not-equal" => Boolean(arguments[0] != arguments[1]),
            "greater-than-or-equal" => Boolean(arguments[0] >= arguments[1]),
            "less-than" => Boolean(arguments[0] < arguments[1]),
            "add" => unchecked(arguments[0] + arguments[1]),
            "subtract" => unchecked(arguments[0] - arguments[1]),
            "multiply" => unchecked(arguments[0] * arguments[1]),
            "divide" when arguments[1] != 0 => arguments[0] / arguments[1],
            "modulo" when arguments[1] != 0 => arguments[0] % arguments[1],
            "and" => Boolean(arguments[0] != 0 && arguments[1] != 0),
            "or" => Boolean(arguments[0] != 0 || arguments[1] != 0),
            "bitwise-and" => arguments[0] & arguments[1],
            "not" => Boolean(arguments[0] == 0),
            "negate" => unchecked(-arguments[0]),
            "random-inclusive" => 0,
            _ => throw Unsupported(expression, site),
        };
        if (expression.Kind != "random-inclusive")
            return new ClassicIntExpressionValue(randomState, value);
        return Consume(
            randomState,
            randomContract,
            site,
            expression.Offset,
            arguments[0],
            arguments[1]);
    }

    private static ClassicIntExpressionValue Consume(
        ClassicRetailRandomLifecycleState state,
        ClassicRetailRandomContract contract,
        ClassicMapIntRandomSite site,
        int offset,
        int minimum,
        int maximum)
    {
        var result = ClassicRetailRandomLifecycle.Consume(
            state,
            contract,
            $"int-random:{site.Program}:{site.Procedure}:{offset:x}",
            site.SourceIdentity,
            minimum,
            maximum);
        return new ClassicIntExpressionValue(result.State, result.Value);
    }

    private static int RequiredVariable(
        IReadOnlyDictionary<int, int> variables,
        IReadOnlyList<int> arguments,
        ClassicIntExpression expression,
        ClassicMapIntRandomSite site)
    {
        if (arguments.Count != 1 || !variables.TryGetValue(arguments[0], out var value))
            throw Unsupported(expression, site);
        return value;
    }

    private static int RequiredGameValue(
        IReadOnlyDictionary<(int, int), int> values,
        IReadOnlyList<int> arguments,
        ClassicIntExpression expression,
        ClassicMapIntRandomSite site)
    {
        if (arguments.Count != 2 ||
            !values.TryGetValue((arguments[0], arguments[1]), out var value))
            throw Unsupported(expression, site);
        return value;
    }

    private static InvalidOperationException Unsupported(
        ClassicIntExpression expression,
        ClassicMapIntRandomSite site) =>
        new($"Classic INT expression failed at {site.SourceIdentity}:" +
            $"0x{expression.Offset:x}:{expression.Kind}.");

    private static int Boolean(bool value) => value ? 1 : 0;
}
