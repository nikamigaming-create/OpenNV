using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    private bool MatchesCompiledActorCallback(FalloutCompiledScriptProgram program, FalloutCompiledEvent block,
        FalloutReferenceScriptEvent delivered, FalloutFormKey reference)
    {
        var name = FalloutCompiledScriptEvents.Name(block.Event);
        if (!name.Equals(FalloutReferencePackageEvents.CanonicalName(delivered.Name), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Compiled event differs from its actual admitted reference callback.");
        var filter = FalloutCompiledScriptEvents.ActorCallbackFilter(program, block);
        if (block.Event is 10 or 12) FalloutReferencePackageEvents.RequireActor(records, reference);
        if (block.Event != 7) return true;
        var topics = delivered.Topics ?? (delivered.Topic is { } single ? new HashSet<FalloutFormKey> { single } : null);
        if (topics is not { Count: > 0 } || topics.Any(topic => records.GetEffective(topic).Signature != "DIAL"))
            throw new InvalidDataException("Compiled speech completion has no actual winning topic identity.");
        if (filter is null) return true;
        var selected = filter.Form!.Value;
        if (records.GetEffective(selected).Signature != "DIAL")
            throw new InvalidDataException("Compiled speech-completion SCRO filter is not an original topic.");
        return topics.Contains(selected);
    }
}
