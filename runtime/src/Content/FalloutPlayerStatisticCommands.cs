using System.Globalization;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal static class FalloutPlayerStatisticCommands
{
    internal static FalloutScriptFunction? Function(string operation, FalloutPlayerStatistics? owner) => operation switch
    {
        "getpcmiscstat" => new([FalloutScriptArgumentKind.Value], arguments =>
            Require(owner).Read(Index(Require(owner), arguments[0].Value)))
        { ReadOnly = true },
        "modpcmiscstat" => new([FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.Number], arguments =>
        {
            var current = Require(owner); var delta = Integer(arguments[1].Number);
            current.Mod(delta == 0 ? (ushort)0 : Index(current, arguments[0].Value), delta, "script-ModPCMiscStat"); return 0;
        }),
        _ => null,
    };
    internal static bool Apply(string operation, IReadOnlyList<string> arguments, FalloutPlayerStatistics? owner)
    {
        if (operation != "modpcmiscstat") return false;
        if (arguments.Count != 2) throw new InvalidDataException("ModPCMiscStat requires its original enum and signed delta.");
        var current = Require(owner);
        if (!double.TryParse(arguments[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
            throw new InvalidDataException("Statistic delta has no resolved source number.");
        var delta = Integer(raw);
        var token = arguments[0];
        var index = delta == 0 ? (ushort)0 : double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric)
            ? Index(current, (FalloutScriptValue)numeric) : current.Source.Index(token.Trim('"'));
        current.Mod(index, delta, "script-ModPCMiscStat"); return true;
    }
    internal static ushort Index(FalloutPlayerStatistics owner, FalloutScriptValue argument)
    {
        if (argument.Kind == FalloutScriptValueKind.String) return owner.Source.Index(argument.Text);
        if (argument.Kind != FalloutScriptValueKind.Number || !double.IsFinite(argument.Number) ||
            argument.Number != Math.Truncate(argument.Number) || argument.Number < 0 || argument.Number > ushort.MaxValue)
            throw new InvalidDataException("Statistic argument is not an original UInt16 enum or original catalogue name.");
        var index = checked((ushort)argument.Number);
        if (index >= owner.Source.Rows.Count) throw new NotSupportedException("Statistic enum is outside the selected original catalogue.");
        return index;
    }
    private static FalloutPlayerStatistics Require(FalloutPlayerStatistics? owner) => owner ??
        throw new NotSupportedException("Statistic command has no actual current campaign owner.");
    private static int Integer(double value)
    {
        var integer = Math.Truncate(value);
        if (!double.IsFinite(value) || integer < int.MinValue || integer > int.MaxValue)
            throw new InvalidDataException("Statistic delta exceeds its original signed integer argument.");
        return (int)integer;
    }
}
