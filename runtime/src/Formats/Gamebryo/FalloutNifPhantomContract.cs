using System.Globalization;

namespace OpenNV.Runtime.Formats.Gamebryo;

// The decoded phantom is a query volume, never a rigid-body/floor surrogate.
// Reserved source lanes remain part of the identity without selecting behavior.
internal sealed record FalloutNifPhantomContract(string NifSha256, int Attachment, int Target,
    int Body, int Shape, ushort AttachmentFlags, FalloutNifCollisionFilter Filter,
    byte BroadPhase, string SourceIdentity)
{
    internal static FalloutNifPhantomContract Read(FalloutNifFile source, FalloutNifCollisionObject attachment)
    {
        if (attachment.IsBlend || attachment.Target < 0 || attachment.Body < 0 ||
            source.ReadObject(attachment.Body) is not FalloutNifSimpleShapePhantom phantom)
            throw new NotSupportedException("Shaped-phantom attachment has no independent query-volume contract.");
        if ((attachment.Flags & ~(1 | 4 | 8 | 32 | 64)) != 0 || phantom.Shape < 0)
            throw new NotSupportedException("Shaped-phantom attachment flags/shape have no native consumer.");
        if (phantom.Filter is not { Layer: 12, Flags: 0, Group: 0 } || phantom.BroadPhaseType != 2)
            throw new NotSupportedException($"Shaped-phantom filter {phantom.Filter.Layer}/{phantom.Filter.Flags}/{phantom.Filter.Group} " +
                $"and broad phase {phantom.BroadPhaseType} have no trigger contact consumer.");
        if (phantom.Property.Data != 0 || phantom.Property.Size != 0)
            throw new NotSupportedException("Shaped-phantom property entries require their actual source callback owner.");
        var identity = source.Sha256 + ":" + string.Join(':', new[] { attachment.Block.Index,
            attachment.Target, phantom.Block.Index, phantom.Shape }.Select(value => value.ToString(CultureInfo.InvariantCulture)));
        return new(source.Sha256, attachment.Block.Index, attachment.Target, phantom.Block.Index,
            phantom.Shape, attachment.Flags, phantom.Filter, phantom.BroadPhaseType, identity);
    }
}
