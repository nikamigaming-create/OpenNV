using Godot;

namespace OpenNV.Runtime.Presentation.Rendering;

internal static class RuntimeRendering
{
    internal static Godot.Environment.ToneMapper ParseToneMapper(string value) => value switch
    {
        "linear" => Godot.Environment.ToneMapper.Linear,
        "reinhard" => Godot.Environment.ToneMapper.Reinhardt,
        "filmic" => Godot.Environment.ToneMapper.Filmic,
        "aces" => Godot.Environment.ToneMapper.Aces,
        _ => throw new InvalidOperationException($"Unsupported configured tone mapper: {value}"),
    };
}
