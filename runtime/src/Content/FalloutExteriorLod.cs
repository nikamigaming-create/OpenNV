using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutLodBlock(int Level, int X, int Y, string Terrain, string? Objects)
{
    internal float Distance(float x, float y) => Math.Max(
        Math.Max(X * 4096f - x, x - (X + Level) * 4096f),
        Math.Max(Y * 4096f - y, y - (Y + Level) * 4096f));
    internal bool Contains(FalloutLodBlock other) => other.X >= X && other.Y >= Y &&
        other.X + other.Level <= X + Level && other.Y + other.Level <= Y + Level;
}

// The file graph defines the quadtree's origin and available coverage. In
// particular, child worlds need not start on the zero-aligned world grid.
internal sealed class FalloutExteriorLod
{
    private readonly FalloutLodBlock[] _roots;
    private readonly Dictionary<FalloutLodBlock, FalloutLodBlock[]> _children = [];
    internal IReadOnlyList<FalloutLodBlock> Blocks { get; }
    internal float LoadDistance { get; }
    internal float SplitMultiplier { get; }
    internal float MorphMultiplier { get; }

    internal FalloutExteriorLod(string worldName, IReadOnlyList<string> resources,
        float loadDistance, float splitMultiplier, float morphMultiplier)
    {
        if (!float.IsFinite(loadDistance) || loadDistance <= 0 || !float.IsFinite(splitMultiplier) || splitMultiplier <= 0 ||
            !float.IsFinite(morphMultiplier) || morphMultiplier <= 0 || morphMultiplier >= 1)
            throw new InvalidDataException("Source terrain distances are invalid.");
        LoadDistance = loadDistance; SplitMultiplier = splitMultiplier; MorphMultiplier = morphMultiplier;
        var pattern = new Regex("^" + Regex.Escape(worldName) + @"\.level(\d+)\.x(-?\d+)\.y(-?\d+)\.nif$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var terrain = new Dictionary<(int Level, int X, int Y), string>();
        var objects = new Dictionary<(int Level, int X, int Y), string>();
        foreach (var source in resources)
        {
            var path = source.Replace('/', '\\');
            var match = pattern.Match(path[(path.LastIndexOf('\\') + 1)..]);
            if (!match.Success) continue;
            var level = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var x = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var y = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            if (level < 1 || (level & (level - 1)) != 0) throw new InvalidDataException("LOD level must be a power-of-two cell span.");
            var target = path.Contains("\\blocks\\", StringComparison.OrdinalIgnoreCase) ? objects : terrain;
            if (!target.TryAdd((level, x, y), source)) throw new InvalidDataException("Duplicate winning LOD block.");
        }
        Blocks = terrain.Select(pair => new FalloutLodBlock(pair.Key.Level, pair.Key.X, pair.Key.Y, pair.Value,
            objects.GetValueOrDefault(pair.Key))).OrderByDescending(block => block.Level).ThenBy(block => block.X).ThenBy(block => block.Y).ToArray();
        if (Blocks.Count == 0) throw new NotSupportedException($"World {worldName} has no owned terrain LOD blocks.");
        _roots = Blocks.Where(block => !Blocks.Any(parent => parent.Level > block.Level && parent.Contains(block))).ToArray();
        foreach (var parent in Blocks)
        {
            var children = Blocks.Where(child => child.Level * 2 == parent.Level && parent.Contains(child)).ToArray();
            // Keep the coarse source block if the finer source set leaves a
            // quadrant uncovered. No empty substitute tile is manufactured.
            if (children.Length == 4 && children.Select(child => (child.X, child.Y)).Distinct().Count() == 4 &&
                children.All(child => (child.X == parent.X || child.X == parent.X + child.Level) &&
                    (child.Y == parent.Y || child.Y == parent.Y + child.Level))) _children.Add(parent, children);
        }
    }

    internal IReadOnlyList<FalloutLodBlock> Select(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        var selected = new List<FalloutLodBlock>();
        foreach (var root in _roots) Visit(root);
        return selected;
        void Visit(FalloutLodBlock block)
        {
            var distance = block.Distance(x, y);
            if (distance > LoadDistance) return;
            if (distance < block.Level * 4096f * SplitMultiplier && _children.TryGetValue(block, out var children))
                foreach (var child in children) Visit(child);
            else selected.Add(block);
        }
    }

    internal IReadOnlyList<FalloutLodBlock> ResidentCover(IReadOnlyList<FalloutLodBlock> selected, IReadOnlySet<FalloutLodBlock> available)
    {
        var cover = new HashSet<FalloutLodBlock>();
        foreach (var block in selected)
        {
            var finer = Finer(block);
            if (finer is not null) cover.UnionWith(finer);
            else if (Blocks.Where(parent => available.Contains(parent) && parent.Contains(block)).MinBy(parent => parent.Level) is { } parent)
                cover.Add(parent);
        }
        // A retained coarse parent covers the whole quadrant. Do not draw its
        // newly uploaded children on top while a sibling is still unavailable.
        return cover.Where(block => !cover.Any(parent => parent.Level > block.Level && parent.Contains(block))).ToArray();

        List<FalloutLodBlock>? Finer(FalloutLodBlock block)
        {
            if (available.Contains(block)) return [block];
            if (!_children.TryGetValue(block, out var children)) return null;
            var result = new List<FalloutLodBlock>();
            foreach (var child in children)
            {
                if (Finer(child) is not { } nested) return null;
                result.AddRange(nested);
            }
            return result;
        }
    }

    internal IReadOnlyList<FalloutLodBlock> PreparationOrder(IReadOnlyList<FalloutLodBlock> selected,
        IReadOnlySet<FalloutLodBlock> available, float x, float y)
    {
        var missing = selected.Where(block => !available.Contains(block)).ToArray();
        // Establish a complete coarse cover first, then refine nearest demand.
        // A child becoming available must never erase its unloaded siblings.
        var uncovered = missing.Where(block => ResidentCover([block], available).Count == 0).ToArray();
        var roots = _roots.Where(root => uncovered.Any(root.Contains) && !available.Contains(root));
        return roots.OrderBy(root => root.Distance(x, y)).Concat(missing.OrderBy(block => block.Distance(x, y)))
            .Distinct().ToArray();
    }

    internal static string WorldName(FalloutPluginStack records, FalloutFormKey world)
    {
        var seen = new HashSet<FalloutFormKey>();
        while (seen.Add(world))
        {
            var record = records.GetEffective(world);
            if (record.Signature != "WRLD") throw new InvalidDataException("LOD owner must be WRLD.");
            var fields = record.ReadSubrecords().ToArray();
            var flags = fields.SingleOrDefault(field => field.Signature == "PNAM").Data;
            if (!flags.IsEmpty && flags.Length != 2) throw new InvalidDataException("WRLD parent flags require two bytes.");
            var parent = fields.SingleOrDefault(field => field.Signature == "WNAM").Data;
            if (!parent.IsEmpty && flags.Length == 2 && (BinaryPrimitives.ReadUInt16LittleEndian(flags.Span) & 2) != 0)
            {
                if (parent.Length != 4) throw new InvalidDataException("WRLD LOD parent requires a FormID.");
                world = record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(parent.Span));
                continue;
            }
            return FalloutDialogueTopic.Text(fields.Single(field => field.Signature == "EDID").Data.Span);
        }
        throw new InvalidDataException("WRLD LOD inheritance cycle.");
    }
}
