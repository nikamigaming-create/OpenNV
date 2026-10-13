using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal interface IFalloutActorPhysicalRegistrationConsumers
{
    FalloutActorPhysicalAdmissionSource Source { get; }
    FalloutFormKey Actor { get; }
    string Owner { get; }
    void RequireCurrent();
    FalloutActorProcessFact<int> ReadMode();
    FalloutSourceSceneNode? ReadReferenceModel();
    bool HasCurrentProcess();
    FalloutProcessPhysicalActivityQuery ReadCurrentProcessActivity();
}

internal sealed record FalloutActorPhysicalRegistrationTrace(FalloutActorPhysicalAdmissionSource Source,
    FalloutFormKey Actor, uint Arguments, string Owner, string Step,
    IReadOnlyList<FalloutActorProcessFact<int>> Modes, bool ModelReturned, FalloutSourceSceneNode? Model,
    bool? ProcessPresent, IReadOnlyList<FalloutProcessPhysicalActivityQuery> Activities,
    bool? Register, byte? Motion, bool Returned, string? Failure);

// The predicate chooses registration and source model motion. It does not
// register a utility group, publish a body or create a physical controller.
internal static partial class FalloutActorPhysicalRegistrationPredicate
{
    private const uint ModeMask = 0x400;
    internal const string EffectBoundary = "source-Actor-physical-registration-mode-four-active-effect-selection-unbound";
    internal static FalloutActorPhysicalRegistrationTrace Execute(uint arguments,
        IFalloutActorPhysicalRegistrationConsumers consumers, Action<FalloutActorPhysicalRegistrationTrace> observe)
    {
        ArgumentNullException.ThrowIfNull(consumers); ArgumentNullException.ThrowIfNull(observe);
        consumers.Source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(consumers.Owner);
        var source = consumers.Source; var actor = consumers.Actor;
        var trace = new FalloutActorPhysicalRegistrationTrace(source, actor, arguments, consumers.Owner,
            "caller-mode", [], false, null, null, [], null, null, false, null);
        var modes = new List<FalloutActorProcessFact<int>>(2);
        var activities = new List<FalloutProcessPhysicalActivityQuery>(5);
        try
        {
            // Caller mode 2 skips the predicate, including its model getter.
            if (ReadMode() == 2) return Return(true, "caller-mode-two-return");
            Step("reference-model"); var model = consumers.ReadReferenceModel(); Require();
            if (model is not null && (model.Process == Guid.Empty || model.Factory == Guid.Empty || model.NativeRoot == 0))
                throw new InvalidDataException("Actor physical predicate lost its actual selected reference-model lifetime.");
            trace = trace with { ModelReturned = true, Model = model }; observe(trace);
            if (model is null) return Return(false, "null-reference-model-return");
            if ((arguments & ModeMask) != 0)
            {
                Step("predicate-mode"); var mode = ReadMode();
                if (mode is 1 or 2 or 6) return Return(true, "predicate-mode-return");
                if (mode == 4) { Step("active-effects"); throw new NotSupportedException(EffectBoundary); }
            }
            Step("current-process"); var present = consumers.HasCurrentProcess(); Require();
            trace = trace with { ProcessPresent = present }; observe(trace);
            if (!present) return Return(false, "null-process-return");
            foreach (var expected in new[] { 1, 3, 2, 4, 5 })
            {
                Step("process-activity"); var query = consumers.ReadCurrentProcessActivity(); Require();
                if (query is null || query.Process is null || query.Process.Actor is null ||
                    query.Value is < sbyte.MinValue or > sbyte.MaxValue ||
                    query.Process.Actor.Reference != actor || query.Process.ExecutableSha256 != source.Main.EngineSha256 ||
                    query.Process.Process == Guid.Empty || query.Process.GameplayOwnership == Guid.Empty || query.Process.Epoch < 1)
                    throw new InvalidDataException("Actor physical predicate consumed a foreign or retired process activity query.");
                activities.Add(query); trace = trace with { Activities = activities.ToArray() }; observe(trace);
                if (query.Value == expected) return Return(true, "process-activity-return");
            }
            return Return(false, "ordinary-model-motion-return");
        }
        catch (Exception error)
        {
            trace = trace with { Failure = error.Message, Returned = false, Register = null, Motion = null }; observe(trace); throw;
        }
        int ReadMode()
        {
            var mode = consumers.ReadMode(); Require();
            modes.Add(mode); trace = trace with { Modes = modes.ToArray() }; observe(trace); return mode.Require();
        }
        void Require()
        {
            consumers.RequireCurrent();
            if (consumers.Source != source || consumers.Actor != actor)
                throw new InvalidDataException("Actor physical predicate changed its actual source/current Actor during a child.");
        }
        void Step(string step) { Require(); trace = trace with { Step = step }; observe(trace); }
        FalloutActorPhysicalRegistrationTrace Return(bool register, string step)
        {
            Require(); trace = trace with { Step = step, Register = register, Motion = register ? (byte)1 : (byte)6, Returned = true };
            observe(trace); return trace;
        }
    }
}
