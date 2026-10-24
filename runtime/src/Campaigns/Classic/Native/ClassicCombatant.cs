using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Immutable owned PRO/AI declarations. Mutable HP, AP and turns live in the encounter.</summary>
internal sealed class ClassicCombatant
{
    private readonly byte[] _bytes;
    internal Fallout1NativeMapObject Source { get; }
    internal int Stat(int index) => checked(Word(48 + index * 4) + Word(188 + index * 4));
    internal int SkillPoints(int index) => Word(328 + index * 4);
    internal int Experience => Word(404);
    internal int KillType => Word(408);
    internal int DamageType => Word(412);
    private int Word(int offset) => BinaryPrimitives.ReadInt32BigEndian(_bytes.AsSpan(offset));

    internal ClassicCombatant(ClassicPlayerSession player, ClassicCombatParticipant actor)
    {
        Source = player.Catalog.Load(actor.Map).Objects.TopLevelObjects.Single(row => row.Serial == actor.Serial);
        _bytes = player.Catalog.Read(Source.Prototype.LogicalPath!, out _);
        if (_bytes.Length != 416 || ClassicPremadeReader.Hash(_bytes) != actor.PrototypeSha256 ||
            Source.Inventory.Count != 0 ||
            (Word(44) & ~0x60) != 0 || actor.Results != 0 || ((Source.Fid >> 16) & 0xff) != 0)
            throw new NotSupportedException("Combatant needs its unowned script, equipment, status or prototype-flag behavior.");
        if (Experience < 0 || KillType is < 0 or >= 19 || DamageType != 0)
            throw new NotSupportedException("Combatant death category or native damage type is unsupported.");
    }

    internal int Unarmed(ClassicSkillContract contract) => ClassicSkillOwner.Resolve(contract, "Unarmed",
        new(Enumerable.Range(0, 7).Select(Stat).ToArray(), SkillPoints(3), false, 0, 0, ClassicSkillDifficulty.Normal));

    internal int RequireAdjacentRetaliation(ClassicPlayerSession player, ClassicCombatParticipant actor, int chance)
    {
        if (ClassicHexGrid.Distance(actor.Tile, player.Tile) != 1)
            throw new NotSupportedException("Enemy turn requires its source approach/flee movement owner.");
        var sections = new List<Dictionary<string, string>>();
        Dictionary<string, string>? section = null;
        foreach (var raw in Encoding.Latin1.GetString(player.Catalog.Read("data/ai.txt", out _)).Split('\n'))
        {
            var line = raw.Split(';')[0].Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { section = new(StringComparer.OrdinalIgnoreCase); sections.Add(section); }
            else if (section is not null && line.IndexOf('=') is var split && split > 0)
            {
                if (!section.TryAdd(line[..split].Trim(), line[(split + 1)..].Trim()))
                    throw new InvalidDataException("Duplicate AI declaration key.");
            }
        }
        var packet = sections.SingleOrDefault(row => row.TryGetValue("packet_num", out var value) &&
            int.TryParse(value, out var number) && number == actor.AiPacket)
            ?? throw new InvalidDataException("Source AI packet is absent or ambiguous.");
        int Number(string key) => packet.TryGetValue(key, out var value) && int.TryParse(value, out var number)
            ? number : throw new InvalidDataException("Source AI packet has no numeric " + key + ".");
        // This owner executes the source's adjacent unarmed retaliation branch.
        // Flee, distance preferences, drugs and called attacks have their own actions.
        if (Number("chance") != 0 || actor.MaximumActionPoints > 30 || actor.HitPoints < Number("min_hp") || chance < Number("min_to_hit") ||
            packet.GetValueOrDefault("run_away_mode", "none") is not ("none" or "never") ||
            packet.GetValueOrDefault("distance", "charge") is not ("charge" or "on_your_own" or "stay") ||
            Stat(4) >= 5)
            throw new NotSupportedException("Source AI selected an unowned flee, distance or called-shot decision.");
        var frequency = Number("called_freq");
        if (frequency <= 0) throw new InvalidDataException("Invalid source AI called-shot frequency.");
        return frequency;
    }
}
