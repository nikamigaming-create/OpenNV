using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutSourcePackageAssignmentStep
{
    SpecialPackage, SpecialCallback, Process, PreviousPackage, PreviousEvent, PreviousCallback,
    ProcessFlag, StartEvent, StartCallback, Procedure, SavedPosition, PackageFlag,
    ActorAssignment, TailProcess, Tier, Clock, TierCallbacks, PlayerIdentity, FourthStore, Returned, Failed
}
internal sealed record FalloutSourcePackageAssignmentCall(FalloutPackageCommandFieldsSource Source,
    string Stack, FalloutPackageCommandFieldInvocation Invocation, FalloutFormKey? CommandSource,
    FalloutFormKey Package, string Producer, FalloutSourcePackageAssignmentStep Step,
    FalloutSourcePackageAssignmentStep? FailedAt, string? Failure,
    IReadOnlyList<FalloutSourcePackageAssignmentStep> ReturnedChildren,
    FalloutProcessPackageRead? SpecialPackage, bool? SpecialSelected,
    FalloutProcessSceneQuery? Process, FalloutProcessPackageRead? PreviousPackage,
    int? Procedure, int? Tier, bool? IsPlayer, uint? ClockBits, bool? Result,
    FalloutProcessSceneQuery? TailProcess = null,
    FalloutProcessPackageRead? FirstPrimary = null,
    FalloutPackageAssignmentSource? AssignmentSource = null,
    bool ConsumerEntered = false);
internal sealed record FalloutSourceSelectedPackageRead(FalloutProcessPackageRead? FirstPrimary,
    FalloutProcessPackageRead? Selected);

internal interface IFalloutSourcePackageAssignmentConsumers
{
    FalloutPackageCommandFieldsSource Source { get; }
    FalloutPackageAssignmentSource AssignmentSource { get; }
    string Stack { get; }
    void RequireCurrent(FalloutPackageCommandFieldInvocation invocation);
    FalloutSourceSelectedPackageRead ReadSelectedPackage(FalloutPackageCommandFieldInvocation invocation,
        Action<FalloutSourceSelectedPackageRead> observe);
    FalloutProcessPackageRead? ReadCurrentPackage(FalloutPackageCommandFieldInvocation invocation);
    sbyte ReadPackageKind(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package);
    void SpecialPackageCallback(FalloutPackageCommandFieldInvocation invocation);
    FalloutProcessSceneQuery? ReadProcess(FalloutPackageCommandFieldInvocation invocation);
    void MarkEvent(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package, uint mask);
    void PackageChanged(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package);
    void ClearProcessFlag(FalloutPackageCommandFieldInvocation invocation);
    void PackageStarted(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package);
    int EnsureProcedure(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package);
    void SavePosition(FalloutPackageCommandFieldInvocation invocation);
    void SetPackageFlag(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package, uint mask);
    void AssignActorPackage(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package,
        bool primary, bool alternate);
    int ReadProcessTier(FalloutPackageCommandFieldInvocation invocation);
    uint StoreProcessClock(FalloutPackageCommandFieldInvocation invocation);
    void UpdateTierOne(FalloutPackageCommandFieldInvocation invocation);
    bool ReadActualPlayerIdentity(FalloutPackageCommandFieldInvocation invocation);
    void StorePlayerFourth(FalloutPackageCommandFieldInvocation invocation);
}

