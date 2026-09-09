using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>One saved character choice; campaign progression is created only by the gameplay owner.</summary>
internal sealed record ClassicCharacterDraft(string Schema, string Campaign, string ClassicProfileId,
    string? PremadeId, string? PremadeGcdSha256, string? PremadeBiographySha256, string? PremadePortraitSha256,
    ClassicCharacterProfile Character, ClassicAppearanceDraft? Appearance)
{
    internal const string CurrentSchema = "opennv-classic-character-choice/v1";

    internal void Validate(string campaign, string profile, IReadOnlyList<ClassicPremade> premades)
    {
        if (Schema != CurrentSchema || Campaign != campaign || ClassicProfileId != profile || Character is null)
            throw new InvalidDataException("Character selection belongs to another campaign or owned installation.");
        Character.Validate();
        if (PremadeId is not null)
        {
            var source = premades.SingleOrDefault(row => row.Id == PremadeId);
            if (source is null || PremadeGcdSha256 != source.GcdSha256 || PremadeBiographySha256 != source.BiographySha256 ||
                PremadePortraitSha256 != source.PortraitSha256 || JsonSerializer.Serialize(Character) != JsonSerializer.Serialize(source.Character) || Appearance is not null)
                throw new InvalidDataException("Saved premade selection differs from its owned character files.");
        }
        else
        {
            if (PremadeGcdSha256 is not null || PremadeBiographySha256 is not null || PremadePortraitSha256 is not null)
                throw new InvalidDataException("Custom character cannot claim premade likeness provenance.");
            if (Appearance is not null)
            {
                if (Appearance.Schema != ClassicAppearanceDraft.CurrentSchema || Appearance.Campaign != campaign ||
                    Appearance.ClassicProfileId != profile || Appearance.Character.Female != Character.Female || Appearance.Character.Face is null)
                    throw new InvalidDataException("Custom character and approved appearance identities differ.");
                Appearance.Character.Face.Validate();
            }
        }
    }

    internal static ClassicCharacterDraft? Read(string path, string campaign, string profile, IReadOnlyList<ClassicPremade> premades)
    {
        if (!File.Exists(path)) return null;
        var result = JsonSerializer.Deserialize<ClassicCharacterDraft>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Character choice is empty.");
        result.Validate(campaign, profile, premades); return result;
    }

    internal void Save(string path, IReadOnlyList<ClassicPremade> premades)
    {
        Validate(Campaign, ClassicProfileId, premades);
        var target = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(this)); File.Move(temporary, target, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
