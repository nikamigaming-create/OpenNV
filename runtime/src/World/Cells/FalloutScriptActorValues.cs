using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal static class FalloutScriptActorValues
{
    internal static FalloutScriptFunction? Function(string operation, Func<FalloutFormKey> subject,
        FalloutFormKey player, Func<FalloutReferenceWorld> world,
        Func<FalloutFormKey, string, FalloutActorValueRead, double> read,
        Action<FalloutFormKey, string, string, double>? referenceWrite = null,
        Action<string, string, double>? playerWrite = null)
    {
        if (FalloutActorValue.Query(operation) is { } kind && kind != FalloutActorValueRead.Current)
            return new([FalloutScriptArgumentKind.Identifier], arguments =>
                read(subject(), arguments[0].Identifier!, kind))
            { ReadOnly = true };
        if (FalloutActorValueCommands.Operation(operation) is not null)
            return new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number], arguments =>
            {
                var actor = subject();
                var name = arguments[0].Identifier!;
                var amount = FalloutActorValueCommands.Argument(operation, arguments[1].Number);
                if (actor == player)
                {
                    if (playerWrite is not null) playerWrite(name, operation, amount);
                    else if (referenceWrite is not null) referenceWrite(actor, name, operation, amount);
                    else throw new NotSupportedException("Player actor-value command has no state owner.");
                }
                else if (FalloutActorValueCommands.Operation(operation) == FalloutActorValueOperation.Restore)
                    world().RestoreActorValue(actor, name, (float)amount);
                else if (referenceWrite is not null) referenceWrite(actor, name, operation, amount);
                else world().ChangeActorValue(actor, name, operation, amount);
                return 0;
            });
        return null;
    }
}
