using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutRegionImposters(FalloutFormKey Region, byte Flags, byte Priority,
    IReadOnlyList<FalloutFormKey> References, IReadOnlyList<FalloutImposterDroppedLink> DroppedLinks)
{
    internal static FalloutRegionImposters? Read(FalloutPluginStack records, FalloutFormKey region)
    {
        var source = records.GetEffective(region);
        if (source.IsDeleted || source.Signature != "REGN")
            throw new InvalidDataException("Imposter membership requires a winning live REGN.");
        uint? group = null;
        byte flags = 0, priority = 0;
        var declarations = 0;
        var references = new List<FalloutFormKey>();
        var dropped = new List<FalloutImposterDroppedLink>();
        foreach (var field in source.ReadSubrecords())
        {
            if (field.Signature == "RDAT")
            {
                if (field.Data.Length != 8) throw new InvalidDataException("REGN data header has invalid extent.");
                group = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
                if (group == 8)
                {
                    if (++declarations != 1) throw new InvalidDataException("REGN repeats imposter data.");
                    flags = field.Data.Span[4]; priority = field.Data.Span[5];
                }
            }
            else if (field.Signature == "RDID")
            {
                if (group != 8 || field.Data.Length % 4 != 0)
                    throw new InvalidDataException("RDID requires its type-eight header and complete reference array.");
                for (var offset = 0; offset < field.Data.Length; offset += 4)
                {
                    var raw = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span[offset..]);
                    var key = source.Plugin.AdjustOptionalFormId(raw);
                    if (key is null || !records.TryGetEffective(key.Value, out var target))
                    {
                        dropped.Add(new(key, raw, "null-or-unresolved-source-form"));
                        continue;
                    }
                    if (target.Signature is not ("REFR" or "ACHR" or "ACRE"))
                    {
                        dropped.Add(new(key, raw, "source-form-is-not-a-reference"));
                        continue;
                    }
                    if (target.IsDeleted) throw new NotSupportedException("Deleted RDID reference admission is unqualified.");
                    if (!references.Contains(key.Value)) references.Add(key.Value);
                }
            }
        }
        return declarations == 0 ? null : new(region, flags, priority, references.AsReadOnly(), dropped.AsReadOnly());
    }
}

internal sealed record FalloutImposterDroppedLink(FalloutFormKey? Source, uint RawFormId, string Reason);

// This owner consumes an accepted current-player region list. It has no clock,
// polling interval, gameplay Enable/Disable call or model-name classification.
// The caller owns source region geometry and actual reference 3D materialization.
internal sealed class FalloutRegionImposterVisibility(FalloutPluginStack records)
{
    private readonly Dictionary<FalloutFormKey, FalloutRegionImposters?> _data = [];
    private FalloutRegionImposters[] _published = [];
    private HashSet<FalloutFormKey> _selected = [];
    private readonly HashSet<FalloutFormKey> _adopted = [];
    internal string? Error { get; private set; }
    internal IReadOnlySet<FalloutFormKey> Selected => _selected;
    internal bool Adopted(FalloutFormKey reference) => _adopted.Contains(reference);

    private FalloutRegionImposters? Data(FalloutFormKey region)
    {
        if (!_data.TryGetValue(region, out var data))
            _data.Add(region, data = FalloutRegionImposters.Read(records, region));
        return data;
    }

    // Reconstruct a completed owner's bookkeeping without replaying attachment,
    // 3D creation, role mutation or show callbacks during cold/view recreation.
    internal void RestorePublished(IReadOnlyList<FalloutFormKey> regions)
    {
        if (_published.Length != 0 || _selected.Count != 0 || Error is not null)
            throw new InvalidOperationException("Region publication restore requires a fresh visibility owner.");
        var published = regions.Select(Data).OfType<FalloutRegionImposters>().ToArray();
        _published = published;
        _selected = published.SelectMany(group => group.References).ToHashSet();
        _adopted.UnionWith(_selected);
    }

    // Initial native model setup uses the player cache, with CELL-region
    // fallback only when that cache has no region entries. Weather priority
    // selection is not an imposter membership owner.
    internal bool AdmittedAtLoad(FalloutFormKey reference, IReadOnlyList<FalloutFormKey> playerRegions,
        IReadOnlyList<FalloutFormKey> cellRegions)
    {
        var regions = playerRegions.Count != 0 ? playerRegions : cellRegions;
        return regions.Any(region => Data(region)?.References.Contains(reference) == true);
    }

    internal bool RenderAdmitted(FalloutFormKey reference, bool sourceImposter, bool gameplayEnabled) =>
        gameplayEnabled && (!(sourceImposter || _adopted.Contains(reference)) || Error is null && _selected.Contains(reference));

    // Call only for an accepted region-list publication. Retire departed previous
    // groups in reverse cached-region order before loading/showing current groups. Shared references
    // remain in the new selection and are shown again in source list order.
    // A materializer must return only after the actual reference 3D exists.
    internal bool Publish(IReadOnlyList<FalloutFormKey> regions, Action<FalloutFormKey> ensureActual3D,
        Action<FalloutFormKey, bool> setRenderVisibility)
    {
        if (Error is not null) return false;
        try
        {
            // Validate all source declarations before touching presentation.
            var current = regions.Select(Data).OfType<FalloutRegionImposters>().ToArray();
            _selected = current.SelectMany(group => group.References).ToHashSet();
            var retainedRegions = current.Select(group => group.Region).ToHashSet();
            foreach (var previous in _published.Reverse().Where(group => !retainedRegions.Contains(group.Region)))
                foreach (var reference in previous.References) setRenderVisibility(reference, false);
            foreach (var group in current)
                foreach (var reference in group.References)
                {
                    ensureActual3D(reference);
                    // Native type-eight activation adopts the imposter role
                    // even if this reference did not declare XSRF bit two.
                    _adopted.Add(reference);
                    setRenderVisibility(reference, true);
                }
            _published = current;
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            InvalidOperationException or NotSupportedException or KeyNotFoundException)
        {
            // Presentation can be partially retired/materialized. Retain the
            // reached source fault once; never report successful publication.
            Error = error.Message;
            return false;
        }
    }
}
