using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutActorProcessBodyObservation(FalloutFormKey Actor,
    FalloutActorProcessFact<bool> Source3D, FalloutActorProcessBodyBinding? Body, string Owner);

internal sealed partial class FalloutActorProcessCommonState
{
    // Optional only at construction: without this genuine producer, a null
    // nullable callback remains explicitly unowned. It is never
    // promoted to the original legal known-null 3D branch.
    private Func<FalloutFormKey, FalloutActorProcessBodyObservation>? _source3D;

    internal void BindSource3DInitializer(Func<FalloutFormKey, FalloutActorProcessBodyObservation> observe)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(observe);
        if (_source3D is not null) throw new InvalidOperationException("Actual source 3D initializer is already bound.");
        _source3D = observe;
    }

    private FalloutActorProcessBodyObservation ObserveInitializationBody(FalloutFormKey actor)
    {
        FalloutActorProcessBodyObservation value;
        if (_source3D is not null) value = Callback(() => _source3D(actor));
        else
        {
            var actual = Callback(() => _body(actor));
            value = new(actor, actual is null ? new(null, "actual-source-3D-initializer-producer-unbound") :
                new(true, actual.Owner), actual, "actual-common-body-callback");
        }
        RequireBodyObservation(value, actor);
        _ = value.Source3D.Require();
        return value;
    }

    internal static void RequireBodyObservation(FalloutActorProcessBodyObservation value, FalloutFormKey actor)
    {
        if (value is null || value.Actor != actor || value.Source3D is null || string.IsNullOrWhiteSpace(value.Owner) ||
            string.IsNullOrWhiteSpace(value.Source3D.Owner) ||
            value.Source3D.Value == false && value.Body is not null ||
            value.Source3D.Value == true && value.Source3D.Failure is null && value.Body is null ||
            value.Source3D.Value is null && value.Body is not null)
            throw new InvalidDataException("High initialization lost its explicit source 3D fact and matching real body.");
        if (value.Body is { } body) RequireBody(body, actor);
    }

    private void InitializeCurrentSourceBody(FalloutFormKey actor)
    {
        var current = Require(actor); var observation = ObserveInitializationBody(actor);
        // The original initializer returns before all skeleton/BPTD/LOD
        // lookup work if its actual source 3D getter returns null.
        _current[actor] = current with
        {
            Body = observation.Body,
            Source3D = observation.Source3D,
            Phase = FalloutProcessCommonPhase.Initialized,
            Changed = Next()
        };
    }

    private void ObserveExistingCurrentSourceBody(FalloutFormKey actor, FalloutProcessCommonEntry current)
    {
        var observation = ObserveInitializationBody(actor);
        if (current.Body is { } before && observation.Body is { } after && !BodyEquivalent(before, after))
            throw new NotSupportedException("Source High body replacement has no admitted body-part/LOD rebinding transaction.");
        _current[actor] = current with
        {
            Body = observation.Body,
            Source3D = observation.Source3D,
            Phase = FalloutProcessCommonPhase.Initialized,
            Changed = Next()
        };
    }

    private static void ValidateSourceBodyEntry(FalloutProcessCommonEntry entry)
    {
        if (entry.Source3D is { } fact)
        {
            RequireBodyObservation(new(entry.Source.Reference, fact, entry.Body, fact.Owner), entry.Source.Reference);
            _ = fact.Require();
            if (entry.Level != FalloutDetectionProcessLevel.High || entry.Phase != FalloutProcessCommonPhase.Initialized)
                throw new InvalidDataException("Common source 3D receipt is outside the actual initialized High process.");
        }
        else if (entry.Phase == FalloutProcessCommonPhase.Initialized || entry.Body is not null)
            throw new InvalidDataException("Initialized High common state omitted its actually consumed source 3D getter.");
    }
}