// The compiled command calls this owner synchronously. Each entered child is
// recorded before it runs; later failure preserves earlier committed stores.
// Native driver attachment borrows a returned call, never executes it again.
internal static partial class FalloutSourcePackageAssignment
{
    internal const uint PackageChangedEvent = 0x800;
    internal const uint PackageStartedEvent = 0x200;
    internal const uint ScriptPackageRuntimeFlag = 0x4000;
    internal const sbyte SpecialPackageKind = 28;
    internal static bool Execute(FalloutPackageCommandFieldInvocation invocation,
        FalloutFormKey? commandSource, FalloutFormKey package, string producer,
        IFalloutSourcePackageAssignmentConsumers consumers,
        Action<FalloutSourcePackageAssignmentCall> observe)
    {
        ArgumentNullException.ThrowIfNull(consumers); ArgumentNullException.ThrowIfNull(observe);
        ArgumentException.ThrowIfNullOrWhiteSpace(producer); consumers.Source.Validate();
        consumers.AssignmentSource.Validate();
        if (consumers.AssignmentSource.Fields != consumers.Source)
            throw new InvalidDataException("Package command changed its selected field and ordered assignment sources.");
        if (invocation is null || invocation.SourceCommand == Guid.Empty || invocation.Process == Guid.Empty ||
            invocation.Epoch < 1 || invocation.ThreadId != Environment.CurrentManagedThreadId ||
            package.ObjectId == 0 || string.IsNullOrWhiteSpace(package.OwnerPlugin))
            throw new InvalidDataException("Package assignment has no actual source command/Actor/thread/form ownership.");
        invocation.Actor.Validate();
        var returned = new List<FalloutSourcePackageAssignmentStep>();
        var call = new FalloutSourcePackageAssignmentCall(consumers.Source, consumers.Stack, invocation,
            commandSource, package, producer, FalloutSourcePackageAssignmentStep.SpecialPackage,
            null, null, [], null, null, null, null, null, null, null, null, null,
            AssignmentSource: consumers.AssignmentSource);
        try
        {
            Child(FalloutSourcePackageAssignmentStep.SpecialPackage, () =>
            {
                var selectedRead = consumers.ReadSelectedPackage(invocation, partial =>
                {
                    call = call with { FirstPrimary = partial.FirstPrimary, SpecialPackage = partial.Selected }; observe(call);
                });
                var read = selectedRead.Selected;
                var selected = read?.Fields.Fields.Package is { } current &&
                    consumers.ReadPackageKind(invocation, current) == SpecialPackageKind;
                call = call with { FirstPrimary = selectedRead.FirstPrimary, SpecialPackage = read, SpecialSelected = selected };
            });
            if (call.SpecialSelected == true)
                Child(FalloutSourcePackageAssignmentStep.SpecialCallback, () => consumers.SpecialPackageCallback(invocation));
            Child(FalloutSourcePackageAssignmentStep.Process, () => call = call with { Process = consumers.ReadProcess(invocation) });
            if (call.Process is null) return Return();
            {
                Child(FalloutSourcePackageAssignmentStep.PreviousPackage,
                    () => call = call with { PreviousPackage = consumers.ReadCurrentPackage(invocation) });
                if (call.PreviousPackage?.Fields.Fields.Package is { } old)
                {
                    Child(FalloutSourcePackageAssignmentStep.PreviousEvent, () => consumers.MarkEvent(invocation, old, PackageChangedEvent));
                    Child(FalloutSourcePackageAssignmentStep.PreviousCallback, () => consumers.PackageChanged(invocation, old));
                }
                Child(FalloutSourcePackageAssignmentStep.ProcessFlag, () => consumers.ClearProcessFlag(invocation));
                Child(FalloutSourcePackageAssignmentStep.StartEvent, () => consumers.MarkEvent(invocation, package, PackageStartedEvent));
                Child(FalloutSourcePackageAssignmentStep.StartCallback, () => consumers.PackageStarted(invocation, package));
                Child(FalloutSourcePackageAssignmentStep.Procedure, () => call = call with { Procedure = consumers.EnsureProcedure(invocation, package) });
                Child(FalloutSourcePackageAssignmentStep.SavedPosition, () => consumers.SavePosition(invocation));
                Child(FalloutSourcePackageAssignmentStep.PackageFlag, () => consumers.SetPackageFlag(invocation, package, ScriptPackageRuntimeFlag));
                Child(FalloutSourcePackageAssignmentStep.ActorAssignment, () => consumers.AssignActorPackage(invocation, package, false, false));
                Child(FalloutSourcePackageAssignmentStep.TailProcess, () => call = call with { TailProcess = consumers.ReadProcess(invocation) });
                if (call.TailProcess is not null)
                {
                    Child(FalloutSourcePackageAssignmentStep.Tier, () => call = call with { Tier = consumers.ReadProcessTier(invocation) });
                    if (call.Tier != 0)
                    {
                        Child(FalloutSourcePackageAssignmentStep.Clock, () => call = call with { ClockBits = consumers.StoreProcessClock(invocation) });
                        if (call.Tier == 1)
                            Child(FalloutSourcePackageAssignmentStep.TierCallbacks, () => consumers.UpdateTierOne(invocation));
                        return Return();
                    }
                }
            }
            // Only the post-assignment absent-process arm joins this tail.
            Child(FalloutSourcePackageAssignmentStep.PlayerIdentity,
                () => call = call with { IsPlayer = consumers.ReadActualPlayerIdentity(invocation) });
            if (call.IsPlayer == true)
                Child(FalloutSourcePackageAssignmentStep.FourthStore, () => consumers.StorePlayerFourth(invocation));
            return Return();
        }
        catch (Exception error)
        {
            call = call with
            {
                FailedAt = call.Step,
                Step = FalloutSourcePackageAssignmentStep.Failed,
                Failure = error.Message,
                ReturnedChildren = Array.AsReadOnly(returned.ToArray())
            };
            observe(call); throw;
        }
        void Child(FalloutSourcePackageAssignmentStep step, Action operation)
        {
            call = call with { Step = step, ConsumerEntered = false, ReturnedChildren = Array.AsReadOnly(returned.ToArray()) }; observe(call);
            consumers.RequireCurrent(invocation);
            call = call with { ConsumerEntered = true }; observe(call);
            operation(); consumers.RequireCurrent(invocation);
            returned.Add(step);
            call = call with { ReturnedChildren = Array.AsReadOnly(returned.ToArray()) }; observe(call);
        }
        bool Return()
        {
            call = call with { Step = FalloutSourcePackageAssignmentStep.Returned, ConsumerEntered = false }; observe(call);
            consumers.RequireCurrent(invocation);
            call = call with
            {
                Step = FalloutSourcePackageAssignmentStep.Returned,
                Result = true,
                ReturnedChildren = Array.AsReadOnly(returned.ToArray())
            };
            observe(call); return true;
        }
    }
}
