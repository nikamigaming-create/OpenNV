using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerReferenceOrientationOwner
{
    internal FalloutPlayerReferenceOrientationSnapshot Capture()
    {
        RequireOwner(allowFailure: true);
        var saved = new FalloutPlayerReferenceOrientationSnapshot(Schema, _source, Stack, Process, Factory, Binding,
            _sequence, _x, _y, _z, _stores, _playerFov, _playerFovSetting, _playerFovInvocation, _playerFovReturned,
            _placement, _call, _failure, _retired, _projection.World, _projection.CurrentRootFactory,
            Array.AsReadOnly(_publications.ToArray()));
        Validate(saved); return saved;
    }

    internal static void Validate(FalloutPlayerReferenceOrientationSnapshot saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (saved.Publications is not { Count: > 0 })
            throw new InvalidDataException("Reference orientation omitted its original and current factory publications.");
        var processes = new HashSet<Guid>(); var factories = new HashSet<Guid>();
        FalloutPlayerReferenceOrientationPublication? previous = null;
        foreach (var publication in saved.Publications)
        {
            if (publication is null || publication.Process == Guid.Empty || publication.Factory == Guid.Empty ||
                !processes.Add(publication.Process) || !factories.Add(publication.Factory) ||
                (previous is null ? publication.PreviousProcess is not null || publication.PreviousFactory is not null :
                    publication.PreviousProcess != previous.Process || publication.PreviousFactory != previous.Factory))
                throw new InvalidDataException("Reference orientation repeated or detached a cold factory publication.");
            previous = publication;
        }
        if (previous!.Process != saved.CapturedProcess || previous.Factory != saved.Factory)
            throw new InvalidDataException("Reference orientation omitted its current actual factory publication.");
        saved.Source.Validate(); saved.Binding.Validate();
        if (saved.Schema != Schema || !saved.Binding.IsCharacter || string.IsNullOrWhiteSpace(saved.Stack) || saved.CapturedProcess == Guid.Empty ||
            saved.Factory == Guid.Empty || saved.ProjectionWorld == Guid.Empty || saved.ProjectionRootFactory == Guid.Empty ||
            saved.Sequence < 0 || saved.Stores < 0 ||
            saved.PlayerFovInvocation == Guid.Empty ||
            (saved.PlayerFovInvocation is null) != (saved.PlayerFovBits is null) ||
            (saved.PlayerFovInvocation is null) != (saved.PlayerFovSetting is null) ||
            saved.PlayerFovReturned && saved.PlayerFovInvocation is null ||
            saved.PlayerFovInvocation is not null && !saved.PlayerFovReturned && saved.Failure is null ||
            saved.Stores == 0 && (saved.XBits != 0 || saved.YBits != 0 || saved.ZBits != 0 || saved.Placement is not null))
            throw new InvalidDataException("Cold reference orientation lost its constructor fields or current source process.");
        if (saved.PlayerFovSetting is { } setting)
        {
            if (setting is not
                {
                    Declaration:
                    {
                        Name: FalloutPlayerWorldOrientationSource.ConstructorFovName,
                        Collection: FalloutIniCollection.Main, Payload: FalloutPlayerWorldOrientationSource.ConstructorFovDefaultBits
                    }, Number: { } number
                } ||
                !double.IsFinite(number) || (double)(float)number != number || string.IsNullOrWhiteSpace(setting.Origin) ||
                saved.PlayerFovBits != BitConverter.SingleToUInt32Bits((float)number) || saved.Sequence < 2)
                throw new InvalidDataException("Cold Player FOV lost its independent effective Main constructor store.");
        }
        if (saved.Placement is { } placement)
        {
            if (placement.Invocation == Guid.Empty || string.IsNullOrWhiteSpace(placement.Producer) ||
                !Enum.IsDefined(placement.Ingress) || placement.Changed < 1 || placement.Changed > saved.Sequence || saved.Stores < 3 ||
                !float.IsFinite(BitConverter.UInt32BitsToSingle(placement.XBits)) ||
                !float.IsFinite(BitConverter.UInt32BitsToSingle(placement.YBits)) ||
                !float.IsFinite(BitConverter.UInt32BitsToSingle(placement.ZBits)))
                throw new InvalidDataException("Cold reference placement omitted its captured raw rotation payload.");
            if ((saved.Call is null || placement.Changed > saved.Call.Changed) &&
                (saved.XBits != placement.XBits || saved.YBits != placement.YBits || saved.ZBits != placement.ZBits))
                throw new InvalidDataException("Cold current reference rotation disagrees with its last actual placement.");
        }
        if (saved.Call is not { } call)
        {
            if (saved.Placement is null && saved.Stores != 0)
                throw new InvalidDataException("Cold reference orientation invented stores without their actual writer.");
            return;
        }
        if (call.Invocation == Guid.Empty || string.IsNullOrWhiteSpace(call.Producer) || call.Entered < 1 ||
            call.Changed <= call.Entered || call.Changed > saved.Sequence || !Enum.IsDefined(call.Step) ||
            call.Returned != (call.Step == FalloutReferenceOrientationStep.Returned) ||
            call.Returned && call.Failure is not null || !call.Returned && call.Failure is null ||
            call.Failure is not null && saved.Failure is null || call.Consumer == Guid.Empty)
            throw new InvalidDataException("Cold reference orientation changed its actual failed or returned writer prefix.");
        if (call.Axis is not { } axis)
        {
            if (call.Ingress is not ("virtual-yaw-delta" or "Player-pitch-delta") ||
                call.Step != FalloutReferenceOrientationStep.Admission || call.BeforeBits is not null || call.StoredBits is not null ||
                call.ChangeMask is not null || call.Consumer is not null || call.Returned)
                throw new InvalidDataException("Cold reference delta manufactured an admitted raw setter or change callback.");
            return;
        }
        if (!Enum.IsDefined(axis) || call.Ingress != "raw-reference-setter" || call.BeforeBits is null || call.StoredBits is null ||
            saved.Stores < 1 || call.Step is FalloutReferenceOrientationStep.Requested or FalloutReferenceOrientationStep.Admission ||
            call.Step == FalloutReferenceOrientationStep.Stored && (call.StoredBits != call.ArgumentBits || call.ChangeMask is not null || call.Consumer is not null) ||
            call.Step == FalloutReferenceOrientationStep.Normalized && (axis != FalloutReferenceRotationAxis.Z || !saved.Binding.IsCharacter || call.ChangeMask is not null || call.Consumer is not null) ||
            call.Step is FalloutReferenceOrientationStep.ChangeCallback or FalloutReferenceOrientationStep.Returned && call.ChangeMask != 2 ||
            call.Returned && call.Consumer is null)
            throw new InvalidDataException("Cold raw reference setter changed its store-before-mask-two callback order.");
        if (axis == FalloutReferenceRotationAxis.Z && saved.Binding.IsCharacter && call.Step != FalloutReferenceOrientationStep.Stored &&
            call.StoredBits != FalloutPlayerControlOrientation.NormalizeBits(call.ArgumentBits))
            throw new InvalidDataException("Cold raw yaw setter changed its source normalization store.");
        if (saved.Placement is null || call.Changed > saved.Placement.Changed)
        {
            var current = axis switch { FalloutReferenceRotationAxis.X => saved.XBits, FalloutReferenceRotationAxis.Y => saved.YBits, _ => saved.ZBits };
            if (current != call.StoredBits) throw new InvalidDataException("Cold reference field disagrees with its last actual raw setter.");
        }
    }
}
