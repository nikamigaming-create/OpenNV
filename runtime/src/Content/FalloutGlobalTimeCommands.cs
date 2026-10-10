using OpenNV.Runtime.Gameplay.State;
using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.Content;

internal static class FalloutGlobalTimeCommands
{
    private static readonly ConditionalWeakTable<FalloutGlobalTimeState, FalloutScriptFunction> Functions = new();
    internal static FalloutScriptFunction? Function(string operation, FalloutGlobalTimeState? owner)
    {
        if (owner is null || !(operation.Equals("setglobaltimemultiplier", StringComparison.OrdinalIgnoreCase) ||
            operation.Equals("sgtm", StringComparison.OrdinalIgnoreCase))) return null;
        return Functions.GetValue(owner, Create);
    }

    private static FalloutScriptFunction Create(FalloutGlobalTimeState owner) =>
        new(owner.Source.HasSoundOverride ?
            [FalloutScriptArgumentKind.Number, FalloutScriptArgumentKind.OptionalNumber] :
            [FalloutScriptArgumentKind.Number], arguments =>
        {
            if (arguments.Count < 1 || arguments.Count > (owner.Source.HasSoundOverride ? 2 : 1))
                throw new InvalidDataException("SetGlobalTimeMultiplier arguments differ from its selected original command declaration.");
            owner.Set(arguments[0].Number, arguments.Count == 2 ? arguments[1].Number : null);
            return 0;
        });
}
