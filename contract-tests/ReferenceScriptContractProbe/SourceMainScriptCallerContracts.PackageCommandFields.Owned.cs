using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static object AuditOwnedPackageCommandFields(FalloutPluginStack records,
        FalloutActorProcessCommonState common, FalloutPlayerMovementControlSource controls)
    {
        var owned = records.OwnedSource ?? throw new InvalidDataException("Package field audit requires its actual owned source.");
        var source = FalloutExecutableStringTable.ReadPackageCommandFields(owned.FalloutExecutablePath, controls);
        var assignmentSource = FalloutExecutableStringTable.ReadPackageAssignment(owned.FalloutExecutablePath, source);
        var pureProcedures = records.EffectiveRecords("PACK").Select(record =>
        {
            var kind = unchecked((sbyte)FalloutPackageData.Read(record).Procedure);
            return new { record.FormKey, kind, procedure = FalloutPackageAssignmentSource.PureProcedure(kind) };
        }).Where(value => value.procedure is not null).ToArray();
        var current = common.ReadSceneQueries(records.RuntimeFormKey(0x14), 1);
        FalloutPackageCommandFieldInvocation? entered = null;
        var scene = new FalloutStandalonePlayerSceneState(FalloutStandalonePlayerSceneSource.Read(controls.Main), owned.StackId, Guid.NewGuid());
        var fields = new FalloutActorPackageCommandFields(source, owned.StackId, current.Actor, current.Process, current.Epoch,
            scene.Capture().Constructor, RequireOwnedScope);
        try
        {
            var snapshot = fields.Capture();
            Require(snapshot.Values == FalloutPackageCommandFieldValues.Constructed && snapshot.Availability.All(value => !value.Available) &&
                snapshot.Operations.Count == 0 && current.AnimationContext is null,
                "Owned package-field audit guessed preceding-writer closure or borrowed a native model/pending destination.");
            var invocation = new FalloutPackageCommandFieldInvocation(Guid.NewGuid(), current.Actor, current.Process, current.Epoch,
                Environment.CurrentManagedThreadId);
            entered = invocation;
            var initializedAnimation = fields.InitializeConstructedPlayerAnimation(invocation, FalloutInstallationSettings.Read(owned));
            var animation = fields.ReadAnimationContext(invocation,
                () => common.ReadSceneQueries(current.Actor.Reference, current.Epoch),
                () => scene.ReadPackageCommandAnimationView(invocation, RequireOwnedScope));
            var independent = fields.Capture();
            Require(initializedAnimation.Returned && animation is { Returned: true, Context: null, ViewRead.View: 0 } &&
                animation.ViewRead.Process == scene.Process && animation.ViewRead.Process != current.Process &&
                independent.Availability.Single(value => value.Available).Lane == FalloutPackageCommandFieldLane.Animation,
                "Owned animation initialization lost the actual settings/current scene view or admitted sibling fields.");
            return new { source = source.Identity, assignmentSource = assignmentSource.Identity, constructor = snapshot, borrowedCommon = current,
                initializedAnimation, animation, animationInitializationScope = "AUTHORED_WITH_OWNED_DATA",
                pureProcedureRecords = pureProcedures.Length, pureProcedureKinds = pureProcedures.Select(value => value.kind).Distinct().Order().ToArray(),
                precedingWriters = "UNPROVEN", originalCommand = "UNEXECUTED", wholePlayer = "UNEXECUTED" };
        }
        finally { entered = null; fields.Retire(); scene.Retire(); }

        void RequireOwnedScope(FalloutPackageCommandFieldInvocation invocation)
        {
            var actual = common.ReadSceneQueries(current.Actor.Reference, current.Epoch);
            if (invocation != entered || invocation.Actor != actual.Actor || invocation.Process != actual.Process ||
                invocation.Epoch != actual.Epoch || invocation.ThreadId != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Owned animation component left its actual selected Common and separate scene lifetime.");
        }
    }
}
