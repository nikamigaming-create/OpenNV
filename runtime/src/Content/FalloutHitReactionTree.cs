using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.Content;

/// <summary>Hit-context IDLE selection from winning records, independent of actor identity.</summary>
internal sealed class FalloutHitReactionTree
{
    private readonly FalloutIdleBranch[] _roots;
    private readonly Dictionary<FalloutFormKey, Lazy<IReadOnlyList<FalloutIdleBranch>>> _children;
    private sealed record Index(FalloutIdleBranch[] Roots,
        Dictionary<FalloutFormKey, Lazy<IReadOnlyList<FalloutIdleBranch>>> Children);
    private static readonly ConditionalWeakTable<FalloutPluginStack, ConcurrentDictionary<(string, ushort), Lazy<Index>>> Indexes = new();
    internal IReadOnlyList<FalloutFormKey> Roots => _roots.Select(value => value.Record.FormKey).ToArray();
    internal IReadOnlyList<FalloutFormKey> LastVisited { get; private set; } = [];

    internal FalloutHitReactionTree(FalloutPluginStack records, string skeletonPath, ushort contextFunction = 391)
    {
        var directory = skeletonPath[..skeletonPath.LastIndexOf('/')];
        var index = Indexes.GetOrCreateValue(records).GetOrAdd((directory.ToLowerInvariant(), contextFunction),
            _ => new(() => BuildIndex(records, directory, contextFunction), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        _roots = index.Roots; _children = index.Children;
    }

    private static Index BuildIndex(FalloutPluginStack records, string directory, ushort contextFunction)
    {
        var pluginOrder = records.Plugins.ToDictionary(value => value.Plugin.Name, value => value.LoadOrderIndex, StringComparer.OrdinalIgnoreCase);
        var branches = records.EffectiveRecords("IDLE")
            .Where(record => record.ReadSubrecords().Any(field => field.Signature == "MODL" &&
                ("meshes/" + FalloutDialogueTopic.Text(field.Data.Span).Replace('\\', '/')).StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase)))
            .Select(FalloutFurnitureIdleTree.Read).ToDictionary(branch => branch.Record.FormKey);
        var roots = new HashSet<FalloutFormKey>();
        foreach (var branch in branches.Values.Where(branch => branch.Conditions.Any(condition => condition.Function == contextFunction)))
        {
            var current = branch;
            var seen = new HashSet<FalloutFormKey>();
            while (current.Parent is { } parent)
            {
                if (!seen.Add(current.Record.FormKey)) throw new InvalidDataException("Hit-reaction IDLE parent cycle.");
                current = branches.GetValueOrDefault(parent) ?? throw new NotSupportedException("Hit-reaction IDLE parent has a different model owner.");
            }
            roots.Add(current.Record.FormKey);
        }
        var children = branches.Values.Where(branch => branch.Parent is not null).GroupBy(branch => branch.Parent!.Value)
            .ToDictionary(group => group.Key, group => new Lazy<IReadOnlyList<FalloutIdleBranch>>(
                () => FalloutFurnitureIdleTree.Order(group.OrderBy(child => pluginOrder[child.Record.FormKey.OwnerPlugin]).ToArray()),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return new(roots.Select(key => branches[key]).ToArray(), children);
    }

    internal IEnumerable<string> AnimationPaths()
    {
        var pending = new Stack<FalloutIdleBranch>(_roots);
        var seen = new HashSet<FalloutFormKey>();
        while (pending.TryPop(out var branch))
        {
            if (!seen.Add(branch.Record.FormKey)) throw new InvalidDataException("Hit-reaction IDLE cycle.");
            if (branch.Model.EndsWith(".kf", StringComparison.OrdinalIgnoreCase)) yield return branch.Model;
            else foreach (var child in _children.GetValueOrDefault(branch.Record.FormKey)?.Value ?? []) pending.Push(child);
        }
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
            var children = _children.GetValueOrDefault(branch.Record.FormKey)?.Value ?? [];
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
