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
        => SelectRoots(branch => branch.Conditions.Any(condition => condition.Function is 159 or 49), evaluate, "Furniture");

    internal FalloutPluginRecord? SelectProcedure(int procedure, Func<FalloutCondition, float> evaluate)
    {
        var byForm = _branches.ToDictionary(branch => branch.Record.FormKey);
        var requested = _branches.Where(branch => branch.Conditions.Any(condition => condition.Function == 143 &&
            condition.RunOn == 0 && condition.Argument1 == 0 && condition.Argument2 == 0 &&
            (condition.Flags & 0xe4) == 0 && condition.Comparison == procedure)).Select(branch => branch.Record.FormKey).ToHashSet();
        var roots = new HashSet<FalloutFormKey>();
        foreach (var key in requested)
        {
            var branch = byForm[key]; var seen = new HashSet<FalloutFormKey>();
            while (branch.Parent is { } parent)
            {
                if (!seen.Add(branch.Record.FormKey)) throw new InvalidDataException("Procedure IDLE ancestry is cyclic.");
                if (!byForm.TryGetValue(parent, out branch!)) throw new NotSupportedException("Procedure IDLE has an unavailable model ancestor.");
            }
            roots.Add(branch.Record.FormKey);
        }
        var eligible = roots.Select(root => byForm[root]).Where(branch => FalloutCondition.AllPass(branch.Conditions, evaluate)).ToArray();
        var matches = eligible.Select(branch => Visit(branch, false, [])).Where(record => record is not null).DistinctBy(record => record!.FormKey).ToArray();
        if (matches.Length > 1) throw new NotSupportedException($"Actor procedure {procedure} has {matches.Length} eligible source KF leaves.");
        if (matches.Length == 0 && eligible.Any(branch => (branch.Group & 0x80) == 0))
            throw new NotSupportedException($"Actor procedure {procedure} requires a source file but no eligible leaf was returned.");
        return matches.SingleOrDefault();

        FalloutPluginRecord? Visit(FalloutIdleBranch branch, bool ownsProcedure, HashSet<FalloutFormKey> ancestors)
        {
            if (!ancestors.Add(branch.Record.FormKey)) throw new InvalidDataException("Procedure IDLE parent cycle.");
            ownsProcedure |= requested.Contains(branch.Record.FormKey);
            if (branch.Model.EndsWith(".kf", StringComparison.OrdinalIgnoreCase)) return ownsProcedure ? branch.Record : null;
            foreach (var child in Order(_branches.Where(value => value.Parent == branch.Record.FormKey)
                .OrderBy(value => _pluginOrder[value.Record.FormKey.OwnerPlugin]).ToArray()))
            {
                if (!FalloutCondition.AllPass(child.Conditions, evaluate)) continue;
                var selected = Visit(child, ownsProcedure, new(ancestors));
                if (selected is not null) return selected;
                if ((child.Group & 0x80) != 0) return null;
            }
            return null;
        }
    }

    private FalloutPluginRecord SelectRoots(Func<FalloutIdleBranch, bool> requested,
        Func<FalloutCondition, float> evaluate, string owner)
    {
        // The furniture procedure requests the source branch whose predicates
        // inspect sitting/sleeping state. No EDID, actor or animation filename
        // selects a pose. Other AI procedures retain their own selection owner.
        var roots = _branches.Where(branch => branch.Parent is null &&
            !branch.Model.EndsWith(".kf", StringComparison.OrdinalIgnoreCase) &&
            requested(branch) &&
            FalloutCondition.AllPass(branch.Conditions, evaluate)).ToArray();
        if (roots.Length != 1) throw new NotSupportedException($"{owner} has {roots.Length} eligible source roots: {string.Join(',', roots.Select(value => value.Record.FormKey))}.");
        return Visit(roots[0], new HashSet<FalloutFormKey>()) ??
            throw new NotSupportedException($"{owner} has no eligible source KF leaf.");

        FalloutPluginRecord? Visit(FalloutIdleBranch branch, HashSet<FalloutFormKey> ancestors)
        {
            if (!ancestors.Add(branch.Record.FormKey)) throw new InvalidDataException("IDLE parent cycle.");
            if (branch.Model.EndsWith(".kf", StringComparison.OrdinalIgnoreCase)) return branch.Record;
            foreach (var child in Order(_branches.Where(value => value.Parent == branch.Record.FormKey)
                .OrderBy(value => _pluginOrder[value.Record.FormKey.OwnerPlugin]).ToArray()))
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
        // Winning overrides can insert a master node after a sibling declared
        // by a later plugin. Resolve that source dependency across the complete
        // sibling set, retaining the supplied plugin/registration order for
        // independent insertions. A missing anchor or cycle remains a fault.
        var remaining = branches.ToDictionary(value => value.Record.FormKey);
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
        return result;
    }
}
