using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal static class FalloutSleepWaitCommands
{
    internal static FalloutScriptFunction? Function(string operation, FalloutSleepWait? owner,
        Action<FalloutRestRequest>? openMenu) => operation switch
    {
        "getpcsleephours" => new([], _ => Require(owner).ReadPlayerHours()) { ReadOnly = true },
        "setpcsleephours" => new([FalloutScriptArgumentKind.Number], values =>
        { Require(owner).SetScriptHours(Integer(values[0].Number)); return 0; }),
        "showsleepwaitmenu" => new([FalloutScriptArgumentKind.Number, FalloutScriptArgumentKind.OptionalNumber], values =>
        {
            var kind = Integer(values[0].Number) == 1 ? FalloutRestKind.Sleep : FalloutRestKind.Wait;
            var test = values.Count > 1 && Integer(values[1].Number) == 1;
            var request = test ? new FalloutRestRequest(FalloutRestKind.Wait, FalloutRestOrigin.PlayerControl) :
                new FalloutRestRequest(kind, FalloutRestOrigin.SourceCommand);
            (openMenu ?? throw new NotSupportedException("ShowSleepWaitMenu has no actual native source factory."))(request);
            return 0;
        }),
        _ => null,
    };
    private static FalloutSleepWait Require(FalloutSleepWait? owner) => owner ??
        throw new NotSupportedException("Sleep-hour command has no authoritative current player rest owner.");
    private static int Integer(double value)
    {
        var integer = Math.Truncate(value);
        if (!double.IsFinite(value) || integer < int.MinValue || integer > int.MaxValue)
            throw new InvalidDataException("Sleep/wait command exceeds its source signed integer argument.");
        return (int)integer;
    }
}
