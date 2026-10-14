namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginDomainChild
{
    internal byte[] ReadModuleBookSoundPair(uint image, uint imageBytes, uint pairRva)
        => ReadModelExtent(checked(image + pairRva), 29, image, imageBytes, 0x1000000, null, executable: true);
}
