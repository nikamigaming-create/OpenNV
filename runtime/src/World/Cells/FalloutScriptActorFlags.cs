using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal static class FalloutScriptActorFlags
{
    internal static bool Supports(string name, bool scoped) => name == "setessential" && !scoped ||
        name is "setactorrefessential" or "isactorrefessential";

    internal static FalloutScriptFunction? Function(string name, bool scoped, FalloutPluginStack records,
        Func<FalloutReferenceWorld> world, Func<FalloutFormKey> subject)
    {
        if (name == "setessential" && !scoped)
            return FalloutScriptFunction.Typed([FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.Number], arguments =>
            {
                world().ActorBaseFlags.SetEssential(arguments[0].Value.FormKey(records),
                    FalloutPlayerActorValues.SignedInteger(arguments[1].Number) > 0);
                return 0;
            });
        if (name == "setactorrefessential")
            return new([FalloutScriptArgumentKind.Number], arguments =>
            {
                world().SetActorRefEssential(subject(), FalloutPlayerActorValues.SignedInteger(arguments[0].Number) > 0);
                return 0;
            });
        if (name == "isactorrefessential")
            return new([], _ => world().IsActorRefEssential(subject()) ? 1 : 0) { ReadOnly = true };
        return null;
    }
}
