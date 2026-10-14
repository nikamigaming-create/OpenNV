using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerReferenceOrientationBinding(FalloutFormKey Reference, FalloutFormKey Base,
    string BaseSignature, uint BaseFlags, bool IsCharacter)
{
    internal static FalloutPlayerReferenceOrientationBinding Read(FalloutPluginStack records, FalloutFormKey reference,
        FalloutFormKey currentBase, FalloutPlayerWorldOrientationSource source)
    {
        source.Validate();
        var winning = records.GetEffective(currentBase);
        var acbs = winning.ReadSubrecords().Where(row => row.Signature == "ACBS").ToArray();
        if (winning.Signature != "NPC_" || acbs is not [{ Data.Length: 24 }])
            throw new NotSupportedException("Player reference orientation has no exact winning NPC base flags.");
        // The selected Player virtual Character predicate returns true. The
        // current base's tilt flag remains an independent winning record field.
        var binding = new FalloutPlayerReferenceOrientationBinding(reference, currentBase, winning.Signature,
            BinaryPrimitives.ReadUInt32LittleEndian(acbs[0].Data.Span), true);
        binding.Validate(); return binding;
    }
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Reference.OwnerPlugin) || Reference.ObjectId == 0 ||
            string.IsNullOrWhiteSpace(Base.OwnerPlugin) || Base.ObjectId == 0 || BaseSignature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("Player orientation lost its current source reference/winning base binding.");
    }
}

internal enum FalloutReferenceRotationAxis { X, Y, Z }
internal enum FalloutReferenceOrientationStep { Requested, Stored, Normalized, ChangeCallback, Returned, Admission }
internal enum FalloutReferencePlacementIngress { RawPending, MoveTo, DirectedDoor, SourceLoad }
internal sealed record FalloutReferenceOrientationPlacement(Guid Invocation, string Producer, FalloutReferencePlacementIngress Ingress,
    long Changed, uint XBits, uint YBits, uint ZBits);
internal sealed record FalloutReferenceOrientationCall(Guid Invocation, string Producer, string Ingress, long Entered, long Changed,
    FalloutReferenceRotationAxis? Axis, uint ArgumentBits, uint? BeforeBits, uint? StoredBits,
    FalloutReferenceOrientationStep Step, byte? ChangeMask, Guid? Consumer, bool Returned, string? Failure);
internal sealed record FalloutPlayerReferenceOrientationPublication(Guid Process, Guid Factory,
    Guid? PreviousProcess = null, Guid? PreviousFactory = null);
internal sealed record FalloutPlayerReferenceOrientationSnapshot(string Schema, FalloutPlayerWorldOrientationSource Source,
    string Stack, Guid CapturedProcess, Guid Factory, FalloutPlayerReferenceOrientationBinding Binding, long Sequence,
    uint XBits, uint YBits, uint ZBits, long Stores, uint? PlayerFovBits, FalloutIniValue? PlayerFovSetting,
    Guid? PlayerFovInvocation, bool PlayerFovReturned, FalloutReferenceOrientationPlacement? Placement,
    FalloutReferenceOrientationCall? Call, string? Failure, bool Retired, Guid ProjectionWorld, Guid? ProjectionRootFactory,
    IReadOnlyList<FalloutPlayerReferenceOrientationPublication>? Publications = null);

internal interface IFalloutReferenceOrientationChangeConsumer
{
    FalloutPlayerWorldOrientationSource Source { get; }
    string Stack { get; }
    Guid Process { get; }
    Guid Factory { get; }
    FalloutFormKey Reference { get; }
    void RequireCurrent();
    void Changed(Guid invocation, byte mask);
}
