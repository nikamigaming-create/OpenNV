using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// The selected source opener either enters the perk catalog directly or
// consumes a live signed level interval. Absence of a GMST cannot select it.
internal sealed record FalloutPerkAwardCadence(string? LevelIntervalSetting)
{
    internal void Validate()
    {
        if (LevelIntervalSetting is { } setting &&
            (string.IsNullOrWhiteSpace(setting) || setting[0] != 'i'))
            throw new InvalidDataException("Perk cadence requires its source integer-setting operand.");
    }

    internal int? ReadInterval(FalloutPluginStack records)
    {
        Validate();
        return LevelIntervalSetting is { } setting
            ? unchecked((int)FalloutGameSettingIntegers.Read(records, setting)) : null;
    }
}
