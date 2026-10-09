using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record FalloutSkyCloudGeometry(int Slot, int Geometry, int Property);
internal sealed record FalloutSkyCloudResource(string Resource, string SourceSha256, int Root,
    IReadOnlyList<FalloutSkyCloudGeometry> Slots)
{
    internal const string Path = "meshes\\sky\\clouds.nif";
    internal static FalloutSkyCloudResource Read(ReadOnlyMemory<byte> bytes, string resource)
    {
        var nif = FalloutNifFile.Read(bytes);
        if (nif.Roots.Count != 1 || nif.ReadObject(nif.Roots[0]) is not FalloutNifNode root)
            throw new NotSupportedException("Source Clouds model has no unique authored node/child-list factory.");
        var slots = new List<FalloutSkyCloudGeometry>();
        foreach (var child in root.Children)
        {
            if (child < 0) throw new NotSupportedException("Source Clouds direct child is null; the original native child-index/count factory is unowned for this layout.");
            if (nif.ReadObject(child) is not FalloutNifGeometry geometry)
                throw new NotSupportedException("Source Clouds direct child has an unowned native geometry/RTTI factory.");
            var properties = geometry.Properties.Where(index => index >= 0).Select(nif.ReadObject)
                .OfType<FalloutNifSkyShaderProperty>().ToArray();
            if (properties.Length != 1 || properties[0].SkyObjectType != FalloutSkyNativeChildSource.CloudPropertyType ||
                properties[0].Block.TypeName != FalloutSkyNativeChildSource.CloudPropertyClass || properties[0].Controller != -1 ||
                properties[0].ExtraData.Length != 0)
                throw new NotSupportedException("Source Clouds child has no admitted original sky-property/sampler factory.");
            slots.Add(new(slots.Count, child, properties[0].Block.Index));
        }
        // This is the selected Clouds constructor's physical array extent.
        // A larger/different source factory needs its actual capacity owner.
        if (slots.Count > FalloutSkyNativeChildSource.CloudCapacity || slots.Select(slot => slot.Geometry).Distinct().Count() != slots.Count)
            throw new NotSupportedException("Source Clouds child list exceeds or aliases the selected native slot factory.");
        return new(resource, Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant(), root.Block.Index, slots);
    }
}
