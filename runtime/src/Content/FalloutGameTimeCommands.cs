using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal static class FalloutGameTimeCommands
{
    internal static FalloutScriptFunction? Function(string operation, FalloutGameTime? clock,
        Func<bool>? isHardcore = null) => operation switch
        {
            "getgamedayspassed" => new([FalloutScriptArgumentKind.OptionalNumber, FalloutScriptArgumentKind.OptionalNumber,
            FalloutScriptArgumentKind.OptionalNumber], arguments => Require(clock).GetDaysPassed(
                arguments.Count > 0 ? DateArgument(arguments[0].Number) : 2281,
                arguments.Count > 1 ? DateArgument(arguments[1].Number) : 10,
                arguments.Count > 2 ? DateArgument(arguments[2].Number) : 13))
            { ReadOnly = true },
            "setgamehour" => new([FalloutScriptArgumentKind.Number], arguments =>
            {
                var hour = FalloutGameTime.SourceHour(arguments[0].Number);
                var owner = Require(clock);
                if (isHardcore is null)
                    throw new NotSupportedException("SetGameHour has no authoritative player Hardcore query.");
                if (isHardcore())
                    throw new NotSupportedException("SetGameHour requires the original forced Hardcore-needs update owner.");
                owner.SetHour(hour);
                return 0;
            }),
            _ => null,
        };

    private static FalloutGameTime Require(FalloutGameTime? clock) => clock ??
        throw new NotSupportedException("Game-time commands have no shared source-calendar clock owner.");

    private static int DateArgument(double value)
    {
        var integer = Math.Truncate(value);
        if (!double.IsFinite(value) || integer < int.MinValue || integer > int.MaxValue)
            throw new InvalidDataException("GetGameDaysPassed requires source signed 32-bit date arguments.");
        return (int)integer;
    }
}
