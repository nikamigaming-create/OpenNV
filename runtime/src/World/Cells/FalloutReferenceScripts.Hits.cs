using System.Buffers.Binary;
using System.Collections.Frozen;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    private static bool IsHitEvent(string name) => name.Equals("OnHit", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("OnHitWith", StringComparison.OrdinalIgnoreCase);

    private FalloutReferenceScriptEvent AdmitHitEvent(FalloutFormKey reference, FalloutReferenceScriptEvent item)
    {
        FalloutReferenceHitEvents.RequireReference(records, reference);
        if (item.ActionReference is not null || item.TriggerReferences is not null || item.Topic is not null ||
            item.Topics is not null || item.Package is not null || item.Packages is not null)
            throw new InvalidDataException("Hit events have typed contact identities and no action, topic or package reference.");
        if (item.Name.Equals("OnHitWith", StringComparison.OrdinalIgnoreCase))
        {
            if (item.HitAttackers is not null || item.HitWeapons is not { Count: > 0 } weapons)
                throw new InvalidDataException("OnHitWith has absent or conflicting source weapon identities.");
            foreach (var weapon in weapons)
                if (records.GetEffective(weapon).Signature != "WEAP")
                    throw new InvalidDataException("OnHitWith requires typed source WEAP forms.");
            return item with { HitWeapons = weapons.ToFrozenSet() };
        }
        if (item.HitWeapons is not null || item.HitAttackers is not { Count: > 0 } attackers)
            throw new InvalidDataException("OnHit has absent or conflicting attacker identities.");
        foreach (var attacker in attackers) FalloutReferenceHitEvents.RequireActor(records, attacker);
        return item with { HitAttackers = attackers.ToFrozenSet() };
    }

    private bool MatchesHitBlock(FalloutFormKey reference, FalloutScriptBindings bindings,
        FalloutScriptEventProgram block, FalloutReferenceScriptEvent item)
    {
        if (block.Filter is null) return true;
        if (block.Event.Equals("OnHit", StringComparison.OrdinalIgnoreCase))
        {
            var actor = bindings.Reference(block.Filter);
            FalloutReferenceHitEvents.RequireActor(records, actor);
            // The native block's actor filter disables it on non-actors.
            return FalloutReferenceHitEvents.IsActor(records, reference) && item.HitAttackers!.Contains(actor);
        }
        var filter = bindings.Form(block.Filter);
        if (filter.Signature == "WEAP") return item.HitWeapons!.Contains(filter.FormKey);
        if (filter.Signature != "FLST") throw new InvalidDataException("OnHitWith filter requires a source WEAP or FLST form.");
        var matches = false;
        foreach (var field in filter.ReadSubrecords().Where(field => field.Signature == "LNAM"))
        {
            if (field.Data.Length != 4) throw new InvalidDataException("Hit weapon list has an invalid FormID extent.");
            var form = filter.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
            if (records.GetEffective(form).Signature == "FLST")
                throw new NotSupportedException("Nested object-script hit weapon lists require their source membership contract.");
            matches |= item.HitWeapons!.Contains(form);
        }
        return matches;
    }
}
