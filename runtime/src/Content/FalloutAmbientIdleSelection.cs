using System.Runtime.CompilerServices;
using System.Buffers.Binary;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

// Automatic high-process requests use an independent countdown, not PACK polling.
internal sealed record FalloutAmbientIdleSnapshot(float WaitSeconds, IReadOnlyList<FalloutIdleReplayCooldown> Cooldowns)
{
    internal void Validate()
    {
        if (!float.IsFinite(WaitSeconds) || Cooldowns is null) throw new InvalidDataException("Automatic idle countdown is invalid.");
        if (Cooldowns.Any(value => value is null || !FalloutActorFurnitureContinuation.ValidKey(value.Idle) ||
            !FalloutActorFurnitureContinuation.ValidHash(value.Sha256) || !float.IsFinite(value.Remaining) || value.Remaining <= 0) ||
            Cooldowns.Select(value => value.Idle).Distinct().Count() != Cooldowns.Count)
            throw new InvalidDataException("Automatic idle replay history is invalid.");
    }
}

internal sealed class FalloutAmbientIdleState
{
    internal float WaitSeconds { get; private set; } = 1;
    internal FalloutIdleReplayState Replay { get; } = new();
    private readonly Dictionary<FalloutFormKey, string> _hashes = [];
    internal void Started(FalloutPluginRecord record, ushort seconds)
    { _hashes[record.FormKey] = FalloutActorFurnitureContinuation.RecordHash(record); Replay.Started(record.FormKey, seconds); }
    internal bool Advance(float seconds, float waitSeconds, bool admitted)
    {
        if (!float.IsFinite(seconds) || seconds < 0 || !float.IsFinite(waitSeconds))
            throw new InvalidDataException("Automatic idle clock/settings are invalid.");
        if (!admitted) return false;
        if (WaitSeconds < 0 && waitSeconds != 0) { WaitSeconds = waitSeconds; return true; }
        WaitSeconds -= seconds;
        if (!float.IsFinite(WaitSeconds)) throw new InvalidDataException("Automatic idle countdown overflowed.");
        return false;
    }
    internal FalloutAmbientIdleSnapshot Capture() => new(WaitSeconds, Replay.Remaining.Select(value =>
        new FalloutIdleReplayCooldown(value.Key, _hashes[value.Key], value.Value)).ToArray());
    internal void Restore(FalloutAmbientIdleSnapshot saved, FalloutPluginStack records)
    {
        saved.Validate();
        foreach (var value in saved.Cooldowns)
        {
            var source = records.GetEffective(value.Idle);
            if (source.Signature != "IDLE" || !FalloutActorFurnitureContinuation.RecordHash(source).Equals(value.Sha256, StringComparison.OrdinalIgnoreCase) ||
                value.Remaining > FalloutIdleAnimationData.Read(source).ReplayDelaySeconds)
                throw new InvalidDataException("Automatic idle replay history differs from its source.");
            _hashes.Add(value.Idle, value.Sha256);
        }
        WaitSeconds = saved.WaitSeconds; Replay.Restore(saved.Cooldowns.ToDictionary(value => value.Idle, value => value.Remaining));
    }
}

// One immutable registration tree per loaded source stack. Selection mutates
// only the caller's actual RNG and replay owner, never this index.
internal sealed class FalloutAmbientIdleTree
{
    private sealed class Index(FalloutPluginStack stack)
    {
        internal readonly FalloutIdleBranch[] Branches = stack.EffectiveRecords("IDLE")
            .Where(record => (record.Flags & 0x20) == 0).Select(Read)
            // Source flag40 registers in the independent direct-request
            // collection, not the ordinary automatic root/child collection.
            .Where(branch => (branch.Group & 0x40) == 0).ToArray();
        internal readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutIdleBranch>> Children = [];
        internal IReadOnlyList<FalloutIdleBranch>? Roots;
        private static FalloutIdleBranch Read(FalloutPluginRecord record)
        {
            if (record.ReadSubrecords().Any(field => field.Signature == "MODL")) return FalloutFurnitureIdleTree.Read(record);
            var related = record.ReadSubrecords().Single(field => field.Signature == "ANAM").Data;
            var timing = FalloutIdleAnimationData.Read(record);
            if (related.Length != 8) throw new InvalidDataException("Automatic IDLE ancestry has an invalid extent.");
            return new(record, "", record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(related.Span)),
                record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(related.Span[4..])),
                timing.Group, FalloutCondition.Read(record));
        }
    }
    private static readonly ConditionalWeakTable<FalloutPluginStack, Index> Indexes = new();
    private readonly Index _index;
    private readonly string _directory;
    internal FalloutAmbientIdleTree(FalloutPluginStack stack, string skeleton)
    {
        _index = Indexes.GetValue(stack, source => new(source));
        _directory = skeleton[..skeleton.LastIndexOf('/')].Replace('\\', '/').TrimEnd('/') + "/";
    }
    internal FalloutPluginRecord? Select(Func<FalloutCondition, float> evaluate, Func<FalloutFormKey, bool> canReplay)
    {
        var roots = _index.Roots ??= FalloutFurnitureIdleTree.Order(_index.Branches.Where(branch => branch.Parent is null).ToArray());
        foreach (var root in roots)
        {
            if (!root.Model.StartsWith(_directory, StringComparison.OrdinalIgnoreCase)) continue;
            var selected = Visit(root, []);
            if (selected is not null)
                return selected.Model.EndsWith(".kf", StringComparison.OrdinalIgnoreCase) ? selected.Record : null;
        }
        return null;

        FalloutIdleBranch? Visit(FalloutIdleBranch branch, HashSet<FalloutFormKey> ancestors)
        {
            if (!ancestors.Add(branch.Record.FormKey)) throw new InvalidDataException("Automatic IDLE tree is cyclic.");
            // Conditions precede replay membership, including their RNG draws.
            if (!FalloutCondition.AllPass(branch.Conditions, evaluate, evaluateRunOn: true)) return null;
            var file = branch.Model.EndsWith(".kf", StringComparison.OrdinalIgnoreCase);
            FalloutIdleBranch? selected = file ? canReplay(branch.Record.FormKey) ? branch : null :
                (branch.Group & 0x80) != 0 ? branch : null;
            if (!_index.Children.TryGetValue(branch.Record.FormKey, out var children))
            {
                children = FalloutFurnitureIdleTree.Order(_index.Branches.Where(value => value.Parent == branch.Record.FormKey).ToArray());
                _index.Children.Add(branch.Record.FormKey, children);
            }
            foreach (var child in children)
                if (Visit(child, new(ancestors)) is { } leaf) return leaf;
            return selected;
        }
    }
}
