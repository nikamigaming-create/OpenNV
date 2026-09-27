namespace OpenNV.Runtime.Content;

/// <summary>Hit-context IDLE selection from winning records, independent of actor identity.</summary>
internal sealed class FalloutHitReactionTree
{
    private readonly Dictionary<FalloutFormKey, FalloutIdleBranch> _branches;
    private readonly FalloutIdleBranch[] _roots;
    private readonly Dictionary<string, int> _pluginOrder;
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutIdleBranch>> _children = [];
    internal IReadOnlyList<FalloutFormKey> Roots => _roots.Select(value => value.Record.FormKey).ToArray();
    internal IReadOnlyList<FalloutFormKey> LastVisited { get; private set; } = [];

    internal FalloutHitReactionTree(FalloutPluginStack records, string skeletonPath)
    {
        var directory = skeletonPath[..skeletonPath.LastIndexOf('/')];
        _pluginOrder = records.Plugins.ToDictionary(value => value.Plugin.Name, value => value.LoadOrderIndex, StringComparer.OrdinalIgnoreCase);
        _branches = records.EffectiveRecords("IDLE")
            .Where(record => record.ReadSubrecords().Any(field => field.Signature == "MODL" &&
                ("meshes/" + FalloutDialogueTopic.Text(field.Data.Span).Replace('\\', '/')).StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase)))
            .Select(FalloutFurnitureIdleTree.Read).ToDictionary(branch => branch.Record.FormKey);
        var roots = new HashSet<FalloutFormKey>();
        foreach (var branch in _branches.Values.Where(branch => branch.Conditions.Any(condition => condition.Function == 391)))
        {
            var current = branch;
            var seen = new HashSet<FalloutFormKey>();
            while (current.Parent is { } parent)
            {
                if (!seen.Add(current.Record.FormKey)) throw new InvalidDataException("Hit-reaction IDLE parent cycle.");
                current = _branches.GetValueOrDefault(parent) ?? throw new NotSupportedException("Hit-reaction IDLE parent has a different model owner.");
            }
            roots.Add(current.Record.FormKey);
        }
        _roots = roots.Select(key => _branches[key]).ToArray();
    }

    internal FalloutPluginRecord? Select(Func<FalloutCondition, float> evaluate)
    {
        var visited = new List<FalloutFormKey>();
        LastVisited = visited;
        bool Pass(FalloutIdleBranch branch)
        {
            visited.Add(branch.Record.FormKey);
            return FalloutCondition.AllPass(branch.Conditions, evaluate);
        }
        // Hit-context roots are distinguished by their predicates (including
        // GetHitLocation), never by EDID, an actor whitelist or KF spelling.
        var eligible = _roots.Where(Pass).ToArray();
        if (eligible.Length > 1) throw new NotSupportedException("Multiple eligible hit-reaction roots need source root-priority ownership.");
        return eligible.Length == 0 ? null : Visit(eligible[0], []);

        FalloutPluginRecord? Visit(FalloutIdleBranch branch, HashSet<FalloutFormKey> ancestors)
        {
            if (!ancestors.Add(branch.Record.FormKey)) throw new InvalidDataException("Hit-reaction IDLE cycle.");
            if (branch.Model.EndsWith(".kf", StringComparison.OrdinalIgnoreCase)) return branch.Record;
            if (!_children.TryGetValue(branch.Record.FormKey, out var children))
                _children.Add(branch.Record.FormKey, children = FalloutFurnitureIdleTree.Order(_branches.Values
                    .Where(child => child.Parent == branch.Record.FormKey).OrderBy(child => _pluginOrder[child.Record.FormKey.OwnerPlugin]).ToArray()));
            foreach (var child in children)
            {
                if (!Pass(child)) continue;
                var selected = Visit(child, new(ancestors));
                if (selected is not null) return selected;
                if ((child.Group & 0x80) != 0) return null;
            }
            return null;
        }
    }
}
