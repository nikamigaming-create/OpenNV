using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutActorPackageAssignmentSource? _sourceActorPackageAssignment;
    private FalloutReferenceFollowerSource? _sourceReferenceFollower;
    private FalloutSourceDataHandlerLoadingState? _sourceDataHandlerLoading;
    private readonly Guid _sourceDataHandlerConstructor = Guid.NewGuid();
    private FalloutDataHandlerLoadingInvocation? _enteredDataHandlerLoading;
    private FalloutActorPackageAssignmentCall? _currentActorPackageAssignment;
    private FalloutActorMoverResetJoin? _enteredActorMoverJoin;

    internal void PrepareStandaloneActorPackageAssignmentOwners()
    {
        if (!HasStandalonePlayerScene) return;
        RequireStandaloneControlPublication();
        if (_sourceActorPackageAssignment is not null || _sourceDataHandlerLoading is not null)
            throw new InvalidOperationException("Actor package assignment replaced its source construction.");
        var fields = _sourcePackageFields[_enginePlayer];
        var reference = _sourcePlayerReferencePosition ?? throw new NotSupportedException("source-Actor-package-reference-owner-unbound");
        if (reference.LastPrefix is not { Returned: true } || _sourceReferenceBaseAssignment is not { Returned: true })
            throw new NotSupportedException("source-Actor-package-preceding-reference-prefix-unreturned");
        var owned = records.OwnedSource ?? throw new NotSupportedException("source-Actor-package-owned-image-unbound");
        var source = FalloutExecutableStringTable.ReadActorPackageAssignment(owned.FalloutExecutablePath, fields.Source);
        _sourceActorPackageAssignment = source;
        _sourceReferenceFollower = FalloutExecutableStringTable.ReadReferenceFollowerExtra(
            owned.FalloutExecutablePath, reference.Source);
        ProcessCommon.BindActorPackageAssignment(source, fields.Actor.Reference, fields.Epoch, process =>
        {
            RequireStandaloneControlPublication();
            if (_enteredPackageCommand is not null || _sourceActorPackageAssignment != source ||
                process.Actor != fields.Actor || process.Epoch != fields.Epoch ||
                process.Process != fields.Process || reference.LastPrefix is not { Returned: true })
                throw new InvalidOperationException("Actor assignment fields left the actual initial Common/reference prefix.");
        });
        var savedLoading = _processCommonRestore is { } cold ? cold.ReferenceOwners?.DataHandlerLoading ??
            throw new InvalidDataException("Cold World omitted its actual DataHandler loading history.") : null;
        var loading = new FalloutSourceDataHandlerLoadingState(source, fields.Stack, ActualSourceProcessIdentity,
            _sourceDataHandlerConstructor, RequireActualDataHandlerLoading, savedLoading);
        _sourceDataHandlerLoading = loading;
        if (savedLoading is not null)
            loading.BindCurrentIndexedRecords(records, records.LoadReceipt, RequireRecords);
        else
            loading.PreserveIndexedRecordPrefix(FalloutExecutableStringTable.ReadDataHandlerLoadingPreservation(
                owned.FalloutExecutablePath, source), records, records.LoadReceipt, RequireRecords);

        void RequireRecords()
        {
            RequireStandaloneControlPublication();
            if (!ReferenceEquals(records.OwnedSource, owned) || !ReferenceEquals(_sourceDataHandlerLoading, loading) ||
                loading.RuntimeProcess != ActualSourceProcessIdentity || _enteredPackageCommand is not null)
                throw new InvalidOperationException("DataHandler prefix lost its actual selected records/World binding.");
            records.RequireLiveLoadReceipt(records.LoadReceipt);
        }
    }

    private FalloutDataHandlerLoadingRead ReadActualDataHandlerLoading(FalloutPackageCommandFieldInvocation command)
    {
        RequireActualSourcePackageCommand(command);
        records.RequireLiveLoadReceipt(records.LoadReceipt);
        if (_enteredDataHandlerLoading is not null)
            throw new InvalidOperationException("DataHandler loading reentered its actual package cleanup.");
        var loading = _sourceDataHandlerLoading ?? throw new NotSupportedException("source-DataHandler-loading-field-owner-unbound");
        var invocation = new FalloutDataHandlerLoadingInvocation(Guid.NewGuid(), ActualSourceProcessIdentity,
            command.Process, command.Actor, command.Epoch, Environment.CurrentManagedThreadId,
            FalloutDataHandlerLoadingIngress.PackageCleanup, command.SourceCommand);
        _enteredDataHandlerLoading = invocation;
        try { return loading.Read(invocation); }
        finally { _enteredDataHandlerLoading = null; }
    }

    private void RequireActualDataHandlerLoading(FalloutDataHandlerLoadingInvocation invocation)
    {
        var command = _enteredPackageCommand ?? throw new InvalidOperationException("DataHandler loading has no entered package caller.");
        RequireActualSourcePackageCommand(command);
        if (_enteredDataHandlerLoading != invocation || invocation.ThreadId != Environment.CurrentManagedThreadId ||
            invocation.RuntimeProcess != ActualSourceProcessIdentity || invocation.CommonProcess != command.Process ||
            invocation.Actor != command.Actor || invocation.Epoch != command.Epoch ||
            invocation.PackageCommand != command.SourceCommand || invocation.Ingress != FalloutDataHandlerLoadingIngress.PackageCleanup)
            throw new InvalidDataException("DataHandler read changed its independent actual World/field/Common caller.");
    }
}
