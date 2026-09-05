using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutIdleBranch(FalloutPluginRecord Record, string Model,
    FalloutFormKey? Parent, FalloutFormKey? Previous, byte Group,
    IReadOnlyList<FalloutCondition> Conditions);

/// <summary>Source-ordered furniture procedure branches, shared by every actor and marker.</summary>
internal sealed class FalloutFurnitureIdleTree
{
    private readonly IReadOnlyList<FalloutIdleBranch> _branches;
    private readonly IReadOnlyDictionary<string, int> _pluginOrder;

    internal FalloutFurnitureIdleTree(FalloutPluginStack stack, string skeletonPath)
    {
        _pluginOrder = stack.Plugins.ToDictionary(value => value.Plugin.Name, value => value.LoadOrderIndex, StringComparer.OrdinalIgnoreCase);
        var directory = skeletonPath[..skeletonPath.LastIndexOf('/')];
        _branches = stack.EffectiveRecords("IDLE").Select(record => (record, fields: record.ReadSubrecords().ToArray()))
            .Where(value => value.fields.Any(field => field.Signature == "MODL" &&
                ("meshes/" + FalloutDialogueTopic.Text(field.Data.Span).Replace('\\', '/')).StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase)))
            .Select(value => Read(value.record)).ToArray();
    }

    internal static FalloutIdleBranch Read(FalloutPluginRecord record)
    {
        var fields = record.ReadSubrecords().ToArray();
        var related = fields.Single(field => field.Signature == "ANAM").Data;
        var data = fields.Single(field => field.Signature == "DATA").Data;
        if (related.Length != 8 || data.Length is not (6 or 8)) throw new InvalidDataException("IDLE tree has an invalid field extent.");
        return new(record, "meshes/" + FalloutDialogueTopic.Text(fields.Single(field => field.Signature == "MODL").Data.Span).Replace('\\', '/'),
            record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(related.Span)),
            record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(related.Span[4..])),
            data.Span[0], FalloutCondition.Read(record));
    }

    internal FalloutPluginRecord Select(Func<FalloutCondition, float> evaluate)
    {
        // The furniture procedure requests the source branch whose predicates
        // inspect sitting/sleeping state. No EDID, actor or animation filename
        // selects a pose. Other AI procedures retain their own selection owner.
        var roots = _branches.Where(branch => branch.Parent is null &&
            !branch.Model.EndsWith(".kf", StringComparison.OrdinalIgnoreCase) &&
            branch.Conditions.Any(condition => condition.Function is 159 or 49) &&
            FalloutCondition.AllPass(branch.Conditions, evaluate)).ToArray();
        if (roots.Length != 1) throw new NotSupportedException($"Furniture procedure has {roots.Length} eligible source roots: {string.Join(',', roots.Select(value => value.Record.FormKey))}.");
        return Visit(roots[0], new HashSet<FalloutFormKey>()) ??
            throw new NotSupportedException("Furniture procedure has no eligible source KF leaf.");

        FalloutPluginRecord? Visit(FalloutIdleBranch branch, HashSet<FalloutFormKey> ancestors)
        {
            if (!ancestors.Add(branch.Record.FormKey)) throw new InvalidDataException("IDLE parent cycle.");
            if (branch.Model.EndsWith(".kf", StringComparison.OrdinalIgnoreCase)) return branch.Record;
            foreach (var child in Order(_branches.Where(value => value.Parent == branch.Record.FormKey)
                .OrderBy(value => _pluginOrder[value.Record.Plugin.Name]).ToArray()))
            {
                if (!FalloutCondition.AllPass(child.Conditions, evaluate)) continue;
                var result = Visit(child, new(ancestors));
                if (result is not null) return result;
                if ((child.Group & 0x80) != 0) return null;
            }
            return null;
        }
    }

    internal static IReadOnlyList<FalloutIdleBranch> Order(IReadOnlyList<FalloutIdleBranch> branches)
    {
        var result = new List<FalloutIdleBranch>();
        foreach (var plugin in branches.GroupBy(value => value.Record.Plugin.Name, StringComparer.OrdinalIgnoreCase))
        {
            var remaining = plugin.ToDictionary(value => value.Record.FormKey);
            if (remaining.Values.GroupBy(value => value.Previous).Any(group => group.Count() != 1))
                throw new NotSupportedException("One plugin declares ambiguous IDLE sibling insertion points.");
            while (remaining.Count != 0)
            {
                var next = remaining.Values.Where(value => value.Previous is null ||
                    result.Any(prior => prior.Record.FormKey == value.Previous)).ToArray();
                if (next.Length == 0) throw new NotSupportedException("IDLE sibling chain is missing or cyclic.");
                foreach (var item in next)
                {
                    var at = item.Previous is null ? 0 : result.FindIndex(value => value.Record.FormKey == item.Previous) + 1;
                    result.Insert(at, item);
                    remaining.Remove(item.Record.FormKey);
                }
            }
        }
        return result;
    }
}
