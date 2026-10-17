using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutRagdollDriveParameter(string Setting, uint Default, uint Value, string? Origin, long Stores);
internal sealed record FalloutRagdollDriveBody(int Body, ulong NativeBody, FalloutPhysicalVector3 CenterOfMass,
    FalloutPhysicalRotation Rotation, FalloutPhysicalVector3 LinearVelocity, FalloutPhysicalVector3 AngularVelocity);
internal sealed record FalloutLivingRagdollDriveSnapshot(FalloutReferenceModelDataInvocation Load, Guid Identity,
    Guid Instance, IReadOnlyList<int> Parents, IReadOnlyList<FalloutRagdollDriveParameter> Parameters,
    IReadOnlyList<FalloutRagdollDriveBody> PreviousBodies, IReadOnlyList<int>? ParameterIndices,
    bool Returned, string? Failure, bool Retired)
{
    internal void Validate(FalloutLivingRagdollSkeletonSnapshot instance)
    {
        instance.Descriptor.Validate();
        if (Load != instance.Load || Identity == Guid.Empty || Instance != instance.Instance || Parents is null || Parameters is null ||
            PreviousBodies is null || !Parents.SequenceEqual(instance.Descriptor.Bones.Select(bone => bone.Parent)) ||
            ParameterIndices is not null || Parameters.Count != FalloutRagdollDriveSource.Settings.Count ||
            PreviousBodies.Count > Parents.Count || Returned == (Failure is not null) || Failure is not null && string.IsNullOrWhiteSpace(Failure) ||
            Returned && (PreviousBodies.Count != Parents.Count || Parameters.Any(value => value.Stores != 1)))
            throw new InvalidDataException("Ragdoll drive changed its source instance, parent tree or entered constructor prefix.");
        var unwritten = false;
        for (var index = 0; index < Parameters.Count; ++index)
        {
            var value = Parameters[index];
            if (value is null || value.Setting != FalloutRagdollDriveSource.Settings[index] || value.Default != FalloutRagdollDriveSource.Defaults[index] ||
                value.Stores is not (0 or 1) || unwritten && value.Stores != 0 || value.Stores == 0 && (value.Value != value.Default || value.Origin is not null) ||
                value.Stores == 1 && (string.IsNullOrWhiteSpace(value.Origin) || PreviousBodies.Count != Parents.Count || !float.IsFinite(BitConverter.UInt32BitsToSingle(value.Value))))
                throw new InvalidDataException("Ragdoll drive omitted or reordered a real source setting store.");
            unwritten |= value.Stores == 0;
        }
        for (var index = 0; index < PreviousBodies.Count; ++index)
            if (PreviousBodies[index] is not { } body || body.Body != instance.Descriptor.Bones[index].Body || body.NativeBody != instance.NativeBodies[index])
                throw new InvalidDataException("Ragdoll drive history borrowed another physical body's state.");
    }
}

internal sealed class FalloutLivingRagdollDrive
{
    private readonly FalloutLivingActorModel _model;
    private readonly FalloutLivingRagdollSkeletonSnapshot _instance;
    private readonly IReadOnlyList<FalloutLivingPhysicalBody> _bodies;
    private readonly FalloutReferenceModelDataInvocation _load;
    private readonly Guid _identity = Guid.NewGuid();
    private readonly List<FalloutRagdollDriveBody> _previous = [];
    private readonly FalloutRagdollDriveParameter[] _parameters;
    private bool _entered, _returned, _retired;
    private long _reentry;
    private string? _failure;

    internal FalloutLivingRagdollDrive(FalloutLivingActorModel model, FalloutLivingRagdollSkeletonSnapshot instance,
        IReadOnlyList<FalloutLivingPhysicalBody> bodies, FalloutReferenceModelDataInvocation load)
    {
        _model = model; _instance = instance; _bodies = bodies; _load = load;
        model.RequireCurrent(); instance.Descriptor.Validate();
        if (instance.Load != load || bodies.Count != instance.Descriptor.Bones.Count ||
            !bodies.Select(body => body.Source.Block.Index).SequenceEqual(instance.Descriptor.Bones.Select(bone => bone.Body)) ||
            !bodies.Select(body => body.Backend.PhysicalObjectId).SequenceEqual(instance.NativeBodies))
            throw new InvalidDataException("Ragdoll drive changed its real instance body array or physical parent tree.");
        _parameters = FalloutRagdollDriveSource.Settings.Select((name, index) => new FalloutRagdollDriveParameter(
            name, FalloutRagdollDriveSource.Defaults[index], FalloutRagdollDriveSource.Defaults[index], null, 0)).ToArray();
    }

    internal void Initialize(Func<string, FalloutIniValue> readSetting, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireIdle(); requireActual(_load);
        if (_returned || _failure is not null || _previous.Count != 0) throw new InvalidOperationException("Ragdoll drive initialization replayed an entered source constructor.");
        var fault = _reentry; _entered = true;
        try
        {
            foreach (var body in _bodies)
            {
                Check(); var state = body.Capture().Dynamics;
                _previous.Add(new(body.Source.Block.Index, body.Backend.PhysicalObjectId, state.CenterOfMass,
                    state.Transform.Rotation, state.LinearVelocity, state.AngularVelocity)); Check();
            }
            for (var index = 0; index < _parameters.Length; ++index)
            {
                Check(); var before = _parameters[index]; var current = readSetting(before.Setting);
                if (current.Declaration.Collection != FalloutIniCollection.Main || current.Declaration.Name != before.Setting ||
                    current.Declaration.Kind != 'f' || current.Number is not { } number || !double.IsFinite(number))
                    throw new InvalidDataException("Ragdoll drive selected a missing, foreign-collection or nonfloat current source setting.");
                var value = (float)number;
                if (!float.IsFinite(value)) throw new InvalidDataException("Ragdoll drive source setting exceeds Float32 range.");
                _parameters[index] = before with { Value = BitConverter.SingleToUInt32Bits(value), Origin = current.Origin, Stores = 1 }; Check();
            }
            _returned = true;
        }
        catch (Exception error) { _failure ??= error.Message; throw; }
        finally { _entered = false; }
        void Check()
        {
            _model.RequireCurrent(); requireActual(_load);
            if (_reentry != fault || _retired) throw new InvalidOperationException("Ragdoll drive swallowed source reentry or retired during initialization.");
        }
    }
    internal FalloutLivingRagdollDriveSnapshot Capture()
    {
        RequireIdle();
        return new(_load, _identity, _instance.Instance, _instance.Descriptor.Bones.Select(bone => bone.Parent).ToArray(),
            _parameters.ToArray(), _previous.ToArray(), null, _returned, _failure, _retired);
    }
    internal void Retire() { RequireIdle(); _retired = true; }
    private void RequireIdle()
    {
        if (_entered) { ++_reentry; throw new InvalidOperationException("Ragdoll drive cannot reenter its actual source constructor."); }
        _model.RequireCurrent();
    }
}
