namespace OpenNV.Runtime.Content;

internal static class FalloutInputControlCommands
{
    internal static bool IsQuery(string operation) => operation is "getcontrol" or "getaltcontrol";
    internal static bool IsCommand(string operation) => operation is "setcontrol" or "setaltcontrol";
    internal static FalloutScriptFunction Query(string operation, FalloutInputControls controls) => operation == "getaltcontrol"
        ? new([FalloutScriptArgumentKind.Number], arguments =>
        {
            var code = controls.Get(FalloutInputControls.Index(arguments[0].Number), 1);
            return code == -1 ? -1 : code - 256;
        })
        : new([FalloutScriptArgumentKind.Number, FalloutScriptArgumentKind.OptionalNumber], arguments =>
            controls.Get(FalloutInputControls.Index(arguments[0].Number), arguments.Count == 2 ? FalloutInputControls.Index(arguments[1].Number) : 0));

    internal static void Execute(string operation, FalloutInputControls controls, IReadOnlyList<double> arguments)
    {
        if (arguments.Count is < 2 or > 3 || operation == "setaltcontrol" && arguments.Count != 2)
            throw new InvalidDataException("Control command has an invalid argument count.");
        controls.Set(FalloutInputControls.Index(arguments[0]), FalloutInputControls.Index(arguments[1]),
            operation == "setaltcontrol" ? 1 : arguments.Count == 3 ? FalloutInputControls.Index(arguments[2]) : 0);
    }
}
