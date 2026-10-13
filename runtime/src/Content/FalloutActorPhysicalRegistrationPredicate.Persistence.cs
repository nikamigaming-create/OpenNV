namespace OpenNV.Runtime.Content;

internal static partial class FalloutActorPhysicalRegistrationPredicate
{
    // Cold receipts retain historical model/process IDs. Validation neither
    // calls the predicate again nor promotes those IDs to current factories.
    internal static void Validate(FalloutActorPhysicalRegistrationTrace trace)
    {
        if (trace is null || trace.Source is null || trace.Actor.ObjectId == 0 || string.IsNullOrWhiteSpace(trace.Actor.OwnerPlugin) ||
            string.IsNullOrWhiteSpace(trace.Owner) || string.IsNullOrWhiteSpace(trace.Step) ||
            trace.Modes is null || trace.Modes.Count > 2 || trace.Modes.Any(mode => mode is null) ||
            trace.Activities is null || trace.Activities.Count > 5 ||
            trace.Activities.Any(query => query is null || query.Process is null || query.Process.Actor is null) ||
            trace.Failure is not null && string.IsNullOrWhiteSpace(trace.Failure) ||
            trace.Returned != (trace.Register is not null && trace.Motion is not null && trace.Failure is null) ||
            !trace.Returned && (trace.Register is not null || trace.Motion is not null || trace.Failure is null))
            throw new InvalidDataException("Actor physical predicate lost its actual source arguments, return or failed prefix.");
        trace.Source.Validate();
        if (trace.Model is { } model && (model.Process == Guid.Empty || model.Factory == Guid.Empty || model.NativeRoot == 0) ||
            !trace.ModelReturned && trace.Model is not null)
            throw new InvalidDataException("Actor physical predicate changed its historical reference-model getter.");
        if (trace.Modes.Count == 0)
        { Failed("caller-mode"); NoModelOrProcess(); return; }
        if (trace.Modes[0].Value is null || trace.Modes[0].Failure is not null || string.IsNullOrWhiteSpace(trace.Modes[0].Owner))
        { Failed("caller-mode"); NoModelOrProcess(); return; }
        if (trace.Modes[0].Require() == 2)
        { Complete(true, "caller-mode-two-return"); NoModelOrProcess(); return; }
        if (!trace.ModelReturned)
        { Failed("reference-model"); NoProcess(); SingleMode(); return; }
        if (trace.Model is null)
        { Complete(false, "null-reference-model-return"); NoProcess(); SingleMode(); return; }
        if ((trace.Arguments & ModeMask) != 0)
        {
            if (trace.Modes.Count == 1)
            { Failed("predicate-mode"); NoProcess(); return; }
            var mode = trace.Modes[1];
            if (mode.Value is null || mode.Failure is not null || string.IsNullOrWhiteSpace(mode.Owner))
            { Failed("predicate-mode"); NoProcess(); return; }
            if (mode.Require() is 1 or 2 or 6)
            { Complete(true, "predicate-mode-return"); NoProcess(); return; }
            if (mode.Require() == 4)
            {
                Failed("active-effects"); NoProcess();
                if (trace.Failure != EffectBoundary)
                    throw new InvalidDataException("Actor mode-four physical predicate changed its reached effect-selection boundary.");
                return;
            }
        }
        else SingleMode();
        if (trace.ProcessPresent is null)
        { Failed("current-process"); NoProcess(); return; }
        if (trace.ProcessPresent == false)
        { Complete(false, "null-process-return"); NoActivities(); return; }
        var expected = new[] { 1, 3, 2, 4, 5 };
        for (var index = 0; index < trace.Activities.Count; ++index)
        {
            var query = trace.Activities[index];
            if (query.Value is < sbyte.MinValue or > sbyte.MaxValue || query.Process.Actor.Reference != trace.Actor ||
                query.Process.ExecutableSha256 != trace.Source.Main.EngineSha256 || query.Process.Process == Guid.Empty ||
                query.Process.GameplayOwnership == Guid.Empty || query.Process.Epoch < 1)
                throw new InvalidDataException("Actor physical predicate retained a foreign or nonsigned process activity read.");
            if (query.Value != expected[index]) continue;
            Complete(true, "process-activity-return");
            if (index != trace.Activities.Count - 1)
                throw new InvalidDataException("Actor physical predicate continued reading after its actual qualifying activity.");
            return;
        }
        if (trace.Activities.Count == expected.Length) Complete(false, "ordinary-model-motion-return");
        else Failed("process-activity");

        void NoModelOrProcess()
        {
            SingleMode(allowEmpty: true); NoProcess();
            if (trace.ModelReturned || trace.Model is not null)
                throw new InvalidDataException("Actor physical predicate read a model after the caller's short circuit or failure.");
        }
        void SingleMode(bool allowEmpty = false)
        {
            if (trace.Modes.Count != 1 && !(allowEmpty && trace.Modes.Count == 0))
                throw new InvalidDataException("Actor physical predicate invented or replayed a raw-mode getter.");
        }
        void NoProcess()
        {
            NoActivities();
            if (trace.ProcessPresent is not null)
                throw new InvalidDataException("Actor physical predicate queried its process before a source branch permitted it.");
        }
        void NoActivities()
        {
            if (trace.Activities.Count != 0)
                throw new InvalidDataException("Actor physical predicate retained activity reads outside their actual branch.");
        }
        void Failed(string step)
        {
            if (trace.Returned || trace.Failure is null || trace.Step != step)
                throw new InvalidDataException("Actor physical predicate lost its exact failed source prefix.");
        }
        void Complete(bool register, string step)
        {
            if (!trace.Returned && trace.Step == step && trace.Failure is not null) return;
            if (!trace.Returned || trace.Step != step || trace.Register != register || trace.Motion != (register ? 1 : 6))
                throw new InvalidDataException("Actor physical predicate manufactured a return or selected different model motion.");
        }
    }
}
