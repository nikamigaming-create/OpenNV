using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutPackageRuntimeRegistry
{
    internal FalloutPackageRegistrySnapshot Capture()
    {
        RequireCurrent();
        return new(Schema, Source, _stack, Factory, _sequence, Array.AsReadOnly(_packages.Values.Select(package =>
            new FalloutPackageRuntimeSnapshot(package.Form, package.RecordSha256, package.Kind, package.SourceFlags, package.Procedure, package.Flags,
                package.LifetimeCount, package.ProcedureAvailable, package.FlagsAvailable, package.CountAvailable, Array.AsReadOnly(package.History.ToArray()),
                package.Target.Capture())).ToArray()),
            Array.AsReadOnly(_publications.ToArray()), _countAuthorityAvailable, Array.AsReadOnly(_countAuthorityObservations.ToArray()));
    }
    private void Restore(FalloutPackageRegistrySnapshot saved)
    {
        Validate(saved, _executable, _stack);
        if (saved.Factory == Factory || saved.Packages.Count != _packages.Count) throw new InvalidDataException("Cold package registry changed its winning object set or reused its old factory.");
        foreach (var state in saved.Packages)
        {
            var package = Require(state.Form);
            if (package.RecordSha256 != state.RecordSha256 || package.Kind != state.Kind || package.SourceFlags != state.SourceFlags)
                throw new InvalidDataException("Cold mutable package changed its selected original record/kind.");
            package.Procedure = state.Procedure; package.Flags = state.Flags; package.RestoreCount(state.LifetimeCount);
            package.ProcedureAvailable = state.ProcedureAvailable; package.FlagsAvailable = state.FlagsAvailable;
            package.CountAvailable = state.CountAvailable;
            package.Target.Restore(state.Target ?? throw new InvalidDataException("Cold PACK omitted its actual target field owner."));
            RetainUnavailable(package);
            package.History.AddRange(state.History);
        }
        _sequence = saved.Sequence; RestoredFactory = saved.Factory;
        _countAuthorityAvailable = saved.CountAuthorityAvailable; _countAuthorityObservations.AddRange(saved.CountAuthorityObservations);
        _publications.AddRange(saved.Publications); _publications.Add(new(Factory, saved.Factory));
    }
    internal static void Validate(FalloutPackageRegistrySnapshot saved, string executable, string stack)
    {
        if (saved is null || saved.Schema != Schema || saved.Source != SourceFor(executable) || saved.Stack != stack || saved.Factory == Guid.Empty ||
            saved.Sequence < 0 || saved.Packages is null || saved.CountAuthorityObservations is null ||
            saved.CountAuthorityAvailable != (saved.CountAuthorityObservations.Count == 0) ||
            saved.Publications is not { Count: > 0 } publications || publications[^1].Factory != saved.Factory)
            throw new InvalidDataException("Package registry omitted its selected source, mutable objects or current factory lineage.");
        var factories = new HashSet<Guid>();
        for (var index = 0; index < publications.Count; ++index)
            if (publications[index] is not { } item || item.Factory == Guid.Empty || !factories.Add(item.Factory) ||
                (index == 0 ? item.PreviousFactory is not null : item.PreviousFactory != publications[index - 1].Factory))
                throw new InvalidDataException("Package registry lost its exact fresh cold factory history.");
        var forms = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance); var sequences = new HashSet<long>();
        long globalSequence = 0;
        foreach (var observation in saved.CountAuthorityObservations)
        {
            if (observation is null || observation.Sequence <= globalSequence || observation.Sequence > saved.Sequence ||
                !sequences.Add(observation.Sequence) || string.IsNullOrWhiteSpace(observation.Producer) || observation.Invocation is not { } invocation ||
                invocation.Actor is null || invocation.SourceCommand == Guid.Empty || invocation.Process == Guid.Empty || invocation.Epoch < 1 || invocation.ThreadId < 1)
                throw new InvalidDataException("Package global count barrier lost its actual entered writer history.");
            invocation.Actor.Validate(); globalSequence = observation.Sequence;
        }
        var firstGlobalBarrier = saved.CountAuthorityObservations.Count == 0 ? long.MaxValue : saved.CountAuthorityObservations[0].Sequence;
        foreach (var package in saved.Packages)
        {
            if (package is null || !forms.Add(package.Form) || package.Form.ObjectId == 0 || string.IsNullOrWhiteSpace(package.Form.OwnerPlugin) ||
                package.RecordSha256 is not { Length: 64 } || !package.RecordSha256.All(Uri.IsHexDigit) || package.History is null)
                throw new InvalidDataException("Package registry has a foreign, duplicate or incomplete winning object.");
            int count = 0, procedure = -1; var flags = package.SourceFlags; long previous = 0;
            var procedureAvailable = true; var flagsAvailable = true; var countAvailable = true; var targetAvailable = true;
            var target = package.Target ?? throw new InvalidDataException("Mutable PACK omitted its target constructor/load/writer owner.");
            FalloutPackageRuntimeTargetFields.Validate(target);
            if (target.Package != package.Form || target.RecordSha256 != package.RecordSha256 || target.Publications.Count != publications.Count)
                throw new InvalidDataException("Mutable PACK target borrowed another winning object.");
            foreach (var mutation in package.History)
            {
                if (mutation is null || mutation.Package != package.Form || mutation.Sequence <= previous || mutation.Sequence > saved.Sequence ||
                    !sequences.Add(mutation.Sequence) || !Enum.IsDefined(mutation.Kind) || string.IsNullOrWhiteSpace(mutation.Producer) ||
                    mutation.Invocation is null || mutation.Invocation.SourceCommand == Guid.Empty || mutation.Invocation.Process == Guid.Empty || mutation.Invocation.Epoch < 1 ||
                    mutation.Invocation.ThreadId < 1 || mutation.Invocation.Actor is null || mutation.BeforeCount != count || mutation.BeforeProcedure != procedure ||
                    mutation.BeforeFlags != flags)
                    throw new InvalidDataException("Mutable package lost an ordered original writer/count prefix.");
                mutation.Invocation.Actor.Validate();
                switch (mutation.Kind)
                {
                    case FalloutPackageRuntimeMutationKind.Retain:
                    case FalloutPackageRuntimeMutationKind.Release:
                        if (!countAvailable || firstGlobalBarrier < mutation.Sequence)
                            throw new InvalidDataException("Package lifetime operation consumed an unavailable current count.");
                        count = unchecked(count + (mutation.Kind == FalloutPackageRuntimeMutationKind.Retain ? 1 : -1)); break;
                    case FalloutPackageRuntimeMutationKind.Procedure:
                        if (!procedureAvailable) throw new InvalidDataException("Unavailable package procedure was repaired without its missing writer.");
                        procedure = mutation.AfterProcedure; break;
                    case FalloutPackageRuntimeMutationKind.ScriptAssignedFlag:
                        if (!flagsAvailable) throw new InvalidDataException("Unavailable package flags were repaired without their missing writer.");
                        flags |= ScriptAssignedFlag; break;
                    case FalloutPackageRuntimeMutationKind.UnownedProcedure: procedureAvailable = false; break;
                    case FalloutPackageRuntimeMutationKind.UnownedFlags: flagsAvailable = false; break;
                    case FalloutPackageRuntimeMutationKind.UnownedLifetime: countAvailable = false; break;
                    case FalloutPackageRuntimeMutationKind.UnownedTarget: targetAvailable = false; break;
                }
                if (mutation.AfterCount != count || mutation.AfterProcedure != procedure || mutation.AfterFlags != flags)
                    throw new InvalidDataException("Package mutation changed a field outside its actual admitted writer.");
                previous = mutation.Sequence;
            }
            if (package.LifetimeCount != count || package.Procedure != procedure || package.Flags != flags ||
                package.ProcedureAvailable != procedureAvailable || package.FlagsAvailable != flagsAvailable || package.CountAvailable != countAvailable ||
                target.Available != targetAvailable)
                throw new InvalidDataException("Package mutable fields differ from their exact retained writer history.");
        }
        if (sequences.Count != saved.Sequence) throw new InvalidDataException("Package registry omitted an entered mutation sequence.");
    }
}
