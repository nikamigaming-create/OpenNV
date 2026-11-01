namespace OpenNV.Runtime.Formats.Gamebryo;

// Actor KF loading selects the first top-level object. Additional roots are
// retained source objects, not independent automatic playback requests.
internal static class FalloutKfSequenceSource
{
    internal static FalloutNifControllerSequence Primary(FalloutNifFile source)
    {
        if (source.Roots.Count == 0)
            throw new InvalidDataException("Actor KF has no primary top-level object.");
        var primary = source.ReadObject(source.Roots[0]) as FalloutNifControllerSequence ??
            throw new NotSupportedException("Actor KF primary top-level object is not a controller sequence.");
        // The stream still deserializes its other top-level objects. Preserve
        // their typed-reader errors without treating them as playback requests.
        foreach (var root in source.Roots.Skip(1)) _ = source.ReadObject(root);
        return primary;
    }
}
