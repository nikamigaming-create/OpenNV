using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicArmorDefinition(int ArmorClass, int[] Resistance, int[] Threshold, int Perk, int MaleArt, int FemaleArt);
internal sealed record ClassicWeaponDefinition(int Animation, int MinimumDamage, int MaximumDamage, int DamageType,
    int PrimaryRange, int SecondaryRange, int ProjectilePid, int MinimumStrength, int PrimaryAp, int SecondaryAp,
    int CriticalFailure, int Perk, int BurstRounds, int Caliber, int AmmoPid, int Capacity, byte Sound);
internal sealed record ClassicAmmoDefinition(int Caliber, int PackSize, int ArmorClassModifier, int ResistanceModifier, int Multiplier, int Divisor);
internal sealed record ClassicDrugDefinition(int[] Stats, int[] Immediate, int FirstDelay, int[] FirstEffect,
    int SecondDelay, int[] SecondEffect, int AddictionChance, int AddictionPerk, int AddictionDelay);
internal sealed record ClassicMiscDefinition(int PowerPid, int PowerType, int Charges);
internal sealed record ClassicItemDefinition(int Pid, string Name, string Description, string? Icon, int Weight,
    int Size, int Subtype, int Script, uint ContainerFlags, int ContainerSize = 0, uint ExtendedFlags = 0,
    ClassicArmorDefinition? Armor = null, ClassicWeaponDefinition? Weapon = null, ClassicAmmoDefinition? Ammo = null,
    ClassicDrugDefinition? Drug = null, ClassicMiscDefinition? Misc = null, int? KeyCode = null);

/// <summary>Both classic games' original item PRO payloads. The donor library supplies no item rules.</summary>
internal sealed class ClassicItemDefinitions(ClassicMapCatalog source)
{
    private readonly Dictionary<int, ClassicItemDefinition> _definitions = [];
    private readonly Dictionary<int, string> _messages = Messages(source.Read("text/english/game/pro_item.msg", out _));
    private IReadOnlyList<string>? _icons;

    internal static Dictionary<int, string> Messages(byte[] bytes)
    {
        var result = new Dictionary<int, string>();
        foreach (Match match in Regex.Matches(Encoding.Latin1.GetString(bytes), @"^\s*\{(\d+)\}\{[^}]*\}\{([^}]*)\}", RegexOptions.Multiline))
            result[int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)] = match.Groups[2].Value;
        return result;
    }

    internal ClassicItemDefinition Read(int pid)
    {
        if (_definitions.TryGetValue(pid, out var result)) return result;
        var prototype = Fallout1NativeObjectGraphReader.ResolvePrototype(source, pid);
        if (prototype.ObjectType != 0 || prototype.LogicalPath is null) throw new NotSupportedException("This source object is not an inventory item.");
        var bytes = source.Read(prototype.LogicalPath, out _);
        _icons ??= Fallout1NativeLists.Read(source.Read("art/inven/inven.lst", out _));
        result = Decode(bytes, pid, _messages, _icons);
        _definitions.Add(pid, result); return result;
    }

    internal static ClassicItemDefinition Decode(byte[] bytes, int pid, IReadOnlyDictionary<int, string> messages, IReadOnlyList<string> icons)
    {
        int Word(int offset)
        {
            if (offset < 0 || offset > bytes.Length - 4) throw new InvalidDataException($"Item PRO {pid} is truncated at {offset:x}.");
            return BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
        }
        int[] Words(int offset, int count) => Enumerable.Range(0, count).Select(index => Word(offset + index * 4)).ToArray();
        if (bytes.Length < 57 || Word(0) != pid || (uint)pid >> 24 != 0) throw new InvalidDataException("Item PRO identity/common header is invalid.");
        var subtype = Word(32); var fid = unchecked((uint)Word(52)); var message = Word(4);
        string? icon = null;
        if (fid != uint.MaxValue)
        {
            var index = (int)(fid & 0xfff);
            if (fid >> 24 != 7 || index >= icons.Count) throw new InvalidDataException("Item inventory art is outside its source INVEN list.");
            icon = "art/inven/" + icons[index];
        }
        var expected = subtype switch
        {
            0 => 129,
            1 => 65,
            2 => 125,
            3 => 122,
            4 => 81,
            5 => 69,
            6 => 61,
            _ => throw new NotSupportedException($"Item PRO {pid} subtype {subtype} is unsupported.")
        };
        if (bytes.Length != expected) throw new NotSupportedException($"Item PRO {pid} subtype {subtype} has {bytes.Length} bytes; expected {expected}.");
        if (Word(44) < 0 || Word(40) < 0) throw new InvalidDataException("Source item dimensions are invalid.");
        return new(pid, messages.GetValueOrDefault(message, $"Item {pid}"), messages.GetValueOrDefault(message + 1, ""),
            icon, Word(44), Word(40), subtype, Word(28), subtype == 1 ? unchecked((uint)Word(61)) : 0,
            subtype == 1 ? Word(57) : 0, unchecked((uint)Word(24)),
            subtype == 0 ? new(Word(57), Words(61, 7), Words(89, 7), Word(117), Word(121), Word(125)) : null,
            subtype == 3 ? new(Word(57), Word(61), Word(65), Word(69), Word(73), Word(77), Word(81), Word(85),
                Word(89), Word(93), Word(97), Word(101), Word(105), Word(109), Word(113), Word(117), bytes[121]) : null,
            subtype == 4 ? new(Word(57), Word(61), Word(65), Word(69), Word(73), Word(77)) : null,
            subtype == 2 ? new(Words(57, 3), Words(69, 3), Word(81), Words(85, 3), Word(97), Words(101, 3), Word(113), Word(117), Word(121)) : null,
            subtype == 5 ? new(Word(57), Word(61), Word(65)) : null,
            subtype == 6 ? Word(57) : null);
    }
}
