namespace OpenNV.Runtime.Content;

// A real inventory item's event-list identity and raw source local cells.
// It is independent of a placed reference and never supplies a synthetic REFR.
internal sealed record FalloutInventoryScriptOrderEntry(FalloutFormKey Item, IReadOnlyList<Guid> Instances, int? BaseContribution = null, bool? LeveledItem = null);
internal sealed record FalloutInventoryScriptInitialization(Guid Identity, bool GameMode);

internal sealed record FalloutInventoryScriptEvent(FalloutFormKey Actor, uint Mask);

internal sealed record FalloutInventoryScriptState(Guid Identity, FalloutFormKey Script, string Sha256,
    IReadOnlyList<FalloutScriptEffectLocalCell> Locals, IReadOnlyList<FalloutInventoryScriptEvent>? Events = null,
    string? Error = null, bool? LeveledItem = null)
{
    internal void ValidateShape()
    {
        if (Identity == Guid.Empty || Script.ObjectId == 0 || string.IsNullOrWhiteSpace(Script.OwnerPlugin) ||
            Sha256 is not { Length: 64 } || Sha256.Any(value => !char.IsAsciiHexDigit(value)) ||
            Locals is null || Locals.Any(value => value is null) ||
            Events is { } events && (events.Any(value => value is null || value.Actor.ObjectId == 0 ||
                string.IsNullOrWhiteSpace(value.Actor.OwnerPlugin) || value.Mask == 0 || (value.Mask & ~395u) != 0) ||
                events.Select(value => value.Actor).Distinct().Count() != events.Count) ||
            Error is not null && string.IsNullOrWhiteSpace(Error))
            throw new InvalidDataException("Inventory script event-list identity or retained cells are invalid.");
    }

    internal FalloutInventoryScriptState Admit(FalloutFormKey actor, uint mask)
    {
        if (mask is not (1u or 2u or 8u or 128u or 256u)) throw new ArgumentOutOfRangeException(nameof(mask));
        var pending = (Events ?? []).ToList();
        var index = pending.FindIndex(value => value.Actor == actor);
        if (index < 0) pending.Add(new(actor, mask));
        else pending[index] = pending[index] with { Mask = pending[index].Mask | mask };
        return this with { Events = pending.ToArray() };
    }

    internal FalloutScriptEffectLocals Resolve(FalloutPluginRecord script)
    {
        ValidateShape();
        if (script.IsDeleted || Script != script.FormKey || Sha256 != Hash(script))
            throw new InvalidDataException("Inventory event-list state differs from its winning attached script.");
        return new(script, Locals);
    }

    internal static FalloutInventoryScriptState? Prepare(FalloutPluginStack records, FalloutPluginRecord item,
        FalloutItemVariant variant)
    {
        var script = FalloutScriptLocals.AttachedScript(records, item);
        if (script is null)
        {
            if (variant.ScriptState is not null) throw new InvalidDataException("Inventory event-list has no attached script.");
            return null;
        }
        var fields = script.ReadSubrecords().ToArray();
        var source = fields.Where(field => field.Signature == "SCTX").ToArray();
        var header = fields.Where(field => field.Signature == "SCHR").ToArray();
        var compiled = fields.Where(field => field.Signature == "SCDA").ToArray();
        if (source.Length != 1 || header.Length != 1 || compiled.Length != 1 ||
            FalloutScriptHeader.Read(header[0].Data.Span).CompiledBytes != compiled[0].Data.Length)
            throw new NotSupportedException("Inventory script requires its complete compiled identity and source.");
        if (variant.Count != 1)
            throw new NotSupportedException("Stacked inventory script event-list allocation/splitting is not qualified.");
        if (variant.ScriptState is { } retained) { _ = retained.Resolve(script); return retained; }
        return new(Guid.NewGuid(), script.FormKey, Hash(script), new FalloutScriptEffectLocals(script).Capture());
    }

    internal static string Hash(FalloutPluginRecord script) =>
        script.ReadDataHash().ToLowerInvariant();

    internal static void ValidateInventory(FalloutPluginStack records, IEnumerable<FalloutCampaignItem> items)
    {
        var identities = new HashSet<Guid>();
        foreach (var item in items)
            foreach (var variant in item.Variants ?? [])
            {
                if (variant.ScriptState is not { } state) continue;
                if (variant.Count != 1 || !identities.Add(state.Identity))
                    throw new InvalidDataException("Saved inventory script event-list identity is repeated or stacked.");
                var script = FalloutScriptLocals.AttachedScript(records, records.GetEffective(item.FormKey)) ??
                    throw new InvalidDataException("Saved inventory script event-list has no attached source.");
                _ = state.Resolve(script);
            }
    }
}
