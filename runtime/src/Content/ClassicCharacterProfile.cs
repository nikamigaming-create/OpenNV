using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record ClassicCharacterProfile(string Name, int Age, bool Female,
    int[] Special, int[] TaggedSkills, int[] Traits, int[] SkillBonuses)
{
    internal static readonly string[] SpecialNames = ["Strength", "Perception", "Endurance", "Charisma", "Intelligence", "Agility", "Luck"];
    internal static readonly string[] SkillNames = ["Small Guns", "Big Guns", "Energy Weapons", "Unarmed", "Melee Weapons", "Throwing",
        "First Aid", "Doctor", "Sneak", "Lockpick", "Steal", "Traps", "Science", "Repair", "Speech", "Barter", "Gambling", "Outdoorsman"];
    internal static readonly string[] TraitNames = ["Fast Metabolism", "Bruiser", "Small Frame", "One Hander", "Finesse", "Kamikaze",
        "Heavy Handed", "Fast Shot", "Bloody Mess", "Jinxed", "Good Natured", "Chem Reliant", "Chem Resistant", "Night Person", "Skilled", "Gifted"];

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 11 || Name.Any(char.IsControl) || Age is < 16 or > 35 ||
            Special is not { Length: 7 } || Special.Any(value => value is < 1 or > 10) || Special.Sum() != 40 ||
            TaggedSkills is not { Length: 3 } || TaggedSkills.Distinct().Count() != 3 || TaggedSkills.Any(value => value is < 0 or >= 18) ||
            Traits is null || Traits.Length > 2 || Traits.Distinct().Count() != Traits.Length || Traits.Any(value => value is < 0 or >= 16) ||
            SkillBonuses is not { Length: 18 } || SkillBonuses.Any(value => value < 0))
            throw new InvalidDataException("Classic character requires a name, valid age, 40 allocated SPECIAL points, three tag skills and at most two traits.");
    }
}

internal sealed record ClassicPremade(string Id, ClassicCharacterProfile Character, string Biography,
    string GcdPath, string GcdSha256, string BiographySha256, string PortraitPath, string PortraitSha256);

/// <summary>The original character picker reads GCD/BIO/FRM resources in the winning owned namespace.</summary>
internal static class ClassicPremadeReader
{
    internal static IReadOnlyList<ClassicPremade> Load(string campaign, Func<string, byte[]> read)
    {
        var result = new List<ClassicPremade>();
        foreach (var id in new[] { "combat", "stealth", "diplomat" })
        {
            var gcdPath = $"premade/{id}.gcd"; var portraitPath = $"art/intrface/{id}.frm";
            var gcd = read(gcdPath); var bio = read($"premade/{id}.bio"); var portrait = read(portraitPath);
            var biography = Text(bio).TrimEnd('\0').Trim();
            if (string.IsNullOrWhiteSpace(biography)) throw new InvalidDataException($"Character biography is empty: {id}.");
            result.Add(new(id, Read(campaign, gcd), biography, gcdPath, Hash(gcd), Hash(bio), portraitPath, Hash(portrait)));
        }
        return result;
    }

    internal static ClassicCharacterProfile Read(string campaign, ReadOnlySpan<byte> data)
    {
        var shift = campaign switch { "fallout-1" => 0, "fallout-2" => 1, _ => throw new ArgumentException("Unknown classic campaign.") };
        if (data.Length != 428 + shift * 4) throw new InvalidDataException("Classic premade GCD extent is invalid for this campaign.");
        var words = new int[data.Length / 4];
        for (var index = 0; index < words.Length; index++) words[index] = BinaryPrimitives.ReadInt32BigEndian(data[(index * 4)..]);
        var name = data.Slice(368 + shift * 4, 32); var end = name.IndexOf((byte)0);
        if (end <= 0 || words[35] is not (0 or 1) || words[103 + shift] != -1 || words[106 + shift] != 0)
            throw new InvalidDataException("Classic premade GCD name, sex or creation slots are invalid.");
        var result = new ClassicCharacterProfile(Text(name[..end]), words[34], words[35] == 1,
            words[1..8], words[(100 + shift)..(103 + shift)], words[(104 + shift)..(106 + shift)].Where(value => value != -1).ToArray(), words[71..89]);
        result.Validate(); return result;
    }

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Text(ReadOnlySpan<byte> bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);
    }
}
