namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class RuntimeNifParticleSystem
{
    private readonly Dictionary<int, FalloutNifColorAnimation> _keyColors = [];

    private void ConfigureColorKeys(FalloutNifFile file, FalloutNifParticleColorKeys modifier) =>
        _keyColors.Add(modifier.Block.Index, new FalloutNifColorAnimation(file, modifier.Data));
}
