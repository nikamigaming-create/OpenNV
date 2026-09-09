using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>
/// Persistent player-authored appearance, separate from campaign progression.
/// Source identity and the complete FaceGen state follow the character, not the view.
/// </summary>
internal sealed record ClassicAppearanceDraft(string Schema, string Campaign, string ClassicProfileId,
    string AppearanceStackId, FalloutNativeRaceSexSelection Character)
{
    internal const string CurrentSchema = "opennv-classic-character-appearance/v1";

    internal static ClassicAppearanceDraft? Read(string path, string campaign, string classicProfile, string appearanceStack)
    {
        if (!File.Exists(path)) return null;
        var draft = JsonSerializer.Deserialize<ClassicAppearanceDraft>(File.ReadAllBytes(path))
            ?? throw new InvalidDataException("Saved character appearance is empty.");
        if (draft.Schema != CurrentSchema || draft.Campaign != campaign || draft.ClassicProfileId != classicProfile ||
            draft.AppearanceStackId != appearanceStack || draft.Character?.Face is null)
            throw new InvalidDataException("Saved character appearance belongs to a different campaign or appearance source.");
        draft.Character.Face.Validate(); return draft;
    }

    internal void Save(string path)
    {
        if (Schema != CurrentSchema || Character.Face is null) throw new InvalidDataException("Character appearance is incomplete.");
        Character.Face.Validate();
        var destination = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(this));
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
