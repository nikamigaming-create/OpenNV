using System.Text.Json;
using OpenNV.Runtime.Presentation.CharacterCreation;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal sealed partial record OpeningNewGameFlow
{
    private static OpeningNativeFaceGenAgeControl ParseNativeFaceGenAgeControl(
        JsonElement source) => new(
            source.GetProperty("settingEntity").GetString()!,
            source.GetProperty("sourceLabel").GetString()!,
            source.GetProperty("rawMinimum").GetSingle(),
            source.GetProperty("rawMaximum").GetSingle(),
            source.GetProperty("rawStep").GetSingle(),
            source.GetProperty("mappedMinimumYears").GetSingle(),
            source.GetProperty("mappedMaximumYears").GetSingle(),
            source.GetProperty("mappedMultiplier").GetSingle(),
            source.GetProperty("mappedAddend").GetSingle(),
            source.GetProperty("geometryAxisSha256").GetString()!,
            ParseFloatArray(source.GetProperty("geometryAxis")),
            source.GetProperty("geometryOffset").GetSingle(),
            source.GetProperty("textureAxisSha256").GetString()!,
            ParseFloatArray(source.GetProperty("textureAxis")),
            source.GetProperty("textureOffset").GetSingle(),
            source.GetProperty("semantics").GetString()!);
}
