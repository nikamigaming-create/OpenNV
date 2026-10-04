using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutFactionRelationSnapshot(FalloutFormKey From, FalloutFormKey To,
    string FromSha256, string ToSha256, uint CombatReaction);

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<(FalloutFormKey From, FalloutFormKey To), FalloutFactionRelationSnapshot> _factionRelations = [];

    private FalloutPluginRecord FactionSource(FalloutFormKey faction)
    {
        var source = records.GetEffective(faction);
        if (source.Signature != "FACT") throw new InvalidDataException("Faction relationship requires two FACT forms.");
        return source;
    }

    internal uint FactionCombatReaction(FalloutFormKey from, FalloutFormKey to)
    {
        var source = FactionSource(from);
        _ = FactionSource(to);
        if (_factionRelations.TryGetValue((from, to), out var changed)) return changed.CombatReaction;
        uint? result = null;
        foreach (var field in source.ReadSubrecords().Where(field => field.Signature == "XNAM"))
        {
            if (field.Data.Length != 12) throw new NotSupportedException("Faction combat relation extent is unbound.");
            var data = field.Data.Span;
            var other = source.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(data));
            var reaction = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
            if (reaction > 3) throw new InvalidDataException("Faction combat relation is invalid.");
            if (other != to) continue;
            if (result is not null) throw new InvalidDataException("Faction repeats a source combat relation.");
            result = reaction;
        }
        return result ?? 0;
    }

    internal void SetFactionRelationship(FalloutFormKey first, FalloutFormKey second, bool allies,
        double firstFlag = 0, double secondFlag = 0)
    {
        uint Reaction(double flag) => flag switch
        {
            0 => allies ? 2u : 1u,
            1 => allies ? 3u : 0u,
            _ => throw new InvalidDataException("Faction relationship flag must be zero or one."),
        };
        var forward = Reaction(firstFlag);
        var reverse = Reaction(secondFlag);
        var firstHash = RecordHash(FactionSource(first));
        var secondHash = RecordHash(FactionSource(second));
        if (first == second && forward != reverse)
            throw new InvalidDataException("One faction cannot have two different reactions to itself.");
        _factionRelations[(first, second)] = new(first, second, firstHash, secondHash, forward);
        _factionRelations[(second, first)] = new(second, first, secondHash, firstHash, reverse);
    }

    internal IReadOnlyList<FalloutFactionRelationSnapshot> CaptureFactionRelations() => _factionRelations.Values
        .OrderBy(value => value.From.ToString(), StringComparer.Ordinal)
        .ThenBy(value => value.To.ToString(), StringComparer.Ordinal).ToArray();

    internal void RestoreFactionRelations(IReadOnlyList<FalloutFactionRelationSnapshot>? snapshots)
    {
        var admitted = new Dictionary<(FalloutFormKey, FalloutFormKey), FalloutFactionRelationSnapshot>();
        foreach (var snapshot in snapshots ?? [])
        {
            if (snapshot is null || snapshot.CombatReaction > 3 ||
                !RecordHash(FactionSource(snapshot.From)).Equals(snapshot.FromSha256, StringComparison.OrdinalIgnoreCase) ||
                !RecordHash(FactionSource(snapshot.To)).Equals(snapshot.ToSha256, StringComparison.OrdinalIgnoreCase) ||
                !admitted.TryAdd((snapshot.From, snapshot.To), snapshot))
                throw new InvalidDataException("Saved faction reaction repeats a pair, is invalid or differs from its winning source.");
        }
        _factionRelations.Clear();
        foreach (var (pair, snapshot) in admitted) _factionRelations.Add(pair, snapshot);
    }
}
