using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginBinaryFile
{
    internal FalloutNativeBinarySourceState CaptureSourceState()
    {
        var actual = Capture();
        return new(actual.Plugin, actual.SourceSha256, actual.RuntimeSha256, actual.ConstructionOwner,
            actual.BufferCapacity, actual.BinaryOffset, actual.BackendOffset, actual.LogicalOffset,
            actual.CachedSize, actual.BufferBytes, actual.BufferConsumed,
            Convert.ToHexString(SHA256.HashData(actual.WrittenBuffer)), actual.WrittenExtent,
            actual.BufferSources.ToArray(), actual.Revision, actual.Good);
    }

    internal FalloutNativeBinaryFileSnapshot RecreateSourceState(FalloutNativeBinarySourceState saved)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(saved);
        if (!StringComparer.OrdinalIgnoreCase.Equals(saved.Plugin, Plugin) ||
            !StringComparer.OrdinalIgnoreCase.Equals(saved.SourceSha256, SourceSha256) ||
            !StringComparer.OrdinalIgnoreCase.Equals(saved.RuntimeSha256, _construction.RuntimeSha256) ||
            saved.ConstructionOwner != _construction.DeclarationOwner || saved.BufferCapacity != Capacity ||
            saved.WrittenExtent > Capacity || saved.BufferSources is null ||
            saved.WrittenSha256 is null || saved.WrittenSha256.Length != 64 || !saved.WrittenSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Source-bound binary continuation changed its actual contributor/construction/provenance.");
        var buffer = new byte[checked((int)saved.WrittenExtent)]; var next = 0U;
        foreach (var range in saved.BufferSources)
        {
            if (range is null || range.Length == 0 || range.Offset != next || next > saved.WrittenExtent ||
                range.Length > saved.WrittenExtent - next || (ulong)range.SourceOffset + range.Length > SourceLength)
                throw new InvalidDataException("Source-bound binary continuation has an incomplete or overlapping retained byte range.");
            ReadOriginal(range.SourceOffset, buffer.AsSpan(checked((int)range.Offset), checked((int)range.Length)));
            next = checked(next + range.Length);
        }
        if (next != saved.WrittenExtent || !StringComparer.OrdinalIgnoreCase.Equals(
                Convert.ToHexString(SHA256.HashData(buffer)), saved.WrittenSha256))
            throw new InvalidDataException("Source-bound retained buffer differs from its complete original source digest.");
        return new(saved.Plugin, saved.SourceSha256, saved.RuntimeSha256, saved.ConstructionOwner,
            saved.BufferCapacity, saved.BinaryOffset, saved.BackendOffset, saved.LogicalOffset, saved.CachedSize,
            saved.BufferBytes, saved.BufferConsumed, buffer, saved.WrittenExtent, saved.BufferSources.ToArray(), saved.Revision, saved.Good);
    }

    internal void RestoreSourceState(FalloutNativeBinarySourceState saved) => Restore(RecreateSourceState(saved));
}
