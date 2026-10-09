using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

// SCTX and compiled SCDA enter these same state consumers. The selected image
// still owns each command's receiver and argument declaration.
internal static class FalloutActorAiCommands
{
    internal static bool Apply(FalloutReferenceWorld world, FalloutFormKey callingOwner,
        FalloutFormKey target, string operation, IReadOnlyList<double> arguments)
    {
        if (operation.Equals("SetActorsAI", StringComparison.OrdinalIgnoreCase))
        {
            if (arguments.Count != 1) throw new InvalidDataException("SetActorsAI requires one signed integer.");
            world.SetActorsAi(target, arguments[0], callingOwner); return true;
        }
        if (operation.Equals("ToggleActorsAI", StringComparison.OrdinalIgnoreCase))
        {
            if (arguments.Count != 0) throw new InvalidDataException("ToggleActorsAI takes no arguments.");
            world.ToggleActorsAi(target, callingOwner); return true;
        }
        return false;
    }
    internal static int Query(FalloutReferenceWorld world, FalloutFormKey actor) => world.IsActorsAiOff(actor);
}
