namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class FalloutNifTextureAddressing
{
    internal static bool RepeatForGodot(uint clampMode) => clampMode switch
    {
        0 => false, // CLAMP_S_CLAMP_T
        3 => true,  // WRAP_S_WRAP_T
        1 or 2 => throw new NotSupportedException(
            $"NIF texture clamp mode {clampMode} requires independent U/V sampler addressing."),
        _ => throw new InvalidDataException($"Unknown NIF texture clamp mode {clampMode}."),
    };
}
