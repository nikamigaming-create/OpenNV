using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal static partial class FalloutProcessPackageState
{
    internal static FalloutPackageStateMutation Mutate(FalloutPackageCommandFieldInvocation invocation, FalloutProcessSceneQuery process,
        FalloutProcessPackageFields owner, FalloutPackageStateOperation operation, FalloutPackageRuntimeRegistry registry,
        FalloutPackageRuntimeObject? requested, FalloutPackageExtraReference? extra, IFalloutPackageStateServices services,
        Func<long> next, Action<FalloutProcessPackageWords, FalloutPackageStateMutation> publish)
    {
        var stores = new List<FalloutPackageStateStore>(10);
        var entered = next(); var fields = owner.Fields;
        var call = new FalloutPackageStateMutation(invocation, process, owner.Publication.Factory, owner.Slot, operation, requested?.Form,
            extra, entered, entered, FalloutPackageStateStep.Entered, null, fields, fields, stores.AsReadOnly(), null, false, null, null, null);
        try
        {
            services.RequireCurrent(invocation);
            Step(FalloutPackageStateStep.Equality);
            var old = fields.Package is { } oldForm ? registry.Require(oldForm) : null;
            if (requested is not null) registry.RequireObject(requested);
            if (operation == FalloutPackageStateOperation.Set && ReferenceEquals(old, requested))
            { call = call with { Equal = true }; return Return(); }
            if (old is not null)
            {
                Step(FalloutPackageStateStep.SpecialPlayerPackage);
                var special = services.ReadPlayerSpecialPackage(invocation);
                if (special is not null) registry.RequireObject(special);
                if (ReferenceEquals(special, old)) services.ClearPlayerSpecialPackage(invocation, old);
                Step(FalloutPackageStateStep.Release);
                var count = registry.Release(invocation, old); call = call with { ReleasedCount = count };
                if (!old.FlagsAvailable) throw new NotSupportedException("source-package-runtime-flags-current-writer-unowned");
                if (count == 0 && (old.Flags & FalloutPackageRuntimeRegistry.TemporaryFlag) != 0)
                {
                    Store(FalloutPackageStateStep.TemporaryPackagePointerClear, fields with { Package = null });
                    Step(FalloutPackageStateStep.TemporaryDeletion); services.DeleteTemporaryPackage(invocation, old);
                    throw new NotSupportedException("source-package-temporary-form-deletion-result-lifetime-unowned");
                }
                Store(FalloutPackageStateStep.PackagePointerClear, fields with { Package = null });
            }
            if (fields.Context is { } context)
            {
                Step(FalloutPackageStateStep.ContextDestruction); services.DestroyContext(invocation, context);
                throw new NotSupportedException("source-package-context-destruction-result-lifetime-unowned");
            }
            Store(FalloutPackageStateStep.TimeReset, fields with { TimeBits = FalloutPlayerFollowingContinuationSource.PackageUnsetTimeBits });
            Store(FalloutPackageStateStep.IndexReset, fields with { Index = -1 });
            Store(FalloutPackageStateStep.FlagsReset, fields with { Flags = 0 });
            Store(FalloutPackageStateStep.ExtraReset, fields with { Extra = null });
            if (operation == FalloutPackageStateOperation.Clear || requested is null) return Return();
            Step(FalloutPackageStateStep.Retain); call = call with { RetainedCount = registry.Retain(invocation, requested) };
            Step(FalloutPackageStateStep.GameHour); var hour = services.ReadGameHour(invocation);
            if (hour is null || string.IsNullOrWhiteSpace(hour.Producer) || hour.Global is null && hour.Bits != FalloutPackageRuntimeRegistry.NullGameHourBits)
                throw new InvalidDataException("Package setter lost its actual retained GameHour read/null branch.");
            call = call with { Hour = hour };
            Store(FalloutPackageStateStep.TimeStore, fields with { TimeBits = hour.Bits });
            Store(FalloutPackageStateStep.PackageStore, fields with { Package = requested.Form });
            Step(FalloutPackageStateStep.ContextFactory);
            if (AllocatesContext(requested.Kind))
            {
                _ = services.ConstructContext(invocation, requested);
                throw new NotSupportedException("source-package-context-construction-result-lifetime-unowned");
            }
            Store(FalloutPackageStateStep.ContextStore, fields with { Context = null });
            Store(FalloutPackageStateStep.IndexStore, fields with { Index = 0 });
            if (extra is not null) services.RequireExtra(invocation, extra);
            Store(FalloutPackageStateStep.ExtraStore, fields with { Extra = extra });
            return Return();
        }
        catch (Exception error)
        {
            call = call with { Step = FalloutPackageStateStep.Failed, FailedAt = call.Step, Changed = next(), After = fields, Failure = error.Message };
            publish(fields, call); throw;
        }
        void Step(FalloutPackageStateStep step)
        { call = call with { Step = step, Changed = next(), After = fields }; publish(fields, call); }
        void Store(FalloutPackageStateStep step, FalloutProcessPackageWords after)
        {
            var changed = next(); stores.Add(new(step, fields, after, changed)); fields = after;
            call = call with { Step = step, Changed = changed, After = fields }; publish(fields, call);
        }
        FalloutPackageStateMutation Return()
        {
            services.RequireCurrent(invocation);
            call = call with { Step = FalloutPackageStateStep.Returned, Changed = next(), After = fields };
            publish(fields, call); return FreezeMutation(call);
        }
    }
    internal static bool AllocatesContext(sbyte kind) => kind is 2 or 12 or 13 or 14 or 16;
    internal static FalloutPackageStateMutation FreezeMutation(FalloutPackageStateMutation call) =>
        call with { Stores = Array.AsReadOnly(call.Stores.ToArray()) };
}
